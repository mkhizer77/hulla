import basant from "../../../../content/seasons/basant-2026.json";

export interface PassReward {
  readonly coins?: number;
  readonly gems?: number;
  readonly items?: readonly string[];
  readonly title?: string;
}

export interface SeasonTier {
  readonly tier: number;
  readonly free: PassReward;
  readonly premium: PassReward;
}

export interface Season {
  readonly id: string;
  readonly version: number;
  readonly name: string;
  readonly startTs: number;
  readonly endTs: number;
  readonly xpPerTier: number;
  readonly xp: {
    readonly round: number;
    readonly dailyMission: number;
    readonly weeklyMission: number;
    readonly catchUpDays: number;
    readonly catchUpMultiplier: number;
  };
  readonly tiers: readonly SeasonTier[];
}

/** The seasons bundled from content, in date order (GDD 12.10). */
export const BUNDLED_SEASONS: readonly Season[] = [basant];
