import { ErrorCodes, ruleError, type RuleError, type SeatIndex } from "@hulla/shared-ts";

import { SUITS, cardRank, cardSuit, isCard, rankValue, type Card } from "../card-kit";

import { RUNG_SEAT_COUNT, type RungPhase, type RungState } from "./state";

import type { RungMove } from "./moves";

/** RG7: a caller may ask at most twice in a row. */
export const MAX_REDEALS = 2;

const JACK = rankValue("J");

export function currentRungPhase(state: RungState): RungPhase {
  return state.phase;
}

export function isRungRoundOver(state: RungState): boolean {
  return state.phase === "ended";
}

/** RG7: allowed by the rules, not asked twice already, and no A, K, Q or J among the five. */
export function canRedeal(state: RungState, seat: SeatIndex): boolean {
  return (
    state.phase === "call" &&
    seat === state.callerSeat &&
    state.rules.redeal === "noHonours" &&
    state.redeals < MAX_REDEALS &&
    (state.hands[seat] ?? []).every((card) => rankValue(cardRank(card)) < JACK)
  );
}

/** RG11: cards of the led suit when any are held, otherwise anything. */
export function playableCards(state: RungState, seat: SeatIndex): readonly Card[] {
  const hand = state.hands[seat] ?? [];
  const led = state.trick.plays[0];
  if (led === undefined) {
    return hand;
  }
  const following = hand.filter((card) => cardSuit(card) === cardSuit(led.card));
  return following.length > 0 ? following : hand;
}

/** Every move `applyRungMove` would accept for this seat, and nothing else (ARCHITECTURE 7.1). */
export function legalRungMoves(state: RungState, seat: SeatIndex): readonly RungMove[] {
  if (state.phase === "ended" || state.turnSeat !== seat) {
    return [];
  }
  switch (state.phase) {
    case "call": {
      // RG6: one of the four suits, never a pass.
      const moves: RungMove[] = SUITS.map((suit) => ({ type: "call_trump", suit }));
      if (canRedeal(state, seat)) {
        moves.push({ type: "redeal" });
      }
      return moves;
    }
    case "courtChoice":
      return [
        { type: "court_choice", playOn: false },
        { type: "court_choice", playOn: true },
      ];
    case "play":
      return playableCards(state, seat).map((card) => ({ type: "play_card", card }));
  }
}

const PHASE_OF: Readonly<Record<RungMove["type"], RungPhase>> = {
  call_trump: "call",
  redeal: "call",
  play_card: "play",
  court_choice: "courtChoice",
};

/** The catalogue error for a move, or null when it is legal. Total: never throws (CODING_RULES 12). */
export function validateRungMove(
  state: RungState,
  seat: SeatIndex,
  move: RungMove,
): RuleError | null {
  if (!Number.isInteger(seat) || seat < 0 || seat >= RUNG_SEAT_COUNT) {
    return ruleError(ErrorCodes.rules.invalidSeat, { seat });
  }
  if (state.phase === "ended") {
    return ruleError(ErrorCodes.rules.roundOver);
  }
  if (PHASE_OF[move.type] !== state.phase) {
    return ruleError(ErrorCodes.rules.wrongPhase, { phase: state.phase });
  }
  if (state.turnSeat !== seat) {
    return ruleError(ErrorCodes.rules.notYourTurn);
  }
  switch (move.type) {
    case "call_trump":
      return (SUITS as readonly string[]).includes(move.suit)
        ? null
        : ruleError(ErrorCodes.rules.illegalMove, { reason: "unknown_suit" });
    case "redeal":
      return canRedeal(state, seat)
        ? null
        : ruleError(ErrorCodes.rules.illegalMove, { reason: "redeal_not_allowed" });
    case "court_choice":
      return typeof move.playOn === "boolean"
        ? null
        : ruleError(ErrorCodes.rules.illegalMove, { reason: "invalid_choice" });
    case "play_card": {
      const hand = state.hands[seat] ?? [];
      if (!isCard(move.card) || !hand.includes(move.card)) {
        return ruleError(ErrorCodes.rules.cardNotInHand);
      }
      return playableCards(state, seat).includes(move.card)
        ? null
        : ruleError(ErrorCodes.rules.mustFollowSuit);
    }
  }
}
