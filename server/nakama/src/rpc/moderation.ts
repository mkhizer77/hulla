import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { nkBlockPorts, nkBlockStore, setBlock } from "../services/blocks";
import { nkProfileStore } from "../services/profiles";
import {
  CHAT_LOG_COLLECTION,
  CHAT_RETENTION_MS,
  createReport,
  listReports,
  nkReportStore,
  SYSTEM_USER_ID,
} from "../services/reports";
import { nkSocialStore } from "../services/social";

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

const str = (value: unknown): string => (typeof value === "string" ? value : "");

/** `{ userId, blocked }`: a block is absolute, both ways, and follows the account. */
export function rpcBlockSet(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const input = parse(payload);
  return JSON.stringify(
    setBlock(
      nkBlockStore(nk),
      nkSocialStore(nk),
      nkBlockPorts(nk, logger),
      ctx.userId ?? null,
      str(input["userId"]),
      input["blocked"] === true,
    ),
  );
}

export function rpcBlocksGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? null;
  if (userId === null) {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  return JSON.stringify({ ok: true, blocked: nkBlockStore(nk).read(userId).blocked });
}

/** `{ userId, reason, tableId?, roundId? }`: the table's chat and the target's card ride along. */
export function rpcReportCreate(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const input = parse(payload);
  const tableId = str(input["tableId"]);
  const roundId = str(input["roundId"]);
  const response = createReport(nkReportStore(nk, nkProfileStore(nk, logger)), {
    reporterId: ctx.userId ?? null,
    targetId: str(input["userId"]),
    reason: str(input["reason"]),
    tableId: tableId === "" ? null : tableId,
    roundId: roundId === "" ? null : roundId,
    now: Date.now(),
  });
  if (response.ok) {
    logger.info("report filed", { reportId: response.reportId, autoMuted: response.autoMuted });
  }
  return JSON.stringify(response);
}

/** Triage: the newest reports, server key only. */
export function rpcReportsList(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (ctx.userId !== undefined && ctx.userId !== "") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  const limit = parse(payload)["limit"];
  const n = typeof limit === "number" && limit > 0 && limit <= 200 ? Math.floor(limit) : 50;
  return JSON.stringify({ ok: true, reports: listReports(nk, n) });
}

/** Drops chat logs untouched for seven days (GDD 11.1); server key only, for the cron sidecar. */
export function rpcAdminChatLogsPurge(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (ctx.userId !== undefined && ctx.userId !== "") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  const nowInput = parse(payload)["now"];
  const now = typeof nowInput === "number" && Number.isFinite(nowInput) ? nowInput : Date.now();
  const stale: string[] = [];
  let cursor: string | undefined;
  for (let page = 0; page < 10; page += 1) {
    const result = nk.storageList(SYSTEM_USER_ID, CHAT_LOG_COLLECTION, 100, cursor);
    const objects = result.objects ?? [];
    for (const object of objects) {
      if (object.collection !== CHAT_LOG_COLLECTION) continue;
      const updated = (object.value as { updatedTs?: unknown }).updatedTs;
      if (typeof updated !== "number" || now - updated > CHAT_RETENTION_MS) {
        if (!stale.includes(object.key)) stale.push(object.key);
      }
    }
    if (
      objects.length < 100 ||
      result.cursor === undefined ||
      result.cursor === "" ||
      result.cursor === cursor
    ) {
      break;
    }
    cursor = result.cursor;
  }
  if (stale.length > 0) {
    nk.storageDelete(
      stale.map((key) => ({ collection: CHAT_LOG_COLLECTION, key, userId: SYSTEM_USER_ID })),
    );
  }
  logger.info("chat logs purged", { count: stale.length });
  return JSON.stringify({ ok: true, purged: stale.length });
}
