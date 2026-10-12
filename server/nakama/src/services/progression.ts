import type { RoundRecord } from "../match/types";

/**
 * XP, levels, titles and per-round coins (GDD 12.2, 12.3): computed on the server from the round
 * record, with the daily soft cap and the login streak kept per player in `progress`.
 */
export const PROGRESS_COLLECTION = "progress";
export const XP_BY_FINISH = [40, 30, 20] as const;
export const XP_OTHERS = 15;
export const XP_BHABHI = 10;
export const XP_PER_THULLA = 2;
export const XP_THULLA_CAP = 10;
export const XP_CLEAN_ROUND = 10;
export const FRIEND_BONUS = 0.2;
export const COINS_BY_FINISH = [60, 45, 35] as const;
export const COINS_OTHERS = 25;
export const COINS_BHABHI = 15;
export const DAILY_SOFT_CAP = 1_500;
export const CAPPED_RATE = 0.2;
export const STREAK_STEP = 0.1;
export const STREAK_MAX = 0.5;
/** Every level grants coins; the amount is an interim choice (OQ-021). */
export const COINS_PER_LEVEL = 50;
export const GEM_MILESTONES: Readonly<Record<number, number>> = {
  5: 10,
  10: 20,
  20: 30,
  30: 40,
  50: 60,
  75: 80,
  100: 100,
};
export const TITLES: readonly { readonly level: number; readonly id: string }[] = [
  { level: 1, id: "naya_khiladi" },
  { level: 10, id: "khiladi" },
  { level: 25, id: "purana_khiladi" },
  { level: 50, id: "ustad" },
  { level: 75, id: "shahenshah" },
  { level: 100, id: "thulla_master" },
];
export const MAX_LEVEL = 100;
export const DEFAULT_TZ_OFFSET_MINUTES = 300;
const DAY_MS = 24 * 60 * 60 * 1000;

/** XP needed to go from level n to n + 1 (GDD 12.2). */
export function xpToAdvance(level: number): number {
  return 100 + 25 * level;
}

export interface LevelInfo {
  readonly level: number;
  readonly intoLevel: number;
  readonly toNext: number;
}

export function levelFor(totalXp: number): LevelInfo {
  let level = 1;
  let remaining = Math.max(0, totalXp);
  while (level < MAX_LEVEL && remaining >= xpToAdvance(level)) {
    remaining -= xpToAdvance(level);
    level += 1;
  }
  return { level, intoLevel: remaining, toNext: level >= MAX_LEVEL ? 0 : xpToAdvance(level) };
}

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

/** The GDD tables applied to one seat of a completed round. */
export function roundReward(
  record: RoundRecord,
  seat: number,
  ctx: RewardContext,
): RoundReward | null {
  const result = record.result;
  if (result === null || !record.completed) return null;
  const bhabhi = result.loserSeat === seat;
  const finish = bhabhi ? null : result.finishOrder.indexOf(seat);
  const position = finish === null || finish < 0 ? null : finish;
  const baseXp = bhabhi
    ? XP_BHABHI
    : position !== null && position < XP_BY_FINISH.length
      ? (XP_BY_FINISH[position] ?? XP_OTHERS)
      : XP_OTHERS;
  const highlights = record.highlights[seat] ?? {};
  const thullaXp = Math.min(XP_THULLA_CAP, (highlights["thullasGiven"] ?? 0) * XP_PER_THULLA);
  const cleanXp = !bhabhi && (highlights["cardsPickedUp"] ?? 0) === 0 ? XP_CLEAN_ROUND : 0;
  const subtotal = baseXp + thullaXp + cleanXp;
  const friendXp = ctx.withFriend ? Math.round(subtotal * FRIEND_BONUS) : 0;
  const baseCoins = bhabhi
    ? COINS_BHABHI
    : position !== null && position < COINS_BY_FINISH.length
      ? (COINS_BY_FINISH[position] ?? COINS_OTHERS)
      : COINS_OTHERS;
  const streak = Math.min(STREAK_MAX, Math.max(0, ctx.loginStreak - 1) * STREAK_STEP);
  const streakCoins = Math.round(baseCoins * streak);
  const capped = ctx.coinsToday >= DAILY_SOFT_CAP;
  const coins = capped
    ? Math.round((baseCoins + streakCoins) * CAPPED_RATE)
    : baseCoins + streakCoins;
  return {
    xp: subtotal + friendXp,
    coins,
    breakdown: {
      finish: position,
      bhabhi,
      baseXp,
      thullaXp,
      cleanXp,
      friendXp,
      baseCoins,
      streakCoins,
      capped,
    },
  };
}

export interface Progress {
  readonly xp: number;
  readonly level: number;
  readonly coinsToday: number;
  /** The local day the daily counters belong to, as `YYYY-MM-DD`. */
  readonly day: string;
  readonly loginStreak: number;
  readonly lastLoginDay: string;
  readonly tzOffsetMinutes: number;
  readonly titles: readonly string[];
  readonly lastReward: LastReward | null;
  readonly updatedTs: number;
}

export interface LastReward {
  readonly tableId: string;
  readonly roundId: string;
  readonly xp: number;
  readonly coins: number;
  readonly levelFrom: number;
  readonly levelTo: number;
  readonly titles: readonly string[];
  readonly grants: readonly {
    readonly level: number;
    readonly coins: number;
    readonly gems: number;
  }[];
  readonly breakdown: RoundReward["breakdown"];
}

export const EMPTY_PROGRESS: Progress = {
  xp: 0,
  level: 1,
  coinsToday: 0,
  day: "",
  loginStreak: 0,
  lastLoginDay: "",
  tzOffsetMinutes: DEFAULT_TZ_OFFSET_MINUTES,
  titles: ["naya_khiladi"],
  lastReward: null,
  updatedTs: 0,
};

/** The player's local calendar day for a moment, from their stored offset. */
export function localDay(ts: number, tzOffsetMinutes: number): string {
  const shifted = new Date(ts + tzOffsetMinutes * 60_000);
  const y = shifted.getUTCFullYear();
  const m = String(shifted.getUTCMonth() + 1).padStart(2, "0");
  const d = String(shifted.getUTCDate()).padStart(2, "0");
  return `${String(y)}-${m}-${d}`;
}

function dayBefore(day: string, tzOffsetMinutes: number): string {
  const [y, m, d] = day.split("-").map(Number);
  const noonUtc = Date.UTC(y ?? 1970, (m ?? 1) - 1, d ?? 1, 12) - tzOffsetMinutes * 60_000;
  return localDay(noonUtc - DAY_MS, tzOffsetMinutes);
}

/** A sign-in: today extends a streak that ran yesterday, restarts one that lapsed. */
export function touchLogin(progress: Progress, now: number): Progress {
  const today = localDay(now, progress.tzOffsetMinutes);
  if (progress.lastLoginDay === today) return progress;
  const streak =
    progress.lastLoginDay === dayBefore(today, progress.tzOffsetMinutes)
      ? progress.loginStreak + 1
      : 1;
  return { ...progress, loginStreak: streak, lastLoginDay: today, updatedTs: now };
}

/** The daily counters roll over at local midnight. */
export function forDay(progress: Progress, now: number): Progress {
  const today = localDay(now, progress.tzOffsetMinutes);
  return progress.day === today ? progress : { ...progress, day: today, coinsToday: 0 };
}

export interface LevelGrant {
  readonly level: number;
  readonly coins: number;
  readonly gems: number;
  readonly title: string | null;
}

/** What each level gained between two XP totals grants (GDD 12.2). */
export function levelGrants(fromLevel: number, toLevel: number): LevelGrant[] {
  const grants: LevelGrant[] = [];
  for (let level = fromLevel + 1; level <= toLevel; level += 1) {
    grants.push({
      level,
      coins: COINS_PER_LEVEL,
      gems: GEM_MILESTONES[level] ?? 0,
      title: TITLES.find((t) => t.level === level)?.id ?? null,
    });
  }
  return grants;
}

export interface Applied {
  readonly progress: Progress;
  readonly grants: LevelGrant[];
  readonly reward: LastReward;
}

/** Applies a round's reward to the record: XP, level-ups, titles and today's coin count. */
export function applyReward(
  progress: Progress,
  reward: RoundReward,
  ids: { readonly tableId: string; readonly roundId: string },
  now: number,
): Applied {
  const today = forDay(progress, now);
  const xp = today.xp + reward.xp;
  const before = levelFor(today.xp).level;
  const after = levelFor(xp).level;
  const grants = levelGrants(before, after);
  const newTitles = grants.flatMap((g) =>
    g.title === null || today.titles.includes(g.title) ? [] : [g.title],
  );
  const lastReward: LastReward = {
    tableId: ids.tableId,
    roundId: ids.roundId,
    xp: reward.xp,
    coins: reward.coins,
    levelFrom: before,
    levelTo: after,
    titles: newTitles,
    grants: grants.map((g) => ({ level: g.level, coins: g.coins, gems: g.gems })),
    breakdown: reward.breakdown,
  };
  return {
    progress: {
      ...today,
      xp,
      level: after,
      coinsToday: today.coinsToday + reward.coins,
      titles: [...today.titles, ...newTitles],
      lastReward,
      updatedTs: now,
    },
    grants,
    reward: lastReward,
  };
}

export function isProgress(value: unknown): value is Progress {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v["xp"] === "number" && typeof v["level"] === "number" && typeof v["day"] === "string"
  );
}

export interface ProgressStore {
  read(userId: string): Progress;
  write(userId: string, progress: Progress): void;
}

export function nkProgressStore(nk: nkruntime.Nakama): ProgressStore {
  return {
    read(userId) {
      const objects = nk.storageRead([{ collection: PROGRESS_COLLECTION, key: "state", userId }]);
      const value: unknown = objects[0]?.value;
      return isProgress(value) ? { ...EMPTY_PROGRESS, ...value } : EMPTY_PROGRESS;
    },
    write(userId, progress) {
      nk.storageWrite([
        {
          collection: PROGRESS_COLLECTION,
          key: "state",
          userId,
          value: progress,
          permissionRead: 1,
          permissionWrite: 0,
        },
      ]);
    },
  };
}
