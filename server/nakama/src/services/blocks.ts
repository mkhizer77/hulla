import { ErrorCodes, ruleError, type RuleError } from "@hulla/shared-ts";

import { log } from "../infra/log";

import { setMute, type SocialStore } from "./social";

/**
 * Blocking is absolute (GDD 10.11, 24.4): it mutes both ways, keeps the pair out of each other's
 * matchmaking, and stops friend requests. Nakama records the block on the friend graph; this
 * store keeps both directions so either side can be checked in one read.
 */
export const BLOCKS_COLLECTION = "blocks";
export const BLOCK_CAP = 500;
/** How many excluded ids a matchmaker query carries; beyond that the oldest blocks are left out. */
export const MATCHMAKER_EXCLUDE_CAP = 100;

export interface BlockRecord {
  readonly blocked: readonly string[];
  readonly blockedBy: readonly string[];
}

export const EMPTY_BLOCKS: BlockRecord = { blocked: [], blockedBy: [] };

export interface BlockStore {
  read(userId: string): BlockRecord;
  write(userId: string, record: BlockRecord): void;
}

export interface BlockPorts {
  /** Nakama's own block: drops the friendship and refuses requests from the blocked side. */
  block(userId: string, targetId: string): void;
  unblock(userId: string, targetId: string): void;
}

export type BlockResponse =
  | { readonly ok: true; readonly blocked: readonly string[] }
  | { readonly ok: false; readonly error: RuleError };

function add(list: readonly string[], id: string, cap: number): readonly string[] {
  return list.includes(id) ? list : [...list.slice(-(cap - 1)), id];
}

function remove(list: readonly string[], id: string): readonly string[] {
  return list.filter((x) => x !== id);
}

export function setBlock(
  blocks: BlockStore,
  social: SocialStore,
  ports: BlockPorts,
  userId: string | null,
  targetId: string,
  blocked: boolean,
): BlockResponse {
  if (userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  if (targetId === "" || targetId === userId) {
    return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "user_id" }) };
  }
  const mine = blocks.read(userId);
  const theirs = blocks.read(targetId);
  if (blocked) {
    blocks.write(userId, { ...mine, blocked: add(mine.blocked, targetId, BLOCK_CAP) });
    blocks.write(targetId, { ...theirs, blockedBy: add(theirs.blockedBy, userId, BLOCK_CAP) });
    ports.block(userId, targetId);
    setMute(social, userId, targetId, true);
    setMute(social, targetId, userId, true);
    return { ok: true, blocked: blocks.read(userId).blocked };
  }
  blocks.write(userId, { ...mine, blocked: remove(mine.blocked, targetId) });
  blocks.write(targetId, { ...theirs, blockedBy: remove(theirs.blockedBy, userId) });
  ports.unblock(userId, targetId);
  setMute(social, userId, targetId, false);
  setMute(social, targetId, userId, false);
  return { ok: true, blocked: blocks.read(userId).blocked };
}

/** Everyone this player must not meet: those they blocked and those who blocked them. */
export function excludedIds(blocks: BlockStore, userId: string): readonly string[] {
  const record = blocks.read(userId);
  return [...new Set([...record.blocked, ...record.blockedBy])];
}

export function isBlockedEitherWay(blocks: BlockStore, userId: string, otherId: string): boolean {
  const record = blocks.read(userId);
  return record.blocked.includes(otherId) || record.blockedBy.includes(otherId);
}

export interface MatchmakerTicket {
  query: string;
  stringProperties: Record<string, string>;
}

/**
 * Every ticket carries its owner's id and excludes the blocked ids, so Nakama never pairs the two
 * (GDD 10.11). The newest blocks win when there are more than the query can carry.
 */
export function excludeBlocked<T extends MatchmakerTicket>(
  ticket: T,
  userId: string,
  excluded: readonly string[],
): T {
  const ids = excluded.slice(-MATCHMAKER_EXCLUDE_CAP);
  const clauses = ids.map((id) => `-properties.uid:${id}`);
  const query = [ticket.query.trim(), ...clauses].filter((c) => c !== "").join(" ");
  return { ...ticket, query, stringProperties: { ...ticket.stringProperties, uid: userId } };
}

function isBlockRecord(value: unknown): value is BlockRecord {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return Array.isArray(v["blocked"]) && Array.isArray(v["blockedBy"]);
}

export function nkBlockStore(nk: nkruntime.Nakama): BlockStore {
  return {
    read(userId) {
      const objects = nk.storageRead([{ collection: BLOCKS_COLLECTION, key: "list", userId }]);
      const value: unknown = objects[0]?.value;
      return isBlockRecord(value) ? value : EMPTY_BLOCKS;
    },
    write(userId, record) {
      nk.storageWrite([
        {
          collection: BLOCKS_COLLECTION,
          key: "list",
          userId,
          value: { blocked: [...record.blocked], blockedBy: [...record.blockedBy] },
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
  };
}

export function nkBlockPorts(nk: nkruntime.Nakama, logger: nkruntime.Logger): BlockPorts {
  const username = (userId: string): string => nk.usersGetId([userId])[0]?.username ?? "";
  return {
    block(userId, targetId) {
      try {
        nk.friendsBlock(userId, username(userId), [targetId], []);
      } catch (error: unknown) {
        log.warn(logger, "friend graph block failed", { error: String(error) });
      }
    },
    unblock(userId, targetId) {
      try {
        nk.friendsDelete(userId, username(userId), [targetId], []);
      } catch (error: unknown) {
        log.warn(logger, "friend graph unblock failed", { error: String(error) });
      }
    },
  };
}
