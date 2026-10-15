import { describe, expect, it } from "vitest";

import { applyMove, legalMoves } from "../engine";

import { fixedRng, handOf, stateFrom, types } from "./helpers";

const cards = (state: Parameters<typeof legalMoves>[0], seat: number): string[] =>
  legalMoves(state, seat).map((m) => (m.type === "play_card" ? m.card : m.type));

describe("normal tricks", () => {
  it("R16_the_leader_may_lead_any_card", () => {
    const state = stateFrom({
      hands: [["2H", "9S", "KD"], ["3H"], ["4C"]],
      leader: 0,
      mode: "ranked",
    });
    expect(cards(state, 0)).toEqual(["9S", "2H", "KD"]);
    expect(cards(state, 1)).toEqual([]);
  });

  it("R17_responses_go_clockwise_and_skip_escaped_seats", () => {
    const state = stateFrom({ hands: [["2H", "9S"], [], ["4H", "5C"], ["6H"]], leader: 0 });
    const led = applyMove(state, 0, { type: "play_card", card: "2H" }, fixedRng());
    expect(led.ok && led.state.turnSeat).toBe(2);
    expect(led.ok && cards(led.state, 3)).toEqual([]);
    const early = led.ok && applyMove(led.state, 3, { type: "play_card", card: "6H" }, fixedRng());
    expect(early && !early.ok && early.error.code).toBe("rules.not_your_turn");
    const escaped = applyMove(state, 1, { type: "play_card", card: "2H" }, fixedRng());
    expect(!escaped.ok && escaped.error.code).toBe("rules.seat_not_active");
  });

  it("R18_a_responder_holding_the_led_suit_must_follow_it", () => {
    const state = stateFrom({
      hands: [["2H"], ["4H", "5C"], ["6H"]],
      leader: 0,
      plays: [{ seat: 0, card: "2H" }],
    });
    expect(cards(state, 1)).toEqual(["4H"]);
    const wrong = applyMove(state, 1, { type: "play_card", card: "5C" }, fixedRng());
    expect(!wrong.ok && wrong.error.code).toBe("rules.must_follow_suit");
    expect(!wrong.ok && wrong.error.detail).toEqual({ ledSuit: "H" });
  });

  it("R19_a_void_responder_may_play_anything_and_it_is_a_thulla", () => {
    const state = stateFrom({
      hands: [["2H"], ["5C", "QS"], ["6H"]],
      leader: 0,
      plays: [{ seat: 0, card: "2H" }],
    });
    expect(cards(state, 1)).toEqual(["QS", "5C"]);
    const result = applyMove(state, 1, { type: "play_card", card: "QS" }, fixedRng());
    expect(result.ok && result.events[0]).toMatchObject({ type: "card_played", thulla: true });
  });

  it("R20_a_thulla_ends_the_trick_immediately_and_later_seats_do_not_play", () => {
    const state = stateFrom({
      hands: [
        ["6H", "2C"],
        ["KH", "3C"],
        ["QS", "4C"],
        ["9H", "5C"],
      ],
      leader: 0,
      trickIndex: 5,
      plays: [
        { seat: 0, card: "6H" },
        { seat: 1, card: "KH" },
      ],
    });
    const result = applyMove(state, 2, { type: "play_card", card: "QS" }, fixedRng());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(types(result.events)).toEqual(["card_played", "trick_resolved"]);
    expect(handOf(result.state, 3)).toEqual(["9H", "5C"]);
    expect(result.state.trick.plays).toEqual([]);
  });

  it("R21_the_highest_card_of_the_led_suit_picks_up_everything_and_leads", () => {
    const state = stateFrom({
      hands: [
        ["6H", "2C"],
        ["KH", "3C"],
        ["QS", "4C"],
        ["9H", "5C"],
      ],
      leader: 0,
      trickIndex: 5,
      plays: [
        { seat: 0, card: "6H" },
        { seat: 1, card: "KH" },
      ],
    });
    const result = applyMove(state, 2, { type: "play_card", card: "QS" }, fixedRng());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.events[1]).toMatchObject({
      type: "trick_resolved",
      picker: 1,
      thullaBy: 2,
      cards: ["6H", "KH", "QS"],
      nextLeader: 1,
    });
    expect(handOf(result.state, 1)).toEqual(["QS", "6H", "KH", "3C"]);
    expect(result.state.leaderSeat).toBe(1);
    expect(result.state.turnSeat).toBe(1);
    expect(result.state.discard).toEqual([]);
    expect(result.state.stats[1]).toMatchObject({ cardsPickedUp: 3, thullasReceived: 1 });
    expect(result.state.stats[2]).toMatchObject({ thullasGiven: 1 });
  });

  it("R22_without_a_thulla_the_cards_go_to_the_discard_and_the_highest_leads", () => {
    const state = stateFrom({
      hands: [
        ["6H", "2C"],
        ["KH", "3C"],
        ["7H", "4C"],
      ],
      leader: 0,
      plays: [
        { seat: 0, card: "6H" },
        { seat: 1, card: "KH" },
      ],
    });
    const result = applyMove(state, 2, { type: "play_card", card: "7H" }, fixedRng());
    expect(result.ok && result.events[1]).toMatchObject({ picker: null, nextLeader: 1 });
    expect(result.ok && result.state.discard).toEqual(["6H", "KH", "7H"]);
    expect(result.ok && result.state.leaderSeat).toBe(1);
  });

  it("R23_the_pile_is_public_in_the_resolution_event", () => {
    const state = stateFrom({
      hands: [["6H", "2C"], ["KH"], ["7H"]],
      leader: 0,
      plays: [
        { seat: 0, card: "6H" },
        { seat: 1, card: "KH" },
      ],
    });
    const result = applyMove(state, 2, { type: "play_card", card: "7H" }, fixedRng());
    expect(result.ok && result.events[1]).toMatchObject({
      cards: ["6H", "KH", "7H"],
      ledSuit: "H",
    });
  });

  it("R24_with_thulla_ends_trick_off_play_continues_and_the_highest_takes_everything", () => {
    const state = stateFrom({
      hands: [
        ["6H", "2C"],
        ["KH", "3C"],
        ["QS", "4C"],
        ["9H", "5C"],
      ],
      leader: 0,
      preset: "hardcore",
      plays: [
        { seat: 0, card: "6H" },
        { seat: 1, card: "KH" },
      ],
    });
    const afterThulla = applyMove(state, 2, { type: "play_card", card: "QS" }, fixedRng());
    expect(afterThulla.ok && types(afterThulla.events)).toEqual(["card_played"]);
    expect(afterThulla.ok && afterThulla.state.turnSeat).toBe(3);
    expect(afterThulla.ok && cards(afterThulla.state, 3)).toEqual(["9H"]);
    if (!afterThulla.ok) throw new Error(afterThulla.error.code);
    const result = applyMove(afterThulla.state, 3, { type: "play_card", card: "9H" }, fixedRng());
    expect(result.ok && result.events[1]).toMatchObject({
      picker: 1,
      cards: ["6H", "KH", "QS", "9H"],
    });
    expect(result.ok && handOf(result.state, 1)).toEqual(["QS", "6H", "9H", "KH", "3C"]);
  });

  it("R47_an_illegal_play_is_rejected_with_a_reason_and_leaves_the_state_untouched", () => {
    const state = stateFrom({
      hands: [["2H"], ["4H", "5C"], ["6H"]],
      leader: 0,
      plays: [{ seat: 0, card: "2H" }],
    });
    const notHeld = applyMove(state, 1, { type: "play_card", card: "AS" }, fixedRng());
    expect(!notHeld.ok && notHeld.error.code).toBe("rules.card_not_in_hand");
    const badSeat = applyMove(state, 9, { type: "play_card", card: "4H" }, fixedRng());
    expect(!badSeat.ok && badSeat.error.code).toBe("rules.invalid_seat");
    expect(state.trick.plays).toHaveLength(1);
    expect(handOf(state, 1)).toEqual(["4H", "5C"]);
  });
});
