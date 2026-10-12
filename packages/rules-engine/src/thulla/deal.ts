import {
  ErrorCodes,
  bytesToHex,
  canonicalJson,
  err,
  ok,
  ruleError,
  sha256Hex,
  shuffle,
  utf8Encode,
  type Result,
  type Rng,
  type RuleError,
  type SeatIndex,
} from "@hulla/shared-ts";

import { buildDeck, dealClockwise, nextSeat, sortCards, type Card } from "../card-kit";

import { THULLA_RULES_VERSION, validateRules, type ThullaRules } from "./rules";
import { EMPTY_STATS, THULLA_SEATS, type SeatState, type ThullaState } from "./state";

import type { CreateRoundInput } from "../contract";
import type { ThullaEvent } from "./events";

/** The dealer seat rotates clockwise each round (GDD R5). */
export function nextDealer(dealerSeat: SeatIndex, seatCount: number): SeatIndex {
  return nextSeat(dealerSeat, seatCount);
}

/** SHA-256 over the raw seed bytes followed by the UTF-8 canonical JSON of the deck order (R6). */
export function fairDealCommitment(seed: Uint8Array, deckOrder: readonly Card[]): string {
  const order = utf8Encode(canonicalJson(deckOrder));
  const message = new Uint8Array(seed.length + order.length);
  message.set(seed);
  message.set(order, seed.length);
  return sha256Hex(message);
}

/**
 * The deal (GDD 6.3): shuffle with the injected `Rng`, publish the commitment, deal clockwise from
 * the seat left of the dealer, store hands in canonical order, and seat the Ace of Spades holder as
 * the opening leader. Same rules, seat count, dealer, seed and `Rng` state give the same round.
 */
export function createThullaRound(
  input: CreateRoundInput<ThullaRules>,
  rng: Rng,
): Result<ThullaState, RuleError> {
  const rules = validateRules(input.rules);
  if (!rules.ok) {
    return rules;
  }
  const { seatCount, dealerSeat, seed } = input;
  if (
    !Number.isInteger(seatCount) ||
    seatCount < THULLA_SEATS.min ||
    seatCount > THULLA_SEATS.max
  ) {
    return err(ruleError(ErrorCodes.rules.invalidSeatCount, { seatCount }));
  }
  if (!Number.isInteger(dealerSeat) || dealerSeat < 0 || dealerSeat >= seatCount) {
    return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "invalid_dealer", dealerSeat }));
  }
  if (seed.length === 0) {
    return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "empty_seed" }));
  }

  const deckOrder = shuffle(rng, buildDeck(rules.value.deckCount));
  const commitment = fairDealCommitment(seed, deckOrder);

  const hands = dealClockwise(deckOrder, seatCount, nextSeat(dealerSeat, seatCount));
  const seats: SeatState[] = hands.map((hand) => ({
    hand: sortCards(hand),
    escaped: false,
    escapeRank: null,
  }));

  const leaderSeat = hands.findIndex((hand) => hand.includes("AS"));
  const openingPending: SeatIndex[] = [];
  for (let seat = 0; seat < seatCount; seat++) {
    if (seat !== leaderSeat) {
      openingPending.push(seat);
    }
  }

  return ok({
    rulesVersion: THULLA_RULES_VERSION,
    rules: rules.value,
    seatCount,
    dealerSeat,
    leaderSeat,
    turnSeat: leaderSeat,
    phase: "opening",
    trickIndex: 0,
    seats,
    trick: { ledSuit: null, plays: [], thullaBy: null },
    discard: [],
    openingPending,
    handTakeUsed: false,
    pendingAutoLead: false,
    stats: seats.map(() => EMPTY_STATS),
    escapeOrder: [],
    bhabhiSeat: null,
    confirmedBhabhi: null,
    pendingDraw: null,
    commitment,
    seedHex: bytesToHex(seed),
    deckOrder,
  });
}

export function roundStartedEvent(state: ThullaState): ThullaEvent {
  return {
    type: "round_started",
    commitment: state.commitment,
    seatCount: state.seatCount,
    dealerSeat: state.dealerSeat,
    leaderSeat: state.leaderSeat,
    handCounts: state.seats.map((seat) => seat.hand.length),
  };
}
