import {
  ErrorCodes,
  err,
  ok,
  ruleError,
  type Result,
  type RuleError,
  type SeatIndex,
} from "@hulla/shared-ts";

import { cardSuit, isCard, sortCards, type Card } from "../card-kit";

import { resolveRules, type RulesMode, type ThullaPreset, type ThullaRules } from "./rules";
import {
  EMPTY_STATS,
  EMPTY_TRICK,
  THULLA_RULES_VERSION as RULES_VERSION,
  type SeatStats,
  type ThullaPhase,
  type ThullaState,
} from "./state";

/**
 * A full, explicit mid-round state as the shared vectors describe it (PROJECT_STRUCTURE 4). Every
 * engine builds the same state from the same setup, so a vector never depends on a deal. Hands are
 * written as they were before the listed `plays`; the played cards sit in the pile. An empty hand
 * marks an escaped seat. Two seats describe an endgame directly; a deal still needs three to six.
 */
export interface ThullaSetup {
  readonly hands: readonly (readonly Card[])[];
  readonly leader: SeatIndex;
  readonly dealerSeat?: SeatIndex;
  readonly trickIndex?: number;
  readonly discard?: readonly Card[];
  readonly mode?: RulesMode;
  readonly preset?: ThullaPreset;
  readonly rules?: Partial<ThullaRules>;
  readonly phase?: ThullaPhase;
  readonly plays?: readonly { readonly seat: SeatIndex; readonly card: Card }[];
  readonly openingPending?: readonly SeatIndex[];
  readonly handTakeUsed?: boolean;
  readonly pendingAutoLead?: boolean;
  /** Escaped seats in escape order; defaults to seat order of the empty hands. */
  readonly escapeOrder?: readonly SeatIndex[];
  readonly stats?: readonly SeatStats[];
}

export function stateFromSetup(setup: ThullaSetup): Result<ThullaState, RuleError> {
  const rules = resolveRules(
    setup.mode ?? "private",
    setup.preset ?? "standard",
    setup.rules ?? {},
  );
  if (!rules.ok) {
    return rules;
  }
  const seatCount = setup.hands.length;
  if (seatCount < 2 || seatCount > 6) {
    return err(ruleError(ErrorCodes.rules.invalidSeatCount, { seatCount }));
  }
  const plays = setup.plays ?? [];
  const seen = new Set<Card>();
  for (const card of [...setup.hands.flat(), ...(setup.discard ?? [])]) {
    if (!isCard(card) || seen.has(card)) {
      return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "bad_card", card }));
    }
    seen.add(card);
  }
  for (const play of plays) {
    if (!setup.hands[play.seat]?.includes(play.card)) {
      return err(
        ruleError(ErrorCodes.rules.invalidRules, { reason: "play_not_in_hand", card: play.card }),
      );
    }
  }
  if (!Number.isInteger(setup.leader) || setup.leader < 0 || setup.leader >= seatCount) {
    return err(ruleError(ErrorCodes.rules.invalidSeat, { seat: setup.leader }));
  }
  const onTable = new Set(plays.map((p) => p.card));
  const escapeOrder =
    setup.escapeOrder ?? setup.hands.flatMap((hand, seat) => (hand.length === 0 ? [seat] : []));
  const seats = setup.hands.map((hand, seat) => {
    const escaped = hand.length === 0;
    return {
      hand: sortCards(hand.filter((card) => !onTable.has(card))),
      escaped,
      escapeRank: escaped ? escapeOrder.indexOf(seat) + 1 : null,
    };
  });
  const first = plays[0];
  const last = plays[plays.length - 1];
  let turnSeat: SeatIndex | null = setup.leader;
  if (setup.openingPending !== undefined) {
    turnSeat = null;
  } else if (last !== undefined) {
    turnSeat = nextActive(seats, last.seat);
  }
  return ok({
    rulesVersion: RULES_VERSION,
    rules: rules.value,
    seatCount,
    dealerSeat: setup.dealerSeat ?? 0,
    leaderSeat: setup.leader,
    turnSeat,
    phase: setup.phase ?? "play",
    trickIndex: setup.trickIndex ?? 1,
    seats,
    trick:
      first === undefined
        ? EMPTY_TRICK
        : {
            ledSuit: cardSuit(first.card),
            plays,
            thullaBy: plays.find((p) => cardSuit(p.card) !== cardSuit(first.card))?.seat ?? null,
          },
    discard: setup.discard ?? [],
    openingPending: setup.openingPending ?? [],
    handTakeUsed: setup.handTakeUsed ?? false,
    pendingAutoLead: setup.pendingAutoLead ?? false,
    stats: setup.stats ?? seats.map(() => EMPTY_STATS),
    escapeOrder,
    bhabhiSeat: null,
    confirmedBhabhi: null,
    pendingDraw: null,
    commitment: "setup",
    seedHex: "",
    deckOrder: [],
  });
}

function nextActive(seats: readonly { readonly escaped: boolean }[], from: SeatIndex): SeatIndex {
  for (let step = 1; step <= seats.length; step++) {
    const seat = (from + step) % seats.length;
    if (!seats[seat]?.escaped) {
      return seat;
    }
  }
  return from;
}
