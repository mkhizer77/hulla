import { rungBot, rungModule, type RungMove, type RungState } from "@hulla/rules-engine";
import { describe, expect, it } from "vitest";

import { rungHost } from "../games/rung/host";
import { hostFor } from "../match/registry";
import { tableCore, type PresenceInfo, type TableCore } from "../match/table_core";
import { botBench } from "../rpc/ops";

import { T0, payloadOf, presence, sentTo } from "./table-helpers";

import type { AnyGameHost, Batch, Envelope, TableState } from "../match/types";
import type { TableState as TableStatePayload } from "@hulla/protocol/generated";

interface Room {
  readonly state: TableState<unknown, unknown>;
  readonly sent: Batch["outbound"];
  /** Seats the bot policy was asked to play for, in order. */
  readonly botAsked: number[];
  now: number;
  join(p: PresenceInfo): Batch;
  send(sessionId: string, type: string, payload: object): Batch;
  tick(advanceMs?: number): Batch;
}

/** A room on the real hosts; Rung's bot is wrapped so the test sees which seats it plays. */
function room(gameId: "thulla" | "rung", players: number): Room {
  let uuids = 0;
  const botAsked: number[] = [];
  const spied = {
    ...rungHost,
    module: {
      ...rungModule,
      bot: {
        ...rungBot,
        choose: (state: RungState, seat: number, memory: unknown, rng: never): RungMove | null => {
          botAsked.push(seat);
          return rungBot.choose(state, seat, memory, rng);
        },
      },
    },
  } as unknown as AnyGameHost;
  const games = (id: string): AnyGameHost | null => (id === "rung" ? spied : hostFor(id));
  const coreOf = (id: string): TableCore<unknown, unknown> => {
    const host = games(id);
    if (host === null) throw new Error(`no host for ${id}`);
    return tableCore(host, {
      ids: {
        uuid: () => {
          uuids += 1;
          return `00000000-0000-4000-8000-${String(uuids).padStart(12, "0")}`;
        },
      },
      persist: { roundRecord: () => undefined },
      games,
    });
  };
  const state = coreOf(gameId).create(
    { code: "ABCDEF", gameId, mode: "private", rules: { gameId } },
    T0,
  );
  const sent: Batch["outbound"] = [];
  const keep = (batch: Batch): Batch => {
    sent.push(...batch.outbound);
    return batch;
  };
  const r: Room = {
    state,
    sent,
    botAsked,
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

function errorOf(batch: Batch, sessionId: string): unknown {
  const error = sentTo(batch, sessionId).find((e) => e.type === "error");
  if (error === undefined) return null;
  const payload = payloadOf<{ code: string; detail?: { reason?: string } }>(error);
  return payload.detail?.reason ?? payload.code;
}

function rung(r: Room): RungState {
  return r.state.round?.state as RungState;
}

function start(r: Room): void {
  r.state.seats.forEach((slot, i) => {
    if (slot.playerId !== null && !slot.bot) r.send(`s${String(i)}`, "ready", { ready: true });
  });
  r.send("s0", "start_round", {});
  if (r.state.round === null) throw new Error("the round did not start");
}

/** The seat to act plays the timeout policy's move itself, as a present player. */
function act(r: Room, clientSeq: number): void {
  const state = rung(r);
  const seat = state.turnSeat ?? 0;
  const move = rungModule.onDeadline(state, seat);
  if (move === null) throw new Error("no move");
  const { type, ...rest } = move;
  r.send(`s${String(seat)}`, type, { roundId: r.state.round?.roundId, clientSeq, ...rest });
}

describe("Rung: bots fill empty seats in a private room (design 6.5)", () => {
  it("the host adds bots to empty seats, removes one, and only the host may", () => {
    const r = room("rung", 2);
    expect(errorOf(r.send("s1", "add_bot", {}), "s1")).toBe("table.not_host");
    const batch = r.send("s0", "add_bot", {});
    const table = payloadOf<TableStatePayload>(
      sentTo(batch, "s1").find((e) => e.type === "table_state"),
    );
    expect(table.seats[2]).toMatchObject({ bot: true, ready: true, connected: true });
    expect(table.seats[2]?.displayName).not.toBeNull();
    r.send("s0", "add_bot", { seat: 3 });
    expect(r.state.seats.map((s) => s.bot)).toEqual([false, false, true, true]);
    expect(new Set(r.state.seats.map((s) => s.displayName)).size).toBe(4);
    // No seat left, and a taken seat is refused.
    expect(errorOf(r.send("s0", "add_bot", {}), "s0")).toBe("seat");
    expect(errorOf(r.send("s0", "remove_bot", { seat: 1 }), "s0")).toBe("seat");
    r.send("s0", "remove_bot", { seat: 3 });
    expect(r.state.seats[3]?.playerId).toBeNull();
  });

  it("one person and three bots play a whole deal, and the bots think before they play", () => {
    const r = room("rung", 1);
    for (let i = 0; i < 3; i += 1) r.send("s0", "add_bot", {});
    expect(errorOf(r.send("s0", "add_bot", {}), "s0")).toBe("seat");
    start(r);
    let clientSeq = 0;
    for (let i = 0; i < 2_000 && r.state.round?.result === null; i += 1) {
      if (rung(r).turnSeat === 0) {
        clientSeq += 1;
        act(r, clientSeq);
      }
      r.tick(200);
    }
    expect(r.state.round?.result).not.toBeNull();
    // Every bot seat was played by the bot policy, never the person's seat.
    expect(new Set(r.botAsked)).toEqual(new Set([1, 2, 3]));
    const plays = r.sent
      .filter((o) => o.envelope.type === "rung_card_played")
      .map((o) => o.envelope.payload as { seat: number; auto: boolean });
    expect(plays.filter((p) => p.seat === 0).every((p) => !p.auto)).toBe(true);
    expect(plays.filter((p) => p.seat !== 0).every((p) => p.auto)).toBe(true);
  });

  it("a friend who joins a full room takes a bot's seat", () => {
    const r = room("rung", 2);
    r.send("s0", "add_bot", {});
    r.send("s0", "add_bot", {});
    r.join(presence(2));
    expect(r.state.seats.map((s) => s.playerId)).toEqual(["u0", "u1", "bot_2", "u2"]);
    expect(r.state.presences["s2"]?.seat).toBe(3);
  });

  it("bots are for private Rung rooms: Thulla refuses, and a switch to Thulla sends them home", () => {
    const thulla = room("thulla", 2);
    expect(errorOf(thulla.send("s0", "add_bot", {}), "s0")).toBe("bots");
    const r = room("rung", 2);
    r.send("s0", "add_bot", {});
    r.send("s0", "change_game", { gameId: "thulla" });
    expect(r.state.seats.filter((s) => s.bot)).toHaveLength(0);
    expect(r.state.seats.filter((s) => s.playerId !== null)).toHaveLength(2);
  });

  it("a bot is never made host when the host leaves", () => {
    const r = room("rung", 2);
    r.send("s0", "add_bot", {});
    r.send("s0", "leave_table", {});
    expect(r.state.hostSeat).toBe(1);
  });
});

describe("Rung: a bot covers an absent seat (RG43)", () => {
  it("RG43 after two timeouts in a row the bot plays the seat, and the player takes it back by acting", () => {
    const r = room("rung", 4);
    start(r);
    // The bot's memory of the table is kept from the deal on, in case it must take over.
    expect(r.state.round?.botMemory).not.toBeNull();
    const absent = (rung(r).callerSeat + 1) % 4;
    let clientSeq = 0;
    const timeouts = (): number => r.state.seats[absent]?.timeouts ?? 0;
    for (let i = 0; i < 4_000 && r.state.seats[absent]?.autoPlay !== true; i += 1) {
      if (r.state.round?.result !== null) throw new Error("the deal ended first");
      if (rung(r).turnSeat === absent) {
        r.tick(1_000);
      } else {
        clientSeq += 1;
        act(r, clientSeq);
        r.tick(1_500);
      }
    }
    // Two missed turns: RG42 played the first, and on the second the bot took the seat over.
    expect(timeouts()).toBe(2);
    expect(r.botAsked).toEqual([absent]);
    expect(
      r.sent.some(
        (o) =>
          o.envelope.type === "presence" &&
          (o.envelope.payload as { status: string; seat: number }).status === "auto_play" &&
          (o.envelope.payload as { seat: number }).seat === absent,
      ),
    ).toBe(true);
    // From now on the bot plays that seat, a few seconds after its turn starts.
    for (let i = 0; i < 400 && r.botAsked.length === 1; i += 1) {
      if (rung(r).turnSeat === absent) r.tick(500);
      else {
        clientSeq += 1;
        act(r, clientSeq);
        r.tick(1_500);
      }
    }
    expect(r.botAsked).toEqual([absent, absent]);
    expect(r.state.round?.result).toBeNull();
    // The player comes back by playing a card themselves.
    for (let i = 0; i < 40 && rung(r).turnSeat !== absent; i += 1) {
      clientSeq += 1;
      act(r, clientSeq);
      r.tick(1_500);
    }
    const asked = r.botAsked.length;
    clientSeq += 1;
    act(r, clientSeq);
    expect(r.state.seats[absent]?.autoPlay).toBe(false);
    expect(r.botAsked).toHaveLength(asked);
  });

  it("R45 Thulla still waits for three timeouts and plays the timeout policy", () => {
    expect(hostFor("thulla")?.coverAbsent).toBeUndefined();
    expect(rungHost.coverAbsent).toEqual({ afterTimeouts: 2 });
  });
});

describe("admin_bot_bench", () => {
  it("plays whole deals with bots in every seat and reports the time per decision", () => {
    let now = 0;
    const result = botBench("rung", 2, () => {
      now += 7;
      return now;
    });
    expect(result).toMatchObject({ ok: true, gameId: "rung", deals: 2, avgMs: 7, maxMs: 7 });
    expect(result.decisions).toBeGreaterThan(60);
    expect(result.slow).toEqual([]);
    expect(botBench("thulla", 1, () => 0)).toMatchObject({ ok: true, deals: 1 });
    expect(botBench("ludo", 1, () => 0)).toMatchObject({ ok: false });
  });
});

describe("Rung Quick Play: a matched table (Rung design 4)", () => {
  /** A public Rung table the matchmaker opened for these players; `party` queued together. */
  function matched(expected: number, party: readonly string[] = []): Room {
    const r = room("rung", 0);
    const core = tableCore(rungHost as unknown as AnyGameHost, {
      ids: { uuid: () => "00000000-0000-4000-8000-000000000001" },
      persist: { roundRecord: () => undefined },
    });
    const state = core.create(
      {
        code: "QUICKP",
        gameId: "rung",
        mode: "public",
        rules: { gameId: "rung", preset: "rung" },
        autoStart: { expected },
        parties: party.length > 1 ? [{ id: "p1", members: party }] : [],
      },
      T0,
    );
    const q: Room = {
      ...r,
      state,
      now: T0,
      join(p) {
        const decision = core.joinAttempt(state, p, {}, q.now);
        if (!decision.accept) throw new Error(`join rejected: ${decision.code}`);
        return core.join(state, [p], q.now);
      },
      tick(advanceMs = 0) {
        q.now += advanceMs;
        return core.tick(state, q.now);
      },
    };
    return q;
  }

  it("RG1 two who queued together sit opposite as partners, whatever order they arrive in", () => {
    for (const order of [
      [0, 1, 2, 3],
      [2, 0, 3, 1],
      [0, 2, 3, 1],
      [3, 2, 1, 0],
    ]) {
      const q = matched(4, ["u0", "u1"]);
      for (const i of order) q.join(presence(i));
      const seatOf = (id: string): number => q.state.seats.findIndex((s) => s.playerId === id);
      expect(Math.abs(seatOf("u0") - seatOf("u1")), order.join("")).toBe(2);
      expect(q.state.seats.every((s) => s.playerId !== null)).toBe(true);
    }
  });

  it("the table deals itself once all four are seated, to five points", () => {
    const q = matched(4);
    for (let i = 0; i < 4; i += 1) q.join(presence(i));
    for (let i = 0; i < 40 && q.state.round === null; i += 1) q.tick(1_000);
    expect(q.state.round).not.toBeNull();
    expect((q.state.round?.state as RungState).rules.target).toBe(5);
  });

  it("bots take the seats of matched players who never arrive", () => {
    const q = matched(4);
    q.join(presence(0));
    q.join(presence(1));
    for (let i = 0; i < 60 && q.state.round === null; i += 1) q.tick(1_000);
    expect(q.state.seats.filter((s) => s.bot)).toHaveLength(2);
    expect(q.state.round).not.toBeNull();
  });

  it("nobody arriving closes the table instead of dealing to bots", () => {
    const q = matched(4);
    for (let i = 0; i < 60; i += 1) q.tick(1_000);
    expect(q.state.round).toBeNull();
    expect(q.state.seats.some((s) => s.bot)).toBe(false);
  });
});

describe("Rung lobby: the host chooses the pairs (swap_seats)", () => {
  it("two seats trade places, presences and the host seat follow, and only the host may", () => {
    const r = room("rung", 3);
    expect(errorOf(r.send("s1", "swap_seats", { a: 0, b: 1 }), "s1")).toBe("table.not_host");
    // The host moves from seat 0 to the empty seat 3, then puts u2 across from u1.
    r.send("s0", "swap_seats", { a: 0, b: 3 });
    expect(r.state.seats.map((s) => s.playerId)).toEqual([null, "u1", "u2", "u0"]);
    expect(r.state.hostSeat).toBe(3);
    expect(r.state.presences["s0"]?.seat).toBe(3);
    r.send("s0", "swap_seats", { a: 2, b: 3 });
    expect(r.state.seats.map((s) => s.playerId)).toEqual([null, "u1", "u0", "u2"]);
    expect(r.state.presences["s2"]?.seat).toBe(3);
    expect(r.state.hostSeat).toBe(2);
    // Nothing to swap, a seat that does not exist, and a swap mid-deal are refused.
    expect(errorOf(r.send("s0", "swap_seats", { a: 2, b: 2 }), "s0")).toBe("seat");
    expect(errorOf(r.send("s0", "swap_seats", { a: 2, b: 7 }), "s0")).toBe("seat");
    r.send("s0", "add_bot", {});
    expect(r.state.seats[0]?.playerId).toBe("bot_0");
    r.send("s0", "swap_seats", { a: 0, b: 1 });
    expect(r.state.seats.map((s) => s.playerId)).toEqual(["u1", "bot_1", "u0", "u2"]);
    // After the swaps a session no longer sits at the seat of its own number.
    for (const session of ["s0", "s1", "s2"]) r.send(session, "ready", { ready: true });
    r.send("s0", "start_round", {});
    expect(r.state.round).not.toBeNull();
    expect(errorOf(r.send("s0", "swap_seats", { a: 0, b: 1 }), "s0")).toBe("in_round");
  });
});
