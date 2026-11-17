import {
  ErrorCodes,
  ruleError,
  ROOM_CODE_ALPHABET,
  type Rng,
  type RuleError,
} from "@hulla/shared-ts";

import { GUEST_NAME, EMPTY_STATS, type Profile } from "./profiles";

import type { Inventory } from "./catalog";
import type { Progress } from "./progression";
import type { Entitlements } from "./purchases";
import type { ProfileStats } from "@hulla/protocol/generated";

/**
 * Guest progress merges into a linked account (GDD 10.2, #38). The guest asks for a one-time
 * code while still signed in as the guest; the linked account redeems it. Stats add up, streaks
 * and name changes take the larger, and the linked account keeps its identity: handle, friend
 * code, privacy, and its name unless it still wears the generated guest name. The guest account
 * is then deleted.
 *
 * A merge must not work as a transfer (D2). Only a guest, an account with nothing but device
 * ids, can be merged away; its wallet moves through the ledger once, and an account takes a
 * wallet this way at most once in thirty days (interim, OQ-015). What the guest owns (items,
 * entitlements, the further progress) moves too.
 */
export const MERGE_CODES_COLLECTION = "merge_codes";
export const MERGE_CODE_TTL_MS = 10 * 60_000;
export const MERGE_CODE_LENGTH = 8;
/** The gap between two merges that carry a wallet into the same account (OQ-015). */
export const WALLET_MERGE_GAP_MS = 30 * 86_400_000;

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
  isGuest: (userId: string) => boolean = () => true,
): PrepareResponse {
  if (userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  if (!isGuest(userId))
    return {
      ok: false,
      error: ruleError(ErrorCodes.auth.mergeCodeInvalid, { reason: "not_guest" }),
    };
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

/** Everything either account bought or was granted; the linked account's choices stay. */
export function mergeInventory(into: Inventory, guest: Inventory, now: number): Inventory {
  return {
    ...into,
    owned: { ...guest.owned, ...into.owned },
    equipped: { ...guest.equipped, ...into.equipped },
    updatedTs: now,
  };
}

export function mergeEntitlements(
  into: Entitlements,
  guest: Entitlements,
  now: number,
): Entitlements {
  const products: Record<string, number> = { ...into.products };
  for (const [id, count] of Object.entries(guest.products)) {
    products[id] = (products[id] ?? 0) + count;
  }
  return {
    removeAds: into.removeAds || guest.removeAds,
    frames: [...new Set([...into.frames, ...guest.frames])],
    products,
    pendingPass: into.pendingPass ?? guest.pendingPass,
    updatedTs: now,
  };
}

/**
 * The further of the two levels, never the sum: adding XP would cross levels without their
 * grants. The linked account keeps its own day counters and streak.
 */
export function mergeProgress(into: Progress, guest: Progress, now: number): Progress {
  const ahead = guest.xp > into.xp ? guest : into;
  return {
    ...into,
    xp: ahead.xp,
    level: ahead.level,
    titles: [...new Set([...into.titles, ...guest.titles])],
    loginStreak: Math.max(into.loginStreak, guest.loginStreak),
    updatedTs: now,
  };
}

export interface MergePorts {
  readonly codes: MergeCodeStore;
  readProfile(userId: string): Profile | null;
  writeProfile(userId: string, profile: Profile): void;
  /** True for an account that signs in by device id only: no email, phone or provider. */
  isGuest(userId: string): boolean;
  readWallet(userId: string): Readonly<Record<string, number>>;
  /** Credits through the ledger under a key made from the guest's id, so a replay adds nothing. */
  creditWallet(
    userId: string,
    changeset: Readonly<Record<string, number>>,
    fromUserId: string,
  ): void;
  /** When this account last took a wallet from a guest, or null. */
  lastWalletMergeTs(userId: string): number | null;
  markWalletMerge(userId: string, now: number): void;
  /** Items, entitlements and the further progress of the guest, added to the linked account. */
  moveOwned?(fromUserId: string, toUserId: string): void;
  /** Moves the guest's round history to the linked account; the stats were summed already. */
  moveRounds(fromUserId: string, toUserId: string): number;
  deleteAccount(userId: string): void;
  /** The guest's device ids, which move to the linked account so the phone keeps signing in to it. */
  devicesOf?(userId: string): readonly string[];
  linkDevices?(userId: string, deviceIds: readonly string[]): void;
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
  if (!ports.isGuest(from))
    return {
      ok: false,
      error: ruleError(ErrorCodes.auth.mergeCodeInvalid, { reason: "not_guest" }),
    };
  const into = ports.readProfile(userId);
  if (into === null) return { ok: false, error: ruleError(ErrorCodes.profile.notFound) };
  const changeset = walletChangeset(ports.readWallet(from));
  const carriesWallet = Object.keys(changeset).length > 0;
  if (carriesWallet) {
    const last = ports.lastWalletMergeTs(userId);
    if (last !== null && now - last < WALLET_MERGE_GAP_MS)
      return {
        ok: false,
        error: ruleError(ErrorCodes.auth.mergeCodeInvalid, { reason: "too_soon" }),
      };
  }
  const guest = ports.readProfile(from);
  const merged = guest === null ? into : mergeProfiles(into, guest, now);
  ports.writeProfile(userId, merged);
  if (carriesWallet) {
    ports.creditWallet(userId, changeset, from);
    ports.markWalletMerge(userId, now);
  }
  ports.moveOwned?.(from, userId);
  const rounds = ports.moveRounds(from, userId);
  // The guest's device signs in by device id at every start; once the guest is gone that id must
  // lead to the account it merged into (a phone or provider sign-in on a new device, #99).
  const devices = ports.devicesOf?.(from) ?? [];
  ports.deleteAccount(from);
  if (devices.length > 0) ports.linkDevices?.(userId, devices);
  return { ok: true, mergedFrom: from, rounds, profile: merged };
}

export { EMPTY_STATS };
