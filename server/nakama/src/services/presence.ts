import { ErrorCodes, ruleError, type RuleError } from "@hulla/shared-ts";

import { type Profile } from "./profiles";

/**
 * Presence (GDD 10.4): Online, In lobby (joinable), In round, Away, Offline with last seen. Nakama's
 * own session tracker says who is online; the match binding writes which table a player sits at;
 * the client sets Away; the live match label says whether the table is still joinable.
 */
export const PRESENCE_COLLECTION = "presence";
export const PRESENCE_KEY = "state";
export const MAX_FRIENDS_LISTED = 500;

export interface TablePointer {
  readonly matchId: string;
  readonly joinedTs: number;
}

export interface PresenceRecord {
  readonly table: TablePointer | null;
  readonly away: boolean;
  readonly lastSeenTs: number;
}

export const EMPTY_PRESENCE: PresenceRecord = { table: null, away: false, lastSeenTs: 0 };

export type PresenceState = "online" | "lobby" | "round" | "away" | "offline" | "hidden";

/** What the match label carries (see `labelFor` in the match binding). */
export interface TableLabel {
  readonly code: string;
  readonly gameId: string;
  readonly mode: string;
  readonly status: string;
  readonly seated: number;
  readonly open: number;
}

export interface FriendTable extends TableLabel {
  readonly matchId: string;
  readonly joinable: boolean;
}

export interface FriendPresence {
  readonly userId: string;
  readonly displayName: string | null;
  readonly handle: string | null;
  readonly avatar: string | null;
  readonly favourite: boolean;
  readonly state: PresenceState;
  /** Null while online, or hidden by the friend's privacy choice. */
  readonly lastSeenTs: number | null;
  readonly table: FriendTable | null;
}

export interface PresenceStore {
  read(userId: string): PresenceRecord;
  readMany(userIds: readonly string[]): ReadonlyMap<string, PresenceRecord>;
  write(userId: string, record: PresenceRecord): void;
}

export interface PresencePorts {
  friendIds(userId: string): readonly string[];
  onlineIds(userIds: readonly string[]): ReadonlySet<string>;
  profiles(userIds: readonly string[]): ReadonlyMap<string, Profile>;
  /** The live label of a match, or null once it is gone. */
  label(matchId: string): TableLabel | null;
  favourites(userId: string): readonly string[];
}

export function parseLabel(label: string): TableLabel | null {
  try {
    const value: unknown = JSON.parse(label);
    if (typeof value !== "object" || value === null) return null;
    const v = value as Record<string, unknown>;
    if (typeof v["code"] !== "string" || typeof v["status"] !== "string") return null;
    return {
      code: v["code"],
      gameId: typeof v["gameId"] === "string" ? v["gameId"] : "",
      mode: typeof v["mode"] === "string" ? v["mode"] : "",
      status: v["status"],
      seated: typeof v["seated"] === "number" ? v["seated"] : 0,
      open: typeof v["open"] === "number" ? v["open"] : 0,
    };
  } catch {
    return null;
  }
}

/** Called by the match binding when a player takes a seat. */
export function seatTaken(
  store: PresenceStore,
  userId: string,
  matchId: string,
  now: number,
): void {
  const current = store.read(userId);
  if (current.table?.matchId === matchId) return;
  store.write(userId, { ...current, table: { matchId, joinedTs: now }, lastSeenTs: now });
}

/** Called by the match binding when a player leaves a table for good. */
export function seatReleased(
  store: PresenceStore,
  userId: string,
  matchId: string,
  now: number,
): void {
  const current = store.read(userId);
  if (current.table?.matchId !== matchId) return;
  store.write(userId, { ...current, table: null, lastSeenTs: now });
}

export function touch(store: PresenceStore, userId: string, now: number): void {
  store.write(userId, { ...store.read(userId), lastSeenTs: now });
}

export type AwayResponse =
  { readonly ok: true; readonly away: boolean } | { readonly ok: false; readonly error: RuleError };

/** The client marks itself Away when the app goes to the background and back. */
export function setAway(
  store: PresenceStore,
  userId: string | null,
  away: boolean,
  now: number,
): AwayResponse {
  if (userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  store.write(userId, { ...store.read(userId), away, lastSeenTs: now });
  return { ok: true, away };
}

const ORDER: Record<PresenceState, number> = {
  lobby: 0,
  round: 1,
  online: 2,
  away: 3,
  offline: 4,
  hidden: 5,
};

function stateOf(
  online: boolean,
  record: PresenceRecord,
  table: FriendTable | null,
  showOnline: boolean,
): PresenceState {
  if (!showOnline) return "hidden";
  if (!online) return "offline";
  if (record.away) return "away";
  if (table === null) return "online";
  return table.status === "inRound" ? "round" : "lobby";
}

function tableOf(ports: PresencePorts, record: PresenceRecord): FriendTable | null {
  if (record.table === null) return null;
  const label = ports.label(record.table.matchId);
  if (label === null || label.status === "closed") return null;
  return {
    ...label,
    matchId: record.table.matchId,
    joinable: label.status === "open" && label.open > 0,
  };
}

/**
 * Every friend with a state and, when seated, their table; joinable lobbies first so the Home hero
 * can offer "Join Ali's table (2 seats open)" (GDD 14.10), then in round, online, away, offline by
 * last seen, and friends who hide their presence last.
 */
export function listFriendPresence(
  store: PresenceStore,
  ports: PresencePorts,
  userId: string,
): FriendPresence[] {
  const ids = ports.friendIds(userId).slice(0, MAX_FRIENDS_LISTED);
  if (ids.length === 0) return [];
  const online = ports.onlineIds(ids);
  const records = store.readMany(ids);
  const profiles = ports.profiles(ids);
  const favourites = new Set(ports.favourites(userId));
  const friends = ids.map((id): FriendPresence => {
    const record = records.get(id) ?? EMPTY_PRESENCE;
    const profile = profiles.get(id) ?? null;
    const showOnline = profile?.privacy.showOnline ?? true;
    const table = online.has(id) && showOnline ? tableOf(ports, record) : null;
    const state = stateOf(online.has(id), record, table, showOnline);
    return {
      userId: id,
      displayName: profile?.displayName ?? null,
      handle: profile?.handle ?? null,
      avatar: profile?.avatar ?? null,
      favourite: favourites.has(id),
      state,
      lastSeenTs: state === "offline" && record.lastSeenTs > 0 ? record.lastSeenTs : null,
      table,
    };
  });
  return friends.sort((a, b) => {
    if (ORDER[a.state] !== ORDER[b.state]) return ORDER[a.state] - ORDER[b.state];
    if (a.state === "lobby" && b.state === "lobby")
      return (b.table?.open ?? 0) - (a.table?.open ?? 0);
    if (a.state === "offline") return (b.lastSeenTs ?? 0) - (a.lastSeenTs ?? 0);
    if (a.favourite !== b.favourite) return a.favourite ? -1 : 1;
    return (a.displayName ?? "").localeCompare(b.displayName ?? "");
  });
}

function isPresenceRecord(value: unknown): value is PresenceRecord {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v["away"] === "boolean" && typeof v["lastSeenTs"] === "number" && "table" in v;
}

const CHUNK = 100;

function chunks<T>(items: readonly T[]): T[][] {
  const out: T[][] = [];
  for (let i = 0; i < items.length; i += CHUNK) out.push(items.slice(i, i + CHUNK));
  return out;
}

export function nkPresenceStore(nk: nkruntime.Nakama): PresenceStore {
  const readMany = (userIds: readonly string[]): ReadonlyMap<string, PresenceRecord> => {
    const found = new Map<string, PresenceRecord>();
    for (const ids of chunks(userIds)) {
      const objects = nk.storageRead(
        ids.map((userId) => ({ collection: PRESENCE_COLLECTION, key: PRESENCE_KEY, userId })),
      );
      for (const object of objects) {
        if (isPresenceRecord(object.value)) found.set(object.userId, object.value);
      }
    }
    return found;
  };
  return {
    readMany,
    read: (userId) => readMany([userId]).get(userId) ?? EMPTY_PRESENCE,
    write(userId, record) {
      nk.storageWrite([
        {
          collection: PRESENCE_COLLECTION,
          key: PRESENCE_KEY,
          userId,
          value: { table: record.table, away: record.away, lastSeenTs: record.lastSeenTs },
          permissionRead: 1,
          permissionWrite: 0,
        },
      ]);
    },
  };
}

/** Nakama tracks every connected socket in the user's notification stream (mode 0). */
function hasSocket(nk: nkruntime.Nakama, userId: string): boolean {
  try {
    return nk.streamCount({ mode: NOTIFICATION_STREAM, subject: userId }) > 0;
  } catch {
    return false;
  }
}

const NOTIFICATION_STREAM = 0;

/** Online for presence: a status set by the client, or any connected socket. */
export function isUserOnline(nk: nkruntime.Nakama, userId: string): boolean {
  try {
    if (nk.usersGetId([userId])[0]?.online === true) return true;
  } catch {
    return hasSocket(nk, userId);
  }
  return hasSocket(nk, userId);
}

export function nkPresencePorts(
  nk: nkruntime.Nakama,
  profiles: { read(userId: string): Profile | null },
  favourites: (userId: string) => readonly string[],
): PresencePorts {
  return {
    friendIds(userId) {
      const ids: string[] = [];
      let cursor: string | undefined;
      for (let page = 0; page < 10; page += 1) {
        const list = nk.friendsList(userId, CHUNK, 0, cursor);
        const friends = list.friends ?? [];
        for (const f of friends) {
          const id = f.user?.userId;
          if (id !== undefined && !ids.includes(id)) ids.push(id);
        }
        if (
          friends.length < CHUNK ||
          list.cursor === undefined ||
          list.cursor === "" ||
          list.cursor === cursor
        )
          break;
        cursor = list.cursor;
      }
      return ids;
    },
    onlineIds(userIds) {
      const online = new Set<string>();
      for (const ids of chunks(userIds)) {
        for (const user of nk.usersGetId(ids)) {
          if (user.online) online.add(user.userId);
        }
      }
      for (const id of userIds) {
        if (!online.has(id) && hasSocket(nk, id)) online.add(id);
      }
      return online;
    },
    profiles(userIds) {
      const found = new Map<string, Profile>();
      for (const id of userIds) {
        const profile = profiles.read(id);
        if (profile !== null) found.set(id, profile);
      }
      return found;
    },
    label(matchId) {
      const match = nk.matchGet(matchId);
      return match === null ? null : parseLabel(match.label);
    },
    favourites,
  };
}
