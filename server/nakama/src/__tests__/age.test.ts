import { afterEach, describe, expect, it, vi } from "vitest";

import { rpcAgeGet, rpcAgeSet } from "../rpc/age";
import { voiceToken } from "../rpc/voice_token";
import { AGE_COLLECTION, AGE_KEY, checkBirthYear, limitsFor, youngestAge } from "../services/age";

import { fakeLogger, memoryNk } from "./fake-nk";

type Json = Record<string, unknown>;
/** 9 November 2026. */
const NOW = Date.UTC(2026, 10, 9);
const ctxFor = (userId: string): nkruntime.Context =>
  ({ userId, env: {} }) as unknown as nkruntime.Context;

const set = (m: ReturnType<typeof memoryNk>, userId: string, body: unknown): Json =>
  JSON.parse(rpcAgeSet(ctxFor(userId), fakeLogger(), m.nk, JSON.stringify(body))) as Json;
const get = (m: ReturnType<typeof memoryNk>, userId: string): Json =>
  JSON.parse(rpcAgeGet(ctxFor(userId), fakeLogger(), m.nk)) as Json;

afterEach(() => {
  vi.useRealTimers();
});

describe("the age band (GDD 24.6)", () => {
  it("counts the youngest age a birth year allows", () => {
    expect(youngestAge(2010, NOW)).toBe(15);
    expect(youngestAge(2008, NOW)).toBe(17);
    expect(youngestAge(2000, NOW)).toBe(25);
  });

  it("keeps out under-13s and nonsense years", () => {
    expect(checkBirthYear(2013, NOW)).toEqual({ ok: false, reason: "too_young" });
    expect(checkBirthYear(2012, NOW)).toEqual({ ok: true, birthYear: 2012 });
    expect(checkBirthYear(2027, NOW)).toEqual({ ok: false, reason: "year" });
    expect(checkBirthYear(1899, NOW)).toEqual({ ok: false, reason: "year" });
    expect(checkBirthYear("2000", NOW)).toEqual({ ok: false, reason: "year" });
    expect(checkBirthYear(2000.5, NOW)).toEqual({ ok: false, reason: "year" });
  });

  it("locks strangers under 16 and personalised ads under 18; an unanswered age gets both limits", () => {
    expect(limitsFor({ birthYear: 2011, setTs: 0 }, NOW)).toEqual({
      known: true,
      strangersLocked: true,
      personalisedAds: false,
    });
    expect(limitsFor({ birthYear: 2009, setTs: 0 }, NOW)).toEqual({
      known: true,
      strangersLocked: false,
      personalisedAds: false,
    });
    expect(limitsFor({ birthYear: 2007, setTs: 0 }, NOW)).toEqual({
      known: true,
      strangersLocked: false,
      personalisedAds: true,
    });
    expect(limitsFor(null, NOW)).toEqual({
      known: false,
      strangersLocked: true,
      personalisedAds: false,
    });
  });
});

describe("age_set and age_get", () => {
  it("stores the year once, server-only, and refuses a different year later", () => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    const m = memoryNk();
    expect(get(m, "u1")).toMatchObject({ ok: true, known: false });
    expect(set(m, "u1", { birthYear: 2009 })).toMatchObject({
      ok: true,
      known: true,
      strangersLocked: false,
      personalisedAds: false,
    });
    const [stored] = m.nk.storageRead([{ collection: AGE_COLLECTION, key: AGE_KEY, userId: "u1" }]);
    expect(stored?.value).toMatchObject({ birthYear: 2009 });
    expect(stored?.permissionRead).toBe(0);
    expect(set(m, "u1", { birthYear: 2009 })).toMatchObject({ ok: true });
    expect(set(m, "u1", { birthYear: 1990 })).toMatchObject({
      ok: false,
      error: { code: "profile.age_locked" },
    });
    expect(get(m, "u1")).toMatchObject({ ok: true, known: true, strangersLocked: false });
  });

  it("refuses an under-13 and stores nothing", () => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    const m = memoryNk();
    expect(set(m, "kid", { birthYear: 2015 })).toMatchObject({
      ok: false,
      error: { code: "profile.too_young" },
    });
    expect(get(m, "kid")).toMatchObject({ known: false });
  });

  it("needs a signed-in caller and a year", () => {
    const m = memoryNk();
    expect(set(m, "", { birthYear: 2000 })).toMatchObject({
      ok: false,
      error: { code: "table.not_authorised" },
    });
    expect(set(m, "u2", {})).toMatchObject({ ok: false, error: { code: "table.invalid_payload" } });
  });
});

describe("voice under 16 (GDD 24.6)", () => {
  const config = {
    url: "wss://voice.test",
    apiUrl: "https://voice.test",
    apiKey: "key",
    apiSecret: "secret-secret-secret-secret-1234",
  };
  const base = {
    runtime: { nowMs: () => NOW } as never,
    userId: "teen",
    config,
    membership: () => ({ present: true, seat: 1, tableId: "t" }),
  };

  it("is refused at a public table and allowed in a private room", () => {
    const publicTable = voiceToken(
      { ...base, strangersLocked: true, privateTable: () => false },
      JSON.stringify({ matchId: "m" }),
    );
    expect(publicTable).toMatchObject({ ok: false, error: { code: "voice.age_limited" } });
    const privateRoom = voiceToken(
      { ...base, strangersLocked: true, privateTable: () => true },
      JSON.stringify({ matchId: "m" }),
    );
    expect(privateRoom).toMatchObject({ ok: true });
    const adult = voiceToken(
      { ...base, strangersLocked: false, privateTable: () => false },
      JSON.stringify({ matchId: "m" }),
    );
    expect(adult).toMatchObject({ ok: true });
  });
});
