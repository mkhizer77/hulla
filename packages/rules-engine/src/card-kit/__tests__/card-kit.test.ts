import { describe, expect, it } from "vitest";

import {
  RANKS,
  SUITS,
  buildDeck,
  cardIndex,
  cardRank,
  cardSuit,
  cardsOfSuit,
  compareCards,
  dealClockwise,
  isCard,
  nextSeat,
  rankValue,
  sortCards,
  standardDeck,
  type Card,
} from "../index";

describe("card kit", () => {
  it("R1_deck_has_52_distinct_cards_in_four_suits_and_no_jokers", () => {
    const deck = standardDeck();
    expect(deck).toHaveLength(52);
    expect(new Set(deck).size).toBe(52);
    for (const suit of SUITS) {
      expect(cardsOfSuit(deck, suit)).toHaveLength(13);
    }
    expect(deck.every(isCard)).toBe(true);
    expect(isCard("XS")).toBe(false);
    expect(isCard("10H")).toBe(false);
    expect(isCard("ASS")).toBe(false);
    expect(buildDeck(1)).toEqual(deck);
  });

  it("R2_ranks_order_two_low_ace_high", () => {
    expect(RANKS[0]).toBe("2");
    expect(RANKS[12]).toBe("A");
    expect(rankValue("2")).toBe(0);
    expect(rankValue("T")).toBe(8);
    expect(rankValue("A")).toBe(12);
    expect(compareCards("2S", "AS")).toBeLessThan(0);
    expect(compareCards("KH", "AH")).toBeLessThan(0);
    expect(compareCards("TD", "9D")).toBeGreaterThan(0);
    expect(cardRank("TH")).toBe("T");
    expect(cardSuit("TH")).toBe("H");
  });

  it("R8_canonical_order_is_spades_hearts_clubs_diamonds_then_rank", () => {
    expect(cardIndex("2S")).toBe(0);
    expect(cardIndex("AS")).toBe(12);
    expect(cardIndex("2H")).toBe(13);
    expect(cardIndex("2C")).toBe(26);
    expect(cardIndex("AD")).toBe(51);
    const shuffled: Card[] = ["AD", "2C", "KH", "3S", "2S"];
    expect(sortCards(shuffled)).toEqual(["2S", "3S", "KH", "2C", "AD"]);
    expect(shuffled).toEqual(["AD", "2C", "KH", "3S", "2S"]);
    expect(sortCards(standardDeck())).toEqual(standardDeck());
  });

  it("R4_next_seat_is_clockwise_and_wraps", () => {
    expect(nextSeat(0, 4)).toBe(1);
    expect(nextSeat(3, 4)).toBe(0);
    expect(nextSeat(5, 6)).toBe(0);
  });

  it("R7_deal_goes_one_card_at_a_time_from_the_first_seat_and_extras_fall_earliest", () => {
    const hands = dealClockwise(standardDeck(), 3, 1);
    expect(hands.map((hand) => hand.length)).toEqual([17, 18, 17]);
    expect(hands[1]?.[0]).toBe("2S");
    expect(hands[2]?.[0]).toBe("3S");
    expect(hands[0]?.[0]).toBe("4S");
    expect(dealClockwise(standardDeck(), 5, 0).map((hand) => hand.length)).toEqual([
      11, 11, 10, 10, 10,
    ]);
  });
});
