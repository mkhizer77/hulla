/**
 * The player's age band from the birth year they give at first launch (GDD 24.6, 25.4, #87). Only
 * the year is kept, in a server-only object that no other player can read. A year gives a range of
 * two possible ages, so the rules use the youngest: a player born in 2010 counts as 15 for the whole
 * of 2026.
 */
export const AGE_COLLECTION = "age";
export const AGE_KEY = "birth_year";

/** Players under this age can't play (COPPA, GDD 24.6). */
export const MINIMUM_AGE = 13;
/** Under this age: no voice and no free text with players who aren't friends (GDD 24.6). */
export const STRANGER_CHAT_AGE = 16;
/** Under this age: no personalised ads (GDD 24.6, 25.7). */
export const ADULT_AGE = 18;

const EARLIEST_YEAR = 1900;

export interface AgeRecord {
  readonly birthYear: number;
  readonly setTs: number;
}

/** The youngest the player can be in the year of `nowMs`. */
export function youngestAge(birthYear: number, nowMs: number): number {
  return new Date(nowMs).getUTCFullYear() - birthYear - 1;
}

export type BirthYearCheck =
  | { readonly ok: true; readonly birthYear: number }
  | { readonly ok: false; readonly reason: "year" | "too_young" };

export function checkBirthYear(value: unknown, nowMs: number): BirthYearCheck {
  const year = new Date(nowMs).getUTCFullYear();
  if (
    typeof value !== "number" ||
    !Number.isInteger(value) ||
    value < EARLIEST_YEAR ||
    value > year
  ) {
    return { ok: false, reason: "year" };
  }
  if (youngestAge(value, nowMs) < MINIMUM_AGE) return { ok: false, reason: "too_young" };
  return { ok: true, birthYear: value };
}

/** What the age allows; a player who hasn't answered yet is treated as the youngest band that can play. */
export interface AgeLimits {
  readonly known: boolean;
  /** Voice and free text only with friends. */
  readonly strangersLocked: boolean;
  /** Ads may be personalised (with consent). */
  readonly personalisedAds: boolean;
}

export function limitsFor(record: AgeRecord | null, nowMs: number): AgeLimits {
  if (record === null) return { known: false, strangersLocked: true, personalisedAds: false };
  const age = youngestAge(record.birthYear, nowMs);
  return {
    known: true,
    strangersLocked: age < STRANGER_CHAT_AGE,
    personalisedAds: age >= ADULT_AGE,
  };
}

export interface AgeStore {
  read(userId: string): AgeRecord | null;
  write(userId: string, record: AgeRecord): void;
}

export function nkAgeStore(nk: nkruntime.Nakama): AgeStore {
  return {
    read(userId) {
      const [object] = nk.storageRead([{ collection: AGE_COLLECTION, key: AGE_KEY, userId }]);
      const value = object?.value as Partial<AgeRecord> | undefined;
      return typeof value?.birthYear === "number"
        ? { birthYear: value.birthYear, setTs: typeof value.setTs === "number" ? value.setTs : 0 }
        : null;
    },
    write(userId, record) {
      nk.storageWrite([
        {
          collection: AGE_COLLECTION,
          key: AGE_KEY,
          userId,
          value: { ...record },
          // Only the server reads it: never on the public card, never visible to other players.
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
  };
}

/** The limits for a player, read safely: a failed read keeps the youngest limits. */
export function ageLimitsOf(nk: nkruntime.Nakama, userId: string, nowMs: number): AgeLimits {
  try {
    return limitsFor(nkAgeStore(nk).read(userId), nowMs);
  } catch {
    return limitsFor(null, nowMs);
  }
}
