import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { ledgerLines, nkLedgerPorts } from "../services/ledger";
import { forDay, levelFor, nkProgressStore, touchLogin } from "../services/progression";
import { nkEntitlementStore } from "../services/purchases";

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

/** Balances and the newest ledger lines; coins and gems never move between accounts (D2). */
export function rpcWalletGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  return JSON.stringify({
    ok: true,
    balances: nkLedgerPorts(nk).balances(userId),
    ledger: ledgerLines(nk, userId, 50),
    entitlements: nkEntitlementStore(nk).read(userId),
  });
}

/**
 * `{ tzOffsetMinutes? }`: XP, level, titles, today's coins against the soft cap, the login streak
 * and the last round's reward block for the results screen (GDD 14.8). The client sends its
 * UTC offset so the daily counters roll at local midnight.
 */
export function rpcProgressGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  const store = nkProgressStore(nk);
  const now = Date.now();
  const offset = parse(payload)["tzOffsetMinutes"];
  let progress = store.read(userId);
  if (typeof offset === "number" && Number.isInteger(offset) && Math.abs(offset) <= 14 * 60) {
    progress = { ...progress, tzOffsetMinutes: offset };
  }
  const next = touchLogin(forDay(progress, now), now);
  if (next !== progress || progress.updatedTs === 0) store.write(userId, next);
  const level = levelFor(next.xp);
  return JSON.stringify({
    ok: true,
    xp: next.xp,
    level: level.level,
    intoLevel: level.intoLevel,
    toNext: level.toNext,
    titles: next.titles,
    coinsToday: next.coinsToday,
    loginStreak: next.loginStreak,
    lastReward: next.lastReward,
  });
}
