import { shuffle, type Rng, type SeatIndex } from "@hulla/shared-ts";

import { SUITS, cardRank, cardsOfSuit, suitIndex, type Card, type Suit } from "../card-kit";

import { autoCallSuit, rungAutoPlayMove } from "./autoplay";
import { rungBotMemory, type RungBotMemory } from "./bot-memory";
import { policyCard } from "./bot-policy";
import {
  cardId,
  cardOf,
  cloneSim,
  copySim,
  simFrom,
  simLegal,
  simPlay,
  simValue,
  type CardId,
  type Sim,
} from "./bot-sim";
import { canRedeal, playableCards } from "./legal";
import { RUNG_SEAT_COUNT, RUNG_TRICKS, type RungState } from "./state";

import type { RungMove } from "./moves";
import type { RungRules } from "./rules";

/*
 * A bot that plays Rung at a strong club level (Rung design 6). It sees what a person in its seat
 * sees: its own cards, the open trick, the counts, and the public facts in `RungBotMemory`. For
 * each card it could play it deals the unseen cards many times in ways that fit those facts,
 * plays every deal out (exactly near the end, by the rule policy before that) and keeps the card
 * worth the most bazi points on average. It never reads another seat's hand, only how many cards
 * that seat holds.
 */

/** How much a bot thinks and whether it slips; one set per level (Rung design 6.4). */
export interface RungBotTuning {
  /** Deals of the unseen cards per decision. */
  readonly samples: number;
  /** With this many tricks or fewer left, a deal is solved exactly instead of played out. */
  readonly endgameTricks: number;
  /** Positions the exact search may visit per deal and card before it plays the rest out. */
  readonly endgameNodes: number;
  /**
   * The most work one decision may do, in card plays of the simulator (a searched position
   * counts as three). Fewer deals are sampled when a decision would exceed it, never under
   * `minSamples`; this is what keeps the worst case inside Nakama's budget (design 6.1).
   */
  readonly work: number;
  readonly minSamples: number;
  /** How often the second-best card is played when it is within `slipGap` points of the best. */
  readonly slipChance: number;
  readonly slipGap: number;
  /** RG7: ask for a redeal when the best suit scores under this (6.3, the call). */
  readonly redealBelow: number;
}

/** "Ustad": no slips. Every covered seat and every table of regular players. */
export const USTAD_BOT: RungBotTuning = {
  samples: 40,
  endgameTricks: 3,
  endgameNodes: 1_500,
  work: 6_000,
  minSamples: 8,
  slipChance: 0,
  slipGap: 0,
  redealBelow: 8,
};

/** "Khiladi": fewer deals and a rare second-best card, never one that throws away a sure trick. */
export const KHILADI_BOT: RungBotTuning = {
  samples: 16,
  endgameTricks: 3,
  endgameNodes: 1_500,
  work: 4_000,
  minSamples: 8,
  slipChance: 0.06,
  slipGap: 0.25,
  redealBelow: 8,
};

export interface ScoredRungCard {
  readonly card: Card;
  /** Average bazi points for the bot's team over the sampled deals. */
  readonly score: number;
}

const HONOUR_POINTS: Readonly<Partial<Record<string, number>>> = { A: 4, K: 3, Q: 2, J: 1 };

/** The call (6.3): two per card of the suit, plus A 4, K 3, Q 2, J 1. */
export function callScore(hand: readonly Card[], suit: Suit): number {
  return cardsOfSuit(hand, suit).reduce(
    (sum, card) => sum + 2 + (HONOUR_POINTS[cardRank(card)] ?? 0),
    0,
  );
}

/** The best suit to call from the first five; ties go to RG9's choice, then ♠ ♥ ♣ ♦. */
export function botCallSuit(hand: readonly Card[]): Suit {
  const fallback = autoCallSuit(hand);
  let best = fallback;
  let bestScore = callScore(hand, fallback);
  for (const suit of SUITS) {
    const score = callScore(hand, suit);
    if (score > bestScore) {
      best = suit;
      bestScore = score;
    }
  }
  return best;
}

/** The bot's move for `seat`; null when it is not that seat's turn. */
export function rungBotMove(
  state: RungState,
  seat: SeatIndex,
  memory: RungBotMemory | null,
  rng: Rng,
  tuning: RungBotTuning = USTAD_BOT,
): RungMove | null {
  if (state.phase === "ended" || state.turnSeat !== seat) {
    return null;
  }
  if (state.phase === "call") {
    const hand = state.hands[seat] ?? [];
    const suit = botCallSuit(hand);
    if (canRedeal(state, seat) && callScore(hand, suit) < tuning.redealBelow) {
      return { type: "redeal" };
    }
    return { type: "call_trump", suit };
  }
  if (state.phase === "courtChoice") {
    // RG27: a failed chase costs nothing, so the bigger prize is always worth playing for.
    return { type: "court_choice", playOn: true };
  }
  const scored = scoreRungCards(state, seat, memory, rng, tuning);
  const best = scored[0];
  if (best === undefined) {
    return rungAutoPlayMove(state, seat);
  }
  const second = scored[1];
  const slip =
    second !== undefined &&
    tuning.slipChance > 0 &&
    best.score - second.score < tuning.slipGap &&
    rng.nextInt(1_000_000) < tuning.slipChance * 1_000_000;
  return { type: "play_card", card: slip ? second.card : best.card };
}

/**
 * The rule policy's card alone (6.3): no search. The play-out policy inside the search, and a
 * yardstick in self-play for what the search adds. Null when it is not the seat's turn to play.
 */
export function rungPolicyMove(
  state: RungState,
  seat: SeatIndex,
  memory: RungBotMemory | null,
  rng: Rng,
): RungMove | null {
  if (state.phase !== "play" || state.turnSeat !== seat) {
    return null;
  }
  const hands = deal(publicFacts(state, seat, memory ?? rungBotMemory()), rng);
  if (hands === null) {
    return rungAutoPlayMove(state, seat);
  }
  return { type: "play_card", card: cardOf(policyCard(simFrom(state, hands))) };
}

/** Every distinct card the seat may play, best first. Cards that play the same count once. */
export function scoreRungCards(
  state: RungState,
  seat: SeatIndex,
  memory: RungBotMemory | null,
  rng: Rng,
  tuning: RungBotTuning = USTAD_BOT,
): ScoredRungCard[] {
  if (state.phase !== "play" || state.turnSeat !== seat) {
    return [];
  }
  const legal = playableCards(state, seat);
  if (legal.length <= 1) {
    return legal.map((card) => ({ card, score: 0 }));
  }
  const facts = publicFacts(state, seat, memory ?? rungBotMemory());
  const candidates = distinctCards(legal.map(cardId), facts);
  if (candidates.length === 1) {
    return candidates.map((id) => ({ card: cardOf(id), score: 0 }));
  }
  const totals = candidates.map(() => 0);
  const sign = seat % 2 === 0 ? 1 : -1;
  let sim: Sim | null = null;
  let preferred = -1;
  let dealt = 0;
  // Each sample plays every candidate to the end of the deal: about one play per card left.
  const cardsLeft = state.hands.reduce((sum, hand) => sum + hand.length, 0);
  const perSample = candidates.length * Math.max(4, cardsLeft);
  const samples = Math.max(
    tuning.minSamples,
    Math.min(tuning.samples, Math.floor(tuning.work / perSample)),
  );
  const work = { left: tuning.work };
  for (let sample = 0; sample < samples; sample += 1) {
    // The exact search can cost far more than a play-out; stop once the budget is spent.
    if (work.left < 0 && sample >= tuning.minSamples) break;
    const hands = deal(facts, rng);
    if (hands === null) break;
    const base = simFrom(state, hands);
    if (preferred < 0) preferred = policyCard(base);
    sim ??= cloneSim(base);
    for (let c = 0; c < candidates.length; c += 1) {
      copySim(sim, base);
      simPlay(sim, candidates[c] ?? 0);
      totals[c] = (totals[c] ?? 0) + sign * evaluate(sim, state.rules, tuning, work);
    }
    dealt += 1;
  }
  if (dealt === 0) {
    return [];
  }
  return candidates
    .map((id, index) => ({ id, score: (totals[index] ?? 0) / dealt }))
    .sort(
      (a, b) =>
        b.score - a.score ||
        Number(b.id === preferred) - Number(a.id === preferred) ||
        (a.id % 13) - (b.id % 13) ||
        a.id - b.id,
    )
    .map(({ id, score }) => ({ card: cardOf(id), score }));
}

interface Work {
  left: number;
}

/** The value of a position to team 0: solved exactly near the end, played out before that. */
function evaluate(sim: Sim, rules: RungRules, tuning: RungBotTuning, work: Work): number {
  if (!sim.ended && work.left > 0 && RUNG_TRICKS - sim.trickNo <= tuning.endgameTricks) {
    return search(sim, rules, -1_000, 1_000, { left: tuning.endgameNodes }, work);
  }
  return playOut(sim, rules, work);
}

function playOut(sim: Sim, rules: RungRules, work: Work): number {
  let steps = 0;
  for (; steps < 52 && !sim.ended; steps += 1) {
    simPlay(sim, policyCard(sim));
  }
  work.left -= steps;
  return simValue(sim, rules);
}

/** Alpha-beta over the open hands of one imagined deal; team 0 maximises. */
function search(
  sim: Sim,
  rules: RungRules,
  alpha: number,
  beta: number,
  budget: Work,
  work: Work,
): number {
  if (sim.ended) {
    return simValue(sim, rules);
  }
  budget.left -= 1;
  work.left -= 3;
  if (budget.left < 0 || work.left < 0) {
    return playOut(cloneSim(sim), rules, work);
  }
  const moves: number[] = [];
  const count = simLegal(sim, moves);
  const first = policyCard(sim);
  const blockers = blockerMasks(sim);
  const maximising = sim.turn % 2 === 0;
  let lo = alpha;
  let hi = beta;
  let best = maximising ? -1_000 : 1_000;
  let previous = -1;
  for (let i = -1; i < count; i += 1) {
    // The policy's card first: a good first move makes the cut-offs come early.
    const move = i < 0 ? first : (moves[i] ?? 0);
    if (i >= 0) {
      if (move === first) {
        previous = move;
        continue;
      }
      // A card touching the previous one of the suit, with nothing live between, plays the same.
      const same = previous >= 0 && touching(previous, move, blockers);
      previous = move;
      if (same) continue;
    }
    const child = cloneSim(sim);
    simPlay(child, move);
    const value = search(child, rules, lo, hi, budget, work);
    if (maximising) {
      if (value > best) best = value;
      if (best > lo) lo = best;
    } else {
      if (value < best) best = value;
      if (best < hi) hi = best;
    }
    if (lo >= hi) break;
  }
  return best;
}

/** Per suit, the cards that sit between a seat's own: the other seats' and the open trick's. */
function blockerMasks(sim: Sim): number[] {
  const masks = [0, 0, 0, 0];
  for (let seat = 0; seat < RUNG_SEAT_COUNT; seat += 1) {
    if (seat === sim.turn) continue;
    for (let suit = 0; suit < 4; suit += 1) {
      masks[suit] = (masks[suit] ?? 0) | (sim.hands[seat * 4 + suit] ?? 0);
    }
  }
  for (let i = 0; i < sim.trickLen; i += 1) {
    const id = sim.trick[i] ?? 0;
    const suit = Math.floor(id / 13);
    masks[suit] = (masks[suit] ?? 0) | (1 << (id % 13));
  }
  return masks;
}

/** Whether `low` and `high` are of one suit with no blocker ranked between them. */
function touching(low: CardId, high: CardId, blockers: readonly number[]): boolean {
  const suit = Math.floor(low / 13);
  if (Math.floor(high / 13) !== suit || high <= low) return false;
  const between = ((1 << (high % 13)) - 1) & ~((2 << (low % 13)) - 1);
  return ((blockers[suit] ?? 0) & between) === 0;
}

interface Facts {
  readonly seat: SeatIndex;
  readonly mine: readonly CardId[];
  /** Cards the bot has not seen: not its own, not played. */
  readonly unseen: readonly CardId[];
  /** How many cards each other seat holds (public); 0 for the bot's own seat. */
  readonly need: readonly number[];
  /** Per seat, a 4-bit mask of the suits it is known to lack. */
  readonly voids: readonly number[];
  readonly noRungs: readonly boolean[];
  readonly trump: number;
  readonly caller: number;
  /** Per suit, the ranks on the open trick. */
  readonly onTable: readonly number[];
}

function publicFacts(state: RungState, seat: SeatIndex, memory: RungBotMemory): Facts {
  const mine = (state.hands[seat] ?? []).map(cardId);
  const seen = new Set<CardId>(mine);
  for (const card of memory.played) seen.add(cardId(card));
  const onTable = [0, 0, 0, 0];
  for (const play of state.trick.plays) {
    const id = cardId(play.card);
    seen.add(id);
    const suit = Math.floor(id / 13);
    onTable[suit] = (onTable[suit] ?? 0) | (1 << (id % 13));
  }
  const unseen: CardId[] = [];
  for (let id = 0; id < 52; id += 1) {
    if (!seen.has(id)) unseen.push(id);
  }
  return {
    seat,
    mine,
    unseen,
    need: state.hands.map((hand, index) => (index === seat ? 0 : hand.length)),
    voids: memory.voids.map((suits) =>
      suits.reduce((mask, suit) => mask | (1 << suitIndex(suit)), 0),
    ),
    noRungs: memory.noRungs,
    trump: state.trump === null ? -1 : suitIndex(state.trump),
    caller: state.callerSeat,
    onTable,
  };
}

/** One card per run of the bot's cards that play the same: the lowest of each run. */
function distinctCards(legal: readonly CardId[], facts: Facts): CardId[] {
  const blockers = [...facts.onTable];
  for (const id of facts.unseen) {
    const suit = Math.floor(id / 13);
    blockers[suit] = (blockers[suit] ?? 0) | (1 << (id % 13));
  }
  const sorted = [...legal].sort((a, b) => a - b);
  const out: CardId[] = [];
  let previous = -1;
  for (const id of sorted) {
    if (previous < 0 || !touching(previous, id, blockers)) out.push(id);
    previous = id;
  }
  return out;
}

/**
 * One way the unseen cards may lie (6.3 step 1): every other seat gets as many cards as it holds,
 * never of a suit it failed to follow, and seats that would not cut get few rungs. Null when the
 * facts cannot be met, which happens only if the memory is not this deal's.
 */
function deal(facts: Facts, rng: Rng): CardId[][] | null {
  const total = facts.need.reduce((sum, n) => sum + n, 0);
  if (facts.unseen.length < total) {
    return null;
  }
  for (let attempt = 0; attempt < 8; attempt += 1) {
    // After a few failures the soft facts go first, then the voids, so a deal always comes out.
    const hands = tryDeal(facts, rng, total, attempt < 6);
    if (hands !== null) return hands;
  }
  return tryDeal(facts, rng, total, false);
}

function tryDeal(facts: Facts, rng: Rng, total: number, strict: boolean): CardId[][] | null {
  const need = [...facts.need];
  const hands: CardId[][] = facts.need.map((_, seat) =>
    seat === facts.seat ? [...facts.mine] : [],
  );
  const eligible = (id: CardId, seat: number): boolean =>
    (need[seat] ?? 0) > 0 &&
    (!strict || ((facts.voids[seat] ?? 0) & (1 << Math.floor(id / 13))) === 0);
  const options = (id: CardId): number => {
    let n = 0;
    for (let seat = 0; seat < RUNG_SEAT_COUNT; seat += 1) if (eligible(id, seat)) n += 1;
    return n;
  };
  // The cards with the fewest possible owners are placed first; the shuffle orders the rest.
  const cards = shuffle(rng, [...facts.unseen]).slice(0, total);
  const order = cards
    .map((id, index) => ({ id, index, options: options(id) }))
    .sort((a, b) => a.options - b.options || a.index - b.index);
  for (const { id } of order) {
    const rung = Math.floor(id / 13) === facts.trump;
    let sum = 0;
    const weights = [0, 0, 0, 0];
    for (let seat = 0; seat < RUNG_SEAT_COUNT; seat += 1) {
      if (!eligible(id, seat)) continue;
      let weight = (need[seat] ?? 0) * 20;
      if (rung && strict) {
        // Soft facts: the caller called a suit they held; a seat that would not cut has few rungs.
        if (seat === facts.caller) weight = Math.floor((weight * 5) / 4);
        if (facts.noRungs[seat] === true) weight = Math.max(1, Math.floor(weight / 8));
      }
      weights[seat] = weight;
      sum += weight;
    }
    if (sum === 0) {
      return null;
    }
    let pick = rng.nextInt(sum);
    let chosen = 0;
    for (let seat = 0; seat < RUNG_SEAT_COUNT; seat += 1) {
      const weight = weights[seat] ?? 0;
      if (weight === 0) continue;
      if (pick < weight) {
        chosen = seat;
        break;
      }
      pick -= weight;
    }
    hands[chosen]?.push(id);
    need[chosen] = (need[chosen] ?? 0) - 1;
  }
  return hands;
}
