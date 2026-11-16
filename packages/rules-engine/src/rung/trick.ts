import { cardRank, cardSuit, rankValue, type Card, type Suit } from "../card-kit";

import {
  RUNG_SEAT_COUNT,
  RUNG_TRICKS,
  RUNG_TRICKS_TO_WIN,
  nextRungSeat,
  otherTeam,
  teamOf,
  withRungStats,
  type RungOutcomeKind,
  type RungState,
  type RungTeam,
} from "./state";

import type { RungEvent } from "./events";
import type { SeatIndex } from "@hulla/shared-ts";

type Play = RungState["trick"]["plays"][number];

/** RG12: the highest rung wins; with no rung, the highest card of the led suit. */
export function trickWinner(plays: readonly Play[], trump: Suit | null): SeatIndex {
  const first = plays[0];
  if (first === undefined) {
    throw new Error("invariant: a trick is resolved only with cards in it");
  }
  let best = first;
  for (const play of plays.slice(1)) {
    if (beats(play.card, best.card, trump)) {
      best = play;
    }
  }
  return best.seat;
}

/** Whether `card` beats `best`, the card winning so far, which is of the led suit or a rung. */
function beats(card: Card, best: Card, trump: Suit | null): boolean {
  if (cardSuit(card) === cardSuit(best)) {
    return rankValue(cardRank(card)) > rankValue(cardRank(best));
  }
  return cardSuit(card) === trump;
}

/** RG10 to RG13: one card onto the trick; the fourth resolves it. The move is already validated. */
export function playRungCard(
  state: RungState,
  seat: SeatIndex,
  card: Card,
): { state: RungState; events: RungEvent[] } {
  const led = state.trick.plays[0];
  const cut =
    led !== undefined && cardSuit(card) === state.trump && cardSuit(led.card) !== state.trump;
  const plays = [...state.trick.plays, { seat, card }];
  const events: RungEvent[] = [
    { type: "card_played", seat, card, trickIndex: state.trickIndex, cut },
  ];
  const played: RungState = {
    ...state,
    hands: state.hands.map((hand, index) =>
      index === seat ? hand.filter((held) => held !== card) : hand,
    ),
    trick: { ...state.trick, plays },
    turnSeat: nextRungSeat(seat),
    stats: cut
      ? withRungStats(state.stats, seat, (stats) => ({ ...stats, cuts: stats.cuts + 1 }))
      : state.stats,
  };
  if (plays.length < RUNG_SEAT_COUNT) {
    return { state: played, events };
  }
  return { state: resolveTrick(played, events), events };
}

/** RG15 to RG23: who owns the trick, then RG24 to RG27: whether the deal goes on. */
function resolveTrick(state: RungState, events: RungEvent[]): RungState {
  const plays = state.trick.plays;
  const winner = trickWinner(plays, state.trump);
  const winningCard = plays.find((play) => play.seat === winner)?.card;
  const team = teamOf(winner);
  const trickNo = state.trickIndex + 1;
  const counts: [number, number] = [state.counts[0], state.counts[1]];
  let stats = withRungStats(state.stats, winner, (current) => ({
    ...current,
    tricksWon: current.tricksWon + 1,
  }));
  let next: RungState;

  if (state.rules.collect === "single") {
    // RG15: the trick is the team's at once.
    counts[team] += 1;
    events.push(trickWon(state, winner, 0, counts));
    next = { ...state, counts, stats };
  } else {
    // RG16: onto the pile; RG17 and RG18: two in a row by one player take it.
    const pile = state.pile + 1;
    const sameRunner = state.lastWinner === winner;
    const run = sameRunner ? state.run + 1 : 1;
    const thisAce = winningCard !== undefined && cardRank(winningCard) === "A";
    const alreadyBlocked = sameRunner && state.runAceBlocked;
    // RG22: two aces in a row are refused once; the run stays alive.
    const aceHit =
      state.rules.aceRule && sameRunner && state.lastWinAce && thisAce && !alreadyBlocked;
    const take =
      trickNo === RUNG_TRICKS || // RG20
      (run >= 2 &&
        !aceHit &&
        !(state.rules.firstTwo && trickNo === 2) && // RG21
        !(state.rules.lastTwo && trickNo === RUNG_TRICKS - 1)); // RG23
    events.push(trickWon(state, winner, pile, counts));
    if (take) {
      counts[team] += pile;
      stats = withRungStats(stats, winner, (current) => ({
        ...current,
        pilesTaken: current.pilesTaken + 1,
      }));
      events.push({ type: "pile_taken", seat: winner, team, tricks: pile, counts });
      // RG19: the run starts again.
      next = {
        ...state,
        counts,
        stats,
        pile: 0,
        lastWinner: null,
        run: 0,
        lastWinAce: false,
        runAceBlocked: false,
      };
    } else {
      next = {
        ...state,
        counts,
        stats,
        pile,
        lastWinner: winner,
        run,
        lastWinAce: thisAce,
        runAceBlocked: alreadyBlocked || aceHit,
      };
    }
  }

  return afterTrick(next, winner, team, trickNo, events);
}

function trickWon(
  state: RungState,
  winner: SeatIndex,
  pile: number,
  counts: readonly [number, number],
): RungEvent {
  return {
    type: "trick_won",
    trickIndex: state.trickIndex,
    winner,
    cards: state.trick.plays.map((play) => play.card),
    pile,
    counts: [counts[0], counts[1]],
  };
}

/** What a team at seven against zero has already won: a court in Single Sar, a win in Double Sar. */
export function prizeAtSeven(state: RungState): RungOutcomeKind {
  return state.rules.collect === "single" ? "court" : "win";
}

/** What playing on can reach: all thirteen (RG31). */
function prizeAtThirteen(state: RungState): RungOutcomeKind {
  return state.rules.collect === "single" ? "court52" : "court";
}

function afterTrick(
  state: RungState,
  winner: SeatIndex,
  team: RungTeam,
  trickNo: number,
  events: RungEvent[],
): RungState {
  const chasing = state.chasing;
  if (chasing !== null) {
    // RG27: the chase ends the moment the other team has a trick; nothing is lost.
    if (state.counts[otherTeam(chasing)] > 0) {
      return endRungRound(state, chasing, prizeAtSeven(state), events);
    }
    if (trickNo === RUNG_TRICKS) {
      return endRungRound(state, chasing, prizeAtThirteen(state), events);
    }
    return nextTrick(state, winner);
  }
  if (state.counts[team] < RUNG_TRICKS_TO_WIN) {
    return nextTrick(state, winner);
  }
  // RG25: seven against at least one ends the deal.
  if (state.counts[otherTeam(team)] > 0) {
    return endRungRound(state, team, "win", events);
  }
  if (trickNo === RUNG_TRICKS) {
    // Double Sar: the whole deal lay in the pile and trick thirteen took it (RG20).
    return endRungRound(state, team, state.rules.chase ? prizeAtThirteen(state) : "win", events);
  }
  if (!state.rules.chase) {
    return endRungRound(state, team, prizeAtSeven(state), events);
  }
  // RG26: the player who won the trick chooses to stop or play on.
  events.push({ type: "court_choice_opened", seat: winner, team });
  return {
    ...state,
    phase: "courtChoice",
    turnSeat: winner,
    trickIndex: state.trickIndex + 1,
    trick: { leaderSeat: winner, plays: [] },
  };
}

/** RG13: the winner leads the next trick. */
function nextTrick(state: RungState, winner: SeatIndex): RungState {
  return {
    ...state,
    turnSeat: winner,
    trickIndex: state.trickIndex + 1,
    trick: { leaderSeat: winner, plays: [] },
  };
}

export function endRungRound(
  state: RungState,
  team: RungTeam,
  kind: RungOutcomeKind,
  events: RungEvent[],
): RungState {
  events.push({ type: "round_ended", team, kind, counts: [state.counts[0], state.counts[1]] });
  return {
    ...state,
    phase: "ended",
    turnSeat: null,
    trick: { leaderSeat: state.trick.leaderSeat, plays: [] },
    outcome: { team, kind },
  };
}
