import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { BUNDLED_RANKED } from "../content/ranked";
import {
  currentState,
  nkRankedStore,
  rankedBoard,
  rankedView,
  rankOn,
  seasonAt,
} from "../services/ranked";

const BOARD_LIMIT = 100;

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
 * The caller's ranked card (GDD 7.5, 12.9): season, rating, league, placement progress, the
 * leagues and their rewards, and the band the client queues with. A first call after a season
 * ends settles it, so `lastSeason` carries the reward just paid.
 */
export function rpcRankedGet(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  const now = Date.now();
  const config = BUNDLED_RANKED;
  const season = seasonAt(config, now);
  const before = nkRankedStore(nk).read(userId);
  const state = currentState(nk, logger, userId, now, config);
  const rank = rankOn(nk, season.id, userId);
  const view = rankedView(state, config, season, rank);
  const settledNow = before !== null && before.seasonId !== state.seasonId;
  return JSON.stringify({ ok: true, ...view, seasonSettled: settledNow });
}

/** `{ limit? }`: the current season's top places with the league each shows. */
export function rpcRankedBoard(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if ((ctx.userId ?? "") === "") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  const input = parse(payload);
  const asked = typeof input["limit"] === "number" ? Math.floor(input["limit"]) : 50;
  const limit = Math.min(Math.max(asked, 1), BOARD_LIMIT);
  const season = seasonAt(BUNDLED_RANKED, Date.now());
  return JSON.stringify({ ok: true, season, rows: rankedBoard(nk, BUNDLED_RANKED, season, limit) });
}
