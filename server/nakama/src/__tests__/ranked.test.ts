import { stateFromSeed, Xoshiro128StarStar } from "@hulla/shared-ts";
import { afterEach, describe, expect, it, vi } from "vitest";

import { BUNDLED_CATALOG } from "../content/catalog";
import { BUNDLED_RANKED, type RankedConfig } from "../content/ranked";
import {
  beforeMatchmakerAdd,
  beforePartyMatchmakerAdd,
  rankedTicket,
} from "../hooks/before_matchmaker_add";
import { tableForMatch } from "../hooks/matchmaker_matched";
import { rpcRankedBoard, rpcRankedGet } from "../rpc/ranked";
import { tableCreate } from "../rpc/table_create";
import { catalogView, EMPTY_INVENTORY, grant, purchase, spotlight } from "../services/catalog";
import { nkLedgerPorts } from "../services/ledger";
import { applyProfileUpdate, bootstrapProfile } from "../services/profiles";
import {
  abandonedSeat,
  afterRound,
  applyRankedRound,
  boardIdFor,
  displayLeague,
  leagueFor,
  newState,
  nkRankedStore,
  paySeasonReward,
  rateRankedRound,
  rollover,
  seasonAt,
  type RankedState,
  type SeasonRewardPorts,
} from "../services/ranked";

import { fakeLogger, memoryNk } from "./fake-nk";
import { broadcasts, harness, opened, presence, type Harness } from "./table-helpers";

import type { MatchedEntry } from "../hooks/matchmaker_matched";
import type { CreateTableParams, RoundRecord } from "../match/types";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const ctxFor = (userId: string): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env: {} }) as unknown as nkruntime.Context;
const cfg: RankedConfig = BUNDLED_RANKED;
const WEEK = 7 * 24 * 60 * 60 * 1000;
const S1 = cfg.season.anchorTs + WEEK;
const S2 = cfg.season.anchorTs + cfg.season.weeks * WEEK + WEEK;

function rankedRecord(
  roundId: string,
  finish: number[],
  endedTs = S1,
  extra: Partial<RoundRecord> = {},
): RoundRecord {
  const loser = finish[finish.length - 1] ?? 3;
  return {
    tableId: "t_RANKED",
    roundId,
    gameId: "thulla",
    rulesVersion: "1",
    rules: { gameId: "thulla", preset: "standard" },
    mode: "ranked",
    dealerSeat: 0,
    seedHex: "00",
    players: ["u0", "u1", "u2", "u3"],
    moves: [],
    result: {
      gameId: "thulla",
      finishOrder: finish,
      loserSeat: loser,
      points: [0, 0, 0, 0],
    },
    series: null,
    finalStateHash: "h",
    highlights: [{}, {}, {}, {}],
    startedTs: endedTs - 60_000,
    endedTs,
    completed: true,
    absentMs: [0, 0, 0, 0],
    presentAtEnd: [true, true, true, true],
    ...extra,
  };
}

const placedAt = (rating: number, league: string, protectedRounds = 0): RankedState => ({
  ...newState(cfg, "s1", 0),
  rating,
  ratedRounds: 40,
  seasonRounds: 20,
  league,
  peakLeague: league,
  protectedRounds,
});

afterEach(() => {
  vi.useRealTimers();
});

describe("ranked seasons, leagues and placement (GDD 7.5, 12.9)", () => {
  it("runs eight-week seasons from the content anchor", () => {
    expect(seasonAt(cfg, cfg.season.anchorTs)).toMatchObject({ id: "s1", number: 1 });
    const second = seasonAt(cfg, S2);
    expect(second).toMatchObject({ id: "s2", number: 2 });
    expect(second.endTs - second.startTs).toBe(cfg.season.weeks * WEEK);
    expect(seasonAt(cfg, cfg.season.anchorTs - WEEK).id).toBe("s1");
  });

  it("maps ratings to leagues Bronze to Diamond; Ustad is Diamond inside the top places", () => {
    expect(leagueFor(cfg, 900)).toBe("bronze");
    expect(leagueFor(cfg, 1200)).toBe("silver");
    expect(leagueFor(cfg, 1250)).toBe("gold");
    expect(leagueFor(cfg, 1450)).toBe("platinum");
    expect(leagueFor(cfg, 2000)).toBe("diamond");
    const diamond = placedAt(1700, "diamond");
    expect(displayLeague(diamond, cfg, 3)).toBe("ustad");
    expect(displayLeague(diamond, cfg, cfg.ustadTop + 1)).toBe("diamond");
    expect(displayLeague(diamond, cfg, null)).toBe("diamond");
    expect(displayLeague(placedAt(1450, "platinum"), cfg, 1)).toBe("platinum");
    expect(displayLeague(newState(cfg, "s1", 0), cfg, 1)).toBeNull();
  });

  it("hides the league for the ten placement rounds", () => {
    let state = newState(cfg, "s1", 0);
    for (let i = 0; i < cfg.placementRounds - 1; i += 1) {
      state = afterRound(state, cfg, 1300, `t:r${String(i)}`, false, i);
      expect(state.league).toBeNull();
    }
    state = afterRound(state, cfg, 1300, "t:r9", false, 9);
    expect(state).toMatchObject({ league: "gold", peakLeague: "gold", seasonRounds: 10 });
  });

  it("protects a promotion for three rounds, then demotes", () => {
    let state = afterRound(placedAt(1240, "silver"), cfg, 1260, "t:a", false, 1);
    expect(state).toMatchObject({ league: "gold", protectedRounds: 3 });
    state = afterRound(state, cfg, 1240, "t:b", false, 2);
    expect(state).toMatchObject({ league: "gold", protectedRounds: 2 });
    state = afterRound(state, cfg, 1240, "t:c", false, 3);
    state = afterRound(state, cfg, 1240, "t:d", false, 4);
    expect(state).toMatchObject({ league: "gold", protectedRounds: 0 });
    state = afterRound(state, cfg, 1240, "t:e", false, 5);
    expect(state).toMatchObject({ league: "silver", peakLeague: "gold" });
  });

  it("rolls a season over: the league's reward, half the distance kept, history kept", () => {
    const ended = placedAt(1600, "diamond");
    const next = seasonAt(cfg, S2);
    const rolled = rollover(ended, cfg, next, 12, S2);
    expect(rolled.settled).toMatchObject({ seasonId: "s1", league: "ustad", rank: 12 });
    expect(rolled.settled?.reward?.title).toBe("ranked_ustad");
    expect(rolled.state).toMatchObject({
      seasonId: "s2",
      rating: 1400,
      seasonRounds: 0,
      league: null,
      ratedRounds: 40,
    });
    expect(rolled.state.history[0]?.seasonId).toBe("s1");
    const unplaced = rollover(newState(cfg, "s1", 0), cfg, next, null, S2);
    expect(unplaced.settled).toMatchObject({ league: null, reward: null });
    expect(rollover(rolled.state, cfg, next, null, S2).settled).toBeNull();
  });

  it("pays a season reward once", () => {
    const m = memoryNk();
    const granted: string[] = [];
    const ports: SeasonRewardPorts = {
      ledger: nkLedgerPorts(m.nk),
      grantItem: (_u, item) => granted.push(item),
      grantTitle: (_u, title) => granted.push(title),
      grantFrame: (_u, frame) => granted.push(frame),
    };
    const summary = rollover(placedAt(1600, "diamond"), cfg, seasonAt(cfg, S2), null, S2).settled;
    if (summary === null) throw new Error("not settled");
    expect(paySeasonReward(ports, "u0", summary, S2)).toBe(true);
    expect(paySeasonReward(ports, "u0", summary, S2)).toBe(false);
    expect(granted).toEqual(["back_ranked_diamond", "ranked_diamond", "ranked_diamond"]);
    expect(m.wallets.get("u0")).toMatchObject({ coins: 1000 });
  });
});

describe("ranked rounds (R53, R46)", () => {
  it("R53 rates the round from the finish order", () => {
    const seats = ["u0", "u1", "u2", "u3"].map((userId, seat) => ({
      userId,
      seat,
      state: placedAt(1300, "gold"),
      abandoned: false,
    }));
    const outcomes = rateRankedRound(cfg, rankedRecord("r1", [2, 0, 3, 1]), seats);
    expect(outcomes.map((o) => o.delta)).toEqual([5, -16, 16, -5]);
    expect(outcomes[2]?.state.lastRound).toBe("t_RANKED:r1");
  });

  it("R46 an abandoner places below the Bhabhi and pays the penalty", () => {
    const record = rankedRecord("r1", [0, 1, 2, 3], S1, {
      absentMs: [0, 0, 0, cfg.abandonAfterMs + 1],
      presentAtEnd: [true, true, true, false],
    });
    expect(abandonedSeat(record, 3, cfg)).toBe(true);
    expect(abandonedSeat(record, 2, cfg)).toBe(false);
    const back = rankedRecord("r1", [0, 1, 2, 3], S1, {
      absentMs: [0, 0, 0, cfg.abandonAfterMs + 1],
    });
    expect(abandonedSeat(back, 3, cfg)).toBe(false);
    const seats = ["u0", "u1", "u2", "u3"].map((userId, seat) => ({
      userId,
      seat,
      state: placedAt(1300, "gold"),
      abandoned: seat === 3,
    }));
    const outcomes = rateRankedRound(cfg, record, seats);
    expect(outcomes[3]?.delta).toBe(-16 - cfg.abandonPenalty);
    expect(outcomes[3]?.state.abandons).toBe(1);
  });

  it("rates ten placement rounds, posts the board, ignores a retried round and settles the season with rewards", () => {
    const m = memoryNk();
    const log = fakeLogger();
    for (let i = 0; i < cfg.placementRounds; i += 1) {
      applyRankedRound(m.nk, log, rankedRecord(`r${String(i)}`, [0, 1, 2, 3], S1 + i));
    }
    const store = nkRankedStore(m.nk);
    const first = store.read("u0");
    expect(first).toMatchObject({ seasonId: "s1", seasonRounds: 10, ratedRounds: 10 });
    expect(first?.league).not.toBeNull();
    expect(first?.rating).toBeGreaterThan(cfg.startRating);
    expect(store.read("u3")?.rating).toBeLessThan(cfg.startRating);
    // Retrying the last round changes nobody.
    applyRankedRound(m.nk, log, rankedRecord("r9", [0, 1, 2, 3], S1 + 9));
    expect(store.read("u0")).toEqual(first);
    // Other modes are not rated.
    expect(
      applyRankedRound(m.nk, log, { ...rankedRecord("x", [0, 1, 2, 3]), mode: "public" }),
    ).toEqual([]);
    const board = m.boards.get(boardIdFor("s1"));
    expect(board?.get("u0")?.score).toBe(first?.rating);

    vi.useFakeTimers();
    vi.setSystemTime(S1 + 100);
    const top = parse(rpcRankedBoard(ctxFor("u1"), log, m.nk, "{}"));
    expect((top["rows"] as Json[]).map((r) => r["userId"])).toEqual(["u0", "u1", "u2", "u3"]);
    const league = first?.league ?? "";
    const reward = cfg.leagues.find((l) => l.id === league)?.reward;
    expect((top["rows"] as Json[])[0]?.["league"]).toBe(league);
    const card = parse(rpcRankedGet(ctxFor("u0"), log, m.nk));
    expect(card).toMatchObject({
      ok: true,
      season: { id: "s1" },
      rating: first?.rating,
      league,
      rank: 1,
      placement: { played: 10, of: 10 },
      seasonSettled: false,
    });

    vi.setSystemTime(S2);
    const settled = parse(rpcRankedGet(ctxFor("u0"), log, m.nk));
    expect(settled).toMatchObject({
      ok: true,
      season: { id: "s2" },
      league: null,
      placement: { played: 0, of: 10 },
      seasonSettled: true,
      lastSeason: { seasonId: "s1", league, rank: 1 },
    });
    expect(settled["rating"]).toBe(
      Math.round(cfg.startRating + ((first?.rating ?? 0) - cfg.startRating) * cfg.softReset),
    );
    const coins = m.wallets.get("u0")?.["coins"] ?? 0;
    expect(coins).toBe(reward?.coins);
    expect(m.object("progress", "state", "u0")?.["titles"]).toContain(reward?.title);
    for (const item of reward?.items ?? []) {
      expect(m.object("inventory", "state", "u0")?.["owned"]).toHaveProperty(item);
    }
    // A second visit pays nothing more.
    parse(rpcRankedGet(ctxFor("u0"), log, m.nk));
    expect(m.wallets.get("u0")?.["coins"]).toBe(coins);
    // The Bhabhi every round never finished above Bronze and gets Bronze's reward.
    const last = parse(rpcRankedGet(ctxFor("u3"), log, m.nk));
    expect(last["lastSeason"]).toMatchObject({ league: "bronze" });
    expect(m.wallets.get("u3")?.["coins"]).toBe(200);
  });

  it("refuses ranked_get without a session", () => {
    const m = memoryNk();
    const out = parse(rpcRankedGet(ctxFor(""), fakeLogger(), m.nk));
    expect(out).toMatchObject({ ok: false, error: { code: "table.not_authorised" } });
    expect(parse(rpcRankedBoard(ctxFor(""), fakeLogger(), m.nk, ""))["ok"]).toBe(false);
  });
});

describe("the ranked queue (GDD 7.5, 9.6)", () => {
  const ticket = (stringProperties: Record<string, string>, band?: number) => ({
    query: "*",
    minCount: 3,
    maxCount: 6,
    countMultiple: 1,
    stringProperties,
    numericProperties: band === undefined ? {} : { band, rating: 9999 },
  });

  it("puts the stored rating and a held band on the ticket, at four seats", () => {
    const shaped = rankedTicket(ticket({ mode: "ranked" }, 10_000), 1300);
    expect(shaped).toMatchObject({
      minCount: 4,
      maxCount: 4,
      numericProperties: { rating: 1300, band: cfg.matchmaking.maxBand },
      stringProperties: { game: "thulla", mode: "ranked" },
    });
    expect(shaped.query).toBe(
      "+properties.game:thulla +properties.mode:ranked +properties.rating:>=700 +properties.rating:<=1900",
    );
    expect(rankedTicket(ticket({ mode: "ranked" }), 100).query).toContain(
      "+properties.rating:>=0 +properties.rating:<=250",
    );
  });

  it("rewrites ranked tickets in the hook and leaves Quick Play alone", () => {
    const m = memoryNk();
    const ranked = beforeMatchmakerAdd(ctxFor("u0"), fakeLogger(), m.nk, {
      matchmakerAdd: ticket({ mode: "ranked" }, 0),
    });
    expect(ranked.matchmakerAdd.numericProperties).toMatchObject({ rating: cfg.startRating });
    expect(ranked.matchmakerAdd.stringProperties).toMatchObject({ mode: "ranked", uid: "u0" });
    const quick = beforeMatchmakerAdd(ctxFor("u0"), fakeLogger(), m.nk, {
      matchmakerAdd: ticket({ mode: "quick" }),
    });
    expect(quick.matchmakerAdd.minCount).toBe(3);
    expect(quick.matchmakerAdd.query).toBe("*");
  });

  it("refuses parties in Ranked", () => {
    const m = memoryNk();
    const party = (mode: string) => ({
      partyMatchmakerAdd: { ...ticket({ mode }), partyId: "p1" },
    });
    expect(() =>
      beforePartyMatchmakerAdd(ctxFor("u0"), fakeLogger(), m.nk, party("ranked")),
    ).toThrow("table.ranked_solo_only");
    expect(beforePartyMatchmakerAdd(ctxFor("u0"), fakeLogger(), m.nk, party("quick"))).toBeTruthy();
  });

  it("opens a ranked table with the ranked settings and no parties", () => {
    const created: CreateTableParams[] = [];
    const entries: MatchedEntry[] = [0, 1, 2, 3].map((i) => ({
      userId: `u${String(i)}`,
      partyId: i < 2 ? "p1" : null,
      properties: { game: "thulla", mode: "ranked" },
    }));
    tableForMatch(
      {
        runtime: { uuidv4: () => "0000", nowMs: () => S1 } as never,
        matchCreate: (_module, params) => {
          created.push(params);
          return "m1";
        },
      },
      entries,
    );
    expect(created[0]).toMatchObject({
      mode: "ranked",
      rules: {
        preset: "standard",
        settings: { cardMemoryAssist: false, spectators: false, voiceDefault: "pushToTalk" },
      },
      parties: [],
      autoStart: { expected: 4 },
    });
  });

  it("never creates a ranked table by code", () => {
    const out = tableCreate(
      {
        runtime: { uuidv4: () => "0000", nowMs: () => S1 } as never,
        userId: "u0",
        region: "local",
        store: { read: () => null, create: () => true, remove: () => undefined },
        matchCreate: () => "m1",
      },
      JSON.stringify({ mode: "ranked" }),
    );
    expect(out).toMatchObject({ ok: false, error: { detail: { reason: "mode" } } });
  });
});

describe("earn-only rewards", () => {
  it("the shop never sells a ranked card back", () => {
    const m = memoryNk();
    const bought = purchase(
      EMPTY_INVENTORY,
      BUNDLED_CATALOG,
      nkLedgerPorts(m.nk),
      "u0",
      "back_ranked_gold",
      S1,
    );
    expect(bought.response).toMatchObject({
      ok: false,
      error: { code: "economy.item_not_for_sale" },
    });
    const ids = (inventory: typeof EMPTY_INVENTORY) =>
      catalogView(inventory, BUNDLED_CATALOG, S1).items.map((i) => i.id);
    expect(ids(EMPTY_INVENTORY)).not.toContain("back_ranked_gold");
    expect(ids(grant(EMPTY_INVENTORY, "back_ranked_gold", S1))).toContain("back_ranked_gold");
    for (let week = 0; week < 20; week += 1) {
      expect(
        spotlight(BUNDLED_CATALOG, S1 + week * WEEK).some((id) => id.startsWith("back_ranked")),
      ).toBe(false);
    }
  });

  it("a ranked frame shows only once owned", () => {
    const profile = bootstrapProfile(
      { read: () => null, write: () => undefined, claimFriendCode: () => true },
      Xoshiro128StarStar.fromState(stateFromSeed(new Uint8Array([7]))),
      "u0",
      "player",
      S1,
    );
    const locked = applyProfileUpdate(profile, { frame: "ranked_gold_x" }, S1);
    expect(locked.ok).toBe(false);
    const refused = applyProfileUpdate(profile, { frame: "ranked_ustad" }, S1);
    expect(refused).toMatchObject({ ok: false, error: { code: "profile.frame_locked" } });
    const owned = applyProfileUpdate(profile, { frame: "ranked_ustad" }, S1, ["ranked_ustad"]);
    expect(owned.ok && owned.value.frame).toBe("ranked_ustad");
    expect(applyProfileUpdate(profile, { frame: "f01" }, S1).ok).toBe(true);
  });
});

describe("absence at the table (R46)", () => {
  const playOut = (h: Harness): void => {
    const roundId = h.state.round?.roundId ?? "";
    let guard = 0;
    while (h.state.phase === "inRound" && guard < 200) {
      const seat = h.state.round?.state.turnSeat ?? 0;
      if (h.state.seats[seat]?.connected === true) {
        h.send(`s${String(seat)}`, "play_card", { roundId, clientSeq: 500 + guard, card: 2 });
      } else {
        h.tick(5_000);
      }
      guard += 1;
    }
  };

  it("R46 records each seat's time away and whether it was there at the end", () => {
    const h = harness();
    opened(h);
    h.core.leave(h.state, [presence(1)], h.now);
    h.tick(20_000);
    h.join(presence(1));
    h.core.leave(h.state, [presence(2)], h.now);
    expect(broadcasts(h.tick(70_000)).length).toBeGreaterThanOrEqual(0);
    playOut(h);
    const record = h.records[0];
    expect(record?.completed).toBe(true);
    expect(record?.presentAtEnd).toEqual([true, true, false]);
    expect(record?.absentMs?.[0]).toBe(0);
    expect(record?.absentMs?.[1]).toBe(20_000);
    expect(record?.absentMs?.[2]).toBeGreaterThan(cfg.abandonAfterMs);
  });
});
