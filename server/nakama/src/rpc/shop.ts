import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { log } from "../infra/log";
import { emitAnalyticsSafely } from "../services/analytics";
import {
  catalogView,
  equip,
  isCatalog,
  itemById,
  nkCatalogStore,
  purchase,
} from "../services/catalog";
import { nkLedgerPorts } from "../services/ledger";

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

/** The catalog with prices, what the caller owns and has equipped, and this week's spotlight. */
export function rpcEconomyCatalog(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const store = nkCatalogStore(nk);
  emitAnalyticsSafely(logger, "shop_view", {});
  return JSON.stringify({
    ok: true,
    ...catalogView(store.inventory(userId), store.catalog(), Date.now()),
  });
}

/** `{ itemId }`: coins for common and rare, gems for epic and legendary, once per item. */
export function rpcEconomyPurchase(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const itemId = parse(payload)["itemId"];
  const store = nkCatalogStore(nk);
  const catalog = store.catalog();
  const current = store.inventory(userId);
  const result = purchase(
    current,
    catalog,
    nkLedgerPorts(nk),
    userId,
    typeof itemId === "string" ? itemId : "",
    Date.now(),
  );
  if (result.inventory !== current) store.writeInventory(userId, result.inventory);
  if (result.response.ok && result.response.applied) {
    const item = itemById(catalog, result.response.itemId);
    emitAnalyticsSafely(logger, "purchase", {
      item: result.response.itemId,
      price: item?.price.gems ?? item?.price.coins ?? 0,
      placement: item?.price.gems !== undefined ? "gems" : "coins",
    });
  }
  return JSON.stringify(result.response);
}

/** `{ itemId, gameId? }`: equips an owned item everywhere or for one game; it shows at the next round. */
export function rpcInventoryEquip(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const input = parse(payload);
  const itemId = input["itemId"];
  const gameId =
    typeof input["gameId"] === "string" && input["gameId"] !== "" ? input["gameId"] : null;
  const store = nkCatalogStore(nk);
  const current = store.inventory(userId);
  const result = equip(
    current,
    store.catalog(),
    typeof itemId === "string" ? itemId : "",
    gameId,
    Date.now(),
  );
  if (result.inventory !== current) store.writeInventory(userId, result.inventory);
  return JSON.stringify(result.response);
}

/** Replaces the catalog without a restart; the server key only. */
export function rpcCatalogSet(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (ctx.userId !== undefined && ctx.userId !== "") return refused();
  let input: unknown;
  try {
    input = JSON.parse(payload);
  } catch {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "json" }),
    });
  }
  if (!isCatalog(input)) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.content.schemaInvalid, { reason: "catalog" }),
    });
  }
  nkCatalogStore(nk).writeCatalog(input);
  log.info(logger, "catalog replaced", {
    id: input.id,
    version: input.version,
    items: input.items.length,
  });
  return JSON.stringify({ ok: true, id: input.id, version: input.version });
}
