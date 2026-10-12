import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { nkLedgerPorts } from "../services/ledger";
import {
  claim,
  forPeriod,
  isMissionPool,
  missionsView,
  nkMissionsStore,
  reroll,
} from "../services/missions";

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

/** `{ tzOffsetMinutes? }`: today's three and this week's five with progress, drawn on first sight. */
export function rpcMissionsGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const store = nkMissionsStore(nk);
  const pool = store.pool();
  const now = Date.now();
  let record = store.read(userId);
  const offset = parse(payload)["tzOffsetMinutes"];
  if (typeof offset === "number" && Number.isInteger(offset) && Math.abs(offset) <= 14 * 60) {
    record = { ...record, tzOffsetMinutes: offset };
  }
  const next = forPeriod(record, pool, userId, now);
  if (next !== record || record.updatedTs === 0) store.write(userId, { ...next, updatedTs: now });
  return JSON.stringify({ ok: true, ...missionsView(next, pool) });
}

/** `{ missionId }`: pays a finished mission through the ledger, once. */
export function rpcMissionsClaim(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const store = nkMissionsStore(nk);
  const pool = store.pool();
  const now = Date.now();
  const missionId = parse(payload)["missionId"];
  const current = forPeriod(store.read(userId), pool, userId, now);
  const result = claim(
    current,
    pool,
    nkLedgerPorts(nk),
    userId,
    typeof missionId === "string" ? missionId : "",
    now,
  );
  if (result.record !== current) store.write(userId, result.record);
  return JSON.stringify(result.response);
}

/** `{ missionId }`: the free daily re-roll. */
export function rpcMissionsReroll(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const store = nkMissionsStore(nk);
  const pool = store.pool();
  const now = Date.now();
  const missionId = parse(payload)["missionId"];
  const current = forPeriod(store.read(userId), pool, userId, now);
  const result = reroll(current, pool, userId, typeof missionId === "string" ? missionId : "", now);
  if (result.record !== current) store.write(userId, result.record);
  return JSON.stringify(result.response);
}

/** Replaces the mission pool without a restart; the server key only. */
export function rpcMissionsSet(
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
  if (!isMissionPool(input)) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.content.schemaInvalid, { reason: "missions" }),
    });
  }
  nkMissionsStore(nk).writePool(input);
  logger.info("mission pool replaced", { id: input.id, version: input.version });
  return JSON.stringify({ ok: true, id: input.id, version: input.version });
}
