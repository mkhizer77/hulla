import { isProvisional, placesFromFinish, rateRound, type RatedEntry } from "@hulla/rules-engine";

import {
  BUNDLED_RANKED,
  type League,
  type LeagueReward,
  type RankedConfig,
} from "../content/ranked";
import { log } from "../infra/log";

import { grant as grantItem, nkCatalogStore } from "./catalog";
import { nkLedgerPorts, type LedgerPorts } from "./ledger";
import { nkProgressStore } from "./progression";
import { nkEntitlementStore } from "./purchases";

import type { RoundRecord } from "../match/types";

/**
 * Ranked seasons (GDD 7.5, 12.9, R53): the rating moves by the engine's pairwise Elo after each
 * ranked round; ten placement rounds hide the league; a promotion is protected for three rounds;
 * Ustad is Diamond inside the top 500 of the season board. Seasons run eight weeks from an anchor
 * in content, and a player's first ranked call or round after a season ends pays that season's
 * league reward once and carries half the distance from the start rating into the next season.
 */
export const RANKED_COLLECTION = "ranked";
const WEEK_MS = 7 * 24 * 60 * 60 * 1000;
const HISTORY_KEPT = 12;

export interface SeasonInfo {
  readonly id: string;
  readonly number: number;
  readonly startTs: number;
  readonly endTs: number;
}

export interface SeasonSummary {
  readonly seasonId: string;
  /** Null when the player did not finish placement. */
  readonly league: string | null;
  readonly rating: number;
  readonly rank: number | null;
  readonly reward: LeagueReward | null;
}

export interface RankedState {
  readonly seasonId: string;
  readonly rating: number;
  /** Rated rounds ever played; R53 doubles K below ten. */
  readonly ratedRounds: number;
  readonly seasonRounds: number;
  /** The league held, null during placement. Ustad is shown from the board rank, never stored. */
  readonly league: string | null;
  readonly peakLeague: string | null;
  /** Rounds left in which a fall below the league's floor does not demote. */
  readonly protectedRounds: number;
  readonly abandons: number;
  /** `${tableId}:${roundId}` of the last round applied, so a retried write changes nothing. */
  readonly lastRound: string | null;
  readonly history: readonly SeasonSummary[];
  readonly updatedTs: number;
}

export function seasonAt(config: RankedConfig, now: number): SeasonInfo {
  const length = config.season.weeks * WEEK_MS;
  const number = Math.max(1, Math.floor((now - config.season.anchorTs) / length) + 1);
  const startTs = config.season.anchorTs + (number - 1) * length;
  return { id: `s${String(number)}`, number, startTs, endTs: startTs + length };
}

export function boardIdFor(seasonId: string): string {
  return `ranked_${seasonId}`;
}

export function newState(config: RankedConfig, seasonId: string, now: number): RankedState {
  return {
    seasonId,
    rating: config.startRating,
    ratedRounds: 0,
    seasonRounds: 0,
    league: null,
    peakLeague: null,
    protectedRounds: 0,
    abandons: 0,
    lastRound: null,
    history: [],
    updatedTs: now,
  };
}

export function isPlaced(state: RankedState, config: RankedConfig): boolean {
  return state.seasonRounds >= config.placementRounds;
}

const ladder = (config: RankedConfig): readonly League[] =>
  config.leagues.filter((l) => l.topOnly !== true);

function indexOf(config: RankedConfig, id: string | null): number {
  return id === null ? -1 : config.leagues.findIndex((l) => l.id === id);
}

/** The highest league the rating reaches, ignoring the top-only one. */
export function leagueFor(config: RankedConfig, rating: number): string {
  const reached = ladder(config).filter((l) => l.minRating <= rating);
  return (reached[reached.length - 1] ?? ladder(config)[0] ?? config.leagues[0])?.id ?? "bronze";
}

/** What a player shows: the stored league, or the top-only league inside the board's top places. */
export function displayLeague(
  state: RankedState,
  config: RankedConfig,
  rank: number | null,
): string | null {
  if (state.league === null) return null;
  const top = config.leagues.find((l) => l.topOnly === true);
  const highest = ladder(config)[ladder(config).length - 1];
  if (
    top !== undefined &&
    state.league === highest?.id &&
    rank !== null &&
    rank <= config.ustadTop &&
    state.rating >= top.minRating
  ) {
    return top.id;
  }
  return state.league;
}

/** One rated round's change to the player's own state (placement, promotion, protection). */
export function afterRound(
  state: RankedState,
  config: RankedConfig,
  rating: number,
  roundKey: string,
  abandoned: boolean,
  now: number,
): RankedState {
  const counted: RankedState = {
    ...state,
    rating,
    ratedRounds: state.ratedRounds + 1,
    seasonRounds: state.seasonRounds + 1,
    abandons: state.abandons + (abandoned ? 1 : 0),
    lastRound: roundKey,
    updatedTs: now,
  };
  if (!isPlaced(counted, config)) return { ...counted, league: null };
  const target = leagueFor(config, rating);
  if (state.league === null) {
    return { ...counted, league: target, peakLeague: higher(config, state.peakLeague, target) };
  }
  const from = indexOf(config, state.league);
  const to = indexOf(config, target);
  if (to > from) {
    return {
      ...counted,
      league: target,
      peakLeague: higher(config, state.peakLeague, target),
      protectedRounds: config.protectionRounds,
    };
  }
  const protectedRounds = Math.max(0, state.protectedRounds - 1);
  if (to < from && state.protectedRounds > 0) return { ...counted, protectedRounds };
  return { ...counted, league: target, protectedRounds };
}

function higher(config: RankedConfig, a: string | null, b: string): string {
  return indexOf(config, a) >= indexOf(config, b) ? (a ?? b) : b;
}

export interface RolloverResult {
  readonly state: RankedState;
  /** The ended season's summary when one was settled now. */
  readonly settled: SeasonSummary | null;
}

/**
 * Moves a state from an ended season into `season`: the ended season's league (with Ustad read
 * from its board rank) earns its reward if placement was finished, and the rating keeps
 * `softReset` of its distance from the start.
 */
export function rollover(
  state: RankedState,
  config: RankedConfig,
  season: SeasonInfo,
  endedRank: number | null,
  now: number,
): RolloverResult {
  if (state.seasonId === season.id) return { state, settled: null };
  const league = isPlaced(state, config) ? displayLeague(state, config, endedRank) : null;
  const reward =
    league === null ? null : (config.leagues.find((l) => l.id === league)?.reward ?? null);
  const settled: SeasonSummary = {
    seasonId: state.seasonId,
    league,
    rating: state.rating,
    rank: league === null ? null : endedRank,
    reward,
  };
  const carried = Math.round(
    config.startRating + (state.rating - config.startRating) * config.softReset,
  );
  return {
    state: {
      ...newState(config, season.id, now),
      rating: carried,
      ratedRounds: state.ratedRounds,
      abandons: state.abandons,
      history: [settled, ...state.history].slice(0, HISTORY_KEPT),
    },
    settled,
  };
}

export interface RankedSeat {
  readonly userId: string;
  readonly seat: number;
  readonly state: RankedState;
  readonly abandoned: boolean;
}

export interface RankedOutcome {
  readonly userId: string;
  readonly before: number;
  readonly after: number;
  readonly delta: number;
  readonly state: RankedState;
}

/**
 * R53 over one ranked round; R46's abandoner (absent past the limit and gone at the end) places
 * below the Bhabhi and loses `abandonPenalty` more.
 */
export function rateRankedRound(
  config: RankedConfig,
  record: RoundRecord,
  seats: readonly RankedSeat[],
): RankedOutcome[] {
  const result = record.result;
  if (result === null) return [];
  const seatCount = record.players.length;
  const places = placesFromFinish(result.finishOrder, seatCount);
  const roundKey = `${record.tableId}:${record.roundId}`;
  const entries: RatedEntry[] = seats.map((s) => ({
    id: s.userId,
    rating: s.state.rating,
    rounds: s.state.ratedRounds,
    place: s.abandoned ? seatCount : (places[s.seat] ?? seatCount),
  }));
  const changes = rateRound(entries);
  return seats.map((s, index) => {
    const change = changes[index];
    const before = s.state.rating;
    const after = Math.max(
      0,
      (change?.after ?? before) - (s.abandoned ? config.abandonPenalty : 0),
    );
    return {
      userId: s.userId,
      before,
      after,
      delta: after - before,
      state: afterRound(s.state, config, after, roundKey, s.abandoned, record.endedTs),
    };
  });
}

/** A seat abandoned the round when it was gone at the end after more than the allowed absence. */
export function abandonedSeat(record: RoundRecord, seat: number, config: RankedConfig): boolean {
  const present = record.presentAtEnd?.[seat] ?? true;
  const absent = record.absentMs?.[seat] ?? 0;
  return !present && absent > config.abandonAfterMs;
}

/* ------------------------------------------------------------------ Nakama */

export interface RankedStore {
  read(userId: string): RankedState | null;
  write(userId: string, state: RankedState): void;
}

function isState(value: unknown): value is RankedState {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v["seasonId"] === "string" &&
    typeof v["rating"] === "number" &&
    typeof v["seasonRounds"] === "number" &&
    Array.isArray(v["history"])
  );
}

export function nkRankedStore(nk: nkruntime.Nakama): RankedStore {
  return {
    read(userId) {
      const objects = nk.storageRead([{ collection: RANKED_COLLECTION, key: "state", userId }]);
      const value: unknown = objects[0]?.value;
      return isState(value) ? value : null;
    },
    write(userId, state) {
      nk.storageWrite([
        {
          collection: RANKED_COLLECTION,
          key: "state",
          userId,
          value: state,
          permissionRead: 1,
          permissionWrite: 0,
        },
      ]);
    },
  };
}

type LeaderboardCreate = (
  id: string,
  authoritative: boolean,
  sortOrder: string,
  operator: string,
  resetSchedule: string | null,
  metadata: Record<string, unknown>,
  enableRank: boolean,
) => void;

function ensureBoard(nk: nkruntime.Nakama, seasonId: string): void {
  const create = nk.leaderboardCreate.bind(nk) as unknown as LeaderboardCreate;
  create(boardIdFor(seasonId), true, "descending", "set", null, { seasonId }, true);
}

/** The player's place on a season's board, or null when they have no record there. */
export function rankOn(nk: nkruntime.Nakama, seasonId: string, userId: string): number | null {
  try {
    const list = nk.leaderboardRecordsList(boardIdFor(seasonId), [userId], 1);
    const mine = (list.ownerRecords ?? []).find((r) => r.ownerId === userId);
    return mine === undefined || mine.rank <= 0 ? null : mine.rank;
  } catch {
    return null;
  }
}

function postRating(
  nk: nkruntime.Nakama,
  logger: nkruntime.Logger,
  state: RankedState,
  userId: string,
): void {
  const write = (): void => {
    nk.leaderboardRecordWrite(boardIdFor(state.seasonId), userId, undefined, state.rating, 0, {
      league: state.league,
    });
  };
  try {
    write();
  } catch {
    try {
      ensureBoard(nk, state.seasonId);
      write();
    } catch (error: unknown) {
      log.warn(logger, "ranked rating not posted", { error: String(error) });
    }
  }
}

export interface SeasonRewardPorts {
  readonly ledger: LedgerPorts;
  grantItem(userId: string, itemId: string, now: number): void;
  grantTitle(userId: string, title: string, now: number): void;
  grantFrame(userId: string, frame: string, now: number): void;
}

/** Pays a settled season's league reward; the ledger key is the season, so it pays once. */
export function paySeasonReward(
  ports: SeasonRewardPorts,
  userId: string,
  summary: SeasonSummary,
  now: number,
): boolean {
  const reward = summary.reward;
  if (reward === null || summary.league === null) return false;
  const sourceId = `ranked:${summary.seasonId}:reward`;
  if (!ports.ledger.claim(userId, sourceId)) return false;
  const coins = reward.coins ?? 0;
  if (coins > 0) {
    ports.ledger.update(
      userId,
      { coins },
      { reason: "ranked_season", sourceId, season: summary.seasonId, league: summary.league },
    );
  }
  for (const itemId of reward.items ?? []) ports.grantItem(userId, itemId, now);
  ports.grantTitle(userId, reward.title, now);
  if (reward.frame !== undefined) ports.grantFrame(userId, reward.frame, now);
  return true;
}

export function nkSeasonRewardPorts(nk: nkruntime.Nakama): SeasonRewardPorts {
  return {
    ledger: nkLedgerPorts(nk),
    grantItem(userId, itemId, now) {
      const store = nkCatalogStore(nk);
      const inventory = store.inventory(userId);
      const next = grantItem(inventory, itemId, now);
      if (next !== inventory) store.writeInventory(userId, next);
    },
    grantTitle(userId, title, now) {
      const store = nkProgressStore(nk);
      const progress = store.read(userId);
      if (progress.titles.includes(title)) return;
      store.write(userId, { ...progress, titles: [...progress.titles, title], updatedTs: now });
    },
    grantFrame(userId, frame, now) {
      const store = nkEntitlementStore(nk);
      const current = store.read(userId);
      if (current.frames.includes(frame)) return;
      store.write(userId, { ...current, frames: [...current.frames, frame], updatedTs: now });
    },
  };
}

/**
 * The player's state on the season running at `now`: a first visit starts it, and a state from
 * an ended season is settled (reward paid once) and carried over before anything else reads it.
 */
export function currentState(
  nk: nkruntime.Nakama,
  logger: nkruntime.Logger,
  userId: string,
  now: number,
  config: RankedConfig = BUNDLED_RANKED,
): RankedState {
  const store = nkRankedStore(nk);
  const season = seasonAt(config, now);
  const stored = store.read(userId);
  if (stored === null) return newState(config, season.id, now);
  if (stored.seasonId === season.id) return stored;
  const rolled = rollover(stored, config, season, rankOn(nk, stored.seasonId, userId), now);
  if (rolled.settled !== null) {
    try {
      paySeasonReward(nkSeasonRewardPorts(nk), userId, rolled.settled, now);
    } catch (error: unknown) {
      log.warn(logger, "ranked season reward not paid", { error: String(error) });
    }
  }
  store.write(userId, rolled.state);
  return rolled.state;
}

/**
 * Rates a completed ranked round for every human seat and posts the placed players' ratings to
 * the season board. A seat whose state already carries this round is left as it is.
 */
export function applyRankedRound(
  nk: nkruntime.Nakama,
  logger: nkruntime.Logger,
  record: RoundRecord,
  config: RankedConfig = BUNDLED_RANKED,
): RankedOutcome[] {
  if (record.mode !== "ranked" || !record.completed || record.result === null) return [];
  const roundKey = `${record.tableId}:${record.roundId}`;
  const seats: RankedSeat[] = [];
  record.players.forEach((userId, seat) => {
    if (userId === null) return;
    seats.push({
      userId,
      seat,
      state: currentState(nk, logger, userId, record.endedTs, config),
      abandoned: abandonedSeat(record, seat, config),
    });
  });
  const outcomes = rateRankedRound(config, record, seats);
  const store = nkRankedStore(nk);
  for (const [index, outcome] of outcomes.entries()) {
    if (seats[index]?.state.lastRound === roundKey) continue;
    store.write(outcome.userId, outcome.state);
    if (isPlaced(outcome.state, config)) postRating(nk, logger, outcome.state, outcome.userId);
  }
  return outcomes;
}

export interface RankedView {
  readonly season: SeasonInfo;
  readonly rating: number;
  readonly league: string | null;
  readonly peakLeague: string | null;
  readonly rank: number | null;
  readonly placement: { readonly played: number; readonly of: number };
  readonly protectedRounds: number;
  readonly provisional: boolean;
  readonly leagues: readonly Pick<League, "id" | "name" | "minRating" | "topOnly" | "reward">[];
  readonly matchmaking: RankedConfig["matchmaking"];
  readonly seats: number;
  readonly lastSeason: SeasonSummary | null;
}

export function rankedView(
  state: RankedState,
  config: RankedConfig,
  season: SeasonInfo,
  rank: number | null,
): RankedView {
  return {
    season,
    rating: state.rating,
    league: displayLeague(state, config, rank),
    peakLeague: state.peakLeague,
    rank: isPlaced(state, config) ? rank : null,
    placement: {
      played: Math.min(state.seasonRounds, config.placementRounds),
      of: config.placementRounds,
    },
    protectedRounds: state.protectedRounds,
    provisional: isProvisional(state.ratedRounds),
    leagues: config.leagues.map((l) => ({
      id: l.id,
      name: l.name,
      minRating: l.minRating,
      ...(l.topOnly === true ? { topOnly: true } : {}),
      reward: l.reward,
    })),
    matchmaking: config.matchmaking,
    seats: config.seats,
    lastSeason: state.history[0] ?? null,
  };
}

export interface RankedBoardRow {
  readonly rank: number;
  readonly userId: string;
  readonly username: string;
  readonly rating: number;
  readonly league: string | null;
}

/** The current season's top places, with the league each shows. */
export function rankedBoard(
  nk: nkruntime.Nakama,
  config: RankedConfig,
  season: SeasonInfo,
  limit: number,
): RankedBoardRow[] {
  let list: nkruntime.LeaderboardRecordList;
  try {
    list = nk.leaderboardRecordsList(boardIdFor(season.id), undefined, limit);
  } catch {
    return [];
  }
  return (list.records ?? []).map((r) => {
    const stored = (r.metadata as { league?: unknown } | undefined)?.league;
    const league = typeof stored === "string" ? stored : leagueFor(config, r.score);
    const shown = displayLeague(
      { ...newState(config, season.id, 0), rating: r.score, league },
      config,
      r.rank,
    );
    return {
      rank: r.rank,
      userId: r.ownerId,
      username: r.username,
      rating: r.score,
      league: shown,
    };
  });
}
