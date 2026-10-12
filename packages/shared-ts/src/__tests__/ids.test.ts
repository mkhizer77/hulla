import * as fc from "fast-check";
import { describe, expect, it } from "vitest";

import {
  ROOM_CODE_ALPHABET,
  ROOM_CODE_LENGTH,
  generateRoomCode,
  isRoomCode,
  normaliseRoomCode,
  roundId,
  tableId,
} from "../ids";
import { Xoshiro128StarStar } from "../prng";

describe("room codes", () => {
  it("the alphabet has 32 symbols and none of 0, O, 1, I", () => {
    expect(ROOM_CODE_ALPHABET).toHaveLength(32);
    expect(new Set(ROOM_CODE_ALPHABET).size).toBe(32);
    for (const banned of "0O1I") {
      expect(ROOM_CODE_ALPHABET).not.toContain(banned);
    }
  });

  it("generates codes of the right length from the alphabet, deterministically", () => {
    const a = generateRoomCode(Xoshiro128StarStar.fromState([1, 2, 3, 4]));
    const b = generateRoomCode(Xoshiro128StarStar.fromState([1, 2, 3, 4]));
    expect(a).toBe(b);
    expect(a).toHaveLength(ROOM_CODE_LENGTH);
    expect(isRoomCode(a)).toBe(true);
  });

  it("normalises case and separators and rejects ambiguous characters", () => {
    expect(normaliseRoomCode(" 7kq-2mx ")).toBe("7KQ2MX");
    expect(normaliseRoomCode("7KQ2M")).toBeUndefined();
    expect(normaliseRoomCode("7KQ2MO")).toBeUndefined();
    expect(normaliseRoomCode("7KQ2M1")).toBeUndefined();
  });

  it("every generated code normalises to itself", () => {
    fc.assert(
      fc.property(fc.tuple(fc.nat(), fc.nat(), fc.nat(), fc.nat()), ([a, b, c, d]) => {
        const rng = Xoshiro128StarStar.fromState([a, b, c, d || 1]);
        const code = generateRoomCode(rng);
        expect(normaliseRoomCode(code.toLowerCase())).toBe(code);
      }),
    );
  });

  it("ids follow the protocol prefixes", () => {
    expect(tableId("7KQ2MX")).toBe("t_7KQ2MX");
    expect(roundId(3)).toBe("r_3");
  });
});
