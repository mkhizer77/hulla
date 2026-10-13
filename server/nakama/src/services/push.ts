import { ErrorCodes, ruleError, type RuleError } from "@hulla/shared-ts";

import { DEFAULT_TZ_OFFSET_MINUTES } from "./progression";

/**
 * Push notifications (GDD 10.10, 19.3; ARCHITECTURE 8.1): device tokens, per-type settings and
 * quiet hours per account, and the delivery policy. Transport is FCM HTTP v1 (`fcm.ts`); this file
 * never touches the network. One storage object per account lives under the system user so the
 * hourly sweep can page every registration.
 */
export const PUSH_COLLECTION = "push";
export const PUSH_OAUTH_COLLECTION = "push_oauth";
export const INVITES_COLLECTION = "invites";
export const SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000";
/** A token not refreshed for this long is dropped (OQ-032). */
export const STALE_DEVICE_MS = 60 * 24 * 3600 * 1000;
export const MAX_DEVICES = 5;
export const MAX_FAILURES = 10;
export const QUIET_START_HOUR = 23;
export const QUIET_END_HOUR = 8;
/** The daily reward push goes out at the first sweep after this local hour. */
export const DAILY_REWARD_PUSH_HOUR = 9;
export const SEASON_END_WINDOW_MS = 24 * 3600 * 1000;
export const INVITE_CAP_PER_HOUR = 20;
export const INVITE_RECIPIENTS_MAX = 10;
export const FRIEND_ONLINE_THROTTLE_MS = 3600 * 1000;
/** A sign-in this soon after the last one is a token refresh, not "came online". */
export const STILL_ONLINE_MS = 10 * 60 * 1000;
export const TABLE_INVITE_NOTIFICATION_CODE = 1004;

export const PUSH_TYPES = [
  "table_invite",
  "friend_request",
  "friend_online",
  "daily_reward",
  "season_end",
] as const;
export type PushType = (typeof PUSH_TYPES)[number];
export type PushRoute = "lobby" | "friends" | "home" | "pass";
export const PLATFORMS = ["android", "ios"] as const;
export type Platform = (typeof PLATFORMS)[number];

export interface PushTypeSpec {
  readonly route: PushRoute;
  readonly defaultOn: boolean;
}

/** The notification table of GDD 10.10 (club game nights arrive with clubs in v1.2). */
export const PUSH_SPECS: Readonly<Record<PushType, PushTypeSpec>> = {
  table_invite: { route: "lobby", defaultOn: true },
  friend_request: { route: "friends", defaultOn: true },
  friend_online: { route: "friends", defaultOn: false },
  daily_reward: { route: "home", defaultOn: true },
  season_end: { route: "pass", defaultOn: true },
};

export interface PushDevice {
  readonly token: string;
  readonly platform: Platform;
  readonly locale: string;
  readonly tzOffsetMinutes: number;
  readonly updatedTs: number;
  readonly failures: number;
}

export interface PushSettings {
  readonly types: Readonly<Record<PushType, boolean>>;
  readonly quietHours: boolean;
  /** Off mutes table invites from players who are not friends (GDD 10.5). */
  readonly invitesFromNonFriends: boolean;
}

export interface PushRecord {
  /** Keyed by the client's install id; a token refresh replaces the entry. */
  readonly devices: Readonly<Record<string, PushDevice>>;
  readonly settings: PushSettings;
  /** Per-type throttles: the local day of the last daily reward push, the season id, online marks. */
  readonly sent: Readonly<Record<string, string>>;
  readonly updatedTs: number;
}

export const DEFAULT_PUSH_SETTINGS: PushSettings = {
  types: {
    table_invite: PUSH_SPECS.table_invite.defaultOn,
    friend_request: PUSH_SPECS.friend_request.defaultOn,
    friend_online: PUSH_SPECS.friend_online.defaultOn,
    daily_reward: PUSH_SPECS.daily_reward.defaultOn,
    season_end: PUSH_SPECS.season_end.defaultOn,
  },
  quietHours: true,
  invitesFromNonFriends: true,
};

export const EMPTY_PUSH_RECORD: PushRecord = {
  devices: {},
  settings: DEFAULT_PUSH_SETTINGS,
  sent: {},
  updatedTs: 0,
};

const INSTALL_ID = /^[A-Za-z0-9_-]{1,64}$/;
const TOKEN_MAX = 4096;
const LOCALE_MAX = 16;

export interface DeviceInput {
  readonly installId: string;
  readonly token: string;
  readonly platform: Platform;
  readonly locale: string;
  readonly tzOffsetMinutes: number;
}

export function validTz(value: unknown): value is number {
  return typeof value === "number" && Number.isInteger(value) && Math.abs(value) <= 14 * 60;
}

/** `{ installId, token, platform, locale?, tzOffsetMinutes? }` or null when anything is off. */
export function parseDevice(input: Readonly<Record<string, unknown>>): DeviceInput | null {
  const installId = input["installId"];
  const token = input["token"];
  const platform = input["platform"];
  if (typeof installId !== "string" || !INSTALL_ID.test(installId)) return null;
  if (typeof token !== "string" || token === "" || token.length > TOKEN_MAX) return null;
  if (platform !== "android" && platform !== "ios") return null;
  const locale = input["locale"];
  const tz = input["tzOffsetMinutes"];
  return {
    installId,
    token,
    platform,
    locale: typeof locale === "string" && locale.length <= LOCALE_MAX ? locale : "en",
    tzOffsetMinutes: validTz(tz) ? tz : DEFAULT_TZ_OFFSET_MINUTES,
  };
}

/** Registers or refreshes a device; the same token under another install id moves over. */
export function registerDevice(record: PushRecord, input: DeviceInput, now: number): PushRecord {
  const devices: Record<string, PushDevice> = {};
  for (const [id, device] of Object.entries(record.devices)) {
    if (id !== input.installId && device.token !== input.token) devices[id] = device;
  }
  devices[input.installId] = {
    token: input.token,
    platform: input.platform,
    locale: input.locale,
    tzOffsetMinutes: input.tzOffsetMinutes,
    updatedTs: now,
    failures: 0,
  };
  const ids = Object.keys(devices);
  if (ids.length <= MAX_DEVICES) return { ...record, devices, updatedTs: now };
  ids.sort((a, b) => (devices[a]?.updatedTs ?? 0) - (devices[b]?.updatedTs ?? 0));
  const kept: Record<string, PushDevice> = {};
  for (const id of ids.slice(ids.length - MAX_DEVICES)) {
    const device = devices[id];
    if (device !== undefined) kept[id] = device;
  }
  return { ...record, devices: kept, updatedTs: now };
}

export function unregisterDevice(record: PushRecord, installId: string, now: number): PushRecord {
  if (!(installId in record.devices)) return record;
  const devices: Record<string, PushDevice> = {};
  for (const [id, device] of Object.entries(record.devices)) {
    if (id !== installId) devices[id] = device;
  }
  return { ...record, devices, updatedTs: now };
}

/** Drops tokens that stopped refreshing or kept failing; the same object when nothing changes. */
export function pruneDevices(record: PushRecord, now: number): PushRecord {
  const devices: Record<string, PushDevice> = {};
  let dropped = 0;
  for (const [id, device] of Object.entries(record.devices)) {
    if (now - device.updatedTs > STALE_DEVICE_MS || device.failures >= MAX_FAILURES) dropped += 1;
    else devices[id] = device;
  }
  return dropped === 0 ? record : { ...record, devices, updatedTs: now };
}

export function isPushType(value: unknown): value is PushType {
  return typeof value === "string" && (PUSH_TYPES as readonly string[]).includes(value);
}

/** `{ types?: {type: bool}, quietHours?, invitesFromNonFriends? }`; an unknown key is refused. */
export function applySettings(
  record: PushRecord,
  patch: Readonly<Record<string, unknown>>,
  now: number,
):
  | { readonly ok: true; readonly record: PushRecord }
  | { readonly ok: false; readonly error: RuleError } {
  const invalid = (reason: string): { readonly ok: false; readonly error: RuleError } => ({
    ok: false,
    error: ruleError(ErrorCodes.table.invalidPayload, { reason }),
  });
  let types = { ...record.settings.types };
  const patchTypes = patch["types"];
  if (patchTypes !== undefined) {
    if (typeof patchTypes !== "object" || patchTypes === null || Array.isArray(patchTypes)) {
      return invalid("types");
    }
    for (const [key, value] of Object.entries(patchTypes as Record<string, unknown>)) {
      if (!isPushType(key) || typeof value !== "boolean") return invalid("types");
      types = { ...types, [key]: value };
    }
  }
  const quiet = patch["quietHours"];
  if (quiet !== undefined && typeof quiet !== "boolean") return invalid("quiet_hours");
  const nonFriends = patch["invitesFromNonFriends"];
  if (nonFriends !== undefined && typeof nonFriends !== "boolean") return invalid("invites");
  return {
    ok: true,
    record: {
      ...record,
      settings: {
        types,
        quietHours: typeof quiet === "boolean" ? quiet : record.settings.quietHours,
        invitesFromNonFriends:
          typeof nonFriends === "boolean" ? nonFriends : record.settings.invitesFromNonFriends,
      },
      updatedTs: now,
    },
  };
}

const DAY_MS = 24 * 3600 * 1000;

export function localHour(ts: number, tzOffsetMinutes: number): number {
  const shifted = ts + tzOffsetMinutes * 60_000;
  const inDay = ((shifted % DAY_MS) + DAY_MS) % DAY_MS;
  return Math.floor(inDay / 3_600_000);
}

/** 23:00 to 08:00 local (GDD 10.10). */
export function inQuietHours(ts: number, tzOffsetMinutes: number): boolean {
  const hour = localHour(ts, tzOffsetMinutes);
  return hour >= QUIET_START_HOUR || hour < QUIET_END_HOUR;
}

/** The account's time zone: its most recently refreshed device. */
export function recordTz(record: PushRecord): number {
  let best: PushDevice | null = null;
  for (const device of Object.values(record.devices)) {
    if (best === null || device.updatedTs > best.updatedTs) best = device;
  }
  return best === null ? DEFAULT_TZ_OFFSET_MINUTES : best.tzOffsetMinutes;
}

export interface PushMessage {
  readonly type: PushType;
  /** Arguments for the localised body (a display name, a room code, hours left). */
  readonly args: readonly string[];
  readonly data: Readonly<Record<string, string>>;
}

/**
 * The FCM v1 message: data for routing plus localisation keys for the text, so the device renders
 * the title and body from its own string table (no user-facing strings on the server). The keys
 * use underscores because Android string resource names cannot contain dots.
 */
export function fcmMessage(token: string, message: PushMessage): Record<string, unknown> {
  const titleKey = `push_${message.type}_title`;
  const bodyKey = `push_${message.type}_body`;
  const args = [...message.args];
  return {
    token,
    data: { ...message.data, type: message.type, route: PUSH_SPECS[message.type].route },
    android: {
      priority: "high",
      collapse_key: message.type,
      notification: {
        channel_id: "hulla",
        tag: message.type,
        title_loc_key: titleKey,
        body_loc_key: bodyKey,
        body_loc_args: args,
      },
    },
    apns: {
      headers: { "apns-priority": "10" },
      payload: {
        aps: {
          alert: { "title-loc-key": titleKey, "loc-key": bodyKey, "loc-args": args },
          sound: "default",
          "thread-id": message.type,
        },
      },
    },
  };
}

export type SendOutcome = "sent" | "invalid_token" | "failed" | "unconfigured";

export interface PushSender {
  readonly configured: boolean;
  send(token: string, message: PushMessage): SendOutcome;
}

export interface PushStore {
  read(userId: string): PushRecord;
  write(userId: string, record: PushRecord): void;
  list(
    limit: number,
    cursor?: string,
  ): {
    readonly entries: readonly { userId: string; record: PushRecord }[];
    readonly cursor?: string;
  };
}

export interface DeliverOptions {
  /** Table invites from friends ignore quiet hours (GDD 10.10). */
  readonly bypassQuiet?: boolean;
  /** The admin test ignores settings and quiet hours. */
  readonly force?: boolean;
}

export interface PushReport {
  readonly sent: number;
  readonly skipped: "off" | "quiet" | "no_devices" | null;
  readonly outcomes: Readonly<Record<string, SendOutcome | "quiet">>;
}

/** Sends one message to every device of an account under its settings; prunes dead tokens. */
export function pushTo(
  store: PushStore,
  sender: PushSender,
  userId: string,
  message: PushMessage,
  now: number,
  options: DeliverOptions = {},
): PushReport {
  const force = options.force === true;
  const original = store.read(userId);
  const record = pruneDevices(original, now);
  if (!force && !record.settings.types[message.type]) {
    if (record !== original) store.write(userId, record);
    return { sent: 0, skipped: "off", outcomes: {} };
  }
  const ids = Object.keys(record.devices);
  if (ids.length === 0) {
    if (record !== original) store.write(userId, record);
    return { sent: 0, skipped: "no_devices", outcomes: {} };
  }
  const outcomes: Record<string, SendOutcome | "quiet"> = {};
  const devices: Record<string, PushDevice> = {};
  let sent = 0;
  let changed = record !== original;
  for (const id of ids) {
    const device = record.devices[id];
    if (device === undefined) continue;
    const quiet =
      !force &&
      options.bypassQuiet !== true &&
      record.settings.quietHours &&
      inQuietHours(now, device.tzOffsetMinutes);
    if (quiet) {
      outcomes[id] = "quiet";
      devices[id] = device;
      continue;
    }
    const outcome = sender.send(device.token, message);
    outcomes[id] = outcome;
    if (outcome === "invalid_token") {
      changed = true;
      continue;
    }
    if (outcome === "sent") sent += 1;
    const failures = outcome === "failed" ? device.failures + 1 : 0;
    if (failures !== device.failures) changed = true;
    devices[id] = { ...device, failures };
  }
  if (changed) store.write(userId, { ...record, devices, updatedTs: now });
  const allQuiet = Object.values(outcomes).every((o) => o === "quiet");
  return { sent, skipped: allQuiet ? "quiet" : null, outcomes };
}

export interface InviteRate {
  readonly sentTs: readonly number[];
}

export const EMPTY_INVITE_RATE: InviteRate = { sentTs: [] };
const HOUR_MS = 3600 * 1000;

/** How many invites the sender may still send this hour (GDD 10.5: twenty an hour). */
export function inviteAllowance(rate: InviteRate, now: number): number {
  const recent = rate.sentTs.filter((ts) => now - ts < HOUR_MS).length;
  return Math.max(0, INVITE_CAP_PER_HOUR - recent);
}

export function noteInvites(rate: InviteRate, now: number, count: number): InviteRate {
  const kept = rate.sentTs.filter((ts) => now - ts < HOUR_MS);
  for (let i = 0; i < count; i += 1) kept.push(now);
  return { sentTs: kept };
}

function isDevice(value: unknown): value is PushDevice {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v["token"] === "string" &&
    (v["platform"] === "android" || v["platform"] === "ios") &&
    typeof v["updatedTs"] === "number"
  );
}

/** Reads a stored record, filling defaults for settings added after it was written. */
export function normaliseRecord(value: unknown): PushRecord {
  if (typeof value !== "object" || value === null) return EMPTY_PUSH_RECORD;
  const v = value as Record<string, unknown>;
  const devices: Record<string, PushDevice> = {};
  const rawDevices = v["devices"];
  if (typeof rawDevices === "object" && rawDevices !== null) {
    for (const [id, device] of Object.entries(rawDevices as Record<string, unknown>)) {
      if (isDevice(device)) {
        devices[id] = {
          token: device.token,
          platform: device.platform,
          locale: typeof device.locale === "string" ? device.locale : "en",
          tzOffsetMinutes: validTz(device.tzOffsetMinutes)
            ? device.tzOffsetMinutes
            : DEFAULT_TZ_OFFSET_MINUTES,
          updatedTs: device.updatedTs,
          failures: typeof device.failures === "number" ? device.failures : 0,
        };
      }
    }
  }
  const rawSettings = (v["settings"] ?? {}) as Record<string, unknown>;
  const rawTypes = (rawSettings["types"] ?? {}) as Record<string, unknown>;
  const types = { ...DEFAULT_PUSH_SETTINGS.types };
  for (const type of PUSH_TYPES) {
    const flag = rawTypes[type];
    if (typeof flag === "boolean") types[type] = flag;
  }
  const sent: Record<string, string> = {};
  const rawSent = v["sent"];
  if (typeof rawSent === "object" && rawSent !== null) {
    for (const [key, mark] of Object.entries(rawSent as Record<string, unknown>)) {
      if (typeof mark === "string") sent[key] = mark;
    }
  }
  return {
    devices,
    settings: {
      types,
      quietHours:
        typeof rawSettings["quietHours"] === "boolean"
          ? rawSettings["quietHours"]
          : DEFAULT_PUSH_SETTINGS.quietHours,
      invitesFromNonFriends:
        typeof rawSettings["invitesFromNonFriends"] === "boolean"
          ? rawSettings["invitesFromNonFriends"]
          : DEFAULT_PUSH_SETTINGS.invitesFromNonFriends,
    },
    sent,
    updatedTs: typeof v["updatedTs"] === "number" ? v["updatedTs"] : 0,
  };
}

export function nkPushStore(nk: nkruntime.Nakama): PushStore {
  return {
    read(userId) {
      const objects = nk.storageRead([
        { collection: PUSH_COLLECTION, key: userId, userId: SYSTEM_USER_ID },
      ]);
      return normaliseRecord(objects[0]?.value);
    },
    write(userId, record) {
      nk.storageWrite([
        {
          collection: PUSH_COLLECTION,
          key: userId,
          userId: SYSTEM_USER_ID,
          value: {
            devices: record.devices,
            settings: record.settings,
            sent: record.sent,
            updatedTs: record.updatedTs,
          },
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
    list(limit, cursor) {
      const page = nk.storageList(SYSTEM_USER_ID, PUSH_COLLECTION, limit, cursor);
      const entries = (page.objects ?? []).map((object) => ({
        userId: object.key,
        record: normaliseRecord(object.value),
      }));
      return page.cursor === undefined || page.cursor === ""
        ? { entries }
        : { entries, cursor: page.cursor };
    },
  };
}

export interface InviteRateStore {
  read(userId: string): InviteRate;
  write(userId: string, rate: InviteRate): void;
}

export function nkInviteRateStore(nk: nkruntime.Nakama): InviteRateStore {
  return {
    read(userId) {
      const objects = nk.storageRead([{ collection: INVITES_COLLECTION, key: "rate", userId }]);
      const value = objects[0]?.value as { sentTs?: unknown } | undefined;
      const list = Array.isArray(value?.sentTs) ? (value.sentTs as unknown[]) : [];
      return { sentTs: list.filter((ts): ts is number => typeof ts === "number") };
    },
    write(userId, rate) {
      nk.storageWrite([
        {
          collection: INVITES_COLLECTION,
          key: "rate",
          userId,
          value: { sentTs: [...rate.sentTs] },
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
  };
}
