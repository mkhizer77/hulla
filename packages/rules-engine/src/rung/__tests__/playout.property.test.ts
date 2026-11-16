import { Xoshiro128StarStar, stateHash } from "@hulla/shared-ts";
import * as fc from "fast-check";
import { describe, expect, it } from "vitest";

import { applyRungMove, legalRungMoves } from "../engine";
import { RUNG_PHASES, rungModule } from "../module";
import { resolveRungRules, type RungPreset, type RungRules } from "../rules";

import { deal, seedOf } from "./helpers";

import type { Card } from "../../card-kit";
import type { RungState } from "../state";

const PRESETS: readonly RungPreset[] = ["rung", "courtPiece", "hokm"];

function rulesFor(preset: RungPreset, extras: Partial<RungRules>): RungRules {
  const rules = resolveRungRules("private", preset, preset === "rung" ? extras : {});
  if (!rules.ok) {
    throw new Error(rules.error.code);
  }
  return rules.value;
}

function cardsAtTable(state: RungState, tricksDone: number): number {
  const held = state.hands.reduce((sum, hand) => sum + hand.length, 0);
  return held + state.stock.length + state.trick.plays.length + tricksDone * 4;
}

/** Plays a whole deal; `choose` picks among the legal moves. Checks the invariants at every step. */
function playOut(
  start: RungState,
  seedByte: number,
  choose: (state: RungState, seat: number, count: number) => number,
): RungState {
  const rng = Xoshiro128StarStar.fromSeed(seedOf(seedByte + 1));
  let state = start;
  let tricksDone = 0;
  for (let step = 0; step < 80 && state.phase !== "ended"; step++) {
    const seat = state.turnSeat;
    if (seat === null) {
      throw new Error("an unfinished deal always has a seat to act");
    }
    const legal = legalRungMoves(state, seat);
    expect(legal.length).toBeGreaterThan(0);
    expect(RUNG_PHASES.map((phase) => phase.id)).toContain(rungModule.currentPhase(state));
    if (state.phase === "play") {
      // A card is accepted exactly when it is offered.
      const offered = legal.map((move) => (move.type === "play_card" ? move.card : null));
      for (const card of state.hands[seat] ?? ([] as Card[])) {
        const accepted = applyRungMove(state, seat, { type: "play_card", card }, rng).ok;
        expect(accepted).toBe(offered.includes(card));
      }
    }
    const move = legal[choose(state, seat, legal.length)] ?? legal[0];
    if (move === undefined) {
      throw new Error("no legal move");
    }
    const result = applyRungMove(state, seat, move, rng);
    if (!result.ok) {
      throw new Error(`a legal move was refused: ${result.error.code}`);
    }
    if (result.events.some((event) => event.type === "trick_won")) {
      tricksDone += 1;
    }
    state = result.state;
    expect(cardsAtTable(state, tricksDone)).toBe(52);
    expect(state.counts[0] + state.counts[1] + state.pile).toBe(tricksDone);
    expect(stateHash(state)).toMatch(/^[0-9a-f]{64}$/);
  }
  return state;
}

describe("whole deals", () => {
  it("every deal of legal moves ends with one outcome that matches the counts", () => {
    fc.assert(
      fc.property(
        fc.integer({ min: 0, max: 255 }),
        fc.integer({ min: 0, max: 3 }),
        fc.constantFrom(...PRESETS),
        fc.record({ firstTwo: fc.boolean(), aceRule: fc.boolean(), lastTwo: fc.boolean() }),
        fc.boolean(),
        fc.array(fc.nat(), { minLength: 1, maxLength: 60 }),
        (seedByte, dealerSeat, preset, extras, chase, picks) => {
          const rules = { ...rulesFor(preset, extras), chase };
          let cursor = 0;
          const end = playOut(deal(dealerSeat, seedByte, rules), seedByte, (_s, _seat, count) => {
            const pick = picks[cursor % picks.length] ?? 0;
            cursor += 1;
            return pick % count;
          });
          expect(end.phase).toBe("ended");
          expect(rungModule.isRoundOver(end)).toBe(true);
          const outcome = end.outcome;
          if (outcome === null) {
            throw new Error("an ended deal has an outcome");
          }
          expect(end.counts[outcome.team]).toBeGreaterThanOrEqual(7);
          if (outcome.kind === "court52") {
            expect(end.counts[outcome.team]).toBe(13);
            expect(rules.collect).toBe("single");
          }
          if (outcome.kind === "court" && rules.collect === "double") {
            expect(end.counts[outcome.team]).toBe(13);
          }
          if (!chase && rules.collect === "double") {
            expect(outcome.kind).toBe("win");
          }
          const result = rungModule.results(end);
          expect(result.points.reduce((sum, value) => sum + value, 0)).toBeGreaterThan(0);
          expect(result.loserSeat).toBeNull();
        },
      ),
      { numRuns: 150 },
    );
  });

  it("RG42_a_table_of_absent_seats_finishes_every_deal_and_the_same_seed_gives_the_same_end", () => {
    for (const preset of PRESETS) {
      for (let seedByte = 0; seedByte < 12; seedByte++) {
        const run = (): RungState =>
          playOut(deal(seedByte % 4, seedByte, rulesFor(preset, {})), seedByte, (state, seat) => {
            const auto = rungModule.onDeadline(state, seat);
            const legal = legalRungMoves(state, seat);
            const index = legal.findIndex((move) => JSON.stringify(move) === JSON.stringify(auto));
            expect(index).toBeGreaterThanOrEqual(0);
            return index;
          });
        const end = run();
        expect(end.phase).toBe("ended");
        expect(stateHash(run())).toBe(stateHash(end));
      }
    }
  });
});
