import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import {
  achievementsView,
  isAchievementList,
  nkAchievementStore,
  settleUnlocks,
  withCounter,
} from "../services/achievements";
import { nkLedgerPorts } from "../services/ledger";
import { nkFriendIds } from "../services/presence";

const refused = (): string =>
  JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });

/** The list with progress; the friend count is refreshed here so a social unlock lands on view. */
export function rpcAchievementsGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const store = nkAchievementStore(nk);
  const list = store.list();
  const now = Date.now();
  const current = store.read(userId);
  const withFriends = withCounter(current, "friends", nkFriendIds(nk, userId).length, now);
  const unlocked = settleUnlocks(nk, nkLedgerPorts(nk), store, userId, withFriends, list, now);
  const record = store.read(userId);
  return JSON.stringify({
    ok: true,
    unlockedNow: unlocked.map((u) => u.id),
    achievements: achievementsView(record, list),
  });
}

/** Replaces the list without a restart; the server key only. */
export function rpcAchievementsSet(
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
  if (!isAchievementList(input)) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.content.schemaInvalid, { reason: "achievements" }),
    });
  }
  nkAchievementStore(nk).writeList(input);
  logger.info("achievement list replaced", { id: input.id, version: input.version });
  return JSON.stringify({ ok: true, id: input.id, version: input.version });
}
