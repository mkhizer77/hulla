import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { log } from "../infra/log";
import { METRICS, metrics } from "../infra/metrics";
import { emitAnalyticsSafely } from "../services/analytics";
import {
  BUNDLED_PRODUCTS,
  applyPurchase,
  classifyValidationError,
  isStore,
  listPurchases,
  nkPurchasePorts,
  productById,
  validateReceipt,
  type GrantResult,
  type ValidatedPurchase,
} from "../services/purchases";
import { audit } from "../services/sanctions";

function parse(payload: string): Record<string, unknown> {
  try {
    const value: unknown = payload === "" ? {} : JSON.parse(payload);
    return typeof value === "object" && value !== null && !Array.isArray(value)
      ? (value as Record<string, unknown>)
      : {};
  } catch {
    return {};
  }
}

const refused = (): string =>
  JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });

const RECEIPT_MAX = 64 * 1024;

/** The products for the store screen and Unity IAP's initialisation, with the caller's entitlements. */
export function rpcEconomyProducts(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  return JSON.stringify({
    ok: true,
    products: BUNDLED_PRODUCTS.products,
    entitlements: nkPurchasePorts(nk).entitlements.read(userId),
  });
}

/** Grants a batch of validated purchases and records each grant. */
export function grantAll(
  nk: nkruntime.Nakama,
  logger: nkruntime.Logger,
  userId: string,
  purchases: readonly ValidatedPurchase[],
  now: number,
): GrantResult[] {
  const ports = nkPurchasePorts(nk);
  return purchases.map((purchase) => {
    const result = applyPurchase(ports, BUNDLED_PRODUCTS, userId, purchase, now);
    if (!result.applied) return result;
    const product = productById(BUNDLED_PRODUCTS, purchase.productId);
    audit(nk, {
      actorId: userId,
      action: "iap_grant",
      targetId: userId,
      detail: {
        productId: purchase.productId,
        store: purchase.store,
        environment: purchase.environment,
        transactionId: purchase.transactionId,
      },
      ts: now,
    });
    metrics.count(nk, METRICS.purchases, { store: purchase.store, product: purchase.productId });
    emitAnalyticsSafely(logger, "purchase", {
      item: purchase.productId,
      price: product?.usd ?? 0,
      placement: "store",
    });
    return result;
  });
}

function summary(nk: nkruntime.Nakama, userId: string, purchases: GrantResult[]): string {
  const ports = nkPurchasePorts(nk);
  return JSON.stringify({
    ok: true,
    purchases,
    balances: ports.ledger.balances(userId),
    entitlements: ports.entitlements.read(userId),
  });
}

/**
 * `{ store: apple|google, receipt }`: Unity IAP's receipt payload (the base64 App Store receipt,
 * or Google's `{"json":...,"signature":...}` string). Nakama validates it with the store, the
 * server grants once per transaction; a tampered receipt is refused and logged.
 */
export function rpcIapVerify(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const input = parse(payload);
  const store = input["store"];
  const receipt = input["receipt"];
  if (!isStore(store)) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "store" }),
    });
  }
  if (typeof receipt !== "string" || receipt === "" || receipt.length > RECEIPT_MAX) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "receipt" }),
    });
  }
  let validated: ValidatedPurchase[];
  try {
    validated = validateReceipt(nk, store, userId, receipt);
  } catch (error: unknown) {
    const problem = classifyValidationError(error);
    log.warn(logger, "receipt refused", { store, problem, error: String(error) });
    metrics.count(nk, METRICS.purchasesRejected, { store, problem });
    return JSON.stringify({
      ok: false,
      error: ruleError(
        problem === "unavailable"
          ? ErrorCodes.economy.storeUnavailable
          : ErrorCodes.economy.receiptInvalid,
        { store },
      ),
    });
  }
  return summary(nk, userId, grantAll(nk, logger, userId, validated, Date.now()));
}

/** Restore purchases: every transaction Nakama holds for the account is granted again if missing. */
export function rpcIapRestore(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const purchases = listPurchases(nk, userId);
  return summary(nk, userId, grantAll(nk, logger, userId, purchases, Date.now()));
}
