import { OPCODES } from "@hulla/protocol/generated";
import { describe, expect, it } from "vitest";

import {
  thullaMatchInit,
  thullaMatchJoin,
  thullaMatchJoinAttempt,
  thullaMatchLeave,
  thullaMatchLoop,
  thullaMatchSignal,
  thullaMatchTerminate,
} from "../games/thulla/match";
import { afterAuthenticateDevice, afterAuthenticateGoogle } from "../hooks/after_authenticate";
import { beforeChannelMessageSend } from "../hooks/before_channel_message";
import { matchmakerMatched } from "../hooks/matchmaker_matched";
import {
  rpcAccountDelete,
  rpcAccountDeleteCancel,
  rpcAccountExport,
  rpcAdminPurgeDeleted,
} from "../rpc/account";
import { rpcAccountMerge, rpcAccountMergePrepare } from "../rpc/account_merge";
import { rpcHello } from "../rpc/hello";
import { rpcPresenceSet, rpcTableListFriends } from "../rpc/presence";
import { rpcPresetsGet } from "../rpc/presets_get";
import { rpcProfileGet, rpcProfileUpdate } from "../rpc/profile";
import { rpcFavouriteSet, rpcFriendAddByCode, rpcSocialOverview } from "../rpc/social";
import { rpcTableCreate } from "../rpc/table_create";
import { rpcTableJoinByCode } from "../rpc/table_join_by_code";
import { rpcVoiceToken } from "../rpc/voice_token";
import { GRACE_PERIOD_MS } from "../services/account_deletion";
import { isUserOnline, nkPresenceStore, seatTaken } from "../services/presence";

import { fakeDispatcher, matchMessage, nkPresence } from "./fake-match";
import { fakeLogger, memoryNk, type LogLine } from "./fake-nk";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const ctxFor = (userId: string, extra: Json = {}): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env: {}, ...extra }) as unknown as nkruntime.Context;
const ADMIN = { env: {} } as unknown as nkruntime.Context;

/** Signs a user in: the after-authenticate hook bootstraps the profile with a friend code. */
function signIn(m: ReturnType<typeof memoryNk>, userId: string, lines: LogLine[] = []): string {
  afterAuthenticateDevice(ctxFor(userId), fakeLogger(lines), m.nk);
  const profile = parse(rpcProfileGet(ctxFor(userId), fakeLogger(), m.nk, "{}"));
  return String((profile["profile"] as Json)["friendCode"]);
}

describe("the RPCs and hooks against an in-memory Nakama", () => {
  it("hello and presets answer without state", () => {
    const m = memoryNk();
    expect(parse(rpcHello(ctxFor("u1"), fakeLogger(), m.nk))).toMatchObject({ ok: true });
    expect(parse(rpcPresetsGet(ctxFor("u1"), fakeLogger(), m.nk, "{}"))).toMatchObject({
      ok: true,
    });
  });

  it("sign-in creates a profile once and touches presence; the profile RPCs read and edit it", () => {
    const m = memoryNk();
    const lines: LogLine[] = [];
    const code = signIn(m, "u1", lines);
    expect(code).toHaveLength(8);
    afterAuthenticateGoogle(ctxFor("u1"), fakeLogger(lines), m.nk);
    expect(lines.filter((l) => l.message === "profile created")).toHaveLength(1);
    expect(m.object("presence", "state", "u1")).toMatchObject({ table: null, away: false });
    const updated = parse(
      rpcProfileUpdate(ctxFor("u1"), fakeLogger(), m.nk, JSON.stringify({ displayName: "Ali K" })),
    );
    expect(updated).toMatchObject({ ok: true, profile: { displayName: "Ali K" } });
    expect(m.users.get("u1")?.displayName).toBe("Ali K");
    const byHandle = parse(
      rpcProfileGet(ctxFor("u2"), fakeLogger(), m.nk, JSON.stringify({ handle: "user_u1" })),
    );
    expect(byHandle).toMatchObject({ ok: true, profile: { displayName: "Ali K" } });
    expect(parse(rpcProfileGet(ctxFor("u2"), fakeLogger(), m.nk, "{}"))).toMatchObject({
      ok: false,
      error: { code: "profile.not_found" },
    });
  });

  it("friends by code, favourites, recent players and the presence list run end to end", () => {
    const m = memoryNk();
    const aliCode = signIn(m, "ali");
    signIn(m, "sara");
    signIn(m, "noor");
    const log = fakeLogger();
    expect(
      parse(rpcFriendAddByCode(ctxFor("sara"), log, m.nk, JSON.stringify({ friendCode: aliCode }))),
    ).toEqual({ ok: true, userId: "ali" });
    expect(m.friends.get("sara")?.get("ali")).toBe(1);
    m.nk.friendsAdd("ali", "user_ali", ["sara"], [], {});
    expect(m.friends.get("ali")?.get("sara")).toBe(0);
    expect(
      parse(
        rpcFavouriteSet(ctxFor("ali"), log, m.nk, JSON.stringify({ userId: "sara", pinned: true })),
      ),
    ).toEqual({ ok: true, favourites: ["sara"] });
    expect(
      parse(
        rpcFavouriteSet(ctxFor("ali"), log, m.nk, JSON.stringify({ userId: "noor", pinned: true })),
      ),
    ).toMatchObject({ error: { code: "social.not_friends" } });
    expect(parse(rpcFavouriteSet(ctxFor("ali"), log, m.nk, "{}"))).toMatchObject({
      error: { code: "table.invalid_payload" },
    });

    m.online.add("sara");
    expect(isUserOnline(m.nk, "sara")).toBe(true);
    expect(isUserOnline(m.nk, "noor")).toBe(false);
    const matchId = m.nk.matchCreate("thulla", {
      code: "ABCDEF",
      mode: "private",
      gameId: "thulla",
    });
    seatTaken(nkPresenceStore(m.nk), "sara", matchId, 1_000);
    const list = parse(rpcTableListFriends(ctxFor("ali"), log, m.nk));
    expect(list).toMatchObject({
      ok: true,
      friends: [
        { userId: "sara", state: "lobby", favourite: true, table: { matchId, joinable: true } },
      ],
    });
    expect(
      parse(rpcPresenceSet(ctxFor("sara"), log, m.nk, JSON.stringify({ away: true }))),
    ).toEqual({
      ok: true,
      away: true,
    });
    expect(
      (parse(rpcTableListFriends(ctxFor("ali"), log, m.nk))["friends"] as Json[])[0],
    ).toMatchObject({
      state: "away",
    });
    expect(parse(rpcPresenceSet(ctxFor("sara"), log, m.nk, "not json"))).toEqual({
      ok: true,
      away: false,
    });
    expect(parse(rpcSocialOverview(ctxFor("ali"), log, m.nk))).toMatchObject({
      ok: true,
      favourites: ["sara"],
      recent: [],
    });
    expect(
      parse(rpcTableListFriends({ env: {} } as unknown as nkruntime.Context, log, m.nk)),
    ).toMatchObject({
      ok: false,
    });
  });

  it("creates a table, joins it by code, refuses voice without LiveKit settings, and runs the Thulla callbacks", () => {
    const m = memoryNk();
    const log = fakeLogger();
    const created = parse(
      rpcTableCreate(
        ctxFor("host", { env: { HULLA_REGION: "test" } }),
        log,
        m.nk,
        JSON.stringify({ mode: "private" }),
      ),
    );
    expect(created).toMatchObject({ ok: true });
    const code = String(created["code"]);
    expect(
      parse(
        rpcTableJoinByCode(
          ctxFor("guest"),
          log,
          m.nk,
          JSON.stringify({ code: code.toLowerCase() }),
        ),
      ),
    ).toMatchObject({ ok: true, matchId: created["matchId"] });
    expect(
      parse(rpcTableJoinByCode(ctxFor("guest"), log, m.nk, JSON.stringify({ code: "ZZZZZZ" }))),
    ).toMatchObject({
      ok: false,
    });
    expect(
      parse(
        rpcVoiceToken(ctxFor("host"), log, m.nk, JSON.stringify({ matchId: created["matchId"] })),
      ),
    ).toMatchObject({ ok: false, error: { code: "voice.unavailable" } });

    const ctx = { matchId: "m1.n1", env: {} } as unknown as nkruntime.Context;
    const d = fakeDispatcher();
    const init = thullaMatchInit(ctx, log, m.nk, {
      code,
      gameId: "thulla",
      mode: "private",
      rules: { gameId: "thulla", preset: "family" },
    });
    expect(init.state.rules.settings).toMatchObject({ turnTimerSeconds: 30, loserTitle: "donkey" });
    let state = init.state;
    const rebuilt = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;
    const presences: [nkruntime.Presence, nkruntime.Presence, nkruntime.Presence] = [
      nkPresence(0),
      nkPresence(1),
      nkPresence(2),
    ];
    for (const p of presences) {
      const attempt = thullaMatchJoinAttempt(ctx, log, m.nk, d.dispatcher, 1, state, p, {});
      expect(attempt?.accept).toBe(true);
      state = rebuilt(attempt?.state ?? state);
    }
    state = rebuilt(
      thullaMatchJoin(ctx, log, m.nk, d.dispatcher, 1, state, presences)?.state ?? state,
    );
    expect(m.object("presence", "state", "u0")).toMatchObject({ table: { matchId: "m1.n1" } });
    const ping = matchMessage(presences[0], OPCODES.ping.opcode, {
      v: 1,
      type: "ping",
      tableId: state.tableId,
      seq: 1,
      ts: 0,
      payload: {},
    });
    state = rebuilt(
      thullaMatchLoop(ctx, log, m.nk, d.dispatcher, 2, state, [ping])?.state ?? state,
    );
    const inspected = thullaMatchSignal(
      ctx,
      log,
      m.nk,
      d.dispatcher,
      3,
      state,
      JSON.stringify({ kind: "inspect" }),
    );
    expect(JSON.parse(inspected?.data ?? "{}")).toMatchObject({ ok: true });
    state = rebuilt(
      thullaMatchLeave(ctx, log, m.nk, d.dispatcher, 4, state, [presences[2]])?.state ?? state,
    );
    expect(m.object("presence", "state", "u2")).toMatchObject({ table: null });
    thullaMatchTerminate(ctx, log, m.nk, d.dispatcher, 5, state, 0);
    expect(m.object("presence", "state", "u0")).toMatchObject({ table: null });
  });

  it("the matchmaker hook opens a public table and the chat hook paces messages", () => {
    const m = memoryNk();
    const log = fakeLogger();
    const entries = ["a", "b", "c"].map((id) => ({
      presence: { userId: id, sessionId: `s${id}`, username: id, node: "n1" },
      properties: { game: "thulla", mode: "quick", lane: "any" },
      partyId: id === "b" ? "p1" : undefined,
    })) as unknown as nkruntime.MatchmakerResult[];
    const matchId = matchmakerMatched(ctxFor(""), log, m.nk, entries);
    // A linked account: guests may not free-text at a public table (GDD 9.4).
    m.user("a", { googleId: "g-a" });
    expect(m.matches.get(matchId)?.params).toMatchObject({
      mode: "public",
      autoStart: { expected: 3 },
    });

    const envelope = {
      channelMessageSend: {
        channelId: `2...${matchId}`,
        content: JSON.stringify({ text: "salaam" }),
      },
    };
    expect(beforeChannelMessageSend(ctxFor("a"), log, m.nk, envelope)).toBe(envelope);
    expect(() => beforeChannelMessageSend(ctxFor("a"), log, m.nk, envelope)).toThrow(
      "network.rate_limited",
    );
  });

  it("deletion, export, purge and the guest merge run through storage", () => {
    const m = memoryNk();
    const log = fakeLogger();
    signIn(m, "guest");
    signIn(m, "linked");
    m.wallets.set("guest", { coins: 40 });
    expect(parse(rpcAccountExport(ctxFor("guest"), log, m.nk))).toMatchObject({
      ok: true,
      profile: { displayName: expect.stringMatching(/^Guest/) as string },
    });
    const prepared = parse(rpcAccountMergePrepare(ctxFor("guest"), log, m.nk));
    expect(prepared).toMatchObject({ ok: true });
    const merged = parse(
      rpcAccountMerge(ctxFor("linked"), log, m.nk, JSON.stringify({ code: prepared["code"] })),
    );
    expect(merged).toMatchObject({ ok: true });
    expect(m.wallets.get("linked")).toEqual({ coins: 40 });
    expect(m.deleted).toContain("guest");
    expect(
      parse(rpcAccountMerge(ctxFor("linked"), log, m.nk, JSON.stringify({ code: "nope" }))),
    ).toMatchObject({ ok: false, error: { code: "auth.merge_code_invalid" } });

    const requested = parse(rpcAccountDelete(ctxFor("linked"), log, m.nk));
    expect(requested).toMatchObject({ ok: true });
    expect(parse(rpcAccountDeleteCancel(ctxFor("linked"), log, m.nk))).toMatchObject({ ok: true });
    parse(rpcAccountDelete(ctxFor("linked"), log, m.nk));
    const early = parse(rpcAdminPurgeDeleted(ADMIN, log, m.nk, JSON.stringify({ dryRun: true })));
    expect(early).toMatchObject({ ok: true });
    const late = parse(
      rpcAdminPurgeDeleted(
        ADMIN,
        log,
        m.nk,
        JSON.stringify({ now: Date.now() + GRACE_PERIOD_MS + 1 }),
      ),
    );
    expect(late).toMatchObject({ ok: true, purged: ["linked"] });
    expect(m.loggedOut).toContain("linked");
    expect(parse(rpcAdminPurgeDeleted(ctxFor("linked"), log, m.nk, "{}"))).toMatchObject({
      ok: false,
    });
    expect(parse(rpcAdminPurgeDeleted(ADMIN, log, m.nk, "{"))).toMatchObject({ ok: false });
  });
});
