import { describe, expect, it } from "vitest";

import { applyMove, legalMoves } from "../engine";

import { cardsAtTable, fixedRng, handOf, stateFrom, types } from "./helpers";

describe("escaping and the empty-hand leader", () => {
  it("R25_a_player_with_no_cards_who_is_not_the_next_leader_escapes", () => {
    const state = stateFrom({
      hands: [["6H", "2C"], ["KH"], ["7H", "4C"]],
      leader: 0,
      discard: ["2S"],
      plays: [
        { seat: 0, card: "6H" },
        { seat: 1, card: "KH" },
      ],
    });
    const result = applyMove(state, 2, { type: "play_card", card: "7H" }, fixedRng());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    // Seat 1 emptied its hand but holds the lead, so it does not escape; it draws instead (R26).
    expect(result.state.leaderSeat).toBe(1);
    expect(result.state.seats[1]).toMatchObject({ escaped: false, hand: ["2S"] });
    expect(result.state.escapeOrder).toEqual([]);
  });

  it("R26_the_leader_who_wins_with_the_last_card_draws_from_the_discard_excluding_this_trick", () => {
    // GDD 6.16 example 2: Noor (3) holds J♣ only; Bilal (2) leads 4♣.
    const state = stateFrom({
      hands: [["9C", "2D"], ["7C", "5D"], ["4C", "TD"], ["JC"]],
      leader: 2,
      discard: ["8D", "2S", "3S"],
      plays: [
        { seat: 2, card: "4C" },
        { seat: 3, card: "JC" },
        { seat: 0, card: "9C" },
      ],
    });
    const result = applyMove(state, 1, { type: "play_card", card: "7C" }, fixedRng([0]));
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(types(result.events)).toEqual(["card_played", "trick_resolved", "private_draw"]);
    expect(result.events[2]).toEqual({
      type: "private_draw",
      seat: 3,
      card: "8D",
      source: "discard",
      from: null,
      visibleTo: [3],
    });
    expect(handOf(result.state, 3)).toEqual(["8D"]);
    expect(result.state.discard).toEqual(["2S", "3S", "4C", "JC", "9C", "7C"]);
    expect(result.state.leaderSeat).toBe(3);
    expect(result.state.pendingAutoLead).toBe(true);
    expect(legalMoves(result.state, 3)).toEqual([{ type: "play_card", card: "8D" }]);
    expect(cardsAtTable(result.state)).toBe(cardsAtTable(state));
  });

  it("R27_the_next_trick_resolves_normally_and_the_drawer_escapes_when_beaten", () => {
    const state = stateFrom({
      hands: [["2D", "3H"], ["5D", "4H"], ["TD", "6H"], ["8D"]],
      leader: 3,
      pendingAutoLead: true,
    });
    let next = applyMove(state, 3, { type: "play_card", card: "8D" }, fixedRng());
    expect(next.ok && next.state.pendingAutoLead).toBe(false);
    next = next.ok ? applyMove(next.state, 0, { type: "play_card", card: "2D" }, fixedRng()) : next;
    next = next.ok ? applyMove(next.state, 1, { type: "play_card", card: "5D" }, fixedRng()) : next;
    const result = next.ok
      ? applyMove(next.state, 2, { type: "play_card", card: "TD" }, fixedRng())
      : next;
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.events[1]).toMatchObject({ type: "trick_resolved", picker: null, nextLeader: 2 });
    expect(result.events[2]).toEqual({ type: "escape", seat: 3, rank: 1 });
    expect(result.state.seats[3]).toMatchObject({ escaped: true, escapeRank: 1 });
    expect(result.state.escapeOrder).toEqual([3]);
  });

  it("R27_branch_a_thulla_after_the_draw_makes_the_drawer_pick_up_and_stay_in", () => {
    const state = stateFrom({
      hands: [["2D", "3H"], ["KS", "4H"], ["TD", "6H"], ["8D"]],
      leader: 3,
      pendingAutoLead: true,
    });
    let next = applyMove(state, 3, { type: "play_card", card: "8D" }, fixedRng());
    next = next.ok ? applyMove(next.state, 0, { type: "play_card", card: "2D" }, fixedRng()) : next;
    const result = next.ok
      ? applyMove(next.state, 1, { type: "play_card", card: "KS" }, fixedRng())
      : next;
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.events[1]).toMatchObject({ picker: 3, cards: ["8D", "2D", "KS"] });
    expect(handOf(result.state, 3)).toEqual(["KS", "2D", "8D"]);
    expect(result.state.escapeOrder).toEqual([]);
  });

  it("R28_draw_next_player_takes_a_random_card_from_the_next_seat_who_sees_it_go", () => {
    const state = stateFrom({
      hands: [["9C"], ["7C", "5D", "QH"], ["4C", "TD"]],
      leader: 2,
      rules: { emptyHandLeader: "drawNextPlayer" },
      plays: [
        { seat: 2, card: "4C" },
        { seat: 0, card: "9C" },
      ],
    });
    // Seat 1 keeps [QH, 5D] in canonical order; index 0 is the queen of hearts.
    const result = applyMove(state, 1, { type: "play_card", card: "7C" }, fixedRng([0]));
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.events[2]).toEqual({
      type: "private_draw",
      seat: 0,
      card: "QH",
      source: "nextPlayer",
      from: 1,
      visibleTo: [0, 1],
    });
    expect(handOf(result.state, 0)).toEqual(["QH"]);
    expect(handOf(result.state, 1)).toEqual(["5D"]);
  });

  it("R28_instant_exit_escapes_the_leader_and_the_next_active_seat_leads", () => {
    const state = stateFrom({
      hands: [["9C"], ["7C", "5D"], ["4C", "TD"]],
      leader: 2,
      rules: { emptyHandLeader: "instantExit" },
      plays: [
        { seat: 2, card: "4C" },
        { seat: 0, card: "9C" },
      ],
    });
    const result = applyMove(state, 1, { type: "play_card", card: "7C" }, fixedRng());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.events[2]).toEqual({ type: "escape", seat: 0, rank: 1 });
    expect(result.state.leaderSeat).toBe(1);
    expect(result.state.turnSeat).toBe(1);
    expect(result.state.pendingAutoLead).toBe(false);
    expect(result.state.discard).toEqual(["4C", "9C", "7C"]);
  });

  it("R29_E6_a_last_card_played_as_a_thulla_escapes_immediately_while_the_picker_takes_the_pile", () => {
    // Appendix C, V-061.
    const state = stateFrom({
      hands: [["5C", "2H"], ["TC", "3H"], ["AD"]],
      leader: 0,
      plays: [
        { seat: 0, card: "5C" },
        { seat: 1, card: "TC" },
      ],
    });
    const result = applyMove(state, 2, { type: "play_card", card: "AD" }, fixedRng());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.events[1]).toMatchObject({ picker: 1 });
    expect(result.state.escapeOrder).toEqual([2]);
    expect(result.state.seats.filter((s) => !s.escaped)).toHaveLength(2);
    expect(types(result.events)).toContain("endgame_started");
  });

  it("E31_OQ001_an_empty_discard_under_non_default_toggles_draws_from_the_next_player", () => {
    const state = stateFrom({
      hands: [["9C"], ["7C", "5D"], ["4C", "TD"]],
      leader: 2,
      discard: [],
      plays: [
        { seat: 2, card: "4C" },
        { seat: 0, card: "9C" },
      ],
    });
    const result = applyMove(state, 1, { type: "play_card", card: "7C" }, fixedRng([0]));
    expect(result.ok && result.events[2]).toMatchObject({ source: "nextPlayer", from: 1 });
  });

  it("E3_E7_the_leader_who_led_the_last_card_picks_up_when_it_is_highest_and_stays_active", () => {
    const state = stateFrom({
      hands: [["KH"], ["2H", "4C"], ["QS", "5C"]],
      leader: 0,
      plays: [
        { seat: 0, card: "KH" },
        { seat: 1, card: "2H" },
      ],
    });
    const result = applyMove(state, 2, { type: "play_card", card: "QS" }, fixedRng());
    expect(result.ok && handOf(result.state, 0)).toEqual(["QS", "2H", "KH"]);
    expect(result.ok && result.state.seats[0]?.escaped).toBe(false);
    expect(result.ok && result.state.escapeOrder).toEqual([]);
  });

  it("E5_when_the_drawer_is_beaten_and_a_later_seat_thullas_the_highest_led_card_picks_up", () => {
    const state = stateFrom({
      hands: [["8D"], ["KD", "2C"], ["QS", "5C"]],
      leader: 0,
      plays: [
        { seat: 0, card: "8D" },
        { seat: 1, card: "KD" },
      ],
    });
    const result = applyMove(state, 2, { type: "play_card", card: "QS" }, fixedRng());
    expect(result.ok && result.events[1]).toMatchObject({ picker: 1 });
    expect(result.ok && result.state.escapeOrder).toEqual([0]);
  });

  it("E26_a_draw_on_the_last_trick_before_the_endgame_leaves_the_drawer_one_card_in_a_two_player_game", () => {
    const state = stateFrom({
      hands: [["9C", "KH"], ["7C"], ["4C", "TD", "3H"]],
      leader: 2,
      discard: ["2S", "3S"],
      plays: [
        { seat: 2, card: "4C" },
        { seat: 0, card: "9C" },
      ],
    });
    // Seat 0 wins with 9C but still holds KH; seat 1 played its last card and escapes.
    const result = applyMove(state, 1, { type: "play_card", card: "7C" }, fixedRng([0]));
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.state.escapeOrder).toEqual([1]);
    expect(types(result.events)).toContain("endgame_started");
    // Now seat 0 leads its last card and seat 2 follows lower: seat 0 draws from the opponent (R38).
    const lead = applyMove(result.state, 0, { type: "play_card", card: "KH" }, fixedRng());
    const follow = lead.ok
      ? applyMove(lead.state, 2, { type: "play_card", card: "3H" }, fixedRng([1]))
      : lead;
    expect(follow.ok && follow.events[2]).toMatchObject({
      source: "opponent",
      from: 2,
      visibleTo: [0, 2],
    });
    expect(follow.ok && handOf(follow.state, 0)).toHaveLength(1);
  });

  it("R10_E30_when_everyone_else_escapes_in_the_same_trick_the_leader_is_the_bhabhi_without_a_draw", () => {
    const state = stateFrom({
      hands: [["9C"], ["7C"], ["4C"]],
      leader: 2,
      discard: ["2S"],
      plays: [
        { seat: 2, card: "4C" },
        { seat: 0, card: "9C" },
      ],
    });
    const result = applyMove(state, 1, { type: "play_card", card: "7C" }, fixedRng());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(types(result.events)).toEqual([
      "card_played",
      "trick_resolved",
      "escape",
      "escape",
      "round_ended",
    ]);
    expect(result.state.phase).toBe("ended");
    expect(result.state.bhabhiSeat).toBe(0);
    expect(result.state.escapeOrder).toEqual([1, 2]);
    expect(cardsAtTable(result.state)).toBe(cardsAtTable(state));
  });

  it("R11_escape_ranks_follow_seat_order_from_the_leader_and_are_never_tied", () => {
    const state = stateFrom({
      hands: [["9C", "AH"], ["7C"], ["4C"], ["8C"]],
      leader: 2,
      plays: [
        { seat: 2, card: "4C" },
        { seat: 3, card: "8C" },
        { seat: 0, card: "9C" },
      ],
    });
    const result = applyMove(state, 1, { type: "play_card", card: "7C" }, fixedRng());
    expect(result.ok && result.state.escapeOrder).toEqual([1, 2, 3]);
    expect(result.ok && result.state.seats.map((s) => s.escapeRank)).toEqual([null, 1, 2, 3]);
  });

  it("E12_drawing_the_opponents_last_card_ends_the_round_with_the_leader_as_bhabhi", () => {
    const state = stateFrom({
      hands: [["9S"], ["3S", "7S"], []],
      leader: 0,
      plays: [{ seat: 0, card: "9S" }],
    });
    const result = applyMove(state, 1, { type: "play_card", card: "3S" }, fixedRng([0]));
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(types(result.events)).toEqual([
      "card_played",
      "trick_resolved",
      "private_draw",
      "escape",
      "round_ended",
    ]);
    expect(result.state.bhabhiSeat).toBe(0);
    expect(handOf(result.state, 0)).toEqual(["7S"]);
  });
});
