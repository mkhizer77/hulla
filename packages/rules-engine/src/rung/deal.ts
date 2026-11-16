import {
  ErrorCodes,
  bytesToHex,
  err,
  hexToBytes,
  ok,
  ruleError,
  shuffle,
  type Result,
  type Rng,
  type RuleError,
  type SeatIndex,
} from "@hulla/shared-ts";

import { sortCards, standardDeck, type Card } from "../card-kit";
import { fairDealCommitment } from "../card-kit/commitment";

import { RUNG_RULES_VERSION, validateRungRules, type RungRules } from "./rules";
import { EMPTY_RUNG_STATS, RUNG_SEAT_COUNT, nextRungSeat, type RungState } from "./state";

import type { CreateRoundInput } from "../contract";
import type { RungEvent } from "./events";

/** RG5: five cards each before the call. */
export const FIRST_DEAL = 5;
/** RG8: the rest in fours. */
const LATER_BATCH = 4;

/** RG5: five cards to each seat starting with the caller; the rest is the stock. */
export function dealFirstFive(
  deckOrder: readonly Card[],
  callerSeat: SeatIndex,
): { hands: Card[][]; stock: Card[] } {
  const hands: Card[][] = Array.from({ length: RUNG_SEAT_COUNT }, () => []);
  for (let i = 0; i < RUNG_SEAT_COUNT; i++) {
    const seat = (callerSeat + i) % RUNG_SEAT_COUNT;
    hands[seat] = sortCards(deckOrder.slice(i * FIRST_DEAL, (i + 1) * FIRST_DEAL));
  }
  return { hands, stock: deckOrder.slice(RUNG_SEAT_COUNT * FIRST_DEAL) };
}

/** RG8: the stock goes out four at a time, twice round, starting with the caller. */
export function completeHands(
  hands: readonly (readonly Card[])[],
  stock: readonly Card[],
  callerSeat: SeatIndex,
): Card[][] {
  const full = hands.map((hand) => [...hand]);
  for (let batch = 0; batch * LATER_BATCH < stock.length; batch++) {
    const seat = (callerSeat + batch) % RUNG_SEAT_COUNT;
    full[seat]?.push(...stock.slice(batch * LATER_BATCH, (batch + 1) * LATER_BATCH));
  }
  return full.map(sortCards);
}

/**
 * The first half of the deal (Rung design 2.3): shuffle with the injected `Rng`, publish the
 * commitment, give each seat five cards and wait for the caller. Same rules, dealer, seed and
 * `Rng` state give the same deal.
 */
export function createRungRound(
  input: CreateRoundInput<RungRules>,
  rng: Rng,
): Result<RungState, RuleError> {
  const rules = validateRungRules(input.rules);
  if (!rules.ok) {
    return rules;
  }
  const { seatCount, dealerSeat, seed } = input;
  if (seatCount !== RUNG_SEAT_COUNT) {
    return err(ruleError(ErrorCodes.rules.invalidSeatCount, { seatCount }));
  }
  if (!Number.isInteger(dealerSeat) || dealerSeat < 0 || dealerSeat >= seatCount) {
    return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "invalid_dealer", dealerSeat }));
  }
  if (seed.length === 0) {
    return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "empty_seed" }));
  }

  const deckOrder = shuffle(rng, standardDeck());
  const callerSeat = nextRungSeat(dealerSeat);
  const { hands, stock } = dealFirstFive(deckOrder, callerSeat);
  return ok({
    rulesVersion: RUNG_RULES_VERSION,
    rules: rules.value,
    seatCount,
    dealerSeat,
    callerSeat,
    phase: "call",
    turnSeat: callerSeat,
    hands,
    stock,
    trump: null,
    redeals: 0,
    trickIndex: 0,
    trick: { leaderSeat: callerSeat, plays: [] },
    lastWinner: null,
    run: 0,
    lastWinAce: false,
    runAceBlocked: false,
    pile: 0,
    counts: [0, 0],
    chasing: null,
    outcome: null,
    stats: hands.map(() => EMPTY_RUNG_STATS),
    commitment: fairDealCommitment(seed, deckOrder),
    seedHex: bytesToHex(seed),
    deckOrder,
  });
}

/** RG7: the same dealer deals again from the next shuffle of the same stream, with a new commitment. */
export function redealRung(state: RungState, rng: Rng): RungState {
  const deckOrder = shuffle(rng, standardDeck());
  const { hands, stock } = dealFirstFive(deckOrder, state.callerSeat);
  return {
    ...state,
    hands,
    stock,
    redeals: state.redeals + 1,
    commitment: fairDealCommitment(hexToBytes(state.seedHex), deckOrder),
    deckOrder,
  };
}

export function rungRoundStartedEvent(state: RungState): RungEvent {
  return {
    type: "round_started",
    commitment: state.commitment,
    dealerSeat: state.dealerSeat,
    callerSeat: state.callerSeat,
    handCounts: state.hands.map((hand) => hand.length),
  };
}
