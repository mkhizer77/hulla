/**
 * R53: the ranked rating. After a round every pair of rated players is compared; the better place
 * wins the pair (escaping earlier beats escaping later, the Bhabhi loses to everyone) and each pair
 * applies an Elo update with K = 32/(N−1), doubled while a player is provisional. Bot seats are left
 * out of the comparisons. Game-agnostic: the caller turns its result into places.
 */
export const RATING = {
  kBase: 32,
  provisionalRounds: 10,
  provisionalFactor: 2,
  scale: 400,
} as const;

export interface RatedEntry {
  readonly id: string;
  readonly rating: number;
  /** Rated rounds played before this one; provisional below `RATING.provisionalRounds`. */
  readonly rounds: number;
  /** 0 is best; equal places draw the pair. */
  readonly place: number;
  readonly bot?: boolean;
}

export interface RatingChange {
  readonly id: string;
  readonly before: number;
  readonly after: number;
  readonly delta: number;
  readonly provisional: boolean;
}

/** The chance the first player beats the second under Elo. */
export function expectedScore(rating: number, opponent: number): number {
  return 1 / (1 + 10 ** ((opponent - rating) / RATING.scale));
}

export function isProvisional(rounds: number): boolean {
  return rounds < RATING.provisionalRounds;
}

/** K for one pair in a round of `players` rated players. */
export function kFactor(players: number, rounds: number): number {
  if (players < 2) return 0;
  const k = RATING.kBase / (players - 1);
  return isProvisional(rounds) ? k * RATING.provisionalFactor : k;
}

/**
 * Every rated entry's change from one round, from the ratings before it. Bots keep their rating
 * and are not compared; with fewer than two rated players nothing moves. Deltas round to whole
 * points.
 */
export function rateRound(entries: readonly RatedEntry[]): RatingChange[] {
  const rated = entries.filter((e) => e.bot !== true);
  const n = rated.length;
  return entries.map((entry) => {
    const provisional = isProvisional(entry.rounds);
    if (entry.bot === true || n < 2) {
      return { id: entry.id, before: entry.rating, after: entry.rating, delta: 0, provisional };
    }
    const k = kFactor(n, entry.rounds);
    let sum = 0;
    for (const other of rated) {
      if (other === entry) continue;
      const score = entry.place < other.place ? 1 : entry.place > other.place ? 0 : 0.5;
      sum += k * (score - expectedScore(entry.rating, other.rating));
    }
    const delta = Math.round(sum);
    return { id: entry.id, before: entry.rating, after: entry.rating + delta, delta, provisional };
  });
}

/** Places from a finish order (first out is 0); seats missing from it share the last place. */
export function placesFromFinish(finishOrder: readonly number[], seatCount: number): number[] {
  return Array.from({ length: seatCount }, (_, seat) => {
    const index = finishOrder.indexOf(seat);
    return index === -1 ? finishOrder.length : index;
  });
}
