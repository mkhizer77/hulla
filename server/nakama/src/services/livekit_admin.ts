import { base64Url, hmacSha256, utf8Encode } from "./bytes";

import type { LiveKitConfig } from "./livekit_token";

/**
 * Server-side calls to LiveKit's RoomService (ADR-0024): a connected client keeps its room until it
 * leaves, because LiveKit refreshes its token, so leaving or being kicked from the table also removes
 * the player from the voice room here.
 */
export interface VoiceHttp {
  httpRequest(
    url: string,
    method: "post",
    headers: Record<string, string>,
    body: string,
    timeoutMs: number,
  ): { readonly code: number; readonly body: string };
}

const ADMIN_TOKEN_SECONDS = 60;
const HTTP_TIMEOUT_MS = 3_000;

/** A short admin token for one room; never leaves the server. */
export function roomAdminToken(config: LiveKitConfig, room: string, issueTs: number): string {
  const claims = {
    iss: config.apiKey,
    nbf: issueTs,
    exp: issueTs + ADMIN_TOKEN_SECONDS,
    video: { room, roomAdmin: true },
  };
  const signingInput = `${encodeJson({ alg: "HS256", typ: "JWT" })}.${encodeJson(claims)}`;
  const signature = hmacSha256(utf8Encode(config.apiSecret), utf8Encode(signingInput));
  return `${signingInput}.${base64Url(signature)}`;
}

/**
 * Removes each identity from the room. True when LiveKit took every request; a 404 (not in the room)
 * counts as done. Failures are left to the caller to log; play never waits on voice (E25).
 */
export function removeFromRoom(
  http: VoiceHttp,
  config: LiveKitConfig,
  room: string,
  identities: readonly string[],
  issueTs: number,
): boolean {
  const token = roomAdminToken(config, room, issueTs);
  let ok = true;
  for (const identity of new Set(identities)) {
    const response = http.httpRequest(
      `${config.apiUrl}/twirp/livekit.RoomService/RemoveParticipant`,
      "post",
      { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
      JSON.stringify({ room, identity }),
      HTTP_TIMEOUT_MS,
    );
    if (response.code !== 200 && response.code !== 404) ok = false;
  }
  return ok;
}

function encodeJson(value: unknown): string {
  return base64Url(utf8Encode(JSON.stringify(value)));
}
