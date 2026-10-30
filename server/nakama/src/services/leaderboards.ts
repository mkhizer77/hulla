import { ErrorCodes, ruleError, type RuleError } from "@hulla/shared-ts";

import { log } from "../infra/log";

import { credit, type LedgerPorts } from "./ledger";
import { PROFILE_KEY, PROFILES_COLLECTION } from "./profiles";
import { forDay, nkProgressStore, type Progress } from "./progression";

import type { RoundRecord } from "../match/types";

/**
 * Weekly and monthly boards by Baithak points earned (GDD 12.8, R50) on Nakama leaderboards:
 * authoritative, incrementing, reset by cron. Points from private rooms stop at a daily cap;
 * public tables are uncapped. The reset hook pays the top brackets.
 */
export const BOARDS = {
  weekly: { id: "baithak_weekly", reset: "0 0 * * 1" },
  monthly: { id: "baithak_monthly", reset: "0 0 1 * *" },
} as const;
export type BoardKey = keyof typeof BOARDS;
export const PRIVATE_POINTS_DAILY_CAP = 30;
export const BOARD_PAGE = 50;
/** Gems for the top brackets at reset, until the cosmetic catalog exists (OQ-024). */
export const BRACKET_GEMS: readonly { readonly upToRank: number; readonly gems: number }[] = [
  { upToRank: 1, gems: 20 },
  { upToRank: 3, gems: 10 },
  { upToRank: 10, gems: 5 },
];

export function isBoardKey(value: unknown): value is BoardKey {
  return value === "weekly" || value === "monthly";
}

/** Points earned this round: the positive side of R50 only. */
export function pointsEarned(record: RoundRecord, seat: number): number {
  return Math.max(0, record.result?.points[seat] ?? 0);
}

/** How much of a round's points may post, given what private rooms already posted today. */
export function allowedPoints(points: number, mode: string, privateToday: number): number {
  if (mode !== "private") return points;
  return Math.max(0, Math.min(points, PRIVATE_POINTS_DAILY_CAP - privateToday));
}

/**
 * The typings spell the sort order and operator as ambient const enums, which have no runtime
 * value in the bundle; Nakama takes the plain strings.
 */
type LeaderboardCreate = (
  id: string,
  authoritative: boolean,
  sortOrder: string,
  operator: string,
  resetSchedule: string | null,
  metadata: Record<string, unknown>,
  enableRank: boolean,
) => void;

export function ensureLeaderboards(nk: nkruntime.Nakama, logger: nkruntime.Logger): void {
  const create = nk.leaderboardCreate.bind(nk) as unknown as LeaderboardCreate;
  for (const board of Object.values(BOARDS)) {
    try {
      create(board.id, true, "descending", "increment", board.reset, {}, true);
    } catch (error: unknown) {
      log.warn(logger, "leaderboard not created", { id: board.id, error: String(error) });
    }
  }
}

/** Posts a seat's points to both boards; the private-room cap lives in the player's progress. */
export function postRoundPoints(
  nk: nkruntime.Nakama,
  logger: nkruntime.Logger,
  record: RoundRecord,
  userId: string,
  seat: number,
): number {
  const points = pointsEarned(record, seat);
  if (points === 0) return 0;
  const store = nkProgressStore(nk);
  const progress: Progress = forDay(store.read(userId), record.endedTs);
  const used = progress.privatePointsToday ?? 0;
  const allowed = allowedPoints(points, record.mode, used);
  if (allowed === 0) return 0;
  if (record.mode === "private") {
    store.write(userId, {
      ...progress,
      privatePointsToday: used + allowed,
      updatedTs: record.endedTs,
    });
  }
  for (const board of Object.values(BOARDS)) {
    try {
      nk.leaderboardRecordWrite(board.id, userId, undefined, allowed, 0, {
        tableId: record.tableId,
        roundId: record.roundId,
      });
    } catch (error: unknown) {
      log.warn(logger, "points not posted", { board: board.id, error: String(error) });
    }
  }
  return allowed;
}

export interface BoardRow {
  readonly rank: number;
  readonly userId: string;
  readonly username: string;
  readonly score: number;
  readonly rounds: number;
}

export interface BoardView {
  readonly board: BoardKey;
  readonly scope: "global" | "friends";
  readonly rows: BoardRow[];
  readonly me: BoardRow | null;
  readonly nextCursor: string | null;
  readonly nextResetTs: number | null;
}

/**
 * The names a board shows: the profile's display name, else the account's, else the username.
 * Records are written without a name, so they are looked up for each page in one read.
 */
export function playerNames(nk: nkruntime.Nakama, ids: readonly string[]): Map<string, string> {
  const names = new Map<string, string>();
  const unique = [...new Set(ids)].filter((id) => id !== "");
  if (unique.length === 0) return names;
  try {
    const objects = nk.storageRead(
      unique.map((userId) => ({ collection: PROFILES_COLLECTION, key: PROFILE_KEY, userId })),
    );
    for (const object of objects) {
      const name = (object.value as { displayName?: unknown }).displayName;
      if (typeof name === "string" && name.trim() !== "") names.set(object.userId, name);
    }
  } catch {
    // Profiles unreadable: fall through to the accounts.
  }
  const missing = unique.filter((id) => !names.has(id));
  if (missing.length > 0) {
    try {
      for (const user of nk.usersGetId(missing)) {
        const name = user.displayName !== "" ? user.displayName : user.username;
        if (name !== "") names.set(user.userId, name);
      }
    } catch {
      // Accounts unreadable: the record's own username stays.
    }
  }
  return names;
}

const toRow = (r: nkruntime.LeaderboardRecord, names?: Map<string, string>): BoardRow => ({
  rank: r.rank,
  userId: r.ownerId,
  username: names?.get(r.ownerId) ?? r.username,
  score: r.score,
  rounds: r.numScore,
});

export type BoardResponse =
  ({ readonly ok: true } & BoardView) | { readonly ok: false; readonly error: RuleError };

/** Global pages, or the caller's friends (and the caller) on the same board (GDD 12.8). */
export function boardView(
  nk: nkruntime.Nakama,
  userId: string,
  board: BoardKey,
  scope: "global" | "friends",
  friendIds: readonly string[],
  limit: number,
  cursor: string | undefined,
): BoardResponse {
  const id = BOARDS[board].id;
  const owners = scope === "friends" ? [userId, ...friendIds].slice(0, 100) : undefined;
  let list: nkruntime.LeaderboardRecordList;
  try {
    list = nk.leaderboardRecordsList(id, owners, Math.min(Math.max(limit, 1), BOARD_PAGE), cursor);
  } catch (error: unknown) {
    return {
      ok: false,
      error: ruleError(ErrorCodes.content.packUnknown, { reason: String(error) }),
    };
  }
  // Nakama keeps the page of top records in `records` and the asked-for owners in `ownerRecords`,
  // so the friends view reads the latter and the global view the former.
  const source = scope === "friends" ? (list.ownerRecords ?? []) : (list.records ?? []);
  const mine =
    (list.ownerRecords ?? []).find((r) => r.ownerId === userId) ??
    (list.records ?? []).find((r) => r.ownerId === userId);
  const names = playerNames(nk, [...source.map((r) => r.ownerId), userId]);
  const rows = source
    .map((r) => toRow(r, names))
    .sort((a, b) => b.score - a.score || a.rank - b.rank);
  const nextResetTs = nextResetOf(nk, id);
  return {
    ok: true,
    board,
    scope,
    rows,
    me: mine === undefined ? null : toRow(mine, names),
    nextCursor: list.nextCursor ?? null,
    nextResetTs,
  };
}

function nextResetOf(nk: nkruntime.Nakama, id: string): number | null {
  try {
    const meta = nk.leaderboardsGetId([id])[0];
    return meta === undefined || meta.nextReset === 0 ? null : meta.nextReset * 1000;
  } catch {
    return null;
  }
}

export function gemsForRank(rank: number): number {
  for (const bracket of BRACKET_GEMS) {
    if (rank <= bracket.upToRank) return bracket.gems;
  }
  return 0;
}

/**
 * Registered with `registerLeaderboardReset`: pays the ended period's top ten through the ledger,
 * keyed by board and period so a second firing changes nothing.
 */
export function payTopBrackets(
  nk: nkruntime.Nakama,
  ledger: LedgerPorts,
  leaderboardId: string,
  resetTs: number,
): { readonly paid: number } {
  const list = nk.leaderboardRecordsList(leaderboardId, undefined, 10, undefined, resetTs);
  let paid = 0;
  for (const record of list.records ?? []) {
    const gems = gemsForRank(record.rank);
    if (gems === 0) continue;
    const done = credit(ledger, {
      userId: record.ownerId,
      sourceId: `board:${leaderboardId}:${String(resetTs)}`,
      reason: "leaderboard",
      gems,
      metadata: { rank: record.rank, score: record.score },
    });
    if (done.ok && done.applied) paid += 1;
  }
  return { paid };
}
