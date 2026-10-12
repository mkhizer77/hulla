import { cardRank, cardSuit, rankValue, sortCards, type Card, type Suit } from "../card-kit";

import {
  EMPTY_TRICK,
  activeSeats,
  endRound,
  markEscaped,
  nextActiveSeat,
  withSeat,
  withStats,
  type ThullaState,
} from "./state";

import type { ApplyResult } from "../contract";
import type { ThullaEvent } from "./events";
import type { SeatIndex } from "@hulla/shared-ts";

type Outcome = ApplyResult<ThullaState, ThullaEvent>;

/** A validated card play: records it, hands the turn on, or resolves the trick when it is complete. */
export function playCard(state: ThullaState, seat: SeatIndex, card: Card): Outcome {
  const opening = state.phase === "opening";
  const leading = state.trick.plays.length === 0;
  const ledSuit = state.trick.ledSuit ?? cardSuit(card);
  // R19 and R20: an off-suit response is a thulla; in the opening only under R15.
  const thulla =
    !leading && cardSuit(card) !== ledSuit && (!opening || state.rules.openingThullaApplies);
  const events: ThullaEvent[] = [
    { type: "card_played", seat, card, trickIndex: state.trickIndex, thulla },
  ];
  const simultaneous =
    opening && state.rules.openingSimultaneous && !state.rules.openingThullaApplies;
  const played: ThullaState = {
    ...state,
    seats: withSeat(state.seats, seat, (current) => ({
      ...current,
      hand: current.hand.filter((held) => held !== card),
    })),
    trick: {
      ledSuit,
      plays: [...state.trick.plays, { seat, card }],
      thullaBy: state.trick.thullaBy ?? (thulla ? seat : null),
    },
    openingPending: simultaneous
      ? state.openingPending.filter((pending) => pending !== seat)
      : state.openingPending,
    pendingAutoLead: false,
  };
  const everyoneHasPlayed = simultaneous
    ? played.openingPending.length === 0
    : played.trick.plays.length === activeSeats(played).length;
  if ((thulla && state.rules.thullaEndsTrick) || everyoneHasPlayed) {
    return resolveTrick(played, events);
  }
  const turnSeat = simultaneous ? null : nextActiveSeat(played, seat);
  return { ok: true, state: { ...played, turnSeat }, events };
}

/** GDD 6.14: pick-up or discard, escapes before any draw, the empty-hand draw, then the next trick. */
function resolveTrick(state: ThullaState, events: ThullaEvent[]): Outcome {
  const { plays, thullaBy } = state.trick;
  const ledSuit = state.trick.ledSuit;
  if (ledSuit === null) {
    throw new Error("invariant: a trick with plays always has a led suit");
  }
  const top = highestOfLedSuit(plays, ledSuit);
  const pile = plays.map((p) => p.card);
  const pickedUp = thullaBy !== null;
  let next: ThullaState = { ...state, leaderSeat: top, trick: EMPTY_TRICK, phase: "play" };
  if (thullaBy !== null) {
    next = pickUp(next, top, thullaBy, pile);
  }
  events.push({
    type: "trick_resolved",
    trickIndex: state.trickIndex,
    ledSuit,
    cards: pile,
    thullaBy,
    picker: pickedUp ? top : null,
    nextLeader: top,
    handCounts: next.seats.map((s) => s.hand.length),
  });

  const activeBefore = activeSeats(state).length;
  if (pickedUp && activeBefore === 2 && state.rules.twoPlayerEndgame === "thullaEndsRound") {
    // R42: the first thulla of the endgame ends the round and the picker is the Bhabhi.
    next = escape(next, nextActiveSeat(next, top), events);
    return finish(next, top, events);
  }

  next = markEscapes(next, top, events);
  if (activeSeats(next).length === 1) {
    return finish(settlePile(next, pile, pickedUp), top, events);
  }
  if (next.seats[top]?.hand.length === 0) {
    next = planDrawForEmptyLeader(next, top, events);
  }
  next = settlePile(next, pile, pickedUp);
  const active = activeSeats(next);
  if (activeBefore > 2 && active.length === 2) {
    events.push({ type: "endgame_started", seats: active });
  }
  return {
    ok: true,
    state: {
      ...next,
      trickIndex: state.trickIndex + 1,
      turnSeat: next.leaderSeat,
      handTakeUsed: false,
      openingPending: [],
    },
    events,
  };
}

/** R21: the highest card of the led suit; the leader's own card guarantees one exists. */
function highestOfLedSuit(
  plays: readonly { readonly seat: SeatIndex; readonly card: Card }[],
  ledSuit: Suit,
): SeatIndex {
  let best: { seat: SeatIndex; value: number } | null = null;
  for (const play of plays) {
    if (cardSuit(play.card) !== ledSuit) {
      continue;
    }
    const value = rankValue(cardRank(play.card));
    if (best === null || value > best.value) {
      best = { seat: play.seat, value };
    }
  }
  if (best === null) {
    throw new Error("invariant: the led suit is always represented in a trick");
  }
  return best.seat;
}

/** R21: the picker takes every card of the pile, the thulla included. */
function pickUp(
  state: ThullaState,
  picker: SeatIndex,
  giver: SeatIndex,
  pile: readonly Card[],
): ThullaState {
  return {
    ...state,
    seats: withSeat(state.seats, picker, (current) => ({
      ...current,
      hand: sortCards([...current.hand, ...pile]),
    })),
    stats: withStats(
      withStats(state.stats, picker, (current) => ({
        ...current,
        cardsPickedUp: current.cardsPickedUp + pile.length,
        thullasReceived: current.thullasReceived + 1,
      })),
      giver,
      (current) => ({ ...current, thullasGiven: current.thullasGiven + 1 }),
    ),
  };
}

/** R22 and R26: a trick without a thulla joins the discard after any draw. */
function settlePile(state: ThullaState, pile: readonly Card[], pickedUp: boolean): ThullaState {
  return pickedUp ? state : { ...state, discard: [...state.discard, ...pile] };
}

/** R25 and R27: every empty-handed seat other than the leader escapes, in seat order from the leader. */
function markEscapes(state: ThullaState, leader: SeatIndex, events: ThullaEvent[]): ThullaState {
  let next = state;
  for (let step = 1; step < state.seatCount; step++) {
    const seat = (leader + step) % state.seatCount;
    const seatState = next.seats[seat];
    if (seatState !== undefined && !seatState.escaped && seatState.hand.length === 0) {
      next = escape(next, seat, events);
    }
  }
  return next;
}

function escape(state: ThullaState, seat: SeatIndex, events: ThullaEvent[]): ThullaState {
  const next = markEscaped(state, seat);
  events.push({ type: "escape", seat, rank: next.escapeOrder.length });
  return next;
}

/**
 * R26, R28 and R38: the leader with the hand but no cards draws one, or exits at once. The draw
 * itself is deferred so the forced-outcome search can enumerate it (R41); `applyMove` executes it.
 */
function planDrawForEmptyLeader(
  state: ThullaState,
  leader: SeatIndex,
  events: ThullaEvent[],
): ThullaState {
  const active = activeSeats(state);
  if (active.length === 2) {
    const opponent = nextActiveSeat(state, leader);
    if (state.rules.twoPlayerEndgame === "drawDiscard" && state.discard.length > 0) {
      return planDraw(state, "discard", null);
    }
    return planDraw(state, "opponent", opponent);
  }
  switch (state.rules.emptyHandLeader) {
    case "drawDiscard":
      // An empty discard is only possible under non-default toggles: OQ-001 interim choice.
      return state.discard.length > 0
        ? planDraw(state, "discard", null)
        : planDraw(state, "nextPlayer", nextActiveSeat(state, leader));
    case "drawNextPlayer":
      return planDraw(state, "nextPlayer", nextActiveSeat(state, leader));
    case "instantExit": {
      const exited = escape(state, leader, events);
      return { ...exited, leaderSeat: nextActiveSeat(exited, leader) };
    }
  }
}

/** The pool is fixed now: the discard before this trick's cards join it (R26), or a hand. */
function planDraw(
  state: ThullaState,
  source: "discard" | "nextPlayer" | "opponent",
  from: SeatIndex | null,
): ThullaState {
  const pool = from === null ? state.discard : (state.seats[from]?.hand ?? []);
  return { ...state, pendingDraw: { source, from, pool } };
}

function finish(state: ThullaState, bhabhiSeat: SeatIndex, events: ThullaEvent[]): Outcome {
  const ended = endRound(state, bhabhiSeat);
  events.push({ type: "round_ended", bhabhiSeat, escapeOrder: ended.escapeOrder });
  return { ok: true, state: ended, events };
}
