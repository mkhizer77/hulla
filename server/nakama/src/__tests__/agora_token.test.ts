import { createRequire } from "node:module";
import { inflateSync } from "node:zlib";

import { describe, expect, it } from "vitest";

import { buildRtcToken, isAgoraId } from "../services/agora_token";
import { adler32, base64, hmacSha256, utf8Encode, zlibStored } from "../services/bytes";

interface ReferenceToken {
  salt: number;
  add_service(service: unknown): void;
  build(): string;
  from_string(token: string): boolean;
  verifySignature(appCertificate: string): boolean;
}

interface ReferenceModule {
  AccessToken2: new (
    appId: string,
    cert: string,
    issueTs: number,
    expire: number,
  ) => ReferenceToken;
  ServiceRtc: new (
    channel: string,
    uid: number | string,
  ) => { add_privilege(p: number, expire: number): void };
}

// Agora's own builder is the oracle: same inputs must give the same signed content.
const reference = createRequire(import.meta.url)(
  "agora-token/src/AccessToken2.js",
) as ReferenceModule;

const APP_ID = "970CA35de60c44645bbae8a215061b33";
const CERT = "5CFd2fd1755d40ecb72977518be15d3b";

function decoded(token: string): Buffer {
  expect(token.startsWith("007")).toBe(true);
  return inflateSync(Buffer.from(token.slice(3), "base64"));
}

describe("the Agora token builder", () => {
  it("packs and signs exactly like Agora's reference builder, for a speaker and a listener", () => {
    for (const publishAudio of [true, false]) {
      const input = {
        channel: "t_ABCDEF",
        account: "",
        issueTs: 1_760_200_000,
        expireSeconds: 3_600,
        salt: 123_456,
        publishAudio,
      };
      const mine = buildRtcToken({ appId: APP_ID, appCertificate: CERT }, input);
      const theirs = new reference.AccessToken2(APP_ID, CERT, input.issueTs, input.expireSeconds);
      theirs.salt = input.salt;
      const service = new reference.ServiceRtc(input.channel, 0);
      service.add_privilege(1, input.expireSeconds);
      if (publishAudio) service.add_privilege(2, input.expireSeconds);
      theirs.add_service(service);
      expect(decoded(mine).equals(decoded(theirs.build()))).toBe(true);
      const parsed = new reference.AccessToken2("", "", 0, 0);
      expect(parsed.from_string(mine)).toBe(true);
      expect(parsed.verifySignature(CERT)).toBe(true);
      expect(parsed.verifySignature(APP_ID)).toBe(false);
    }
  });

  it("writes a zlib stream Node inflates, with the right checksum, for small and large inputs", () => {
    for (const size of [0, 1, 1_000, 70_000]) {
      const data = new Uint8Array(size).map((_, i) => (i * 7) % 251);
      const inflated = inflateSync(Buffer.from(zlibStored(data)));
      expect(Buffer.from(data).equals(inflated)).toBe(true);
    }
    expect(adler32(utf8Encode("Wikipedia"))).toBe(0x11e60398);
  });

  it("matches the Node implementations of HMAC-SHA256 and base64", async () => {
    const { createHmac } = await import("node:crypto");
    const key = utf8Encode("key");
    const message = utf8Encode("The quick brown fox jumps over the lazy dog");
    expect(Buffer.from(hmacSha256(key, message)).toString("hex")).toBe(
      createHmac("sha256", Buffer.from(key)).update(Buffer.from(message)).digest("hex"),
    );
    const longKey = new Uint8Array(100).fill(7);
    expect(Buffer.from(hmacSha256(longKey, message)).toString("hex")).toBe(
      createHmac("sha256", Buffer.from(longKey)).update(Buffer.from(message)).digest("hex"),
    );
    for (const text of ["", "a", "ab", "abc", "hulla!"]) {
      expect(base64(utf8Encode(text))).toBe(Buffer.from(text).toString("base64"));
    }
    expect(isAgoraId(APP_ID)).toBe(true);
    expect(isAgoraId("nope")).toBe(false);
  });
});
