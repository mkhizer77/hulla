import { ErrorCodes, ROOM_CODE_ALPHABET, ruleError, type RuleError } from "@hulla/shared-ts";

import { FRIEND_CODE_LENGTH, nkAreFriends, type Profile, type ProfileStore } from "./profiles";

/**
 * Friends on Nakama's own friend graph (GDD 10.3, 10.6): the client sends, accepts and declines
 * requests through the friend API; the server adds the friend-code path, the caps, pinned
 * favourites and the Recent Players list written at round end.
 */
export const FRIEND_CAP = 500;
export const FAVOURITE_CAP = 10;
export const RECENT_CAP = 50;
export const FAVOURITES_COLLECTION = "favourites";
export const RECENT_COLLECTION = "recent_players";
export const MUTES_COLLECTION = "mutes";
export const MUTE_CAP = 200;

export interface RecentPlayer {
  readonly userId: string;
  readonly lastPlayedTs: number;
  readonly rounds: number;
}

export interface SocialStore {
  userIdForFriendCode(code: string): string | null;
  favourites(userId: string): readonly string[];
  writeFavourites(userId: string, ids: readonly string[]): void;
  /** Who pinned this player: the reverse index the "friend came online" push reads. */
  favouritedBy(userId: string): readonly string[];
  writeFavouritedBy(userId: string, ids: readonly string[]): void;
  recent(userId: string): readonly RecentPlayer[];
  writeRecent(userId: string, players: readonly RecentPlayer[]): void;
  mutes(userId: string): readonly string[];
  writeMutes(userId: string, ids: readonly string[]): void;
}

export interface FriendPorts {
  friendCount(userId: string): number;
  /** Blocked either way: no request in either direction (GDD 10.11). */
  isBlocked?(userId: string, otherId: string): boolean;
  isFriend(userId: string, otherId: string): boolean;
  /** Sends the request, or completes the friendship when the other side already asked. */
  addFriend(userId: string, otherId: string): void;
}

/** Upper-cases and strips spaces and dashes; undefined unless eight symbols of the code alphabet. */
export function normaliseFriendCode(input: string): string | undefined {
  const cleaned = input.toUpperCase().replace(/[\s-]/g, "");
  if (cleaned.length !== FRIEND_CODE_LENGTH) return undefined;
  for (let i = 0; i < cleaned.length; i += 1) {
    if (!ROOM_CODE_ALPHABET.includes(cleaned.charAt(i))) return undefined;
  }
  return cleaned;
}

export type AddByCodeResponse =
  | { readonly ok: true; readonly userId: string }
  | { readonly ok: false; readonly error: RuleError };

export function addFriendByCode(
  store: SocialStore,
  ports: FriendPorts,
  userId: string | null,
  input: string,
): AddByCodeResponse {
  if (userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  const code = normaliseFriendCode(input);
  const target = code === undefined ? null : store.userIdForFriendCode(code);
  if (target === null) return { ok: false, error: ruleError(ErrorCodes.profile.notFound) };
  if (target === userId) return { ok: false, error: ruleError(ErrorCodes.social.self) };
  if (ports.isBlocked?.(userId, target) === true) {
    return { ok: false, error: ruleError(ErrorCodes.social.blocked) };
  }
  if (ports.friendCount(userId) >= FRIEND_CAP) {
    return { ok: false, error: ruleError(ErrorCodes.social.friendCap, { cap: FRIEND_CAP }) };
  }
  ports.addFriend(userId, target);
  return { ok: true, userId: target };
}

export type FavouriteResponse =
  | { readonly ok: true; readonly favourites: readonly string[] }
  | { readonly ok: false; readonly error: RuleError };

/** Pins or unpins a mutual friend; at most ten pinned (GDD 10.3). */
export function setFavourite(
  store: SocialStore,
  ports: FriendPorts,
  userId: string | null,
  targetId: string,
  pinned: boolean,
): FavouriteResponse {
  if (userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  const current = store.favourites(userId);
  if (!pinned) {
    const next = current.filter((id) => id !== targetId);
    if (next.length !== current.length) {
      store.writeFavourites(userId, next);
      store.writeFavouritedBy(
        targetId,
        store.favouritedBy(targetId).filter((id) => id !== userId),
      );
    }
    return { ok: true, favourites: next };
  }
  if (current.includes(targetId)) return { ok: true, favourites: current };
  if (!ports.isFriend(userId, targetId))
    return { ok: false, error: ruleError(ErrorCodes.social.notFriends) };
  if (current.length >= FAVOURITE_CAP) {
    return { ok: false, error: ruleError(ErrorCodes.social.favouriteCap, { cap: FAVOURITE_CAP }) };
  }
  const next = [...current, targetId];
  store.writeFavourites(userId, next);
  const watchers = store.favouritedBy(targetId);
  if (!watchers.includes(userId)) store.writeFavouritedBy(targetId, [...watchers, userId]);
  return { ok: true, favourites: next };
}

export type MuteResponse =
  | { readonly ok: true; readonly mutes: readonly string[] }
  | { readonly ok: false; readonly error: RuleError };

/** A mute hides a player's text, quick chat and emotes on every device; the client applies it (GDD 11.1). */
export function setMute(
  store: SocialStore,
  userId: string | null,
  targetId: string,
  muted: boolean,
): MuteResponse {
  if (userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  if (targetId === userId || targetId === "") {
    return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "user_id" }) };
  }
  const current = store.mutes(userId);
  const next = muted
    ? current.includes(targetId)
      ? current
      : [...current.slice(-(MUTE_CAP - 1)), targetId]
    : current.filter((id) => id !== targetId);
  if (next !== current) store.writeMutes(userId, next);
  return { ok: true, mutes: next };
}

/** After a round: everyone at the table remembers everyone else, most recent first, fifty deep. */
export function recordRecentPlayers(
  store: SocialStore,
  playerIds: readonly string[],
  now: number,
): void {
  const unique = [...new Set(playerIds)];
  for (const me of unique) {
    const existing = store.recent(me);
    const byId = new Map(existing.map((p) => [p.userId, p]));
    for (const other of unique) {
      if (other === me) continue;
      const seen = byId.get(other);
      byId.set(other, { userId: other, lastPlayedTs: now, rounds: (seen?.rounds ?? 0) + 1 });
    }
    const next = [...byId.values()]
      .sort((a, b) => b.lastPlayedTs - a.lastPlayedTs)
      .slice(0, RECENT_CAP);
    store.writeRecent(me, next);
  }
}

export interface RecentCard {
  readonly userId: string;
  readonly displayName: string | null;
  readonly handle: string | null;
  readonly avatar: string | null;
  readonly lastPlayedTs: number;
  readonly rounds: number;
  readonly friend: boolean;
  readonly favourite: boolean;
}

export interface SocialOverview {
  readonly favourites: readonly string[];
  readonly recent: readonly RecentCard[];
}

/** The Recent Players list with cards, for the results screen (GDD 14.8). */
export function socialOverview(
  store: SocialStore,
  ports: FriendPorts,
  profiles: Pick<ProfileStore, "read">,
  userId: string,
): SocialOverview {
  const favourites = store.favourites(userId);
  const recent = store.recent(userId).map((p): RecentCard => {
    const profile: Profile | null = profiles.read(p.userId);
    return {
      userId: p.userId,
      displayName: profile?.displayName ?? null,
      handle: profile?.handle ?? null,
      avatar: profile?.avatar ?? null,
      lastPlayedTs: p.lastPlayedTs,
      rounds: p.rounds,
      friend: ports.isFriend(userId, p.userId),
      favourite: favourites.includes(p.userId),
    };
  });
  return { favourites, recent };
}

function isRecentPlayer(value: unknown): value is RecentPlayer {
  if (typeof value !== "object" || value === null) return false;
  const p = value as Record<string, unknown>;
  return (
    typeof p["userId"] === "string" &&
    typeof p["lastPlayedTs"] === "number" &&
    typeof p["rounds"] === "number"
  );
}

export function nkSocialStore(nk: nkruntime.Nakama): SocialStore {
  const SYSTEM = "00000000-0000-0000-0000-000000000000";
  const readList = (
    collection: string,
    userId: string,
    field: string,
    key = "list",
  ): readonly unknown[] => {
    const objects = nk.storageRead([{ collection, key, userId }]);
    const value = objects[0]?.value as Record<string, unknown> | undefined;
    const list = value?.[field];
    return Array.isArray(list) ? (list as unknown[]) : [];
  };
  const writeList = (
    collection: string,
    userId: string,
    field: string,
    list: readonly unknown[],
    key = "list",
  ): void => {
    nk.storageWrite([
      {
        collection,
        key,
        userId,
        value: { [field]: [...list] },
        permissionRead: 1,
        permissionWrite: 0,
      },
    ]);
  };
  return {
    userIdForFriendCode(code) {
      const objects = nk.storageRead([{ collection: "friend_codes", key: code, userId: SYSTEM }]);
      const value = objects[0]?.value as { userId?: unknown } | undefined;
      return typeof value?.userId === "string" ? value.userId : null;
    },
    favourites: (userId) =>
      readList(FAVOURITES_COLLECTION, userId, "ids").filter(
        (id): id is string => typeof id === "string",
      ),
    writeFavourites: (userId, ids) => {
      writeList(FAVOURITES_COLLECTION, userId, "ids", ids);
    },
    favouritedBy: (userId) =>
      readList(FAVOURITES_COLLECTION, userId, "ids", "by").filter(
        (id): id is string => typeof id === "string",
      ),
    writeFavouritedBy: (userId, ids) => {
      writeList(FAVOURITES_COLLECTION, userId, "ids", ids, "by");
    },
    recent: (userId) => readList(RECENT_COLLECTION, userId, "players").filter(isRecentPlayer),
    writeRecent: (userId, players) => {
      writeList(RECENT_COLLECTION, userId, "players", players);
    },
    mutes: (userId) =>
      readList(MUTES_COLLECTION, userId, "ids").filter(
        (id): id is string => typeof id === "string",
      ),
    writeMutes: (userId, ids) => {
      writeList(MUTES_COLLECTION, userId, "ids", ids);
    },
  };
}

export function nkFriendPorts(
  nk: nkruntime.Nakama,
  isBlocked?: (userId: string, otherId: string) => boolean,
): FriendPorts {
  return {
    ...(isBlocked === undefined ? {} : { isBlocked }),
    friendCount(userId) {
      let count = 0;
      let cursor: string | undefined;
      for (let page = 0; page < 10; page += 1) {
        const list = nk.friendsList(userId, 100, 0, cursor);
        const friends = list.friends ?? [];
        count += friends.length;
        if (
          friends.length < 100 ||
          list.cursor === undefined ||
          list.cursor === "" ||
          list.cursor === cursor
        )
          break;
        cursor = list.cursor;
      }
      return count;
    },
    isFriend: (userId, otherId) => nkAreFriends(nk, userId, otherId),
    addFriend(userId, otherId) {
      const username = nk.usersGetId([userId])[0]?.username ?? "";
      nk.friendsAdd(userId, username, [otherId], [], {});
    },
  };
}
