import { ErrorCodes, ruleError, type RuleError, type SeatIndex } from "@hulla/shared-ts";

import { cardSuit, type Card, type Suit } from "../card-kit";

import { activeSeats, type SeatState, type ThullaPhase, type ThullaState } from "./state";

import type { ThullaMove } from "./moves";

const ACE_OF_SPADES: Card = "AS";

export function currentPhase(state: ThullaState): ThullaPhase {
  return state.phase;
}

export function isRoundOver(state: ThullaState): boolean {
  return state.phase === "ended";
}

/** Every move `applyMove` would accept for this seat, and nothing else (ARCHITECTURE 7.1). */
export function legalMoves(state: ThullaState, seat: SeatIndex): readonly ThullaMove[] {
  const seatState = state.seats[seat];
  if (state.phase === "ended" || seatState === undefined || seatState.escaped) {
    return [];
  }
  if (state.phase === "opening") {
    return openingMoves(state, seat, seatState);
  }
  if (state.trick.plays.length === 0) {
    return leadMoves(state, seat, seatState);
  }
  if (state.turnSeat !== seat) {
    return [];
  }
  return followOrAny(seatState.hand, state.trick.ledSuit);
}

/** R12: the Ace of Spades opens; R13 and R15: the others follow spades if they hold any. */
function openingMoves(state: ThullaState, seat: SeatIndex, seatState: SeatState): ThullaMove[] {
  if (state.trick.plays.length === 0) {
    return seat === state.leaderSeat ? [play(ACE_OF_SPADES)] : [];
  }
  return mayRespondInOpening(state, seat) ? followOrAny(seatState.hand, "S") : [];
}

function mayRespondInOpening(state: ThullaState, seat: SeatIndex): boolean {
  return state.turnSeat === null ? state.openingPending.includes(seat) : state.turnSeat === seat;
}

/** R16: the leader leads anything; R32 and R35: a take is on offer before the lead. */
function leadMoves(state: ThullaState, seat: SeatIndex, seatState: SeatState): ThullaMove[] {
  const moves: ThullaMove[] = seat === state.leaderSeat ? seatState.hand.map(play) : [];
  if (canTakeHand(state, seat)) {
    moves.push({ type: "take_hand" });
  }
  return moves;
}

/** R18 and R19: cards of the led suit when any are held, otherwise anything. */
function followOrAny(hand: readonly Card[], ledSuit: Suit | null): ThullaMove[] {
  const following = ledSuit === null ? [] : hand.filter((card) => cardSuit(card) === ledSuit);
  return (following.length > 0 ? following : hand).map(play);
}

function play(card: Card): ThullaMove {
  return { type: "play_card", card };
}

/** R31 to R36: before the lead, from trick two, with three or more active, once per window. */
export function canTakeHand(state: ThullaState, seat: SeatIndex): boolean {
  return takeHandError(state, seat) === null;
}

export function takeHandError(state: ThullaState, seat: SeatIndex): RuleError | null {
  const windowClosed = ruleError(ErrorCodes.rules.handTakeWindowClosed);
  if (state.rules.handTaking === "off") {
    return ruleError(ErrorCodes.rules.handTakeDisabled);
  }
  if (state.phase !== "play" || state.trick.plays.length > 0 || state.pendingAutoLead) {
    return windowClosed;
  }
  if (activeSeats(state).length < 3) {
    return ruleError(ErrorCodes.rules.handTakeDisabledEndgame);
  }
  if (state.trickIndex < 1 || state.handTakeUsed) {
    return windowClosed;
  }
  if (state.rules.handTaking === "leaderOnly" && seat !== state.leaderSeat) {
    return ruleError(ErrorCodes.rules.handTakeNotLeader);
  }
  return null;
}

/** The single validation path `applyMove` and `legalMoves` share (R47). */
export function validateMove(
  state: ThullaState,
  seat: SeatIndex,
  move: ThullaMove,
): RuleError | null {
  if (state.phase === "ended") {
    return ruleError(ErrorCodes.rules.roundOver);
  }
  const seatState = state.seats[seat];
  if (seatState === undefined) {
    return ruleError(ErrorCodes.rules.invalidSeat, { seat });
  }
  if (seatState.escaped) {
    return ruleError(ErrorCodes.rules.seatNotActive, { seat });
  }
  if (move.type === "take_hand") {
    return takeHandError(state, seat);
  }
  if (!seatState.hand.includes(move.card)) {
    return ruleError(ErrorCodes.rules.cardNotInHand, { card: move.card });
  }
  if (state.phase === "opening") {
    return validateOpeningPlay(state, seat, seatState, move.card);
  }
  if (state.trick.plays.length === 0) {
    return seat === state.leaderSeat ? null : ruleError(ErrorCodes.rules.notYourTurn, { seat });
  }
  if (state.turnSeat !== seat) {
    return ruleError(ErrorCodes.rules.notYourTurn, { seat });
  }
  return followSuitError(seatState.hand, state.trick.ledSuit, move.card);
}

function validateOpeningPlay(
  state: ThullaState,
  seat: SeatIndex,
  seatState: SeatState,
  card: Card,
): RuleError | null {
  if (state.trick.plays.length === 0) {
    if (seat !== state.leaderSeat) {
      return ruleError(ErrorCodes.rules.notYourTurn, { seat });
    }
    return card === ACE_OF_SPADES ? null : ruleError(ErrorCodes.rules.mustLeadAceOfSpades);
  }
  if (!mayRespondInOpening(state, seat)) {
    return ruleError(ErrorCodes.rules.notYourTurn, { seat });
  }
  return followSuitError(seatState.hand, "S", card);
}

function followSuitError(
  hand: readonly Card[],
  ledSuit: Suit | null,
  card: Card,
): RuleError | null {
  if (ledSuit === null || cardSuit(card) === ledSuit) {
    return null;
  }
  const holdsLedSuit = hand.some((held) => cardSuit(held) === ledSuit);
  return holdsLedSuit ? ruleError(ErrorCodes.rules.mustFollowSuit, { ledSuit }) : null;
}
