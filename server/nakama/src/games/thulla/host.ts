import {
  canTakeHand,
  legalMoves,
  resolveRules,
  results,
  roundStartedEvent,
  thullaModule,
} from "@hulla/rules-engine";
import { ErrorCodes, err, ok, ruleError, stateHash } from "@hulla/shared-ts";

import { seriesPayload } from "../../match/view";

import type { EncodeContext, GameHost, GameMessage } from "../../match/types";
import type { Check } from "@hulla/protocol/generated";
import type {
  Card,
  ThullaEvent,
  ThullaMove,
  ThullaRules,
  ThullaState,
  ThullaView,
} from "@hulla/rules-engine";
import type { Result, RuleError, SeatIndex } from "@hulla/shared-ts";

/** The thulla moment never delays the next turn by more than this (GDD 14.6). */
export const THULLA_MOMENT_MS = 1_200;

export type ThullaHost = GameHost<ThullaState, ThullaMove, ThullaView, ThullaEvent, ThullaRules>;

function isThulla(event: ThullaEvent): boolean {
  return (
    (event.type === "card_played" && event.thulla) ||
    (event.type === "trick_resolved" && event.thullaBy !== null)
  );
}

function publicMessage(type: string, payload: object): GameMessage {
  return { type, payload, seat: null };
}

/** `clientSeq` and `auto` for the one play the submitted move made; engine play-outs are auto. */
function moveMeta(ctx: EncodeContext<ThullaState>, seat: SeatIndex, first: boolean): object {
  const move = ctx.move;
  if (move === null || !first || move.seat !== seat) return { auto: true };
  return move.clientSeq === null ? { auto: true } : { clientSeq: move.clientSeq, auto: false };
}

/** Engine events to wire messages (ARCHITECTURE 8.4 step 7; opcodes in protocol/opcodes.json). */
export function encodeThullaEvents(
  ctx: EncodeContext<ThullaState>,
  events: readonly ThullaEvent[],
): GameMessage[] {
  const out: GameMessage[] = [];
  const roundId = ctx.roundId;
  let ownPlayPending = true;
  events.forEach((event, index) => {
    switch (event.type) {
      case "round_started":
        out.push(
          publicMessage("round_start", {
            roundId,
            commitment: event.commitment,
            seatCount: event.seatCount,
            dealerSeat: event.dealerSeat,
            leaderSeat: event.leaderSeat,
            handCounts: [...event.handCounts],
            rulesVersion: ctx.state.rulesVersion,
          }),
        );
        return;
      case "card_played": {
        const meta = moveMeta(ctx, event.seat, ownPlayPending);
        ownPlayPending = false;
        out.push(
          publicMessage("move_applied", {
            roundId,
            seat: event.seat,
            move: { type: "play_card", card: event.card },
            trickIndex: event.trickIndex,
            thulla: event.thulla,
            ...meta,
          }),
        );
        return;
      }
      case "trick_resolved": {
        const escapes: object[] = [];
        for (let i = index + 1; i < events.length; i += 1) {
          const next = events[i];
          if (next?.type !== "escape") break;
          escapes.push({ roundId, seat: next.seat, rank: next.rank });
        }
        out.push(
          publicMessage("trick_resolved", {
            roundId,
            trickIndex: event.trickIndex,
            ledSuit: event.ledSuit,
            cards: [...event.cards],
            thullaBy: event.thullaBy,
            picker: event.picker,
            nextLeader: event.nextLeader,
            handCounts: [...event.handCounts],
            escapes,
          }),
        );
        if (event.picker !== null) {
          out.push(
            publicMessage("pickup", {
              roundId,
              seat: event.picker,
              cards: [...event.cards],
              trickIndex: event.trickIndex,
            }),
          );
        }
        return;
      }
      case "escape":
        out.push(publicMessage("escape", { roundId, seat: event.seat, rank: event.rank }));
        return;
      case "private_draw":
        for (const seat of event.visibleTo) {
          out.push({
            type: "private_draw",
            payload: {
              roundId,
              seat: event.seat,
              card: event.card,
              source: event.source,
              from: event.from,
              autoLead: true,
            },
            seat,
          });
        }
        return;
      case "hand_taken": {
        const meta = moveMeta(ctx, event.taker, ownPlayPending);
        ownPlayPending = false;
        out.push(
          publicMessage("move_applied", {
            roundId,
            seat: event.taker,
            move: { type: "take_hand" },
            trickIndex: ctx.state.trickIndex,
            ...meta,
          }),
        );
        out.push(
          publicMessage("hand_taken", {
            roundId,
            taker: event.taker,
            target: event.target,
            cardCount: event.cardCount,
          }),
        );
        return;
      }
      case "endgame_started":
        out.push(publicMessage("endgame_started", { roundId, seats: [...event.seats] }));
        return;
      case "confirmed":
        out.push(
          publicMessage("confirmed", {
            roundId,
            bhabhiSeat: event.bhabhiSeat,
            cardsRemaining: event.cardsRemaining,
            fastForward: event.fastForward,
          }),
        );
        return;
      case "round_ended": {
        const result = results(ctx.state);
        if (result === null) return;
        out.push(
          publicMessage("round_end", {
            roundId,
            escapeOrder: [...result.escapeOrder],
            bhabhiSeat: result.bhabhiSeat,
            bhabhiCards: result.bhabhiCards,
            points: [...result.points],
            stats: result.stats.map((s) => ({ ...s })),
            fairness: {
              commitment: ctx.state.commitment,
              seed: ctx.seedHex,
              deckOrder: [...ctx.state.deckOrder],
            },
            ...(ctx.series === null ? {} : { series: seriesPayload(ctx.series) }),
          }),
        );
        return;
      }
    }
  });
  return out;
}

/** Validates the payload with the generated check for its type, then maps it to an engine move. */
export function decodeThullaMove(
  type: string,
  payload: Record<string, unknown>,
  check: Check | undefined,
): Result<ThullaMove, RuleError> {
  if (type !== "play_card" && type !== "take_hand") {
    return err(ruleError(ErrorCodes.table.invalidPayload, { reason: "unsupported_type", type }));
  }
  if (check?.(payload) !== true) {
    return err(ruleError(ErrorCodes.table.invalidPayload, { reason: "schema", type }));
  }
  if (type === "play_card") {
    return ok({ type: "play_card", card: payload["card"] as Card });
  }
  return ok({ type: "take_hand" });
}

/** Thulla at the table core: moves in, events out, the legal mask per turn, the seed reveal. */
export const thullaHost: ThullaHost = {
  module: thullaModule,
  resolveRules(selection, mode) {
    return resolveRules(mode, selection.preset ?? "standard", selection.overrides ?? {});
  },
  isMoveType: (type) => type === "play_card" || type === "take_hand",
  decodeMove: decodeThullaMove,
  roundStartEvents: (state) => [roundStartedEvent(state)],
  encodeEvents: encodeThullaEvents,
  turnStart(ctx, seat, deadlineTs, simultaneous) {
    const legal = legalMoves(ctx.state, seat).flatMap((m) =>
      m.type === "play_card" ? [m.card] : [],
    );
    const base = {
      roundId: ctx.roundId,
      seat,
      legal,
      canTakeHand: canTakeHand(ctx.state, seat),
      deadlineTs,
    };
    return simultaneous ? { ...base, simultaneous: true } : base;
  },
  presentationDelayMs: (events) => (events.some(isThulla) ? THULLA_MOMENT_MS : 0),
  stateHash: (state) => stateHash(state),
};
