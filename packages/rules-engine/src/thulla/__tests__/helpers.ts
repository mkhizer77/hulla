import { cardSuit, sortCards, type Card } from "../../card-kit";
import { resolveRules, type RulesMode, type ThullaPreset, type ThullaRules } from "../rules";
import { EMPTY_STATS, EMPTY_TRICK, type ThullaPhase, type ThullaState } from "../state";

import type { ThullaEvent } from "../events";
import type { Rng, SeatIndex } from "@hulla/shared-ts";

export interface Setup {
  /** One hand per seat; an empty hand means the seat has escaped, ranked in seat order. */
  readonly hands: readonly (readonly Card[])[];
  readonly leader: SeatIndex;
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
}

/** Builds a full explicit state the way the shared vectors will (PROJECT_STRUCTURE 4). */
export function stateFrom(setup: Setup): ThullaState {
  const rules = resolveRules(
    setup.mode ?? "private",
    setup.preset ?? "standard",
    setup.rules ?? {},
  );
  if (!rules.ok) {
    throw new Error(`bad rules in test setup: ${rules.error.code}`);
  }
  const plays = setup.plays ?? [];
  // Hands are written as they were before the listed plays; the played cards sit in the pile.
  const onTable = new Set(plays.map((p) => p.card));
  const escapeOrder: SeatIndex[] = [];
  const seats = setup.hands.map((hand, seat) => {
    const escaped = hand.length === 0;
    if (escaped) {
      escapeOrder.push(seat);
    }
    const remaining = hand.filter((card) => !onTable.has(card));
    return { hand: sortCards(remaining), escaped, escapeRank: escaped ? escapeOrder.length : null };
  });
  const first = plays[0];
  const lastPlay = plays[plays.length - 1];
  let turnSeat: SeatIndex | null = setup.leader;
  if (setup.openingPending !== undefined) {
    turnSeat = null;
  } else if (lastPlay !== undefined) {
    turnSeat = nextActive(seats, lastPlay.seat);
  }
  return {
    rulesVersion: "1.0.0",
    rules: rules.value,
    seatCount: setup.hands.length,
    dealerSeat: 0,
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
    stats: seats.map(() => EMPTY_STATS),
    escapeOrder,
    bhabhiSeat: null,
    confirmedBhabhi: null,
    pendingDraw: null,
    commitment: "test",
    seedHex: "00",
    deckOrder: [],
  };
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

/** An Rng that answers `nextInt` with the given indices in order (mod the bound), then zeros. */
export function fixedRng(indices: readonly number[] = []): Rng {
  let cursor = 0;
  return {
    nextUint32: () => 0,
    nextInt: (n) => {
      const value = indices[cursor] ?? 0;
      cursor += 1;
      return value % n;
    },
  };
}

export function types(events: readonly ThullaEvent[]): string[] {
  return events.map((event) => event.type);
}

export function handOf(state: ThullaState, seat: SeatIndex): readonly Card[] {
  return state.seats[seat]?.hand ?? [];
}

export function cardsAtTable(state: ThullaState): number {
  const inHands = state.seats.reduce((sum, seat) => sum + seat.hand.length, 0);
  return inHands + state.discard.length + state.trick.plays.length;
}
