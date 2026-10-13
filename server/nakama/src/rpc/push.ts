import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { METRICS, metrics } from "../infra/metrics";
import { emitAnalyticsSafely } from "../services/analytics";
import { isBlockedEitherWay, nkBlockStore } from "../services/blocks";
import { parseLabel, nkPresenceStore } from "../services/presence";
import { nkAreFriends } from "../services/profiles";
import {
  INVITE_RECIPIENTS_MAX,
  TABLE_INVITE_NOTIFICATION_CODE,
  applySettings,
  inviteAllowance,
  isPushType,
  nkInviteRateStore,
  nkPushStore,
  noteInvites,
  parseDevice,
  pushTo,
  registerDevice,
  unregisterDevice,
  type PushMessage,
  type PushType,
} from "../services/push";
import { displayNameOf, nkPushPorts, pushSweep } from "../services/push_triggers";

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

const refused = (): string =>
  JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });

const invalid = (reason: string): string =>
  JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason }) });

/** `{ installId, token, platform, locale?, tzOffsetMinutes? }`: registers or refreshes a device. */
export function rpcPushRegister(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const input = parseDevice(parse(payload));
  if (input === null) return invalid("device");
  const store = nkPushStore(nk);
  const next = registerDevice(store.read(userId), input, Date.now());
  store.write(userId, next);
  return JSON.stringify({
    ok: true,
    devices: Object.keys(next.devices).length,
    settings: next.settings,
  });
}

/** `{ installId }`: forgets a device, for sign-out on it. */
export function rpcPushUnregister(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const installId = parse(payload)["installId"];
  if (typeof installId !== "string" || installId === "") return invalid("install_id");
  const store = nkPushStore(nk);
  const current = store.read(userId);
  const next = unregisterDevice(current, installId, Date.now());
  if (next !== current) store.write(userId, next);
  return JSON.stringify({ ok: true, devices: Object.keys(next.devices).length });
}

export function rpcPushSettingsGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  return JSON.stringify({ ok: true, settings: nkPushStore(nk).read(userId).settings });
}

/** `{ types?, quietHours?, invitesFromNonFriends? }`: the per-type switches of GDD 10.10. */
export function rpcPushSettingsSet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const store = nkPushStore(nk);
  const result = applySettings(store.read(userId), parse(payload), Date.now());
  if (!result.ok) return JSON.stringify(result);
  store.write(userId, result.record);
  return JSON.stringify({ ok: true, settings: result.record.settings });
}

function uniqueIds(value: unknown): string[] | null {
  if (!Array.isArray(value)) return null;
  const ids: string[] = [];
  for (const id of value) {
    if (typeof id !== "string" || id === "") return null;
    if (!ids.includes(id)) ids.push(id);
  }
  return ids;
}

/**
 * `{ userIds }`: invites up to ten players to the table the caller sits at (GDD 10.5): an in-app
 * notification (1004) with the code, and a push that friends receive even in quiet hours. Twenty
 * invites an hour per sender; blocked players and those who muted invites from non-friends are
 * skipped and named in `skipped`.
 */
export function rpcTableInvite(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const targets = uniqueIds(parse(payload)["userIds"]);
  if (targets === null || targets.length === 0 || targets.length > INVITE_RECIPIENTS_MAX) {
    return invalid("user_ids");
  }
  const pointer = nkPresenceStore(nk).read(userId).table;
  if (pointer === null) {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notSeated) });
  }
  const match = nk.matchGet(pointer.matchId);
  const label = match === null ? null : parseLabel(match.label);
  if (match === null || label === null || label.status === "closed") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notFound) });
  }
  const now = Date.now();
  const rates = nkInviteRateStore(nk);
  const rate = rates.read(userId);
  let remaining = inviteAllowance(rate, now);
  if (remaining === 0) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.network.rateLimited, { reason: "invites", retryAfterS: 3600 }),
    });
  }
  const ports = nkPushPorts(nk, logger, ctx.env);
  const blocks = nkBlockStore(nk);
  const fromName = displayNameOf(nk, logger, userId);
  const sent: string[] = [];
  const skipped: Record<string, string> = {};
  for (const target of targets) {
    if (target === userId) {
      skipped[target] = "self";
      continue;
    }
    if (remaining === 0) {
      skipped[target] = "rate_limited";
      continue;
    }
    if (isBlockedEitherWay(blocks, userId, target)) {
      skipped[target] = "blocked";
      continue;
    }
    const friend = nkAreFriends(nk, userId, target);
    const record = ports.store.read(target);
    if (!friend && !record.settings.invitesFromNonFriends) {
      skipped[target] = "muted";
      continue;
    }
    try {
      nk.notificationSend(
        target,
        "table_invite",
        {
          matchId: pointer.matchId,
          code: label.code,
          gameId: label.gameId,
          mode: label.mode,
          fromUserId: userId,
          fromName,
          openSeats: label.open,
          sentTs: now,
        },
        TABLE_INVITE_NOTIFICATION_CODE,
        userId,
        false,
      );
    } catch {
      // the recipient's in-app notice is best effort; the push still goes
    }
    const message: PushMessage = {
      type: "table_invite",
      args: [fromName, label.code],
      data: { matchId: pointer.matchId, code: label.code, fromUserId: userId },
    };
    pushTo(ports.store, ports.sender, target, message, now, { bypassQuiet: friend });
    remaining -= 1;
    sent.push(target);
    emitAnalyticsSafely(logger, "invite_sent", { channel: "in_app" }, { tableId: label.code });
  }
  if (sent.length > 0) {
    rates.write(userId, noteInvites(rate, now, sent.length));
    metrics.count(nk, METRICS.invites, { channel: "in_app" }, sent.length);
  }
  return JSON.stringify({ ok: true, sent, skipped, remaining });
}

/** `{ limit? }`: the hourly sweep (server key or owner); the cron sidecar calls it. */
export function rpcAdminPushSweep(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (!isAdmin(ctx)) return refused();
  const limit = parse(payload)["limit"];
  const result = pushSweep(
    nk,
    logger,
    ctx.env,
    Date.now(),
    typeof limit === "number" && limit > 0 ? Math.min(limit, 20_000) : undefined,
  );
  return JSON.stringify({ ok: true, ...result });
}

const SAMPLE_ARGS: Readonly<Record<PushType, readonly string[]>> = {
  table_invite: ["Hulla", "TEST12"],
  friend_request: ["Hulla"],
  friend_online: ["Hulla"],
  daily_reward: [],
  season_end: ["12"],
};

/** `{ userId, type }`: sends one push regardless of settings, to check a device end to end. */
export function rpcAdminPushTest(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (!isAdmin(ctx)) return refused();
  const input = parse(payload);
  const userId = input["userId"];
  const type = input["type"];
  if (typeof userId !== "string" || userId === "") return invalid("user_id");
  if (!isPushType(type)) return invalid("type");
  const ports = nkPushPorts(nk, logger, ctx.env);
  const data: Record<string, string> =
    type === "table_invite"
      ? { matchId: "test", code: "TEST12", fromUserId: userId }
      : type === "season_end"
        ? { seasonId: "test" }
        : { fromUserId: userId };
  const report = pushTo(
    ports.store,
    ports.sender,
    userId,
    { type, args: SAMPLE_ARGS[type], data },
    Date.now(),
    { force: true },
  );
  return JSON.stringify({ ok: true, configured: ports.sender.configured, ...report });
}
