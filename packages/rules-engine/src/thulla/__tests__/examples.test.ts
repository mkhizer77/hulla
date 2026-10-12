import { describe, expect, it } from "vitest";

import { applyMove, currentPhase, isRoundOver } from "../engine";

import { fixedRng, handOf, stateFrom } from "./helpers";

describe("GDD worked examples and Appendix C vectors", () => {
  it("V-021 R20 R21: Ali leads 6♥, Sara K♥, Bilal Q♠ thulla; Noor does not play; Sara picks up", () => {
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
    expect(result.state.trick.plays).toEqual([]);
    expect(handOf(result.state, 3)).toContain("9H");
    expect(result.events[1]).toMatchObject({ picker: 1, nextLeader: 1 });
    for (const card of ["6H", "KH", "QS"] as const) {
      expect(handOf(result.state, 1)).toContain(card);
    }
    expect(result.state.leaderSeat).toBe(1);
  });

  it("V-034 R26: Noor wins with her last card and draws from the discard, not from this trick", () => {
    const state = stateFrom({
      hands: [["9C", "2D"], ["7C", "5D"], ["4C", "TD"], ["JC"]],
      leader: 2,
      discard: ["8D", "QH"],
      plays: [
        { seat: 2, card: "4C" },
        { seat: 3, card: "JC" },
        { seat: 0, card: "9C" },
      ],
    });
    const result = applyMove(state, 1, { type: "play_card", card: "7C" }, fixedRng([0]));
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.state.leaderSeat).toBe(3);
    expect(handOf(result.state, 3)).toHaveLength(1);
    expect(result.events[2]).toMatchObject({ type: "private_draw", seat: 3, source: "discard" });
    expect(["4C", "JC", "9C", "7C"]).not.toContain(handOf(result.state, 3)[0]);
    expect(result.state.pendingAutoLead).toBe(true);
  });

  it("GDD 6.16 example 2 end to end: the draw, the second trick and the escape", () => {
    const state = stateFrom({
      hands: [["9C", "2D", "3H"], ["7C", "5D", "4H"], ["4C", "TD", "6H"], ["JC"]],
      leader: 2,
      discard: ["8D", "2S"],
      plays: [
        { seat: 2, card: "4C" },
        { seat: 3, card: "JC" },
        { seat: 0, card: "9C" },
      ],
    });
    const drawn = applyMove(state, 1, { type: "play_card", card: "7C" }, fixedRng([0]));
    expect(drawn.ok && handOf(drawn.state, 3)).toEqual(["8D"]);
    if (!drawn.ok) return;
    let next = applyMove(drawn.state, 3, { type: "play_card", card: "8D" }, fixedRng());
    next = next.ok ? applyMove(next.state, 0, { type: "play_card", card: "2D" }, fixedRng()) : next;
    next = next.ok ? applyMove(next.state, 1, { type: "play_card", card: "5D" }, fixedRng()) : next;
    const result = next.ok
      ? applyMove(next.state, 2, { type: "play_card", card: "TD" }, fixedRng())
      : next;
    expect(result.ok && result.state.leaderSeat).toBe(2);
    expect(result.ok && result.state.escapeOrder).toEqual([3]);
    expect(result.ok && result.state.seats[3]?.escapeRank).toBe(1);
  });

  it("V-061 R29: a last card played as a thulla escapes while the picker takes the pile", () => {
    const state = stateFrom({
      hands: [["5C", "2H"], ["TC", "3H"], ["AD"]],
      leader: 0,
      rules: { confirmedFastForward: false },
      plays: [
        { seat: 0, card: "5C" },
        { seat: 1, card: "TC" },
      ],
    });
    const result = applyMove(state, 2, { type: "play_card", card: "AD" }, fixedRng());
    expect(result.ok && result.events[1]).toMatchObject({ picker: 1 });
    expect(result.ok && result.state.escapeOrder).toEqual([2]);
    expect(result.ok && result.state.seats.filter((s) => !s.escaped).length).toBe(2);
  });

  it("V-070 R33: leader-only hand take moves three cards and escapes the target", () => {
    const state = stateFrom({
      hands: [["5H"], ["7H", "8H"], ["2C", "4D", "9S"], ["TH"]],
      leader: 1,
      trickIndex: 3,
      rules: { handTaking: "leaderOnly" },
    });
    const result = applyMove(state, 1, { type: "take_hand" }, fixedRng());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    for (const card of ["2C", "4D", "9S"] as const) {
      expect(handOf(result.state, 1)).toContain(card);
    }
    expect(result.state.escapeOrder).toEqual([2]);
    expect(result.events[0]).toMatchObject({ type: "hand_taken" });
    expect(result.state.leaderSeat).toBe(1);
  });

  it("currentPhase and isRoundOver report the phase the table core switches on", () => {
    const state = stateFrom({
      hands: [["9C"], ["7C"], ["4C"]],
      leader: 2,
      plays: [
        { seat: 2, card: "4C" },
        { seat: 0, card: "9C" },
      ],
    });
    expect(currentPhase(state)).toBe("play");
    expect(isRoundOver(state)).toBe(false);
    const result = applyMove(state, 1, { type: "play_card", card: "7C" }, fixedRng());
    expect(result.ok && currentPhase(result.state)).toBe("ended");
    expect(result.ok && isRoundOver(result.state)).toBe(true);
    const late = result.ok
      ? applyMove(result.state, 0, { type: "play_card", card: "9C" }, fixedRng())
      : result;
    expect(!late.ok && late.error.code).toBe("rules.round_over");
  });

  it("V-071 R36: a hand take with two active players is rejected as HAND_TAKE_DISABLED_ENDGAME", () => {
    const state = stateFrom({
      hands: [["5H", "6H"], ["7H"]],
      leader: 0,
      rules: { handTaking: "leaderOnly" },
    });
    const result = applyMove(state, 0, { type: "take_hand" }, fixedRng());
    expect(!result.ok && result.error.code).toBe("rules.hand_take_disabled_endgame");
  });
});
