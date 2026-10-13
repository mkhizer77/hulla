import { describe, expect, it } from "vitest";

import {
  FCM_CLIENT_EMAIL_ENV,
  FCM_PRIVATE_KEY_ENV,
  FCM_PROJECT_ENV,
  TOKEN_URL,
  accessToken,
  assertionClaims,
  classifyResponse,
  fcmConfigFrom,
  nkFcmSender,
  sendFcm,
  sendUrl,
  type AccessToken,
  type HttpPorts,
  type TokenCache,
} from "../services/fcm";
import { fcmMessage, type PushMessage } from "../services/push";

import { fakeLogger, memoryNk, type LogLine } from "./fake-nk";

const T0 = 1_760_200_000_000;
const CONFIG = { projectId: "hulla-test", clientEmail: "push@hulla-test.iam", privateKey: "PEM" };
const MESSAGE: PushMessage = {
  type: "table_invite",
  args: ["Ali", "7KQ2M"],
  data: { matchId: "m1", code: "7KQ2M", fromUserId: "u1" },
};

interface Call {
  url: string;
  headers: Record<string, string>;
  body: string;
}

function http(
  answer: (url: string, call: number) => { code: number; body: string },
): HttpPorts & { readonly calls: Call[] } {
  const calls: Call[] = [];
  return {
    calls,
    jwtGenerate: (algorithm, _key, claims) => `${algorithm}:${JSON.stringify(claims)}`,
    httpRequest(url, _method, headers, body) {
      calls.push({ url, headers, body });
      return answer(url, calls.length);
    },
  };
}

function cache(): TokenCache & { value: AccessToken | null } {
  const holder: { value: AccessToken | null } = { value: null };
  return {
    get value() {
      return holder.value;
    },
    set value(v) {
      holder.value = v;
    },
    read: () => holder.value,
    write: (v) => {
      holder.value = v;
    },
  };
}

const tokenOk = { code: 200, body: JSON.stringify({ access_token: "at-1", expires_in: 3600 }) };

describe("FCM HTTP v1 from the runtime (GDD 10.10, TECH_STACK 3)", () => {
  it("needs all three variables and unescapes the key's newlines", () => {
    expect(fcmConfigFrom({})).toBeNull();
    expect(fcmConfigFrom({ [FCM_PROJECT_ENV]: "p", [FCM_CLIENT_EMAIL_ENV]: "e" })).toBeNull();
    expect(
      fcmConfigFrom({
        [FCM_PROJECT_ENV]: "p",
        [FCM_CLIENT_EMAIL_ENV]: "e@p.iam",
        [FCM_PRIVATE_KEY_ENV]: "key line one\\nkey line two\\n",
      }),
    ).toEqual({
      projectId: "p",
      clientEmail: "e@p.iam",
      privateKey: "key line one\nkey line two",
    });
    expect(sendUrl("hulla-test")).toBe(
      "https://fcm.googleapis.com/v1/projects/hulla-test/messages:send",
    );
  });

  it("signs the service-account assertion and caches the access token until it expires", () => {
    const claims = assertionClaims(CONFIG, T0);
    expect(claims).toMatchObject({
      iss: CONFIG.clientEmail,
      aud: TOKEN_URL,
      scope: "https://www.googleapis.com/auth/firebase.messaging",
      iat: Math.floor(T0 / 1000),
      exp: Math.floor(T0 / 1000) + 3600,
    });
    const h = http(() => tokenOk);
    const c = cache();
    expect(accessToken(h, c, CONFIG, T0)).toBe("at-1");
    expect(accessToken(h, c, CONFIG, T0 + 1000)).toBe("at-1");
    expect(h.calls).toHaveLength(1);
    expect(h.calls[0]?.body).toContain(
      "grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer",
    );
    expect(accessToken(h, c, CONFIG, T0 + 3600 * 1000)).toBe("at-1");
    expect(h.calls).toHaveLength(2);
    const refused = http(() => ({ code: 403, body: "no" }));
    expect(accessToken(refused, cache(), CONFIG, T0)).toBeNull();
  });

  it("classifies the send: dead tokens are pruned, everything else retries later", () => {
    expect(classifyResponse(200, "{}")).toBe("sent");
    expect(
      classifyResponse(
        404,
        '{"error":{"status":"NOT_FOUND","details":[{"errorCode":"UNREGISTERED"}]}}',
      ),
    ).toBe("invalid_token");
    expect(
      classifyResponse(400, "The registration token is not a valid FCM registration token"),
    ).toBe("invalid_token");
    expect(classifyResponse(400, "Invalid JSON payload")).toBe("failed");
    expect(classifyResponse(429, "quota")).toBe("failed");
    expect(classifyResponse(503, "")).toBe("failed");
  });

  it("sends with the bearer token, retries once after a 401 with a fresh token, and never twice", () => {
    let tokens = 0;
    const h = http((url) => {
      if (url === TOKEN_URL) {
        tokens += 1;
        return { code: 200, body: JSON.stringify({ access_token: `at-${String(tokens)}` }) };
      }
      return tokens === 1 ? { code: 401, body: "expired" } : { code: 200, body: "{}" };
    });
    const c = cache();
    expect(sendFcm(h, c, CONFIG, "tok-1", MESSAGE, T0)).toBe("sent");
    expect(h.calls.map((call) => call.url)).toEqual([
      TOKEN_URL,
      sendUrl(CONFIG.projectId),
      TOKEN_URL,
      sendUrl(CONFIG.projectId),
    ]);
    expect(h.calls[1]?.headers["Authorization"]).toBe("Bearer at-1");
    expect(h.calls[3]?.headers["Authorization"]).toBe("Bearer at-2");
    const sent = JSON.parse(h.calls[3]?.body ?? "{}") as { message: Record<string, unknown> };
    expect(sent.message).toEqual(fcmMessage("tok-1", MESSAGE));
    const always401 = http((url) => (url === TOKEN_URL ? tokenOk : { code: 401, body: "" }));
    expect(sendFcm(always401, cache(), CONFIG, "tok-1", MESSAGE, T0)).toBe("failed");
    expect(always401.calls).toHaveLength(4);
  });

  it("the box's sender is a no-op without the variables and counts outcomes with them", () => {
    const m = memoryNk();
    const off = nkFcmSender(m.nk, fakeLogger(), {});
    expect(off.configured).toBe(false);
    expect(off.send("tok", MESSAGE)).toBe("unconfigured");
    expect(m.requests).toHaveLength(0);
    const env = {
      [FCM_PROJECT_ENV]: "hulla-test",
      [FCM_CLIENT_EMAIL_ENV]: "push@hulla-test.iam",
      [FCM_PRIVATE_KEY_ENV]: "PEM",
    };
    const on = nkFcmSender(m.nk, fakeLogger(), env);
    expect(on.configured).toBe(true);
    expect(on.send("tok", MESSAGE)).toBe("sent");
    expect(m.requests.map((r) => r.url)).toEqual([TOKEN_URL, sendUrl("hulla-test")]);
    expect(m.object("push_oauth", "google", "00000000-0000-0000-0000-000000000000")).toMatchObject({
      token: "at-1",
    });
    m.http = () => {
      throw new Error("connection refused");
    };
    const lines: LogLine[] = [];
    expect(nkFcmSender(m.nk, fakeLogger(lines), env).send("tok", MESSAGE)).toBe("failed");
    expect(lines.some((l) => l.message === "push request failed")).toBe(true);
  });
});
