import * as fc from "fast-check";
import { describe, expect, it } from "vitest";

import { bytesToHex, hexToBytes } from "../hex";
import { utf8Encode } from "../utf8";

describe("utf8Encode", () => {
  it("agrees with TextEncoder on every string, lone surrogates included", () => {
    const oracle = new TextEncoder();
    fc.assert(
      fc.property(fc.string({ unit: "binary" }), (text) => {
        expect(utf8Encode(text)).toEqual(oracle.encode(text));
      }),
    );
    fc.assert(
      fc.property(fc.string({ unit: "grapheme" }), (text) => {
        expect(utf8Encode(text)).toEqual(oracle.encode(text));
      }),
    );
  });

  it("replaces a lone surrogate with U+FFFD like TextEncoder", () => {
    expect(utf8Encode("a\ud800b")).toEqual(new TextEncoder().encode("a\ud800b"));
    expect(utf8Encode("\udc00")).toEqual(Uint8Array.from([0xef, 0xbf, 0xbd]));
  });

  it("encodes Nastaliq, Devanagari and an emoji", () => {
    expect(utf8Encode("ہلّا")).toEqual(new TextEncoder().encode("ہلّا"));
    expect(utf8Encode("हल्ला")).toEqual(new TextEncoder().encode("हल्ला"));
    expect(utf8Encode("😀")).toEqual(Uint8Array.from([0xf0, 0x9f, 0x98, 0x80]));
  });
});

describe("hex", () => {
  it("round-trips bytes", () => {
    fc.assert(
      fc.property(fc.uint8Array({ maxLength: 64 }), (bytes) => {
        expect(hexToBytes(bytesToHex(bytes))).toEqual(bytes);
      }),
    );
  });

  it("rejects odd lengths and non-hex characters", () => {
    expect(() => hexToBytes("abc")).toThrow(TypeError);
    expect(() => hexToBytes("zz")).toThrow(TypeError);
    expect(hexToBytes("")).toEqual(new Uint8Array(0));
  });
});
