import { BUNDLED_PRODUCTS, type Product, type Products } from "../content/products";

import { nkLedgerPorts, type LedgerPorts } from "./ledger";
import {
  BUNDLED_SEASONS,
  activatePremium,
  currentSeason,
  forSeason,
  nkPassStore,
  nkRewardPorts,
} from "./season_pass";

/**
 * In-app purchases (GDD 13.4, 20.9; ARCHITECTURE 8.10): Nakama validates the Apple or Google
 * receipt, the server grants what the product says through the ledger and the inventory, once per
 * transaction id, and keeps the entitlements (Remove Ads, frames, what was bought) per account so
 * a reinstall or a second device restores them. No wagering, no transfers (D2).
 */
export const ENTITLEMENTS_COLLECTION = "entitlements";
export const STORES = ["apple", "google"] as const;
export type Store = (typeof STORES)[number];

export interface Entitlements {
  readonly removeAds: boolean;
  readonly frames: readonly string[];
  /** Product id to the number of granted transactions. */
  readonly products: Readonly<Record<string, number>>;
  /** A premium pass bought while no season runs waits here until one starts. */
  readonly pendingPass: "premium" | "plus" | null;
  readonly updatedTs: number;
}

export const EMPTY_ENTITLEMENTS: Entitlements = {
  removeAds: false,
  frames: [],
  products: {},
  pendingPass: null,
  updatedTs: 0,
};

/** A purchase as Nakama returns it, with the store named plainly. */
export interface ValidatedPurchase {
  readonly userId: string;
  readonly productId: string;
  readonly transactionId: string;
  readonly store: string;
  readonly environment: string;
  readonly purchaseTime: number;
  readonly refundTime: number;
  readonly seenBefore: boolean;
}

export interface EntitlementStore {
  read(userId: string): Entitlements;
  write(userId: string, entitlements: Entitlements): void;
}

export interface PurchasePorts {
  readonly ledger: LedgerPorts;
  readonly entitlements: EntitlementStore;
  grantItem(userId: string, itemId: string, now: number): void;
  /** False when no season runs; the premium then waits in `pendingPass`. */
  activatePass(userId: string, plus: boolean, now: number): boolean;
}

export type GrantReason = "unknown_product" | "other_account" | "refunded" | "already_granted";

export interface GrantResult {
  readonly productId: string;
  readonly transactionId: string;
  readonly store: string;
  readonly applied: boolean;
  readonly reason?: GrantReason;
}

export function productById(products: Products, id: string): Product | null {
  return products.products.find((p) => p.id === id) ?? null;
}

/** The ledger's idempotency key: a transaction is granted once however often it is sent. */
export function sourceIdFor(purchase: ValidatedPurchase): string {
  return `iap:${purchase.store}:${purchase.transactionId}`;
}

export function isStore(value: unknown): value is Store {
  return value === "apple" || value === "google";
}

/** Grants one validated purchase to the caller; a repeat, a refund or another account's receipt grants nothing. */
export function applyPurchase(
  ports: PurchasePorts,
  products: Products,
  callerId: string,
  purchase: ValidatedPurchase,
  now: number,
): GrantResult {
  const base = {
    productId: purchase.productId,
    transactionId: purchase.transactionId,
    store: purchase.store,
  };
  const product = productById(products, purchase.productId);
  if (product === null) return { ...base, applied: false, reason: "unknown_product" };
  if (purchase.userId !== "" && purchase.userId !== callerId) {
    return { ...base, applied: false, reason: "other_account" };
  }
  if (purchase.refundTime > 0) return { ...base, applied: false, reason: "refunded" };
  const sourceId = sourceIdFor(purchase);
  if (!ports.ledger.claim(callerId, sourceId)) {
    return { ...base, applied: false, reason: "already_granted" };
  }
  const grants = product.grants;
  if (grants.gems !== undefined && grants.gems > 0) {
    ports.ledger.update(
      callerId,
      { gems: grants.gems },
      { reason: "purchase", sourceId, productId: product.id, store: purchase.store },
    );
  }
  for (const itemId of grants.items ?? []) ports.grantItem(callerId, itemId, now);
  const current = ports.entitlements.read(callerId);
  let pendingPass = current.pendingPass;
  if (grants.pass !== undefined) {
    const plus = grants.pass === "plus";
    if (!ports.activatePass(callerId, plus, now)) {
      pendingPass = pendingPass === "plus" ? "plus" : grants.pass;
    }
  }
  const frames = [...current.frames];
  for (const frame of grants.frames ?? []) if (!frames.includes(frame)) frames.push(frame);
  ports.entitlements.write(callerId, {
    removeAds: current.removeAds || (grants.entitlements ?? []).includes("removeAds"),
    frames,
    products: { ...current.products, [product.id]: (current.products[product.id] ?? 0) + 1 },
    pendingPass,
    updatedTs: now,
  });
  return { ...base, applied: true };
}

/** Opens a premium pass bought between seasons once a season runs; true when it was applied. */
export function applyPendingPass(ports: PurchasePorts, userId: string, now: number): boolean {
  const current = ports.entitlements.read(userId);
  if (current.pendingPass === null) return false;
  if (!ports.activatePass(userId, current.pendingPass === "plus", now)) return false;
  ports.entitlements.write(userId, { ...current, pendingPass: null, updatedTs: now });
  return true;
}

export function isEntitlements(value: unknown): value is Entitlements {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v["removeAds"] === "boolean" &&
    Array.isArray(v["frames"]) &&
    typeof v["products"] === "object" &&
    v["products"] !== null
  );
}

export function nkEntitlementStore(nk: nkruntime.Nakama): EntitlementStore {
  return {
    read(userId) {
      const objects = nk.storageRead([
        { collection: ENTITLEMENTS_COLLECTION, key: "state", userId },
      ]);
      const value: unknown = objects[0]?.value;
      if (!isEntitlements(value)) return EMPTY_ENTITLEMENTS;
      const pending = (value as { pendingPass?: unknown }).pendingPass;
      return {
        removeAds: value.removeAds,
        frames: value.frames.filter((f): f is string => typeof f === "string"),
        products: value.products,
        pendingPass: pending === "premium" || pending === "plus" ? pending : null,
        updatedTs: typeof value.updatedTs === "number" ? value.updatedTs : 0,
      };
    },
    write(userId, entitlements) {
      nk.storageWrite([
        {
          collection: ENTITLEMENTS_COLLECTION,
          key: "state",
          userId,
          value: {
            removeAds: entitlements.removeAds,
            frames: [...entitlements.frames],
            products: entitlements.products,
            pendingPass: entitlements.pendingPass,
            updatedTs: entitlements.updatedTs,
          },
          permissionRead: 1,
          permissionWrite: 0,
        },
      ]);
    },
  };
}

export function nkPurchasePorts(nk: nkruntime.Nakama): PurchasePorts {
  const ledger = nkLedgerPorts(nk);
  const rewards = nkRewardPorts(nk, ledger);
  return {
    ledger,
    entitlements: nkEntitlementStore(nk),
    grantItem: (userId, itemId, now) => {
      rewards.grantItem(userId, itemId, now);
    },
    activatePass(userId, plus, now) {
      const season = currentSeason(BUNDLED_SEASONS, now);
      if (season === null) return false;
      const store = nkPassStore(nk);
      const state = forSeason(store.read(userId), season);
      store.write(userId, activatePremium(state, season, plus, now));
      return true;
    },
  };
}

/** `APPLE_APP_STORE` → `apple`, `GOOGLE_PLAY_STORE` → `google`. */
export function storeName(nakamaStore: string): string {
  if (nakamaStore === "APPLE_APP_STORE") return "apple";
  if (nakamaStore === "GOOGLE_PLAY_STORE") return "google";
  return nakamaStore.toLowerCase();
}

export function fromNakama(purchase: nkruntime.ValidatedPurchase): ValidatedPurchase {
  return {
    userId: purchase.userId,
    productId: purchase.productId,
    transactionId: purchase.transactionId,
    store: storeName(purchase.store),
    environment: purchase.environment,
    purchaseTime: purchase.purchaseTime,
    refundTime: purchase.refundTime,
    seenBefore: purchase.seenBefore,
  };
}

export type ReceiptProblem = "unavailable" | "invalid";

/** A box without the store credentials says so; anything else is a bad receipt. */
export function classifyValidationError(error: unknown): ReceiptProblem {
  const message = String(error).toLowerCase();
  return message.includes("not configured") || message.includes("not set")
    ? "unavailable"
    : "invalid";
}

/** Nakama's own validators (ARCHITECTURE 8.10); the purchase is persisted so a repeat is seen. */
export function validateReceipt(
  nk: nkruntime.Nakama,
  store: Store,
  userId: string,
  receipt: string,
): ValidatedPurchase[] {
  const response =
    store === "apple"
      ? nk.purchaseValidateApple(userId, receipt, true)
      : nk.purchaseValidateGoogle(userId, receipt, true);
  return (response.validatedPurchases ?? []).map(fromNakama);
}

const PAGE = 100;

/** Every purchase Nakama holds for the account, with the usual guard against a cursor on the last page. */
export function listPurchases(nk: nkruntime.Nakama, userId: string): ValidatedPurchase[] {
  const out: ValidatedPurchase[] = [];
  let cursor: string | undefined;
  for (let page = 0; page < 10; page += 1) {
    const list = nk.purchasesList(userId, PAGE, cursor);
    const purchases = list.validatedPurchases ?? [];
    for (const purchase of purchases) out.push(fromNakama(purchase));
    if (
      purchases.length < PAGE ||
      list.cursor === undefined ||
      list.cursor === "" ||
      list.cursor === cursor
    ) {
      break;
    }
    cursor = list.cursor;
  }
  return out;
}

export { BUNDLED_PRODUCTS };
