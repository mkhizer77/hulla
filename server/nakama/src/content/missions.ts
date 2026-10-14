import file from "../../../../content/missions/v1.json";

/** A mission as content defines it; the player's progress lives in `missions` storage. */
export interface MissionDef {
  readonly id: string;
  readonly name: string;
  readonly description: string;
  readonly metric: MissionMetric;
  readonly target: number;
  readonly reward: { readonly coins?: number; readonly gems?: number };
}

export type MissionMetric =
  | "rounds_played"
  | "rounds_with_friend"
  | "escapes"
  | "escaped_first"
  | "thullas_given"
  | "clean_rounds"
  | "low_pickup_rounds"
  | "hands_taken"
  | "series_won"
  | "voice_private";

export interface MissionPool {
  readonly id: string;
  readonly version: number;
  readonly daily: readonly MissionDef[];
  readonly weekly: readonly MissionDef[];
  /** Drawn first in a new player's first daily set (GDD 17.2: play a round with a friend). */
  readonly welcome?: MissionDef;
}

export const BUNDLED_MISSIONS: MissionPool = {
  id: file.id,
  version: file.version,
  daily: file.daily as MissionDef[],
  weekly: file.weekly as MissionDef[],
  welcome: file.welcome as MissionDef,
};
