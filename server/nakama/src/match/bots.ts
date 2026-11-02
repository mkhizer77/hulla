import { utf8Encode, Xoshiro128StarStar, type Rng } from "@hulla/shared-ts";

import { pickSeatAvatar } from "./avatars";
import { emptySlot } from "./lobby";

import type { SeatSlot } from "./types";

/**
 * Bots for a lone Quick Play player (owner decision, 29 Oct; GDD D7, 12.x): after 30 s without other
 * players the client asks for a table with up to three bots. A bot seat is ready, present and never
 * away, its name marked as a bot on every client. It plays the game's bot policy (GDD 8) from what
 * the table has seen, or the R44 policy for a game without one. Bots earn nothing and are left out
 * of stats, rewards and recent players (the round record lists them as empty).
 */
export const MAX_BOTS = 3;

/** Regional first names for bots (GDD bot roster); the client tags them as bots, so none passes as human. */
export const BOT_NAMES: readonly string[] = [
  "Rafiq",
  "Mehak",
  "Bilal",
  "Dadi",
  "Sunny",
  "Tanvir",
  "Priyanka",
  "Rohan",
  "Mithu",
  "Zara",
  "Kabir",
  "Noor",
];

/** How many bots fill a table of `seats` for one player: everyone else, at most three. */
export function botsFor(seats: number): number {
  return Math.max(0, Math.min(MAX_BOTS, Math.floor(seats) - 1));
}

/** A bot's seat at `index`: ready, present and always under auto-play, with a face nobody else shows. */
export function botSlot(
  seats: readonly SeatSlot[],
  index: number,
  name: string,
  now: number,
): SeatSlot {
  const slot = emptySlot();
  slot.playerId = `bot_${index}`;
  slot.displayName = name;
  slot.bot = true;
  slot.ready = true;
  slot.connected = true;
  slot.autoPlay = true;
  slot.rematch = true;
  slot.joinedTs = now;
  slot.avatar = pickSeatAvatar([...seats.slice(0, index), slot], index, null);
  return slot;
}

/** Bot names for one table, distinct, starting from a seed so tables differ. */
export function botNames(seed: number, count: number): string[] {
  const start = Math.abs(Math.floor(seed)) % BOT_NAMES.length;
  return Array.from(
    { length: count },
    (_, i) => BOT_NAMES[(start + i * 5) % BOT_NAMES.length] ?? "Bot",
  );
}

/** A bot never shares a seated person's name: on a clash it takes a free name from the roster. */
export function renameClashingBots(seats: readonly SeatSlot[], name: string | null): void {
  const key = name?.trim().toLowerCase();
  if (key === undefined || key === "") return;
  for (const slot of seats) {
    if (!slot.bot || slot.displayName?.toLowerCase() !== key) continue;
    const taken = new Set(seats.map((s) => s.displayName?.toLowerCase() ?? ""));
    slot.displayName = BOT_NAMES.find((n) => !taken.has(n.toLowerCase())) ?? slot.displayName;
  }
}

/**
 * How long a bot thinks before it plays (GDD 8.3): around the configured delay, quicker for a lone
 * legal card, a little longer the more it can choose from. `key` names the decision, so the pause
 * stays the same on every tick that checks it.
 */
export function botThinkMs(baseMs: number, key: string, choices: number): number {
  if (choices <= 1) return Math.round(baseMs * 0.5);
  const spread = 0.6 + 0.6 * unit(key);
  return Math.round(baseMs * spread + 40 * Math.min(choices, 8));
}

/** The bot's own randomness for one decision, apart from the round's deal and draws (ADR-0009). */
export function botRng(seedHex: string, moveCount: number, seat: number): Rng {
  return Xoshiro128StarStar.fromSeed(
    utf8Encode(`bot:${seedHex}:${String(moveCount)}:${String(seat)}`),
  );
}

/** A number in [0, 1) from `key` (FNV-1a). */
function unit(key: string): number {
  let hash = 0x811c9dc5;
  for (let i = 0; i < key.length; i += 1) {
    hash ^= key.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }
  return hash / 0x100000000;
}
