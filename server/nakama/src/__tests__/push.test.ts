import { readFileSync } from "node:fs";
import { join } from "node:path";

import { stateFromSeed, Xoshiro128StarStar } from "@hulla/shared-ts";
import { afterEach, describe, expect, it, vi } from "vitest";

import { afterAddFriends } from "../hooks/after_add_friends";
import { afterAuthenticateDevice } from "../hooks/after_authenticate";
import { rpcDailyRewardClaim } from "../rpc/daily_reward";
import {
  rpcAdminPushSweep,
  rpcAdminPushTest,
  rpcPushRegister,
  rpcPushSettingsGet,
  rpcPushSettingsSet,
  rpcPushUnregister,
  rpcTableInvite,
} from "../rpc/push";
import { rpcFavouriteSet, rpcFriendAddByCode } from "../rpc/social";
import { rpcTableCreate } from "../rpc/table_create";
import { nkBlockStore } from "../services/blocks";
import {
  FCM_CLIENT_EMAIL_ENV,
  FCM_PRIVATE_KEY_ENV,
  FCM_PROJECT_ENV,
  sendUrl,
} from "../services/fcm";
import { nkPresenceStore, seatTaken } from "../services/presence";
import { bootstrapProfile, nkProfileStore } from "../services/profiles";
import {
  DEFAULT_PUSH_SETTINGS,
  EMPTY_INVITE_RATE,
  EMPTY_PUSH_RECORD,
  INVITE_CAP_PER_HOUR,
  MAX_DEVICES,
  MAX_FAILURES,
  PUSH_SPECS,
  PUSH_TYPES,
  STALE_DEVICE_MS,
  applySettings,
  fcmMessage,
  inQuietHours,
  inviteAllowance,
  localHour,
  noteInvites,
  normaliseRecord,
  parseDevice,
  pruneDevices,
  pushTo,
  registerDevice,
  unregisterDevice,
  type PushRecord,
  type PushSender,
  type PushStore,
  type SendOutcome,
} from "../services/push";
import { pushSweep } from "../services/push_triggers";
import { BUNDLED_SEASONS } from "../services/season_pass";

import { fakeLogger, memoryNk, type MemoryNk } from "./fake-nk";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const SYSTEM = "00000000-0000-0000-0000-000000000000";
const ENV = {
  [FCM_PROJECT_ENV]: "hulla-test",
  [FCM_CLIENT_EMAIL_ENV]: "push@hulla-test.iam",
  [FCM_PRIVATE_KEY_ENV]: "PEM",
  HULLA_REGION: "test",
};
const ctxFor = (userId: string, env: Record<string, string> = ENV): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env }) as unknown as nkruntime.Context;
const ADMIN = { env: ENV } as unknown as nkruntime.Context;
/** 2026-10-13 05:00 UTC: 10:00 in Pakistan (UTC+5), a working hour. */
const T_DAY = Date.UTC(2026, 9, 13, 5, 0, 0);
/** 18:30 UTC: 23:30 in Pakistan, quiet hours. */
const T_NIGHT = Date.UTC(2026, 9, 13, 18, 30, 0);
const PKT = 300;

const device = (installId: string, token = `tok-${installId}`, tz = PKT): Json => ({
  installId,
  token,
  platform: "android",
  locale: "ur",
  tzOffsetMinutes: tz,
});

function register(m: MemoryNk, userId: string, installId = `${userId}-phone`, tz = PKT): void {
  const out = parse(
    rpcPushRegister(
      ctxFor(userId),
      fakeLogger(),
      m.nk,
      JSON.stringify(device(installId, undefined, tz)),
    ),
  );
  expect(out).toMatchObject({ ok: true });
}

/** The pushes sent through the fake FCM, by type. */
function pushed(m: MemoryNk): { token: string; type: string; route: string; args: string[] }[] {
  return m.requests
    .filter((r) => r.url === sendUrl("hulla-test"))
    .map((r) => {
      const body = JSON.parse(r.body) as {
        message: {
          token: string;
          data: Record<string, string>;
          android: { notification: { body_loc_args: string[] } };
        };
      };
      return {
        token: body.message.token,
        type: body.message.data["type"] ?? "",
        route: body.message.data["route"] ?? "",
        args: body.message.android.notification.body_loc_args,
      };
    });
}

function befriend(m: MemoryNk, a: string, b: string): void {
  m.nk.friendsAdd(a, "", [b], [], {});
  m.nk.friendsAdd(b, "", [a], [], {});
}

function memoryStore(
  initial: Record<string, PushRecord> = {},
): PushStore & { records: Map<string, PushRecord> } {
  const records = new Map<string, PushRecord>(Object.entries(initial));
  return {
    records,
    read: (u) => records.get(u) ?? EMPTY_PUSH_RECORD,
    write: (u, r) => void records.set(u, r),
    list: () => ({
      entries: [...records.entries()].map(([userId, record]) => ({ userId, record })),
    }),
  };
}

function sender(outcomes: Record<string, SendOutcome> = {}): PushSender & { sent: string[] } {
  const sent: string[] = [];
  return {
    configured: true,
    sent,
    send(token) {
      sent.push(token);
      return outcomes[token] ?? "sent";
    },
  };
}

afterEach(() => {
  vi.useRealTimers();
});

describe("push records, settings and quiet hours (GDD 10.10)", () => {
  it("matches the protocol's push types and routes", () => {
    const schema = JSON.parse(
      readFileSync(
        join(
          import.meta.dirname,
          "..",
          "..",
          "..",
          "..",
          "packages",
          "protocol",
          "schemas",
          "shell",
          "notifications.json",
        ),
        "utf8",
      ),
    ) as { definitions: { pushType: { enum: string[] }; pushRoute: { enum: string[] } } };
    expect([...PUSH_TYPES]).toEqual(schema.definitions.pushType.enum);
    for (const type of PUSH_TYPES) {
      expect(schema.definitions.pushRoute.enum).toContain(PUSH_SPECS[type].route);
    }
    expect(DEFAULT_PUSH_SETTINGS.types.friend_online).toBe(false);
    expect(DEFAULT_PUSH_SETTINGS.types.table_invite).toBe(true);
  });

  it("validates a device, moves a token between install ids, keeps five and forgets on request", () => {
    expect(parseDevice({ installId: "bad id", token: "t", platform: "android" })).toBeNull();
    expect(parseDevice({ installId: "a", token: "", platform: "android" })).toBeNull();
    expect(parseDevice({ installId: "a", token: "t", platform: "web" })).toBeNull();
    const input = parseDevice({
      installId: "a",
      token: "t",
      platform: "ios",
      tzOffsetMinutes: 99_999,
    });
    expect(input).toMatchObject({ locale: "en", tzOffsetMinutes: 300 });
    let record = registerDevice(EMPTY_PUSH_RECORD, input!, T_DAY + 1);
    record = registerDevice(record, { ...input!, installId: "b", token: "t2" }, T_DAY + 2);
    expect(Object.keys(record.devices)).toEqual(["a", "b"]);
    record = registerDevice(record, { ...input!, installId: "c", token: "t" }, T_DAY + 3);
    expect(Object.keys(record.devices)).toEqual(["b", "c"]);
    for (let i = 0; i < MAX_DEVICES + 2; i += 1) {
      record = registerDevice(
        record,
        { ...input!, installId: `d${String(i)}`, token: `x${String(i)}` },
        T_DAY + 10 + i,
      );
    }
    expect(Object.keys(record.devices)).toHaveLength(MAX_DEVICES);
    expect(record.devices["b"]).toBeUndefined();
    expect(unregisterDevice(record, "nope", 99)).toBe(record);
    expect(Object.keys(unregisterDevice(record, "d6", 99).devices)).toHaveLength(MAX_DEVICES - 1);
    const stale = {
      ...record,
      devices: {
        ...record.devices,
        old: { ...record.devices["d6"]!, updatedTs: 0 },
        dead: { ...record.devices["d6"]!, failures: MAX_FAILURES },
      },
    };
    const pruned = pruneDevices(stale, T_DAY + STALE_DEVICE_MS);
    expect(Object.keys(pruned.devices)).toEqual(Object.keys(record.devices));
    expect(pruneDevices(record, T_DAY + 100)).toBe(record);
    expect(
      normaliseRecord({
        devices: { a: { token: "t", platform: "ios", updatedTs: 1 } },
        settings: { types: { daily_reward: false } },
      }),
    ).toMatchObject({
      devices: { a: { token: "t", locale: "en", tzOffsetMinutes: 300, failures: 0 } },
      settings: { types: { daily_reward: false, table_invite: true }, quietHours: true },
    });
  });

  it("applies settings patches and refuses unknown types", () => {
    const on = applySettings(
      EMPTY_PUSH_RECORD,
      { types: { friend_online: true }, quietHours: false },
      5,
    );
    expect(on).toMatchObject({
      ok: true,
      record: {
        settings: {
          types: { friend_online: true },
          quietHours: false,
          invitesFromNonFriends: true,
        },
        updatedTs: 5,
      },
    });
    expect(applySettings(EMPTY_PUSH_RECORD, { types: { club: true } }, 5)).toMatchObject({
      ok: false,
      error: { detail: { reason: "types" } },
    });
    expect(applySettings(EMPTY_PUSH_RECORD, { types: { daily_reward: "no" } }, 5)).toMatchObject({
      ok: false,
    });
    expect(applySettings(EMPTY_PUSH_RECORD, { quietHours: 1 }, 5)).toMatchObject({ ok: false });
    expect(applySettings(EMPTY_PUSH_RECORD, { invitesFromNonFriends: false }, 5)).toMatchObject({
      ok: true,
      record: { settings: { invitesFromNonFriends: false } },
    });
  });

  it("knows the local hour and the 23:00 to 08:00 quiet window", () => {
    expect(localHour(T_DAY, PKT)).toBe(10);
    expect(localHour(T_NIGHT, PKT)).toBe(23);
    expect(localHour(T_DAY, -300)).toBe(0);
    expect(inQuietHours(T_NIGHT, PKT)).toBe(true);
    expect(inQuietHours(T_DAY, PKT)).toBe(false);
    expect(inQuietHours(Date.UTC(2026, 9, 13, 2, 59), PKT)).toBe(true);
    expect(inQuietHours(Date.UTC(2026, 9, 13, 3, 0), PKT)).toBe(false);
    expect(inQuietHours(Date.UTC(2026, 9, 13, 17, 59), PKT)).toBe(false);
  });

  it("builds an FCM message with localisation keys and string data for both platforms", () => {
    const message = fcmMessage("tok", {
      type: "table_invite",
      args: ["Ali", "7KQ2M"],
      data: { code: "7KQ2M" },
    });
    expect(message).toMatchObject({
      token: "tok",
      data: { type: "table_invite", route: "lobby", code: "7KQ2M" },
      android: {
        priority: "high",
        notification: {
          title_loc_key: "push_table_invite_title",
          body_loc_key: "push_table_invite_body",
          body_loc_args: ["Ali", "7KQ2M"],
        },
      },
      apns: {
        payload: {
          aps: { alert: { "loc-key": "push_table_invite_body", "loc-args": ["Ali", "7KQ2M"] } },
        },
      },
    });
  });

  it("delivers under the settings: type off, quiet per device with the friend bypass, dead tokens pruned", () => {
    const input = parseDevice(device("a"))!;
    const night = { ...input, installId: "b", token: "tok-b", tzOffsetMinutes: PKT };
    const day = { ...input, installId: "c", token: "tok-c", tzOffsetMinutes: -600 };
    let record = registerDevice(
      registerDevice(EMPTY_PUSH_RECORD, night, T_NIGHT - 2),
      day,
      T_NIGHT - 1,
    );
    const store = memoryStore({ u1: record });
    const s = sender({ "tok-c": "invalid_token" });
    const message = { type: "daily_reward" as const, args: [], data: {} };
    expect(pushTo(store, s, "u1", message, T_NIGHT)).toEqual({
      sent: 0,
      skipped: null,
      outcomes: { b: "quiet", c: "invalid_token" },
    });
    expect(Object.keys(store.read("u1").devices)).toEqual(["b"]);
    expect(pushTo(store, s, "u1", message, T_NIGHT)).toEqual({
      sent: 0,
      skipped: "quiet",
      outcomes: { b: "quiet" },
    });
    expect(
      pushTo(store, s, "u1", { ...message, type: "table_invite" }, T_NIGHT, { bypassQuiet: true }),
    ).toMatchObject({ sent: 1 });
    record = applySettings(store.read("u1"), { types: { daily_reward: false } }, 3).ok
      ? (
          applySettings(store.read("u1"), { types: { daily_reward: false } }, 3) as {
            record: PushRecord;
          }
        ).record
      : record;
    store.write("u1", record);
    expect(pushTo(store, s, "u1", message, T_DAY)).toEqual({
      sent: 0,
      skipped: "off",
      outcomes: {},
    });
    expect(pushTo(store, s, "u1", message, T_DAY, { force: true })).toMatchObject({ sent: 1 });
    expect(pushTo(store, s, "nobody", message, T_DAY)).toEqual({
      sent: 0,
      skipped: "no_devices",
      outcomes: {},
    });
    const flaky = sender({ "tok-b": "failed" });
    for (let i = 0; i < MAX_FAILURES; i += 1)
      pushTo(store, flaky, "u1", message, T_DAY + i, { force: true });
    expect(store.read("u1").devices["b"]?.failures).toBe(MAX_FAILURES);
    expect(pushTo(store, flaky, "u1", message, T_DAY + 50, { force: true })).toMatchObject({
      skipped: "no_devices",
    });
  });

  it("allows twenty invites an hour and rolls the window", () => {
    expect(inviteAllowance(EMPTY_INVITE_RATE, T_DAY)).toBe(INVITE_CAP_PER_HOUR);
    const full = noteInvites(EMPTY_INVITE_RATE, T_DAY, INVITE_CAP_PER_HOUR);
    expect(inviteAllowance(full, T_DAY + 1)).toBe(0);
    expect(inviteAllowance(full, T_DAY + 3_600_001)).toBe(INVITE_CAP_PER_HOUR);
    expect(noteInvites(full, T_DAY + 3_600_001, 1).sentTs).toHaveLength(1);
  });
});

describe("the push RPCs, hooks and sweep against an in-memory Nakama", () => {
  it("registers devices under the system user, serves and edits settings, and forgets a device", () => {
    const m = memoryNk();
    const log = fakeLogger();
    expect(parse(rpcPushRegister(ctxFor(""), log, m.nk, "{}"))).toMatchObject({
      error: { code: "table.not_authorised" },
    });
    expect(
      parse(rpcPushRegister(ctxFor("u1"), log, m.nk, JSON.stringify({ installId: "x" }))),
    ).toMatchObject({ error: { detail: { reason: "device" } } });
    register(m, "u1", "phone");
    register(m, "u1", "tablet");
    expect(m.object("push", "u1", SYSTEM)).toMatchObject({
      devices: { phone: { token: "tok-phone" }, tablet: {} },
    });
    expect(parse(rpcPushSettingsGet(ctxFor("u1"), log, m.nk))).toEqual({
      ok: true,
      settings: DEFAULT_PUSH_SETTINGS,
    });
    expect(
      parse(
        rpcPushSettingsSet(
          ctxFor("u1"),
          log,
          m.nk,
          JSON.stringify({ types: { season_end: false } }),
        ),
      ),
    ).toMatchObject({ ok: true, settings: { types: { season_end: false } } });
    expect(
      parse(
        rpcPushSettingsSet(ctxFor("u1"), log, m.nk, JSON.stringify({ types: { nope: false } })),
      ),
    ).toMatchObject({ ok: false });
    expect(
      parse(rpcPushUnregister(ctxFor("u1"), log, m.nk, JSON.stringify({ installId: "tablet" }))),
    ).toEqual({ ok: true, devices: 1 });
    expect(parse(rpcPushUnregister(ctxFor("u1"), log, m.nk, "{}"))).toMatchObject({ ok: false });
  });

  it("invites players to the caller's table: in-app notice, push, friend bypass, mutes, blocks and the cap", () => {
    vi.useFakeTimers();
    vi.setSystemTime(T_NIGHT);
    const m = memoryNk();
    const log = fakeLogger();
    const rng = Xoshiro128StarStar.fromState(stateFromSeed(new Uint8Array([1, 2, 3])));
    bootstrapProfile(nkProfileStore(m.nk, log), rng, "u1", "ali", T_DAY);
    nkProfileStore(m.nk, log).write("u1", {
      ...nkProfileStore(m.nk, log).read("u1")!,
      displayName: "Ali",
    });
    expect(
      parse(rpcTableInvite(ctxFor("u1"), log, m.nk, JSON.stringify({ userIds: ["u2"] }))),
    ).toMatchObject({ error: { code: "table.not_seated" } });
    const created = parse(
      rpcTableCreate(ctxFor("u1"), log, m.nk, JSON.stringify({ mode: "private" })),
    );
    const matchId = created["matchId"] as string;
    seatTaken(nkPresenceStore(m.nk), "u1", matchId, T_NIGHT);
    expect(
      parse(rpcTableInvite(ctxFor("u1"), log, m.nk, JSON.stringify({ userIds: [] }))),
    ).toMatchObject({ ok: false });
    befriend(m, "u1", "u2");
    for (const u of ["u2", "u3", "u4", "u5"]) register(m, u);
    rpcPushSettingsSet(ctxFor("u3"), log, m.nk, JSON.stringify({ invitesFromNonFriends: false }));
    nkBlockStore(m.nk).write("u1", { blocked: [], blockedBy: ["u4"] });
    const out = parse(
      rpcTableInvite(
        ctxFor("u1"),
        log,
        m.nk,
        JSON.stringify({ userIds: ["u2", "u3", "u4", "u5", "u1", "u2"] }),
      ),
    );
    expect(out).toEqual({
      ok: true,
      sent: ["u2", "u5"],
      skipped: { u3: "muted", u4: "blocked", u1: "self" },
      remaining: INVITE_CAP_PER_HOUR - 2,
    });
    const label = JSON.parse(m.nk.matchGet(matchId)!.label) as { code: string };
    expect(m.notifications).toEqual([
      {
        userId: "u2",
        subject: "table_invite",
        code: 1004,
        senderId: "u1",
        content: {
          matchId,
          code: label.code,
          gameId: "thulla",
          mode: "private",
          fromUserId: "u1",
          fromName: "Ali",
          openSeats: 4,
          sentTs: T_NIGHT,
        },
      },
      expect.objectContaining({ userId: "u5", subject: "table_invite" }),
    ]);
    // 23:30 local: the friend's push goes out, the stranger's waits for the morning.
    expect(pushed(m)).toEqual([
      { token: "tok-u2-phone", type: "table_invite", route: "lobby", args: ["Ali", label.code] },
    ]);
    expect(m.object("invites", "rate", "u1")).toMatchObject({ sentTs: [T_NIGHT, T_NIGHT] });
    const many = Array.from({ length: 10 }, (_, i) => `p${String(i)}`);
    expect(
      parse(rpcTableInvite(ctxFor("u1"), log, m.nk, JSON.stringify({ userIds: many }))),
    ).toMatchObject({ ok: true, remaining: INVITE_CAP_PER_HOUR - 12 });
    expect(
      parse(rpcTableInvite(ctxFor("u1"), log, m.nk, JSON.stringify({ userIds: many }))),
    ).toMatchObject({
      ok: true,
      sent: many.slice(0, 8),
      skipped: { p8: "rate_limited", p9: "rate_limited" },
      remaining: 0,
    });
    expect(
      parse(rpcTableInvite(ctxFor("u1"), log, m.nk, JSON.stringify({ userIds: ["u2"] }))),
    ).toMatchObject({ error: { code: "network.rate_limited" } });
    vi.setSystemTime(T_NIGHT + 3_600_001);
    expect(
      parse(rpcTableInvite(ctxFor("u1"), log, m.nk, JSON.stringify({ userIds: ["u2"] }))),
    ).toMatchObject({ ok: true, sent: ["u2"] });
    expect(
      parse(rpcTableInvite(ctxFor("u1"), log, m.nk, JSON.stringify({ userIds: [...many, "x"] }))),
    ).toMatchObject({ ok: false });
  });

  it("pushes a friend request from the client's AddFriends and from friend_add_by_code, never an accept", () => {
    vi.useFakeTimers();
    vi.setSystemTime(T_DAY);
    const m = memoryNk();
    const log = fakeLogger();
    register(m, "u2");
    register(m, "u3");
    m.user("u3", { username: "sara_k" });
    afterAddFriends(ctxFor("u1"), log, m.nk, undefined, { ids: ["u2"], usernames: ["sara_k"] });
    expect(pushed(m).map((p) => [p.token, p.type])).toEqual([
      ["tok-u2-phone", "friend_request"],
      ["tok-u3-phone", "friend_request"],
    ]);
    befriend(m, "u1", "u2");
    afterAddFriends(ctxFor("u1"), log, m.nk, undefined, { ids: ["u2"] });
    expect(pushed(m)).toHaveLength(2);
    afterAddFriends(ctxFor(""), log, m.nk, undefined, { ids: ["u2"] });
    expect(pushed(m)).toHaveLength(2);
    const rng = Xoshiro128StarStar.fromState(stateFromSeed(new Uint8Array([9])));
    const profile = bootstrapProfile(nkProfileStore(m.nk, log), rng, "u4", "u4", T_DAY);
    register(m, "u4");
    expect(
      parse(
        rpcFriendAddByCode(
          ctxFor("u1"),
          log,
          m.nk,
          JSON.stringify({ friendCode: profile.friendCode }),
        ),
      ),
    ).toMatchObject({ ok: true, userId: "u4" });
    expect(pushed(m).at(-1)).toMatchObject({
      token: "tok-u4-phone",
      type: "friend_request",
      route: "friends",
    });
  });

  it("tells the players who pinned a friend when they come online, opt-in and once an hour", () => {
    vi.useFakeTimers();
    vi.setSystemTime(T_DAY);
    const m = memoryNk();
    const log = fakeLogger();
    befriend(m, "u1", "u2");
    befriend(m, "u1", "u3");
    expect(
      parse(
        rpcFavouriteSet(ctxFor("u2"), log, m.nk, JSON.stringify({ userId: "u1", pinned: true })),
      ),
    ).toMatchObject({ ok: true });
    expect(
      parse(
        rpcFavouriteSet(ctxFor("u3"), log, m.nk, JSON.stringify({ userId: "u1", pinned: true })),
      ),
    ).toMatchObject({ ok: true });
    expect(m.object("favourites", "by", "u1")).toEqual({ ids: ["u2", "u3"] });
    register(m, "u2");
    register(m, "u3");
    rpcPushSettingsSet(ctxFor("u2"), log, m.nk, JSON.stringify({ types: { friend_online: true } }));
    afterAuthenticateDevice(ctxFor("u1"), log, m.nk);
    expect(pushed(m)).toEqual([
      {
        token: "tok-u2-phone",
        type: "friend_online",
        route: "friends",
        args: [expect.any(String) as string],
      },
    ]);
    afterAuthenticateDevice(ctxFor("u1"), log, m.nk);
    expect(pushed(m)).toHaveLength(1);
    vi.setSystemTime(T_DAY + 20 * 60_000);
    afterAuthenticateDevice(ctxFor("u1"), log, m.nk);
    expect(pushed(m)).toHaveLength(1);
    vi.setSystemTime(T_DAY + 61 * 60_000);
    afterAuthenticateDevice(ctxFor("u1"), log, m.nk);
    expect(pushed(m)).toHaveLength(2);
    expect(
      parse(
        rpcFavouriteSet(ctxFor("u2"), log, m.nk, JSON.stringify({ userId: "u1", pinned: false })),
      ),
    ).toMatchObject({ ok: true });
    expect(m.object("favourites", "by", "u1")).toEqual({ ids: ["u3"] });
  });

  it("the sweep sends the daily reward once after 09:00 local, the season end once in its last day, and prunes", () => {
    vi.useFakeTimers();
    vi.setSystemTime(T_DAY);
    const m = memoryNk();
    const log = fakeLogger();
    register(m, "u1");
    register(m, "u2", "u2-phone", -240);
    register(m, "u3");
    rpcDailyRewardClaim(ctxFor("u3"), log, m.nk);
    rpcPushSettingsSet(ctxFor("u2"), log, m.nk, JSON.stringify({ types: { season_end: false } }));
    rpcPushSettingsSet(ctxFor("u3"), log, m.nk, JSON.stringify({ types: { season_end: false } }));
    const first = pushSweep(m.nk, log, ENV, T_DAY);
    // u1 at 10:00: sent; u2 at 01:00 (UTC-4): quiet; u3 claimed already.
    expect(first).toMatchObject({
      scanned: 3,
      dailyReward: 1,
      seasonEnd: 0,
      pruned: 0,
      configured: true,
    });
    expect(pushed(m)).toEqual([
      { token: "tok-u1-phone", type: "daily_reward", route: "home", args: [] },
    ]);
    expect(pushSweep(m.nk, log, ENV, T_DAY + 3_600_000)).toMatchObject({ dailyReward: 0 });
    // The next local day: u1 and u3 again (u3 claimed yesterday), u2 still asleep.
    expect(pushSweep(m.nk, log, ENV, T_DAY + 24 * 3_600_000)).toMatchObject({ dailyReward: 2 });
    const season = BUNDLED_SEASONS[0]!;
    const ending = season.endTs - 2 * 3_600_000;
    const tz = (12 - localHour(ending, 0)) * 60;
    // Months later: u1 and u3 refreshed their tokens, u2's went stale.
    vi.setSystemTime(ending);
    register(m, "u1", "u1-phone", tz);
    register(m, "u3", "u3-phone", tz);
    m.requests.length = 0;
    expect(pushSweep(m.nk, log, ENV, ending)).toMatchObject({ seasonEnd: 1, pruned: 1 });
    expect(pushed(m).filter((p) => p.type === "season_end")).toEqual([
      { token: "tok-u1-phone", type: "season_end", route: "pass", args: ["2"] },
    ]);
    expect(pushSweep(m.nk, log, ENV, ending + 60_000)).toMatchObject({ seasonEnd: 0 });
    expect(pushSweep(m.nk, log, ENV, ending + STALE_DEVICE_MS + 1)).toMatchObject({
      pruned: 2,
      scanned: 3,
    });
    expect(parse(rpcAdminPushSweep(ctxFor("u1"), log, m.nk, "{}"))).toMatchObject({ ok: false });
    expect(parse(rpcAdminPushSweep(ADMIN, log, m.nk, "{}"))).toMatchObject({
      ok: true,
      scanned: 3,
    });
  });

  it("the admin test push ignores settings and reports an unconfigured box", () => {
    vi.useFakeTimers();
    vi.setSystemTime(T_NIGHT);
    const m = memoryNk();
    const log = fakeLogger();
    register(m, "u1");
    rpcPushSettingsSet(ctxFor("u1"), log, m.nk, JSON.stringify({ types: { daily_reward: false } }));
    expect(parse(rpcAdminPushTest(ctxFor("u1"), log, m.nk, "{}"))).toMatchObject({ ok: false });
    expect(
      parse(rpcAdminPushTest(ADMIN, log, m.nk, JSON.stringify({ userId: "u1", type: "club" }))),
    ).toMatchObject({ ok: false });
    expect(
      parse(
        rpcAdminPushTest(ADMIN, log, m.nk, JSON.stringify({ userId: "u1", type: "daily_reward" })),
      ),
    ).toMatchObject({ ok: true, configured: true, sent: 1, outcomes: { "u1-phone": "sent" } });
    const bare = { env: {} } as unknown as nkruntime.Context;
    expect(
      parse(
        rpcAdminPushTest(bare, log, m.nk, JSON.stringify({ userId: "u1", type: "table_invite" })),
      ),
    ).toMatchObject({
      ok: true,
      configured: false,
      sent: 0,
      outcomes: { "u1-phone": "unconfigured" },
    });
  });
});
