import { createHash } from "node:crypto";

import { afterEach, describe, expect, it, vi } from "vitest";

import { BUNDLED_CATALOG } from "../content/catalog";
import { rpcAdReward, rpcAdsGet } from "../rpc/ads";
import { rpcConfigSet } from "../rpc/config";
import { rpcMissionsGet, rpcMissionsReroll } from "../rpc/missions";
import { rpcIapVerify } from "../rpc/purchases";
import { rpcEconomyCatalog, rpcInventoryEquip } from "../rpc/shop";
import {
  DEFAULT_CAPS,
  EMPTY_AD_RECORD,
  EVENT_KEY_ENV,
  TRIAL_MS,
  applyAdReward,
  eventToken,
  forDay,
  nkAdStore,
  parseCustomData,
  spendRerollCredit,
  verifyEventToken,
  type AdPorts,
} from "../services/ads";
import {
  equippedFor,
  nkCatalogStore,
  owns,
  startTrial,
  EMPTY_INVENTORY,
} from "../services/catalog";
import { DEFAULT_CONFIG } from "../services/config";
import { EMPTY_PROGRESS, nkProgressStore, type LastReward } from "../services/progression";

import { fakeLogger, memoryNk, type LogLine, type MemoryNk } from "./fake-nk";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const KEY = "event-key-123";
const ENV = { [EVENT_KEY_ENV]: KEY, HULLA_REGION: "test" };
/** 10:00 in Pakistan. */
const T_DAY = Date.UTC(2026, 9, 13, 5, 0, 0);
const ctxFor = (userId: string, env: Record<string, string> = ENV): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env }) as unknown as nkruntime.Context;
/** AppLovin's GET: no session, the parameters in the query string. */
const callback = (
  params: Record<string, string>,
  env: Record<string, string> = ENV,
): nkruntime.Context =>
  ({
    env,
    queryParams: Object.fromEntries(Object.entries(params).map(([k, v]) => [k, [v]])),
  }) as unknown as nkruntime.Context;
const SERVER_KEY = { env: ENV } as unknown as nkruntime.Context;

const lastReward: LastReward = {
  tableId: "t_1",
  roundId: "r_1",
  xp: 40,
  coins: 60,
  levelFrom: 1,
  levelTo: 1,
  titles: [],
  grants: [],
  breakdown: {
    finish: 1,
    bhabhi: false,
    baseXp: 40,
    thullaXp: 0,
    cleanXp: 0,
    friendXp: 0,
    baseCoins: 60,
    streakCoins: 0,
    capped: false,
  },
};

function memoryPorts(
  coinsOfLastRound: number | null,
  caps = DEFAULT_CAPS,
): AdPorts & {
  readonly claimed: string[];
  readonly wallet: { coins: number };
  readonly inventories: Map<string, typeof EMPTY_INVENTORY>;
} {
  const claimed: string[] = [];
  const wallet = { coins: 0 };
  const inventories = new Map<string, typeof EMPTY_INVENTORY>();
  return {
    claimed,
    wallet,
    inventories,
    ledger: {
      applied: (u, id) => claimed.includes(`${u}:${id}`),
      claim(u, id) {
        if (claimed.includes(`${u}:${id}`)) return false;
        claimed.push(`${u}:${id}`);
        return true;
      },
      update(_u, changeset) {
        wallet.coins += changeset["coins"] ?? 0;
        return { coins: wallet.coins, gems: 0 };
      },
      balances: () => ({ coins: wallet.coins, gems: 0 }),
    },
    progress: {
      read: () => ({
        ...EMPTY_PROGRESS,
        lastReward: coinsOfLastRound === null ? null : { ...lastReward, coins: coinsOfLastRound },
      }),
    },
    catalog: {
      catalog: () => BUNDLED_CATALOG,
      writeCatalog: () => undefined,
      inventory: (u) => inventories.get(u) ?? EMPTY_INVENTORY,
      writeInventory: (u, inv) => void inventories.set(u, inv),
    },
    cap: (placement) => caps[placement],
  };
}

function seedLastReward(m: MemoryNk, userId: string): void {
  const store = nkProgressStore(m.nk);
  store.write(userId, { ...store.read(userId), lastReward, updatedTs: T_DAY });
}

afterEach(() => {
  vi.useRealTimers();
});

describe("the rewarded callback token and custom data (GDD 13.6)", () => {
  it("checks AppLovin's SHA-1 token of the event id and the event key", () => {
    const expected = createHash("sha1").update(`evt-1${KEY}`).digest("hex");
    expect(eventToken("evt-1", KEY)).toBe(expected);
    expect(verifyEventToken("evt-1", expected, KEY)).toBe(true);
    expect(verifyEventToken("evt-1", expected.toUpperCase(), KEY)).toBe(true);
    expect(verifyEventToken("evt-1", expected, "other")).toBe(false);
    expect(verifyEventToken("evt-2", expected, KEY)).toBe(false);
    expect(verifyEventToken("", expected, KEY)).toBe(false);
    expect(verifyEventToken("evt-1", "", KEY)).toBe(false);
    expect(verifyEventToken("evt-1", expected, "")).toBe(false);
  });

  it("parses the placements and refuses anything else", () => {
    expect(parseCustomData("double_coins:t_1:r_1")).toEqual({
      placement: "double_coins",
      tableId: "t_1",
      roundId: "r_1",
    });
    expect(parseCustomData("reroll")).toEqual({ placement: "reroll" });
    expect(parseCustomData("theme_trial:table_marble")).toEqual({
      placement: "theme_trial",
      itemId: "table_marble",
    });
    expect(parseCustomData("double_coins:t_1")).toBeNull();
    expect(parseCustomData("reroll:extra")).toBeNull();
    expect(parseCustomData("theme_trial")).toBeNull();
    expect(parseCustomData("jackpot")).toBeNull();
    expect(parseCustomData("")).toBeNull();
  });
});

describe("rewarded ad grants and caps", () => {
  it("doubles the last round's coins once per round, five times a day, once per event", () => {
    const ports = memoryPorts(60);
    const data = { placement: "double_coins" as const, tableId: "t_1", roundId: "r_1" };
    const first = applyAdReward(ports, EMPTY_AD_RECORD, "u1", data, "e1", T_DAY);
    expect(first.response).toEqual({
      ok: true,
      applied: true,
      placement: "double_coins",
      coins: 60,
      remaining: 4,
    });
    expect(ports.wallet.coins).toBe(60);
    expect(first.record).toMatchObject({ counts: { double_coins: 1 }, doubled: ["t_1:r_1"] });
    // Another ad on the same round pays nothing: one doubling per round.
    expect(applyAdReward(ports, first.record, "u1", data, "e1b", T_DAY).response).toMatchObject({
      error: { code: "economy.already_granted" },
    });
    expect(
      applyAdReward(ports, first.record, "u1", { ...data, roundId: "r_2" }, "e2", T_DAY).response,
    ).toMatchObject({
      error: { code: "table.invalid_payload", detail: { reason: "round" } },
    });
    expect(
      applyAdReward(memoryPorts(null), EMPTY_AD_RECORD, "u1", data, "e3", T_DAY).response,
    ).toMatchObject({
      error: { detail: { reason: "round" } },
    });
    let record = first.record;
    for (let i = 2; i <= 5; i += 1) {
      const p = memoryPorts(10);
      record = applyAdReward(
        p,
        { ...record, doubled: [] },
        "u1",
        data,
        `e${String(i)}`,
        T_DAY,
      ).record;
    }
    expect(record.counts.double_coins).toBe(5);
    expect(
      applyAdReward(memoryPorts(10), { ...record, doubled: [] }, "u1", data, "e9", T_DAY).response,
    ).toMatchObject({
      error: { code: "economy.daily_cap_reached", detail: { reason: "double_coins", cap: 5 } },
    });
    // The next local day starts clean.
    expect(forDay(record, T_DAY + 24 * 3_600_000)).toMatchObject({
      counts: { double_coins: 0 },
      doubled: [],
    });
    expect(forDay(record, T_DAY + 60_000)).toBe(record);
    // A repeated event id (AppLovin retries) applies nothing but answers ok.
    const dup = memoryPorts(60);
    dup.ledger.claim("u1", "ad:e1");
    expect(applyAdReward(dup, EMPTY_AD_RECORD, "u1", data, "e1", T_DAY).response).toEqual({
      ok: true,
      applied: false,
      placement: "double_coins",
      remaining: 5,
    });
  });

  it("earns re-roll credits up to two a day and spends them", () => {
    const ports = memoryPorts(null);
    const one = applyAdReward(ports, EMPTY_AD_RECORD, "u1", { placement: "reroll" }, "e1", T_DAY);
    expect(one.response).toMatchObject({ ok: true, applied: true, rerollCredits: 1, remaining: 1 });
    const two = applyAdReward(ports, one.record, "u1", { placement: "reroll" }, "e2", T_DAY);
    expect(two.response).toMatchObject({ rerollCredits: 2, remaining: 0 });
    expect(
      applyAdReward(ports, two.record, "u1", { placement: "reroll" }, "e3", T_DAY).response,
    ).toMatchObject({
      error: { code: "economy.daily_cap_reached" },
    });
    const spent = spendRerollCredit(two.record, T_DAY);
    expect(spent.rerollCredits).toBe(1);
    expect(spendRerollCredit({ ...spent, rerollCredits: 0 }, T_DAY).rerollCredits).toBe(0);
  });

  it("starts a 24-hour table trial once a day, for tables only, never for an owned one", () => {
    const ports = memoryPorts(null);
    const trial = applyAdReward(
      ports,
      EMPTY_AD_RECORD,
      "u1",
      { placement: "theme_trial", itemId: "table_marble" },
      "e1",
      T_DAY,
    );
    expect(trial.response).toEqual({
      ok: true,
      applied: true,
      placement: "theme_trial",
      itemId: "table_marble",
      until: T_DAY + TRIAL_MS,
      remaining: 0,
    });
    const inventory = ports.inventories.get("u1")!;
    expect(owns(inventory, BUNDLED_CATALOG, "table_marble", T_DAY + 1)).toBe(true);
    expect(owns(inventory, BUNDLED_CATALOG, "table_marble", T_DAY + TRIAL_MS + 1)).toBe(false);
    expect(owns(inventory, BUNDLED_CATALOG, "table_marble")).toBe(false);
    expect(
      equippedFor(
        { ...inventory, equipped: { table: "table_marble" } },
        BUNDLED_CATALOG,
        "thulla",
        T_DAY + 1,
      ).table,
    ).toBe("table_marble");
    expect(
      equippedFor(
        { ...inventory, equipped: { table: "table_marble" } },
        BUNDLED_CATALOG,
        "thulla",
        T_DAY + TRIAL_MS + 1,
      ).table,
    ).toBe("table_charpai");
    expect(
      applyAdReward(
        ports,
        trial.record,
        "u1",
        { placement: "theme_trial", itemId: "table_lahore_rooftop" },
        "e2",
        T_DAY,
      ).response,
    ).toMatchObject({
      error: { code: "economy.daily_cap_reached" },
    });
    const fresh = memoryPorts(null);
    expect(
      applyAdReward(
        fresh,
        EMPTY_AD_RECORD,
        "u1",
        { placement: "theme_trial", itemId: "back_ajrak" },
        "e3",
        T_DAY,
      ).response,
    ).toMatchObject({
      error: { code: "economy.item_unknown" },
    });
    expect(
      applyAdReward(
        fresh,
        EMPTY_AD_RECORD,
        "u1",
        { placement: "theme_trial", itemId: "table_charpai" },
        "e4",
        T_DAY,
      ).response,
    ).toMatchObject({
      error: { code: "economy.item_owned" },
    });
    const expired = startTrial(
      { ...EMPTY_INVENTORY, trials: { table_marble: T_DAY - 1 } },
      "table_dastarkhwan",
      T_DAY + 5,
      T_DAY,
    );
    expect(expired.trials).toEqual({ table_dastarkhwan: T_DAY + 5 });
  });
});

describe("ad_reward and ads_get against an in-memory Nakama", () => {
  it("rewards only a signed callback from the server key, once, and shows the caps", () => {
    vi.useFakeTimers();
    vi.setSystemTime(T_DAY);
    const m = memoryNk();
    const lines: LogLine[] = [];
    const log = fakeLogger(lines);
    seedLastReward(m, "u1");
    const token = eventToken("evt-1", KEY);
    const good = { user_id: "u1", event_id: "evt-1", token, custom: "double_coins:t_1:r_1" };
    // A session may not call it; a box without the key says so; a wrong token is refused with a 200.
    expect(parse(rpcAdReward(ctxFor("u1"), log, m.nk, "{}"))).toMatchObject({
      error: { code: "table.not_authorised" },
    });
    expect(
      parse(rpcAdReward(callback(good, { HULLA_REGION: "test" }), log, m.nk, "")),
    ).toMatchObject({
      error: { code: "economy.ads_unavailable" },
    });
    expect(
      parse(rpcAdReward(callback({ ...good, token: "deadbeef" }), log, m.nk, "")),
    ).toMatchObject({
      error: { code: "economy.ad_reward_invalid" },
    });
    expect(lines.filter((l) => l.message === "ad reward refused")).toHaveLength(1);
    expect(
      parse(rpcAdReward(callback({ ...good, custom: "jackpot" }), log, m.nk, "")),
    ).toMatchObject({
      error: { detail: { reason: "custom" } },
    });
    const first = parse(rpcAdReward(callback(good), log, m.nk, ""));
    expect(first).toEqual({
      ok: true,
      applied: true,
      placement: "double_coins",
      coins: 60,
      remaining: 4,
    });
    expect(m.wallets.get("u1")).toEqual({ coins: 60 });
    expect(m.ledger).toMatchObject([{ metadata: { reason: "rewarded_ad", sourceId: "ad:evt-1" } }]);
    expect(lines.filter((l) => l.message === "analytics")).toMatchObject([
      { fields: { event: "ad_reward", placement: "double_coins" } },
    ]);
    // AppLovin's retry of the same event changes nothing.
    expect(parse(rpcAdReward(callback(good), log, m.nk, ""))).toMatchObject({
      ok: true,
      applied: false,
    });
    expect(m.wallets.get("u1")).toEqual({ coins: 60 });
    // The JSON body works too, for a box check with curl.
    const body = {
      user_id: "u1",
      event_id: "evt-2",
      token: eventToken("evt-2", KEY),
      custom: "reroll",
    };
    expect(parse(rpcAdReward(SERVER_KEY, log, m.nk, JSON.stringify(body)))).toMatchObject({
      ok: true,
      rerollCredits: 1,
    });
    const view = parse(
      rpcAdsGet(ctxFor("u1"), log, m.nk, JSON.stringify({ tzOffsetMinutes: 300 })),
    );
    expect(view).toMatchObject({
      ok: true,
      rewardedEnabled: false,
      caps: DEFAULT_CAPS,
      used: { double_coins: 1, reroll: 1, theme_trial: 0 },
      rerollCredits: 1,
      interstitials: { enabled: true, fromRound: 4, intervalSeconds: 240 },
    });
    expect(parse(rpcAdsGet(ctxFor(""), log, m.nk, "{}"))).toMatchObject({ ok: false });
  });

  it("caps and the interstitial rules follow the remote configuration, and Remove Ads turns interstitials off", () => {
    vi.useFakeTimers();
    vi.setSystemTime(T_DAY);
    const m = memoryNk();
    const log = fakeLogger();
    expect(
      parse(
        rpcConfigSet(
          SERVER_KEY,
          log,
          m.nk,
          JSON.stringify({
            ...DEFAULT_CONFIG,
            version: 2,
            flags: {
              ...DEFAULT_CONFIG.flags,
              rewarded_ads: [{ value: true }],
              ad_cap_reroll: [{ value: 0 }, { region: "test", value: 1 }],
              interstitial_from_round: [{ value: 2 }],
              interstitials: [{ region: "test", value: false }],
            },
          }),
        ),
      ),
    ).toMatchObject({ ok: true });
    const view = parse(rpcAdsGet(ctxFor("u1"), log, m.nk, "{}"));
    expect(view).toMatchObject({
      rewardedEnabled: true,
      caps: { reroll: 1 },
      interstitials: { enabled: false, fromRound: 2 },
    });
    const first = parse(
      rpcAdReward(
        SERVER_KEY,
        log,
        m.nk,
        JSON.stringify({
          user_id: "u1",
          event_id: "a",
          token: eventToken("a", KEY),
          custom: "reroll",
        }),
      ),
    );
    expect(first).toMatchObject({ ok: true, remaining: 0 });
    expect(
      parse(
        rpcAdReward(
          SERVER_KEY,
          log,
          m.nk,
          JSON.stringify({
            user_id: "u1",
            event_id: "b",
            token: eventToken("b", KEY),
            custom: "reroll",
          }),
        ),
      ),
    ).toMatchObject({ error: { code: "economy.daily_cap_reached", detail: { cap: 1 } } });
    const bought = parse(
      rpcIapVerify(
        ctxFor("u2"),
        log,
        m.nk,
        JSON.stringify({
          store: "apple",
          receipt: JSON.stringify({ productId: "remove_ads", transactionId: "R1" }),
        }),
      ),
    );
    expect(bought).toMatchObject({ ok: true });
    const paid = parse(rpcAdsGet(ctxFor("u2", { HULLA_REGION: "eu" }), log, m.nk, "{}"));
    expect(paid).toMatchObject({ interstitials: { enabled: false }, caps: { reroll: 0 } });
  });

  it("an earned re-roll pays for a second mission swap, and a trial equips a table until it ends", () => {
    vi.useFakeTimers();
    vi.setSystemTime(T_DAY);
    const m = memoryNk();
    const log = fakeLogger();
    const missions = parse(
      rpcMissionsGet(ctxFor("u1"), log, m.nk, JSON.stringify({ tzOffsetMinutes: 300 })),
    );
    const daily = missions["daily"] as { id: string }[];
    const swap = (id: string): Json =>
      parse(rpcMissionsReroll(ctxFor("u1"), log, m.nk, JSON.stringify({ missionId: id })));
    expect(swap(daily[0]!.id)).toMatchObject({ ok: true, usedCredit: false });
    expect(swap(daily[1]!.id)).toMatchObject({ error: { code: "economy.daily_cap_reached" } });
    expect(
      parse(
        rpcAdReward(
          SERVER_KEY,
          log,
          m.nk,
          JSON.stringify({
            user_id: "u1",
            event_id: "x",
            token: eventToken("x", KEY),
            custom: "reroll",
          }),
        ),
      ),
    ).toMatchObject({ ok: true, rerollCredits: 1 });
    expect(swap(daily[1]!.id)).toMatchObject({ ok: true, usedCredit: true });
    expect(nkAdStore(m.nk).read("u1").rerollCredits).toBe(0);
    expect(swap(daily[2]!.id)).toMatchObject({ error: { code: "economy.daily_cap_reached" } });

    const trial = parse(
      rpcAdReward(
        SERVER_KEY,
        log,
        m.nk,
        JSON.stringify({
          user_id: "u1",
          event_id: "y",
          token: eventToken("y", KEY),
          custom: "theme_trial:table_marble",
        }),
      ),
    );
    expect(trial).toMatchObject({ ok: true, itemId: "table_marble", until: T_DAY + TRIAL_MS });
    const catalog = parse(rpcEconomyCatalog(ctxFor("u1"), log, m.nk));
    const marble = (catalog["items"] as { id: string; owned: boolean; trialUntil?: number }[]).find(
      (i) => i.id === "table_marble",
    );
    expect(marble).toMatchObject({ owned: false, trialUntil: T_DAY + TRIAL_MS });
    expect(
      parse(rpcInventoryEquip(ctxFor("u1"), log, m.nk, JSON.stringify({ itemId: "table_marble" }))),
    ).toMatchObject({ ok: true });
    expect(
      equippedFor(nkCatalogStore(m.nk).inventory("u1"), BUNDLED_CATALOG, "thulla", T_DAY + 1).table,
    ).toBe("table_marble");
    vi.setSystemTime(T_DAY + TRIAL_MS + 1);
    expect(
      equippedFor(nkCatalogStore(m.nk).inventory("u1"), BUNDLED_CATALOG, "thulla", Date.now())
        .table,
    ).toBe("table_charpai");
    expect(
      parse(rpcInventoryEquip(ctxFor("u1"), log, m.nk, JSON.stringify({ itemId: "table_marble" }))),
    ).toMatchObject({
      error: { detail: { reason: "not_owned" } },
    });
  });
});
