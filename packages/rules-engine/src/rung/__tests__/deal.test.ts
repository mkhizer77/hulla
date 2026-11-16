import { createHash } from "node:crypto";

import { canonicalJson, stateHash } from "@hulla/shared-ts";
import { describe, expect, it } from "vitest";

import { cardRank, cardSuit, standardDeck, type Card } from "../../card-kit";
import { autoCallSuit, rungAutoPlayMove } from "../autoplay";
import { createRungRound, rungRoundStartedEvent } from "../deal";
import { legalRungMoves } from "../engine";
import { defaultRungRules } from "../rules";

import { apply, deal, errorOf, rngOf, seedOf, types } from "./helpers";

import type { RungState } from "../state";

const WEAK: readonly Card[] = ["2S", "5S", "7H", "9C", "TD"];

function withCallerHand(state: RungState, hand: readonly Card[]): RungState {
  return {
    ...state,
    hands: state.hands.map((held, seat) => (seat === state.callerSeat ? hand : held)),
  };
}

describe("the Rung deal and the call", () => {
  it("RG1_only_four_seats_are_accepted", () => {
    const rules = defaultRungRules("private");
    for (const seatCount of [2, 3, 5, 6]) {
      const round = createRungRound({ rules, seatCount, dealerSeat: 0, seed: seedOf(1) }, rngOf(1));
      expect(!round.ok && round.error.code).toBe("rules.invalid_seat_count");
    }
    expect(deal().seatCount).toBe(4);
  });

  it("rejects bad rules, a dealer outside the seats and an empty seed", () => {
    const rules = defaultRungRules("private");
    const bad = [
      createRungRound({ rules, seatCount: 4, dealerSeat: 4, seed: seedOf(1) }, rngOf(1)),
      createRungRound({ rules, seatCount: 4, dealerSeat: 0, seed: new Uint8Array() }, rngOf(1)),
      createRungRound(
        { rules: { ...rules, target: 6 as 7 }, seatCount: 4, dealerSeat: 0, seed: seedOf(1) },
        rngOf(1),
      ),
    ];
    for (const round of bad) {
      expect(!round.ok && round.error.code).toBe("rules.invalid_rules");
    }
  });

  it("RG2_one_deck_of_52_is_split_between_the_hands_and_the_stock", () => {
    const state = deal();
    const cards = [...state.hands.flat(), ...state.stock];
    expect(cards.slice().sort()).toEqual(standardDeck().sort());
  });

  it("RG4_the_caller_is_the_seat_after_the_dealer_and_acts_first", () => {
    for (const dealerSeat of [0, 1, 2, 3]) {
      const state = deal(dealerSeat);
      expect(state.callerSeat).toBe((dealerSeat + 1) % 4);
      expect(state.turnSeat).toBe(state.callerSeat);
      expect(state.phase).toBe("call");
    }
  });

  it("RG5_five_cards_each_from_the_caller_and_the_rest_wait_in_the_stock", () => {
    const state = deal(1);
    expect(state.hands.map((hand) => hand.length)).toEqual([5, 5, 5, 5]);
    expect(state.stock).toHaveLength(32);
    expect(state.hands[2]?.slice().sort()).toEqual(state.deckOrder.slice(0, 5).sort());
    expect(state.hands[3]?.slice().sort()).toEqual(state.deckOrder.slice(5, 10).sort());
    expect(state.stock).toEqual(state.deckOrder.slice(20));
    expect(rungRoundStartedEvent(state)).toEqual({
      type: "round_started",
      commitment: state.commitment,
      dealerSeat: 1,
      callerSeat: 2,
      handCounts: [5, 5, 5, 5],
    });
  });

  it("RG5_the_same_seed_gives_the_same_deal_and_the_commitment_binds_it", () => {
    const state = deal(3, 9);
    expect(stateHash(deal(3, 9))).toBe(stateHash(state));
    expect(stateHash(deal(3, 10))).not.toBe(stateHash(state));
    const expected = createHash("sha256")
      .update(seedOf(9))
      .update(canonicalJson(state.deckOrder))
      .digest("hex");
    expect(state.commitment).toBe(expected);
  });

  it("RG6_the_caller_names_one_of_four_suits_and_nobody_else_may_act", () => {
    const state = deal();
    const suits = legalRungMoves(state, state.callerSeat).flatMap((move) =>
      move.type === "call_trump" ? [move.suit] : [],
    );
    expect(suits).toEqual(["S", "H", "C", "D"]);
    for (const seat of [1, 2, 3]) {
      expect(legalRungMoves(state, seat)).toEqual([]);
      expect(errorOf(state, seat, { type: "call_trump", suit: "H" })).toBe("rules.not_your_turn");
    }
    expect(errorOf(state, 0, { type: "call_trump", suit: "X" as "S" })).toBe("rules.illegal_move");
    expect(errorOf(state, 0, { type: "play_card", card: "AS" })).toBe("rules.wrong_phase");
  });

  it("RG7_a_redeal_is_offered_only_for_five_cards_without_an_honour", () => {
    const weak = withCallerHand(deal(), WEAK);
    expect(legalRungMoves(weak, 0)).toContainEqual({ type: "redeal" });
    const withJack = withCallerHand(deal(), ["2S", "5S", "7H", "9C", "JD"]);
    expect(legalRungMoves(withJack, 0)).not.toContainEqual({ type: "redeal" });
    expect(errorOf(withJack, 0, { type: "redeal" })).toBe("rules.illegal_move");
    const off = withCallerHand(deal(3, 7, { ...defaultRungRules("private"), redeal: "off" }), WEAK);
    expect(errorOf(off, 0, { type: "redeal" })).toBe("rules.illegal_move");
  });

  it("RG7_a_redeal_deals_new_cards_from_the_same_dealer_with_a_new_commitment", () => {
    const before = withCallerHand(deal(3, 7), WEAK);
    const { state, events } = apply(before, 0, { type: "redeal" }, rngOf(8));
    expect(state.dealerSeat).toBe(3);
    expect(state.callerSeat).toBe(0);
    expect(state.phase).toBe("call");
    expect(state.redeals).toBe(1);
    expect(state.deckOrder).not.toEqual(before.deckOrder);
    expect(state.hands.map((hand) => hand.length)).toEqual([5, 5, 5, 5]);
    expect([...state.hands.flat(), ...state.stock].sort()).toEqual(standardDeck().sort());
    expect(state.commitment).not.toBe(before.commitment);
    expect(events).toEqual([{ type: "redealt", commitment: state.commitment, redeals: 1 }]);
  });

  it("RGE2_a_third_redeal_in_a_row_is_refused_and_only_the_calls_remain", () => {
    const twice = { ...withCallerHand(deal(), WEAK), redeals: 2 };
    expect(legalRungMoves(twice, 0)).toHaveLength(4);
    expect(errorOf(twice, 0, { type: "redeal" })).toBe("rules.illegal_move");
    const once = { ...twice, redeals: 1 };
    expect(legalRungMoves(once, 0)).toHaveLength(5);
  });

  it("RG8_after_the_call_everyone_holds_thirteen_and_keeps_their_first_five", () => {
    const before = deal(2);
    const { state, events } = apply(before, 3, { type: "call_trump", suit: "D" });
    expect(state.trump).toBe("D");
    expect(state.stock).toEqual([]);
    expect(state.hands.map((hand) => hand.length)).toEqual([13, 13, 13, 13]);
    expect(state.hands.flat().sort()).toEqual(standardDeck().sort());
    before.hands.forEach((five, seat) => {
      expect(state.hands[seat]).toEqual(expect.arrayContaining([...five]));
    });
    // Fours from the stock, starting with the caller, twice round.
    expect(state.hands[3]).toEqual(expect.arrayContaining(before.stock.slice(0, 4)));
    expect(state.hands[0]).toEqual(expect.arrayContaining(before.stock.slice(4, 8)));
    expect(state.hands[3]).toEqual(expect.arrayContaining(before.stock.slice(16, 20)));
    expect(types(events)).toEqual(["trump_called", "hands_completed"]);
  });

  it("RG9_a_silent_caller_gets_the_longest_suit_then_the_higher_ranks_then_suit_order", () => {
    expect(autoCallSuit(["2H", "3H", "4H", "AS", "KS"])).toBe("H");
    expect(autoCallSuit(["2H", "3H", "AC", "KC", "9D"])).toBe("C");
    expect(autoCallSuit(["2H", "3H", "2S", "3S", "9D"])).toBe("S");
    expect(autoCallSuit(["2C", "3C", "2D", "3D", "9S"])).toBe("C");
  });

  it("RGE1_the_deadline_calls_for_an_absent_caller_and_for_nobody_else", () => {
    const state = withCallerHand(deal(), ["2H", "3H", "4H", "AS", "KS"]);
    expect(rungAutoPlayMove(state, 0)).toEqual({ type: "call_trump", suit: "H" });
    expect(rungAutoPlayMove(state, 1)).toBeNull();
  });

  it("RG10_the_caller_leads_the_first_trick_with_any_card", () => {
    const { state } = apply(deal(), 0, { type: "call_trump", suit: "S" });
    expect(state.phase).toBe("play");
    expect(state.turnSeat).toBe(0);
    expect(state.trick).toEqual({ leaderSeat: 0, plays: [] });
    expect(legalRungMoves(state, 0)).toHaveLength(13);
    expect(legalRungMoves(state, 1)).toEqual([]);
    const suits = new Set(state.hands[0]?.map(cardSuit));
    const ranks = new Set(state.hands[0]?.map(cardRank));
    expect(suits.size).toBeGreaterThan(0);
    expect(ranks.size).toBeGreaterThan(0);
  });
});
