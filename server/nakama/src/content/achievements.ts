import file from "../../../../content/achievements/v1.json";

export type AchievementMetric =
  | "rounds"
  | "escapes"
  | "bhabhis"
  | "thullas_given"
  | "thullas_received"
  | "cards_picked_up"
  | "biggest_pickup"
  | "hands_taken"
  | "escaped_first"
  | "clean_rounds"
  | "series_won"
  | "friends"
  | "rounds_with_friend"
  | "rounds_with_four_friends"
  | "same_group_rounds"
  | "first_escape_streak"
  | "no_bhabhi_streak"
  | "houdini"
  | "cosmetics_owned"
  | "seasonal_rounds";

export interface AchievementDef {
  readonly id: string;
  readonly name: string;
  readonly description: string;
  readonly category: "play" | "social" | "skill" | "collection" | "seasonal";
  readonly metric: AchievementMetric;
  readonly target: number;
  readonly reward: { readonly gems?: number; readonly title?: string };
}

export interface AchievementList {
  readonly id: string;
  readonly version: number;
  readonly achievements: readonly AchievementDef[];
}

/** The launch set from content (GDD 12.7). */
export const BUNDLED_ACHIEVEMENTS: AchievementList = {
  id: file.id,
  version: file.version,
  achievements: file.achievements as AchievementDef[],
};
