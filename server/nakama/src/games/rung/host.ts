import {
  RUNG_PRESETS,
  canRedeal,
  legalRungMoves,
  resolveRungRules,
  rungModule,
  rungResults,
  rungRoundStartedEvent,
} from "@hulla/rules-engine";
import { ErrorCodes, err, ok, ruleError, stateHash } from "@hulla/shared-ts";

import { withPresetSettings } from "../../content/presets";
import { seriesPayload } from "../../match/view";

import type { EncodeContext, GameHost, GameMessage, LoggedMessage } from "../../match/types";
import type { Check } from "@hulla/protocol/generated";
import type {
  Card,
  RungEvent,
  RungMove,
  RungPreset,
  RungRules,
  RungState,
  RungView,
  Suit,
} from "@hulla/rules-engine";
import type { Result, RuleError, SeatIndex } from "@hulla/shared-ts";

/** How long clients show a finished trick, a taken pile and the call before the next turn starts. */
export const RUNG_TRICK_MS = 900;
export const RUNG_PILE_MS = 1_200;
export const RUNG_CALL_MS = 1_200;

export const RUNG_MOVE_TYPES: readonly string[] = [
  "play_card",
  "call_trump",
  "redeal",
  "court_choice",
];

export type RungHost = GameHost<RungState, RungMove, RungView, RungEvent, RungRules>;

function publicMessage(type: string, payload: object): GameMessage {
  return { type, payload, seat: null };
}

/** `clientSeq` and `auto` for the event the submitted move made; a timeout or a bot is auto. */
function moveMeta(ctx: EncodeContext<RungState>, seat: SeatIndex): object {
  const move = ctx.move;
  if (move?.seat !== seat || move.clientSeq === null) return { auto: true };
  return { clientSeq: move.clientSeq, auto: false };
}

/** Each seat's own cards, to that seat only: after a redeal and when the deal is completed (RG5, RG8). */
function handMessages(ctx: EncodeContext<RungState>): GameMessage[] {
  return ctx.state.hands.map((cards, seat) => ({
    type: "rung_hand",
    payload: { roundId: ctx.roundId, cards: [...cards] },
    seat,
  }));
}

/** Engine events to wire messages (opcodes in protocol/opcodes.json). */
export function encodeRungEvents(
  ctx: EncodeContext<RungState>,
  events: readonly RungEvent[],
): GameMessage[] {
  const out: GameMessage[] = [];
  const roundId = ctx.roundId;
  for (const event of events) {
    switch (event.type) {
      case "round_started":
        out.push(
          publicMessage("rung_round_start", {
            roundId,
            commitment: event.commitment,
            dealerSeat: event.dealerSeat,
            callerSeat: event.callerSeat,
            handCounts: [...event.handCounts],
            rulesVersion: ctx.state.rulesVersion,
          }),
        );
        break;
      case "redealt":
        out.push(
          publicMessage("rung_redealt", {
            roundId,
            seat: ctx.state.callerSeat,
            commitment: event.commitment,
            redeals: event.redeals,
            handCounts: ctx.state.hands.map((hand) => hand.length),
            ...moveMeta(ctx, ctx.state.callerSeat),
          }),
          ...handMessages(ctx),
        );
        break;
      case "trump_called":
        out.push(
          publicMessage("rung_trump_called", {
            roundId,
            seat: event.seat,
            suit: event.suit,
            ...moveMeta(ctx, event.seat),
          }),
        );
        break;
      case "hands_completed":
        out.push(
          publicMessage("rung_hands_completed", { roundId, handCounts: [...event.handCounts] }),
          ...handMessages(ctx),
        );
        break;
      case "card_played":
        out.push(
          publicMessage("rung_card_played", {
            roundId,
            seat: event.seat,
            card: event.card,
            trickIndex: event.trickIndex,
            cut: event.cut,
            ...moveMeta(ctx, event.seat),
          }),
        );
        break;
      case "trick_won":
        out.push(
          publicMessage("rung_trick_won", {
            roundId,
            trickIndex: event.trickIndex,
            winner: event.winner,
            cards: [...event.cards],
            pile: event.pile,
            counts: [...event.counts],
          }),
        );
        break;
      case "pile_taken":
        out.push(
          publicMessage("rung_pile_taken", {
            roundId,
            seat: event.seat,
            team: event.team,
            tricks: event.tricks,
            counts: [...event.counts],
          }),
        );
        break;
      case "court_choice_opened":
        out.push(
          publicMessage("rung_court_choice_opened", {
            roundId,
            seat: event.seat,
            team: event.team,
          }),
        );
        break;
      case "court_choice_made":
        out.push(
          publicMessage("rung_court_choice_made", {
            roundId,
            seat: event.seat,
            playOn: event.playOn,
            ...moveMeta(ctx, event.seat),
          }),
        );
        break;
      case "round_ended": {
        const result = rungResults(ctx.state);
        if (result === null) break;
        out.push(
          publicMessage("rung_round_end", {
            roundId,
            winnerTeam: result.winnerTeam,
            kind: result.kind,
            byCaller: result.byCaller,
            counts: [...result.counts],
            dealPoints: result.dealPoints,
            points: [...result.points],
            stats: result.stats.map((s) => ({ ...s })),
            trump: result.trump,
            fairness: {
              commitment: ctx.state.commitment,
              seed: ctx.seedHex,
              deckOrder: [...ctx.state.deckOrder],
            },
            ...(ctx.series === null ? {} : { series: seriesPayload(ctx.series) }),
          }),
        );
        break;
      }
    }
  }
  return out;
}

/** Validates the payload with the generated check for its type, then maps it to an engine move. */
export function decodeRungMove(
  type: string,
  payload: Record<string, unknown>,
  check: Check | undefined,
): Result<RungMove, RuleError> {
  if (!RUNG_MOVE_TYPES.includes(type)) {
    return err(ruleError(ErrorCodes.table.invalidPayload, { reason: "unsupported_type", type }));
  }
  if (check?.(payload) !== true) {
    return err(ruleError(ErrorCodes.table.invalidPayload, { reason: "schema", type }));
  }
  switch (type) {
    case "play_card":
      return ok({ type: "play_card", card: payload["card"] as Card });
    case "call_trump":
      return ok({ type: "call_trump", suit: payload["suit"] as Suit });
    case "redeal":
      return ok({ type: "redeal" });
    default:
      return ok({ type: "court_choice", playOn: payload["playOn"] === true });
  }
}

function presentationDelay(events: readonly RungEvent[]): number {
  let delay = 0;
  for (const event of events) {
    if (event.type === "trick_won") delay = Math.max(delay, RUNG_TRICK_MS);
    if (event.type === "pile_taken") delay = Math.max(delay, RUNG_PILE_MS);
    if (event.type === "trump_called") delay = Math.max(delay, RUNG_CALL_MS);
  }
  return delay;
}

/** Stats-card numbers per seat, from the deal's own tally in `rung_round_end`. */
export function rungHighlights(
  messages: readonly LoggedMessage[],
  seatCount: number,
): readonly Record<string, number>[] {
  const out = Array.from({ length: seatCount }, () => ({
    tricksWon: 0,
    cuts: 0,
    pilesTaken: 0,
    calls: 0,
    redeals: 0,
  }));
  for (const m of messages) {
    const p = m.payload as Record<string, unknown>;
    const seat = typeof p["seat"] === "number" ? out[p["seat"]] : undefined;
    if (m.type === "rung_trump_called" && seat !== undefined) seat.calls += 1;
    if (m.type === "rung_redealt" && seat !== undefined) seat.redeals += 1;
    if (m.type === "rung_round_end" && Array.isArray(p["stats"])) {
      (p["stats"] as readonly Record<string, unknown>[]).forEach((stats, index) => {
        const row = out[index];
        if (row === undefined) return;
        for (const key of ["tricksWon", "cuts", "pilesTaken"] as const) {
          const value = stats[key];
          if (typeof value === "number") row[key] = value;
        }
      });
    }
  }
  return out;
}

/** Rung at the table core: the call, the play and the court choice in, events and hands out. */
export const rungHost: RungHost = {
  module: rungModule,
  viewField: "rungRound",
  // RG43: after two timeouts in a row, or while a player is away, the bot plays the seat.
  coverAbsent: { afterTimeouts: 2 },
  fillWithBots: true,
  resolveRules(selection, mode) {
    const preset = selection.preset ?? "rung";
    if (!Object.keys(RUNG_PRESETS).includes(preset)) {
      return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "unknown_preset", preset }));
    }
    return resolveRungRules(mode, preset as RungPreset, selection.overrides ?? {});
  },
  normaliseRules: (selection) => withPresetSettings(selection, "rung"),
  isMoveType: (type) => RUNG_MOVE_TYPES.includes(type),
  decodeMove: decodeRungMove,
  roundStartEvents: (state) => [rungRoundStartedEvent(state)],
  encodeEvents: encodeRungEvents,
  turnStart(ctx, seat, deadlineTs) {
    const moves = legalRungMoves(ctx.state, seat);
    const base = {
      roundId: ctx.roundId,
      seat,
      legal: moves.flatMap((m) => (m.type === "play_card" ? [m.card] : [])),
      canTakeHand: false,
      deadlineTs,
      phase: ctx.state.phase,
    };
    if (ctx.state.phase !== "call") return base;
    return {
      ...base,
      suits: moves.flatMap((m) => (m.type === "call_trump" ? [m.suit] : [])),
      canRedeal: canRedeal(ctx.state, seat),
    };
  },
  presentationDelayMs: presentationDelay,
  stateHash: (state) => stateHash(state),
  highlights: rungHighlights,
};
