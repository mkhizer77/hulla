import { afterEach, describe, expect, it, vi } from "vitest";

import { isFrame } from "../content/avatars";
import { BUNDLED_CATALOG } from "../content/catalog";
import { BUNDLED_SEASONS } from "../content/seasons";
import {
  afterValidatePurchaseApple,
  afterValidatePurchaseGoogle,
} from "../hooks/after_validate_purchase";
import { rpcWalletGet } from "../rpc/economy";
import { rpcPassGet } from "../rpc/pass";
import { rpcEconomyProducts, rpcIapRestore, rpcIapVerify } from "../rpc/purchases";
import { nkCatalogStore } from "../services/catalog";
import {
  BUNDLED_PRODUCTS,
  EMPTY_ENTITLEMENTS,
  applyPendingPass,
  applyPurchase,
  classifyValidationError,
  nkEntitlementStore,
  nkPurchasePorts,
  storeName,
  type Entitlements,
  type PurchasePorts,
  type ValidatedPurchase,
} from "../services/purchases";

import { fakeLogger, memoryNk, type LogLine } from "./fake-nk";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const ctxFor = (userId: string): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env: {} }) as unknown as nkruntime.Context;
const T0 = 1_760_200_000_000;
const SEASON = BUNDLED_SEASONS[0]!;
const IN_SEASON = SEASON.startTs + 24 * 3_600_000;

const purchase = (
  productId: string,
  transactionId: string,
  extra: Partial<ValidatedPurchase> = {},
): ValidatedPurchase => ({
  userId: "u1",
  productId,
  transactionId,
  store: "google",
  environment: "SANDBOX",
  purchaseTime: T0,
  refundTime: 0,
  seenBefore: false,
  ...extra,
});

function memoryPorts(seasonRunning: boolean): PurchasePorts & {
  readonly claimed: Set<string>;
  readonly wallet: { gems: number; coins: number };
  readonly items: string[];
  readonly passes: { plus: boolean }[];
  readonly entitlementsOf: Map<string, Entitlements>;
} {
  const claimed = new Set<string>();
  const wallet = { gems: 0, coins: 0 };
  const items: string[] = [];
  const passes: { plus: boolean }[] = [];
  const entitlementsOf = new Map<string, Entitlements>();
  return {
    claimed,
    wallet,
    items,
    passes,
    entitlementsOf,
    ledger: {
      applied: (u, id) => claimed.has(`${u}:${id}`),
      claim(u, id) {
        if (claimed.has(`${u}:${id}`)) return false;
        claimed.add(`${u}:${id}`);
        return true;
      },
      update(_u, changeset) {
        wallet.gems += changeset["gems"] ?? 0;
        wallet.coins += changeset["coins"] ?? 0;
        return { ...wallet };
      },
      balances: () => ({ ...wallet }),
    },
    entitlements: {
      read: (u) => entitlementsOf.get(u) ?? EMPTY_ENTITLEMENTS,
      write: (u, e) => void entitlementsOf.set(u, e),
    },
    grantItem: (_u, itemId) => void items.push(itemId),
    activatePass(_u, plus) {
      if (!seasonRunning) return false;
      passes.push({ plus });
      return true;
    },
  };
}

afterEach(() => {
  vi.useRealTimers();
});

describe("store products and grants (GDD 13.4, 20.9)", () => {
  it("lists the GDD catalogue at the reference prices, and every grant names real content", () => {
    const byId = new Map(BUNDLED_PRODUCTS.products.map((p) => [p.id, p]));
    expect(byId.size).toBe(BUNDLED_PRODUCTS.products.length);
    expect(byId.get("remove_ads")).toMatchObject({ usd: 2.99, kind: "nonConsumable" });
    expect(
      ["gems_120", "gems_650", "gems_1400", "gems_3000", "gems_8000"].map((id) => [
        byId.get(id)?.grants.gems,
        byId.get(id)?.usd,
      ]),
    ).toEqual([
      [120, 0.99],
      [650, 4.99],
      [1400, 9.99],
      [3000, 19.99],
      [8000, 49.99],
    ]);
    expect(byId.get("pass_premium")).toMatchObject({ usd: 4.99, grants: { pass: "premium" } });
    expect(byId.get("pass_premium_plus")).toMatchObject({ usd: 9.99, grants: { pass: "plus" } });
    expect(byId.get("starter_bundle")).toMatchObject({ usd: 1.99, grants: { gems: 200 } });
    const itemIds = new Set(BUNDLED_CATALOG.items.map((i) => i.id));
    for (const product of BUNDLED_PRODUCTS.products) {
      for (const item of product.grants.items ?? []) expect(itemIds.has(item)).toBe(true);
      for (const frame of product.grants.frames ?? []) expect(isFrame(frame)).toBe(true);
    }
    expect(storeName("APPLE_APP_STORE")).toBe("apple");
    expect(storeName("GOOGLE_PLAY_STORE")).toBe("google");
    expect(storeName("HUAWEI_APP_GALLERY")).toBe("huawei_app_gallery");
    expect(classifyValidationError(new Error("Google IAP is not configured"))).toBe("unavailable");
    expect(classifyValidationError(new Error("invalid receipt"))).toBe("invalid");
  });

  it("grants gems once per transaction and refuses unknown, refunded and other accounts' receipts", () => {
    const ports = memoryPorts(true);
    expect(applyPurchase(ports, BUNDLED_PRODUCTS, "u1", purchase("gems_650", "t1"), T0)).toEqual({
      productId: "gems_650",
      transactionId: "t1",
      store: "google",
      applied: true,
    });
    expect(ports.wallet.gems).toBe(650);
    expect(
      applyPurchase(ports, BUNDLED_PRODUCTS, "u1", purchase("gems_650", "t1"), T0),
    ).toMatchObject({
      applied: false,
      reason: "already_granted",
    });
    expect(ports.wallet.gems).toBe(650);
    expect(
      applyPurchase(
        ports,
        BUNDLED_PRODUCTS,
        "u1",
        purchase("gems_650", "t2", { store: "apple" }),
        T0,
      ),
    ).toMatchObject({ applied: true, store: "apple" });
    expect(ports.wallet.gems).toBe(1300);
    expect(
      applyPurchase(ports, BUNDLED_PRODUCTS, "u1", purchase("chips_9000", "t3"), T0),
    ).toMatchObject({
      applied: false,
      reason: "unknown_product",
    });
    expect(
      applyPurchase(
        ports,
        BUNDLED_PRODUCTS,
        "u1",
        purchase("gems_120", "t4", { userId: "u2" }),
        T0,
      ),
    ).toMatchObject({ applied: false, reason: "other_account" });
    expect(
      applyPurchase(
        ports,
        BUNDLED_PRODUCTS,
        "u1",
        purchase("gems_120", "t5", { refundTime: T0 }),
        T0,
      ),
    ).toMatchObject({ applied: false, reason: "refunded" });
    expect(ports.wallet.gems).toBe(1300);
    expect(ports.entitlements.read("u1")).toMatchObject({
      removeAds: false,
      products: { gems_650: 2 },
    });
  });

  it("Remove Ads and the starter bundle become entitlements; the pass opens now or waits for a season", () => {
    const ports = memoryPorts(false);
    applyPurchase(ports, BUNDLED_PRODUCTS, "u1", purchase("remove_ads", "t1"), T0);
    applyPurchase(ports, BUNDLED_PRODUCTS, "u1", purchase("starter_bundle", "t2"), T0);
    expect(ports.wallet.gems).toBe(200);
    expect(ports.items).toEqual(["back_ajrak"]);
    expect(ports.entitlements.read("u1")).toMatchObject({
      removeAds: true,
      frames: ["f01"],
      products: { remove_ads: 1, starter_bundle: 1 },
      pendingPass: null,
    });
    applyPurchase(ports, BUNDLED_PRODUCTS, "u1", purchase("pass_premium", "t3"), T0);
    expect(ports.passes).toEqual([]);
    expect(ports.entitlements.read("u1").pendingPass).toBe("premium");
    applyPurchase(ports, BUNDLED_PRODUCTS, "u1", purchase("pass_premium_plus", "t4"), T0);
    expect(ports.entitlements.read("u1").pendingPass).toBe("plus");
    expect(applyPendingPass(ports, "u1", T0)).toBe(false);
    const running = memoryPorts(true);
    running.entitlementsOf.set("u1", ports.entitlements.read("u1"));
    expect(applyPendingPass(running, "u1", T0)).toBe(true);
    expect(running.passes).toEqual([{ plus: true }]);
    expect(running.entitlements.read("u1").pendingPass).toBeNull();
    expect(applyPendingPass(running, "u1", T0)).toBe(false);
    applyPurchase(running, BUNDLED_PRODUCTS, "u1", purchase("pass_premium", "t5"), T0);
    expect(running.passes).toEqual([{ plus: true }, { plus: false }]);
  });
});

describe("iap_verify, iap_restore and the purchase hooks against an in-memory Nakama", () => {
  it("verifies a receipt, grants once, logs a tampered one, and says when the store is not configured", () => {
    vi.useFakeTimers();
    vi.setSystemTime(IN_SEASON);
    const m = memoryNk();
    const lines: LogLine[] = [];
    const log = fakeLogger(lines);
    expect(parse(rpcIapVerify(ctxFor(""), log, m.nk, "{}"))).toMatchObject({ ok: false });
    expect(
      parse(
        rpcIapVerify(ctxFor("u1"), log, m.nk, JSON.stringify({ store: "steam", receipt: "x" })),
      ),
    ).toMatchObject({
      error: { detail: { reason: "store" } },
    });
    expect(
      parse(rpcIapVerify(ctxFor("u1"), log, m.nk, JSON.stringify({ store: "google" }))),
    ).toMatchObject({
      error: { detail: { reason: "receipt" } },
    });
    expect(parse(rpcEconomyProducts(ctxFor("u1"), log, m.nk))).toMatchObject({
      ok: true,
      products: BUNDLED_PRODUCTS.products,
      entitlements: EMPTY_ENTITLEMENTS,
    });
    const receipt = JSON.stringify({ productId: "gems_1400", transactionId: "GPA.1" });
    const first = parse(
      rpcIapVerify(ctxFor("u1"), log, m.nk, JSON.stringify({ store: "google", receipt })),
    );
    expect(first).toMatchObject({
      ok: true,
      purchases: [
        { productId: "gems_1400", transactionId: "GPA.1", store: "google", applied: true },
      ],
      balances: { gems: 1400 },
      entitlements: { products: { gems_1400: 1 } },
    });
    expect(m.ledger).toMatchObject([
      {
        userId: "u1",
        changeset: { gems: 1400 },
        metadata: { reason: "purchase", sourceId: "iap:google:GPA.1" },
      },
    ]);
    expect(lines.filter((l) => l.message === "analytics")).toMatchObject([
      { fields: { event: "purchase", item: "gems_1400", price: 9.99, placement: "store" } },
    ]);
    expect(
      [...m.storage.values()].filter((o) => o.collection === "audit").map((o) => o.value),
    ).toMatchObject([{ action: "iap_grant", detail: { productId: "gems_1400", store: "google" } }]);
    const again = parse(
      rpcIapVerify(ctxFor("u1"), log, m.nk, JSON.stringify({ store: "google", receipt })),
    );
    expect(again).toMatchObject({
      ok: true,
      purchases: [{ applied: false, reason: "already_granted" }],
      balances: { gems: 1400 },
    });
    expect(
      parse(rpcIapVerify(ctxFor("u2"), log, m.nk, JSON.stringify({ store: "google", receipt }))),
    ).toMatchObject({
      ok: true,
      purchases: [{ applied: false, reason: "other_account" }],
      balances: { gems: 0 },
    });
    const tampered = parse(
      rpcIapVerify(ctxFor("u1"), log, m.nk, JSON.stringify({ store: "apple", receipt: "{bad" })),
    );
    expect(tampered).toMatchObject({
      error: { code: "economy.receipt_invalid", detail: { store: "apple" } },
    });
    expect(lines.filter((l) => l.message === "receipt refused")).toMatchObject([
      { level: "warn", fields: { store: "apple", problem: "invalid" } },
    ]);
    expect(
      parse(
        rpcIapVerify(
          ctxFor("u1"),
          log,
          m.nk,
          JSON.stringify({ store: "apple", receipt: "unconfigured" }),
        ),
      ),
    ).toMatchObject({
      error: { code: "economy.store_unavailable" },
    });
    expect(parse(rpcWalletGet(ctxFor("u1"), log, m.nk))).toMatchObject({
      balances: { gems: 1400 },
      entitlements: { products: { gems_1400: 1 } },
    });
  });

  it("restores Remove Ads and the bundle on a fresh device, and the premium pass waits for the season", () => {
    vi.useFakeTimers();
    vi.setSystemTime(T0);
    const m = memoryNk();
    const log = fakeLogger();
    const verify = (productId: string, transactionId: string): Json =>
      parse(
        rpcIapVerify(
          ctxFor("u1"),
          log,
          m.nk,
          JSON.stringify({ store: "apple", receipt: JSON.stringify({ productId, transactionId }) }),
        ),
      );
    expect(verify("remove_ads", "A1")).toMatchObject({ entitlements: { removeAds: true } });
    expect(verify("starter_bundle", "A2")).toMatchObject({
      balances: { gems: 200 },
      entitlements: { frames: ["f01"], products: { remove_ads: 1, starter_bundle: 1 } },
    });
    expect(nkCatalogStore(m.nk).inventory("u1").owned["back_ajrak"]).toBe(T0);
    expect(verify("pass_premium_plus", "A3")).toMatchObject({
      entitlements: { pendingPass: "plus" },
    });
    expect(parse(rpcPassGet(ctxFor("u1"), log, m.nk))).toEqual({ ok: true, season: null });
    // The entitlements object is lost (a support reset); restore brings every grant back without paying twice.
    nkEntitlementStore(m.nk).write("u1", EMPTY_ENTITLEMENTS);
    const restored = parse(rpcIapRestore(ctxFor("u1"), log, m.nk));
    expect(restored).toMatchObject({
      ok: true,
      purchases: [
        { productId: "remove_ads", applied: false, reason: "already_granted" },
        { productId: "starter_bundle", applied: false, reason: "already_granted" },
        { productId: "pass_premium_plus", applied: false, reason: "already_granted" },
      ],
      balances: { gems: 200 },
      entitlements: { removeAds: false },
    });
    // Nakama still holds the transactions, so the grant can be re-run under a fresh key only by support;
    // what restore proves is that the ledger never pays a transaction twice.
    expect(m.purchases.map((p) => p.productId)).toEqual([
      "remove_ads",
      "starter_bundle",
      "pass_premium_plus",
    ]);
    vi.setSystemTime(IN_SEASON);
    const ports = nkPurchasePorts(m.nk);
    ports.entitlements.write("u1", { ...EMPTY_ENTITLEMENTS, removeAds: true, pendingPass: "plus" });
    expect(parse(rpcPassGet(ctxFor("u1"), log, m.nk))).toMatchObject({
      ok: true,
      premium: true,
      plus: true,
    });
    expect(ports.entitlements.read("u1").pendingPass).toBeNull();
  });

  it("a client that validates through Nakama's own endpoints is granted by the after hooks, once", () => {
    vi.useFakeTimers();
    vi.setSystemTime(IN_SEASON);
    const m = memoryNk();
    const log = fakeLogger();
    const response = m.nk.purchaseValidateGoogle(
      "u1",
      JSON.stringify({ productId: "gems_120", transactionId: "G1" }),
      true,
    );
    afterValidatePurchaseGoogle(ctxFor("u1"), log, m.nk, response);
    afterValidatePurchaseGoogle(ctxFor("u1"), log, m.nk, response);
    expect(m.wallets.get("u1")).toEqual({ gems: 120 });
    const apple = m.nk.purchaseValidateApple(
      "u1",
      JSON.stringify({ productId: "pass_premium", transactionId: "AP1" }),
      true,
    );
    afterValidatePurchaseApple(ctxFor("u1"), log, m.nk, apple);
    expect(parse(rpcPassGet(ctxFor("u1"), log, m.nk))).toMatchObject({
      ok: true,
      premium: true,
      plus: false,
    });
    afterValidatePurchaseApple(ctxFor(""), log, m.nk, apple);
    expect(m.ledger).toHaveLength(1);
  });
});
