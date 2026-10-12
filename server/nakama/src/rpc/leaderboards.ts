import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { boardView, BOARDS, isBoardKey, payTopBrackets } from "../services/leaderboards";
import { nkLedgerPorts } from "../services/ledger";
import { nkFriendIds } from "../services/presence";

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

/** `{ board: weekly|monthly, scope?: global|friends, limit?, cursor? }` (GDD 12.8). */
export function rpcLeaderboardGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  const input = parse(payload);
  const board = input["board"] ?? "weekly";
  if (!isBoardKey(board)) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "board" }),
    });
  }
  const scope = input["scope"] === "friends" ? "friends" : "global";
  const limit = typeof input["limit"] === "number" ? input["limit"] : 20;
  const cursor =
    typeof input["cursor"] === "string" && input["cursor"] !== "" ? input["cursor"] : undefined;
  const friends = scope === "friends" ? nkFriendIds(nk, userId) : [];
  return JSON.stringify(boardView(nk, userId, board, scope, friends, limit, cursor));
}

/** Registered with `registerLeaderboardReset`; a named top-level function for Nakama. */
export function onLeaderboardReset(
  _ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  leaderboard: nkruntime.Leaderboard,
  reset: number,
): void {
  if (!Object.values(BOARDS).some((b) => b.id === leaderboard.id)) return;
  try {
    const result = payTopBrackets(nk, nkLedgerPorts(nk), leaderboard.id, reset);
    logger.info("leaderboard brackets paid", { id: leaderboard.id, reset, paid: result.paid });
  } catch (error: unknown) {
    logger.error("leaderboard brackets not paid", { id: leaderboard.id, error: String(error) });
  }
}
