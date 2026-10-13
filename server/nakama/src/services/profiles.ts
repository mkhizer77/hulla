import { ErrorCodes, err, ok, ruleError, ROOM_CODE_ALPHABET } from "@hulla/shared-ts";

import { avatarForAccount, DEFAULT_FRAME, isAvatar, isFrame } from "../content/avatars";
import { log } from "../infra/log";

import type { RoundRecord } from "../match/types";
import type {
  Profile as ProfileView,
  ProfileStats,
  ProfileUpdate,
} from "@hulla/protocol/generated";
import type { Result, Rng, RuleError } from "@hulla/shared-ts";

export const PROFILES_COLLECTION = "profiles";
export const PROFILE_KEY = "profile";
export const FRIEND_CODES_COLLECTION = "friend_codes";
export const FRIEND_CODE_LENGTH = 8;
/** The first display-name change is free; later ones cost gems once the wallet exists (OQ-013). */
export const FREE_NAME_CHANGES = 1;
/** The name a fresh account gets from its friend code (`bootstrapProfile`). */
export const GUEST_NAME = /^Guest [A-Z2-9]{4}$/;

/** The stored profile: everything the owner sees. */
export interface Profile {
  readonly displayName: string;
  readonly handle: string;
  readonly friendCode: string;
  readonly avatar: string;
  readonly frame: string;
  readonly flag: string | null;
  readonly title: string | null;
  readonly bio: string;
  readonly stats: ProfileStats;
  readonly privacy: { readonly showOnline: boolean; readonly visibility: "everyone" | "friends" };
  readonly nameChanges: number;
  readonly createdTs: number;
  readonly updatedTs: number;
}

export interface ProfileStore {
  read(userId: string): Profile | null;
  write(userId: string, profile: Profile): void;
  /** False when the code already belongs to someone. */
  claimFriendCode(code: string, userId: string): boolean;
}

export type Relation = "self" | "friend" | "other";

export const EMPTY_STATS: ProfileStats = {
  rounds: 0,
  escapes: 0,
  bhabhis: 0,
  thullasGiven: 0,
  thullasReceived: 0,
  cardsPickedUp: 0,
  biggestPickup: 0,
  handsTaken: 0,
  currentStreak: 0,
  bestStreak: 0,
  suits: { S: 0, H: 0, C: 0, D: 0 },
};

export function generateFriendCode(rng: Rng): string {
  let out = "";
  for (let i = 0; i < FRIEND_CODE_LENGTH; i += 1) {
    out += ROOM_CODE_ALPHABET.charAt(rng.nextInt(ROOM_CODE_ALPHABET.length));
  }
  return out;
}

/** A fresh profile for a new account: a guest name from its friend code, the first presets. */
export function bootstrapProfile(
  store: ProfileStore,
  rng: Rng,
  userId: string,
  handle: string,
  now: number,
): Profile {
  const existing = store.read(userId);
  if (existing !== null) return existing;
  let friendCode = generateFriendCode(rng);
  for (let attempt = 0; attempt < 5 && !store.claimFriendCode(friendCode, userId); attempt += 1) {
    friendCode = generateFriendCode(rng);
  }
  const profile: Profile = {
    displayName: `Guest ${friendCode.slice(0, 4)}`,
    handle,
    friendCode,
    avatar: avatarForAccount(userId),
    frame: DEFAULT_FRAME,
    flag: null,
    title: null,
    bio: "",
    stats: EMPTY_STATS,
    privacy: { showOnline: true, visibility: "everyone" },
    nameChanges: 0,
    createdTs: now,
    updatedTs: now,
  };
  store.write(userId, profile);
  return profile;
}

/** Control, format and line-separator characters, checked per code point (portable across Node and goja). */
function hasControl(text: string): boolean {
  for (const ch of Array.from(text)) {
    const cp = ch.codePointAt(0) ?? 0;
    if (cp < 0x20 || (cp >= 0x7f && cp <= 0x9f) || cp === 0xad || cp === 0xfeff) return true;
    if (
      (cp >= 0x200b && cp <= 0x200f) ||
      (cp >= 0x2028 && cp <= 0x202e) ||
      (cp >= 0x2060 && cp <= 0x2064)
    ) {
      return true;
    }
  }
  return false;
}

function codePoints(text: string): number {
  return Array.from(text).length;
}
const HANDLE = /^[A-Za-z0-9_]{3,16}$/;

/** 2 to 20 code points in any script, one line, no control characters; filters come with M2. */
export function normaliseDisplayName(input: string): string | null {
  const trimmed = input.replace(/\s+/g, " ").trim();
  const length = codePoints(trimmed);
  if (length < 2 || length > 20 || hasControl(trimmed)) return null;
  return trimmed;
}

export function normaliseBio(input: string): string | null {
  const trimmed = input.replace(/\s+/g, " ").trim();
  if (codePoints(trimmed) > 60 || hasControl(trimmed)) return null;
  return trimmed;
}

/** The editable fields after validation; the payload passed the generated check already. */
export function applyProfileUpdate(
  profile: Profile,
  update: ProfileUpdate,
  now: number,
): Result<Profile, RuleError> {
  let next: Profile = profile;
  if (update.displayName !== undefined) {
    const name = normaliseDisplayName(update.displayName);
    if (name === null) return err(ruleError(ErrorCodes.profile.invalidName));
    if (name !== profile.displayName) {
      // Leaving the generated guest name is not a rename: the free change stays (GDD 9.4).
      const fromGuestName = GUEST_NAME.test(profile.displayName);
      if (!fromGuestName && profile.nameChanges >= FREE_NAME_CHANGES) {
        return err(
          ruleError(ErrorCodes.profile.nameChangeLocked, { changes: profile.nameChanges }),
        );
      }
      next = {
        ...next,
        displayName: name,
        nameChanges: fromGuestName ? profile.nameChanges : profile.nameChanges + 1,
      };
    }
  }
  if (update.handle !== undefined) {
    if (!HANDLE.test(update.handle)) return err(ruleError(ErrorCodes.profile.invalidHandle));
    next = { ...next, handle: update.handle };
  }
  if (update.avatar !== undefined) {
    if (!isAvatar(update.avatar))
      return err(ruleError(ErrorCodes.profile.unknownAvatar, { avatar: update.avatar }));
    next = { ...next, avatar: update.avatar };
  }
  if (update.frame !== undefined) {
    if (!isFrame(update.frame))
      return err(ruleError(ErrorCodes.profile.unknownAvatar, { frame: update.frame }));
    next = { ...next, frame: update.frame };
  }
  if (update.flag !== undefined) next = { ...next, flag: update.flag };
  if (update.bio !== undefined) {
    const bio = normaliseBio(update.bio);
    if (bio === null) return err(ruleError(ErrorCodes.profile.invalidName, { field: "bio" }));
    next = { ...next, bio };
  }
  if (update.privacy !== undefined) next = { ...next, privacy: update.privacy };
  return ok({ ...next, updatedTs: now });
}

/** What a viewer may see (GDD 10.1 privacy): null when the profile is friends-only and the viewer is not one. */
export function profileView(
  userId: string,
  profile: Profile,
  relation: Relation,
  online: boolean,
): ProfileView | null {
  if (relation === "other" && profile.privacy.visibility === "friends") return null;
  const card: ProfileView = {
    userId,
    displayName: profile.displayName,
    handle: profile.handle,
    avatar: profile.avatar,
    frame: profile.frame,
    flag: profile.flag,
    title: profile.title,
    bio: profile.bio,
    stats: profile.stats,
  };
  const withOnline = relation === "self" || profile.privacy.showOnline ? { ...card, online } : card;
  if (relation === "self") {
    return {
      ...withOnline,
      friendCode: profile.friendCode,
      privacy: profile.privacy,
      nameChanges: profile.nameChanges,
    };
  }
  return relation === "friend" ? { ...withOnline, friendCode: profile.friendCode } : withOnline;
}

/** The stats card after a round: counts from the result and the game's per-seat highlights. */
export function applyRoundToStats(
  stats: ProfileStats,
  seat: number,
  record: RoundRecord,
): ProfileStats {
  const result = record.result;
  if (result === null) return stats;
  const h = record.highlights[seat] ?? {};
  const escaped = result.loserSeat !== seat && result.finishOrder.includes(seat);
  const currentStreak = escaped ? stats.currentStreak + 1 : 0;
  const n = (key: string): number => h[key] ?? 0;
  return {
    rounds: stats.rounds + 1,
    escapes: stats.escapes + (escaped ? 1 : 0),
    bhabhis: stats.bhabhis + (result.loserSeat === seat ? 1 : 0),
    thullasGiven: stats.thullasGiven + n("thullasGiven"),
    thullasReceived: stats.thullasReceived + n("thullasReceived"),
    cardsPickedUp: stats.cardsPickedUp + n("cardsPickedUp"),
    biggestPickup: Math.max(stats.biggestPickup, n("biggestPickup")),
    handsTaken: stats.handsTaken + n("handsTaken"),
    currentStreak,
    bestStreak: Math.max(stats.bestStreak, currentStreak),
    suits: {
      S: stats.suits.S + n("playedS"),
      H: stats.suits.H + n("playedH"),
      C: stats.suits.C + n("playedC"),
      D: stats.suits.D + n("playedD"),
    },
  };
}

export function nkProfileStore(nk: nkruntime.Nakama, logger: nkruntime.Logger): ProfileStore {
  return {
    read(userId) {
      const objects = nk.storageRead([
        { collection: PROFILES_COLLECTION, key: PROFILE_KEY, userId },
      ]);
      const first = objects[0];
      return first === undefined ? null : (first.value as unknown as Profile);
    },
    write(userId, profile) {
      nk.storageWrite([
        {
          collection: PROFILES_COLLECTION,
          key: PROFILE_KEY,
          userId,
          value: profile,
          permissionRead: 1,
          permissionWrite: 0,
        },
      ]);
    },
    claimFriendCode(code, userId) {
      try {
        nk.storageWrite([
          {
            collection: FRIEND_CODES_COLLECTION,
            key: code,
            userId: "00000000-0000-0000-0000-000000000000",
            value: { userId },
            version: "*",
            permissionRead: 0,
            permissionWrite: 0,
          },
        ]);
        return true;
      } catch (error: unknown) {
        log.warn(logger, "friend code taken", { code, error: String(error) });
        return false;
      }
    },
  };
}

/** Are two accounts mutual friends in Nakama's friend graph (state 0)? */
export function nkAreFriends(nk: nkruntime.Nakama, viewerId: string, userId: string): boolean {
  let cursor: string | undefined;
  for (let page = 0; page < 10; page += 1) {
    const list = nk.friendsList(viewerId, 100, 0, cursor);
    const friends = list.friends ?? [];
    if (friends.some((f) => f.user?.userId === userId)) return true;
    // A short page or a repeated cursor is the end, whatever Nakama hands back.
    if (
      friends.length < 100 ||
      list.cursor === undefined ||
      list.cursor === "" ||
      list.cursor === cursor
    ) {
      return false;
    }
    cursor = list.cursor;
  }
  return false;
}
