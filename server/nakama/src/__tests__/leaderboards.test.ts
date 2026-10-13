import { describe, expect, it } from "vitest";

import { onLeaderboardReset, rpcLeaderboardGet } from "../rpc/leaderboards";
import {
  allowedPoints,
  BOARDS,
  boardView,
  ensureLeaderboards,
  gemsForRank,
  payTopBrackets,
  pointsEarned,
  postRoundPoints,
  PRIVATE_POINTS_DAILY_CAP,
} from "../services/leaderboards";
import { nkLedgerPorts } from "../services/ledger";

import { fakeLogger, memoryNk } from "./fake-nk";

import type { RoundRecord } from "../match/types";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const ctxFor = (userId: string): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env: {} }) as unknown as nkruntime.Context;
const T0 = Date.UTC(2026, 9, 13, 12);

function record(mode: "private" | "public", roundId = "r_1"): RoundRecord {
  return {
    tableId: "t_ABCDEF",
    roundId,
    gameId: "thulla",
    rulesVersion: "1",
    rules: { gameId: "thulla" },
    mode,
    dealerSeat: 0,
    seedHex: "00",
    players: ["u0", "u1", "u2", "u3"],
    moves: [],
    result: { gameId: "thulla", finishOrder: [0, 1, 2], loserSeat: 3, points: [3, 2, 1, -1] },
    series: null,
    finalStateHash: "h",
    highlights: [{}, {}, {}, {}],
    startedTs: T0,
    endedTs: T0 + 1000,
    completed: true,
  };
}

describe("Baithak points on the boards (GDD 12.8, R50)", () => {
  it("counts the positive side of R50 and caps private rooms per day", () => {
    const r = record("private");
    expect([0, 1, 2, 3].map((seat) => pointsEarned(r, seat))).toEqual([3, 2, 1, 0]);
    expect(allowedPoints(3, "public", 999)).toBe(3);
    expect(allowedPoints(3, "private", 0)).toBe(3);
    expect(allowedPoints(3, "private", PRIVATE_POINTS_DAILY_CAP - 1)).toBe(1);
    expect(allowedPoints(3, "private", PRIVATE_POINTS_DAILY_CAP)).toBe(0);
    expect(gemsForRank(1)).toBe(20);
    expect(gemsForRank(3)).toBe(10);
    expect(gemsForRank(10)).toBe(5);
    expect(gemsForRank(11)).toBe(0);
  });

  it("posts to both boards, stops private rooms at the cap, and serves global and friends views", () => {
    const m = memoryNk();
    const log = fakeLogger();
    ensureLeaderboards(m.nk, log);
    expect([...m.boards.keys()].sort()).toEqual([BOARDS.monthly.id, BOARDS.weekly.id]);
    let posted = 0;
    for (let i = 0; i < 12; i += 1)
      posted += postRoundPoints(m.nk, log, record("private", `r_${String(i)}`), "u0", 0);
    expect(posted).toBe(PRIVATE_POINTS_DAILY_CAP);
    expect(m.boards.get(BOARDS.weekly.id)?.get("u0")).toMatchObject({
      score: PRIVATE_POINTS_DAILY_CAP,
      numScore: 10,
    });
    expect(m.object("progress", "state", "u0")).toMatchObject({
      privatePointsToday: PRIVATE_POINTS_DAILY_CAP,
    });
    expect(postRoundPoints(m.nk, log, record("public"), "u0", 0)).toBe(3);
    expect(postRoundPoints(m.nk, log, record("public"), "u3", 3)).toBe(0);
    postRoundPoints(m.nk, log, record("public"), "u1", 1);
    postRoundPoints(m.nk, log, record("public"), "u2", 2);

    const global = boardView(m.nk, "u2", "weekly", "global", [], 10, undefined);
    expect(global.ok).toBe(true);
    if (!global.ok) return;
    expect(global.rows.map((r) => [r.userId, r.score, r.rank])).toEqual([
      ["u0", 33, 1],
      ["u1", 2, 2],
      ["u2", 1, 3],
    ]);
    expect(global.me).toMatchObject({ userId: "u2", rank: 3 });
    expect(global.nextResetTs).toBe(1_800_000_000_000);
    const friends = boardView(m.nk, "u2", "monthly", "friends", ["u1"], 10, undefined);
    expect(friends.ok && friends.rows.map((r) => r.userId)).toEqual(["u1", "u2"]);
    // No friends: the friends view holds the caller alone, never the global page.
    const alone = boardView(m.nk, "u2", "monthly", "friends", [], 10, undefined);
    expect(alone.ok && alone.rows.map((r) => r.userId)).toEqual(["u2"]);
    const stranger = boardView(m.nk, "nobody", "monthly", "friends", [], 10, undefined);
    expect(stranger.ok && stranger.rows).toEqual([]);
    expect(stranger.ok && stranger.me).toBeNull();
    m.nk.friendsAdd("u2", "user_u2", ["u1"], [], {});
    m.nk.friendsAdd("u1", "user_u1", ["u2"], [], {});
    const viaRpc = parse(
      rpcLeaderboardGet(
        ctxFor("u2"),
        log,
        m.nk,
        JSON.stringify({ board: "weekly", scope: "friends" }),
      ),
    );
    expect((viaRpc["rows"] as Json[]).map((r) => r["userId"])).toEqual(["u1", "u2"]);
    expect(
      parse(rpcLeaderboardGet(ctxFor("u2"), log, m.nk, JSON.stringify({ board: "yearly" }))),
    ).toMatchObject({
      error: { code: "table.invalid_payload" },
    });
    expect(
      parse(rpcLeaderboardGet({ env: {} } as unknown as nkruntime.Context, log, m.nk, "{}")),
    ).toMatchObject({ ok: false });
  });

  it("pays the top brackets once when a board resets", () => {
    const m = memoryNk();
    const log = fakeLogger();
    ensureLeaderboards(m.nk, log);
    for (let i = 0; i < 12; i += 1) {
      m.nk.leaderboardRecordWrite(BOARDS.weekly.id, `p${String(i)}`, undefined, 100 - i, 0, {});
    }
    const ledger = nkLedgerPorts(m.nk);
    expect(payTopBrackets(m.nk, ledger, BOARDS.weekly.id, 0)).toEqual({ paid: 10 });
    expect(ledger.balances("p0")).toEqual({ coins: 0, gems: 20 });
    expect(ledger.balances("p2")).toEqual({ coins: 0, gems: 10 });
    expect(ledger.balances("p9")).toEqual({ coins: 0, gems: 5 });
    expect(ledger.balances("p10")).toEqual({ coins: 0, gems: 0 });
    expect(payTopBrackets(m.nk, ledger, BOARDS.weekly.id, 0)).toEqual({ paid: 0 });
    onLeaderboardReset(
      {} as nkruntime.Context,
      log,
      m.nk,
      { id: BOARDS.weekly.id } as nkruntime.Leaderboard,
      0,
    );
    onLeaderboardReset(
      {} as nkruntime.Context,
      log,
      m.nk,
      { id: "other" } as nkruntime.Leaderboard,
      0,
    );
    expect(ledger.balances("p0")).toEqual({ coins: 0, gems: 20 });
  });
});
