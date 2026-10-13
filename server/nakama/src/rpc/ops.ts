import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { log } from "../infra/log";
import { liveTables, readDrain, writeDrain } from "../services/ops";
import { audit } from "../services/sanctions";

import { isAdmin } from "./admin";

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

/**
 * `{ on, reason? }`: turns draining on or off and reports the live tables, so a deploy can wait
 * for zero before retiring the node (docs/runbooks/deploy-and-rollback.md). Server key or owner.
 */
export function rpcAdminDrain(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (!isAdmin(ctx)) {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  const input = parse(payload);
  const now = Date.now();
  const current = readDrain(nk);
  if (typeof input["on"] === "boolean" && input["on"] !== current.draining) {
    const reason = typeof input["reason"] === "string" ? input["reason"].slice(0, 64) : "";
    writeDrain(
      nk,
      input["on"]
        ? { draining: true, since: now, reason }
        : { draining: false, since: 0, reason: "" },
    );
    audit(nk, {
      actorId: ctx.userId === undefined || ctx.userId === "" ? "server_key" : ctx.userId,
      action: input["on"] ? "drain_on" : "drain_off",
      targetId: null,
      detail: { reason },
      ts: now,
    });
    log.info(logger, input["on"] ? "draining: no new tables" : "draining ended", { reason });
  }
  const state = readDrain(nk);
  return JSON.stringify({
    ok: true,
    draining: state.draining,
    since: state.since,
    liveTables: liveTables(nk),
  });
}
