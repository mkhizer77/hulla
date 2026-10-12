import { createHash } from "node:crypto";

import * as fc from "fast-check";
import { describe, expect, it } from "vitest";

import { sha256Hex } from "../sha256";
import { utf8Encode } from "../utf8";

describe("sha256", () => {
  it("matches the NIST known answers", () => {
    expect(sha256Hex(new Uint8Array(0))).toBe(
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    );
    expect(sha256Hex(utf8Encode("abc"))).toBe(
      "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
    );
    expect(sha256Hex(utf8Encode("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"))).toBe(
      "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1",
    );
    expect(sha256Hex(utf8Encode("a".repeat(1_000_000)))).toBe(
      "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0",
    );
  });

  it("handles every padding boundary", () => {
    for (const length of [55, 56, 57, 63, 64, 65, 119, 120, 128]) {
      const bytes = Uint8Array.from({ length }, (_, i) => i & 0xff);
      expect(sha256Hex(bytes)).toBe(createHash("sha256").update(bytes).digest("hex"));
    }
  });

  it("agrees with Node's implementation on random input", () => {
    fc.assert(
      fc.property(fc.uint8Array({ maxLength: 512 }), (bytes) => {
        expect(sha256Hex(bytes)).toBe(createHash("sha256").update(bytes).digest("hex"));
      }),
    );
  });
});
