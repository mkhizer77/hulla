/** Canonical suit order ♠ ♥ ♣ ♦ for sorting, hashing and tie-breaks (ARCHITECTURE 7.5, GDD R8, R44). */
export const SUITS = ["S", "H", "C", "D"] as const;

/** Rank order low to high: 2 is lowest, Ace highest (GDD R2). `T` is ten. */
export const RANKS = ["2", "3", "4", "5", "6", "7", "8", "9", "T", "J", "Q", "K", "A"] as const;

export type Suit = (typeof SUITS)[number];
export type Rank = (typeof RANKS)[number];

/** A card is its rank then its suit, as the GDD vectors write them: `AS`, `TH`, `2C`. */
export type Card = `${Rank}${Suit}`;

export function isCard(value: string): value is Card {
  return (
    value.length === 2 &&
    (RANKS as readonly string[]).includes(value.charAt(0)) &&
    (SUITS as readonly string[]).includes(value.charAt(1))
  );
}

export function cardRank(card: Card): Rank {
  return card.charAt(0) as Rank;
}

export function cardSuit(card: Card): Suit {
  return card.charAt(1) as Suit;
}

/** 0 for a two, 12 for an Ace. */
export function rankValue(rank: Rank): number {
  return RANKS.indexOf(rank);
}

export function suitIndex(suit: Suit): number {
  return SUITS.indexOf(suit);
}

/** 0..51: suit-major in canonical order, so hashing never depends on arrival order. */
export function cardIndex(card: Card): number {
  return suitIndex(cardSuit(card)) * RANKS.length + rankValue(cardRank(card));
}

export function compareCards(a: Card, b: Card): number {
  return cardIndex(a) - cardIndex(b);
}

/** A new array in canonical order. */
export function sortCards(cards: readonly Card[]): Card[] {
  return cards.slice().sort(compareCards);
}

export function cardsOfSuit(cards: readonly Card[], suit: Suit): Card[] {
  return cards.filter((card) => cardSuit(card) === suit);
}
