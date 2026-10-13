import { OPCODES } from "@hulla/protocol/generated";
import { describe, expect, it } from "vitest";

import { METRICS, metrics } from "../infra/metrics";
import { tableMatchHandler } from "../match/nakama_match";

import { fakeDispatcher, fakeMatchNk, matchMessage, nkPresence } from "./fake-match";
import { fakeLogger } from "./fake-nk";
import { toyHost } from "./toy-game";

describe("game metrics on the Prometheus endpoint (GDD 20.13)", () => {
  it("records timers in nanoseconds from milliseconds", () => {
    const seen: number[] = [];
    metrics.time(
      { metricsTimerRecord: (_n, _t, value) => void seen.push(value) },
      METRICS.roundDuration,
      {},
      22_000,
    );
    expect(seen).toEqual([22_000 * 1_000_000]);
  });

  it("never throws when the sink is missing or failing", () => {
    expect(() => {
      metrics.count({}, METRICS.moves, { game: "thulla" });
    }).not.toThrow();
    expect(() => {
      metrics.time(
        {
          metricsTimerRecord: () => {
            throw new Error("down");
          },
        },
        METRICS.roundDuration,
        {},
        5,
      );
    }).not.toThrow();
    expect(() => {
      metrics.gauge({}, "x", {}, 1);
    }).not.toThrow();
  });

  it("counts rounds and moves from the binding", () => {
    const counted: { name: string; tags: Record<string, string>; delta: number }[] = [];
    const { nk } = fakeMatchNk();
    const sink = nk as unknown as Record<string, unknown>;
    sink["metricsCounterAdd"] = (name: string, tags: Record<string, string>, delta: number) => {
      counted.push({ name, tags, delta });
    };
    sink["metricsTimerRecord"] = () => undefined;
    const handler = tableMatchHandler(toyHost(), { config: { autoPlayDelayMs: 0 } });
    const logger = fakeLogger();
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
    const envelope = (type: string, seq: number, payload: object) => ({
      v: 1,
      type,
      tableId: state.tableId,
      seq,
      ts: 0,
      payload,
    });
    const ready = presences.map((p) =>
      matchMessage(p, OPCODES.ready.opcode, envelope("ready", 1, { ready: true })),
    );
    state = handler.matchLoop(ctx, logger, nk, d.dispatcher, 2, state, ready)?.state ?? state;
    const start = matchMessage(
      presences[0] ?? nkPresence(0),
      OPCODES.start_round.opcode,
      envelope("start_round", 2, {}),
    );
    state = handler.matchLoop(ctx, logger, nk, d.dispatcher, 3, state, [start])?.state ?? state;
    expect(counted.some((c) => c.name === METRICS.rounds && c.tags["mode"] === "private")).toBe(
      true,
    );
    const roundId = state.round?.roundId ?? "";
    for (let i = 0; i < 3; i += 1) {
      const p = presences[i] ?? nkPresence(i);
      const play = matchMessage(
        p,
        OPCODES.play_card.opcode,
        envelope("play_card", 10 + i, { roundId, clientSeq: 1, card: 1 }),
      );
      state =
        handler.matchLoop(ctx, logger, nk, d.dispatcher, 4 + i, state, [play])?.state ?? state;
    }
    expect(counted.filter((c) => c.name === METRICS.moves).length).toBeGreaterThanOrEqual(3);
    expect(counted.every((c) => c.tags["game"] === "thulla")).toBe(true);
  });
});
