import { describe, expect, it } from "vitest";

import { tableCore, type PresenceInfo, type TableCore } from "../match/table_core";

import { T0, broadcasts, payloadOf, presence, sentTo, types } from "./table-helpers";
import { toyHost, toyModule, type ToyMove, type ToyState } from "./toy-game";

import type { AnyGameHost, Batch, Envelope, TableState } from "../match/types";
import type { TableState as TableStatePayload } from "@hulla/protocol/generated";

/** Two toy games: the first seats up to four ("thulla"), the second exactly three ("rung"). */
const FOUR = toyHost();
const THREE = { ...toyHost(), module: { ...toyModule, id: "rung", seats: { min: 3, max: 3 } } };
const GAMES: Record<string, AnyGameHost> = {
  thulla: FOUR,
  rung: THREE as unknown as AnyGameHost,
};

interface Room {
  readonly state: TableState<ToyState, ToyMove>;
  now: number;
  join(p: PresenceInfo): Batch;
  send(sessionId: string, type: string, payload: object): Batch;
  tick(advanceMs?: number): Batch;
}

/** A room whose core follows the game it plays, as the Nakama binding does. */
function room(mode: "private" | "public" = "private", players = 0): Room {
  let uuids = 0;
  const coreOf = (gameId: string): TableCore<ToyState, ToyMove> =>
    tableCore((gameId === "rung" ? THREE : FOUR) as typeof FOUR, {
      ids: {
        uuid: () => {
          uuids += 1;
          return `00000000-0000-4000-8000-${String(uuids).padStart(12, "0")}`;
        },
      },
      persist: { roundRecord: () => undefined },
      games: (gameId) => GAMES[gameId] ?? null,
    });
  const state = coreOf("thulla").create(
    {
      code: "ABCDEF",
      gameId: "thulla",
      mode,
      rules: { gameId: "thulla", overrides: { target: 4 } },
    },
    T0,
  );
  const r: Room = {
    state,
    now: T0,
    join(p) {
      const core = coreOf(state.gameId);
      const decision = core.joinAttempt(state, p, {}, r.now);
      if (!decision.accept) throw new Error(`join rejected: ${decision.code}`);
      return core.join(state, [p], r.now);
    },
    send(sessionId, type, payload) {
      const envelope: Envelope = { v: 1, type, tableId: state.tableId, seq: 1, ts: r.now, payload };
      return coreOf(state.gameId).inbound(state, sessionId, JSON.stringify(envelope), r.now, type);
    },
    tick(advanceMs = 0) {
      r.now += advanceMs;
      return coreOf(state.gameId).tick(state, r.now);
    },
  };
  for (let i = 0; i < players; i += 1) r.join(presence(i));
  return r;
}

function errorOf(batch: Batch, sessionId: string): unknown {
  const error = sentTo(batch, sessionId).find((e) => e.type === "error");
  if (error === undefined) return null;
  const payload = payloadOf<{ code: string; detail?: unknown }>(error);
  return payload.detail === undefined ? payload.code : [payload.code, payload.detail];
}

function seatsOf(r: Room): (string | null)[] {
  return r.state.seats.map((slot) => slot.playerId);
}

function startRound(r: Room, count: number): string {
  for (let i = 0; i < count; i += 1) r.send(`s${String(i)}`, "ready", { ready: true });
  r.send("s0", "start_round", {});
  const roundId = r.state.round?.roundId ?? "";
  for (let i = 0; i < count; i += 1) {
    r.send(`s${String(i)}`, "play_card", { roundId, clientSeq: 1, card: 1 });
  }
  return roundId;
}

/** With a target of four and three seats, seat 0's first turn ends the round. */
function finishRound(r: Room, roundId: string): Batch {
  return r.send("s0", "play_card", { roundId, clientSeq: 2, card: 1 });
}

describe("switching a room's game", () => {
  it("the host switches in the lobby and the room keeps its id and code", () => {
    const r = room("private", 3);
    const before = { tableId: r.state.tableId, code: r.state.code };
    const batch = r.send("s0", "change_game", { gameId: "rung" });
    expect(r.state.gameId).toBe("rung");
    expect({ tableId: r.state.tableId, code: r.state.code }).toEqual(before);
    expect(r.state.rules).toEqual({ gameId: "rung" });
    expect(r.state.seats).toHaveLength(3);
    expect(r.state.phase).toBe("lobby");
    const changed = broadcasts(batch).find((e) => e.type === "game_changed");
    expect(payloadOf(changed)).toEqual({ gameId: "rung", rules: { gameId: "rung" }, changedBy: 0 });
    for (const sessionId of ["s0", "s1", "s2"]) {
      const table = sentTo(batch, sessionId).find((e) => e.type === "table_state");
      expect(payloadOf<TableStatePayload>(table).gameId).toBe("rung");
    }
  });

  it("everyone keeps their session, seat and microphone through the switch", () => {
    const r = room("private", 3);
    r.send("s1", "set_mic", { mic: true });
    const sessions = Object.keys(r.state.presences).sort();
    const batch = r.send("s0", "change_game", { gameId: "rung" });
    expect(Object.keys(r.state.presences).sort()).toEqual(sessions);
    expect(seatsOf(r)).toEqual(["u0", "u1", "u2"]);
    expect(r.state.seats[1]?.mic).toBe(true);
    // Nobody is dropped from the socket or from the voice room.
    expect(batch.kicks).toEqual([]);
    expect(batch.voiceRemovals).toEqual([]);
    expect(batch.end).toBe(false);
  });

  it("refuses anyone but the host, an unknown game, the same game and a matched table", () => {
    const r = room("private", 3);
    expect(errorOf(r.send("s1", "change_game", { gameId: "rung" }), "s1")).toBe("table.not_host");
    expect(errorOf(r.send("s0", "change_game", { gameId: "ludo" }), "s0")).toEqual([
      "table.invalid_payload",
      { reason: "schema", type: "change_game" },
    ]);
    expect(errorOf(r.send("s0", "change_game", { gameId: "thulla" }), "s0")).toEqual([
      "table.invalid_payload",
      { reason: "same_game" },
    ]);
    expect(r.state.gameId).toBe("thulla");
    const matched = room("public", 3);
    expect(errorOf(matched.send("s0", "change_game", { gameId: "rung" }), "s0")).toEqual([
      "table.locked",
      { reason: "mode" },
    ]);
  });

  it("refuses rules for another game and rules the new game does not accept", () => {
    const r = room("private", 3);
    const wrong = r.send("s0", "change_game", { gameId: "rung", rules: { gameId: "thulla" } });
    expect(errorOf(wrong, "s0")).toEqual(["table.invalid_payload", { reason: "game_id" }]);
    const bad = r.send("s0", "change_game", {
      gameId: "rung",
      rules: { gameId: "rung", overrides: { target: 0 } },
    });
    expect(errorOf(bad, "s0")).toEqual(["rules.invalid_rules", { key: "target" }]);
    expect(r.state.gameId).toBe("thulla");
  });

  it("a table without other games refuses the switch", () => {
    let uuids = 0;
    const core = tableCore(FOUR, {
      ids: { uuid: () => `00000000-0000-4000-8000-${String((uuids += 1)).padStart(12, "0")}` },
      persist: { roundRecord: () => undefined },
    });
    const state = core.create({ code: "ABCDEF", gameId: "thulla", mode: "private" }, T0);
    expect(core.joinAttempt(state, presence(0), {}, T0).accept).toBe(true);
    core.join(state, [presence(0)], T0);
    const envelope = {
      v: 1,
      type: "change_game",
      tableId: state.tableId,
      seq: 1,
      ts: T0,
      payload: { gameId: "rung" },
    };
    const batch = core.inbound(state, "s0", JSON.stringify(envelope), T0, "change_game");
    expect(errorOf(batch, "s0")).toEqual(["table.invalid_payload", { reason: "game_id" }]);
  });

  it("with more players than seats the extra player waits in the room, first in line", () => {
    const r = room("private", 4);
    const batch = r.send("s0", "change_game", { gameId: "rung" });
    expect(seatsOf(r)).toEqual(["u0", "u1", "u2"]);
    const waiting = r.state.presences["s3"];
    expect(waiting?.seat).toBeNull();
    expect(waiting?.seatNext).toBe(true);
    const presenceEvent = broadcasts(batch).find((e) => e.type === "presence");
    expect(payloadOf<{ status: string; playerId: string }>(presenceEvent)).toMatchObject({
      status: "spectating",
      playerId: "u3",
      seat: null,
    });
    expect(batch.kicks).toEqual([]);
    expect(batch.voiceRemovals).toEqual([]);
  });

  it("switching back to the bigger game seats the waiting player again", () => {
    const r = room("private", 4);
    r.send("s0", "change_game", { gameId: "rung" });
    const batch = r.send("s0", "change_game", { gameId: "thulla" });
    expect(seatsOf(r)).toEqual(["u0", "u1", "u2", "u3"]);
    expect(r.state.presences["s3"]?.seat).toBe(3);
    expect(types(batch)).toContain("game_changed");
  });

  it("the host is never the one left without a seat", () => {
    const r = room("private", 4);
    r.send("s0", "transfer_host", { seat: 3 });
    expect(r.state.hostSeat).toBe(3);
    const batch = r.send("s3", "change_game", { gameId: "rung" });
    expect(seatsOf(r)).toEqual(["u0", "u1", "u3"]);
    expect(r.state.hostSeat).toBe(2);
    expect(r.state.presences["s3"]?.seat).toBe(2);
    expect(r.state.presences["s2"]?.seat).toBeNull();
    const changed = broadcasts(batch).find((e) => e.type === "game_changed");
    expect(payloadOf<{ changedBy: number }>(changed).changedBy).toBe(2);
  });

  it("each game keeps its rules and its score for when the room comes back", () => {
    const r = room("private", 3);
    finishRound(r, startRound(r, 3));
    expect(r.state.phase).toBe("roundEnded");
    expect(r.state.series?.roundsPlayed).toBe(1);
    const score = r.state.series;
    r.send("s0", "change_game", {
      gameId: "rung",
      rules: { gameId: "rung", overrides: { target: 6 } },
    });
    expect(r.state.series).toBeNull();
    expect(r.state.phase).toBe("lobby");
    expect(r.state.round).toBeNull();
    expect(r.state.rematchDeadlineTs).toBeNull();
    expect(r.state.seats.every((slot) => !slot.ready && slot.rematch === null)).toBe(true);
    r.send("s0", "change_game", { gameId: "thulla" });
    expect(r.state.rules).toEqual({ gameId: "thulla", overrides: { target: 4 } });
    expect(r.state.series).toEqual(score);
    r.send("s0", "change_game", { gameId: "rung" });
    expect(r.state.rules).toEqual({ gameId: "rung", overrides: { target: 6 } });
  });

  it("the new game deals a round to the same seats", () => {
    const r = room("private", 3);
    r.send("s0", "change_game", {
      gameId: "rung",
      rules: { gameId: "rung", overrides: { target: 4 } },
    });
    const roundId = startRound(r, 3);
    expect(r.state.phase).toBe("inRound");
    expect(roundId).toBe("r_1");
    finishRound(r, roundId);
    expect(r.state.phase).toBe("roundEnded");
    expect(r.state.series?.roundsPlayed).toBe(1);
  });

  it("a switch asked for during a round waits for the round to end", () => {
    const r = room("private", 3);
    const roundId = startRound(r, 3);
    const queued = r.send("s0", "change_game", { gameId: "rung" });
    expect(r.state.gameId).toBe("thulla");
    expect(r.state.phase).toBe("inRound");
    expect(r.state.queuedGame).toEqual({ gameId: "rung", rules: { gameId: "rung" }, by: 0 });
    const table = sentTo(queued, "s1").find((e) => e.type === "table_state");
    expect(payloadOf<TableStatePayload>(table).queuedGame).toEqual({
      gameId: "rung",
      rules: { gameId: "rung" },
    });
    expect(types(queued)).not.toContain("game_changed");
    const ended = finishRound(r, roundId);
    expect(r.state.gameId).toBe("rung");
    expect(r.state.phase).toBe("lobby");
    expect(r.state.queuedGame).toBeNull();
    expect(types(ended)).toContain("game_changed");
    // The finished round still counted for the game it was played in.
    expect(r.state.seriesByGame?.thulla?.roundsPlayed).toBe(1);
  });

  it("naming the game already played takes a queued switch back", () => {
    const r = room("private", 3);
    const roundId = startRound(r, 3);
    r.send("s0", "change_game", { gameId: "rung" });
    const batch = r.send("s0", "change_game", { gameId: "thulla" });
    expect(r.state.queuedGame).toBeNull();
    expect(errorOf(batch, "s0")).toBeNull();
    finishRound(r, roundId);
    expect(r.state.gameId).toBe("thulla");
    expect(r.state.phase).toBe("roundEnded");
  });

  it("the room state survives Nakama's plain-data round trip after a switch", () => {
    const r = room("private", 4);
    r.send("s0", "change_game", { gameId: "rung" });
    expect(JSON.parse(JSON.stringify(r.state))).toEqual(r.state);
  });
});
