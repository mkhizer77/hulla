import type { RoundRecord } from "../match/types";

/** What a round or a deal pays one seat, and what the amount depends on (GDD 12.2, 12.3). */
export interface RewardContext {
  /** At least one other player at the table is a friend (+20% XP). */
  readonly withFriend: boolean;
  /** Coins already earned today before this round, in the player's local day. */
  readonly coinsToday: number;
  /** Consecutive login days including today. */
  readonly loginStreak: number;
  /** Rounds at bot-heavy tables earlier today; absent counts as none. */
  readonly botRoundsToday?: number;
}

export interface RoundReward {
  readonly xp: number;
  readonly coins: number;
  readonly breakdown: {
    readonly finish: number | null;
    readonly bhabhi: boolean;
    readonly baseXp: number;
    readonly thullaXp: number;
    readonly cleanXp: number;
    readonly friendXp: number;
    readonly baseCoins: number;
    readonly streakCoins: number;
    readonly capped: boolean;
  };
}

/** Bots hold half the seats or more (interim reading of "bot-heavy", OQ-064). */
export function botHeavy(record: RoundRecord): boolean {
  const bots = record.bots ?? 0;
  return bots > 0 && bots * 2 >= record.players.length;
}

/**
 * How much of a seat's play was not the player's own: timeouts, auto-play and the covering bot
 * all log their moves as `auto`. "all" is a seat that never acted; "most" is over half.
 */
export function seatCover(record: RoundRecord, seat: number): "none" | "most" | "all" {
  const moves = record.moves.filter((m) => m.seat === seat);
  const auto = moves.filter((m) => m.auto).length;
  if (moves.length === 0 || auto * 2 <= moves.length) return "none";
  return auto === moves.length ? "all" : "most";
}
