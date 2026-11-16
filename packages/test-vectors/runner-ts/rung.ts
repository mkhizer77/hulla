import {
  RUNG_RULES_VERSION,
  applyRungMove,
  createRungRound,
  createRungSeries,
  legalRungMoves,
  nextRungDealer,
  redactRung,
  resolveRungRules,
  rungAutoPlayMove,
  rungResults,
  rungSeriesUpdate,
  rungStateFromSetup,
  standardDeck,
  type Card,
  type RungEvent,
  type RungMode,
  type RungMove,
  type RungOutcome,
  type RungPreset,
  type RungSeriesState,
  type RungSetup,
  type RungState,
} from "@hulla/rules-engine";
import {
  Xoshiro128StarStar,
  hexToBytes,
  stateHash,
  type Rng,
  type SeatIndex,
} from "@hulla/shared-ts";

export interface RungVectorRules {
  readonly mode?: RungMode;
  readonly preset?: RungPreset;
  readonly overrides?: Record<string, unknown>;
}

export interface RungVectorDeal extends RungVectorRules {
  readonly seedHex: string;
  readonly dealerSeat: SeatIndex;
}

/** A bazi as a list of deal outcomes; each deal is dealt by the seat the score names (RG35 to RG37). */
export interface RungVectorBazi extends RungVectorRules {
  readonly firstDealer: SeatIndex;
  readonly deals: readonly RungOutcome[];
}

export interface RungVectorMove {
  readonly seat: SeatIndex;
  readonly move: RungMove;
  readonly expect?: "ok" | { readonly error: string };
}

export interface RungVectorView {
  readonly viewer: "spectator" | SeatIndex;
  readonly hand?: readonly Card[] | null;
  readonly handCounts?: readonly number[];
  readonly stockCount?: number;
  readonly seedHidden?: boolean;
  readonly canRedeal?: boolean | null;
  /** No card of the stock or of another hand appears anywhere in the view. */
  readonly hidesOthers?: boolean;
}

export interface RungVectorAssert {
  readonly phase?: RungState["phase"];
  readonly dealerSeat?: SeatIndex;
  readonly callerSeat?: SeatIndex;
  readonly turnSeat?: SeatIndex | null;
  readonly trickIndex?: number;
  readonly trump?: RungState["trump"];
  readonly redeals?: number;
  readonly counts?: readonly [number, number];
  readonly pile?: number;
  readonly lastWinner?: SeatIndex | null;
  readonly run?: number;
  readonly chasing?: RungState["chasing"];
  readonly outcome?: RungOutcome | null;
  readonly handCounts?: readonly number[];
  readonly stockCount?: number;
  readonly hands?: Readonly<Record<string, readonly Card[]>>;
  /** The hands and the stock together are one full deck (RG2). */
  readonly fullDeck?: boolean;
  /** Each seat's first five cards, in deck order from the caller (RG5). */
  readonly firstFiveFromDeck?: boolean;
  readonly lastEvents?: readonly string[];
  readonly eventsInclude?: readonly string[];
  readonly legal?: Readonly<Record<string, readonly RungMove[]>>;
  readonly autoPlay?: Readonly<Record<string, RungMove | null>>;
  readonly stats?: Readonly<Record<string, Readonly<Record<string, number>>>>;
  readonly results?: Readonly<Record<string, unknown>>;
  readonly nextDealer?: SeatIndex;
  readonly series?: Readonly<Record<string, unknown>>;
  readonly views?: readonly RungVectorView[];
  readonly commitment?: string;
  readonly stateHash?: string;
  readonly seriesHash?: string;
}

/** The shape of every file in `rung/`; `schema/rung-vector.schema.json` is the same contract. */
export interface RungVector {
  readonly id: string;
  readonly version: string;
  readonly title?: string;
  readonly rules: readonly string[];
  readonly deal?: RungVectorDeal;
  readonly setup?: RungSetup;
  readonly bazi?: RungVectorBazi;
  readonly moves: readonly RungVectorMove[];
  readonly assert: RungVectorAssert;
}

export interface RungRunOutcome {
  readonly failures: readonly string[];
  readonly state: RungState | null;
  readonly series: RungSeriesState | null;
}

const NO_RNG: Rng = { nextUint32: () => 0, nextInt: () => 0 };

function same(a: unknown, b: unknown): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

const STATE_FIELDS = [
  "phase",
  "dealerSeat",
  "callerSeat",
  "turnSeat",
  "trickIndex",
  "trump",
  "redeals",
  "counts",
  "pile",
  "lastWinner",
  "run",
  "chasing",
  "outcome",
  "commitment",
] as const;

function playBazi(bazi: RungVectorBazi, fail: (message: string) => void): RungSeriesState | null {
  const rules = resolveRungRules(bazi.mode ?? "private", bazi.preset ?? "rung", bazi.overrides);
  if (!rules.ok) {
    fail(`rules: ${rules.error.code}`);
    return null;
  }
  let series = createRungSeries(bazi.firstDealer);
  for (const outcome of bazi.deals) {
    const dealt = rungStateFromSetup({
      rules: rules.value,
      hands: [[], [], [], []],
      trump: "S",
      dealer: series.nextDealerSeat,
    });
    if (!dealt.ok) {
      fail(`bazi deal: ${dealt.error.code}`);
      return null;
    }
    const result = rungResults({ ...dealt.value, phase: "ended", turnSeat: null, outcome });
    if (result === null) {
      fail("bazi deal has no result");
      return null;
    }
    series = rungSeriesUpdate(series, result) as RungSeriesState;
  }
  return series;
}

function partial(
  label: string,
  expected: Readonly<Record<string, unknown>>,
  actual: object | null,
  fail: (message: string) => void,
): void {
  for (const [key, value] of Object.entries(expected)) {
    const got = actual === null ? undefined : (actual as Record<string, unknown>)[key];
    if (!same(got, value)) {
      fail(`${label}.${key}: expected ${JSON.stringify(value)}, got ${JSON.stringify(got)}`);
    }
  }
}

/** Applies a vector to the TypeScript engine and lists every assertion that does not hold. */
export function runRungVector(vector: RungVector): RungRunOutcome {
  const failures: string[] = [];
  const fail = (message: string): void => {
    failures.push(message);
  };
  if (vector.version !== RUNG_RULES_VERSION) {
    fail(`version ${vector.version} differs from the engine's ${RUNG_RULES_VERSION}; regenerate`);
  }
  const check = vector.assert;

  if (vector.bazi !== undefined) {
    const series = playBazi(vector.bazi, fail);
    if (series !== null) {
      if (check.series !== undefined) partial("series", check.series, series, fail);
      if (check.seriesHash !== undefined && stateHash(series) !== check.seriesHash) {
        fail(`seriesHash: expected ${check.seriesHash}, got ${stateHash(series)}`);
      }
    }
    return { failures, state: null, series };
  }

  let state: RungState | null = null;
  let rng: Rng = NO_RNG;
  if (vector.deal !== undefined) {
    const deal = vector.deal;
    const rules = resolveRungRules(deal.mode ?? "private", deal.preset ?? "rung", deal.overrides);
    if (!rules.ok) {
      fail(`rules: ${rules.error.code}`);
    } else {
      const seed = hexToBytes(deal.seedHex);
      // One stream for the deal and any redeal after it (RG7).
      rng = Xoshiro128StarStar.fromSeed(seed);
      const round = createRungRound(
        { rules: rules.value, seatCount: 4, dealerSeat: deal.dealerSeat, seed },
        rng,
      );
      if (round.ok) state = round.value;
      else fail(`deal: ${round.error.code}`);
    }
  } else if (vector.setup !== undefined) {
    const built = rungStateFromSetup(vector.setup);
    if (built.ok) state = built.value;
    else fail(`setup: ${built.error.code} ${JSON.stringify(built.error.detail)}`);
  } else {
    fail("a vector needs a deal, a setup or a bazi");
  }
  if (state === null) {
    return { failures, state: null, series: null };
  }

  const firstDeal = state;
  let lastEvents: readonly RungEvent[] = [];
  const allEvents: RungEvent[] = [];
  vector.moves.forEach((step, index) => {
    if (state === null) return;
    const result = applyRungMove(state, step.seat, step.move, rng);
    const expected = step.expect ?? "ok";
    if (expected === "ok") {
      if (!result.ok) {
        fail(`move ${index}: expected ok, got ${result.error.code}`);
        return;
      }
      state = result.state;
      lastEvents = result.events;
      allEvents.push(...result.events);
    } else if (result.ok) {
      fail(`move ${index}: expected ${expected.error}, but it was accepted`);
    } else if (result.error.code !== expected.error) {
      fail(`move ${index}: expected ${expected.error}, got ${result.error.code}`);
    }
  });
  const end: RungState = state;

  for (const field of STATE_FIELDS) {
    const expected = check[field];
    if (expected !== undefined && !same(end[field], expected)) {
      fail(`${field}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(end[field])}`);
    }
  }
  const handCounts = end.hands.map((hand) => hand.length);
  if (check.handCounts !== undefined && !same(handCounts, check.handCounts)) {
    fail(
      `handCounts: expected ${JSON.stringify(check.handCounts)}, got ${JSON.stringify(handCounts)}`,
    );
  }
  if (check.stockCount !== undefined && end.stock.length !== check.stockCount) {
    fail(`stockCount: expected ${check.stockCount}, got ${end.stock.length}`);
  }
  for (const [seat, cards] of Object.entries(check.hands ?? {})) {
    if (!same(end.hands[Number(seat)], cards)) {
      fail(
        `hands.${seat}: expected ${JSON.stringify(cards)}, got ${JSON.stringify(end.hands[Number(seat)])}`,
      );
    }
  }
  if (check.fullDeck === true) {
    const cards = [...end.hands.flat(), ...end.stock].sort();
    if (!same(cards, standardDeck().sort())) fail("fullDeck: the hands and stock are not one deck");
  }
  if (check.firstFiveFromDeck === true) {
    for (let i = 0; i < 4; i++) {
      const seat = (firstDeal.callerSeat + i) % 4;
      const five = firstDeal.deckOrder.slice(i * 5, i * 5 + 5);
      if (!five.every((card) => (end.hands[seat] ?? []).includes(card))) {
        fail(`firstFiveFromDeck: seat ${seat} does not hold its first five`);
      }
    }
  }
  if (check.lastEvents !== undefined) {
    const types = lastEvents.map((event) => event.type);
    if (!same(types, check.lastEvents)) {
      fail(
        `lastEvents: expected ${JSON.stringify(check.lastEvents)}, got ${JSON.stringify(types)}`,
      );
    }
  }
  for (const type of check.eventsInclude ?? []) {
    if (!allEvents.some((event) => event.type === type)) fail(`eventsInclude: no ${type}`);
  }
  for (const [seat, moves] of Object.entries(check.legal ?? {})) {
    const legal = legalRungMoves(end, Number(seat));
    if (!same(legal, moves)) {
      fail(`legal.${seat}: expected ${JSON.stringify(moves)}, got ${JSON.stringify(legal)}`);
    }
  }
  for (const [seat, move] of Object.entries(check.autoPlay ?? {})) {
    const auto = rungAutoPlayMove(end, Number(seat));
    if (!same(auto, move)) {
      fail(`autoPlay.${seat}: expected ${JSON.stringify(move)}, got ${JSON.stringify(auto)}`);
    }
  }
  for (const [seat, stats] of Object.entries(check.stats ?? {})) {
    partial(`stats.${seat}`, stats, end.stats[Number(seat)] ?? null, fail);
  }
  const result = rungResults(end);
  if (check.results !== undefined) partial("results", check.results, result, fail);
  if (check.nextDealer !== undefined) {
    const next = result === null ? null : nextRungDealer(result);
    if (next !== check.nextDealer) fail(`nextDealer: expected ${check.nextDealer}, got ${next}`);
  }
  for (const expected of check.views ?? []) {
    const ownSeat = expected.viewer === "spectator" ? null : expected.viewer;
    const view = redactRung(
      end,
      ownSeat === null ? { kind: "spectator" } : { kind: "seat", seat: ownSeat },
    );
    const label = `views[${expected.viewer}]`;
    if (expected.hand !== undefined && !same(view.hand, expected.hand)) {
      fail(
        `${label}.hand: expected ${JSON.stringify(expected.hand)}, got ${JSON.stringify(view.hand)}`,
      );
    }
    if (expected.handCounts !== undefined && !same(view.handCounts, expected.handCounts)) {
      fail(`${label}.handCounts: got ${JSON.stringify(view.handCounts)}`);
    }
    if (expected.stockCount !== undefined && view.stockCount !== expected.stockCount) {
      fail(`${label}.stockCount: got ${view.stockCount}`);
    }
    if (expected.seedHidden !== undefined && (view.seedHex === null) !== expected.seedHidden) {
      fail(`${label}.seedHidden: expected ${expected.seedHidden}`);
    }
    if (expected.canRedeal !== undefined && view.canRedeal !== expected.canRedeal) {
      fail(`${label}.canRedeal: expected ${expected.canRedeal}, got ${view.canRedeal}`);
    }
    if (expected.hidesOthers === true) {
      const json = JSON.stringify(view);
      const hidden = [
        ...end.stock,
        ...end.hands.flatMap((hand, seat) => (seat === ownSeat ? [] : hand)),
      ];
      if (hidden.some((card) => json.includes(`"${card}"`))) {
        fail(`${label}.hidesOthers: a hidden card is in the view`);
      }
    }
  }
  if (check.stateHash !== undefined && stateHash(end) !== check.stateHash) {
    fail(`stateHash: expected ${check.stateHash}, got ${stateHash(end)}`);
  }
  return { failures, state: end, series: null };
}
