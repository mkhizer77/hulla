import { OPCODES } from "@hulla/protocol/generated";
import { describe, expect, it } from "vitest";

import { analyticsFromBatch } from "../match/analytics";
import { tableMatchHandler } from "../match/nakama_match";
import {
  ANALYTICS_LOG,
  emitAnalytics,
  emitAnalyticsSafely,
  forbiddenParam,
} from "../services/analytics";

import { fakeDispatcher, fakeMatchNk, matchMessage, nkPresence } from "./fake-match";
import { fakeLogger, type LogLine } from "./fake-nk";
import { harness, opened } from "./table-helpers";
import { toyHost } from "./toy-game";

import type { Batch, Envelope } from "../match/types";

const T0 = 1_760_200_000_000;

function sink(): { lines: LogLine[]; logger: ReturnType<typeof fakeLogger> } {
  const lines: LogLine[] = [];
  return { lines, logger: fakeLogger(lines) };
}

describe("server analytics emitters (GDD 23.3, ARCHITECTURE 12.4)", () => {
  it("refuses names, chat text, hands and cards, and accepts counts and ids", () => {
    expect(forbiddenParam("displayName", "Ali")).toBe("key displayName");
    expect(forbiddenParam("text", "salaam")).toBe("key text");
    expect(forbiddenParam("hand", ["AS"])).toBe("key hand");
    expect(forbiddenParam("led", "AS")).toBe("card in led");
    expect(forbiddenParam("played", ["3S", "KH"])).toBe("card list in played");
    expect(forbiddenParam("stats", [{ a: 1 }])).toBe("object list in stats");
    expect(forbiddenParam("nested", { a: 1 })).toBe("object in nested");
    expect(forbiddenParam("note", "x".repeat(65))).toBe("long text in note");
    expect(forbiddenParam("cards_count", 7)).toBeNull();
    expect(forbiddenParam("phrase_id", "t22")).toBeNull();
    const { lines, logger } = sink();
    emitAnalytics(logger, "pickup", { cards: 7 }, { tableId: "t_ABCDEF", roundId: "r_1", seat: 2 });
    expect(lines).toEqual([
      {
        level: "info",
        message: ANALYTICS_LOG,
        fields: { event: "pickup", tableId: "t_ABCDEF", roundId: "r_1", seat: 2, cards: 7 },
      },
    ]);
    expect(() => {
      emitAnalytics(
        logger,
        "escape",
        { rank: 1 },
        {
          seat: 1,
          ...({ hand: ["AS"] } as object),
        },
      );
    }).toThrow("key hand");
    emitAnalyticsSafely(logger, "escape", { rank: 1 }, { ...({ cards: ["AS"] } as object) });
    expect(lines.at(-1)).toMatchObject({ level: "warn", message: "analytics event dropped" });
  });

  it("maps the public game messages to the taxonomy with counts, never cards", () => {
    const h = harness();
    opened(h);
    const envelope = (type: string, payload: object, seat: number | null = null): Envelope => ({
      v: 1,
      type,
      tableId: h.state.tableId,
      seq: 1,
      ts: T0,
      payload,
      ...(seat === null ? {} : {}),
    });
    const batch: Batch = {
      outbound: [
        {
          envelope: envelope("round_start", {
            roundId: "r_1",
            seatCount: 3,
            handCounts: [17, 17, 18],
          }),
          to: { kind: "all" },
        },
        {
          envelope: envelope("move_applied", {
            roundId: "r_1",
            seat: 1,
            move: { type: "play_card", card: "AS" },
            trickIndex: 0,
            auto: true,
          }),
          to: { kind: "all" },
        },
        {
          envelope: envelope("trick_resolved", {
            roundId: "r_1",
            trickIndex: 2,
            cards: ["AS", "3S", "KS"],
            thullaBy: 2,
            picker: 0,
            nextLeader: 2,
            handCounts: [],
            escapes: [],
          }),
          to: { kind: "all" },
        },
        {
          envelope: envelope("trick_resolved", {
            roundId: "r_1",
            trickIndex: 3,
            cards: ["2H", "3H"],
            thullaBy: null,
            picker: null,
            nextLeader: 1,
            handCounts: [],
            escapes: [],
          }),
          to: { kind: "all" },
        },
        {
          envelope: envelope("pickup", {
            roundId: "r_1",
            seat: 0,
            cards: ["AS", "3S", "KS"],
            trickIndex: 2,
          }),
          to: { kind: "all" },
        },
        {
          envelope: envelope("hand_taken", { roundId: "r_1", taker: 1, target: 2, cardCount: 5 }),
          to: { kind: "all" },
        },
        {
          envelope: envelope("private_draw", {
            roundId: "r_1",
            seat: 0,
            card: "2S",
            source: "discard",
            from: null,
            autoLead: true,
          }),
          to: { kind: "session", sessionId: "s0" },
        },
        {
          envelope: envelope("private_draw", {
            roundId: "r_1",
            seat: 0,
            card: "2S",
            source: "opponent",
            from: 1,
            autoLead: true,
          }),
          to: { kind: "session", sessionId: "s0" },
        },
        {
          envelope: envelope("confirmed", {
            roundId: "r_1",
            bhabhiSeat: 2,
            cardsRemaining: 4,
            fastForward: true,
          }),
          to: { kind: "all" },
        },
        { envelope: envelope("escape", { roundId: "r_1", seat: 1, rank: 1 }), to: { kind: "all" } },
        {
          envelope: envelope("quick_chat_sent", { seat: 0, phraseId: "t22", pack: "v1" }),
          to: { kind: "all" },
        },
        {
          envelope: envelope("emote_sent", { seat: 2, emoteId: "e03", targetSeat: 0 }),
          to: { kind: "all" },
        },
        {
          envelope: envelope("presence", { seat: 1, playerId: "u1", status: "disconnected" }),
          to: { kind: "all" },
        },
        {
          envelope: envelope("presence", { seat: 1, playerId: "u1", status: "reconnected" }),
          to: { kind: "all" },
        },
        {
          envelope: envelope("round_end", {
            roundId: "r_1",
            escapeOrder: [1],
            bhabhiSeat: 2,
            bhabhiCards: 9,
            points: [0, 3, -1],
            stats: [],
            fairness: {},
          }),
          to: { kind: "all" },
        },
      ],
      kicks: [],
      end: false,
    };
    const events = analyticsFromBatch(h.state, batch, T0 + 60_000);
    expect(events.map((e) => e.name)).toEqual([
      "round_start",
      "move_played",
      "thulla",
      "pickup",
      "hand_taken",
      "empty_hand_draw",
      "endgame_draw",
      "confirmed",
      "escape",
      "quick_chat",
      "emote",
      "disconnect",
      "reconnect",
      "round_end",
    ]);
    expect(events[0]?.params).toEqual({
      mode: "private",
      seats: 3,
      humans: 3,
      bots: 0,
      preset_hash: "standard",
    });
    expect(events[1]).toMatchObject({
      params: { auto: true, timeout: true, decision_ms: 0 },
      seat: 1,
    });
    expect(events[2]).toMatchObject({
      params: { pile_size: 3, giver: 2, picker: 0, trick_index: 2 },
      seat: 2,
    });
    expect(events[3]?.params).toEqual({ cards: 3 });
    expect(events[4]?.params).toMatchObject({ cards: 5, players_active: 3 });
    expect(events[7]?.params).toEqual({ cards_remaining: 4, fast_forwarded: true });
    expect(events[9]?.params).toMatchObject({ phrase_id: "t22", pack: "v1", context: "round" });
    expect(events[10]?.params).toMatchObject({ phrase_id: "e03", target: true });
    expect(events[11]?.params).toEqual({ outcome: "dropped" });
    expect(events[13]?.params).toMatchObject({ bhabhi_cards: 9 });
    for (const e of events) {
      for (const [key, value] of Object.entries(e.params)) {
        expect(forbiddenParam(key, value), `${e.name}.${key}`).toBeNull();
      }
    }
  });

  it("logs one analytics line per event from the Nakama binding", () => {
    const handler = tableMatchHandler(toyHost(), { config: { autoPlayDelayMs: 0 } });
    const { nk } = fakeMatchNk();
    const { lines, logger } = sink();
    const d = fakeDispatcher();
    const ctx = {} as nkruntime.Context;
    const init = handler.matchInit(ctx, logger, nk, {
      code: "ABCDEF",
      gameId: "thulla",
      mode: "private",
    });
    let state = init.state;
    const presences = [0, 1, 2].map(nkPresence);
    for (const p of presences) {
      state =
        handler.matchJoinAttempt(ctx, logger, nk, d.dispatcher, 1, state, p, {})?.state ?? state;
    }
    state = handler.matchJoin(ctx, logger, nk, d.dispatcher, 1, state, presences)?.state ?? state;
    const ready = (p: nkruntime.Presence) =>
      matchMessage(p, OPCODES.ready.opcode, {
        v: 1,
        type: "ready",
        tableId: state.tableId,
        seq: 1,
        ts: 0,
        payload: { ready: true },
      });
    state =
      handler.matchLoop(ctx, logger, nk, d.dispatcher, 2, state, presences.map(ready))?.state ??
      state;
    const start = matchMessage(presences[0] ?? nkPresence(0), OPCODES.start_round.opcode, {
      v: 1,
      type: "start_round",
      tableId: state.tableId,
      seq: 2,
      ts: 0,
      payload: {},
    });
    handler.matchLoop(ctx, logger, nk, d.dispatcher, 3, state, [start]);
    const analytics = lines.filter((l) => l.message === ANALYTICS_LOG);
    expect(analytics.map((l) => l.fields?.["event"])).toContain("round_start");
    expect(analytics[0]?.fields).toMatchObject({
      tableId: state.tableId,
      gameId: "thulla",
      mode: "private",
    });
  });
});
