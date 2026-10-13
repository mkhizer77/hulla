import { createHash } from "node:crypto";

import * as fc from "fast-check";
import { describe, expect, it } from "vitest";

import { sha1Hex } from "../sha1";
import { utf8Encode } from "../utf8";

describe("sha1", () => {
  it("matches the known answers", () => {
    expect(sha1Hex(new Uint8Array(0))).toBe("da39a3ee5e6b4b0d3255bfef95601890afd80709");
    expect(sha1Hex(utf8Encode("abc"))).toBe("a9993e364706816aba3e25717850c26c9cd0d89d");
    expect(sha1Hex(utf8Encode("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"))).toBe(
      "84983e441c3bd26ebaae4aa1f95129e5e54670f1",
    );
    expect(sha1Hex(utf8Encode("a".repeat(1_000_000)))).toBe(
      "34aa973cd4c4daa4f61eeb2bdbad27316534016f",
    );
  });

  it("handles every padding boundary", () => {
    for (const length of [55, 56, 57, 63, 64, 65, 119, 120, 128]) {
      const bytes = Uint8Array.from({ length }, (_, i) => i & 0xff);
      expect(sha1Hex(bytes)).toBe(createHash("sha1").update(bytes).digest("hex"));
    }
  });

  it("agrees with Node's implementation on random input", () => {
    fc.assert(
      fc.property(fc.uint8Array({ maxLength: 512 }), (bytes) => {
        expect(sha1Hex(bytes)).toBe(createHash("sha1").update(bytes).digest("hex"));
      }),
    );
  });
});
