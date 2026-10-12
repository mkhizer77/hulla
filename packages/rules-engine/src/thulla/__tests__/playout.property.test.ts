import { Xoshiro128StarStar, stateHash, type Rng, type SeatIndex } from "@hulla/shared-ts";
import * as fc from "fast-check";
import { describe, expect, it } from "vitest";

import { standardDeck } from "../../card-kit";
import { createThullaRound } from "../deal";
import { applyMove, legalMoves } from "../engine";
import { resolveRules, type ThullaRules } from "../rules";

import { cardsAtTable } from "./helpers";

import type { ThullaMove } from "../moves";
import type { ThullaState } from "../state";

const rulesArb: fc.Arbitrary<ThullaRules> = fc
  .record({
    openingSimultaneous: fc.boolean(),
    openingThullaApplies: fc.boolean(),
    thullaEndsTrick: fc.boolean(),
    emptyHandLeader: fc.constantFrom("drawDiscard", "drawNextPlayer", "instantExit"),
    handTaking: fc.constantFrom("off", "leaderOnly", "anyone"),
    twoPlayerEndgame: fc.constantFrom("drawOpponent", "drawDiscard", "thullaEndsRound"),
  })
  .map((overrides) => {
    const rules = resolveRules("private", "standard", overrides);
    if (!rules.ok) {
      throw new Error(rules.error.code);
    }
    return rules.value;
  });

function fresh(
  rules: ThullaRules,
  seatCount: number,
  seedByte: number,
): { state: ThullaState; rng: Rng } {
  const seed = new Uint8Array(32).fill(seedByte);
  const rng = Xoshiro128StarStar.fromSeed(seed);
  const round = createThullaRound(
    { rules, seatCount, dealerSeat: seedByte % seatCount, seed },
    rng,
  );
  if (!round.ok) {
    throw new Error(round.error.code);
  }
  return { state: round.value, rng };
}

const allMoves: ThullaMove[] = [
  { type: "take_hand" },
  ...standardDeck().map((card): ThullaMove => ({ type: "play_card", card })),
];

function sameMove(a: ThullaMove, b: ThullaMove): boolean {
  return (
    a.type === b.type && (a.type !== "play_card" || b.type !== "play_card" || a.card === b.card)
  );
}

/** Plays a whole round with random legal moves, checking the contract at every step. */
function playout(
  rules: ThullaRules,
  seatCount: number,
  seedByte: number,
  checkEveryMove: boolean,
): ThullaState {
  const { rng } = fresh(rules, seatCount, seedByte);
  let { state } = fresh(rules, seatCount, seedByte);
  const picker = Xoshiro128StarStar.fromState([seedByte + 1, 2, 3, 4]);
  const total = cardsAtTable(state);
  for (let step = 0; step < 3000; step++) {
    if (state.phase === "ended") {
      return state;
    }
    const legal: { seat: SeatIndex; move: ThullaMove }[] = [];
    for (let seat = 0; seat < seatCount; seat++) {
      for (const move of legalMoves(state, seat)) {
        legal.push({ seat, move });
      }
    }
    expect(legal.length).toBeGreaterThan(0);
    if (checkEveryMove) {
      for (let seat = 0; seat < seatCount; seat++) {
        const listed = legalMoves(state, seat);
        for (const move of allMoves) {
          const result = applyMove(state, seat, move, rng);
          expect(result.ok).toBe(listed.some((m) => sameMove(m, move)));
        }
      }
    }
    const choice = legal[picker.nextInt(legal.length)];
    if (choice === undefined) {
      throw new Error("no legal move");
    }
    const result = applyMove(state, choice.seat, choice.move, rng);
    expect(result.ok).toBe(true);
    if (!result.ok) {
      throw new Error(result.error.code);
    }
    expect(result.events.length).toBeGreaterThan(0);
    expect(cardsAtTable(result.state)).toBe(total);
    expect(stateHash(result.state)).toMatch(/^[0-9a-f]{64}$/);
    state = result.state;
  }
  throw new Error("round did not end within 3000 moves");
}

describe("random playouts", () => {
  it("R40_every_round_ends_with_one_bhabhi_and_a_full_escape_order_under_any_toggles", () => {
    fc.assert(
      fc.property(
        rulesArb,
        fc.integer({ min: 3, max: 6 }),
        fc.integer({ min: 0, max: 255 }),
        (rules, seats, seed) => {
          const ended = playout(rules, seats, seed, false);
          expect(ended.phase).toBe("ended");
          expect(ended.bhabhiSeat).not.toBeNull();
          expect(ended.escapeOrder).toHaveLength(seats - 1);
          expect(new Set(ended.escapeOrder).size).toBe(seats - 1);
          expect(ended.escapeOrder).not.toContain(ended.bhabhiSeat);
          expect(ended.seats.filter((s) => !s.escaped)).toHaveLength(1);
        },
      ),
      { numRuns: 120 },
    );
  });

  it("legalMoves_and_applyMove_agree_for_every_seat_and_every_card_at_every_step", () => {
    fc.assert(
      fc.property(
        rulesArb,
        fc.integer({ min: 3, max: 6 }),
        fc.integer({ min: 0, max: 255 }),
        (rules, seats, seed) => {
          playout(rules, seats, seed, true);
        },
      ),
      { numRuns: 25 },
    );
  });

  it("applyMove_never_throws_on_arbitrary_seats_and_moves", () => {
    const rules = resolveRules("private");
    if (!rules.ok) throw new Error(rules.error.code);
    const { state, rng } = fresh(rules.value, 4, 9);
    fc.assert(
      fc.property(fc.integer({ min: -2, max: 8 }), fc.constantFrom(...allMoves), (seat, move) => {
        expect(() => applyMove(state, seat, move, rng)).not.toThrow();
      }),
    );
  });

  it("the_same_seed_and_moves_give_the_same_hash", () => {
    const rules = resolveRules("private", "standard");
    if (!rules.ok) throw new Error(rules.error.code);
    const first = playout(rules.value, 4, 42, false);
    const second = playout(rules.value, 4, 42, false);
    expect(stateHash(first)).toBe(stateHash(second));
  });
});
