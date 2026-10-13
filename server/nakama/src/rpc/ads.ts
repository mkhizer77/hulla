import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { log } from "../infra/log";
import { METRICS, metrics } from "../infra/metrics";
import {
  CAP_FLAGS,
  DEFAULT_CAPS,
  EVENT_KEY_ENV,
  PLACEMENTS,
  applyAdReward,
  forDay,
  nkAdStore,
  parseCustomData,
  verifyEventToken,
  type AdPorts,
  type AdsView,
  type Placement,
} from "../services/ads";
import { emitAnalyticsSafely } from "../services/analytics";
import { nkCatalogStore } from "../services/catalog";
import { nkConfigStore, resolveFlag, type Audience, type RemoteConfig } from "../services/config";
import { nkLedgerPorts } from "../services/ledger";
import { nkProgressStore } from "../services/progression";
import { nkEntitlementStore } from "../services/purchases";
import { validTz } from "../services/push";

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

function audienceOf(ctx: nkruntime.Context): Audience {
  return { region: ctx.env["HULLA_REGION"] ?? "local", cohort: "*" };
}

function numberFlag(
  config: RemoteConfig,
  audience: Audience,
  name: string,
  fallback: number,
): number {
  const value = resolveFlag(config, name, audience);
  return typeof value === "number" && Number.isFinite(value) && value >= 0 ? value : fallback;
}

function nkAdPorts(nk: nkruntime.Nakama, config: RemoteConfig, audience: Audience): AdPorts {
  return {
    ledger: nkLedgerPorts(nk),
    progress: nkProgressStore(nk),
    catalog: nkCatalogStore(nk),
    cap: (placement) => numberFlag(config, audience, CAP_FLAGS[placement], DEFAULT_CAPS[placement]),
  };
}

/** The first value of a query parameter, else the JSON field of the same name, else "". */
function param(ctx: nkruntime.Context, input: Record<string, unknown>, name: string): string {
  const fromQuery = ctx.queryParams?.[name]?.[0];
  if (typeof fromQuery === "string" && fromQuery !== "") return fromQuery;
  const fromBody = input[name];
  return typeof fromBody === "string" ? fromBody : "";
}

/**
 * AppLovin's server-to-server rewarded callback (GDD 13.6): a GET on
 * `/v2/rpc/ad_reward?http_key=...&unwrap&user_id={USER_ID}&event_id={EVENT_ID}&token={EVENT_TOKEN}&custom={CUSTOM_DATA}`.
 * The token must be the SHA-1 of the event id and the box's event key; the custom data names the
 * placement. Every answer is a 200 so AppLovin does not retry a refused call; the body says why.
 */
export function rpcAdReward(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  // Only the server key may call this; a session cannot reward itself.
  if (ctx.userId !== undefined && ctx.userId !== "") return refused();
  const key = ctx.env[EVENT_KEY_ENV] ?? "";
  if (key === "") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.economy.adsUnavailable) });
  }
  const input = parse(payload);
  const userId = param(ctx, input, "user_id");
  const eventId = param(ctx, input, "event_id");
  const token = param(ctx, input, "token");
  const custom = param(ctx, input, "custom");
  if (!verifyEventToken(eventId, token, key) || userId === "") {
    log.warn(logger, "ad reward refused", { reason: "token" });
    metrics.count(nk, METRICS.adRewardsRejected, { reason: "token" });
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.economy.adRewardInvalid) });
  }
  const data = parseCustomData(custom);
  if (data === null) {
    metrics.count(nk, METRICS.adRewardsRejected, { reason: "custom" });
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "custom" }),
    });
  }
  const now = Date.now();
  const config = nkConfigStore(nk).read();
  const audience = audienceOf(ctx);
  const store = nkAdStore(nk);
  const current = store.read(userId);
  const result = applyAdReward(
    nkAdPorts(nk, config, audience),
    current,
    userId,
    data,
    eventId,
    now,
  );
  if (result.record !== current) store.write(userId, result.record);
  if (result.response.ok && result.response.applied) {
    metrics.count(nk, METRICS.adRewards, { placement: data.placement });
    emitAnalyticsSafely(logger, "ad_reward", { placement: data.placement });
  } else if (!result.response.ok) {
    metrics.count(nk, METRICS.adRewardsRejected, { reason: result.response.error.code });
  }
  return JSON.stringify(result.response);
}

/**
 * `{ tzOffsetMinutes? }`: what the client may show today: the rewarded caps and today's use, the
 * re-rolls earned, and the interstitial rules, off for an account that holds Remove Ads.
 */
export function rpcAdsGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const now = Date.now();
  const store = nkAdStore(nk);
  let record = store.read(userId);
  const offset = parse(payload)["tzOffsetMinutes"];
  if (validTz(offset) && offset !== record.tzOffsetMinutes) {
    record = { ...record, tzOffsetMinutes: offset };
  }
  const today = forDay(record, now);
  if (today !== store.read(userId)) store.write(userId, today);
  const config = nkConfigStore(nk).read();
  const audience = audienceOf(ctx);
  const caps = {} as Record<Placement, number>;
  for (const placement of PLACEMENTS) {
    caps[placement] = numberFlag(config, audience, CAP_FLAGS[placement], DEFAULT_CAPS[placement]);
  }
  const removeAds = nkEntitlementStore(nk).read(userId).removeAds;
  const view: AdsView = {
    rewardedEnabled: resolveFlag(config, "rewarded_ads", audience) === true,
    caps,
    used: today.counts,
    rerollCredits: today.rerollCredits,
    interstitials: {
      enabled: !removeAds && resolveFlag(config, "interstitials", audience) !== false,
      fromRound: numberFlag(config, audience, "interstitial_from_round", 4),
      intervalSeconds: numberFlag(config, audience, "interstitial_interval_seconds", 240),
    },
  };
  return JSON.stringify({ ok: true, ...view });
}
