import { describe, expect, it } from "vitest";

import { redactRung } from "../view";

import { apply, deal, playAll, stateFrom } from "./helpers";

import type { Card } from "../../card-kit";
import type { RungState } from "../state";

function leaked(state: RungState, seat: number | null): Card[] {
  const view =
    seat === null
      ? redactRung(state, { kind: "spectator" })
      : redactRung(state, { kind: "seat", seat });
  const json = JSON.stringify(view);
  const hidden = [
    ...state.stock,
    ...state.hands.flatMap((hand, index) => (index === seat ? [] : hand)),
  ];
  return hidden.filter((card) => json.includes(`"${card}"`));
}

describe("what a viewer may know", () => {
  it("RGE12_in_the_call_phase_a_seat_sees_its_five_cards_and_no_card_of_the_stock", () => {
    const state = deal();
    for (const seat of [0, 1, 2, 3]) {
      const view = redactRung(state, { kind: "seat", seat });
      expect(view.hand).toEqual(state.hands[seat]);
      expect(view.hand).toHaveLength(5);
      expect(view.stockCount).toBe(32);
      expect(view.handCounts).toEqual([5, 5, 5, 5]);
      expect(leaked(state, seat)).toEqual([]);
      expect(view.seedHex).toBeNull();
      expect(view.deckOrder).toBeNull();
    }
    expect(redactRung(state, { kind: "seat", seat: 1 }).canRedeal).toBe(false);
  });

  it("RG5_no_view_shows_another_hand_during_play", () => {
    const state = apply(deal(), 0, { type: "call_trump", suit: "H" }).state;
    for (const seat of [0, 1, 2, 3]) {
      expect(leaked(state, seat)).toEqual([]);
    }
  });

  it("RGE13_a_spectator_sees_no_hand_at_all", () => {
    const state = apply(deal(), 0, { type: "call_trump", suit: "H" }).state;
    const view = redactRung(state, { kind: "spectator" });
    expect(view.hand).toBeNull();
    expect(view.canRedeal).toBeNull();
    expect(view.trump).toBe("H");
    expect(leaked(state, null)).toEqual([]);
  });

  it("is total: a seat outside the table gets no hand, and the seed shows once the deal ends", () => {
    const state = deal();
    expect(redactRung(state, { kind: "seat", seat: 9 }).hand).toBeNull();
    const ended = playAll(
      {
        ...stateFrom({
          rules: { collect: "single" },
          hands: [["AH"], ["2H"], ["3H"], ["4H"]],
          trump: "S",
          trickIndex: 9,
          counts: [6, 3],
        }),
        seedHex: "ab",
        deckOrder: ["AH"],
      },
      "0:AH 1:2H 2:3H 3:4H",
    ).state;
    const view = redactRung(ended, { kind: "spectator" });
    expect(view.seedHex).toBe("ab");
    expect(view.deckOrder).toEqual(["AH"]);
    expect(view.outcome).toEqual({ team: 0, kind: "win" });
  });
});
