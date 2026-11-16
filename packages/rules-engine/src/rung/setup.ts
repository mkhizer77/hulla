import {
  ErrorCodes,
  err,
  ok,
  ruleError,
  type Result,
  type RuleError,
  type SeatIndex,
} from "@hulla/shared-ts";

import { isCard, sortCards, type Card, type Suit } from "../card-kit";

import { RUNG_RULES_VERSION, defaultRungRules, validateRungRules, type RungRules } from "./rules";
import {
  EMPTY_RUNG_STATS,
  RUNG_SEAT_COUNT,
  type RungSeatStats,
  type RungState,
  type RungTeam,
} from "./state";

/**
 * A deal already in the play phase, written out in full: the shape the shared vectors and the
 * tests build positions from. Anything left out is the start of a deal.
 */
export interface RungSetup {
  readonly rules?: Partial<RungRules>;
  readonly hands: readonly (readonly Card[])[];
  readonly trump: Suit;
  readonly dealer?: SeatIndex;
  /** The seat that led the open trick; defaults to the caller. */
  readonly leader?: SeatIndex;
  readonly plays?: readonly { readonly seat: SeatIndex; readonly card: Card }[];
  readonly trickIndex?: number;
  readonly counts?: readonly [number, number];
  readonly pile?: number;
  readonly lastWinner?: SeatIndex | null;
  readonly run?: number;
  readonly lastWinAce?: boolean;
  readonly runAceBlocked?: boolean;
  readonly chasing?: RungTeam | null;
  readonly stats?: readonly RungSeatStats[];
}

function invalid(reason: string): Result<RungState, RuleError> {
  return err(ruleError(ErrorCodes.rules.invalidRules, { reason }));
}

export function rungStateFromSetup(setup: RungSetup): Result<RungState, RuleError> {
  const rules = validateRungRules({ ...defaultRungRules("private"), ...setup.rules });
  if (!rules.ok) {
    return rules;
  }
  if (setup.hands.length !== RUNG_SEAT_COUNT) {
    return err(ruleError(ErrorCodes.rules.invalidSeatCount, { seatCount: setup.hands.length }));
  }
  const plays = setup.plays ?? [];
  const cards = [...setup.hands.flat(), ...plays.map((play) => play.card)];
  if (!cards.every(isCard) || new Set(cards).size !== cards.length) {
    return invalid("invalid_cards");
  }
  const dealerSeat = setup.dealer ?? 0;
  const callerSeat = (dealerSeat + 1) % RUNG_SEAT_COUNT;
  const leaderSeat = setup.leader ?? callerSeat;
  if (plays.some((play, index) => play.seat !== (leaderSeat + index) % RUNG_SEAT_COUNT)) {
    return invalid("plays_out_of_order");
  }
  if (plays.length >= RUNG_SEAT_COUNT) {
    return invalid("trick_already_complete");
  }
  return ok({
    rulesVersion: RUNG_RULES_VERSION,
    rules: rules.value,
    seatCount: RUNG_SEAT_COUNT,
    dealerSeat,
    callerSeat,
    phase: "play",
    turnSeat: (leaderSeat + plays.length) % RUNG_SEAT_COUNT,
    hands: setup.hands.map(sortCards),
    stock: [],
    trump: setup.trump,
    redeals: 0,
    trickIndex: setup.trickIndex ?? 0,
    trick: { leaderSeat, plays },
    lastWinner: setup.lastWinner ?? null,
    run: setup.run ?? 0,
    lastWinAce: setup.lastWinAce ?? false,
    runAceBlocked: setup.runAceBlocked ?? false,
    pile: setup.pile ?? 0,
    counts: setup.counts ?? [0, 0],
    chasing: setup.chasing ?? null,
    outcome: null,
    stats: setup.stats ?? setup.hands.map(() => EMPTY_RUNG_STATS),
    commitment: "",
    seedHex: "",
    deckOrder: [],
  });
}
