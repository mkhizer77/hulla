import { describe, expect, it } from "vitest";

import { thullaMatchInit } from "../games/thulla/match";
import { botsFor, MAX_BOTS } from "../match/bots";
import { DEFAULT_TABLE_CONFIG } from "../match/types";
import { quickPlayBots } from "../rpc/quick_play_bots";

import { fakeLogger, memoryNk } from "./fake-nk";
import { harness, payloadOf, presence, sentTo } from "./table-helpers";

import type { CreateTableParams } from "../match/types";

describe("Quick Play with bots for a lone player (owner decision, 29 Oct)", () => {
  it("fills everyone else's seats with bots, at most three", () => {
    expect(botsFor(3)).toBe(2);
    expect(botsFor(4)).toBe(3);
    expect(botsFor(6)).toBe(MAX_BOTS);
  });

  it("opens with bots seated, ready, present and on auto-play, each with its own name and face", () => {
    const h = harness({ bots: 3 });
    const bots = h.state.seats.filter((s) => s.bot);
    expect(bots).toHaveLength(3);
    for (const bot of bots) {
      expect(bot).toMatchObject({ ready: true, connected: true, autoPlay: true, rematch: true });
    }
    expect(new Set(bots.map((b) => b.displayName)).size).toBe(3);
    expect(new Set(bots.map((b) => b.avatar)).size).toBe(3);
  });

  it("deals once the player sits, marks bots in table_state and keeps them out of the round record", () => {
    const h = harness({ bots: 3 });
    const joined = h.join(presence(0));
    const state = payloadOf<{ seats: { bot?: boolean; playerId: string | null }[] }>(
      sentTo(joined, "s0").find((e) => e.type === "table_state"),
    );
    expect(state.seats.filter((s) => s.bot === true)).toHaveLength(3);
    expect(state.seats.find((s) => s.playerId === "u0")?.bot).toBeUndefined();
    expect(h.state.autoStart?.startsAtTs).not.toBeNull();
    h.tick(DEFAULT_TABLE_CONFIG.autoStartCountdownMs);
    expect(h.state.phase).toBe("inRound");
    for (let i = 0; i < 400 && h.state.phase === "inRound"; i += 1) h.tick(1_000);
    expect(h.records.length).toBeGreaterThan(0);
    const players = h.records[0]?.players ?? [];
    expect(players.filter((p) => p !== null)).toEqual(["u0"]);
  });

  it("renames a bot that shares the arriving player's name", () => {
    const h = harness({ bots: 3 });
    const clash = h.state.seats.find((s) => s.bot)?.displayName ?? "";
    h.join({ sessionId: "s0", userId: "u0", username: clash });
    const names = h.state.seats.filter((s) => s.playerId !== null).map((s) => s.displayName);
    expect(new Set(names).size).toBe(names.length);
  });

  it("closes when the player leaves, and never deals to bots alone", () => {
    const h = harness({ bots: 2 });
    h.join(presence(0));
    h.send("s0", "leave_table", {});
    h.tick(200);
    expect(h.state.phase).toBe("closing");

    const lonely = harness({ bots: 3 });
    lonely.tick(DEFAULT_TABLE_CONFIG.autoStartJoinWindowMs + 1);
    expect(lonely.state.phase).toBe("closing");
  });

  it("Nakama's match init keeps the bot count from the create params", () => {
    const ctx = { matchId: "m1.n1", env: {} } as unknown as nkruntime.Context;
    const init = thullaMatchInit(ctx, fakeLogger(), memoryNk().nk, {
      code: "ABCDEF",
      gameId: "thulla",
      mode: "public",
      autoStart: { expected: 3 },
      bots: 2,
    });
    const state = init.state as { seats: { bot: boolean }[] };
    expect(state.seats.filter((s) => s.bot)).toHaveLength(2);
  });

  it("the RPC opens a standard public table with bots for the caller", () => {
    let created: CreateTableParams | null = null;
    const response = quickPlayBots(
      {
        runtime: {
          uuidv4: () => "00000000-0000-4000-8000-000000000001",
          nowMs: () => 0,
        } as never,
        userId: "u1",
        matchCreate: (_module, params) => {
          created = params;
          return "match.1";
        },
      },
      JSON.stringify({ seats: 3 }),
    );
    expect(response).toMatchObject({ ok: true, matchId: "match.1" });
    expect(created).toMatchObject({ mode: "public", bots: 2, autoStart: { expected: 3 } });
    expect(
      quickPlayBots({ runtime: {} as never, userId: null, matchCreate: () => "" }, "{}"),
    ).toMatchObject({
      ok: false,
    });
  });
});
