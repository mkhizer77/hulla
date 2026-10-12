import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import {
  listFriendPresence,
  nkPresencePorts,
  nkPresenceStore,
  setAway,
} from "../services/presence";
import { nkProfileStore } from "../services/profiles";
import { nkSocialStore } from "../services/social";

/** The friends list with presence and joinable tables, for Home and the friends screen (GDD 10.4, 14.10). */
export function rpcTableListFriends(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? null;
  if (userId === null)
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  const social = nkSocialStore(nk);
  const ports = nkPresencePorts(nk, nkProfileStore(nk, logger), (id) => social.favourites(id));
  return JSON.stringify({
    ok: true,
    friends: listFriendPresence(nkPresenceStore(nk), ports, userId),
  });
}

function parseAway(payload: string): boolean {
  try {
    const value: unknown = payload === "" ? {} : JSON.parse(payload);
    return (
      typeof value === "object" &&
      value !== null &&
      (value as Record<string, unknown>)["away"] === true
    );
  } catch {
    return false;
  }
}

/** `{ away: boolean }`: the app in the background shows as Away to friends. */
export function rpcPresenceSet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  return JSON.stringify(
    setAway(nkPresenceStore(nk), ctx.userId ?? null, parseAway(payload), Date.now()),
  );
}
