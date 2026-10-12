import { sha256 } from "./sha256";

/** The only source of randomness an engine may use (ADR-0009). */
export interface Rng {
  /** A uniformly distributed integer in [0, 2^32). */
  nextUint32(): number;
  /** A uniformly distributed integer in [0, maxExclusive), unbiased by rejection sampling. */
  nextInt(maxExclusive: number): number;
}

export type Xoshiro128State = readonly [number, number, number, number];

const TWO_POW_32 = 0x100000000;

function rotl(x: number, k: number): number {
  return ((x << k) | (x >>> (32 - k))) >>> 0;
}

/**
 * xoshiro128** (Blackman and Vigna), implemented with 32-bit operations only so the C# mirror can
 * reproduce it bit for bit. The exact sequence is pinned by `__tests__/fixtures/xoshiro128ss.json`,
 * generated from the published C reference (ARCHITECTURE 7.5).
 */
export class Xoshiro128StarStar implements Rng {
  private s0: number;
  private s1: number;
  private s2: number;
  private s3: number;

  private constructor(state: Xoshiro128State) {
    this.s0 = state[0] >>> 0;
    this.s1 = state[1] >>> 0;
    this.s2 = state[2] >>> 0;
    this.s3 = state[3] >>> 0;
  }

  /** Four 32-bit words, not all zero. */
  static fromState(state: Xoshiro128State): Xoshiro128StarStar {
    for (const word of state) {
      if (!Number.isInteger(word) || word < 0 || word >= TWO_POW_32) {
        throw new RangeError("xoshiro128** state words must be integers in [0, 2^32)");
      }
    }
    if (state.every((word) => word === 0)) {
      throw new RangeError("xoshiro128** state must not be all zero");
    }
    return new Xoshiro128StarStar(state);
  }

  /**
   * The state is the first 16 bytes of SHA-256 over the seed, read as four little-endian words; an
   * all-zero result is replaced by the state [1, 0, 0, 0].
   */
  static fromSeed(seed: Uint8Array): Xoshiro128StarStar {
    return new Xoshiro128StarStar(stateFromSeed(seed));
  }

  state(): Xoshiro128State {
    return [this.s0, this.s1, this.s2, this.s3];
  }

  nextUint32(): number {
    const result = Math.imul(rotl(Math.imul(this.s1, 5) >>> 0, 7), 9) >>> 0;
    const t = (this.s1 << 9) >>> 0;
    this.s2 = (this.s2 ^ this.s0) >>> 0;
    this.s3 = (this.s3 ^ this.s1) >>> 0;
    this.s1 = (this.s1 ^ this.s2) >>> 0;
    this.s0 = (this.s0 ^ this.s3) >>> 0;
    this.s2 = (this.s2 ^ t) >>> 0;
    this.s3 = rotl(this.s3, 11);
    return result;
  }

  nextInt(maxExclusive: number): number {
    if (!Number.isInteger(maxExclusive) || maxExclusive < 1 || maxExclusive > TWO_POW_32) {
      throw new RangeError("nextInt expects an integer in [1, 2^32]");
    }
    const limit = TWO_POW_32 - (TWO_POW_32 % maxExclusive);
    let draw = this.nextUint32();
    while (draw >= limit) {
      draw = this.nextUint32();
    }
    return draw % maxExclusive;
  }
}

export function stateFromSeed(seed: Uint8Array): Xoshiro128State {
  const digest = sha256(seed);
  const view = new DataView(digest.buffer, digest.byteOffset, digest.byteLength);
  const state: Xoshiro128State = [
    view.getUint32(0, true),
    view.getUint32(4, true),
    view.getUint32(8, true),
    view.getUint32(12, true),
  ];
  return nonZeroState(state);
}

/** xoshiro128** must never run from an all-zero state; the spec replaces it with [1, 0, 0, 0]. */
export function nonZeroState(state: Xoshiro128State): Xoshiro128State {
  return state.every((word) => word === 0) ? [1, 0, 0, 0] : state;
}

/** Fisher–Yates from the last index down to 1 with `j = nextInt(i + 1)`; returns a new array. */
export function shuffle<T>(rng: Rng, items: readonly T[]): T[] {
  const out = items.slice();
  for (let i = out.length - 1; i >= 1; i--) {
    const j = rng.nextInt(i + 1);
    const atI = out[i];
    const atJ = out[j];
    if (atI === undefined || atJ === undefined) {
      throw new RangeError("shuffle index out of range");
    }
    out[i] = atJ;
    out[j] = atI;
  }
  return out;
}
