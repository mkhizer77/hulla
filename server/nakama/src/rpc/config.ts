import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { log } from "../infra/log";
import {
  clientTooOld,
  isRemoteConfig,
  nkConfigStore,
  resolveAll,
  type Audience,
} from "../services/config";

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

const str = (value: unknown, fallback: string): string =>
  typeof value === "string" && value !== "" ? value : fallback;

/**
 * `{ clientVersion?, cohort? }`: the flags and numbers for the caller's region (the box's
 * `HULLA_REGION`) and cohort, the minimum client version, and whether this client must update.
 * Open to guests and signed-out callers so the update prompt can show before sign-in.
 */
export function rpcConfigGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const input = parse(payload);
  const config = nkConfigStore(nk).read();
  const audience: Audience = {
    region: str(ctx.env["HULLA_REGION"], "local"),
    cohort: str(input["cohort"], "*"),
  };
  return JSON.stringify({
    ok: true,
    version: config.version,
    region: audience.region,
    minClientVersion: config.minClientVersion,
    updateRequired: clientTooOld(input["clientVersion"], config),
    flags: resolveAll(config, audience),
  });
}

/** Replaces the configuration without a restart; the server key only. */
export function rpcConfigSet(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (ctx.userId !== undefined && ctx.userId !== "") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  let input: unknown;
  try {
    input = JSON.parse(payload);
  } catch {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "json" }),
    });
  }
  if (!isRemoteConfig(input)) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.content.schemaInvalid, { reason: "remote_config" }),
    });
  }
  const store = nkConfigStore(nk);
  const current = store.read();
  if (input.version <= current.version && current.updatedTs !== 0) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.content.manifestDowngrade, { current: current.version }),
    });
  }
  store.write({ ...input, updatedTs: Date.now() });
  log.info(logger, "remote config replaced", {
    version: input.version,
    minClientVersion: input.minClientVersion,
  });
  return JSON.stringify({ ok: true, version: input.version });
}
