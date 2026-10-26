import { createHmac } from "node:crypto";

import { describe, expect, it } from "vitest";

import { base64, base64Url, hmacSha256, utf8Encode } from "../services/bytes";
import { buildRoomToken, liveKitConfigFrom } from "../services/livekit_token";

const config = {
  url: "wss://voice.example.test",
  apiUrl: "https://voice.example.test",
  apiKey: "APIhulla",
  apiSecret: "a-local-secret-of-at-least-32-characters",
};

function decode(part: string): unknown {
  return JSON.parse(Buffer.from(part, "base64url").toString("utf8"));
}

describe("the LiveKit token builder", () => {
  it("signs an HS256 JWT that Node's HMAC verifies", () => {
    const token = buildRoomToken(config, {
      room: "t_ABCDEF",
      identity: "u1",
      issueTs: 1_800_000_000,
      expireSeconds: 3_600,
      publishAudio: true,
    });
    const [header, claims, signature] = token.split(".");
    const expected = createHmac("sha256", config.apiSecret)
      .update(`${header}.${claims}`)
      .digest("base64url");
    expect(signature).toBe(expected);
    expect(decode(header ?? "")).toEqual({ alg: "HS256", typ: "JWT" });
    expect(decode(claims ?? "")).toEqual({
      iss: "APIhulla",
      sub: "u1",
      nbf: 1_800_000_000,
      exp: 1_800_003_600,
      video: {
        room: "t_ABCDEF",
        roomJoin: true,
        canSubscribe: true,
        canPublish: true,
        canPublishSources: ["microphone"],
        canPublishData: false,
        canUpdateOwnMetadata: false,
      },
    });
  });

  it("lets a spectator listen without publishing anything", () => {
    const token = buildRoomToken(config, {
      room: "t_ABCDEF",
      identity: "u2",
      issueTs: 1_800_000_000,
      expireSeconds: 60,
      publishAudio: false,
    });
    expect(decode(token.split(".")[1] ?? "")).toMatchObject({
      video: { canPublish: false, canPublishSources: [], canSubscribe: true },
    });
  });

  it("reads its settings from the runtime env and stays off when any is missing or weak", () => {
    const env = {
      LIVEKIT_URL: config.url,
      LIVEKIT_API_KEY: config.apiKey,
      LIVEKIT_API_SECRET: config.apiSecret,
    };
    expect(liveKitConfigFrom(env)).toEqual(config);
    expect(liveKitConfigFrom({ ...env, LIVEKIT_URL: "ws://127.0.0.1:7880/" })).toMatchObject({
      apiUrl: "http://127.0.0.1:7880",
    });
    expect(liveKitConfigFrom({ ...env, LIVEKIT_API_URL: "http://livekit:7880/" })?.apiUrl).toBe(
      "http://livekit:7880",
    );
    expect(liveKitConfigFrom({})).toBeNull();
    expect(liveKitConfigFrom({ ...env, LIVEKIT_URL: "https://voice.example.test" })).toBeNull();
    expect(liveKitConfigFrom({ ...env, LIVEKIT_API_KEY: "" })).toBeNull();
    expect(liveKitConfigFrom({ ...env, LIVEKIT_API_SECRET: "short" })).toBeNull();
  });

  it("matches Node's HMAC-SHA256 and base64 for short, long and empty inputs", () => {
    for (const [key, message] of [
      ["k", "m"],
      ["k".repeat(100), "a longer message ".repeat(20)],
      ["", ""],
    ] as const) {
      const ours = hmacSha256(utf8Encode(key), utf8Encode(message));
      const node = createHmac("sha256", key).update(message).digest();
      expect(Buffer.from(ours).equals(node)).toBe(true);
      expect(base64(ours)).toBe(node.toString("base64"));
      expect(base64Url(ours)).toBe(node.toString("base64url"));
    }
  });
});
