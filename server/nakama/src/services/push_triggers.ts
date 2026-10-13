import { CALENDAR, dailyRewardView, nkDailyRewardStore } from "./daily_reward";
import { nkFcmSender } from "./fcm";
import { nkAreFriends, nkProfileStore } from "./profiles";
import { localDay } from "./progression";
import {
  DAILY_REWARD_PUSH_HOUR,
  FRIEND_ONLINE_THROTTLE_MS,
  SEASON_END_WINDOW_MS,
  inQuietHours,
  localHour,
  nkPushStore,
  pruneDevices,
  pushTo,
  recordTz,
  type PushReport,
  type PushSender,
  type PushStore,
} from "./push";
import { BUNDLED_SEASONS, currentSeason } from "./season_pass";
import { nkSocialStore } from "./social";

/**
 * What sends a push (GDD 10.10): a friend request, a favourite coming online, and the hourly
 * sweep for the daily reward and the season's last day. Table invites live in `rpc/push.ts`.
 */
export interface PushPorts {
  readonly store: PushStore;
  readonly sender: PushSender;
}

export function nkPushPorts(
  nk: nkruntime.Nakama,
  logger: nkruntime.Logger,
  env: Readonly<Record<string, string | undefined>>,
): PushPorts {
  return { store: nkPushStore(nk), sender: nkFcmSender(nk, logger, env) };
}

/** The name a push shows for a player: the profile's display name, else the Nakama username. */
export function displayNameOf(
  nk: nkruntime.Nakama,
  logger: nkruntime.Logger,
  userId: string,
): string {
  const profile = nkProfileStore(nk, logger).read(userId);
  if (profile !== null && profile.displayName !== "") return profile.displayName;
  try {
    return nk.usersGetId([userId])[0]?.username ?? "";
  } catch {
    return "";
  }
}

/** A request that is not yet mutual gets a push; an accept is Nakama's own in-app notice only. */
export function notifyFriendRequest(
  nk: nkruntime.Nakama,
  logger: nkruntime.Logger,
  env: Readonly<Record<string, string | undefined>>,
  fromUserId: string,
  toUserId: string,
  now: number,
): PushReport | null {
  if (fromUserId === toUserId || nkAreFriends(nk, fromUserId, toUserId)) return null;
  const ports = nkPushPorts(nk, logger, env);
  return pushTo(
    ports.store,
    ports.sender,
    toUserId,
    {
      type: "friend_request",
      args: [displayNameOf(nk, logger, fromUserId)],
      data: { fromUserId },
    },
    now,
  );
}

const WATCHERS_MAX = 100;

/**
 * "Friend came online", favourites only and off by default (GDD 10.10): the players who pinned
 * this one get a push, at most once an hour per friend; a sign-in within ten minutes of the last
 * is a token refresh, not an arrival.
 */
export function notifyFavouriteOnline(
  nk: nkruntime.Nakama,
  logger: nkruntime.Logger,
  env: Readonly<Record<string, string | undefined>>,
  userId: string,
  now: number,
  recentlySeen: boolean,
): number {
  if (recentlySeen) return 0;
  const watchers = nkSocialStore(nk).favouritedBy(userId).slice(0, WATCHERS_MAX);
  if (watchers.length === 0) return 0;
  const ports = nkPushPorts(nk, logger, env);
  const key = `online:${userId}`;
  let name: string | null = null;
  let sent = 0;
  for (const watcher of watchers) {
    const record = ports.store.read(watcher);
    if (!record.settings.types.friend_online) continue;
    const last = Number(record.sent[key] ?? "0");
    if (now - last < FRIEND_ONLINE_THROTTLE_MS) continue;
    name ??= displayNameOf(nk, logger, userId);
    const report = pushTo(
      ports.store,
      ports.sender,
      watcher,
      { type: "friend_online", args: [name], data: { fromUserId: userId } },
      now,
    );
    if (report.skipped === "no_devices" || report.skipped === "off") continue;
    sent += report.sent;
    const after = ports.store.read(watcher);
    ports.store.write(watcher, {
      ...after,
      sent: { ...after.sent, [key]: String(now) },
      updatedTs: now,
    });
  }
  return sent;
}

export interface SweepResult {
  readonly scanned: number;
  readonly dailyReward: number;
  readonly seasonEnd: number;
  readonly pruned: number;
  readonly configured: boolean;
}

const PAGE = 100;

/**
 * The hourly sweep the cron sidecar calls: the daily reward push once a day after 09:00 local
 * while today is unclaimed, the season-end push once in the season's last 24 hours, and the
 * pruning of stale tokens. Quiet hours are per device.
 */
export function pushSweep(
  nk: nkruntime.Nakama,
  logger: nkruntime.Logger,
  env: Readonly<Record<string, string | undefined>>,
  now: number,
  limit = 2000,
): SweepResult {
  const ports = nkPushPorts(nk, logger, env);
  const season = currentSeason(BUNDLED_SEASONS, now);
  const seasonEnding =
    season !== null && season.endTs > now && season.endTs - now <= SEASON_END_WINDOW_MS
      ? season
      : null;
  let scanned = 0;
  let dailyReward = 0;
  let seasonEnd = 0;
  let pruned = 0;
  let cursor: string | undefined;
  for (let page = 0; page < Math.ceil(limit / PAGE); page += 1) {
    const list = ports.store.list(PAGE, cursor);
    for (const { userId, record } of list.entries) {
      scanned += 1;
      const kept = pruneDevices(record, now);
      if (kept !== record) {
        pruned += Object.keys(record.devices).length - Object.keys(kept.devices).length;
        ports.store.write(userId, kept);
      }
      if (Object.keys(kept.devices).length === 0) continue;
      const tz = recordTz(kept);
      const quiet = inQuietHours(now, tz);
      const day = localDay(now, tz);
      if (
        !quiet &&
        kept.settings.types.daily_reward &&
        localHour(now, tz) >= DAILY_REWARD_PUSH_HOUR &&
        kept.sent["daily_reward"] !== day &&
        dailyRewardView(nkDailyRewardStore(nk).read(userId), CALENDAR, now).claimable
      ) {
        const report = pushTo(
          ports.store,
          ports.sender,
          userId,
          { type: "daily_reward", args: [], data: {} },
          now,
        );
        dailyReward += report.sent;
        mark(ports.store, userId, "daily_reward", day, now);
      }
      if (
        !quiet &&
        seasonEnding !== null &&
        kept.settings.types.season_end &&
        kept.sent["season_end"] !== seasonEnding.id
      ) {
        const hoursLeft = Math.max(1, Math.ceil((seasonEnding.endTs - now) / 3_600_000));
        const report = pushTo(
          ports.store,
          ports.sender,
          userId,
          { type: "season_end", args: [String(hoursLeft)], data: { seasonId: seasonEnding.id } },
          now,
        );
        seasonEnd += report.sent;
        mark(ports.store, userId, "season_end", seasonEnding.id, now);
      }
    }
    if (
      list.entries.length < PAGE ||
      list.cursor === undefined ||
      list.cursor === "" ||
      list.cursor === cursor
    ) {
      break;
    }
    cursor = list.cursor;
  }
  return { scanned, dailyReward, seasonEnd, pruned, configured: ports.sender.configured };
}

function mark(store: PushStore, userId: string, key: string, value: string, now: number): void {
  const record = store.read(userId);
  store.write(userId, { ...record, sent: { ...record.sent, [key]: value }, updatedTs: now });
}
