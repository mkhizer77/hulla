import { describe, expect, it } from "vitest";

import { BUNDLED_MISSIONS } from "../content/missions";
import {
  rpcMissionsClaim,
  rpcMissionsGet,
  rpcMissionsReroll,
  rpcMissionsSet,
} from "../rpc/missions";
import { type Balances, type LedgerPorts } from "../services/ledger";
import {
  applyMetrics,
  claim,
  DAILY_COUNT,
  draw,
  EMPTY_MISSIONS,
  forPeriod,
  localWeek,
  metricsFromRound,
  recordMissionMetrics,
  reroll,
  seriesWon,
  WEEKLY_COUNT,
  type MissionsRecord,
} from "../services/missions";

import { fakeLogger, memoryNk } from "./fake-nk";

import type { RoundRecord } from "../match/types";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const ctxFor = (userId: string): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env: {} }) as unknown as nkruntime.Context;
const T0 = Date.UTC(2026, 9, 13, 12);
const DAY = 24 * 60 * 60 * 1000;

function record(overrides: Partial<RoundRecord> = {}): RoundRecord {
  return {
    tableId: "t_ABCDEF",
    roundId: "r_1",
    gameId: "thulla",
    rulesVersion: "1",
    rules: { gameId: "thulla" },
    mode: "private",
    dealerSeat: 0,
    seedHex: "00",
    players: ["u0", "u1", "u2", "u3"],
    moves: [],
    result: { gameId: "thulla", finishOrder: [0, 1, 2], loserSeat: 3, points: [3, 2, 1, 0] },
    series: null,
    finalStateHash: "h",
    highlights: [
      { thullasGiven: 2, cardsPickedUp: 0 },
      { cardsPickedUp: 7, handsTaken: 1 },
      {},
      { cardsPickedUp: 12 },
    ],
    startedTs: T0,
    endedTs: T0 + 1000,
    completed: true,
    ...overrides,
  };
}

function memoryLedger(): LedgerPorts & { readonly paid: string[] } {
  const keys = new Set<string>();
  const balances = new Map<string, Balances>();
  const paid: string[] = [];
  return {
    paid,
    applied: (u, s) => keys.has(`${u}/${s}`),
    claim: (u, s) => {
      if (keys.has(`${u}/${s}`)) return false;
      keys.add(`${u}/${s}`);
      return true;
    },
    update(u, changeset) {
      const c = balances.get(u) ?? { coins: 0, gems: 0 };
      const n = {
        coins: c.coins + (changeset["coins"] ?? 0),
        gems: c.gems + (changeset["gems"] ?? 0),
      };
      balances.set(u, n);
      paid.push(JSON.stringify(changeset));
      return n;
    },
    balances: (u) => balances.get(u) ?? { coins: 0, gems: 0 },
  };
}

describe("missions (GDD 12.5)", () => {
  it("draws three dailies and five weeklies per player and period, the same on every device", () => {
    const a = forPeriod(EMPTY_MISSIONS, BUNDLED_MISSIONS, "u1", T0);
    const b = forPeriod(EMPTY_MISSIONS, BUNDLED_MISSIONS, "u1", T0 + 60_000);
    expect(a.daily).toHaveLength(DAILY_COUNT);
    expect(a.weekly).toHaveLength(WEEKLY_COUNT);
    expect(a.daily.map((m) => m.id)).toEqual(b.daily.map((m) => m.id));
    expect(new Set(a.daily.map((m) => m.id)).size).toBe(DAILY_COUNT);
    const other = forPeriod(EMPTY_MISSIONS, BUNDLED_MISSIONS, "u2", T0);
    expect(draw(BUNDLED_MISSIONS.daily, 3, "u1:d:x")).not.toEqual(
      draw(BUNDLED_MISSIONS.daily, 3, "u2:d:x"),
    );
    expect(other.week).toBe(a.week);
    // A new local day redraws the dailies and keeps the weeklies; a new week redraws those too.
    const tomorrow = forPeriod(
      { ...a, daily: a.daily.map((m) => ({ ...m, progress: 1 })) },
      BUNDLED_MISSIONS,
      "u1",
      T0 + DAY,
    );
    expect(tomorrow.day).not.toBe(a.day);
    expect(tomorrow.daily.every((m) => m.progress === 0)).toBe(true);
    expect(tomorrow.weekly).toEqual(a.weekly);
    expect(localWeek(T0, 300)).toBe("2026-10-12");
    expect(forPeriod(a, BUNDLED_MISSIONS, "u1", T0 + 7 * DAY).week).toBe("2026-10-19");
  });

  it("counts a round for the right metrics per seat", () => {
    const r = record();
    expect(metricsFromRound(r, 0, true)).toEqual({
      rounds_played: 1,
      rounds_with_friend: 1,
      escapes: 1,
      escaped_first: 1,
      thullas_given: 2,
      clean_rounds: 1,
      low_pickup_rounds: 1,
      hands_taken: 0,
      series_won: 0,
    });
    expect(metricsFromRound(r, 1, false)).toMatchObject({
      escaped_first: 0,
      clean_rounds: 0,
      low_pickup_rounds: 0,
      hands_taken: 1,
    });
    expect(metricsFromRound(r, 3, false)).toMatchObject({ escapes: 0, low_pickup_rounds: 0 });
    const series = {
      roundsPlayed: 3,
      points: [5, 2, 1, 0],
      loserTally: [0, 0, 1, 2],
      nextDealerSeat: 1,
      finished: true,
    };
    expect(seriesWon(record({ series }), 0)).toBe(true);
    expect(seriesWon(record({ series }), 1)).toBe(false);
    expect(seriesWon(record({ series: { ...series, finished: false } }), 0)).toBe(false);
    expect(metricsFromRound(record({ completed: false }), 0, false)).toEqual({});
  });

  it("moves progress up to the target, pays a claim once, and re-rolls once a day", () => {
    const pool = BUNDLED_MISSIONS;
    const base: MissionsRecord = {
      ...EMPTY_MISSIONS,
      day: "2026-10-13",
      week: "2026-10-12",
      daily: [
        { id: "d_thullas_3", progress: 0, claimed: false },
        { id: "d_rounds_3", progress: 0, claimed: false },
        { id: "d_escape_first", progress: 0, claimed: false },
      ],
      weekly: [{ id: "w_rounds_15", progress: 14, claimed: false }],
    };
    const bumped = applyMetrics(
      base,
      pool,
      { thullas_given: 5, rounds_played: 1, escaped_first: 0 },
      T0,
    );
    expect(bumped.changed).toBe(true);
    expect(bumped.record.daily.map((m) => m.progress)).toEqual([3, 1, 0]);
    expect(bumped.record.weekly[0]?.progress).toBe(15);
    expect(applyMetrics(bumped.record, pool, { escaped_first: 0 }, T0).changed).toBe(false);

    const ledger = memoryLedger();
    const early = claim(bumped.record, pool, ledger, "u1", "d_rounds_3", T0);
    expect(early.response).toMatchObject({ error: { code: "economy.already_granted" } });
    const paid = claim(bumped.record, pool, ledger, "u1", "d_thullas_3", T0);
    expect(paid.response).toEqual({ ok: true, applied: true, reward: { coins: 60 } });
    const again = claim(paid.record, pool, ledger, "u1", "d_thullas_3", T0);
    expect(again.response).toEqual({ ok: true, applied: false, reward: { coins: 60 } });
    expect(ledger.paid).toEqual(['{"coins":60}']);
    const weekly = claim(paid.record, pool, ledger, "u1", "w_rounds_15", T0);
    expect(weekly.response).toMatchObject({
      ok: true,
      applied: true,
      reward: { coins: 250, gems: 5 },
    });
    expect(claim(paid.record, pool, ledger, "u1", "nope", T0).response).toMatchObject({
      error: { code: "table.invalid_payload" },
    });

    const rolled = reroll(paid.record, pool, "u1", "d_rounds_3", T0);
    expect(rolled.response).toMatchObject({ ok: true, replaced: "d_rounds_3" });
    if (!rolled.response.ok) return;
    expect(["d_thullas_3", "d_rounds_3", "d_escape_first"]).not.toContain(
      rolled.response.mission.id,
    );
    expect(rolled.record.daily[1]).toEqual({
      id: rolled.response.mission.id,
      progress: 0,
      claimed: false,
    });
    expect(reroll(rolled.record, pool, "u1", "d_escape_first", T0).response).toMatchObject({
      error: { code: "economy.daily_cap_reached" },
    });
    expect(reroll(paid.record, pool, "u1", "d_thullas_3", T0).response).toMatchObject({
      error: { code: "table.invalid_payload" },
    });
  });

  it("runs through the RPCs, records round metrics with a notification, and reloads the pool", () => {
    const m = memoryNk();
    const log = fakeLogger();
    const first = parse(
      rpcMissionsGet(ctxFor("u1"), log, m.nk, JSON.stringify({ tzOffsetMinutes: 300 })),
    );
    expect(first).toMatchObject({ ok: true, rerollAvailable: true });
    expect((first["daily"] as Json[]).length).toBe(DAILY_COUNT);
    const changed = recordMissionMetrics(
      m.nk,
      "u1",
      {
        rounds_played: 1,
        thullas_given: 1,
        escapes: 1,
        escaped_first: 1,
        low_pickup_rounds: 1,
        clean_rounds: 1,
        rounds_with_friend: 1,
        hands_taken: 1,
        series_won: 0,
        voice_private: 0,
      },
      Date.now(),
    );
    expect(changed).toBe(true);
    expect(m.notifications).toMatchObject([{ userId: "u1", subject: "missions_progress" }]);
    const after = parse(rpcMissionsGet(ctxFor("u1"), log, m.nk, "{}"));
    const weekly = after["weekly"] as { id: string; progress: number }[];
    expect(weekly.find((w) => w.id === "w_rounds_15")?.progress).toBe(1);
    const daily = after["daily"] as { id: string; progress: number; claimable: boolean }[];
    const done = daily.find((d) => d.claimable);
    if (done !== undefined) {
      expect(
        parse(rpcMissionsClaim(ctxFor("u1"), log, m.nk, JSON.stringify({ missionId: done.id }))),
      ).toMatchObject({ ok: true, applied: true });
      expect(
        parse(rpcMissionsClaim(ctxFor("u1"), log, m.nk, JSON.stringify({ missionId: done.id }))),
      ).toMatchObject({ ok: true, applied: false });
    }
    const pending = daily.find((d) => !d.claimable);
    if (pending !== undefined) {
      expect(
        parse(
          rpcMissionsReroll(ctxFor("u1"), log, m.nk, JSON.stringify({ missionId: pending.id })),
        ),
      ).toMatchObject({ ok: true });
    }
    expect(
      parse(rpcMissionsGet({ env: {} } as unknown as nkruntime.Context, log, m.nk, "{}")),
    ).toMatchObject({ ok: false });
    expect(parse(rpcMissionsSet(ctxFor("u1"), log, m.nk, "{}"))).toMatchObject({ ok: false });
    expect(
      parse(rpcMissionsSet({ env: {} } as unknown as nkruntime.Context, log, m.nk, "{")),
    ).toMatchObject({ error: { code: "table.invalid_payload" } });
    expect(
      parse(
        rpcMissionsSet(
          { env: {} } as unknown as nkruntime.Context,
          log,
          m.nk,
          JSON.stringify({ id: "x", version: 2, daily: [], weekly: [] }),
        ),
      ),
    ).toMatchObject({ error: { code: "content.schema_invalid" } });
    const replaced = parse(
      rpcMissionsSet(
        { env: {} } as unknown as nkruntime.Context,
        log,
        m.nk,
        JSON.stringify({ ...BUNDLED_MISSIONS, id: "missions-v2", version: 2 }),
      ),
    );
    expect(replaced).toEqual({ ok: true, id: "missions-v2", version: 2 });
  });
});
