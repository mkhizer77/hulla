import {
  BUNDLED_DAILY_REWARDS,
  type DailyRewardCalendar,
  type DailyRewardDay,
} from "../content/daily_rewards";

import { credit, type LedgerPorts } from "./ledger";
import { localDay } from "./progression";

import type { RuleError } from "@hulla/shared-ts";

/**
 * The daily reward calendar (GDD 12.6): a 7-day cycle that simply continues after a missed day.
 * One claim per local day, paid through the ledger with the day as the key, so a second device
 * claiming the same day is a no-op.
 */
export const DAILY_REWARD_COLLECTION = "daily_reward";
export const DAILY_REWARD_NOTIFICATION_CODE = 1002;

export interface DailyRewardState {
  /** 0 to 6: which day of the cycle the next claim pays. */
  readonly cycleIndex: number;
  readonly lastClaimDay: string;
  readonly claims: number;
  readonly tzOffsetMinutes: number;
  readonly updatedTs: number;
}

export const EMPTY_DAILY_REWARD: DailyRewardState = {
  cycleIndex: 0,
  lastClaimDay: "",
  claims: 0,
  tzOffsetMinutes: 300,
  updatedTs: 0,
};

export interface DailyRewardView {
  readonly day: string;
  readonly claimable: boolean;
  readonly today: DailyRewardDay;
  readonly next: DailyRewardDay;
  readonly cycleIndex: number;
  readonly claims: number;
}

function dayAt(calendar: DailyRewardCalendar, index: number): DailyRewardDay {
  const found = calendar.days[((index % 7) + 7) % 7];
  return found ?? { day: 1, coins: 0 };
}

export function dailyRewardView(
  state: DailyRewardState,
  calendar: DailyRewardCalendar,
  now: number,
): DailyRewardView {
  const day = localDay(now, state.tzOffsetMinutes);
  const claimable = state.lastClaimDay !== day;
  // After today's claim the card shows what was paid and what tomorrow brings.
  const todayIndex = claimable ? state.cycleIndex : state.cycleIndex - 1;
  return {
    day,
    claimable,
    today: dayAt(calendar, todayIndex),
    next: dayAt(calendar, todayIndex + 1),
    cycleIndex: state.cycleIndex,
    claims: state.claims,
  };
}

export type DailyClaimResponse =
  | {
      readonly ok: true;
      readonly applied: boolean;
      readonly reward: DailyRewardDay;
      readonly next: DailyRewardDay;
    }
  | { readonly ok: false; readonly error: RuleError };

/** Pays today's reward once and moves the cycle on; a missed day changes nothing (GDD 12.6). */
export function claimDailyReward(
  state: DailyRewardState,
  calendar: DailyRewardCalendar,
  ledger: LedgerPorts,
  userId: string,
  now: number,
): { state: DailyRewardState; response: DailyClaimResponse } {
  const day = localDay(now, state.tzOffsetMinutes);
  if (state.lastClaimDay === day) {
    const paid = dayAt(calendar, state.cycleIndex - 1);
    return {
      state,
      response: { ok: true, applied: false, reward: paid, next: dayAt(calendar, state.cycleIndex) },
    };
  }
  const reward = dayAt(calendar, state.cycleIndex);
  const paid = credit(ledger, {
    userId,
    sourceId: `daily:${day}`,
    reason: "daily_reward",
    coins: reward.coins,
    ...(reward.gems === undefined ? {} : { gems: reward.gems }),
    metadata: { day: reward.day },
  });
  if (!paid.ok) return { state, response: paid };
  const next: DailyRewardState = {
    ...state,
    cycleIndex: (state.cycleIndex + 1) % 7,
    lastClaimDay: day,
    claims: state.claims + 1,
    updatedTs: now,
  };
  return {
    state: next,
    response: { ok: true, applied: paid.applied, reward, next: dayAt(calendar, next.cycleIndex) },
  };
}

export function invalidTz(value: unknown): value is number {
  return typeof value === "number" && Number.isInteger(value) && Math.abs(value) <= 14 * 60;
}

export interface DailyRewardStore {
  read(userId: string): DailyRewardState;
  write(userId: string, state: DailyRewardState): void;
}

function isState(value: unknown): value is DailyRewardState {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v["cycleIndex"] === "number" && typeof v["lastClaimDay"] === "string";
}

export function nkDailyRewardStore(nk: nkruntime.Nakama): DailyRewardStore {
  return {
    read(userId) {
      const objects = nk.storageRead([
        { collection: DAILY_REWARD_COLLECTION, key: "state", userId },
      ]);
      const value: unknown = objects[0]?.value;
      return isState(value) ? { ...EMPTY_DAILY_REWARD, ...value } : EMPTY_DAILY_REWARD;
    },
    write(userId, state) {
      nk.storageWrite([
        {
          collection: DAILY_REWARD_COLLECTION,
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

export const CALENDAR = BUNDLED_DAILY_REWARDS;

/** At sign-in: one in-app notice a day when the reward is waiting (GDD 11.5 "Daily reward ready"). */
export function noteDailyRewardReady(nk: nkruntime.Nakama, userId: string, now: number): boolean {
  const state = nkDailyRewardStore(nk).read(userId);
  const view = dailyRewardView(state, CALENDAR, now);
  if (!view.claimable) return false;
  nk.notificationSend(
    userId,
    "daily_reward_ready",
    { day: view.today.day, coins: view.today.coins, gems: view.today.gems ?? 0 },
    DAILY_REWARD_NOTIFICATION_CODE,
    null,
    false,
  );
  return true;
}
