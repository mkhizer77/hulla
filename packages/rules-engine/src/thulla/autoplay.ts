import { cardRank, cardSuit, rankValue, suitIndex, type Card } from "../card-kit";

import { legalMoves } from "./legal";

import type { ThullaMove } from "./moves";
import type { ThullaState } from "./state";
import type { SeatIndex } from "@hulla/shared-ts";

/**
 * The deterministic auto-play policy of GDD R44, used for timeouts, absences and the confirmed
 * fast-forward (R41). Never a take (R34). Returns null only when the seat has no legal move.
 */
export function autoPlayMove(state: ThullaState, seat: SeatIndex): ThullaMove | null {
  const cards: Card[] = [];
  for (const move of legalMoves(state, seat)) {
    if (move.type === "play_card") {
      cards.push(move.card);
    }
  }
  const first = cards[0];
  if (first === undefined) {
    return null;
  }
  if (cards.length === 1) {
    return { type: "play_card", card: first };
  }
  const leading = state.trick.plays.length === 0 && state.phase !== "opening";
  return { type: "play_card", card: leading ? leadChoice(cards) : responseChoice(state, cards) };
}

/** Following suit: the lowest card of it. Forced thulla: the highest card, ties broken ♠ ♥ ♣ ♦. */
function responseChoice(state: ThullaState, cards: readonly Card[]): Card {
  const ledSuit = state.phase === "opening" ? "S" : state.trick.ledSuit;
  const following = cards.filter((card) => cardSuit(card) === ledSuit);
  if (following.length > 0) {
    return lowest(following);
  }
  return cards.reduce((best, card) => {
    const byRank = rankValue(cardRank(card)) - rankValue(cardRank(best));
    if (byRank !== 0) {
      return byRank > 0 ? card : best;
    }
    return suitIndex(cardSuit(card)) < suitIndex(cardSuit(best)) ? card : best;
  });
}

/** Leading: the lowest card of the longest suit; ties between suits go to the lowest rank overall. */
function leadChoice(cards: readonly Card[]): Card {
  const counts = new Map<string, number>();
  for (const card of cards) {
    counts.set(cardSuit(card), (counts.get(cardSuit(card)) ?? 0) + 1);
  }
  const most = Math.max(...counts.values());
  const candidates = cards.filter((card) => counts.get(cardSuit(card)) === most);
  return lowest(candidates);
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
