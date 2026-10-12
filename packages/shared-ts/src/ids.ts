import type { Rng } from "./prng";

declare const brand: unique symbol;

/** A nominal type over a primitive, so a `TableId` cannot be passed where a `PlayerId` is expected. */
export type Brand<T, Name extends string> = T & { readonly [brand]: Name };

export type PlayerId = Brand<string, "PlayerId">;
export type TableId = Brand<string, "TableId">;
export type RoundId = Brand<string, "RoundId">;

/** Seat position 0..N-1, clockwise (GDD 6.1). A plain number so engines can index with it. */
export type SeatIndex = number;

/** 32 symbols, none of 0, O, 1 or I, so a code can be read out over a voice call (GDD 7.3). */
export const ROOM_CODE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

export const ROOM_CODE_LENGTH = 6;

export function generateRoomCode(rng: Rng, length: number = ROOM_CODE_LENGTH): string {
  let code = "";
  for (let i = 0; i < length; i++) {
    code += ROOM_CODE_ALPHABET.charAt(rng.nextInt(ROOM_CODE_ALPHABET.length));
  }
  return code;
}

export function isRoomCode(value: string): boolean {
  if (value.length !== ROOM_CODE_LENGTH) {
    return false;
  }
  for (let i = 0; i < value.length; i++) {
    if (!ROOM_CODE_ALPHABET.includes(value.charAt(i))) {
      return false;
    }
  }
  return true;
}

/** Upper-cases and strips spaces and dashes; returns undefined when the result is not a valid code. */
export function normaliseRoomCode(input: string): string | undefined {
  const cleaned = input.toUpperCase().replace(/[\s-]/g, "");
  return isRoomCode(cleaned) ? cleaned : undefined;
}

export function tableId(code: string): TableId {
  return `t_${code}` as TableId;
}

export function roundId(index: number): RoundId {
  return `r_${index}` as RoundId;
}
