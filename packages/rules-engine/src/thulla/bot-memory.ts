import { SUITS, cardSuit, isCard, type Card, type Suit } from "../card-kit";

import type { ThullaEvent } from "./events";

/**
 * What everyone at the table has seen this round (GDD 8.2), folded from the public events only:
 * the suits each seat showed it lacks, and the cards each seat is known to hold because it picked
 * them up in front of everyone (R23). One memory serves every bot at the table. Plain data, so the
 * table core can keep it between Nakama callbacks.
 */
export interface ThullaBotMemory {
  readonly seatCount: number;
  /** Suits a seat played off; true for the cards it holds that nobody has seen. */
  readonly voids: readonly (readonly Suit[])[];
  /** Cards a seat picked up and has not played since. */
  readonly known: readonly (readonly Card[])[];
  /** The trick under way and its led suit, so an off-suit play marks a void. */
  readonly trickIndex: number;
  readonly ledSuit: Suit | null;
}

export function botMemory(seatCount: number): ThullaBotMemory {
  return {
    seatCount,
    voids: Array.from({ length: seatCount }, () => []),
    known: Array.from({ length: seatCount }, () => []),
    trickIndex: -1,
    ledSuit: null,
  };
}

/** The next memory after `events`. `private_draw` is skipped: only its seats saw the card. */
export function observeBotMemory(
  memory: ThullaBotMemory,
  events: readonly ThullaEvent[],
): ThullaBotMemory {
  let next = memory;
  for (const event of events) {
    next = observe(next, event);
  }
  return next;
}

function observe(memory: ThullaBotMemory, event: ThullaEvent): ThullaBotMemory {
  switch (event.type) {
    case "round_started":
      return botMemory(event.seatCount);
    case "card_played": {
      const suit = cardSuit(event.card);
      const leading = memory.ledSuit === null || memory.trickIndex !== event.trickIndex;
      const ledSuit = leading ? suit : memory.ledSuit;
      // A played card is in nobody's hand any more, wherever it was last known (R26, R38 draws).
      const known = memory.known.map((cards) => cards.filter((card) => card !== event.card));
      const voids =
        suit !== ledSuit
          ? update(memory.voids, event.seat, (suits) =>
              suits.includes(ledSuit) ? suits : [...suits, ledSuit],
            )
          : memory.voids;
      return { ...memory, known, voids, ledSuit, trickIndex: event.trickIndex };
    }
    case "trick_resolved": {
      const picker = event.picker;
      const known =
        picker === null
          ? memory.known
          : update(memory.known, picker, (cards) => [...cards, ...event.cards]);
      return { ...memory, known, ledSuit: null };
    }
    case "hand_taken": {
      // The taker now holds the target's cards too: what was known stays known, and an unseen card
      // can only be of a suit that one of the two might have held.
      const theirs = memory.known[event.target] ?? [];
      const theirVoids = memory.voids[event.target] ?? [];
      const known = update(
        update(memory.known, event.taker, (cards) => [...cards, ...theirs]),
        event.target,
        () => [],
      );
      const voids = update(
        update(memory.voids, event.taker, (suits) => suits.filter((s) => theirVoids.includes(s))),
        event.target,
        () => [],
      );
      return { ...memory, known, voids };
    }
    default:
      return memory;
  }
}

function update<T>(
  rows: readonly (readonly T[])[],
  seat: number,
  change: (row: readonly T[]) => readonly T[],
): (readonly T[])[] {
  return rows.map((row, index) => (index === seat ? change(row) : row));
}

/** A memory read back from plain data, or null when it does not have the expected shape. */
export function asBotMemory(value: unknown): ThullaBotMemory | null {
  if (typeof value !== "object" || value === null) {
    return null;
  }
  const memory = value as Partial<Record<keyof ThullaBotMemory, unknown>>;
  const { seatCount, voids, known, trickIndex, ledSuit } = memory;
  const rows = (rows: unknown, ok: (item: unknown) => boolean): boolean =>
    Array.isArray(rows) &&
    rows.length === seatCount &&
    rows.every((row) => Array.isArray(row) && row.every(ok));
  const isSuit = (item: unknown): boolean => (SUITS as readonly unknown[]).includes(item);
  const valid =
    typeof seatCount === "number" &&
    typeof trickIndex === "number" &&
    (ledSuit === null || isSuit(ledSuit)) &&
    rows(voids, isSuit) &&
    rows(known, (item) => typeof item === "string" && isCard(item));
  return valid ? (value as ThullaBotMemory) : null;
}
