import { createHmac } from "node:crypto";

import { describe, expect, it } from "vitest";

import { removeFromRoom, roomAdminToken } from "../services/livekit_admin";

const config = {
  url: "wss://voice.example.test",
  apiUrl: "https://voice.example.test",
  apiKey: "APIhulla",
  apiSecret: "a-local-secret-of-at-least-32-characters",
};

function claimsOf(token: string): unknown {
  return JSON.parse(Buffer.from(token.split(".")[1] ?? "", "base64url").toString("utf8"));
}

describe("LiveKit room admin", () => {
  it("signs a one-minute admin token for one room", () => {
    const token = roomAdminToken(config, "m1", 1_800_000_000);
    const [header, claims, signature] = token.split(".");
    expect(signature).toBe(
      createHmac("sha256", config.apiSecret).update(`${header}.${claims}`).digest("base64url"),
    );
    expect(claimsOf(token)).toEqual({
      iss: "APIhulla",
      nbf: 1_800_000_000,
      exp: 1_800_000_060,
      video: { room: "m1", roomAdmin: true },
    });
  });

  it("removes each player once and treats one not in the room as done", () => {
    const calls: { url: string; headers: Record<string, string>; body: string }[] = [];
    const http = {
      httpRequest(url: string, _method: "post", headers: Record<string, string>, body: string) {
        calls.push({ url, headers, body });
        return { code: body.includes("u2") ? 404 : 200, body: "{}" };
      },
    };
    expect(removeFromRoom(http, config, "m1", ["u1", "u2", "u1"], 1_800_000_000)).toBe(true);
    expect(calls.map((c) => c.body)).toEqual([
      JSON.stringify({ room: "m1", identity: "u1" }),
      JSON.stringify({ room: "m1", identity: "u2" }),
    ]);
    expect(calls[0]?.url).toBe(
      "https://voice.example.test/twirp/livekit.RoomService/RemoveParticipant",
    );
    expect(calls[0]?.headers["Authorization"]).toMatch(/^Bearer /);
  });

  it("reports a refusal so the caller can log it", () => {
    const http = { httpRequest: () => ({ code: 401, body: "" }) };
    expect(removeFromRoom(http, config, "m1", ["u1"], 1_800_000_000)).toBe(false);
  });
});
