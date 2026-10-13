import { stateFromSetup } from "@hulla/rules-engine";
import { stateFromSeed, Xoshiro128StarStar } from "@hulla/shared-ts";
import { describe, expect, it } from "vitest";

import { BUNDLED_ACHIEVEMENTS } from "../content/achievements";
import { encodeThullaEvents, thullaHighlights, thullaHost } from "../games/thulla/host";
import { rpcAchievementsGet, rpcAchievementsSet } from "../rpc/achievements";
import {
  achievementsView,
  countRound,
  EMPTY_ACHIEVEMENTS,
  markUnlocked,
  newlyUnlocked,
  nkAchievementStore,
  settleUnlocks,
  withCounter,
  type AchievementRecord,
} from "../services/achievements";
import { nkLedgerPorts } from "../services/ledger";

import { fakeLogger, memoryNk } from "./fake-nk";

import type { LoggedMessage, RoundRecord } from "../match/types";
import type { ThullaMove, ThullaState } from "@hulla/rules-engine";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const ctxFor = (userId: string): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env: {} }) as unknown as nkruntime.Context;
const T0 = Date.UTC(2026, 9, 13, 12);
const RANKS = "23456789TJQKA";
const rankOf = (card: string): number => RANKS.indexOf(card.charAt(0));

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
    result: { gameId: "thulla", finishOrder: [0, 1, 2], loserSeat: 3, points: [3, 2, 1, -1] },
    series: null,
    finalStateHash: "h",
    highlights: [
      { thullasGiven: 1, cardsPickedUp: 0 },
      {},
      {},
      { cardsPickedUp: 11, biggestPickup: 11 },
    ],
    startedTs: T0,
    endedTs: T0 + 1000,
    completed: true,
    ...overrides,
  };
}

describe("achievements (GDD 12.7)", () => {
  it("ships about forty across the five categories, each with a metric, a target and a reward", () => {
    const list = BUNDLED_ACHIEVEMENTS.achievements;
    expect(list.length).toBeGreaterThanOrEqual(38);
    expect(new Set(list.map((a) => a.id)).size).toBe(list.length);
    for (const category of ["play", "social", "skill", "collection", "seasonal"]) {
      expect(list.some((a) => a.category === category)).toBe(true);
    }
    expect(list.every((a) => (a.reward.gems ?? 0) > 0 || a.reward.title !== undefined)).toBe(true);
    expect(list.find((a) => a.id === "houdini")).toMatchObject({
      metric: "houdini",
      target: 1,
      reward: { title: "houdini" },
    });
  });

  it("counts lifetime totals, streaks, the same group and friends at the table", () => {
    const facts = { friendsAtTable: 1, playerIds: ["u0", "u1", "u2", "u3"] };
    let rec = countRound(EMPTY_ACHIEVEMENTS, record(), 0, facts);
    expect(rec.counters).toMatchObject({
      rounds: 1,
      escapes: 1,
      thullas_given: 1,
      escaped_first: 1,
      clean_rounds: 1,
      rounds_with_friend: 1,
      rounds_with_four_friends: 0,
      same_group_rounds: 1,
      first_escape_streak: 1,
      no_bhabhi_streak: 1,
      houdini: 0,
    });
    rec = countRound(rec, record({ roundId: "r_2" }), 3, {
      friendsAtTable: 4,
      playerIds: ["u0", "u1", "u2", "u3"],
    });
    expect(rec.counters).toMatchObject({
      rounds: 2,
      bhabhis: 1,
      cards_picked_up: 11,
      biggest_pickup: 11,
      rounds_with_four_friends: 1,
      same_group_rounds: 2,
      first_escape_streak: 0,
      no_bhabhi_streak: 0,
    });
    rec = countRound(rec, record({ roundId: "r_3" }), 0, {
      friendsAtTable: 0,
      playerIds: ["u0", "u1", "u9"],
    });
    expect(rec.counters.same_group_rounds).toBe(2);
    expect(Object.keys(rec.groups)).toHaveLength(2);
    expect(countRound(rec, record({ completed: false }), 0, facts)).toBe(rec);
  });

  it("unlocks once each, pays gems through the ledger and grants titles", () => {
    const m = memoryNk();
    const store = nkAchievementStore(m.nk);
    const ledger = nkLedgerPorts(m.nk);
    const rec: AchievementRecord = withCounter(EMPTY_ACHIEVEMENTS, "thullas_given", 100, T0);
    const first = settleUnlocks(m.nk, ledger, store, "u1", rec, BUNDLED_ACHIEVEMENTS, T0);
    expect(first.map((u) => u.id).sort()).toEqual(["first_thulla", "thullas_100"]);
    expect(ledger.balances("u1")).toEqual({ coins: 0, gems: 25 });
    expect(m.notifications.filter((n) => n.subject === "achievement_unlocked")).toHaveLength(1);
    const again = settleUnlocks(
      m.nk,
      ledger,
      store,
      "u1",
      store.read("u1"),
      BUNDLED_ACHIEVEMENTS,
      T0 + 1,
    );
    expect(again).toEqual([]);
    expect(ledger.balances("u1")).toEqual({ coins: 0, gems: 25 });
    const titled = withCounter(store.read("u1"), "thullas_given", 1000, T0 + 2);
    const big = settleUnlocks(m.nk, ledger, store, "u1", titled, BUNDLED_ACHIEVEMENTS, T0 + 2);
    expect(big.map((u) => u.id)).toEqual(["thullas_1000"]);
    expect(m.object("progress", "state", "u1")).toMatchObject({
      titles: ["naya_khiladi", "thulla_baaz"],
    });
    expect(newlyUnlocked(markUnlocked(titled, big, T0), BUNDLED_ACHIEVEMENTS)).toEqual([]);
    const view = achievementsView(store.read("u1"), BUNDLED_ACHIEVEMENTS);
    expect(view.find((v) => v.id === "thullas_100")).toMatchObject({
      progress: 100,
      unlockedTs: T0,
    });
    expect(view.find((v) => v.id === "rounds_10")).toMatchObject({ progress: 0, unlockedTs: null });
  });

  it("Houdini: escaping with a single card drawn from the discard, through the real engine", () => {
    // Seat 0 wins the first trick with its last card, so it must lead again and draws from the
    // discard (R26). It leads the drawn card; whoever beats it takes the lead, seat 0's hand is
    // empty, and it has escaped with a single drawn card (E12, GDD 12.7 "Houdini").
    const setup = stateFromSetup({
      hands: [["AS"], ["3S", "QS"], ["5S", "KS"]],
      leader: 0,
      discard: ["2S"],
      rules: { confirmedFastForward: false },
    });
    if (!setup.ok) throw new Error("setup");
    let state: ThullaState = setup.value;
    const rng = Xoshiro128StarStar.fromState(stateFromSeed(new Uint8Array(32)));
    const messages: LoggedMessage[] = [];
    let seq = 0;
    const play = (seat: number, move: ThullaMove): void => {
      const applied = thullaHost.module.applyMove(state, seat, move, rng);
      if (!applied.ok) throw new Error(`move refused: ${applied.error.code}`);
      state = applied.state;
      const encoded = encodeThullaEvents(
        {
          roundId: "r_1",
          seedHex: "00",
          state,
          series: null,
          result: null,
          move: { seat, clientSeq: 1, auto: false },
        },
        applied.events,
      );
      for (const gm of encoded) {
        seq += 1;
        messages.push({
          seq,
          ts: T0,
          type: gm.type,
          payload: gm.payload,
          seat: gm.seat,
          stateHash: null,
        });
      }
    };
    const cardOf = (move: ThullaMove): string => (move.type === "play_card" ? move.card : "");
    const highest = (seat: number): ThullaMove => {
      const legal = thullaHost.module.legalMoves(state, seat).filter((m) => m.type === "play_card");
      const sorted = [...legal].sort((a, b) => rankOf(cardOf(a)) - rankOf(cardOf(b)));
      const best = sorted[sorted.length - 1];
      if (best === undefined) throw new Error(`seat ${String(seat)} cannot play`);
      return best;
    };
    play(0, { type: "play_card", card: "AS" });
    play(1, highest(1));
    play(2, highest(2));
    const escaped = (): boolean =>
      messages.some((m) => m.type === "escape" && (m.payload as { seat: number }).seat === 0);
    for (let guard = 0; guard < 6 && !escaped(); guard += 1) {
      const lead = thullaHost.module.legalMoves(state, 0)[0];
      if (lead === undefined) throw new Error("the leader has nothing to lead");
      play(0, lead);
      if (escaped()) break;
      play(1, highest(1));
      play(2, highest(2));
    }
    expect(
      messages.some(
        (m) => m.type === "private_draw" && (m.payload as { source: string }).source === "discard",
      ),
    ).toBe(true);
    expect(escaped()).toBe(true);
    const highlights = thullaHighlights(messages, 3);
    expect(highlights[0]).toMatchObject({ houdini: 1 });
    expect(highlights[1]).toMatchObject({ houdini: 0 });

    const m = memoryNk();
    const store = nkAchievementStore(m.nk);
    const counted = countRound(
      EMPTY_ACHIEVEMENTS,
      record({
        players: ["u0", "u1", "u2"],
        highlights,
        result: { gameId: "thulla", finishOrder: [0], loserSeat: 2, points: [3, 0, -1] },
      }),
      0,
      { friendsAtTable: 0, playerIds: ["u0", "u1", "u2"] },
    );
    const unlocked = settleUnlocks(
      m.nk,
      nkLedgerPorts(m.nk),
      store,
      "u0",
      counted,
      BUNDLED_ACHIEVEMENTS,
      T0,
    );
    expect(unlocked.map((u) => u.id)).toContain("houdini");
    expect(m.object("progress", "state", "u0")).toMatchObject({
      titles: ["naya_khiladi", "houdini"],
    });
  });

  it("serves the list with progress, refreshing the friend count, and reloads the list", () => {
    const m = memoryNk();
    const log = fakeLogger();
    m.nk.friendsAdd("u1", "user_u1", ["u2"], [], {});
    m.nk.friendsAdd("u2", "user_u2", ["u1"], [], {});
    const got = parse(rpcAchievementsGet(ctxFor("u1"), log, m.nk));
    expect(got).toMatchObject({ ok: true, unlockedNow: ["first_friend"] });
    expect((got["achievements"] as Json[]).find((a) => a["id"] === "first_friend")).toMatchObject({
      progress: 1,
    });
    expect(parse(rpcAchievementsGet(ctxFor("u1"), log, m.nk))).toMatchObject({ unlockedNow: [] });
    expect(
      parse(rpcAchievementsGet({ env: {} } as unknown as nkruntime.Context, log, m.nk)),
    ).toMatchObject({ ok: false });
    expect(parse(rpcAchievementsSet(ctxFor("u1"), log, m.nk, "{}"))).toMatchObject({ ok: false });
    expect(
      parse(rpcAchievementsSet({ env: {} } as unknown as nkruntime.Context, log, m.nk, "{")),
    ).toMatchObject({ error: { code: "table.invalid_payload" } });
    expect(
      parse(
        rpcAchievementsSet(
          { env: {} } as unknown as nkruntime.Context,
          log,
          m.nk,
          JSON.stringify({ id: "x", version: 1, achievements: [] }),
        ),
      ),
    ).toMatchObject({ error: { code: "content.schema_invalid" } });
    expect(
      parse(
        rpcAchievementsSet(
          { env: {} } as unknown as nkruntime.Context,
          log,
          m.nk,
          JSON.stringify({ ...BUNDLED_ACHIEVEMENTS, version: 2 }),
        ),
      ),
    ).toEqual({ ok: true, id: "achievements-v1", version: 2 });
  });
});
