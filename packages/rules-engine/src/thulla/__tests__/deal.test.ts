import { createHash } from "node:crypto";

import {
  Xoshiro128StarStar,
  canonicalJson,
  hexToBytes,
  stateHash,
  type Result,
  type RuleError,
} from "@hulla/shared-ts";
import * as fc from "fast-check";
import { describe, expect, it } from "vitest";

import { sortCards, standardDeck } from "../../card-kit";
import { createThullaRound, fairDealCommitment, nextDealer, roundStartedEvent } from "../deal";
import { defaultRules, type ThullaRules } from "../rules";
import { activeSeats, nextActiveSeat, type ThullaState } from "../state";

function seedOf(byte: number): Uint8Array {
  return new Uint8Array(32).fill(byte & 0xff);
}

function deal(
  seatCount: number,
  dealerSeat: number,
  seedByte = 7,
  rules: ThullaRules = defaultRules("private"),
): Result<ThullaState, RuleError> {
  const seed = seedOf(seedByte);
  return createThullaRound(
    { rules, seatCount, dealerSeat, seed },
    Xoshiro128StarStar.fromSeed(seed),
  );
}

function expectOk(result: Result<ThullaState, RuleError>): ThullaState {
  if (!result.ok) {
    throw new Error(`expected ok, got ${result.error.code}`);
  }
  return result.value;
}

describe("the deal", () => {
  it("R3_seat_counts_three_to_six_are_accepted_two_and_seven_rejected", () => {
    for (const seatCount of [3, 4, 5, 6]) {
      expect(deal(seatCount, 0).ok).toBe(true);
    }
    for (const seatCount of [2, 7, 8, 4.5]) {
      const result = deal(seatCount, 0);
      expect(result.ok).toBe(false);
      expect(!result.ok && result.error.code).toBe("rules.invalid_seat_count");
    }
  });

  it("R3_a_second_deck_is_rejected_until_v1_2", () => {
    const result = deal(4, 0, 7, { ...defaultRules("private"), deckCount: 2 as 1 });
    expect(!result.ok && result.error.code).toBe("rules.invalid_rules");
  });

  it("rejects a dealer outside the seats and an empty seed", () => {
    expect(!deal(4, 4).ok).toBe(true);
    expect(!deal(4, -1).ok).toBe(true);
    const empty = createThullaRound(
      { rules: defaultRules("private"), seatCount: 4, dealerSeat: 0, seed: new Uint8Array(0) },
      Xoshiro128StarStar.fromState([1, 2, 3, 4]),
    );
    expect(!empty.ok && empty.error.detail?.["reason"]).toBe("empty_seed");
  });

  it("R5_the_dealer_seat_rotates_clockwise_each_round", () => {
    expect(nextDealer(0, 4)).toBe(1);
    expect(nextDealer(3, 4)).toBe(0);
    expect(nextDealer(5, 6)).toBe(0);
  });

  it("R6_the_deal_is_deterministic_for_a_seed_and_differs_across_seeds", () => {
    const first = expectOk(deal(4, 0, 7));
    const again = expectOk(deal(4, 0, 7));
    const other = expectOk(deal(4, 0, 8));
    expect(stateHash(first)).toBe(stateHash(again));
    expect(stateHash(first)).not.toBe(stateHash(other));
    expect(first.deckOrder).not.toEqual(standardDeck());
    expect(stateHash(first)).toMatch(/^[0-9a-f]{64}$/);
  });

  it("R6_the_commitment_is_sha256_of_seed_and_deck_order_and_verifies_from_the_reveal", () => {
    const state = expectOk(deal(5, 2, 11));
    const seed = hexToBytes(state.seedHex);
    expect(fairDealCommitment(seed, state.deckOrder)).toBe(state.commitment);
    const oracle = createHash("sha256")
      .update(seed)
      .update(canonicalJson(state.deckOrder), "utf8")
      .digest("hex");
    expect(state.commitment).toBe(oracle);
    const tampered = state.deckOrder.slice().reverse();
    expect(fairDealCommitment(seed, tampered)).not.toBe(state.commitment);
  });

  it("R7_E20_distribution_by_seat_count_with_extra_cards_left_of_the_new_dealer", () => {
    const sizes = (state: ThullaState): number[] => state.seats.map((seat) => seat.hand.length);
    expect(sizes(expectOk(deal(3, 2)))).toEqual([18, 17, 17]);
    expect(sizes(expectOk(deal(3, 0)))).toEqual([17, 18, 17]);
    expect(sizes(expectOk(deal(4, 1)))).toEqual([13, 13, 13, 13]);
    expect(sizes(expectOk(deal(5, 4)))).toEqual([11, 11, 10, 10, 10]);
    expect(sizes(expectOk(deal(5, 0)))).toEqual([10, 11, 11, 10, 10]);
    expect(sizes(expectOk(deal(6, 5)))).toEqual([9, 9, 9, 9, 8, 8]);
  });

  it("R7_every_card_is_dealt_exactly_once", () => {
    fc.assert(
      fc.property(
        fc.integer({ min: 3, max: 6 }),
        fc.integer({ min: 0, max: 255 }),
        (seatCount, seedByte) => {
          const state = expectOk(deal(seatCount, seedByte % seatCount, seedByte));
          const dealt = sortCards(state.seats.flatMap((seat) => seat.hand));
          expect(dealt).toEqual(standardDeck());
          expect(sortCards(state.deckOrder)).toEqual(standardDeck());
        },
      ),
    );
  });

  it("R8_hands_are_stored_in_canonical_order", () => {
    const state = expectOk(deal(6, 0, 3));
    for (const seat of state.seats) {
      expect(seat.hand).toEqual(sortCards(seat.hand));
    }
  });

  it("R9_the_ace_of_spades_holder_is_the_opening_leader_and_the_rest_respond", () => {
    fc.assert(
      fc.property(fc.integer({ min: 3, max: 6 }), fc.integer({ min: 0, max: 255 }), (n, byte) => {
        const state = expectOk(deal(n, 0, byte));
        expect(state.seats[state.leaderSeat]?.hand).toContain("AS");
        expect(state.turnSeat).toBe(state.leaderSeat);
        expect(state.phase).toBe("opening");
        expect(state.openingPending).toHaveLength(n - 1);
        expect(state.openingPending).not.toContain(state.leaderSeat);
        expect(state.trick).toEqual({ ledSuit: null, plays: [], thullaBy: null });
        expect(state.discard).toEqual([]);
        expect(state.escapeOrder).toEqual([]);
        expect(state.bhabhiSeat).toBeNull();
        expect(state.trickIndex).toBe(0);
      }),
    );
  });

  it("round_started carries the commitment and the public hand counts", () => {
    const state = expectOk(deal(4, 3, 9));
    expect(roundStartedEvent(state)).toEqual({
      type: "round_started",
      commitment: state.commitment,
      seatCount: 4,
      dealerSeat: 3,
      leaderSeat: state.leaderSeat,
      handCounts: [13, 13, 13, 13],
    });
  });

  it("active seats and the next active seat skip escaped players", () => {
    const state = expectOk(deal(4, 0));
    expect(activeSeats(state)).toEqual([0, 1, 2, 3]);
    const seats = state.seats.map((seat, i) =>
      i === 1 ? { ...seat, escaped: true, escapeRank: 1 } : seat,
    );
    const withEscape: ThullaState = { ...state, seats };
    expect(activeSeats(withEscape)).toEqual([0, 2, 3]);
    expect(nextActiveSeat(withEscape, 0)).toBe(2);
    expect(nextActiveSeat(withEscape, 3)).toBe(0);
    const alone: ThullaState = {
      ...state,
      seats: state.seats.map((seat, i) =>
        i === 2 ? seat : { ...seat, escaped: true, escapeRank: i },
      ),
    };
    expect(nextActiveSeat(alone, 2)).toBe(2);
  });
});
