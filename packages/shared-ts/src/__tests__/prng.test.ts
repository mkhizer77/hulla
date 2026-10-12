import * as fc from "fast-check";
import { describe, expect, it } from "vitest";

import { hexToBytes } from "../hex";
import {
  Xoshiro128StarStar,
  nonZeroState,
  shuffle,
  stateFromSeed,
  type Xoshiro128State,
} from "../prng";

import fixture from "./fixtures/xoshiro128ss.json";

function asState(words: number[]): Xoshiro128State {
  const [a, b, c, d] = words;
  if (a === undefined || b === undefined || c === undefined || d === undefined) {
    throw new Error("fixture state needs four words");
  }
  return [a, b, c, d];
}

describe("Xoshiro128StarStar", () => {
  it("reproduces the published reference outputs for fixed states", () => {
    for (const vector of fixture.states) {
      const rng = Xoshiro128StarStar.fromState(asState(vector.state));
      expect(vector.outputs.map(() => rng.nextUint32())).toEqual(vector.outputs);
    }
  });

  it("derives its state from the first 16 bytes of SHA-256 over the seed, little-endian", () => {
    for (const vector of fixture.seeded) {
      const seed = hexToBytes(vector.seedHex);
      expect([...stateFromSeed(seed)]).toEqual(vector.state);
      const rng = Xoshiro128StarStar.fromSeed(seed);
      expect([...rng.state()]).toEqual(vector.state);
      expect(vector.outputs.map(() => rng.nextUint32())).toEqual(vector.outputs);
    }
  });

  it("replaces an all-zero derived state with [1, 0, 0, 0] and leaves others alone", () => {
    expect(nonZeroState([0, 0, 0, 0])).toEqual([1, 0, 0, 0]);
    expect(nonZeroState([0, 0, 0, 5])).toEqual([0, 0, 0, 5]);
  });

  it("rejects an all-zero or out-of-range state", () => {
    expect(() => Xoshiro128StarStar.fromState([0, 0, 0, 0])).toThrow(RangeError);
    expect(() => Xoshiro128StarStar.fromState([1, 2, 3, 2 ** 32])).toThrow(RangeError);
    expect(() => Xoshiro128StarStar.fromState([1, 2, 3, -1])).toThrow(RangeError);
  });

  it("nextInt matches the pinned rejection-sampling sequence", () => {
    for (const vector of fixture.nextInt) {
      const rng = Xoshiro128StarStar.fromState(asState(vector.state));
      expect(vector.outputs.map(() => rng.nextInt(vector.n))).toEqual(vector.outputs);
    }
  });

  it("nextInt validates its bound", () => {
    const rng = Xoshiro128StarStar.fromState([1, 2, 3, 4]);
    expect(() => rng.nextInt(0)).toThrow(RangeError);
    expect(() => rng.nextInt(1.5)).toThrow(RangeError);
    expect(() => rng.nextInt(2 ** 32 + 1)).toThrow(RangeError);
    expect(rng.nextInt(1)).toBe(0);
  });

  it("nextInt stays in range and is deterministic", () => {
    fc.assert(
      fc.property(
        fc.tuple(fc.nat(), fc.nat(), fc.nat(), fc.nat()),
        fc.integer({ min: 1, max: 1_000_000 }),
        ([a, b, c, d], n) => {
          const state: Xoshiro128State = [a, b, c, d || 1];
          const first = Xoshiro128StarStar.fromState(state);
          const second = Xoshiro128StarStar.fromState(state);
          for (let i = 0; i < 20; i++) {
            const value = first.nextInt(n);
            expect(value).toBeGreaterThanOrEqual(0);
            expect(value).toBeLessThan(n);
            expect(second.nextInt(n)).toBe(value);
          }
        },
      ),
    );
  });
});

describe("shuffle", () => {
  it("matches the pinned Fisher–Yates results", () => {
    for (const vector of fixture.shuffle) {
      const rng = Xoshiro128StarStar.fromState(asState(vector.state));
      const items = Array.from({ length: vector.length }, (_, i) => i);
      expect(shuffle(rng, items)).toEqual(vector.result);
    }
  });

  it("returns a permutation and leaves the input untouched", () => {
    fc.assert(
      fc.property(
        fc.tuple(fc.nat(), fc.nat(), fc.nat(), fc.nat()),
        fc.array(fc.integer(), { maxLength: 60 }),
        ([a, b, c, d], items) => {
          const copy = items.slice();
          const out = shuffle(Xoshiro128StarStar.fromState([a, b, c, d || 1]), items);
          expect(items).toEqual(copy);
          expect([...out].sort((x, y) => x - y)).toEqual([...items].sort((x, y) => x - y));
        },
      ),
    );
  });
});
