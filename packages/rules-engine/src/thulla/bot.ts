import { shuffle, type Rng, type SeatIndex } from "@hulla/shared-ts";

import {
  RANKS,
  SUITS,
  cardIndex,
  cardRank,
  cardSuit,
  rankValue,
  sortCards,
  standardDeck,
  type Card,
  type Suit,
} from "../card-kit";

import { rawApply } from "./apply";
import { botMemory, type ThullaBotMemory } from "./bot-memory";
import { cardsInPlay } from "./confirm";
import { executeDraw } from "./draw";
import { legalMoves } from "./legal";
import { activeSeats, type ThullaState } from "./state";

import type { ThullaMove } from "./moves";

/*
 * A bot that plays Thulla the way a decent player does (GDD 8.3, the Regular tier). It sees only
 * what a person in its seat sees (GDD 8.2): its own hand, the cards played, the discard, the
 * public pick-ups and voids in `ThullaBotMemory`, and how many cards each seat holds. It never
 * reads another seat's hand. For each card it might play it deals the unseen cards many times in
 * ways that fit what it knows, plays the rest of the trick out with a plain human policy for the
 * others, and keeps the card that sheds the most without picking up. With two players left the
 * opponent's hand is simply every card not seen yet, so it searches the endgame exactly.
 */

/** How a bot weighs its options and how much it thinks; one set per tier (GDD 8.3). */
export interface BotTuning {
  /** Weight of holding a card that tops what the others still have: such cards end up picked up. */
  readonly burden: number;
  /** Cost of each suit held: a suit you lack is a suit you can thulla on. */
  readonly suit: number;
  /** Deals per decision, shared between the candidates, and the bounds per candidate. */
  readonly deals: number;
  readonly minDeals: number;
  readonly maxDeals: number;
  /** The exact two-player search: at most this many cards in play and this many positions. */
  readonly endgameCards: number;
  readonly endgameNodes: number;
  /** A person sometimes plays the second-best card when the two are close. */
  readonly slipChance: number;
  readonly slipGap: number;
}

/** The Regular tier ("Khiladi"): sound play, an occasional slip, light enough for the server. */
export const REGULAR_BOT: BotTuning = {
  burden: 3,
  suit: 0.25,
  deals: 100,
  minDeals: 8,
  maxDeals: 16,
  endgameCards: 12,
  endgameNodes: 600,
  slipChance: 0.12,
  slipGap: 0.4,
};

const ESCAPED = 6;
const BHABHI = -30;
/** Built once at load: Nakama freezes module globals after InitModule. */
const DECK: readonly Card[] = standardDeck();

export interface ScoredCard {
  readonly card: Card;
  readonly score: number;
}

/** The bot's move for `seat`; null when the seat has nothing to play. Never a take (R34). */
export function botMove(
  state: ThullaState,
  seat: SeatIndex,
  memory: ThullaBotMemory | null,
  rng: Rng,
  tuning: BotTuning = REGULAR_BOT,
): ThullaMove | null {
  const scored = scoreCards(state, seat, memory ?? botMemory(state.seatCount), rng, tuning);
  const best = scored[0];
  if (best === undefined) {
    return null;
  }
  const second = scored[1];
  const slip =
    second !== undefined &&
    best.score - second.score < tuning.slipGap &&
    chance(rng) < tuning.slipChance;
  return { type: "play_card", card: slip ? second.card : best.card };
}

/** Every distinct card choice, best first. Cards that play the same (adjacent ranks) count once. */
export function scoreCards(
  state: ThullaState,
  seat: SeatIndex,
  memory: ThullaBotMemory,
  rng: Rng,
  tuning: BotTuning = REGULAR_BOT,
): ScoredCard[] {
  const cards = legalCards(state, seat);
  if (cards.length <= 1) {
    return cards.map((card) => ({ card, score: 0 }));
  }
  const candidates = distinctChoices(state, seat, cards);
  if (candidates.length === 1) {
    return candidates.map((card) => ({ card, score: 0 }));
  }
  const facts = publicFacts(state, seat, memory);
  const endgame = activeSeats(state).length === 2 && cardsInPlay(state) <= tuning.endgameCards;
  const perCard = Math.floor(tuning.deals / candidates.length);
  const deals = endgame ? 1 : Math.max(tuning.minDeals, Math.min(tuning.maxDeals, perCard));
  const totals = new Map<Card, number>(candidates.map((card) => [card, 0]));
  for (let d = 0; d < deals; d++) {
    const world = deal(state, facts, rng);
    for (const card of candidates) {
      totals.set(
        card,
        (totals.get(card) ?? 0) + value(playTrickOut(world, seat, card), seat, tuning),
      );
    }
  }
  const odds = endgame
    ? endgameOdds(deal(state, facts, rng), seat, candidates, tuning.endgameNodes)
    : null;
  return candidates
    .map((card) => ({
      card,
      score: (totals.get(card) ?? 0) / deals + (odds === null ? 0 : -20 * (odds.get(card) ?? 0)),
    }))
    .sort((a, b) => b.score - a.score || cardIndex(a.card) - cardIndex(b.card));
}

/**
 * Two cards of a suit with nothing live between them (only this seat's cards or the discard) do
 * the same thing; the higher one stands for both, since shedding it leaves the lower behind.
 */
function distinctChoices(state: ThullaState, seat: SeatIndex, cards: readonly Card[]): Card[] {
  const out = new Set<Card>([...handOf(state, seat), ...state.discard]);
  const choices: Card[] = [];
  for (const suit of SUITS) {
    const ofSuit = sortCards(cards.filter((card) => cardSuit(card) === suit));
    ofSuit.forEach((card, i) => {
      const higher = ofSuit[i + 1];
      if (higher === undefined || liveBetween(card, higher, out)) {
        choices.push(card);
      }
    });
  }
  return choices;
}

function liveBetween(low: Card, high: Card, out: Set<Card>): boolean {
  const between = RANKS.slice(rankValue(cardRank(low)) + 1, rankValue(cardRank(high)));
  return between.some((rank) => !out.has(`${rank}${cardSuit(low)}`));
}

/** Another seat as the bot sees it: what it is known to hold, how many more, which suits it lacks. */
interface Other {
  readonly seat: SeatIndex;
  readonly known: readonly Card[];
  readonly need: number;
  readonly voids: readonly Suit[];
}

interface Facts {
  readonly others: readonly Other[];
  /** Cards nobody has seen: in some other seat's hand, nobody knows whose. */
  readonly unseen: readonly Card[];
}

/**
 * The public picture from `seat`: hand counts, the discard, the pile and the memory. Every card is
 * in a hand, the discard or the pile, so the unseen cards always fill the other hands exactly.
 */
function publicFacts(state: ThullaState, seat: SeatIndex, memory: ThullaBotMemory): Facts {
  const seen = new Set<Card>([
    ...handOf(state, seat),
    ...state.discard,
    ...state.trick.plays.map((play) => play.card),
  ]);
  const others = activeSeats(state)
    .filter((other) => other !== seat)
    .map((other): Other => {
      // Only the count of another seat's hand is read, never its cards.
      const count = handOf(state, other).length;
      const remembered = memory.known[other] ?? [];
      const known = remembered.filter((card) => !seen.has(card)).slice(0, count);
      known.forEach((card) => seen.add(card));
      return { seat: other, known, need: count - known.length, voids: memory.voids[other] ?? [] };
    });
  return { others, unseen: DECK.filter((card) => !seen.has(card)) };
}

/**
 * One way the unseen cards could lie: each goes to a seat with room that is not known to lack its
 * suit, as likely as the room it has left. A memory that cannot be right is overruled, not trusted.
 */
function deal(state: ThullaState, facts: Facts, rng: Rng): ThullaState {
  const slots = facts.others.map((other) => ({
    ...other,
    hand: [...other.known],
    room: other.need,
  }));
  for (const card of shuffle(rng, facts.unseen)) {
    const withRoom = slots.filter((slot) => slot.room > 0);
    if (withRoom.length === 0) {
      break;
    }
    const open = withRoom.filter((slot) => !slot.voids.includes(cardSuit(card)));
    const choice = open.length > 0 ? open : withRoom;
    let pick = rng.nextInt(choice.reduce((sum, slot) => sum + slot.room, 0));
    const slot = choice.find((candidate) => (pick -= candidate.room) < 0);
    slot?.hand.push(card);
    if (slot !== undefined) {
      slot.room -= 1;
    }
  }
  const bySeat = new Map(slots.map((slot) => [slot.seat, slot.hand]));
  return {
    ...state,
    seats: state.seats.map((seatState, index) => {
      const hand = bySeat.get(index);
      return hand === undefined ? seatState : { ...seatState, hand: sortCards(hand) };
    }),
  };
}

/** `seat` plays `card`, then everyone else answers as a plain player would until the trick ends. */
function playTrickOut(world: ThullaState, seat: SeatIndex, card: Card): ThullaState {
  let state = played(world, seat, card);
  while (state.phase !== "ended" && state.trickIndex === world.trickIndex) {
    const next = state.turnSeat ?? state.openingPending[0] ?? state.leaderSeat;
    state = played(state, next, expectedReply(state, next));
  }
  return state;
}

/** A play the bot already knows is legal: its own candidates and the replies it predicts. */
function played(state: ThullaState, seat: SeatIndex, card: Card): ThullaState {
  const result = rawApply(state, seat, { type: "play_card", card });
  if (!result.ok) {
    throw new Error(`invariant: the bot only plays legal cards (${result.error.code})`);
  }
  return result.state;
}

/**
 * How the others are expected to answer: follow under the top card when someone after them could
 * still thulla, throw their highest card when they play last or nothing can be picked up, and
 * thulla their worst card when they are out of the suit.
 */
function expectedReply(state: ThullaState, seat: SeatIndex): Card {
  const hand = handOf(state, seat);
  const led = state.trick.ledSuit ?? "S";
  const following = hand.filter((card) => cardSuit(card) === led);
  if (following.length === 0) {
    return worstCard(state, seat);
  }
  const free = state.phase === "opening" || isLastToPlay(state);
  if (free) {
    return highest(following);
  }
  const top = topOfLed(state);
  const under = following.filter((card) => rankValue(cardRank(card)) < top);
  return under.length > 0 ? highest(under) : lowest(following);
}

function isLastToPlay(state: ThullaState): boolean {
  return state.trick.plays.length === activeSeats(state).length - 1;
}

function topOfLed(state: ThullaState): number {
  const led = state.trick.ledSuit;
  let top = -1;
  for (const play of state.trick.plays) {
    if (cardSuit(play.card) === led) {
      top = Math.max(top, rankValue(cardRank(play.card)));
    }
  }
  return top;
}

/** The card a player most wants gone: the one most likely to top a trick, alone in its suit first. */
function worstCard(state: ThullaState, seat: SeatIndex): Card {
  const hand = handOf(state, seat);
  const live = liveForOthers(state, seat);
  const badness = (card: Card): number => {
    const alone = hand.filter((held) => cardSuit(held) === cardSuit(card)).length === 1;
    return burden(card, live) + rankValue(cardRank(card)) / 24 + (alone ? 0.3 : 0);
  };
  return hand.reduce((worst, card) => (badness(card) > badness(worst) ? card : worst));
}

/** How good the end of the trick is for `seat`: few cards, none that top the suit, few suits. */
function value(state: ThullaState, seat: SeatIndex, tuning: BotTuning): number {
  if (state.phase === "ended") {
    return state.bhabhiSeat === seat ? BHABHI : ESCAPED;
  }
  const me = state.seats[seat];
  if (me === undefined || me.escaped) {
    return ESCAPED;
  }
  const drawing = state.pendingDraw !== null && state.leaderSeat === seat ? 1 : 0;
  const live = liveForOthers(state, seat);
  const weight = me.hand.reduce((sum, card) => sum + burden(card, live), 0);
  const suits = new Set(me.hand.map(cardSuit)).size;
  return -(me.hand.length + drawing) - tuning.burden * weight - tuning.suit * suits;
}

/** For each suit, the ranks the other seats could still hold: not in the discard, not mine. */
function liveForOthers(state: ThullaState, seat: SeatIndex): Map<Suit, number[]> {
  const out = new Set<Card>([...handOf(state, seat), ...state.discard]);
  const live = new Map<Suit, number[]>(SUITS.map((suit) => [suit, []]));
  for (const card of DECK) {
    if (!out.has(card)) {
      live.get(cardSuit(card))?.push(rankValue(cardRank(card)));
    }
  }
  return live;
}

/** The share of the others' live cards of its suit this card beats; 1 when they have none left. */
function burden(card: Card, live: Map<Suit, number[]>): number {
  const ranks = live.get(cardSuit(card)) ?? [];
  if (ranks.length === 0) {
    return 1;
  }
  const rank = rankValue(cardRank(card));
  return ranks.filter((r) => r < rank).length / ranks.length;
}

/**
 * The chance `seat` ends as the Bhabhi after each candidate, both seats playing their best and
 * every draw averaged (R38, R39). Null when the search would run past its cap.
 */
function endgameOdds(
  world: ThullaState,
  seat: SeatIndex,
  candidates: readonly Card[],
  cap: number,
): Map<Card, number> | null {
  const memo = new Map<string, number>();
  let nodes = 0;

  function odds(state: ThullaState): number | undefined {
    if (state.phase === "ended") {
      return state.bhabhiSeat === seat ? 1 : 0;
    }
    const key = endgameKey(state);
    const seen = memo.get(key);
    if (seen !== undefined) {
      return seen;
    }
    nodes += 1;
    if (nodes > cap) {
      return undefined;
    }
    let result: number;
    const draw = state.pendingDraw;
    if (draw !== null) {
      // Every card of the pool is as likely as the next (R26, R38).
      let sum = 0;
      for (let i = 0; i < draw.pool.length; i++) {
        const child = odds(executeDraw(state, i, []));
        if (child === undefined) {
          return undefined;
        }
        sum += child;
      }
      result = sum / draw.pool.length;
    } else {
      // The bot plays to stay out of it; the opponent plays to put it there.
      const actor = state.turnSeat ?? state.leaderSeat;
      const mine = actor === seat;
      result = mine ? 1 : 0;
      for (const card of legalCards(state, actor)) {
        const child = odds(played(state, actor, card));
        if (child === undefined) {
          return undefined;
        }
        result = mine ? Math.min(result, child) : Math.max(result, child);
      }
    }
    memo.set(key, result);
    return result;
  }

  const out = new Map<Card, number>();
  for (const card of candidates) {
    const result = odds(played(world, seat, card));
    if (result === undefined) {
      return null;
    }
    out.set(card, result);
  }
  return out;
}

/** The search's memo key: cheaper than the canonical JSON of `keyOf`, enough for two seats. */
function endgameKey(state: ThullaState): string {
  const hands = state.seats.map((seat) => seat.hand.join("")).join("|");
  const plays = state.trick.plays.map((play) => `${String(play.seat)}${play.card}`).join("");
  const draw = state.pendingDraw === null ? "" : state.pendingDraw.pool.join("");
  const discard = state.rules.twoPlayerEndgame === "drawDiscard" ? sortCards(state.discard) : [];
  return [
    hands,
    state.leaderSeat,
    state.turnSeat ?? "-",
    plays,
    state.pendingAutoLead ? 1 : 0,
    draw,
    discard.join(""),
  ].join("/");
}

function highest(cards: readonly Card[]): Card {
  return cards.reduce((best, card) =>
    rankValue(cardRank(card)) > rankValue(cardRank(best)) ? card : best,
  );
}

function lowest(cards: readonly Card[]): Card {
  return cards.reduce((best, card) =>
    rankValue(cardRank(card)) < rankValue(cardRank(best)) ? card : best,
  );
}

function chance(rng: Rng): number {
  return rng.nextInt(1_000_000) / 1_000_000;
}

/** The cards `seat` may play now; a take is never a bot's choice (R34). */
function legalCards(state: ThullaState, seat: SeatIndex): Card[] {
  const cards: Card[] = [];
  for (const move of legalMoves(state, seat)) {
    if (move.type === "play_card") {
      cards.push(move.card);
    }
  }
  return cards;
}

function handOf(state: ThullaState, seat: SeatIndex): readonly Card[] {
  return state.seats[seat]?.hand ?? [];
}
