import { describe, expect, it } from "vitest";

import { afterAuthenticateDevice } from "../hooks/after_authenticate";
import { rpcDailyRewardClaim, rpcDailyRewardGet } from "../rpc/daily_reward";
import {
  CALENDAR,
  claimDailyReward,
  dailyRewardView,
  EMPTY_DAILY_REWARD,
  type DailyRewardState,
} from "../services/daily_reward";
import { nkLedgerPorts } from "../services/ledger";
import { localDay } from "../services/progression";

import { fakeLogger, memoryNk } from "./fake-nk";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const ctxFor = (userId: string): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env: {} }) as unknown as nkruntime.Context;
const T0 = Date.UTC(2026, 9, 13, 12);
const DAY = 24 * 60 * 60 * 1000;

describe("the daily reward calendar (GDD 12.6)", () => {
  it("pays the cycle in order, once a day, and carries on after a missed day", () => {
    const m = memoryNk();
    const ledger = nkLedgerPorts(m.nk);
    let state: DailyRewardState = { ...EMPTY_DAILY_REWARD, tzOffsetMinutes: 300 };
    const first = claimDailyReward(state, CALENDAR, ledger, "u1", T0);
    expect(first.response).toEqual({
      ok: true,
      applied: true,
      reward: { day: 1, coins: 50 },
      next: { day: 2, coins: 60 },
    });
    state = first.state;
    expect(state).toMatchObject({ cycleIndex: 1, claims: 1, lastClaimDay: localDay(T0, 300) });
    const again = claimDailyReward(state, CALENDAR, ledger, "u1", T0 + 60_000);
    expect(again.response).toEqual({
      ok: true,
      applied: false,
      reward: { day: 1, coins: 50 },
      next: { day: 2, coins: 60 },
    });
    expect(again.state).toBe(state);
    // Three days missed: the next claim is still day 2.
    const later = claimDailyReward(state, CALENDAR, ledger, "u1", T0 + 4 * DAY);
    expect(later.response).toMatchObject({ applied: true, reward: { day: 2, coins: 60 } });
    state = later.state;
    for (let i = 0; i < 5; i += 1) {
      state = claimDailyReward(state, CALENDAR, ledger, "u1", T0 + (5 + i) * DAY).state;
    }
    expect(state.cycleIndex).toBe(0);
    expect(state.claims).toBe(7);
    expect(ledger.balances("u1")).toEqual({ coins: 600, gems: 7 });
    const eighth = claimDailyReward(state, CALENDAR, ledger, "u1", T0 + 10 * DAY);
    expect(eighth.response).toMatchObject({ reward: { day: 1, coins: 50 } });
  });

  it("shows today and the next day on the card, before and after the claim, at local midnight", () => {
    const late = Date.UTC(2026, 9, 13, 18, 30);
    const state: DailyRewardState = { ...EMPTY_DAILY_REWARD, tzOffsetMinutes: 300, cycleIndex: 3 };
    expect(dailyRewardView(state, CALENDAR, late)).toMatchObject({
      day: "2026-10-13",
      claimable: true,
      today: { day: 4, coins: 80, gems: 2 },
      next: { day: 5, coins: 90 },
    });
    const claimed = { ...state, cycleIndex: 4, lastClaimDay: "2026-10-13", claims: 1 };
    expect(dailyRewardView(claimed, CALENDAR, late)).toMatchObject({
      claimable: false,
      today: { day: 4 },
      next: { day: 5 },
    });
    expect(dailyRewardView(claimed, CALENDAR, late + 31 * 60_000)).toMatchObject({
      day: "2026-10-14",
      claimable: true,
      today: { day: 5 },
      next: { day: 6 },
    });
  });

  it("runs through the RPCs, is idempotent across devices, and notes a waiting reward at sign-in", () => {
    const m = memoryNk();
    const log = fakeLogger();
    afterAuthenticateDevice(ctxFor("u1"), log, m.nk);
    expect(m.notifications.filter((n) => n.subject === "daily_reward_ready")).toHaveLength(1);
    const card = parse(
      rpcDailyRewardGet(ctxFor("u1"), log, m.nk, JSON.stringify({ tzOffsetMinutes: 330 })),
    );
    expect(card).toMatchObject({
      ok: true,
      claimable: true,
      today: { day: 1, coins: 50 },
      next: { day: 2 },
    });
    expect(m.object("daily_reward", "state", "u1")).toMatchObject({ tzOffsetMinutes: 330 });
    expect(parse(rpcDailyRewardClaim(ctxFor("u1"), log, m.nk))).toMatchObject({
      ok: true,
      applied: true,
      reward: { day: 1 },
    });
    expect(parse(rpcDailyRewardClaim(ctxFor("u1"), log, m.nk))).toMatchObject({
      ok: true,
      applied: false,
    });
    expect(
      m.ledger.filter((l) => l.userId === "u1" && l.metadata["reason"] === "daily_reward"),
    ).toHaveLength(1);
    expect(parse(rpcDailyRewardGet(ctxFor("u1"), log, m.nk, "{}"))).toMatchObject({
      claimable: false,
      today: { day: 1 },
      next: { day: 2 },
    });
    afterAuthenticateDevice(ctxFor("u1"), log, m.nk);
    expect(m.notifications.filter((n) => n.subject === "daily_reward_ready")).toHaveLength(1);
    expect(
      parse(rpcDailyRewardGet({ env: {} } as unknown as nkruntime.Context, log, m.nk, "{}")),
    ).toMatchObject({ ok: false });
    expect(
      parse(rpcDailyRewardClaim({ env: {} } as unknown as nkruntime.Context, log, m.nk)),
    ).toMatchObject({ ok: false });
  });
});
