import { log } from "../infra/log";
import { grantAll } from "../rpc/purchases";
import { fromNakama } from "../services/purchases";

/**
 * A client that validates through Nakama's own purchase endpoints gets the same grants as
 * `iap_verify`; the ledger key per transaction keeps the two paths from paying twice.
 */
function grantValidated(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  data: nkruntime.ValidatePurchaseResponse,
): void {
  const userId = ctx.userId ?? "";
  if (userId === "") return;
  try {
    grantAll(nk, logger, userId, (data.validatedPurchases ?? []).map(fromNakama), Date.now());
  } catch (error: unknown) {
    log.error(logger, "purchase grant failed", { error: String(error) });
  }
}

export function afterValidatePurchaseApple(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  data: nkruntime.ValidatePurchaseResponse,
): void {
  grantValidated(ctx, logger, nk, data);
}

export function afterValidatePurchaseGoogle(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  data: nkruntime.ValidatePurchaseResponse,
): void {
  grantValidated(ctx, logger, nk, data);
}
