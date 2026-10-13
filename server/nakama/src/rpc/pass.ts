import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { emitAnalyticsSafely } from "../services/analytics";
import { nkLedgerPorts } from "../services/ledger";
import { applyPendingPass, nkPurchasePorts } from "../services/purchases";
import { audit } from "../services/sanctions";
import {
  activatePremium,
  BUNDLED_SEASONS,
  claimTier,
  currentSeason,
  forSeason,
  nkPassStore,
  nkRewardPorts,
  passView,
} from "../services/season_pass";

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

const noSeason = (): string => JSON.stringify({ ok: true, season: null });

/** The running season's forty tiers with the caller's XP, tier, tracks and claims. */
export function rpcPassGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const now = Date.now();
  const season = currentSeason(BUNDLED_SEASONS, now);
  if (season === null) return noSeason();
  // A premium pass bought between seasons opens now (#58).
  applyPendingPass(nkPurchasePorts(nk), userId, now);
  const state = forSeason(nkPassStore(nk).read(userId), season);
  return JSON.stringify({ ok: true, ...passView(state, season, now) });
}

/** `{ tier, track }`: claims a reached tier once; premium needs the premium pass. */
export function rpcPassClaim(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const now = Date.now();
  const season = currentSeason(BUNDLED_SEASONS, now);
  if (season === null) return noSeason();
  const input = parse(payload);
  const tier = typeof input["tier"] === "number" ? input["tier"] : 0;
  const track = input["track"] === "premium" ? "premium" : "free";
  const store = nkPassStore(nk);
  const state = forSeason(store.read(userId), season);
  const result = claimTier(
    state,
    season,
    nkRewardPorts(nk, nkLedgerPorts(nk)),
    userId,
    tier,
    track,
    now,
  );
  if (result.state !== state) store.write(userId, result.state);
  if (result.response.ok && result.response.applied) {
    emitAnalyticsSafely(logger, "pass_tier", { id: `${season.id}:${String(tier)}:${track}` });
  }
  return JSON.stringify(result.response);
}

/**
 * `{ userId, plus? }`: opens the premium track (Premium+ also skips ten tiers). The server key or
 * an owner account only: the IAP receipt validation of #58 calls it after a verified purchase.
 */
export function rpcPassPremiumGrant(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (!isAdmin(ctx)) return refused();
  const input = parse(payload);
  const userId = typeof input["userId"] === "string" ? input["userId"] : "";
  if (userId === "") {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "user_id" }),
    });
  }
  const now = Date.now();
  const season = currentSeason(BUNDLED_SEASONS, now);
  if (season === null) return noSeason();
  const plus = input["plus"] === true;
  const store = nkPassStore(nk);
  const state = forSeason(store.read(userId), season);
  const next = activatePremium(state, season, plus, now);
  store.write(userId, next);
  audit(nk, {
    actorId: ctx.userId === undefined || ctx.userId === "" ? "server_key" : ctx.userId,
    action: plus ? "pass_premium_plus" : "pass_premium",
    targetId: userId,
    detail: { season: season.id, xp: next.xp },
    ts: now,
  });
  return JSON.stringify({ ok: true, ...passView(next, season, now) });
}
