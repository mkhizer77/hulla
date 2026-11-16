import { RANKS, SUITS, cardIndex, suitIndex, type Card } from "../card-kit";

import { rungDealPoints } from "./results";
import { RUNG_TRICKS, RUNG_TRICKS_TO_WIN, type RungOutcomeKind, type RungState } from "./state";

import type { RungRules } from "./rules";

/*
 * A small mutable copy of a Rung deal for the bot's play-outs and search (Rung design 6.3). The
 * engine's own state is immutable and far too slow to play thousands of deals per decision inside
 * Nakama, so this mirrors RG10 to RG27 over bit masks: a hand is four 13-bit masks, one per suit,
 * bit 0 the two and bit 12 the ace. `bot-sim.test.ts` plays random deals through both and expects
 * the same counts and outcome, so the two cannot drift apart unnoticed.
 */

/** A card as a number: suit index times 13 plus rank index (S H C D, two to ace). */
export type CardId = number;

export const NO_SEAT = -1;
const ACE = 12;
const KINDS: readonly RungOutcomeKind[] = ["win", "court", "court52"];

export function cardId(card: Card): CardId {
  return cardIndex(card);
}

export function cardOf(id: CardId): Card {
  const suit = SUITS[Math.floor(id / 13)] ?? "S";
  const rank = RANKS[id % 13] ?? "2";
  return `${rank}${suit}`;
}

export function highBit(mask: number): number {
  return 31 - Math.clz32(mask);
}

export function lowBit(mask: number): number {
  return 31 - Math.clz32(mask & -mask);
}

export function bitCount(mask: number): number {
  let n = 0;
  for (let m = mask; m !== 0; m &= m - 1) n += 1;
  return n;
}

export interface Sim {
  /** `seat * 4 + suit` to a 13-bit mask of the ranks held. */
  readonly hands: number[];
  readonly trump: number;
  readonly callerTeam: number;
  readonly single: boolean;
  readonly firstTwo: boolean;
  readonly aceRule: boolean;
  readonly lastTwo: boolean;
  readonly chase: boolean;
  turn: number;
  /** Cards on the open trick, in play order. */
  readonly trick: number[];
  trickLen: number;
  leader: number;
  /** The seat and card winning the open trick so far. */
  winSeat: number;
  winCard: number;
  /** Tricks completed. */
  trickNo: number;
  count0: number;
  count1: number;
  pile: number;
  run: number;
  lastWinner: number;
  lastWinAce: boolean;
  runAceBlocked: boolean;
  chasing: number;
  ended: boolean;
  outcomeTeam: number;
  outcomeKind: number;
}

/** A sim of `state` with the given hands (the bot's own and three it imagined). Play phase only. */
export function simFrom(state: RungState, hands: readonly (readonly CardId[])[]): Sim {
  const masks = new Array<number>(16).fill(0);
  hands.forEach((hand, seat) => {
    for (const id of hand) {
      const at = seat * 4 + Math.floor(id / 13);
      masks[at] = (masks[at] ?? 0) | (1 << (id % 13));
    }
  });
  const rules: RungRules = state.rules;
  const sim: Sim = {
    hands: masks,
    trump: state.trump === null ? 0 : suitIndex(state.trump),
    callerTeam: state.callerSeat % 2,
    single: rules.collect === "single",
    firstTwo: rules.firstTwo,
    aceRule: rules.aceRule,
    lastTwo: rules.lastTwo,
    chase: rules.chase,
    turn: state.turnSeat ?? 0,
    trick: [0, 0, 0, 0],
    trickLen: 0,
    leader: state.trick.leaderSeat,
    winSeat: NO_SEAT,
    winCard: 0,
    trickNo: state.trickIndex,
    count0: state.counts[0],
    count1: state.counts[1],
    pile: state.pile,
    run: state.run,
    lastWinner: state.lastWinner ?? NO_SEAT,
    lastWinAce: state.lastWinAce,
    runAceBlocked: state.runAceBlocked,
    chasing: state.chasing ?? NO_SEAT,
    ended: state.phase === "ended",
    outcomeTeam: state.outcome?.team ?? 0,
    outcomeKind: state.outcome === null ? 0 : KINDS.indexOf(state.outcome.kind),
  };
  for (const play of state.trick.plays) {
    place(sim, play.seat, cardId(play.card));
  }
  if (state.phase === "courtChoice" && state.turnSeat !== null) {
    // The bot always plays on (RG27 makes a failed chase free), and so does everyone it imagines.
    sim.chasing = state.turnSeat % 2;
  }
  return sim;
}

export function cloneSim(sim: Sim): Sim {
  return { ...sim, hands: sim.hands.slice(), trick: sim.trick.slice() };
}

/** Copies `from` into `into`, which must come from `cloneSim` of the same deal. */
export function copySim(into: Sim, from: Sim): void {
  for (let i = 0; i < 16; i += 1) into.hands[i] = from.hands[i] ?? 0;
  for (let i = 0; i < 4; i += 1) into.trick[i] = from.trick[i] ?? 0;
  into.turn = from.turn;
  into.trickLen = from.trickLen;
  into.leader = from.leader;
  into.winSeat = from.winSeat;
  into.winCard = from.winCard;
  into.trickNo = from.trickNo;
  into.count0 = from.count0;
  into.count1 = from.count1;
  into.pile = from.pile;
  into.run = from.run;
  into.lastWinner = from.lastWinner;
  into.lastWinAce = from.lastWinAce;
  into.runAceBlocked = from.runAceBlocked;
  into.chasing = from.chasing;
  into.ended = from.ended;
  into.outcomeTeam = from.outcomeTeam;
  into.outcomeKind = from.outcomeKind;
}

/** The suit led on the open trick, or -1 when the seat to act leads. */
export function ledSuit(sim: Sim): number {
  return sim.trickLen === 0 ? -1 : Math.floor((sim.trick[0] ?? 0) / 13);
}

/** RG12: whether `card` beats the card winning so far. */
function beats(sim: Sim, card: CardId): boolean {
  const suit = Math.floor(card / 13);
  const bestSuit = Math.floor(sim.winCard / 13);
  if (suit === bestSuit) return card > sim.winCard;
  return suit === sim.trump;
}

function place(sim: Sim, seat: number, card: CardId): void {
  if (sim.trickLen === 0) {
    sim.leader = seat;
    sim.winSeat = seat;
    sim.winCard = card;
  } else if (beats(sim, card)) {
    sim.winSeat = seat;
    sim.winCard = card;
  }
  sim.trick[sim.trickLen] = card;
  sim.trickLen += 1;
  sim.turn = (seat + 1) % 4;
}

/** The legal cards of the seat to act, into `out`; returns how many (RG11). */
export function simLegal(sim: Sim, out: number[]): number {
  const seat = sim.turn;
  const led = ledSuit(sim);
  let n = 0;
  if (led >= 0) {
    const mask = sim.hands[seat * 4 + led] ?? 0;
    if (mask !== 0) {
      for (let m = mask; m !== 0; m &= m - 1) {
        out[n] = led * 13 + lowBit(m);
        n += 1;
      }
      return n;
    }
  }
  for (let suit = 0; suit < 4; suit += 1) {
    for (let m = sim.hands[seat * 4 + suit] ?? 0; m !== 0; m &= m - 1) {
      out[n] = suit * 13 + lowBit(m);
      n += 1;
    }
  }
  return n;
}

/** The seat to act plays `card`; the fourth card resolves the trick (RG10 to RG27). */
export function simPlay(sim: Sim, card: CardId): void {
  const seat = sim.turn;
  const at = seat * 4 + Math.floor(card / 13);
  sim.hands[at] = (sim.hands[at] ?? 0) & ~(1 << (card % 13));
  place(sim, seat, card);
  if (sim.trickLen === 4) resolve(sim);
}

function addCount(sim: Sim, team: number, by: number): void {
  if (team === 0) sim.count0 += by;
  else sim.count1 += by;
}

function resolve(sim: Sim): void {
  const winner = sim.winSeat;
  const team = winner % 2;
  sim.trickNo += 1;
  const trickNo = sim.trickNo;
  if (sim.single) {
    addCount(sim, team, 1);
  } else {
    const pile = sim.pile + 1;
    const same = sim.lastWinner === winner;
    const run = same ? sim.run + 1 : 1;
    const thisAce = sim.winCard % 13 === ACE;
    const alreadyBlocked = same && sim.runAceBlocked;
    const aceHit = sim.aceRule && same && sim.lastWinAce && thisAce && !alreadyBlocked;
    const take =
      trickNo === RUNG_TRICKS ||
      (run >= 2 &&
        !aceHit &&
        !(sim.firstTwo && trickNo === 2) &&
        !(sim.lastTwo && trickNo === RUNG_TRICKS - 1));
    if (take) {
      addCount(sim, team, pile);
      sim.pile = 0;
      sim.lastWinner = NO_SEAT;
      sim.run = 0;
      sim.lastWinAce = false;
      sim.runAceBlocked = false;
    } else {
      sim.pile = pile;
      sim.lastWinner = winner;
      sim.run = run;
      sim.lastWinAce = thisAce;
      sim.runAceBlocked = alreadyBlocked || aceHit;
    }
  }
  sim.trickLen = 0;
  sim.turn = winner;
  sim.leader = winner;
  sim.winSeat = NO_SEAT;
  const mine = team === 0 ? sim.count0 : sim.count1;
  const theirs = team === 0 ? sim.count1 : sim.count0;
  const atSeven = sim.single ? 1 : 0;
  const atThirteen = sim.single ? 2 : 1;
  if (sim.chasing !== NO_SEAT) {
    const against = sim.chasing === 0 ? sim.count1 : sim.count0;
    if (against > 0) end(sim, sim.chasing, atSeven);
    else if (trickNo === RUNG_TRICKS) end(sim, sim.chasing, atThirteen);
    return;
  }
  if (mine < RUNG_TRICKS_TO_WIN) return;
  if (theirs > 0) end(sim, team, 0);
  else if (trickNo === RUNG_TRICKS) end(sim, team, sim.chase ? atThirteen : 0);
  else if (!sim.chase) end(sim, team, atSeven);
  else sim.chasing = team;
}

function end(sim: Sim, team: number, kind: number): void {
  sim.ended = true;
  sim.outcomeTeam = team;
  sim.outcomeKind = kind;
}

export function simOutcomeKind(sim: Sim): RungOutcomeKind {
  return KINDS[sim.outcomeKind] ?? "win";
}

/**
 * What the finished deal is worth to team 0 in bazi points (RG32), negative when team 1 won, with
 * a sliver per trick so that of two lines worth the same points the bot takes the more tricks.
 */
export function simValue(sim: Sim, rules: RungRules): number {
  const byCaller = sim.outcomeTeam === sim.callerTeam;
  const points = rungDealPoints(rules, simOutcomeKind(sim), byCaller);
  const signed = sim.outcomeTeam === 0 ? points : -points;
  return signed + (sim.count0 - sim.count1) / 100;
}
