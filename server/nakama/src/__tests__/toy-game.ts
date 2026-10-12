import { ErrorCodes, ruleError, stateHash } from "@hulla/shared-ts";

import type { GameHost } from "../match/types";
import type { RulesSelection } from "@hulla/protocol/generated";
import type { GameModule, RoundResult, SeriesState } from "@hulla/rules-engine";
import type { Result, RuleError, SeatIndex } from "@hulla/shared-ts";

/**
 * A tiny game for table-core tests, so the core is proven without Thulla. Every seat first plays
 * one opening number at the same time (a simultaneous window), then seats take turns adding 1 or
 * 2 to a shared total; the round ends when the total reaches the target. Each seat has a secret
 * only it may see, which proves redaction dispatch.
 */
export interface ToyState {
  readonly seatCount: number;
  readonly target: number;
  readonly total: number;
  readonly phase: "opening" | "play" | "ended";
  readonly openingPending: readonly SeatIndex[];
  readonly turnSeat: SeatIndex | null;
  readonly counts: readonly number[];
  readonly secrets: readonly number[];
}

export interface ToyMove {
  readonly type: "add";
  readonly n: number;
}

export interface ToyView {
  readonly viewer: SeatIndex | null;
  readonly total: number;
  readonly phase: string;
  readonly turnSeat: SeatIndex | null;
  readonly secret: number | null;
}

export type ToyEvent =
  | { readonly type: "started"; readonly target: number }
  | { readonly type: "added"; readonly seat: SeatIndex; readonly n: number; readonly total: number }
  | { readonly type: "peek"; readonly seat: SeatIndex; readonly secret: number }
  | { readonly type: "ended"; readonly winner: SeatIndex };

export interface ToyRules {
  readonly target: number;
}

const next = (state: ToyState, seat: SeatIndex): SeatIndex => (seat + 1) % state.seatCount;

export const toyModule: GameModule<ToyState, ToyMove, ToyView, ToyEvent, ToyRules> = {
  id: "thulla",
  version: "toy-1",
  seats: { min: 3, max: 4 },
  direction: "cw",
  rulesSchema: {},
  phases: [
    { id: "opening", deadline: { kind: "simultaneousWindow" } },
    { id: "play", deadline: { kind: "perSeatTimer" } },
    { id: "ended", deadline: { kind: "none" } },
  ],
  createRound(input, rng) {
    if (input.seatCount < 3) {
      return { ok: false, error: ruleError(ErrorCodes.rules.invalidSeatCount) };
    }
    const seats = Array.from({ length: input.seatCount }, (_, i) => i);
    return {
      ok: true,
      value: {
        seatCount: input.seatCount,
        target: input.rules.target,
        total: 0,
        phase: "opening",
        openingPending: seats,
        turnSeat: null,
        counts: seats.map(() => 0),
        secrets: seats.map(() => rng.nextInt(1000)),
      },
    };
  },
  currentPhase: (state) => state.phase,
  legalMoves(state, seat) {
    if (state.phase === "ended") return [];
    if (state.phase === "opening") {
      return state.openingPending.includes(seat) ? [{ type: "add", n: 1 }] : [];
    }
    return state.turnSeat === seat
      ? [
          { type: "add", n: 1 },
          { type: "add", n: 2 },
        ]
      : [];
  },
  applyMove(state, seat, move) {
    if (seat < 0 || seat >= state.seatCount) {
      return { ok: false, error: ruleError(ErrorCodes.rules.invalidSeat) };
    }
    if (state.phase === "ended") return { ok: false, error: ruleError(ErrorCodes.rules.roundOver) };
    const legal = toyModule.legalMoves(state, seat).some((m) => m.n === move.n);
    if (!legal) {
      return { ok: false, error: ruleError(ErrorCodes.rules.illegalMove, { n: move.n }) };
    }
    const total = state.total + move.n;
    const counts = state.counts.map((c, i) => (i === seat ? c + move.n : c));
    const events: ToyEvent[] = [{ type: "added", seat, n: move.n, total }];
    if (state.phase === "opening") {
      const pending = state.openingPending.filter((s) => s !== seat);
      const done = pending.length === 0;
      return {
        ok: true,
        state: {
          ...state,
          total,
          counts,
          openingPending: pending,
          phase: done ? "play" : "opening",
          turnSeat: done ? 0 : null,
        },
        events,
      };
    }
    if (total >= state.target) {
      events.push({ type: "ended", winner: seat });
      return {
        ok: true,
        state: { ...state, total, counts, phase: "ended", turnSeat: null },
        events,
      };
    }
    const turn = next(state, seat);
    events.push({ type: "peek", seat: turn, secret: state.secrets[turn] ?? 0 });
    return { ok: true, state: { ...state, total, counts, turnSeat: turn }, events };
  },
  onDeadline(state, seat) {
    return toyModule.legalMoves(state, seat)[0] ?? null;
  },
  redact(state, viewer) {
    const seat = viewer.kind === "seat" ? viewer.seat : null;
    return {
      viewer: seat,
      total: state.total,
      phase: state.phase,
      turnSeat: state.turnSeat,
      secret: seat === null ? null : (state.secrets[seat] ?? null),
    };
  },
  isRoundOver: (state) => state.phase === "ended",
  results(state): RoundResult {
    const order = state.counts
      .map((count, seat) => ({ count, seat }))
      .sort((a, b) => b.count - a.count || a.seat - b.seat)
      .map((x) => x.seat);
    return {
      gameId: "thulla",
      finishOrder: order,
      loserSeat: order[order.length - 1] ?? null,
      points: state.counts.map((c) => c),
    };
  },
  seriesUpdate(series: SeriesState, result: RoundResult): SeriesState {
    return {
      roundsPlayed: series.roundsPlayed + 1,
      points: series.points.map((p, i) => p + (result.points[i] ?? 0)),
      loserTally: series.loserTally.map((t, i) => (i === result.loserSeat ? t + 1 : t)),
      nextDealerSeat: (series.nextDealerSeat + 1) % series.points.length,
      finished: false,
    };
  },
};

export function toyHost(
  options: { readonly presentationDelayMs?: number } = {},
): GameHost<ToyState, ToyMove, ToyView, ToyEvent, ToyRules> {
  return {
    module: toyModule,
    resolveRules(selection: RulesSelection): Result<ToyRules, RuleError> {
      const target = (selection.overrides as Record<string, unknown> | undefined)?.["target"];
      if (target !== undefined && (typeof target !== "number" || target < 1)) {
        return { ok: false, error: ruleError(ErrorCodes.rules.invalidRules, { key: "target" }) };
      }
      return { ok: true, value: { target: typeof target === "number" ? target : 10 } };
    },
    isMoveType: (type) => type === "play_card",
    decodeMove(_type, payload): Result<ToyMove, RuleError> {
      const n = payload["card"];
      if (typeof n !== "number") {
        return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "card" }) };
      }
      return { ok: true, value: { type: "add", n } };
    },
    roundStartEvents: (state) => [{ type: "started", target: state.target }],
    encodeEvents(ctx, events) {
      return events.map((event) => {
        switch (event.type) {
          case "started":
            return {
              type: "round_start",
              payload: { roundId: ctx.roundId, target: event.target },
              seat: null,
            };
          case "added":
            return {
              type: "move_applied",
              payload: {
                roundId: ctx.roundId,
                seat: event.seat,
                move: { n: event.n },
                total: event.total,
                ...(ctx.move?.clientSeq === undefined || ctx.move.clientSeq === null
                  ? {}
                  : { clientSeq: ctx.move.clientSeq }),
                auto: ctx.move?.auto === true,
              },
              seat: null,
            };
          case "peek":
            return {
              type: "private_draw",
              payload: { roundId: ctx.roundId, secret: event.secret },
              seat: event.seat,
            };
          case "ended":
            return {
              type: "round_end",
              payload: {
                roundId: ctx.roundId,
                winner: event.winner,
                seedHex: ctx.seedHex,
                series: ctx.series,
              },
              seat: null,
            };
        }
      });
    },
    turnStart: (ctx, seat, deadlineTs, simultaneous) => ({
      roundId: ctx.roundId,
      seat,
      legal: toyModule.legalMoves(ctx.state, seat).map((m) => m.n),
      deadlineTs,
      simultaneous,
    }),
    presentationDelayMs: () => options.presentationDelayMs ?? 0,
    stateHash: (state) => stateHash(state),
  };
}
