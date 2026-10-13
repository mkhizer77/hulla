import { describe, expect, it } from "vitest";

import { thullaMatchInit, thullaMatchJoinAttempt } from "../games/thulla/match";
import { rpcConfigGet, rpcConfigSet } from "../rpc/config";
import { rpcHello } from "../rpc/hello";
import {
  clientTooOld,
  compareVersions,
  DEFAULT_CONFIG,
  isRemoteConfig,
  parseVersion,
  resolveAll,
  resolveFlag,
  type RemoteConfig,
} from "../services/config";

import { fakeDispatcher, nkPresence } from "./fake-match";
import { fakeLogger, memoryNk } from "./fake-nk";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const ctxFor = (userId: string, env: Record<string, string> = {}): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env }) as unknown as nkruntime.Context;
const SERVER_KEY = { env: {} } as unknown as nkruntime.Context;

const staged: RemoteConfig = {
  version: 2,
  minClientVersion: "0.2.0",
  flags: {
    quick_play: [{ value: true }, { region: "asia", value: false }],
    shop: [
      { value: false },
      { cohort: "beta", value: true },
      { region: "eu", cohort: "beta", value: "soft" },
    ],
    turn_seconds_public: [{ value: 15 }, { region: "asia", value: 12 }],
  },
  updatedTs: 0,
};

describe("remote configuration and the minimum client version (GDD 22.5, 20.15)", () => {
  it("compares versions and treats an unknown version as current", () => {
    expect(parseVersion("1.2.3")).toEqual([1, 2, 3]);
    expect(parseVersion("1.2.3-dev")).toEqual([1, 2, 3]);
    expect(parseVersion("1.2")).toBeNull();
    expect(parseVersion(12)).toBeNull();
    expect(compareVersions("0.1.9", "0.2.0")).toBeLessThan(0);
    expect(compareVersions("1.0.0", "0.9.9")).toBeGreaterThan(0);
    expect(compareVersions("0.2.0", "0.2.0")).toBe(0);
    expect(clientTooOld("0.1.0", staged)).toBe(true);
    expect(clientTooOld("0.2.0", staged)).toBe(false);
    expect(clientTooOld(undefined, staged)).toBe(false);
    expect(clientTooOld("garbage", staged)).toBe(false);
  });

  it("resolves a flag by region and cohort, the most specific rule first", () => {
    expect(resolveFlag(staged, "quick_play", { region: "eu", cohort: "*" })).toBe(true);
    expect(resolveFlag(staged, "quick_play", { region: "asia", cohort: "*" })).toBe(false);
    expect(resolveFlag(staged, "shop", { region: "asia", cohort: "*" })).toBe(false);
    expect(resolveFlag(staged, "shop", { region: "asia", cohort: "beta" })).toBe(true);
    expect(resolveFlag(staged, "shop", { region: "eu", cohort: "beta" })).toBe("soft");
    expect(resolveFlag(staged, "nope", { region: "eu", cohort: "*" })).toBeNull();
    expect(resolveAll(staged, { region: "asia", cohort: "*" })).toEqual({
      quick_play: false,
      shop: false,
      turn_seconds_public: 12,
    });
    expect(resolveAll(DEFAULT_CONFIG, { region: "local", cohort: "*" })).toMatchObject({
      quick_play: true,
      voice: true,
      shop: false,
    });
    expect(isRemoteConfig(staged)).toBe(true);
    expect(isRemoteConfig({ version: 1, minClientVersion: "x", flags: {} })).toBe(false);
    expect(
      isRemoteConfig({ version: 1, minClientVersion: "1.0.0", flags: { a: [{ value: {} }] } }),
    ).toBe(false);
  });

  it("serves the box's region, replaces the config live, refuses a downgrade, and tells hello", () => {
    const m = memoryNk();
    const log = fakeLogger();
    const before = parse(
      rpcConfigGet(
        ctxFor("u1", { HULLA_REGION: "asia" }),
        log,
        m.nk,
        JSON.stringify({ clientVersion: "0.1.0" }),
      ),
    );
    expect(before).toMatchObject({
      ok: true,
      version: 1,
      region: "asia",
      minClientVersion: "0.1.0",
      updateRequired: false,
    });
    expect(parse(rpcHello(ctxFor("u1"), log, m.nk))).toMatchObject({
      ok: true,
      minClientVersion: "0.1.0",
    });
    expect(parse(rpcConfigSet(ctxFor("u1"), log, m.nk, JSON.stringify(staged)))).toMatchObject({
      ok: false,
    });
    expect(parse(rpcConfigSet(SERVER_KEY, log, m.nk, "{"))).toMatchObject({
      error: { code: "table.invalid_payload" },
    });
    expect(
      parse(rpcConfigSet(SERVER_KEY, log, m.nk, JSON.stringify({ version: 2 }))),
    ).toMatchObject({ error: { code: "content.schema_invalid" } });
    expect(parse(rpcConfigSet(SERVER_KEY, log, m.nk, JSON.stringify(staged)))).toEqual({
      ok: true,
      version: 2,
    });
    expect(
      parse(rpcConfigSet(SERVER_KEY, log, m.nk, JSON.stringify({ ...staged, version: 2 }))),
    ).toMatchObject({ error: { code: "content.manifest_downgrade" } });
    const after = parse(
      rpcConfigGet(
        ctxFor("u1", { HULLA_REGION: "asia" }),
        log,
        m.nk,
        JSON.stringify({ clientVersion: "0.1.0", cohort: "beta" }),
      ),
    );
    expect(after).toMatchObject({
      version: 2,
      minClientVersion: "0.2.0",
      updateRequired: true,
      flags: { quick_play: false, shop: true, turn_seconds_public: 12 },
    });
    expect(parse(rpcHello(ctxFor("u1"), log, m.nk))).toMatchObject({ minClientVersion: "0.2.0" });

    // An old client cannot join a table; a current one can.
    const ctx = { matchId: "m1.n1", env: {} } as unknown as nkruntime.Context;
    const init = thullaMatchInit(ctx, log, m.nk, {
      code: "ABCDEF",
      gameId: "thulla",
      mode: "private",
    });
    const old = thullaMatchJoinAttempt(
      ctx,
      log,
      m.nk,
      fakeDispatcher().dispatcher,
      1,
      init.state,
      nkPresence(0),
      { clientVersion: "0.1.5" },
    );
    expect(old).toMatchObject({ accept: false, rejectMessage: "auth.update_required" });
    const fresh = thullaMatchJoinAttempt(
      ctx,
      log,
      m.nk,
      fakeDispatcher().dispatcher,
      1,
      init.state,
      nkPresence(1),
      { clientVersion: "0.2.0" },
    );
    expect(fresh?.accept).toBe(true);
  });
});
