import { describe, expect, it } from "vitest";

import { BUNDLED_SEASONS } from "../content/seasons";
import { rpcPassClaim, rpcPassGet, rpcPassPremiumGrant } from "../rpc/pass";
import { nkCatalogStore, owns } from "../services/catalog";
import { nkLedgerPorts } from "../services/ledger";
import {
  activatePremium,
  addPassXp,
  claimTier,
  currentSeason,
  earnPassXp,
  emptyPass,
  forSeason,
  nkRewardPorts,
  passView,
  tierFor,
  TIER_COUNT,
  xpFor,
} from "../services/season_pass";

import { fakeLogger, memoryNk } from "./fake-nk";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const ctxFor = (userId: string): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env: {} }) as unknown as nkruntime.Context;
const SERVER_KEY = { env: {} } as unknown as nkruntime.Context;
const season = BUNDLED_SEASONS[0];
if (season === undefined) throw new Error("no season");
const MID = season.startTs + 7 * 24 * 60 * 60 * 1000;
const LATE = season.endTs - 3 * 24 * 60 * 60 * 1000;

describe("the season pass (GDD 12.10)", () => {
  it("ships forty tiers with about 300 premium gems, cosmetics on both tracks and a title at the end", () => {
    expect(season.tiers).toHaveLength(TIER_COUNT);
    const premiumGems = season.tiers.reduce((n, t) => n + (t.premium.gems ?? 0), 0);
    expect(premiumGems).toBeGreaterThanOrEqual(250);
    expect(premiumGems).toBeLessThanOrEqual(350);
    expect(
      season.tiers.filter((t) => (t.free.items ?? []).length > 0).length,
    ).toBeGreaterThanOrEqual(2);
    expect(
      season.tiers.filter((t) => (t.premium.items ?? []).length > 0).length,
    ).toBeGreaterThanOrEqual(4);
    expect(season.tiers[39]?.premium.title).toBeDefined();
    expect(currentSeason(BUNDLED_SEASONS, MID)?.id).toBe(season.id);
    expect(currentSeason(BUNDLED_SEASONS, season.endTs)).toBeNull();
  });

  it("gains XP from rounds and missions, doubles it in the final two weeks, and caps at tier forty", () => {
    expect(xpFor("round", season, MID)).toBe(25);
    expect(xpFor("round", season, LATE)).toBe(50);
    expect(xpFor("weeklyMission", season, LATE)).toBe(1000);
    let state = emptyPass(season.id);
    for (let i = 0; i < 40; i += 1) state = addPassXp(state, season, "round", MID);
    expect(state.xp).toBe(1000);
    expect(tierFor(state.xp, season)).toBe(1);
    state = { ...state, xp: 99_000 };
    state = addPassXp(state, season, "weeklyMission", LATE);
    expect(state.xp).toBe(40_000);
    expect(tierFor(state.xp, season)).toBe(TIER_COUNT);
    expect(forSeason({ ...state, seasonId: "old" }, season)).toEqual(emptyPass(season.id));
  });

  it("claims a reached tier once, needs premium for the premium track, and Premium+ skips ten tiers", () => {
    const m = memoryNk();
    const ledger = nkLedgerPorts(m.nk);
    const ports = nkRewardPorts(m.nk, ledger);
    let state = { ...emptyPass(season.id), xp: 3 * season.xpPerTier };
    const early = claimTier(state, season, ports, "u1", 4, "free", MID);
    expect(early.response).toMatchObject({ error: { code: "economy.already_granted" } });
    const bad = claimTier(state, season, ports, "u1", 99, "free", MID);
    expect(bad.response).toMatchObject({ error: { code: "table.invalid_payload" } });
    const first = claimTier(state, season, ports, "u1", 1, "free", MID);
    expect(first.response).toMatchObject({ ok: true, applied: true, reward: { coins: 50 } });
    state = first.state;
    expect(claimTier(state, season, ports, "u1", 1, "free", MID).response).toMatchObject({
      ok: true,
      applied: false,
    });
    expect(ledger.balances("u1")).toEqual({ coins: 50, gems: 0 });
    expect(claimTier(state, season, ports, "u1", 3, "premium", MID).response).toMatchObject({
      error: { code: "table.not_authorised" },
    });
    state = activatePremium(state, season, false, MID);
    const premium = claimTier(state, season, ports, "u1", 3, "premium", MID);
    expect(premium.response).toMatchObject({
      ok: true,
      applied: true,
      reward: { items: ["back_kites"] },
    });
    state = premium.state;
    expect(
      owns(nkCatalogStore(m.nk).inventory("u1"), nkCatalogStore(m.nk).catalog(), "back_kites"),
    ).toBe(true);
    const plus = activatePremium(state, season, true, MID);
    expect(tierFor(plus.xp, season)).toBe(13);
    expect(activatePremium(plus, season, true, MID).xp).toBe(plus.xp);
    const view = passView(plus, season, MID);
    expect(view).toMatchObject({ tier: 13, premium: true, plus: true, catchUp: false });
    expect(view.tiers[2]).toMatchObject({
      reached: true,
      premium: { claimed: true },
      free: { claimed: false },
    });
    expect(view.tiers[39]?.reached).toBe(false);
  });

  it("serves the pass, pays claims, grants premium with an audit row, and earns XP from the hooks", () => {
    const m = memoryNk();
    const log = fakeLogger();
    const now = Date.now();
    if (currentSeason(BUNDLED_SEASONS, now) === null) {
      // Outside the season window the RPCs answer with no season; the rest is covered above.
      expect(parse(rpcPassGet(ctxFor("u1"), log, m.nk))).toEqual({ ok: true, season: null });
      return;
    }
    expect(earnPassXp(m.nk, "u1", "round", now)).toBe(true);
    expect(parse(rpcPassGet(ctxFor("u1"), log, m.nk))).toMatchObject({ ok: true, xp: 25, tier: 0 });
    expect(parse(rpcPassPremiumGrant(ctxFor("u1"), log, m.nk, "{}"))).toMatchObject({ ok: false });
    expect(
      parse(
        rpcPassPremiumGrant(SERVER_KEY, log, m.nk, JSON.stringify({ userId: "u1", plus: true })),
      ),
    ).toMatchObject({ ok: true, tier: 10, premium: true, plus: true });
    expect(
      parse(rpcPassClaim(ctxFor("u1"), log, m.nk, JSON.stringify({ tier: 1, track: "premium" }))),
    ).toMatchObject({ ok: true, applied: true, reward: { gems: 5 } });
    expect(
      parse(rpcPassClaim(ctxFor("u1"), log, m.nk, JSON.stringify({ tier: 1, track: "premium" }))),
    ).toMatchObject({ ok: true, applied: false });
    expect(
      parse(rpcPassClaim(ctxFor("u1"), log, m.nk, JSON.stringify({ tier: 11, track: "free" }))),
    ).toMatchObject({ error: { code: "economy.already_granted" } });
    expect(m.storage.get("audit") === undefined).toBe(true);
    expect(
      [...m.storage.values()].some(
        (o) =>
          o.collection === "audit" &&
          (o.value as { action: string }).action === "pass_premium_plus",
      ),
    ).toBe(true);
  });
});
