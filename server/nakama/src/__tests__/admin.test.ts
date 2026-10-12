import { hexToBytes, stateFromSeed, Xoshiro128StarStar } from "@hulla/shared-ts";
import { describe, expect, it } from "vitest";

import { thullaHost } from "../games/thulla/host";
import { thullaMatchInit, thullaMatchJoinAttempt } from "../games/thulla/match";
import { beforeAuthenticateDevice } from "../hooks/before_authenticate";
import { ROUND_LOG_COLLECTION } from "../match/nakama_match";
import { replayRound } from "../match/replay";
import {
  isAdmin,
  rpcAdminAuditList,
  rpcAdminRoundReplay,
  rpcAdminSanction,
  rpcAdminTableInspect,
  rpcSanctionsGet,
} from "../rpc/admin";
import { rpcVoiceToken } from "../rpc/voice_token";
import { SYSTEM_USER_ID } from "../services/reports";
import {
  applySanction,
  isBanned,
  isVoiceSuspended,
  PERMANENT_TS,
  sanctionsView,
  untilFor,
} from "../services/sanctions";

import { fakeDispatcher, nkPresence } from "./fake-match";
import { fakeLogger, memoryNk } from "./fake-nk";

import type { RoundRecord } from "../match/types";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const T0 = 1_760_200_000_000;
const HOUR = 60 * 60 * 1000;
const ctxFor = (userId: string, env: Record<string, string> = {}): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env }) as unknown as nkruntime.Context;
const SERVER_KEY = { env: {} } as unknown as nkruntime.Context;

describe("strikes and bans with expiry (GDD 24.3)", () => {
  it("stacks sanctions, expires them, and clear lifts every one but keeps the history", () => {
    const by = "owner";
    const muted = applySanction(null, {
      action: "mute",
      reason: "spam",
      hours: 1,
      permanent: false,
      by,
      now: T0,
    });
    expect(muted).toMatchObject({ mutedUntilTs: T0 + HOUR, reason: "spam" });
    const voice = applySanction(muted, {
      action: "voice_suspend",
      reason: "abuse",
      hours: 24,
      permanent: false,
      by,
      now: T0,
    });
    expect(isVoiceSuspended(voice, T0 + 23 * HOUR)).toBe(true);
    expect(isVoiceSuspended(voice, T0 + 25 * HOUR)).toBe(false);
    const banned = applySanction(voice, {
      action: "ban",
      reason: "cheating",
      hours: null,
      permanent: true,
      by,
      now: T0 + 1,
    });
    expect(isBanned(banned, T0 + 1_000_000 * HOUR)).toBe(true);
    expect(banned.bannedUntilTs).toBe(PERMANENT_TS);
    const warned = applySanction(banned, {
      action: "warning",
      reason: "tone",
      hours: null,
      permanent: false,
      by,
      now: T0 + 2,
    });
    expect(sanctionsView(warned, T0 + 3)).toEqual({
      mutedUntilTs: T0 + HOUR,
      voiceSuspendedUntilTs: T0 + 24 * HOUR,
      bannedUntilTs: PERMANENT_TS,
      warnings: 1,
    });
    const cleared = applySanction(warned, {
      action: "clear",
      reason: "appeal",
      hours: null,
      permanent: false,
      by,
      now: T0 + 4,
    });
    expect(sanctionsView(cleared, T0 + 5)).toEqual({
      mutedUntilTs: null,
      voiceSuspendedUntilTs: null,
      bannedUntilTs: null,
      warnings: 1,
    });
    expect(cleared.strikes).toHaveLength(5);
    expect(untilFor({ hours: 0, permanent: false, now: T0 })).toBeNull();
    expect(sanctionsView(null, T0)).toEqual({
      mutedUntilTs: null,
      voiceSuspendedUntilTs: null,
      bannedUntilTs: null,
      warnings: 0,
    });
  });
});

/** A completed Thulla round driven by the auto-play policy, logged the way the table logs it. */
function playedRecord(): RoundRecord {
  const rules = thullaHost.resolveRules({ gameId: "thulla", preset: "standard" }, "private");
  if (!rules.ok) throw new Error("rules");
  const seedHex = "0f1e2d3c4b5a69788796a5b4c3d2e1f0ffeeddccbbaa99887766554433221100";
  const seed = hexToBytes(seedHex);
  const rng = Xoshiro128StarStar.fromState(stateFromSeed(seed));
  const created = thullaHost.module.createRound(
    { rules: rules.value, seatCount: 4, dealerSeat: 0, seed },
    rng,
  );
  if (!created.ok) throw new Error("deal");
  let state = created.value;
  const moves: { seat: number; move: unknown; auto: boolean }[] = [];
  for (let guard = 0; guard < 400 && !thullaHost.module.isRoundOver(state); guard += 1) {
    const seat = [0, 1, 2, 3].find((s) => thullaHost.module.legalMoves(state, s).length > 0);
    if (seat === undefined) throw new Error("nobody can move");
    const move = thullaHost.module.onDeadline(state, seat);
    if (move === null) throw new Error("no auto move");
    const applied = thullaHost.module.applyMove(state, seat, move, rng);
    if (!applied.ok) throw new Error("move refused");
    state = applied.state;
    moves.push({ seat, move, auto: true });
  }
  if (!thullaHost.module.isRoundOver(state)) throw new Error("round did not end");
  return {
    tableId: "t_ABCDEF",
    roundId: "r_1",
    gameId: "thulla",
    rulesVersion: thullaHost.module.version,
    rules: { gameId: "thulla", preset: "standard" },
    mode: "private",
    dealerSeat: 0,
    seedHex,
    players: ["u0", "u1", "u2", "u3"],
    moves,
    result: thullaHost.module.results(state),
    series: null,
    finalStateHash: thullaHost.stateHash(state),
    highlights: [{}, {}, {}, {}],
    startedTs: T0,
    endedTs: T0 + 1000,
    completed: true,
  };
}

describe("replaying a round from its log (ARCHITECTURE 8.11)", () => {
  it("reproduces the deal and every move, and notices a tampered log", () => {
    const record = playedRecord();
    const replay = replayRound(thullaHost, record);
    expect(replay.ok).toBe(true);
    if (!replay.ok) return;
    expect(replay.value).toMatchObject({
      roundId: "r_1",
      moves: record.moves.length,
      matches: true,
    });
    expect(replay.value.result).toEqual(record.result);
    const tampered = { ...record, finalStateHash: "0000" };
    const mismatch = replayRound(thullaHost, tampered);
    expect(mismatch.ok && !mismatch.value.matches).toBe(true);
    const broken = { ...record, moves: [{ seat: 3, move: { type: "take_hand" }, auto: false }] };
    expect(replayRound(thullaHost, broken)).toMatchObject({
      ok: false,
      error: { code: "table.invalid_payload" },
    });
  });
});

describe("the admin RPCs against an in-memory Nakama", () => {
  it("gates callers to the server key or the listed owner accounts", () => {
    expect(isAdmin(SERVER_KEY)).toBe(true);
    expect(isAdmin(ctxFor("owner", { HULLA_ADMIN_USER_IDS: "owner, other" }))).toBe(true);
    expect(isAdmin(ctxFor("stranger", { HULLA_ADMIN_USER_IDS: "owner" }))).toBe(false);
    expect(isAdmin(ctxFor("stranger"))).toBe(false);
    const m = memoryNk();
    expect(parse(rpcAdminAuditList(ctxFor("stranger"), fakeLogger(), m.nk, "{}"))).toMatchObject({
      ok: false,
    });
  });

  it("inspects a table and replays a round, writing audit rows", () => {
    const m = memoryNk();
    const log = fakeLogger();
    const matchId = m.nk.matchCreate("thulla", {
      code: "ABCDEF",
      mode: "private",
      gameId: "thulla",
    });
    expect(
      parse(rpcAdminTableInspect(SERVER_KEY, log, m.nk, JSON.stringify({ matchId }))),
    ).toMatchObject({
      ok: true,
      label: { code: "ABCDEF" },
      table: { ok: true },
    });
    expect(
      parse(rpcAdminTableInspect(SERVER_KEY, log, m.nk, JSON.stringify({ matchId: "gone" }))),
    ).toMatchObject({
      error: { code: "table.not_found" },
    });
    const record = playedRecord();
    m.nk.storageWrite([
      {
        collection: ROUND_LOG_COLLECTION,
        key: `${record.tableId}:${record.roundId}`,
        userId: SYSTEM_USER_ID,
        value: record,
      },
    ]);
    const owner = ctxFor("owner", { HULLA_ADMIN_USER_IDS: "owner" });
    expect(
      parse(
        rpcAdminRoundReplay(
          owner,
          log,
          m.nk,
          JSON.stringify({ tableId: "t_ABCDEF", roundId: "r_1" }),
        ),
      ),
    ).toMatchObject({ ok: true, matches: true, players: ["u0", "u1", "u2", "u3"] });
    expect(
      parse(
        rpcAdminRoundReplay(owner, log, m.nk, JSON.stringify({ tableId: "t_X", roundId: "r_9" })),
      ),
    ).toMatchObject({
      error: { code: "table.not_found" },
    });
    const audit = parse(rpcAdminAuditList(owner, log, m.nk, "{}"))["entries"] as Json[];
    expect(audit.map((e) => [e["actorId"], e["action"]])).toEqual([
      ["owner", "round_replay"],
      ["server_key", "table_inspect"],
    ]);
  });

  it("mutes, suspends voice, bans with the device and lifts it all; the ban holds at join and sign-in", () => {
    const m = memoryNk();
    const log = fakeLogger();
    m.devices.set("bad", ["device-1"]);
    const voice = parse(
      rpcAdminSanction(
        SERVER_KEY,
        log,
        m.nk,
        JSON.stringify({ userId: "bad", action: "voice_suspend", hours: 24, reason: "abuse" }),
      ),
    );
    expect(voice).toMatchObject({
      ok: true,
      sanctions: { voiceSuspendedUntilTs: expect.any(Number) as number },
    });
    expect(
      parse(rpcVoiceToken(ctxFor("bad"), log, m.nk, JSON.stringify({ matchId: "m1" }))),
    ).toMatchObject({
      error: { code: "voice.suspended" },
    });
    const banned = parse(
      rpcAdminSanction(
        SERVER_KEY,
        log,
        m.nk,
        JSON.stringify({
          userId: "bad",
          action: "ban",
          permanent: true,
          device: true,
          reason: "cheating",
        }),
      ),
    );
    expect(banned).toMatchObject({ ok: true, sanctions: { bannedUntilTs: PERMANENT_TS } });
    expect(m.loggedOut).toContain("bad");
    expect(parse(rpcSanctionsGet(ctxFor("bad"), log, m.nk))).toMatchObject({
      ok: true,
      bannedUntilTs: PERMANENT_TS,
    });

    const ctx = { matchId: "m1.n1", env: {} } as unknown as nkruntime.Context;
    const init = thullaMatchInit(ctx, log, m.nk, {
      code: "ABCDEF",
      gameId: "thulla",
      mode: "private",
    });
    const presence = { ...nkPresence(0), userId: "bad" };
    const attempt = thullaMatchJoinAttempt(
      ctx,
      log,
      m.nk,
      fakeDispatcher().dispatcher,
      1,
      init.state,
      presence,
      {},
    );
    expect(attempt).toMatchObject({ accept: false, rejectMessage: "auth.banned" });
    expect(() =>
      beforeAuthenticateDevice(ctx, log, m.nk, { account: { id: "device-1" }, create: true }),
    ).toThrow("auth.banned");
    expect(beforeAuthenticateDevice(ctx, log, m.nk, { account: { id: "device-2" } })).toEqual({
      account: { id: "device-2" },
    });

    expect(
      parse(
        rpcAdminSanction(
          SERVER_KEY,
          log,
          m.nk,
          JSON.stringify({ userId: "bad", action: "clear", reason: "appeal" }),
        ),
      ),
    ).toMatchObject({
      sanctions: { bannedUntilTs: null, voiceSuspendedUntilTs: null },
    });
    expect(beforeAuthenticateDevice(ctx, log, m.nk, { account: { id: "device-1" } })).toBeDefined();
    const again = thullaMatchJoinAttempt(
      ctx,
      log,
      m.nk,
      fakeDispatcher().dispatcher,
      1,
      init.state,
      presence,
      {},
    );
    expect(again?.accept).toBe(true);
    expect(
      parse(
        rpcAdminSanction(SERVER_KEY, log, m.nk, JSON.stringify({ userId: "bad", action: "nope" })),
      ),
    ).toMatchObject({
      error: { code: "table.invalid_payload" },
    });
    expect(parse(rpcAdminSanction(ctxFor("bad"), log, m.nk, "{}"))).toMatchObject({ ok: false });
  });
});
