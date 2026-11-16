import {
  SUITS,
  cardRank,
  cardSuit,
  cardsOfSuit,
  rankValue,
  suitIndex,
  type Card,
  type Suit,
} from "../card-kit";

import { playableCards } from "./legal";

import type { RungMove } from "./moves";
import type { RungState } from "./state";
import type { SeatIndex } from "@hulla/shared-ts";

/**
 * The deterministic policy for a timeout (RG9, RG26, RG42). Returns null only when it is not the
 * seat's turn. A bot covers a seat that stays absent (RG43); that is the table core's job.
 */
export function rungAutoPlayMove(state: RungState, seat: SeatIndex): RungMove | null {
  if (state.phase === "ended" || state.turnSeat !== seat) {
    return null;
  }
  switch (state.phase) {
    case "call":
      return { type: "call_trump", suit: autoCallSuit(state.hands[seat] ?? []) };
    case "courtChoice":
      // RG26: no answer means stop.
      return { type: "court_choice", playOn: false };
    case "play": {
      const cards = playableCards(state, seat);
      const first = cards[0];
      if (first === undefined) {
        return null;
      }
      return { type: "play_card", card: autoCard(state, cards) };
    }
  }
}

/** RG9: the longest suit; ties go to the higher total rank, then ♠ ♥ ♣ ♦. */
export function autoCallSuit(hand: readonly Card[]): Suit {
  let best: Suit = SUITS[0];
  let bestLength = -1;
  let bestRanks = -1;
  for (const suit of SUITS) {
    const cards = cardsOfSuit(hand, suit);
    const ranks = cards.reduce((sum, card) => sum + rankValue(cardRank(card)), 0);
    if (cards.length > bestLength || (cards.length === bestLength && ranks > bestRanks)) {
      best = suit;
      bestLength = cards.length;
      bestRanks = ranks;
    }
  }
  return best;
}

/**
 * RG42. Following suit: the lowest card of it. Leading or void: the lowest card of the longest
 * suit that is not the rung, else the lowest rung.
 */
function autoCard(state: RungState, cards: readonly Card[]): Card {
  const led = state.trick.plays[0];
  if (led !== undefined && cards.some((card) => cardSuit(card) === cardSuit(led.card))) {
    return lowest(cards);
  }
  const plain = cards.filter((card) => cardSuit(card) !== state.trump);
  if (plain.length === 0) {
    return lowest(cards);
  }
  let longest: Card[] = [];
  for (const suit of SUITS) {
    const ofSuit = cardsOfSuit(plain, suit);
    if (ofSuit.length > longest.length) {
      longest = ofSuit;
    }
  }
  return lowest(longest);
}

function lowest(cards: readonly Card[]): Card {
  return cards.reduce((best, card) => {
    const byRank = rankValue(cardRank(card)) - rankValue(cardRank(best));
    if (byRank !== 0) {
      return byRank < 0 ? card : best;
    }
    return suitIndex(cardSuit(card)) < suitIndex(cardSuit(best)) ? card : best;
  });
}
