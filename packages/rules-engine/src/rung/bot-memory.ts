import { SUITS, cardSuit, isCard, type Card, type Suit } from "../card-kit";

import { RUNG_SEAT_COUNT } from "./state";
import { trickWinner } from "./trick";

import type { RungEvent } from "./events";

/**
 * What everyone at a Rung table has seen this deal (Rung design 6.2), folded from the public
 * events only. One memory serves every bot at the table and a bot that takes over a seat part way
 * through. Plain data, so the table core can keep it between Nakama callbacks.
 */
export interface RungBotMemory {
  /** Every card played so far, the open trick included. */
  readonly played: readonly Card[];
  /** Suits a seat failed to follow: certain. */
  readonly voids: readonly (readonly Suit[])[];
  /** Seats that were void, losing the trick and did not cut: probably out of rungs. */
  readonly noRungs: readonly boolean[];
  readonly trump: Suit | null;
  /** The open trick, to know who was winning when a seat chose not to cut. */
  readonly trick: readonly { readonly seat: number; readonly card: Card }[];
}

export function rungBotMemory(): RungBotMemory {
  return {
    played: [],
    voids: Array.from({ length: RUNG_SEAT_COUNT }, () => []),
    noRungs: Array.from({ length: RUNG_SEAT_COUNT }, () => false),
    trump: null,
    trick: [],
  };
}

export function observeRungBotMemory(
  memory: RungBotMemory,
  events: readonly RungEvent[],
): RungBotMemory {
  let next = memory;
  for (const event of events) {
    next = observe(next, event);
  }
  return next;
}

function observe(memory: RungBotMemory, event: RungEvent): RungBotMemory {
  switch (event.type) {
    case "round_started":
    case "redealt":
      return rungBotMemory();
    case "trump_called":
      return { ...memory, trump: event.suit };
    case "card_played": {
      const led = memory.trick[0];
      const suit = cardSuit(event.card);
      let voids = memory.voids;
      let noRungs = memory.noRungs;
      if (led !== undefined && suit !== cardSuit(led.card)) {
        const ledSuit = cardSuit(led.card);
        voids = voids.map((suits, seat) =>
          seat === event.seat && !suits.includes(ledSuit) ? [...suits, ledSuit] : suits,
        );
        const winner = trickWinner(memory.trick, memory.trump);
        const losing = winner % 2 !== event.seat % 2;
        if (losing && memory.trump !== null && suit !== memory.trump) {
          noRungs = noRungs.map((flag, seat) => flag || seat === event.seat);
        }
      }
      return {
        ...memory,
        played: [...memory.played, event.card],
        voids,
        noRungs,
        trick: [...memory.trick, { seat: event.seat, card: event.card }],
      };
    }
    case "trick_won":
      return { ...memory, trick: [] };
    default:
      return memory;
  }
}

/** A memory read back from plain data, or null when it does not have the expected shape. */
export function asRungBotMemory(value: unknown): RungBotMemory | null {
  if (typeof value !== "object" || value === null) {
    return null;
  }
  const { played, voids, noRungs, trump, trick } = value as Partial<
    Record<keyof RungBotMemory, unknown>
  >;
  const isSuit = (item: unknown): boolean => (SUITS as readonly unknown[]).includes(item);
  const isCardValue = (item: unknown): boolean => typeof item === "string" && isCard(item);
  const valid =
    Array.isArray(played) &&
    played.every(isCardValue) &&
    Array.isArray(voids) &&
    voids.length === RUNG_SEAT_COUNT &&
    voids.every((row) => Array.isArray(row) && row.every(isSuit)) &&
    Array.isArray(noRungs) &&
    noRungs.length === RUNG_SEAT_COUNT &&
    noRungs.every((flag) => typeof flag === "boolean") &&
    (trump === null || isSuit(trump)) &&
    Array.isArray(trick) &&
    trick.every(
      (play) =>
        typeof play === "object" &&
        play !== null &&
        typeof (play as { seat?: unknown }).seat === "number" &&
        isCardValue((play as { card?: unknown }).card),
    );
  return valid ? (value as RungBotMemory) : null;
}
