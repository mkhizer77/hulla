/** What a round or a deal pays one seat, and what the amount depends on (GDD 12.2, 12.3). */
export interface RewardContext {
  /** At least one other player at the table is a friend (+20% XP). */
  readonly withFriend: boolean;
  /** Coins already earned today before this round, in the player's local day. */
  readonly coinsToday: number;
  /** Consecutive login days including today. */
  readonly loginStreak: number;
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
