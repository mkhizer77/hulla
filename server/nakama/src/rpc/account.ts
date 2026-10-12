import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import {
  cancelDeletion,
  nkDeletionStore,
  nkPurgePorts,
  purgeDue,
  requestDeletion,
} from "../services/account_deletion";
import { nkProfileStore } from "../services/profiles";

/** Schedules the caller's account for deletion after the grace period (GDD 10.2). */
export function rpcAccountDelete(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  return JSON.stringify(requestDeletion(nkDeletionStore(nk), ctx.userId ?? null, Date.now()));
}

export function rpcAccountDeleteCancel(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  return JSON.stringify(cancelDeletion(nkDeletionStore(nk), ctx.userId ?? null));
}

/** The player's personal data: Nakama's account export (storage, wallet, friends, chat) plus the profile and any pending deletion. */
export function rpcAccountExport(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? null;
  if (userId === null) {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  const account: unknown = JSON.parse(nk.accountExportId(userId));
  return JSON.stringify({
    ok: true,
    exportedTs: Date.now(),
    profile: nkProfileStore(nk, logger).read(userId),
    deletion: nkDeletionStore(nk).read(userId),
    account,
  });
}

/**
 * Server-to-server (http key, no user): deletes every account whose grace period has passed.
 * `now` may be passed in tests to move the clock.
 */
export function rpcAdminPurgeDeleted(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (ctx.userId !== undefined && ctx.userId !== "") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  let input: { now?: unknown; dryRun?: unknown };
  try {
    input = (payload === "" ? {} : JSON.parse(payload)) as { now?: unknown; dryRun?: unknown };
  } catch {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "json" }),
    });
  }
  const now = typeof input.now === "number" && Number.isFinite(input.now) ? input.now : Date.now();
  const dryRun = input.dryRun === true;
  const store = nkDeletionStore(nk);
  if (dryRun) {
    return JSON.stringify({
      ok: true,
      dryRun: true,
      due: store.listDue(now),
      scanned: store.inspect?.() ?? [],
    });
  }
  const report = purgeDue(store, nkPurgePorts(nk, logger), now);
  logger.info("deletion purge", { purged: report.purged.length, failed: report.failed.length });
  return JSON.stringify({ ok: true, ...report });
}
