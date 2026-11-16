import { Xoshiro128StarStar, type Rng, type SeatIndex } from "@hulla/shared-ts";

import { createRungRound } from "../deal";
import { applyRungMove } from "../engine";
import { defaultRungRules, type RungRules } from "../rules";
import { rungStateFromSetup, type RungSetup } from "../setup";

import type { Card } from "../../card-kit";
import type { RungEvent } from "../events";
import type { RungMove } from "../moves";
import type { RungOutcome, RungState } from "../state";

/** Dealer 3 makes seat 0 the caller and first leader, which keeps positions easy to read. */
export function stateFrom(setup: Omit<RungSetup, "dealer"> & { dealer?: SeatIndex }): RungState {
  const state = rungStateFromSetup({ dealer: 3, ...setup });
  if (!state.ok) {
    throw new Error(`bad test setup: ${state.error.code} ${JSON.stringify(state.error.detail)}`);
  }
  return state.value;
}

export function seedOf(byte: number): Uint8Array {
  return new Uint8Array(32).fill(byte & 0xff);
}

export function rngOf(byte: number): Rng {
  return Xoshiro128StarStar.fromSeed(seedOf(byte));
}

export const NO_RNG: Rng = { nextUint32: () => 0, nextInt: () => 0 };

/** A dealt round in the call phase; throws on a bad input in tests. */
export function deal(
  dealerSeat = 3,
  seedByte = 7,
  rules: RungRules = defaultRungRules("private"),
): RungState {
  const seed = seedOf(seedByte);
  const round = createRungRound(
    { rules, seatCount: 4, dealerSeat, seed },
    Xoshiro128StarStar.fromSeed(seed),
  );
  if (!round.ok) {
    throw new Error(`expected a deal, got ${round.error.code}`);
  }
  return round.value;
}

export interface Step {
  readonly state: RungState;
  readonly events: readonly RungEvent[];
}

export function apply(state: RungState, seat: SeatIndex, move: RungMove, rng: Rng = NO_RNG): Step {
  const result = applyRungMove(state, seat, move, rng);
  if (!result.ok) {
    throw new Error(`expected ok, got ${result.error.code} ${JSON.stringify(result.error.detail)}`);
  }
  return { state: result.state, events: result.events };
}

export function errorOf(state: RungState, seat: SeatIndex, move: RungMove): string | null {
  const result = applyRungMove(state, seat, move, NO_RNG);
  return result.ok ? null : result.error.code;
}

/** Plays `"0:AH 1:2H 2:4H 3:6H"` in order and returns the last state with every event. */
export function playAll(state: RungState, plays: string): Step {
  let current = state;
  const events: RungEvent[] = [];
  for (const token of plays.split(/\s+/).filter((part) => part !== "")) {
    const [seat, card] = token.split(":");
    const step = apply(current, Number(seat), { type: "play_card", card: card as Card });
    current = step.state;
    events.push(...step.events);
  }
  return { state: current, events };
}

export function types(events: readonly RungEvent[]): string[] {
  return events.map((event) => event.type);
}

/** A finished deal with the given outcome, for scoring tests. Seat 0 called. */
export function endedWith(outcome: RungOutcome, rules: Partial<RungRules> = {}): RungState {
  const base = stateFrom({ rules, hands: [[], [], [], []], trump: "S" });
  return { ...base, phase: "ended", turnSeat: null, outcome };
}

export const SINGLE: Partial<RungRules> = { collect: "single" };
