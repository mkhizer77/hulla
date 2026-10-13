import {
  BUNDLED_ACHIEVEMENTS,
  type AchievementDef,
  type AchievementList,
  type AchievementMetric,
} from "../content/achievements";

import { credit, type LedgerPorts } from "./ledger";
import { seriesWon } from "./missions";
import { nkProgressStore } from "./progression";

import type { RoundRecord } from "../match/types";

/**
 * Achievements (GDD 12.7): lifetime counters and streaks kept per player, evaluated against the
 * content list at round end (and when friends change), paid once through the ledger with the
 * achievement id as the key; a title reward lands in the player's titles.
 */
export const ACHIEVEMENTS_COLLECTION = "achievements";
export const ACHIEVEMENT_LIST_COLLECTION = "content_achievements";
export const SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000";
export const ACHIEVEMENT_NOTIFICATION_CODE = 1003;
export const GROUP_CAP = 50;
export const FOUR_FRIENDS = 4;

export type Counters = Partial<Record<AchievementMetric, number>>;

export interface AchievementRecord {
  readonly counters: Counters;
  readonly unlocked: Readonly<Record<string, number>>;
  /** Rounds played with each exact group of players, keyed by the sorted ids. */
  readonly groups: Readonly<Record<string, number>>;
  readonly updatedTs: number;
}

export const EMPTY_ACHIEVEMENTS: AchievementRecord = {
  counters: {},
  unlocked: {},
  groups: {},
  updatedTs: 0,
};

export interface RoundFacts {
  /** Friends among the other players at the table. */
  readonly friendsAtTable: number;
  readonly playerIds: readonly string[];
}

/** The counters after one seat of a completed round (GDD 12.7). */
export function countRound(
  record: AchievementRecord,
  round: RoundRecord,
  seat: number,
  facts: RoundFacts,
): AchievementRecord {
  const result = round.result;
  if (result === null || !round.completed) return record;
  const h = round.highlights[seat] ?? {};
  const bhabhi = result.loserSeat === seat;
  const escaped = !bhabhi && result.finishOrder.includes(seat);
  const first = result.finishOrder[0] === seat;
  const pickups = h["cardsPickedUp"] ?? 0;
  const c = record.counters;
  const n = (key: AchievementMetric): number => c[key] ?? 0;
  const groupKey = [...facts.playerIds].sort().join(",");
  let groups: Record<string, number> = { ...record.groups };
  if (facts.playerIds.length >= 3) {
    if (!(groupKey in groups) && Object.keys(groups).length >= GROUP_CAP) {
      const oldest = Object.entries(groups).sort((a, b) => a[1] - b[1])[0]?.[0];
      groups = Object.fromEntries(Object.entries(groups).filter(([key]) => key !== oldest));
    }
    groups[groupKey] = (groups[groupKey] ?? 0) + 1;
  }
  const counters: Counters = {
    ...c,
    rounds: n("rounds") + 1,
    escapes: n("escapes") + (escaped ? 1 : 0),
    bhabhis: n("bhabhis") + (bhabhi ? 1 : 0),
    thullas_given: n("thullas_given") + (h["thullasGiven"] ?? 0),
    thullas_received: n("thullas_received") + (h["thullasReceived"] ?? 0),
    cards_picked_up: n("cards_picked_up") + pickups,
    biggest_pickup: Math.max(n("biggest_pickup"), h["biggestPickup"] ?? 0),
    hands_taken: n("hands_taken") + (h["handsTaken"] ?? 0),
    escaped_first: n("escaped_first") + (first ? 1 : 0),
    clean_rounds: n("clean_rounds") + (escaped && pickups === 0 ? 1 : 0),
    series_won: n("series_won") + (seriesWon(round, seat) ? 1 : 0),
    rounds_with_friend: n("rounds_with_friend") + (facts.friendsAtTable > 0 ? 1 : 0),
    rounds_with_four_friends:
      n("rounds_with_four_friends") + (facts.friendsAtTable >= FOUR_FRIENDS ? 1 : 0),
    same_group_rounds: Math.max(n("same_group_rounds"), groups[groupKey] ?? 0),
    first_escape_streak: first ? n("first_escape_streak") + 1 : 0,
    no_bhabhi_streak: bhabhi ? 0 : n("no_bhabhi_streak") + 1,
    houdini: n("houdini") + (h["houdini"] ?? 0),
  };
  return { ...record, counters, groups, updatedTs: round.endedTs };
}

export function withCounter(
  record: AchievementRecord,
  metric: AchievementMetric,
  value: number,
  now: number,
): AchievementRecord {
  if ((record.counters[metric] ?? 0) === value) return record;
  return { ...record, counters: { ...record.counters, [metric]: value }, updatedTs: now };
}

export interface Unlock {
  readonly id: string;
  readonly gems: number;
  readonly title: string | null;
}

/** Every achievement whose target is now met and was not unlocked before. */
export function newlyUnlocked(record: AchievementRecord, list: AchievementList): Unlock[] {
  return list.achievements.flatMap((a) => {
    if (a.id in record.unlocked) return [];
    if ((record.counters[a.metric] ?? 0) < a.target) return [];
    return [{ id: a.id, gems: a.reward.gems ?? 0, title: a.reward.title ?? null }];
  });
}

export function markUnlocked(
  record: AchievementRecord,
  unlocks: readonly Unlock[],
  now: number,
): AchievementRecord {
  if (unlocks.length === 0) return record;
  const unlocked = { ...record.unlocked };
  for (const u of unlocks) unlocked[u.id] = now;
  return { ...record, unlocked, updatedTs: now };
}

export interface AchievementView {
  readonly id: string;
  readonly name: string;
  readonly description: string;
  readonly category: AchievementDef["category"];
  readonly metric: AchievementMetric;
  readonly target: number;
  readonly progress: number;
  readonly unlockedTs: number | null;
  readonly reward: AchievementDef["reward"];
}

export function achievementsView(
  record: AchievementRecord,
  list: AchievementList,
): AchievementView[] {
  return list.achievements.map((a) => ({
    id: a.id,
    name: a.name,
    description: a.description,
    category: a.category,
    metric: a.metric,
    target: a.target,
    progress: Math.min(a.target, record.counters[a.metric] ?? 0),
    unlockedTs: record.unlocked[a.id] ?? null,
    reward: a.reward,
  }));
}

export function isAchievementList(value: unknown): value is AchievementList {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  const list = v["achievements"];
  return (
    typeof v["id"] === "string" &&
    typeof v["version"] === "number" &&
    Array.isArray(list) &&
    list.length > 0 &&
    list.every((a: unknown) => {
      if (typeof a !== "object" || a === null) return false;
      const d = a as Record<string, unknown>;
      return (
        typeof d["id"] === "string" &&
        typeof d["metric"] === "string" &&
        typeof d["target"] === "number"
      );
    })
  );
}

export interface AchievementStore {
  read(userId: string): AchievementRecord;
  write(userId: string, record: AchievementRecord): void;
  list(): AchievementList;
  writeList(list: AchievementList): void;
}

function isRecord(value: unknown): value is AchievementRecord {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v["counters"] === "object" && typeof v["unlocked"] === "object";
}

export function nkAchievementStore(nk: nkruntime.Nakama): AchievementStore {
  return {
    read(userId) {
      const objects = nk.storageRead([
        { collection: ACHIEVEMENTS_COLLECTION, key: "state", userId },
      ]);
      const value: unknown = objects[0]?.value;
      return isRecord(value) ? { ...EMPTY_ACHIEVEMENTS, ...value } : EMPTY_ACHIEVEMENTS;
    },
    write(userId, record) {
      nk.storageWrite([
        {
          collection: ACHIEVEMENTS_COLLECTION,
          key: "state",
          userId,
          value: record,
          permissionRead: 1,
          permissionWrite: 0,
        },
      ]);
    },
    list() {
      try {
        const objects = nk.storageRead([
          { collection: ACHIEVEMENT_LIST_COLLECTION, key: "list", userId: SYSTEM_USER_ID },
        ]);
        const value: unknown = objects[0]?.value;
        return isAchievementList(value) ? value : BUNDLED_ACHIEVEMENTS;
      } catch {
        return BUNDLED_ACHIEVEMENTS;
      }
    },
    writeList(list) {
      nk.storageWrite([
        {
          collection: ACHIEVEMENT_LIST_COLLECTION,
          key: "list",
          userId: SYSTEM_USER_ID,
          value: { id: list.id, version: list.version, achievements: list.achievements },
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
  };
}

/** Pays and records fresh unlocks: gems through the ledger, titles into progress, one notice. */
export function settleUnlocks(
  nk: nkruntime.Nakama,
  ledger: LedgerPorts,
  store: AchievementStore,
  userId: string,
  record: AchievementRecord,
  list: AchievementList,
  now: number,
): Unlock[] {
  const unlocks = newlyUnlocked(record, list);
  if (unlocks.length === 0) {
    store.write(userId, record);
    return [];
  }
  store.write(userId, markUnlocked(record, unlocks, now));
  for (const u of unlocks) {
    if (u.gems > 0) {
      credit(ledger, {
        userId,
        sourceId: `achievement:${u.id}`,
        reason: "achievement",
        gems: u.gems,
        metadata: { achievementId: u.id },
      });
    }
  }
  const titles = unlocks.flatMap((u) => (u.title === null ? [] : [u.title]));
  if (titles.length > 0) {
    const progress = nkProgressStore(nk);
    const current = progress.read(userId);
    const fresh = titles.filter((t) => !current.titles.includes(t));
    if (fresh.length > 0) {
      progress.write(userId, { ...current, titles: [...current.titles, ...fresh], updatedTs: now });
    }
  }
  try {
    nk.notificationSend(
      userId,
      "achievement_unlocked",
      { unlocked: unlocks.map((u) => u.id) },
      ACHIEVEMENT_NOTIFICATION_CODE,
      null,
      true,
    );
  } catch {
    // the list shows the unlock on the next fetch
  }
  return unlocks;
}
