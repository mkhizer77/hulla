import { OPCODES } from "@hulla/protocol/generated";
import { describe, expect, it } from "vitest";

import {
  labelFor,
  ROUND_LOG_COLLECTION,
  tableMatchHandler,
  turnSecondsFrom,
} from "../match/nakama_match";

import { fakeDispatcher, fakeMatchNk, matchMessage, nkPresence } from "./fake-match";
import { fakeLogger } from "./fake-nk";
import { toyHost } from "./toy-game";

const ctx = {} as nkruntime.Context;

function booted() {
  const handler = tableMatchHandler(toyHost(), { config: { autoPlayDelayMs: 0 } });
  const { nk, writes } = fakeMatchNk();
  const logger = fakeLogger();
  const d = fakeDispatcher();
  const init = handler.matchInit(ctx, logger, nk, {
    code: "ABCDEF",
    gameId: "thulla",
    mode: "public",
  });
  let state = init.state;
  // Nakama exports the state to Go after every callback and rebuilds it as plain data, so
  // the state must survive a JSON round trip: no Maps, no class instances, no functions.
  const rebuilt = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;
  const presences = [0, 1, 2].map(nkPresence);
  for (const p of presences) {
    const attempt = handler.matchJoinAttempt(ctx, logger, nk, d.dispatcher, 1, state, p, {});
    expect(attempt?.accept).toBe(true);
    state = rebuilt(attempt?.state ?? state);
  }
  state = rebuilt(
    handler.matchJoin(ctx, logger, nk, d.dispatcher, 1, state, presences)?.state ?? state,
  );
  const envelope = (type: string, payload: object) => ({
    v: 1,
    type,
    tableId: state.tableId,
    seq: 1,
    ts: 0,
    payload,
  });
  const loop = (messages: nkruntime.MatchMessage[]) => {
    const result = handler.matchLoop(ctx, logger, nk, d.dispatcher, 2, state, messages);
    if (result !== null) state = rebuilt(result.state);
    return result;
  };
  return {
    handler,
    nk,
    writes,
    logger,
    d,
    init,
    presences,
    envelope,
    loop,
    get state() {
      return state;
    },
  };
}

describe("the Nakama binding", () => {
  it("creates the table with the tick rate and a queryable label", () => {
    const b = booted();
    expect(b.init.tickRate).toBe(5);
    expect(JSON.parse(b.init.label)).toEqual({
      code: "ABCDEF",
      gameId: "thulla",
      mode: "public",
      preset: b.state.rules.preset ?? null,
      status: "open",
      seated: 0,
      open: 4,
      watching: 0,
      watchable: true,
    });
    expect(JSON.parse(labelFor(b.state))).toMatchObject({ seated: 3, open: 1 });
    expect(b.d.labels.at(-1)).toBe(labelFor(b.state));
  });

  it("sends table_state to the joiner only and presence to everyone, using the registered opcodes", () => {
    const b = booted();
    const first = b.d.broadcasts.slice(0, 2);
    expect(first[0]).toMatchObject({ opcode: OPCODES.table_state.opcode, to: ["s0"] });
    expect(first[1]).toMatchObject({ opcode: OPCODES.presence.opcode, to: null });
  });

  it("decodes string and binary message data, routes by opcode and answers the sender only", () => {
    const b = booted();
    b.d.broadcasts.length = 0;
    const [p0, p1] = b.presences;
    if (p0 === undefined || p1 === undefined) throw new Error("presences");
    b.loop([
      matchMessage(p0, OPCODES.ping.opcode, b.envelope("ping", { sentTs: 5 })),
      matchMessage(p1, OPCODES.ping.opcode, b.envelope("ping", { sentTs: 6 }), true),
      matchMessage(p1, OPCODES.ready.opcode, b.envelope("ping", { sentTs: 7 })),
    ]);
    expect(b.d.broadcasts.map((x) => [x.opcode, x.to])).toEqual([
      [OPCODES.pong.opcode, ["s0"]],
      [OPCODES.pong.opcode, ["s1"]],
      [OPCODES.error.opcode, ["s1"]],
    ]);
    expect(b.d.broadcasts[2]?.envelope.payload).toMatchObject({ detail: { reason: "opcode" } });
  });

  it("runs the lobby and a round through the loop, kicks on leave_table and writes the round record", () => {
    const b = booted();
    const [p0, p1, p2] = b.presences;
    if (p0 === undefined || p1 === undefined || p2 === undefined) throw new Error("presences");
    b.loop(
      [p0, p1, p2].map((p) =>
        matchMessage(p, OPCODES.ready.opcode, b.envelope("ready", { ready: true })),
      ),
    );
    b.loop([matchMessage(p0, OPCODES.start_round.opcode, b.envelope("start_round", {}))]);
    expect(b.state.phase).toBe("inRound");
    expect(b.d.labels.at(-1)).toContain('"status":"inRound"');
    b.loop([matchMessage(p2, OPCODES.leave_table.opcode, b.envelope("leave_table", {}))]);
    expect(b.d.kicks).toEqual([["s2"]]);
    expect(
      b.handler.matchLeave(ctx, b.logger, b.nk, b.d.dispatcher, 3, b.state, [p2])?.state.seats[2]
        ?.left,
    ).toBe(true);
    const roundId = b.state.round?.roundId ?? "";
    let seq = 1;
    for (let guard = 0; guard < 40 && b.state.phase === "inRound"; guard += 1) {
      const opening = b.state.round?.state.phase === "opening";
      const messages = [p0, p1]
        .filter((_p, i) => opening || b.state.round?.state.turnSeat === i)
        .map((p) =>
          matchMessage(
            p,
            OPCODES.play_card.opcode,
            b.envelope("play_card", { roundId, clientSeq: seq, card: opening ? 1 : 2 }),
          ),
        );
      seq += 1;
      b.loop(messages);
    }
    expect(b.state.phase).toBe("roundEnded");
    expect(b.writes[0]).toMatchObject({
      collection: ROUND_LOG_COLLECTION,
      key: `${b.state.tableId}:${roundId}`,
      permissionRead: 0,
    });
  });

  it("ends the match when the table closes and answers inspect signals", () => {
    const b = booted();
    const inspect = b.handler.matchSignal(
      ctx,
      b.logger,
      b.nk,
      b.d.dispatcher,
      4,
      b.state,
      JSON.stringify({ kind: "inspect" }),
    );
    expect(JSON.parse(inspect?.data ?? "{}")).toMatchObject({
      ok: true,
      phase: "lobby",
      seated: 3,
    });
    const garbage = b.handler.matchSignal(ctx, b.logger, b.nk, b.d.dispatcher, 4, b.state, "{");
    expect(JSON.parse(garbage?.data ?? "{}")).toEqual({ ok: false });
    b.handler.matchSignal(
      ctx,
      b.logger,
      b.nk,
      b.d.dispatcher,
      4,
      b.state,
      JSON.stringify({ kind: "close" }),
    );
    expect(b.state.phase).toBe("closing");
    expect(b.d.broadcasts.at(-1)).toMatchObject({ opcode: OPCODES.table_closed.opcode, to: null });
    expect(b.loop([])).toBeNull();
  });

  it("drops a message that makes the core throw and keeps the table alive", () => {
    const b = booted();
    const [p0] = b.presences;
    if (p0 === undefined) throw new Error("presences");
    const result = b.loop([
      { ...matchMessage(p0, OPCODES.ping.opcode, {}), data: 42 as unknown as ArrayBuffer },
    ]);
    expect(result).not.toBeNull();
    const terminated = b.handler.matchTerminate(ctx, b.logger, b.nk, b.d.dispatcher, 5, b.state, 0);
    expect(terminated?.state.closeReason).toBe("server");
  });
});

describe("turnSecondsFrom", () => {
  it("replaces the default turn timer for every mode when set (R43)", () => {
    expect(turnSecondsFrom({ HULLA_TURN_SECONDS: "40" })).toEqual({
      defaultTurnSeconds: { private: 40, public: 40, ranked: 40 },
    });
  });

  it("keeps the defaults when empty, missing or out of range", () => {
    for (const value of ["", "abc", "4", "121", "12.5"]) {
      expect(turnSecondsFrom({ HULLA_TURN_SECONDS: value })).toEqual({});
    }
    expect(turnSecondsFrom({})).toEqual({});
  });
});
