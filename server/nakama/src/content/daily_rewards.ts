import file from "../../../../content/daily-rewards/v1.json";

export interface DailyRewardDay {
  readonly day: number;
  readonly coins: number;
  readonly gems?: number;
}

export interface DailyRewardCalendar {
  readonly id: string;
  readonly version: number;
  readonly days: readonly DailyRewardDay[];
}

/** The 7-day cycle from content (GDD 12.6). */
export const BUNDLED_DAILY_REWARDS: DailyRewardCalendar = {
  id: file.id,
  version: file.version,
  days: file.days,
};
