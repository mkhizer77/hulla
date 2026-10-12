import { RANKS, SUITS, type Card } from "./card";

import type { SeatIndex } from "@hulla/shared-ts";

/** One standard 52-card deck, no jokers (GDD R1), in canonical order. */
export function standardDeck(): Card[] {
  const deck: Card[] = [];
  for (const suit of SUITS) {
    for (const rank of RANKS) {
      deck.push(`${rank}${suit}`);
    }
  }
  return deck;
}

/** `deckCount` copies of the standard deck, in canonical order. Two decks are reserved for v1.2. */
export function buildDeck(deckCount: number): Card[] {
  const deck: Card[] = [];
  for (let i = 0; i < deckCount; i++) {
    deck.push(...standardDeck());
  }
  return deck;
}

/** The seat after `seat` in play order (GDD R4). Direction is visual only; the engine counts up. */
export function nextSeat(seat: SeatIndex, seatCount: number): SeatIndex {
  return (seat + 1) % seatCount;
}

/**
 * Deals every card one at a time clockwise starting at `firstSeat`, so the seats earliest in
 * dealing order receive the extra cards (GDD R7). Hands come back in dealt order; callers sort.
 */
export function dealClockwise(
  deck: readonly Card[],
  seatCount: number,
  firstSeat: SeatIndex,
): Card[][] {
  const hands: Card[][] = Array.from({ length: seatCount }, () => []);
  deck.forEach((card, i) => {
    const seat = (firstSeat + i) % seatCount;
    hands[seat]?.push(card);
  });
  return hands;
}
