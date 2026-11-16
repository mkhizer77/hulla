import { describe, expect, it } from "vitest";

import { rungAutoPlayMove } from "../autoplay";
import { legalRungMoves } from "../engine";
import { trickWinner } from "../trick";

import { SINGLE, apply, errorOf, playAll, stateFrom, types } from "./helpers";

import type { Card } from "../../card-kit";

function cards(moves: readonly { type: string; card?: Card }[]): (Card | undefined)[] {
  return moves.map((move) => move.card);
}

describe("playing a trick", () => {
  it("RG3_turns_pass_to_the_next_seat_from_the_leader", () => {
    const start = stateFrom({
      hands: [["2H"], ["3H"], ["4H"], ["5H"]],
      trump: "S",
      leader: 2,
    });
    expect(start.turnSeat).toBe(2);
    const order = [2, 3, 0];
    let state = start;
    for (const seat of order) {
      const card = state.hands[seat]?.[0] ?? "2H";
      state = apply(state, seat, { type: "play_card", card }).state;
      expect(state.turnSeat).toBe((seat + 1) % 4);
    }
  });

  it("RG11_a_player_holding_the_led_suit_must_follow_it", () => {
    const state = playAll(
      stateFrom({ hands: [["2H"], ["4H", "5C", "AS"], ["6H"], ["7H"]], trump: "S" }),
      "0:2H",
    ).state;
    expect(cards(legalRungMoves(state, 1))).toEqual(["4H"]);
    expect(errorOf(state, 1, { type: "play_card", card: "5C" })).toBe("rules.must_follow_suit");
    expect(errorOf(state, 1, { type: "play_card", card: "AS" })).toBe("rules.must_follow_suit");
  });

  it("RG11_a_void_player_may_play_anything_including_a_rung", () => {
    const state = playAll(
      stateFrom({ hands: [["2H"], ["5C", "AS", "3D"], ["6H"], ["7H"]], trump: "S" }),
      "0:2H",
    ).state;
    expect(cards(legalRungMoves(state, 1))).toEqual(["AS", "5C", "3D"]);
  });

  it("RG12_the_highest_card_of_the_led_suit_wins_when_no_rung_is_played", () => {
    expect(
      trickWinner(
        [
          { seat: 0, card: "9H" },
          { seat: 1, card: "KH" },
          { seat: 2, card: "AC" },
          { seat: 3, card: "TH" },
        ],
        "S",
      ),
    ).toBe(1);
  });

  it("RG12_the_highest_rung_wins_over_any_card_of_the_led_suit", () => {
    const plays = [
      { seat: 0, card: "AH" as Card },
      { seat: 1, card: "2S" as Card },
      { seat: 2, card: "KH" as Card },
      { seat: 3, card: "5S" as Card },
    ];
    expect(trickWinner(plays, "S")).toBe(3);
    expect(trickWinner(plays, "D")).toBe(0);
    expect(trickWinner(plays, null)).toBe(0);
  });

  it("RG13_the_winner_of_a_trick_leads_the_next", () => {
    const { state, events } = playAll(
      stateFrom({
        rules: SINGLE,
        hands: [
          ["2H", "9C"],
          ["3H", "8C"],
          ["AH", "7C"],
          ["4H", "6C"],
        ],
        trump: "S",
      }),
      "0:2H 1:3H 2:AH 3:4H",
    );
    expect(state.turnSeat).toBe(2);
    expect(state.trick).toEqual({ leaderSeat: 2, plays: [] });
    expect(state.trickIndex).toBe(1);
    expect(types(events).slice(-1)).toEqual(["trick_won"]);
    expect(legalRungMoves(state, 2)).toHaveLength(1);
    expect(legalRungMoves(state, 0)).toEqual([]);
  });

  it("RG14_a_revoke_cannot_happen_a_card_is_legal_exactly_when_it_is_accepted", () => {
    const state = playAll(
      stateFrom({
        hands: [["2H"], ["4H", "5C", "AS"], ["6H", "6C"], ["7H", "7C"]],
        trump: "S",
      }),
      "0:2H",
    ).state;
    const legal = cards(legalRungMoves(state, 1));
    for (const card of state.hands[1] ?? []) {
      expect(errorOf(state, 1, { type: "play_card", card }) === null).toBe(legal.includes(card));
    }
  });

  it("refuses a move out of turn, a card not held, a bad seat and a move in the wrong phase", () => {
    const state = stateFrom({ hands: [["2H"], ["3H"], ["4H"], ["5H"]], trump: "S" });
    expect(errorOf(state, 1, { type: "play_card", card: "3H" })).toBe("rules.not_your_turn");
    expect(errorOf(state, 0, { type: "play_card", card: "AD" })).toBe("rules.card_not_in_hand");
    expect(errorOf(state, 0, { type: "play_card", card: "ZZ" as Card })).toBe(
      "rules.card_not_in_hand",
    );
    expect(errorOf(state, 4, { type: "play_card", card: "2H" })).toBe("rules.invalid_seat");
    expect(errorOf(state, 0, { type: "call_trump", suit: "H" })).toBe("rules.wrong_phase");
    expect(errorOf(state, 0, { type: "court_choice", playOn: true })).toBe("rules.wrong_phase");
  });

  it("counts a kaat for the seat that cuts, and none for a rung on a rung lead", () => {
    const cut = playAll(
      stateFrom({ hands: [["2H"], ["3S"], ["4H"], ["5H"]], trump: "S" }),
      "0:2H 1:3S",
    );
    expect(cut.events[1]).toMatchObject({ type: "card_played", seat: 1, cut: true });
    expect(cut.state.stats[1]?.cuts).toBe(1);
    const lead = playAll(
      stateFrom({ hands: [["2S"], ["3S"], ["4H"], ["5H"]], trump: "S" }),
      "0:2S 1:3S",
    );
    expect(lead.state.stats[1]?.cuts).toBe(0);
  });
});

describe("auto-play on a timeout", () => {
  it("RG42_following_suit_plays_the_lowest_card_of_it", () => {
    const state = playAll(
      stateFrom({ hands: [["9H"], ["KH", "4H", "2C"], ["6H"], ["7H"]], trump: "S" }),
      "0:9H",
    ).state;
    expect(rungAutoPlayMove(state, 1)).toEqual({ type: "play_card", card: "4H" });
  });

  it("RG42_a_void_seat_throws_the_lowest_card_of_its_longest_plain_suit", () => {
    const state = playAll(
      stateFrom({ hands: [["9H"], ["2S", "KC", "5C", "3D"], ["6H"], ["7H"]], trump: "S" }),
      "0:9H",
    ).state;
    expect(rungAutoPlayMove(state, 1)).toEqual({ type: "play_card", card: "5C" });
  });

  it("RG42_a_seat_holding_only_rungs_plays_the_lowest_and_a_leader_follows_the_same_rule", () => {
    const void_ = playAll(
      stateFrom({ hands: [["9H"], ["KS", "4S"], ["6H"], ["7H"]], trump: "S" }),
      "0:9H",
    ).state;
    expect(rungAutoPlayMove(void_, 1)).toEqual({ type: "play_card", card: "4S" });
    const lead = stateFrom({
      hands: [["AS", "KD", "3D", "2H"], ["3H"], ["4H"], ["5H"]],
      trump: "S",
    });
    expect(rungAutoPlayMove(lead, 0)).toEqual({ type: "play_card", card: "3D" });
    // Ties between suits go to ♠ ♥ ♣ ♦.
    const tie = stateFrom({ hands: [["9C", "8D"], ["3H"], ["4H"], ["5H"]], trump: "S" });
    expect(rungAutoPlayMove(tie, 0)).toEqual({ type: "play_card", card: "9C" });
    expect(rungAutoPlayMove(lead, 1)).toBeNull();
  });
});
