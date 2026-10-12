import {
  THULLA_RULES_VERSION,
  applyMove,
  createThullaRound,
  forcedBhabhi,
  onDeadline,
  redact,
  resolveRules,
  results,
  sortCards,
  stateFromSetup,
  type Card,
  type RulesMode,
  type ThullaEvent,
  type ThullaMove,
  type ThullaPreset,
  type ThullaSetup,
  type ThullaState,
} from "@hulla/rules-engine";
import {
  Xoshiro128StarStar,
  hexToBytes,
  stateHash,
  type Rng,
  type SeatIndex,
} from "@hulla/shared-ts";

export interface VectorDeal {
  readonly seedHex: string;
  readonly seatCount: number;
  readonly dealerSeat: SeatIndex;
  readonly mode?: RulesMode;
  readonly preset?: ThullaPreset;
  readonly overrides?: Record<string, unknown>;
}

export interface VectorMove {
  readonly seat: SeatIndex;
  readonly move: ThullaMove;
  /** The index the random draw returns for this move; 0 when absent. Ignored after a deal. */
  readonly draw?: number;
  readonly expect?: "ok" | { readonly error: string };
}

export interface VectorView {
  readonly viewer: "spectator" | SeatIndex;
  readonly ownHand?: readonly Card[] | null;
  readonly handCounts?: readonly number[];
  readonly discardCount?: number;
  readonly seedHidden?: boolean;
  readonly canTakeHand?: boolean | null;
}

export interface VectorAssert {
  readonly phase?: ThullaState["phase"];
  readonly leaderSeat?: SeatIndex;
  readonly turnSeat?: SeatIndex | null;
  readonly trickIndex?: number;
  readonly hands?: Readonly<Record<string, readonly Card[]>>;
  readonly handContains?: Readonly<Record<string, readonly Card[]>>;
  readonly handCounts?: readonly number[];
  readonly escapeOrder?: readonly SeatIndex[];
  readonly bhabhiSeat?: SeatIndex | null;
  readonly confirmedBhabhi?: SeatIndex | null;
  readonly discard?: readonly Card[];
  readonly discardCount?: number;
  readonly pendingAutoLead?: boolean;
  readonly lastEvents?: readonly string[];
  readonly eventsInclude?: readonly string[];
  readonly stats?: Readonly<Record<string, Readonly<Record<string, number>>>>;
  readonly forcedBhabhi?: SeatIndex | null;
  readonly autoPlay?: Readonly<Record<string, Card>>;
  readonly results?: {
    readonly finishOrder?: readonly SeatIndex[];
    readonly loserSeat?: SeatIndex;
    readonly points?: readonly number[];
    readonly bhabhiCards?: number;
  };
  readonly views?: readonly VectorView[];
  readonly commitment?: string;
  readonly leaderHoldsAceOfSpades?: boolean;
  readonly stateHash?: string;
}

/** The shape of every file in `thulla/`; `schema/vector.schema.json` is the same contract as JSON Schema. */
export interface Vector {
  readonly id: string;
  readonly version: string;
  readonly title?: string;
  readonly rules: readonly string[];
  readonly deal?: VectorDeal;
  readonly setup?: ThullaSetup;
  readonly moves: readonly VectorMove[];
  readonly assert: VectorAssert;
}

export interface RunOutcome {
  readonly failures: readonly string[];
  readonly state: ThullaState | null;
  readonly lastEvents: readonly ThullaEvent[];
  readonly allEvents: readonly ThullaEvent[];
}

function fixedDraw(index: number): Rng {
  return { nextUint32: () => 0, nextInt: (n) => index % n };
}

function same(a: unknown, b: unknown): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

/** Applies a vector to the TypeScript engine and lists every assertion that does not hold. */
export function runVector(vector: Vector): RunOutcome {
  const failures: string[] = [];
  const fail = (message: string): void => {
    failures.push(message);
  };
  if (vector.version !== THULLA_RULES_VERSION) {
    fail(
      `version ${vector.version} differs from the engine's ${THULLA_RULES_VERSION}; regenerate the vectors`,
    );
  }
  const start = startState(vector);
  if (!start.ok) {
    return { failures: [...failures, start.error], state: null, lastEvents: [], allEvents: [] };
  }
  let state = start.state;
  let lastEvents: readonly ThullaEvent[] = [];
  const allEvents: ThullaEvent[] = [];
  vector.moves.forEach((step, index) => {
    const rng = start.rng ?? fixedDraw(step.draw ?? 0);
    const result = applyMove(state, step.seat, step.move, rng);
    const expected = step.expect ?? "ok";
    if (expected === "ok") {
      if (!result.ok) {
        fail(`move ${index} expected ok, got ${result.error.code}`);
        return;
      }
      state = result.state;
      lastEvents = result.events;
      allEvents.push(...result.events);
    } else if (result.ok) {
      fail(`move ${index} expected ${expected.error}, but it was accepted`);
    } else if (result.error.code !== expected.error) {
      fail(`move ${index} expected ${expected.error}, got ${result.error.code}`);
    }
  });
  checkAssertions(vector.assert, state, lastEvents, allEvents, fail);
  return { failures, state, lastEvents, allEvents };
}

function startState(
  vector: Vector,
): { ok: true; state: ThullaState; rng: Rng | null } | { ok: false; error: string } {
  if (vector.deal !== undefined) {
    const rules = resolveRules(
      vector.deal.mode ?? "private",
      vector.deal.preset ?? "standard",
      vector.deal.overrides ?? {},
    );
    if (!rules.ok) {
      return { ok: false, error: `deal rules: ${rules.error.code}` };
    }
    const seed = hexToBytes(vector.deal.seedHex);
    const rng = Xoshiro128StarStar.fromSeed(seed);
    const round = createThullaRound(
      {
        rules: rules.value,
        seatCount: vector.deal.seatCount,
        dealerSeat: vector.deal.dealerSeat,
        seed,
      },
      rng,
    );
    return round.ok
      ? { ok: true, state: round.value, rng }
      : { ok: false, error: `deal: ${round.error.code}` };
  }
  if (vector.setup !== undefined) {
    const state = stateFromSetup(vector.setup);
    return state.ok
      ? { ok: true, state: state.value, rng: null }
      : { ok: false, error: `setup: ${state.error.code}` };
  }
  return { ok: false, error: "a vector needs a deal or a setup" };
}

function checkAssertions(
  assert: VectorAssert,
  state: ThullaState,
  lastEvents: readonly ThullaEvent[],
  allEvents: readonly ThullaEvent[],
  fail: (message: string) => void,
): void {
  const expectSame = (name: string, actual: unknown, expected: unknown): void => {
    if (!same(actual, expected)) {
      fail(`${name}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
    }
  };
  if (assert.phase !== undefined) expectSame("phase", state.phase, assert.phase);
  if (assert.leaderSeat !== undefined)
    expectSame("leaderSeat", state.leaderSeat, assert.leaderSeat);
  if (assert.turnSeat !== undefined) expectSame("turnSeat", state.turnSeat, assert.turnSeat);
  if (assert.trickIndex !== undefined)
    expectSame("trickIndex", state.trickIndex, assert.trickIndex);
  if (assert.hands !== undefined) {
    for (const [seat, hand] of Object.entries(assert.hands)) {
      expectSame(`hands[${seat}]`, state.seats[Number(seat)]?.hand, sortCards(hand));
    }
  }
  if (assert.handContains !== undefined) {
    for (const [seat, cards] of Object.entries(assert.handContains)) {
      const hand = state.seats[Number(seat)]?.hand ?? [];
      for (const card of cards) {
        if (!hand.includes(card)) {
          fail(`handContains[${seat}]: ${card} missing from ${JSON.stringify(hand)}`);
        }
      }
    }
  }
  if (assert.handCounts !== undefined) {
    expectSame(
      "handCounts",
      state.seats.map((seat) => seat.hand.length),
      assert.handCounts,
    );
  }
  if (assert.escapeOrder !== undefined)
    expectSame("escapeOrder", state.escapeOrder, assert.escapeOrder);
  if (assert.bhabhiSeat !== undefined)
    expectSame("bhabhiSeat", state.bhabhiSeat, assert.bhabhiSeat);
  if (assert.confirmedBhabhi !== undefined) {
    expectSame("confirmedBhabhi", state.confirmedBhabhi, assert.confirmedBhabhi);
  }
  if (assert.discard !== undefined) expectSame("discard", state.discard, assert.discard);
  if (assert.discardCount !== undefined) {
    expectSame("discardCount", state.discard.length, assert.discardCount);
  }
  if (assert.pendingAutoLead !== undefined) {
    expectSame("pendingAutoLead", state.pendingAutoLead, assert.pendingAutoLead);
  }
  if (assert.lastEvents !== undefined) {
    expectSame(
      "lastEvents",
      lastEvents.map((event) => event.type),
      assert.lastEvents,
    );
  }
  if (assert.eventsInclude !== undefined) {
    const types = new Set<string>(allEvents.map((event) => event.type));
    for (const type of assert.eventsInclude) {
      if (!types.has(type)) fail(`eventsInclude: ${type} never emitted`);
    }
  }
  if (assert.stats !== undefined) {
    for (const [seat, expected] of Object.entries(assert.stats)) {
      const actual: Record<string, number> = { ...state.stats[Number(seat)] };
      for (const [key, value] of Object.entries(expected)) {
        expectSame(`stats[${seat}].${key}`, actual[key], value);
      }
    }
  }
  if (assert.forcedBhabhi !== undefined) {
    expectSame("forcedBhabhi", forcedBhabhi(state), assert.forcedBhabhi);
  }
  if (assert.autoPlay !== undefined) {
    for (const [seat, card] of Object.entries(assert.autoPlay)) {
      expectSame(`autoPlay[${seat}]`, onDeadline(state, Number(seat)), { type: "play_card", card });
    }
  }
  if (assert.results !== undefined) {
    const actual = results(state);
    if (actual === null) {
      fail("results: the round has not ended");
    } else {
      const { finishOrder, loserSeat, points, bhabhiCards } = assert.results;
      if (finishOrder !== undefined)
        expectSame("results.finishOrder", actual.finishOrder, finishOrder);
      if (loserSeat !== undefined) expectSame("results.loserSeat", actual.loserSeat, loserSeat);
      if (points !== undefined) expectSame("results.points", actual.points, points);
      if (bhabhiCards !== undefined)
        expectSame("results.bhabhiCards", actual.bhabhiCards, bhabhiCards);
    }
  }
  if (assert.views !== undefined) {
    for (const expected of assert.views) {
      const viewer =
        expected.viewer === "spectator"
          ? { kind: "spectator" as const }
          : { kind: "seat" as const, seat: expected.viewer };
      const view = redact(state, viewer);
      const label = `views[${String(expected.viewer)}]`;
      if (expected.ownHand !== undefined) {
        const own = expected.viewer === "spectator" ? null : view.seats[expected.viewer]?.hand;
        expectSame(
          `${label}.ownHand`,
          own,
          expected.ownHand === null ? null : sortCards(expected.ownHand),
        );
      }
      if (expected.handCounts !== undefined) {
        expectSame(
          `${label}.handCounts`,
          view.seats.map((seat) => seat.handCount),
          expected.handCounts,
        );
      }
      if (expected.discardCount !== undefined) {
        expectSame(`${label}.discardCount`, view.discardCount, expected.discardCount);
      }
      if (expected.seedHidden !== undefined) {
        expectSame(`${label}.seedHidden`, view.seedHex === null, expected.seedHidden);
      }
      if (expected.canTakeHand !== undefined) {
        expectSame(`${label}.canTakeHand`, view.canTakeHand, expected.canTakeHand);
      }
      view.seats.forEach((seat, index) => {
        if (seat.hand !== null && (expected.viewer === "spectator" || index !== expected.viewer)) {
          fail(`${label}: hand of seat ${index} is visible`);
        }
      });
    }
  }
  if (assert.commitment !== undefined)
    expectSame("commitment", state.commitment, assert.commitment);
  if (assert.leaderHoldsAceOfSpades !== undefined) {
    expectSame(
      "leaderHoldsAceOfSpades",
      state.seats[state.leaderSeat]?.hand.includes("AS") ?? false,
      assert.leaderHoldsAceOfSpades,
    );
  }
  if (assert.stateHash !== undefined) expectSame("stateHash", stateHash(state), assert.stateHash);
}

export { stateHash };
