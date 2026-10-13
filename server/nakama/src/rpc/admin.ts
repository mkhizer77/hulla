import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { log } from "../infra/log";
import { ROUND_LOG_COLLECTION } from "../match/nakama_match";
import { gameEntry } from "../match/registry";
import { replayRound } from "../match/replay";
import { credit, nkLedgerPorts } from "../services/ledger";
import {
  applySanction,
  audit,
  banDevices,
  isSanctionAction,
  liftDevices,
  listAudit,
  readModeration,
  sanctionsView,
  untilFor,
  writeModeration,
} from "../services/sanctions";

import type { RoundRecord } from "../match/types";

const SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000";

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

/** The server key (no session) or an owner account listed in `HULLA_ADMIN_USER_IDS`. */
export function isAdmin(ctx: nkruntime.Context): boolean {
  const userId = ctx.userId ?? "";
  if (userId === "") return true;
  const listed = (ctx.env["HULLA_ADMIN_USER_IDS"] ?? "")
    .split(",")
    .map((id) => id.trim())
    .filter((id) => id !== "");
  return listed.includes(userId);
}

const actorOf = (ctx: nkruntime.Context): string =>
  ctx.userId === undefined || ctx.userId === "" ? "server_key" : ctx.userId;

const refused = (): string =>
  JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });

/** `{ matchId }`: the live table as the match handler sees it, with its label. */
export function rpcAdminTableInspect(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (!isAdmin(ctx)) return refused();
  const matchId = str(parse(payload)["matchId"]);
  if (matchId === "") {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "match_id" }),
    });
  }
  const match = nk.matchGet(matchId);
  if (match === null) {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notFound) });
  }
  const answer: unknown = JSON.parse(nk.matchSignal(matchId, JSON.stringify({ kind: "inspect" })));
  audit(nk, {
    actorId: actorOf(ctx),
    action: "table_inspect",
    targetId: null,
    detail: { matchId },
    ts: Date.now(),
  });
  return JSON.stringify({ ok: true, label: JSON.parse(match.label) as unknown, table: answer });
}

/** `{ tableId, roundId }`: replays the round from its log and says whether the hashes agree. */
export function rpcAdminRoundReplay(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (!isAdmin(ctx)) return refused();
  const input = parse(payload);
  const tableId = str(input["tableId"]);
  const roundId = str(input["roundId"]);
  if (tableId === "" || roundId === "") {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "round" }),
    });
  }
  const objects = nk.storageRead([
    { collection: ROUND_LOG_COLLECTION, key: `${tableId}:${roundId}`, userId: SYSTEM_USER_ID },
  ]);
  const record = objects[0]?.value as RoundRecord | undefined;
  if (record === undefined) {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notFound) });
  }
  const entry = gameEntry(record.gameId);
  if (entry === null) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "game_id" }),
    });
  }
  const replay = replayRound(entry.host, record);
  audit(nk, {
    actorId: actorOf(ctx),
    action: "round_replay",
    targetId: null,
    detail: { tableId, roundId, matches: replay.ok ? replay.value.matches : null },
    ts: Date.now(),
  });
  return JSON.stringify(
    replay.ok
      ? { ok: true, ...replay.value, players: record.players }
      : { ok: false, error: replay.error },
  );
}

/**
 * `{ userId, action, reason, hours?, permanent?, device? }`: a warning, mute, voice suspension,
 * ban (with the account's devices when `device` is set) or `clear`; audited.
 */
export function rpcAdminSanction(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (!isAdmin(ctx)) return refused();
  const input = parse(payload);
  const userId = str(input["userId"]);
  const action = input["action"];
  if (userId === "" || !isSanctionAction(action)) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "action" }),
    });
  }
  const now = Date.now();
  const hours =
    typeof input["hours"] === "number" && Number.isFinite(input["hours"]) ? input["hours"] : null;
  const permanent = input["permanent"] === true;
  const sanction = {
    action,
    reason: str(input["reason"]),
    hours,
    permanent,
    by: actorOf(ctx),
    now,
  };
  const next = applySanction(readModeration(nk, userId), sanction);
  writeModeration(nk, userId, next);
  let devices: string[] = [];
  if (action === "ban" || action === "clear") {
    try {
      devices = nk.accountGetId(userId).devices.map((d) => d.id);
    } catch (error: unknown) {
      log.warn(logger, "devices not listed", { error: String(error) });
    }
    if (action === "ban" && input["device"] === true) {
      banDevices(nk, devices, untilFor(sanction) ?? now, sanction.reason);
    }
    if (action === "clear") liftDevices(nk, devices);
  }
  if (action === "ban") {
    try {
      nk.sessionLogout(userId);
    } catch (error: unknown) {
      log.warn(logger, "sessions not ended", { error: String(error) });
    }
  }
  const entry = audit(nk, {
    actorId: actorOf(ctx),
    action: `sanction_${action}`,
    targetId: userId,
    detail: {
      reason: sanction.reason,
      hours,
      permanent,
      device: input["device"] === true,
      devices: devices.length,
    },
    ts: now,
  });
  return JSON.stringify({ ok: true, auditId: entry.id, sanctions: sanctionsView(next, now) });
}

/**
 * `{ userId, coins?, gems?, reason, sourceId }`: a support or compensation grant through the
 * ledger, idempotent on the source id and audited. Never a transfer: one account, one direction.
 */
export function rpcAdminGrant(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (!isAdmin(ctx)) return refused();
  const input = parse(payload);
  const userId = str(input["userId"]);
  const sourceId = str(input["sourceId"]);
  const coins = typeof input["coins"] === "number" ? input["coins"] : 0;
  const gems = typeof input["gems"] === "number" ? input["gems"] : 0;
  if (userId === "" || sourceId === "" || (coins <= 0 && gems <= 0)) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "grant" }),
    });
  }
  const paid = credit(nkLedgerPorts(nk), {
    userId,
    sourceId: `grant:${sourceId}`,
    reason: "grant",
    coins,
    gems,
    metadata: { by: actorOf(ctx), note: str(input["reason"]).slice(0, 64) },
  });
  if (paid.ok) {
    audit(nk, {
      actorId: actorOf(ctx),
      action: "grant",
      targetId: userId,
      detail: {
        coins,
        gems,
        sourceId,
        applied: paid.applied,
        reason: str(input["reason"]).slice(0, 64),
      },
      ts: Date.now(),
    });
  }
  return JSON.stringify(paid);
}

/** `{ limit? }`: the newest audit rows. */
export function rpcAdminAuditList(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (!isAdmin(ctx)) return refused();
  const limit = parse(payload)["limit"];
  const n = typeof limit === "number" && limit > 0 && limit <= 200 ? Math.floor(limit) : 50;
  return JSON.stringify({ ok: true, entries: listAudit(nk, n) });
}

/** The caller's own active sanctions, so the client can explain a mute or a ban. */
export function rpcSanctionsGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  return JSON.stringify({ ok: true, ...sanctionsView(readModeration(nk, userId), Date.now()) });
}
