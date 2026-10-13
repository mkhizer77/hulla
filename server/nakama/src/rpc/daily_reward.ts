import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import {
  CALENDAR,
  claimDailyReward,
  dailyRewardView,
  invalidTz,
  nkDailyRewardStore,
} from "../services/daily_reward";
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

/** `{ tzOffsetMinutes? }`: today's reward, the next one, and whether today is still unclaimed. */
export function rpcDailyRewardGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const store = nkDailyRewardStore(nk);
  let state = store.read(userId);
  const offset = parse(payload)["tzOffsetMinutes"];
  if (invalidTz(offset) && offset !== state.tzOffsetMinutes) {
    state = { ...state, tzOffsetMinutes: offset };
    store.write(userId, state);
  }
  return JSON.stringify({ ok: true, ...dailyRewardView(state, CALENDAR, Date.now()) });
}

/** Claims today's reward through the ledger; a second claim today, from any device, is a no-op. */
export function rpcDailyRewardClaim(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const store = nkDailyRewardStore(nk);
  const current = store.read(userId);
  const result = claimDailyReward(current, CALENDAR, nkLedgerPorts(nk), userId, Date.now());
  if (result.state !== current) store.write(userId, result.state);
  return JSON.stringify(result.response);
}
