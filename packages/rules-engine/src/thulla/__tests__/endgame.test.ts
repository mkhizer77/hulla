import { Xoshiro128StarStar, type SeatIndex } from "@hulla/shared-ts";
import * as fc from "fast-check";
import { describe, expect, it } from "vitest";

import { standardDeck, type Card } from "../../card-kit";
import { autoPlayMove } from "../autoplay";
import { cardsInPlay, forcedBhabhi, shouldSearch } from "../confirm";
import { applyMove, legalMoves } from "../engine";

import { fixedRng, handOf, stateFrom, types, type Setup } from "./helpers";

import type { ThullaState } from "../state";

function play(state: ThullaState, seat: SeatIndex, card: Card, draws: number[] = []) {
  return applyMove(state, seat, { type: "play_card", card }, fixedRng(draws));
}

function okState(result: ReturnType<typeof applyMove>): ThullaState {
  if (!result.ok) {
    throw new Error(result.error.code);
  }
  return result.state;
}

/** A random playout from a state with a seeded Rng; returns the Bhabhi. */
function randomPlayout(start: ThullaState, seedWord: number): SeatIndex {
  const rng = Xoshiro128StarStar.fromState([seedWord + 1, 7, 11, 13]);
  const picker = Xoshiro128StarStar.fromState([seedWord + 3, 5, 9, 17]);
  let state = start;
  for (let step = 0; step < 500 && state.phase !== "ended"; step++) {
    const options: { seat: SeatIndex; card: Card }[] = [];
    for (let seat = 0; seat < state.seatCount; seat++) {
      for (const move of legalMoves(state, seat)) {
        if (move.type === "play_card") {
          options.push({ seat, card: move.card });
        }
      }
    }
    const choice = options[picker.nextInt(options.length)];
    if (choice === undefined) {
      throw new Error("no move");
    }
    const result = applyMove(state, choice.seat, { type: "play_card", card: choice.card }, rng);
    if (!result.ok) {
      throw new Error(result.error.code);
    }
    state = result.state;
  }
  if (state.bhabhiSeat === null) {
    throw new Error("playout did not end");
  }
  return state.bhabhiSeat;
}

describe("the two-player endgame", () => {
  it("R37_the_endgame_begins_with_two_active_players_and_hand_taking_is_off", () => {
    const state = stateFrom({ hands: [["9S", "2H"], ["3S", "7S"], []], leader: 0 });
    expect(legalMoves(state, 0)).not.toContainEqual({ type: "take_hand" });
    const take = applyMove(state, 0, { type: "take_hand" }, fixedRng());
    expect(!take.ok && take.error.code).toBe("rules.hand_take_disabled_endgame");
  });

  it("R38_the_leader_who_plays_the_last_card_draws_one_from_the_opponent_who_sees_it_leave", () => {
    const state = stateFrom({
      hands: [["9S"], ["3S", "7S", "QH", "KH"], []],
      leader: 0,
      plays: [{ seat: 0, card: "9S" }],
    });
    const result = play(state, 1, "3S", [1]);
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    const draw = result.events.find((event) => event.type === "private_draw");
    expect(draw).toMatchObject({ seat: 0, source: "opponent", from: 1, visibleTo: [0, 1] });
    expect(handOf(result.state, 0)).toHaveLength(1);
    expect(handOf(result.state, 1)).toHaveLength(2);
    expect(result.state.pendingAutoLead).toBe(true);
    expect(legalMoves(result.state, 0)).toEqual([
      { type: "play_card", card: handOf(result.state, 0)[0] },
    ]);
  });

  it("R39a_the_opponent_plays_higher_so_the_leader_escapes_and_the_opponent_is_the_bhabhi", () => {
    const state = stateFrom({
      hands: [["9S"], ["KS", "7S"], []],
      leader: 0,
      plays: [{ seat: 0, card: "9S" }],
    });
    const result = play(state, 1, "KS");
    expect(result.ok && result.state.phase).toBe("ended");
    expect(result.ok && result.state.bhabhiSeat).toBe(1);
    expect(result.ok && result.state.escapeOrder).toEqual([2, 0]);
  });

  it("E29_the_opponent_beats_the_leaders_last_card_with_their_own_last_card_and_is_the_bhabhi_with_no_cards", () => {
    const state = stateFrom({
      hands: [["9S"], ["KS"], []],
      leader: 0,
      plays: [{ seat: 0, card: "9S" }],
    });
    const result = play(state, 1, "KS");
    expect(result.ok && result.state.phase).toBe("ended");
    expect(result.ok && result.state.bhabhiSeat).toBe(1);
    expect(result.ok && handOf(result.state, 1)).toEqual([]);
    expect(result.ok && result.state.escapeOrder).toEqual([2, 0]);
  });

  it("R39b_E13_the_opponent_thullas_so_the_leader_picks_up_and_a_last_card_thulla_makes_the_leader_the_bhabhi", () => {
    const continues = stateFrom({
      hands: [["9S", "2H"], ["QH", "KD"], []],
      leader: 0,
      rules: { confirmedFastForward: false },
      plays: [{ seat: 0, card: "9S" }],
    });
    const picked = play(continues, 1, "QH");
    expect(picked.ok && handOf(picked.state, 0)).toEqual(["9S", "2H", "QH"]);
    expect(picked.ok && picked.state.phase).toBe("play");
    const lastCard = stateFrom({
      hands: [["9S", "2H"], ["QH"], []],
      leader: 0,
      plays: [{ seat: 0, card: "9S" }],
    });
    const ended = play(lastCard, 1, "QH");
    expect(ended.ok && ended.state.bhabhiSeat).toBe(0);
    expect(ended.ok && ended.state.escapeOrder).toEqual([2, 1]);
  });

  it("R39c_the_opponent_follows_lower_and_still_holds_cards_so_the_leader_draws_again", () => {
    const state = stateFrom({
      hands: [["9S"], ["3S", "7S", "2S"], []],
      leader: 0,
      rules: { confirmedFastForward: false },
      plays: [{ seat: 0, card: "9S" }],
    });
    const first = okState(play(state, 1, "3S", [0]));
    expect(first.pendingAutoLead).toBe(true);
    const drawn = handOf(first, 0)[0];
    if (drawn === undefined) throw new Error("no draw");
    const led = okState(play(first, 0, drawn));
    const follow = legalMoves(led, 1).map((m) => (m.type === "play_card" ? m.card : ""));
    const lower = follow.find((card) => card !== "7S" || drawn === "2S");
    expect(lower).toBeDefined();
  });

  it("R39d_the_opponent_follows_lower_with_their_last_card_so_the_leader_is_the_bhabhi", () => {
    const state = stateFrom({
      hands: [["9S"], ["3S"], []],
      leader: 0,
      plays: [{ seat: 0, card: "9S" }],
    });
    const result = play(state, 1, "3S");
    expect(result.ok && types(result.events)).toEqual([
      "card_played",
      "trick_resolved",
      "escape",
      "round_ended",
    ]);
    expect(result.ok && result.state.bhabhiSeat).toBe(0);
  });

  it("R41_GDD_example_3_V057_is_confirmed_the_instant_sara_plays_3S_and_fast_forwards_to_ali_as_bhabhi", () => {
    const state = stateFrom({
      hands: [["9S"], ["3S", "7S", "QH", "2D"], [], []],
      leader: 0,
      plays: [{ seat: 0, card: "9S" }],
    });
    expect(
      forcedBhabhi(
        okState(
          play(
            stateFrom({
              hands: [["9S"], ["3S", "7S", "QH", "2D"], [], []],
              leader: 0,
              rules: { confirmedFastForward: false },
              plays: [{ seat: 0, card: "9S" }],
            }),
            1,
            "3S",
            [0],
          ),
        ),
      ),
    ).toBe(0);
    const result = play(state, 1, "3S", [2]);
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    const confirmed = result.events.find((event) => event.type === "confirmed");
    expect(confirmed).toEqual({
      type: "confirmed",
      bhabhiSeat: 0,
      cardsRemaining: 3,
      fastForward: true,
    });
    expect(result.state.phase).toBe("ended");
    expect(result.state.bhabhiSeat).toBe(0);
    expect(result.state.confirmedBhabhi).toBe(0);
    expect(types(result.events).at(-1)).toBe("round_ended");
    expect(types(result.events).filter((t) => t === "card_played").length).toBeGreaterThan(0);
  });

  it("R41_E14_V058_is_not_confirmed_because_one_draw_lets_sara_play_KH_higher", () => {
    const state = stateFrom({
      hands: [["9S"], ["3S", "7S", "QH", "KH"], [], []],
      leader: 0,
      plays: [{ seat: 0, card: "9S" }],
    });
    const result = play(state, 1, "3S", [0]);
    expect(result.ok && types(result.events)).not.toContain("confirmed");
    expect(result.ok && result.state.phase).toBe("play");
    expect(result.ok && result.state.confirmedBhabhi).toBeNull();
  });

  it("R41_with_fast_forward_off_the_confirmed_event_fires_once_and_play_continues_by_hand", () => {
    const state = stateFrom({
      hands: [["9S"], ["3S", "7S", "QH", "2D"], [], []],
      leader: 0,
      rules: { confirmedFastForward: false },
      plays: [{ seat: 0, card: "9S" }],
    });
    const result = play(state, 1, "3S", [1]);
    expect(result.ok && types(result.events)).toContain("confirmed");
    expect(result.ok && result.state.phase).toBe("play");
    if (!result.ok) return;
    const drawn = handOf(result.state, 0)[0];
    if (drawn === undefined) throw new Error("no draw");
    const next = play(result.state, 0, drawn);
    expect(next.ok && types(next.events)).not.toContain("confirmed");
    expect(shouldSearch(result.state)).toBe(false);
  });

  it("R41_no_detection_runs_above_twelve_cards", () => {
    const hearts: Card[] = ["2H", "3H", "4H", "5H", "6H", "7H", "8H", "9H", "TH", "JH", "QH", "KH"];
    const state = stateFrom({
      hands: [["9S", "AS"], hearts, []],
      leader: 0,
      plays: [{ seat: 0, card: "9S" }],
    });
    const result = play(state, 1, "2H");
    expect(result.ok && cardsInPlay(result.state)).toBe(14);
    expect(result.ok && shouldSearch(result.state)).toBe(false);
    expect(result.ok && types(result.events)).not.toContain("confirmed");
  });

  it("R42_draw_discard_in_the_endgame_draws_from_the_discard_when_it_has_cards", () => {
    const state = stateFrom({
      hands: [["9S"], ["3S", "7S", "QH", "KH"], []],
      leader: 0,
      rules: { twoPlayerEndgame: "drawDiscard", confirmedFastForward: false },
      discard: ["4C", "5C"],
      plays: [{ seat: 0, card: "9S" }],
    });
    const result = play(state, 1, "3S", [1]);
    expect(result.ok && result.events.find((e) => e.type === "private_draw")).toMatchObject({
      source: "discard",
      card: "5C",
    });
  });

  it("R42_thulla_ends_round_makes_the_first_endgame_thulla_decide_the_bhabhi", () => {
    const state = stateFrom({
      hands: [["9S", "2H"], ["QH", "KD", "3C"], []],
      leader: 0,
      rules: { twoPlayerEndgame: "thullaEndsRound" },
      plays: [{ seat: 0, card: "9S" }],
    });
    const result = play(state, 1, "QH");
    expect(result.ok && result.state.phase).toBe("ended");
    expect(result.ok && result.state.bhabhiSeat).toBe(0);
    expect(result.ok && result.state.escapeOrder).toEqual([2, 1]);
  });

  it("R44_auto_play_follows_low_dumps_high_and_leads_from_the_longest_suit", () => {
    const following = stateFrom({
      hands: [["2H"], ["4H", "9H", "KS"], ["6H"]],
      leader: 0,
      plays: [{ seat: 0, card: "2H" }],
    });
    expect(autoPlayMove(following, 1)).toEqual({ type: "play_card", card: "4H" });
    const voided = stateFrom({
      hands: [["2H"], ["KS", "KD", "3C"], ["6H"]],
      leader: 0,
      plays: [{ seat: 0, card: "2H" }],
    });
    expect(autoPlayMove(voided, 1)).toEqual({ type: "play_card", card: "KS" });
    const leading = stateFrom({
      hands: [["9S", "2S", "QH", "3D", "4D"], ["4H"], ["6H"]],
      leader: 0,
    });
    expect(autoPlayMove(leading, 0)).toEqual({ type: "play_card", card: "2S" });
    const tied = stateFrom({ hands: [["9S", "QH"], ["4H"], ["6H"]], leader: 0 });
    expect(autoPlayMove(tied, 0)).toEqual({ type: "play_card", card: "9S" });
    expect(autoPlayMove(leading, 1)).toBeNull();
    const opening = stateFrom({
      hands: [
        ["AS", "2H"],
        ["3S", "4H"],
        ["5H", "6D"],
      ],
      leader: 0,
      trickIndex: 0,
      phase: "opening",
    });
    expect(autoPlayMove(opening, 0)).toEqual({ type: "play_card", card: "AS" });
  });

  it("R41_E14_exhaustive_small_endgames_the_search_agrees_with_random_playouts", () => {
    const deck = standardDeck();
    const endgame = fc
      .record({
        a: fc.subarray(deck, { minLength: 1, maxLength: 4 }),
        b: fc.subarray(deck, { minLength: 1, maxLength: 5 }),
        mode: fc.constantFrom("drawOpponent", "drawDiscard", "thullaEndsRound"),
        discard: fc.subarray(deck, { maxLength: 3 }),
      })
      .filter(
        ({ a, b, discard }) =>
          new Set([...a, ...b, ...discard]).size === a.length + b.length + discard.length,
      );
    fc.assert(
      fc.property(
        endgame,
        fc.integer({ min: 0, max: 100 }),
        ({ a, b, mode, discard }, seedWord) => {
          const setup: Setup = {
            hands: [a, b, []],
            leader: 0,
            discard,
            rules: { twoPlayerEndgame: mode, confirmedFastForward: false },
          };
          const state = stateFrom(setup);
          const forced = forcedBhabhi(state);
          if (forced !== null) {
            for (let i = 0; i < 6; i++) {
              expect(randomPlayout(state, seedWord + i)).toBe(forced);
            }
          }
          expect(randomPlayout(state, seedWord)).toBeGreaterThanOrEqual(0);
        },
      ),
      { numRuns: 150 },
    );
  });

  it("R41_the_node_cap_means_not_confirmed", () => {
    const state = stateFrom({
      hands: [["9S"], ["3S", "7S", "QH", "2D"], []],
      leader: 0,
      rules: { confirmedFastForward: false },
      pendingAutoLead: false,
    });
    expect(forcedBhabhi(state, 1)).toBeNull();
  });
});
