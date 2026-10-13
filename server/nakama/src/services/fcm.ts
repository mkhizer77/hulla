import { log } from "../infra/log";
import { METRICS, metrics } from "../infra/metrics";

import {
  PUSH_OAUTH_COLLECTION,
  SYSTEM_USER_ID,
  fcmMessage,
  type PushMessage,
  type PushSender,
  type SendOutcome,
} from "./push";

/**
 * FCM HTTP v1 from the runtime (TECH_STACK 3, ARCHITECTURE 8.1): a service-account JWT signed with
 * `nk.jwtGenerate`, exchanged for an access token that is cached in storage, then one
 * `nk.httpRequest` per device. Nakama's own notifications stay in-app; one FCM project delivers to
 * Android and, through the APNs key uploaded to Firebase, to iOS.
 */
export const FCM_PROJECT_ENV = "HULLA_FCM_PROJECT_ID";
export const FCM_CLIENT_EMAIL_ENV = "HULLA_FCM_CLIENT_EMAIL";
export const FCM_PRIVATE_KEY_ENV = "HULLA_FCM_PRIVATE_KEY";
export const TOKEN_URL = "https://oauth2.googleapis.com/token";
export const FCM_SCOPE = "https://www.googleapis.com/auth/firebase.messaging";
export const TOKEN_TTL_S = 3600;
export const TOKEN_MARGIN_MS = 60_000;
export const HTTP_TIMEOUT_MS = 5_000;

export interface FcmConfig {
  readonly projectId: string;
  readonly clientEmail: string;
  readonly privateKey: string;
}

/** The three variables, or null when any is empty; `\n` in the key becomes a newline. */
export function fcmConfigFrom(env: Readonly<Record<string, string | undefined>>): FcmConfig | null {
  const projectId = (env[FCM_PROJECT_ENV] ?? "").trim();
  const clientEmail = (env[FCM_CLIENT_EMAIL_ENV] ?? "").trim();
  const privateKey = (env[FCM_PRIVATE_KEY_ENV] ?? "").split("\\n").join("\n").trim();
  if (projectId === "" || clientEmail === "" || privateKey === "") return null;
  return { projectId, clientEmail, privateKey };
}

export function sendUrl(projectId: string): string {
  return `https://fcm.googleapis.com/v1/projects/${encodeURIComponent(projectId)}/messages:send`;
}

export interface HttpResponse {
  readonly code: number;
  readonly body: string;
}

export interface HttpPorts {
  jwtGenerate(
    algorithm: "RS256",
    key: string,
    claims: Record<string, string | number | boolean>,
  ): string;
  httpRequest(
    url: string,
    method: "post",
    headers: Record<string, string>,
    body: string,
    timeoutMs: number,
  ): HttpResponse;
}

export interface AccessToken {
  readonly token: string;
  readonly expiresTs: number;
}

export interface TokenCache {
  read(): AccessToken | null;
  write(token: AccessToken | null): void;
}

export function assertionClaims(
  config: FcmConfig,
  now: number,
): Record<string, string | number | boolean> {
  const iat = Math.floor(now / 1000);
  return { iss: config.clientEmail, scope: FCM_SCOPE, aud: TOKEN_URL, iat, exp: iat + TOKEN_TTL_S };
}

/** One JWT-bearer exchange; null when Google refuses. */
export function fetchAccessToken(
  http: HttpPorts,
  config: FcmConfig,
  now: number,
): AccessToken | null {
  const assertion = http.jwtGenerate("RS256", config.privateKey, assertionClaims(config, now));
  const body = `grant_type=${encodeURIComponent("urn:ietf:params:oauth:grant-type:jwt-bearer")}&assertion=${encodeURIComponent(assertion)}`;
  const response = http.httpRequest(
    TOKEN_URL,
    "post",
    { "Content-Type": "application/x-www-form-urlencoded" },
    body,
    HTTP_TIMEOUT_MS,
  );
  if (response.code !== 200) return null;
  try {
    const parsed = JSON.parse(response.body) as { access_token?: unknown; expires_in?: unknown };
    if (typeof parsed.access_token !== "string" || parsed.access_token === "") return null;
    const ttl = typeof parsed.expires_in === "number" ? parsed.expires_in : TOKEN_TTL_S;
    return { token: parsed.access_token, expiresTs: now + ttl * 1000 - TOKEN_MARGIN_MS };
  } catch {
    return null;
  }
}

export function accessToken(
  http: HttpPorts,
  cache: TokenCache,
  config: FcmConfig,
  now: number,
): string | null {
  const cached = cache.read();
  if (cached !== null && cached.expiresTs > now) return cached.token;
  const fresh = fetchAccessToken(http, config, now);
  if (fresh !== null) cache.write(fresh);
  return fresh === null ? null : fresh.token;
}

/** 404 UNREGISTERED and a 400 naming the token mean the token is dead; anything else is a retry. */
export function classifyResponse(code: number, body: string): SendOutcome {
  if (code === 200) return "sent";
  if (code === 404 && body.includes("UNREGISTERED")) return "invalid_token";
  if (code === 400 && body.includes("registration token")) return "invalid_token";
  return "failed";
}

/** Sends one message; a 401 refreshes the access token and retries once. */
export function sendFcm(
  http: HttpPorts,
  cache: TokenCache,
  config: FcmConfig,
  token: string,
  message: PushMessage,
  now: number,
): SendOutcome {
  const payload = JSON.stringify({ message: fcmMessage(token, message) });
  for (let attempt = 0; attempt < 2; attempt += 1) {
    const access = accessToken(http, cache, config, now);
    if (access === null) return "failed";
    const response = http.httpRequest(
      sendUrl(config.projectId),
      "post",
      { Authorization: `Bearer ${access}`, "Content-Type": "application/json" },
      payload,
      HTTP_TIMEOUT_MS,
    );
    if (response.code === 401 && attempt === 0) {
      cache.write(null);
      continue;
    }
    return classifyResponse(response.code, response.body);
  }
  return "failed";
}

export function nkTokenCache(nk: nkruntime.Nakama): TokenCache {
  return {
    read() {
      try {
        const objects = nk.storageRead([
          { collection: PUSH_OAUTH_COLLECTION, key: "google", userId: SYSTEM_USER_ID },
        ]);
        const value = objects[0]?.value as { token?: unknown; expiresTs?: unknown } | undefined;
        return typeof value?.token === "string" && typeof value.expiresTs === "number"
          ? { token: value.token, expiresTs: value.expiresTs }
          : null;
      } catch {
        return null;
      }
    },
    write(token) {
      nk.storageWrite([
        {
          collection: PUSH_OAUTH_COLLECTION,
          key: "google",
          userId: SYSTEM_USER_ID,
          value: token === null ? {} : { token: token.token, expiresTs: token.expiresTs },
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
  };
}

export const UNCONFIGURED_SENDER: PushSender = { configured: false, send: () => "unconfigured" };

function nkHttp(nk: nkruntime.Nakama): HttpPorts {
  return {
    jwtGenerate: (algorithm, key, claims) => nk.jwtGenerate(algorithm, key, claims),
    httpRequest(url, method, headers, body, timeoutMs) {
      const response = nk.httpRequest(url, method, headers, body, timeoutMs);
      return { code: response.code, body: response.body };
    },
  };
}

/** The sender for a box: a no-op until the three FCM variables are set. */
export function nkFcmSender(
  nk: nkruntime.Nakama,
  logger: nkruntime.Logger,
  env: Readonly<Record<string, string | undefined>>,
): PushSender {
  const config = fcmConfigFrom(env);
  if (config === null) return UNCONFIGURED_SENDER;
  const http = nkHttp(nk);
  const cache = nkTokenCache(nk);
  return {
    configured: true,
    send(token, message) {
      let outcome: SendOutcome;
      try {
        outcome = sendFcm(http, cache, config, token, message, Date.now());
      } catch (error: unknown) {
        log.warn(logger, "push request failed", { type: message.type, error: String(error) });
        outcome = "failed";
      }
      if (outcome === "sent") metrics.count(nk, METRICS.pushSent, { type: message.type });
      else metrics.count(nk, METRICS.pushFailed, { type: message.type, outcome });
      return outcome;
    },
  };
}
