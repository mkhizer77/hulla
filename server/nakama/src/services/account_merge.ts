import {
  ErrorCodes,
  ruleError,
  ROOM_CODE_ALPHABET,
  type Rng,
  type RuleError,
} from "@hulla/shared-ts";

import { EMPTY_STATS, type Profile } from "./profiles";

import type { ProfileStats } from "@hulla/protocol/generated";

/**
 * Guest progress merges into a linked account (GDD 10.2, #38). The guest asks for a one-time
 * code while still signed in as the guest; the linked account redeems it. Stats add up, the
 * wallet adds up (interim, OQ-015), streaks and name changes take the larger, and the linked
 * account keeps its identity: handle, friend code, privacy, and its name unless it still wears
 * the generated guest name. The guest account is then deleted.
 */
export const MERGE_CODES_COLLECTION = "merge_codes";
export const MERGE_CODE_TTL_MS = 10 * 60_000;
export const MERGE_CODE_LENGTH = 8;

export interface MergeCode {
  readonly fromUserId: string;
  readonly expiresTs: number;
}

export interface MergeCodeStore {
  read(code: string): MergeCode | null;
  write(code: string, value: MergeCode): boolean;
  remove(code: string): void;
}

export function generateMergeCode(rng: Rng): string {
  let out = "";
  for (let i = 0; i < MERGE_CODE_LENGTH; i += 1) {
    out += ROOM_CODE_ALPHABET.charAt(rng.nextInt(ROOM_CODE_ALPHABET.length));
  }
  return out;
}

export type PrepareResponse =
  | { readonly ok: true; readonly code: string; readonly expiresTs: number }
  | { readonly ok: false; readonly error: RuleError };

export function prepareMerge(
  store: MergeCodeStore,
  rng: Rng,
  userId: string | null,
  now: number,
): PrepareResponse {
  if (userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  for (let attempt = 0; attempt < 5; attempt += 1) {
    const code = generateMergeCode(rng);
    const value = { fromUserId: userId, expiresTs: now + MERGE_CODE_TTL_MS };
    if (store.write(code, value)) return { ok: true, code, expiresTs: value.expiresTs };
  }
  return { ok: false, error: ruleError(ErrorCodes.auth.mergeCodeInvalid, { reason: "exhausted" }) };
}

export function addStats(a: ProfileStats, b: ProfileStats): ProfileStats {
  return {
    rounds: a.rounds + b.rounds,
    escapes: a.escapes + b.escapes,
    bhabhis: a.bhabhis + b.bhabhis,
    thullasGiven: a.thullasGiven + b.thullasGiven,
    thullasReceived: a.thullasReceived + b.thullasReceived,
    cardsPickedUp: a.cardsPickedUp + b.cardsPickedUp,
    biggestPickup: Math.max(a.biggestPickup, b.biggestPickup),
    handsTaken: a.handsTaken + b.handsTaken,
    currentStreak: a.rounds >= b.rounds ? a.currentStreak : b.currentStreak,
    bestStreak: Math.max(a.bestStreak, b.bestStreak),
    suits: {
      S: a.suits.S + b.suits.S,
      H: a.suits.H + b.suits.H,
      C: a.suits.C + b.suits.C,
      D: a.suits.D + b.suits.D,
    },
  };
}

const GUEST_NAME = /^Guest [A-Z2-9]{4}$/;

/** The linked account after absorbing the guest's profile; the linked account's identity wins. */
export function mergeProfiles(into: Profile, guest: Profile, now: number): Profile {
  const keepName = !GUEST_NAME.test(into.displayName) || GUEST_NAME.test(guest.displayName);
  return {
    ...into,
    displayName: keepName ? into.displayName : guest.displayName,
    avatar: into.avatar === "a01" && guest.avatar !== "a01" ? guest.avatar : into.avatar,
    frame: into.frame === "f00" && guest.frame !== "f00" ? guest.frame : into.frame,
    flag: into.flag ?? guest.flag,
    title: into.title ?? guest.title,
    bio: into.bio === "" ? guest.bio : into.bio,
    stats: addStats(into.stats, guest.stats),
    nameChanges: Math.max(into.nameChanges, guest.nameChanges),
    updatedTs: now,
  };
}

/** Wallet changes to apply to the linked account: every positive balance of the guest (OQ-015). */
export function walletChangeset(
  guestWallet: Readonly<Record<string, number>>,
): Record<string, number> {
  const out: Record<string, number> = {};
  for (const [key, value] of Object.entries(guestWallet)) {
    if (typeof value === "number" && Number.isFinite(value) && value > 0) out[key] = value;
  }
  return out;
}

export interface MergePorts {
  readonly codes: MergeCodeStore;
  readProfile(userId: string): Profile | null;
  writeProfile(userId: string, profile: Profile): void;
  readWallet(userId: string): Readonly<Record<string, number>>;
  creditWallet(
    userId: string,
    changeset: Readonly<Record<string, number>>,
    fromUserId: string,
  ): void;
  /** Moves the guest's round history to the linked account; the stats were summed already. */
  moveRounds(fromUserId: string, toUserId: string): number;
  deleteAccount(userId: string): void;
}

export type MergeResponse =
  | {
      readonly ok: true;
      readonly mergedFrom: string;
      readonly rounds: number;
      readonly profile: Profile;
    }
  | { readonly ok: false; readonly error: RuleError };

export function redeemMerge(
  ports: MergePorts,
  userId: string | null,
  code: string,
  now: number,
): MergeResponse {
  if (userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  const ticket = ports.codes.read(code.toUpperCase());
  if (ticket === null || ticket.expiresTs <= now) {
    if (ticket !== null) ports.codes.remove(code.toUpperCase());
    return { ok: false, error: ruleError(ErrorCodes.auth.mergeCodeInvalid) };
  }
  ports.codes.remove(code.toUpperCase());
  const from = ticket.fromUserId;
  if (from === userId)
    return {
      ok: false,
      error: ruleError(ErrorCodes.auth.mergeCodeInvalid, { reason: "same_account" }),
    };
  const into = ports.readProfile(userId);
  if (into === null) return { ok: false, error: ruleError(ErrorCodes.profile.notFound) };
  const guest = ports.readProfile(from);
  const merged = guest === null ? into : mergeProfiles(into, guest, now);
  ports.writeProfile(userId, merged);
  const changeset = walletChangeset(ports.readWallet(from));
  if (Object.keys(changeset).length > 0) ports.creditWallet(userId, changeset, from);
  const rounds = ports.moveRounds(from, userId);
  ports.deleteAccount(from);
  return { ok: true, mergedFrom: from, rounds, profile: merged };
}

export { EMPTY_STATS };
