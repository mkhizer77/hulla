import file from "../../../../content/ranked/v1.json";

export interface LeagueReward {
  readonly title: string;
  readonly coins?: number;
  readonly items?: readonly string[];
  readonly frame?: string;
}

export interface League {
  readonly id: string;
  readonly name: string;
  readonly minRating: number;
  /** Held only inside the top `ustadTop` of the season board (GDD 7.5). */
  readonly topOnly?: boolean;
  readonly reward: LeagueReward;
}

export interface RankedConfig {
  readonly id: string;
  readonly version: number;
  readonly gameId: "thulla";
  readonly seats: number;
  readonly startRating: number;
  readonly placementRounds: number;
  readonly protectionRounds: number;
  readonly softReset: number;
  readonly ustadTop: number;
  readonly abandonAfterMs: number;
  readonly abandonPenalty: number;
  readonly season: { readonly anchorTs: number; readonly weeks: number };
  readonly matchmaking: {
    readonly band: number;
    readonly widenBy: number;
    readonly maxBand: number;
  };
  /** Lowest first; `pnpm content:validate` keeps them climbing. */
  readonly leagues: readonly League[];
}

/** The ranked ladder from content (GDD 7.5, 12.9); the numbers are interim (OQ-040). */
export const BUNDLED_RANKED: RankedConfig = file as RankedConfig;
