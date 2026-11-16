import { createChecks } from "@hulla/protocol/generated";
import { describe, expect, it } from "vitest";

import { hostFor } from "../match/registry";
import { tableCore, type PresenceInfo, type TableCore } from "../match/table_core";

import { T0, broadcasts, payloadOf, presence, sentTo } from "./table-helpers";

import type { AnyGameHost, Batch, Envelope, RoundRecord, TableState } from "../match/types";
import type { TableState as TableStatePayload } from "@hulla/protocol/generated";
import type { RungState } from "@hulla/rules-engine";

type AnyTable = TableState<unknown, unknown>;

interface Room {
  readonly state: AnyTable;
  readonly records: RoundRecord[];
  /** Everything the room sent, in order. */
  readonly sent: Batch["outbound"];
  now: number;
  join(p: PresenceInfo): Batch;
  send(sessionId: string, type: string, payload: object): Batch;
  tick(advanceMs?: number): Batch;
}

/** A room on the real registry, whose core follows the game it plays as the Nakama binding does. */
function room(gameId: "thulla" | "rung", players = 4): Room {
  let uuids = 0;
  const records: RoundRecord[] = [];
  const sent: Batch["outbound"] = [];
  const coreOf = (id: string): TableCore<unknown, unknown> => {
    const host = hostFor(id);
    if (host === null) throw new Error(`no host for ${id}`);
    return tableCore(host, {
      ids: {
        uuid: () => {
          uuids += 1;
          return `00000000-0000-4000-8000-${String(uuids).padStart(12, "0")}`;
        },
      },
      persist: { roundRecord: (record) => records.push(record) },
      games: (id2): AnyGameHost | null => hostFor(id2),
    });
  };
  const state = coreOf(gameId).create(
    { code: "ABCDEF", gameId, mode: "private", rules: { gameId } },
    T0,
  );
  const keep = (batch: Batch): Batch => {
    sent.push(...batch.outbound);
    return batch;
  };
  const r: Room = {
    state,
    records,
    sent,
    now: T0,
    join(p) {
      const core = coreOf(state.gameId);
      const decision = core.joinAttempt(state, p, {}, r.now);
      if (!decision.accept) throw new Error(`join rejected: ${decision.code}`);
      return keep(core.join(state, [p], r.now));
    },
    send(sessionId, type, payload) {
      const envelope: Envelope = { v: 1, type, tableId: state.tableId, seq: 1, ts: r.now, payload };
      return keep(
        coreOf(state.gameId).inbound(state, sessionId, JSON.stringify(envelope), r.now, type),
      );
    },
    tick(advanceMs = 0) {
      r.now += advanceMs;
      return keep(coreOf(state.gameId).tick(state, r.now));
    },
  };
  for (let i = 0; i < players; i += 1) r.join(presence(i));
  return r;
}

function start(r: Room): Batch {
  for (let i = 0; i < 4; i += 1) r.send(`s${String(i)}`, "ready", { ready: true });
  return r.send("s0", "start_round", {});
}

function rung(r: Room): RungState {
  const state = r.state.round?.state;
  if (state === undefined) throw new Error("no round");
  return state as RungState;
}

interface TurnStart {
  readonly seat: number;
  readonly legal: readonly string[];
  readonly phase: string;
  readonly suits?: readonly string[];
  readonly canRedeal?: boolean;
}

/** The latest turn_start the room sent to a seat's session. */
function lastTurn(r: Room, seat: number): TurnStart {
  for (let i = r.sent.length - 1; i >= 0; i -= 1) {
    const o = r.sent[i];
    if (o?.envelope.type === "turn_start" && o.to.kind === "session") {
      const turn = o.envelope.payload as TurnStart;
      if (turn.seat === seat) return turn;
    }
  }
  throw new Error(`no turn_start for seat ${String(seat)}`);
}

/** The acting seat plays what its turn_start offers: the first suit, the first card, or stop. */
function playTurn(r: Room, clientSeq: number): Batch {
  const state = rung(r);
  const seat = state.turnSeat;
  if (seat === null) throw new Error("nobody to act");
  const roundId = r.state.round?.roundId ?? "";
  const turn = lastTurn(r, seat);
  const session = `s${String(seat)}`;
  if (turn.phase === "call") {
    return r.send(session, "call_trump", { roundId, clientSeq, suit: turn.suits?.[0] });
  }
  if (turn.phase === "courtChoice") {
    return r.send(session, "court_choice", { roundId, clientSeq, playOn: false });
  }
  return r.send(session, "play_card", { roundId, clientSeq, card: turn.legal[0] });
}

function playDeal(r: Room): void {
  let clientSeq = 1;
  while (r.state.phase === "inRound" && r.state.round?.result === null) {
    const before = r.sent.length;
    playTurn(r, clientSeq);
    clientSeq += 1;
    const rejected = r.sent.slice(before).find((o) => o.envelope.type === "move_rejected");
    if (rejected !== undefined) throw new Error(JSON.stringify(rejected.envelope.payload));
    // Let the presentation delay pass so the next turn_start is out.
    r.tick(2_000);
    if (clientSeq > 80) throw new Error("the deal did not end");
  }
}

describe("a Rung table on the table core", () => {
  it("RG1 seats exactly four and refuses to deal to three", () => {
    const r = room("rung", 3);
    expect(r.state.seats).toHaveLength(4);
    for (let i = 0; i < 3; i += 1) r.send(`s${String(i)}`, "ready", { ready: true });
    const batch = r.send("s0", "start_round", {});
    expect(sentTo(batch, "s0").some((e) => e.type === "error")).toBe(true);
    expect(r.state.round).toBeNull();
  });

  it("RG4 RG5 the deal opens with five cards each in the Rung view and the caller's turn", () => {
    const r = room("rung");
    const batch = start(r);
    const table = payloadOf<TableStatePayload>(
      sentTo(batch, "s2").find((e) => e.type === "table_state"),
    );
    // Thulla's field stays empty; Rung's view is in its own.
    expect(table.round).toBeNull();
    expect(table.rungRound?.phase).toBe("call");
    expect(table.rungRound?.hand).toHaveLength(5);
    expect(table.rungRound?.stockCount).toBe(32);
    expect(JSON.stringify(table)).not.toContain('"stock"');
    const opened = broadcasts(batch).find((e) => e.type === "rung_round_start");
    const caller = payloadOf<{ callerSeat: number; dealerSeat: number }>(opened);
    expect(caller.callerSeat).toBe((caller.dealerSeat + 1) % 4);
    const turn = lastTurn(r, caller.callerSeat);
    expect(turn.phase).toBe("call");
    expect(turn.suits).toHaveLength(4);
    expect(turn.legal).toEqual([]);
    // Only the caller is asked.
    expect(batch.outbound.filter((o) => o.envelope.type === "turn_start")).toHaveLength(1);
  });

  it("RG6 RG8 the call is public and each seat gets only its own thirteen cards", () => {
    const r = room("rung");
    start(r);
    const caller = rung(r).callerSeat;
    const batch = playTurn(r, 1);
    const called = payloadOf<{ seat: number; clientSeq: number; auto: boolean }>(
      broadcasts(batch).find((e) => e.type === "rung_trump_called"),
    );
    expect(called).toMatchObject({ seat: caller, clientSeq: 1, auto: false });
    expect(broadcasts(batch).some((e) => e.type === "rung_hands_completed")).toBe(true);
    // A hand is never broadcast.
    expect(broadcasts(batch).some((e) => e.type === "rung_hand")).toBe(false);
    for (let seat = 0; seat < 4; seat += 1) {
      const hands = sentTo(batch, `s${String(seat)}`).filter((e) => e.type === "rung_hand");
      expect(hands).toHaveLength(1);
      expect(payloadOf<{ cards: string[] }>(hands[0]).cards).toEqual(rung(r).hands[seat]);
    }
  });

  it("a whole deal: every message matches its schema and the bazi score rides on the series", () => {
    const r = room("rung");
    start(r);
    playDeal(r);
    const checks = createChecks();
    for (const o of r.sent) {
      const check = checks.payload[o.envelope.type];
      expect(check, o.envelope.type).toBeDefined();
      expect(check?.(o.envelope.payload), JSON.stringify(o.envelope)).toBe(true);
    }
    const end = r.sent.find((o) => o.envelope.type === "rung_round_end");
    const result = payloadOf<{
      winnerTeam: number;
      counts: number[];
      points: number[];
      dealPoints: number;
      fairness: { deckOrder: string[] };
      series: { teams: { points: number[]; dealsWon: number[] }; roundsPlayed: number };
    }>(end?.envelope);
    expect(result.fairness.deckOrder).toHaveLength(52);
    expect(result.series.roundsPlayed).toBe(1);
    expect(result.series.teams.dealsWon[result.winnerTeam]).toBe(1);
    expect(result.series.teams.points[result.winnerTeam]).toBe(result.dealPoints);
    expect(result.points.filter((p) => p > 0)).toHaveLength(2);
    // The record is Rung's, with a move for every play and the per-seat tally.
    expect(r.records).toHaveLength(1);
    expect(r.records[0]).toMatchObject({ gameId: "rung", completed: true });
    const tricks = (r.records[0]?.highlights ?? []).reduce((n, h) => n + (h["tricksWon"] ?? 0), 0);
    expect(tricks).toBeGreaterThan(0);
    expect(r.records[0]?.highlights.reduce((n, h) => n + (h["calls"] ?? 0), 0)).toBe(1);
  });

  it("RG42 a table nobody touches plays the deal out on the timers", () => {
    const r = room("rung");
    start(r);
    for (let i = 0; i < 400 && r.state.round?.result === null; i += 1) r.tick(5_000);
    expect(r.state.round?.result).not.toBeNull();
    const plays = r.sent.filter((o) => o.envelope.type === "rung_card_played");
    expect(plays.every((o) => (o.envelope.payload as { auto: boolean }).auto)).toBe(true);
  });

  it("refuses Thulla's moves and a card out of turn at a Rung table", () => {
    const r = room("rung");
    start(r);
    const roundId = r.state.round?.roundId ?? "";
    const caller = rung(r).callerSeat;
    const other = `s${String((caller + 1) % 4)}`;
    const take = r.send(other, "take_hand", { roundId, clientSeq: 1 });
    expect(sentTo(take, other).some((e) => e.type === "error")).toBe(true);
    const early = r.send(other, "call_trump", { roundId, clientSeq: 2, suit: "S" });
    expect(sentTo(early, other).some((e) => e.type === "move_rejected")).toBe(true);
    const bad = r.send(`s${String(caller)}`, "call_trump", { roundId, clientSeq: 3, suit: "X" });
    expect(sentTo(bad, `s${String(caller)}`).some((e) => e.type === "move_rejected")).toBe(true);
    expect(rung(r).trump).toBeNull();
  });
});

describe("switching a real room between Thulla and Rung", () => {
  it("four friends leave a Thulla lobby for Rung, play a deal and come back", () => {
    const r = room("thulla");
    const changed = r.send("s0", "change_game", { gameId: "rung" });
    expect(broadcasts(changed).some((e) => e.type === "game_changed")).toBe(true);
    expect(r.state.gameId).toBe("rung");
    expect(r.state.rules).toMatchObject({ gameId: "rung" });
    expect(r.state.seats.map((s) => s.playerId)).toEqual(["u0", "u1", "u2", "u3"]);
    start(r);
    playDeal(r);
    expect(r.records.map((x) => x.gameId)).toEqual(["rung"]);
    const rungSeries = r.state.series;
    expect(rungSeries?.roundsPlayed).toBe(1);

    // Back to Thulla: the Rung bazi is kept for the next switch.
    r.send("s0", "change_game", { gameId: "thulla" });
    expect(r.state.gameId).toBe("thulla");
    expect(r.state.seriesByGame?.rung).toEqual(rungSeries);
    const batch = start(r);
    const table = payloadOf<TableStatePayload>(
      sentTo(batch, "s1").find((e) => e.type === "table_state"),
    );
    expect(table.gameId).toBe("thulla");
    expect(table.round).not.toBeNull();
    expect(table.rungRound).toBeUndefined();
    expect(broadcasts(batch).some((e) => e.type === "round_start")).toBe(true);

    // And Rung again resumes the bazi where it stopped.
    r.send("s0", "change_game", { gameId: "rung" });
    expect(r.state.queuedGame?.gameId).toBe("rung");
  });

  it("a fifth Thulla player becomes a watcher who sits next when the room moves to Rung", () => {
    const r = room("thulla", 5);
    r.send("s0", "change_game", { gameId: "rung" });
    expect(r.state.seats).toHaveLength(4);
    expect(r.state.presences["s4"]).toMatchObject({ seat: null, seatNext: true });
  });
});
