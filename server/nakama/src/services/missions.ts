import {
  ErrorCodes,
  ruleError,
  sha256,
  stateFromSeed,
  utf8Encode,
  Xoshiro128StarStar,
  type RuleError,
} from "@hulla/shared-ts";

import {
  BUNDLED_MISSIONS,
  type MissionDef,
  type MissionMetric,
  type MissionPool,
} from "../content/missions";

import { credit, type LedgerPorts } from "./ledger";
import { localDay } from "./progression";

import type { RoundRecord } from "../match/types";

/**
 * Missions (GDD 12.5): three daily missions drawn per player at local midnight, five weekly ones
 * per week, progress from round events on the server, one free re-roll a day, rewards through
 * the ledger. The pool comes from content and can be replaced live.
 */
export const MISSIONS_COLLECTION = "missions";
export const MISSION_POOL_COLLECTION = "content_missions";
export const SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000";
export const DAILY_COUNT = 3;
export const WEEKLY_COUNT = 5;
export const LOW_PICKUP_LIMIT = 5;
/** In-app notification code for a progress change (positive codes are the module's own). */
export const MISSIONS_NOTIFICATION_CODE = 1001;

export interface MissionState {
  readonly id: string;
  readonly progress: number;
  readonly claimed: boolean;
}

export interface MissionsRecord {
  readonly day: string;
  readonly week: string;
  readonly daily: readonly MissionState[];
  readonly weekly: readonly MissionState[];
  readonly rerollDay: string;
  readonly tzOffsetMinutes: number;
  readonly updatedTs: number;
}

export const EMPTY_MISSIONS: MissionsRecord = {
  day: "",
  week: "",
  daily: [],
  weekly: [],
  rerollDay: "",
  tzOffsetMinutes: 300,
  updatedTs: 0,
};

/** The Monday that starts the player's local week, as `YYYY-MM-DD`. */
export function localWeek(ts: number, tzOffsetMinutes: number): string {
  const shifted = new Date(ts + tzOffsetMinutes * 60_000);
  const weekday = (shifted.getUTCDay() + 6) % 7;
  const monday = new Date(shifted.getTime() - weekday * 24 * 60 * 60 * 1000);
  return localDay(monday.getTime() - tzOffsetMinutes * 60_000, tzOffsetMinutes);
}

function shuffled<T>(items: readonly T[], seedText: string): T[] {
  const rng = Xoshiro128StarStar.fromState(stateFromSeed(sha256(utf8Encode(seedText))));
  const out = [...items];
  for (let i = out.length - 1; i > 0; i -= 1) {
    const j = rng.nextInt(i + 1);
    const a = out[i];
    const b = out[j];
    if (a !== undefined && b !== undefined) {
      out[i] = b;
      out[j] = a;
    }
  }
  return out;
}

/** A player's draw for a period is a seeded shuffle of the pool, so every device agrees. */
export function draw(pool: readonly MissionDef[], count: number, seedText: string): MissionDef[] {
  return shuffled(pool, seedText).slice(0, count);
}

/** The record for now: a new day draws new dailies, a new week new weeklies. */
export function forPeriod(
  record: MissionsRecord,
  pool: MissionPool,
  userId: string,
  now: number,
): MissionsRecord {
  const day = localDay(now, record.tzOffsetMinutes);
  const week = localWeek(now, record.tzOffsetMinutes);
  let next = record;
  if (record.day !== day) {
    next = {
      ...next,
      day,
      daily: draw(pool.daily, DAILY_COUNT, `${userId}:d:${day}`).map((m) => ({
        id: m.id,
        progress: 0,
        claimed: false,
      })),
    };
  }
  if (record.week !== week) {
    next = {
      ...next,
      week,
      weekly: draw(pool.weekly, WEEKLY_COUNT, `${userId}:w:${week}`).map((m) => ({
        id: m.id,
        progress: 0,
        claimed: false,
      })),
    };
  }
  return next;
}

export type Metrics = Partial<Record<MissionMetric, number>>;

/** What one seat of a completed round counts for. */
export function metricsFromRound(record: RoundRecord, seat: number, withFriend: boolean): Metrics {
  const result = record.result;
  if (result === null || !record.completed) return {};
  const h = record.highlights[seat] ?? {};
  const escaped = result.loserSeat !== seat && result.finishOrder.includes(seat);
  const pickups = h["cardsPickedUp"] ?? 0;
  const metrics: Metrics = {
    rounds_played: 1,
    rounds_with_friend: withFriend ? 1 : 0,
    escapes: escaped ? 1 : 0,
    escaped_first: result.finishOrder[0] === seat ? 1 : 0,
    thullas_given: h["thullasGiven"] ?? 0,
    clean_rounds: escaped && pickups === 0 ? 1 : 0,
    low_pickup_rounds: pickups < LOW_PICKUP_LIMIT ? 1 : 0,
    hands_taken: h["handsTaken"] ?? 0,
    series_won: seriesWon(record, seat) ? 1 : 0,
  };
  return metrics;
}

/** A finished series is won by the seat with the most points (GDD 6.12). */
export function seriesWon(record: RoundRecord, seat: number): boolean {
  const series = record.series;
  if (!series?.finished) return false;
  const mine = series.points[seat] ?? 0;
  return series.points.every((p, i) => i === seat || p < mine);
}

function bump(
  states: readonly MissionState[],
  defs: readonly MissionDef[],
  metrics: Metrics,
): { states: MissionState[]; changed: boolean } {
  let changed = false;
  const states2 = states.map((s) => {
    const def = defs.find((d) => d.id === s.id);
    if (def === undefined || s.claimed || s.progress >= def.target) return s;
    const delta = metrics[def.metric] ?? 0;
    if (delta <= 0) return s;
    changed = true;
    return { ...s, progress: Math.min(def.target, s.progress + delta) };
  });
  return { states: states2, changed };
}

export function applyMetrics(
  record: MissionsRecord,
  pool: MissionPool,
  metrics: Metrics,
  now: number,
): { record: MissionsRecord; changed: boolean } {
  const daily = bump(record.daily, pool.daily, metrics);
  const weekly = bump(record.weekly, pool.weekly, metrics);
  const changed = daily.changed || weekly.changed;
  return {
    record: changed
      ? { ...record, daily: daily.states, weekly: weekly.states, updatedTs: now }
      : record,
    changed,
  };
}

export interface MissionView {
  readonly id: string;
  readonly name: string;
  readonly description: string;
  readonly metric: MissionMetric;
  readonly target: number;
  readonly progress: number;
  readonly claimed: boolean;
  readonly claimable: boolean;
  readonly reward: MissionDef["reward"];
}

function views(states: readonly MissionState[], defs: readonly MissionDef[]): MissionView[] {
  return states.flatMap((s) => {
    const def = defs.find((d) => d.id === s.id);
    if (def === undefined) return [];
    return [
      {
        id: def.id,
        name: def.name,
        description: def.description,
        metric: def.metric,
        target: def.target,
        progress: s.progress,
        claimed: s.claimed,
        claimable: !s.claimed && s.progress >= def.target,
        reward: def.reward,
      },
    ];
  });
}

export function missionsView(
  record: MissionsRecord,
  pool: MissionPool,
): {
  readonly day: string;
  readonly week: string;
  readonly daily: MissionView[];
  readonly weekly: MissionView[];
  readonly rerollAvailable: boolean;
} {
  return {
    day: record.day,
    week: record.week,
    daily: views(record.daily, pool.daily),
    weekly: views(record.weekly, pool.weekly),
    rerollAvailable: record.rerollDay !== record.day,
  };
}

export type ClaimResponse =
  | { readonly ok: true; readonly applied: boolean; readonly reward: MissionDef["reward"] }
  | { readonly ok: false; readonly error: RuleError };

/** Pays a finished mission once; the ledger key is the period and the mission. */
export function claim(
  record: MissionsRecord,
  pool: MissionPool,
  ledger: LedgerPorts,
  userId: string,
  missionId: string,
  now: number,
): { record: MissionsRecord; response: ClaimResponse } {
  const inDaily = record.daily.find((s) => s.id === missionId);
  const inWeekly = record.weekly.find((s) => s.id === missionId);
  const state = inDaily ?? inWeekly;
  const def = (inDaily === undefined ? pool.weekly : pool.daily).find((d) => d.id === missionId);
  if (state === undefined || def === undefined) {
    return {
      record,
      response: {
        ok: false,
        error: ruleError(ErrorCodes.table.invalidPayload, { reason: "mission_id" }),
      },
    };
  }
  if (state.progress < def.target) {
    return {
      record,
      response: {
        ok: false,
        error: ruleError(ErrorCodes.economy.alreadyGranted, { reason: "incomplete" }),
      },
    };
  }
  const period = inDaily === undefined ? `w:${record.week}` : `d:${record.day}`;
  const paid = credit(ledger, {
    userId,
    sourceId: `mission:${period}:${missionId}`,
    reason: "mission",
    ...(def.reward.coins === undefined ? {} : { coins: def.reward.coins }),
    ...(def.reward.gems === undefined ? {} : { gems: def.reward.gems }),
    metadata: { missionId },
  });
  if (!paid.ok) return { record, response: paid };
  const mark = (states: readonly MissionState[]): MissionState[] =>
    states.map((s) => (s.id === missionId ? { ...s, claimed: true } : s));
  const next: MissionsRecord = {
    ...record,
    daily: inDaily === undefined ? record.daily : mark(record.daily),
    weekly: inWeekly === undefined ? record.weekly : mark(record.weekly),
    updatedTs: now,
  };
  return { record: next, response: { ok: true, applied: paid.applied, reward: def.reward } };
}

export type RerollResponse =
  | {
      readonly ok: true;
      readonly replaced: string;
      readonly mission: MissionView;
      /** True when an extra re-roll from a rewarded ad paid for it (GDD 13.6). */
      readonly usedCredit: boolean;
    }
  | { readonly ok: false; readonly error: RuleError };

/**
 * Swaps one unclaimed daily mission for the next unassigned one in the player's draw, once a day
 * for free, then once per re-roll credit earned from a rewarded ad.
 */
export function reroll(
  record: MissionsRecord,
  pool: MissionPool,
  userId: string,
  missionId: string,
  now: number,
  credits = 0,
): { record: MissionsRecord; response: RerollResponse } {
  const usedCredit = record.rerollDay === record.day;
  if (usedCredit && credits <= 0) {
    return {
      record,
      response: {
        ok: false,
        error: ruleError(ErrorCodes.economy.dailyCapReached, { reason: "reroll" }),
      },
    };
  }
  const index = record.daily.findIndex((s) => s.id === missionId);
  const current = record.daily[index];
  if (current === undefined || current.claimed) {
    return {
      record,
      response: {
        ok: false,
        error: ruleError(ErrorCodes.table.invalidPayload, { reason: "mission_id" }),
      },
    };
  }
  const assigned = new Set(record.daily.map((s) => s.id));
  const replacement = shuffled(pool.daily, `${userId}:d:${record.day}`).find(
    (m) => !assigned.has(m.id),
  );
  if (replacement === undefined) {
    return {
      record,
      response: { ok: false, error: ruleError(ErrorCodes.content.packUnknown, { reason: "pool" }) },
    };
  }
  const daily = record.daily.map((s, i) =>
    i === index ? { id: replacement.id, progress: 0, claimed: false } : s,
  );
  const next: MissionsRecord = { ...record, daily, rerollDay: record.day, updatedTs: now };
  const view = views([{ id: replacement.id, progress: 0, claimed: false }], pool.daily)[0];
  if (view === undefined) {
    return {
      record,
      response: { ok: false, error: ruleError(ErrorCodes.content.packUnknown, { reason: "pool" }) },
    };
  }
  return { record: next, response: { ok: true, replaced: missionId, mission: view, usedCredit } };
}

export function isMissionPool(value: unknown): value is MissionPool {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  const ok = (list: unknown, min: number): boolean =>
    Array.isArray(list) &&
    list.length >= min &&
    list.every((m: unknown) => {
      if (typeof m !== "object" || m === null) return false;
      const d = m as Record<string, unknown>;
      return (
        typeof d["id"] === "string" &&
        typeof d["metric"] === "string" &&
        typeof d["target"] === "number"
      );
    });
  return (
    typeof v["id"] === "string" &&
    typeof v["version"] === "number" &&
    ok(v["daily"], DAILY_COUNT) &&
    ok(v["weekly"], WEEKLY_COUNT)
  );
}

export interface MissionsStore {
  read(userId: string): MissionsRecord;
  write(userId: string, record: MissionsRecord): void;
  pool(): MissionPool;
  writePool(pool: MissionPool): void;
}

function isRecord(value: unknown): value is MissionsRecord {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v["day"] === "string" && Array.isArray(v["daily"]) && Array.isArray(v["weekly"]);
}

export function nkMissionsStore(nk: nkruntime.Nakama): MissionsStore {
  return {
    read(userId) {
      const objects = nk.storageRead([{ collection: MISSIONS_COLLECTION, key: "state", userId }]);
      const value: unknown = objects[0]?.value;
      return isRecord(value) ? { ...EMPTY_MISSIONS, ...value } : EMPTY_MISSIONS;
    },
    write(userId, record) {
      nk.storageWrite([
        {
          collection: MISSIONS_COLLECTION,
          key: "state",
          userId,
          value: record,
          permissionRead: 1,
          permissionWrite: 0,
        },
      ]);
    },
    pool() {
      try {
        const objects = nk.storageRead([
          { collection: MISSION_POOL_COLLECTION, key: "pool", userId: SYSTEM_USER_ID },
        ]);
        const value: unknown = objects[0]?.value;
        return isMissionPool(value) ? value : BUNDLED_MISSIONS;
      } catch {
        return BUNDLED_MISSIONS;
      }
    },
    writePool(pool) {
      nk.storageWrite([
        {
          collection: MISSION_POOL_COLLECTION,
          key: "pool",
          userId: SYSTEM_USER_ID,
          value: { id: pool.id, version: pool.version, daily: pool.daily, weekly: pool.weekly },
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
  };
}

/** Records metrics for a player and tells their devices when something moved (GDD 14.10). */
export function recordMissionMetrics(
  nk: nkruntime.Nakama,
  userId: string,
  metrics: Metrics,
  now: number,
): boolean {
  const store = nkMissionsStore(nk);
  const pool = store.pool();
  const current = forPeriod(store.read(userId), pool, userId, now);
  const applied = applyMetrics(current, pool, metrics, now);
  if (applied.record !== current || current !== store.read(userId))
    store.write(userId, applied.record);
  if (applied.changed) {
    try {
      nk.notificationSend(
        userId,
        "missions_progress",
        missionsView(applied.record, pool),
        MISSIONS_NOTIFICATION_CODE,
        null,
        false,
      );
    } catch {
      // a closed socket drops the notice; the next missions_get shows the same state
    }
  }
  return applied.changed;
}
