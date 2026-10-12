import { createHash } from "node:crypto";

import * as fc from "fast-check";
import { describe, expect, it } from "vitest";

import { canonicalJson, stateHash, type JsonValue } from "../canonical-json";

const jsonValue = fc.letrec<{ json: JsonValue }>((tie) => ({
  json: fc.oneof(
    { depthSize: "small" },
    fc.integer(),
    fc.string(),
    fc.boolean(),
    fc.constant(null),
    fc.array(tie("json"), { maxLength: 4 }),
    fc.dictionary(fc.string(), tie("json"), { maxKeys: 4 }),
  ),
})).json;

describe("canonicalJson", () => {
  it("sorts keys, drops whitespace and omits undefined members", () => {
    expect(canonicalJson({ b: 1, a: [true, null, "x"], c: undefined })).toBe(
      '{"a":[true,null,"x"],"b":1}',
    );
    expect(canonicalJson({})).toBe("{}");
    expect(canonicalJson([])).toBe("[]");
  });

  it("orders keys by UTF-16 code units, not by locale", () => {
    expect(canonicalJson({ é: 1, z: 2 })).toBe('{"z":2,"é":1}');
    expect(canonicalJson({ "￿": 1, "😀": 2 })).toBe('{"😀":2,"￿":1}');
  });

  it("escapes strings exactly as JSON.stringify", () => {
    const text = 'quote " backslash \\ newline \n tab \t control \u0001 unicode ہلّا';
    expect(canonicalJson(text)).toBe(JSON.stringify(text));
  });

  it("writes -0 as 0 and rejects anything that is not a safe integer", () => {
    expect(canonicalJson(-0)).toBe("0");
    expect(canonicalJson(Number.MAX_SAFE_INTEGER)).toBe("9007199254740991");
    expect(() => canonicalJson(1.5)).toThrow(TypeError);
    expect(() => canonicalJson(Number.NaN)).toThrow(TypeError);
    expect(() => canonicalJson(Number.POSITIVE_INFINITY)).toThrow(TypeError);
    expect(() => canonicalJson(2 ** 53)).toThrow(TypeError);
  });

  it("rejects undefined inside arrays", () => {
    expect(() => canonicalJson([1, undefined as unknown as JsonValue])).toThrow(TypeError);
  });

  it("round-trips through JSON.parse and is idempotent", () => {
    fc.assert(
      fc.property(jsonValue, (value) => {
        const text = canonicalJson(value);
        const parsed: JsonValue = JSON.parse(text) as JsonValue;
        expect(parsed).toEqual(value);
        expect(canonicalJson(parsed)).toBe(text);
      }),
    );
  });

  it("does not depend on insertion order", () => {
    fc.assert(
      fc.property(fc.dictionary(fc.string(), fc.integer(), { maxKeys: 8 }), (object) => {
        const reversed = Object.fromEntries(Object.entries(object).reverse());
        expect(canonicalJson(reversed)).toBe(canonicalJson(object));
      }),
    );
  });
});

describe("stateHash", () => {
  it("is SHA-256 over the UTF-8 canonical text", () => {
    const state = { seq: 7, hands: [["AS", "2H"], ["KD"]], leader: 1, phase: "play" };
    const expected = createHash("sha256").update(canonicalJson(state), "utf8").digest("hex");
    expect(stateHash(state)).toBe(expected);
    expect(stateHash({ phase: "play", leader: 1, hands: [["AS", "2H"], ["KD"]], seq: 7 })).toBe(
      expected,
    );
  });
});
