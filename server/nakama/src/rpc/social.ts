import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { nkProfileStore } from "../services/profiles";
import {
  addFriendByCode,
  nkFriendPorts,
  nkSocialStore,
  setFavourite,
  setMute,
  socialOverview,
} from "../services/social";

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

/** Sends a friend request to the holder of a friend code (GDD 10.3). */
export function rpcFriendAddByCode(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const code = parse(payload)["friendCode"];
  return JSON.stringify(
    addFriendByCode(
      nkSocialStore(nk),
      nkFriendPorts(nk),
      ctx.userId ?? null,
      typeof code === "string" ? code : "",
    ),
  );
}

/** Pins or unpins a mutual friend, ten at most. */
export function rpcFavouriteSet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const input = parse(payload);
  const target = input["userId"];
  if (typeof target !== "string" || target === "") {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "user_id" }),
    });
  }
  return JSON.stringify(
    setFavourite(
      nkSocialStore(nk),
      nkFriendPorts(nk),
      ctx.userId ?? null,
      target,
      input["pinned"] === true,
    ),
  );
}

/** Favourites and the Recent Players cards for the results screen and the friends screen. */
export function rpcSocialOverview(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? null;
  if (userId === null)
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  return JSON.stringify({
    ok: true,
    ...socialOverview(nkSocialStore(nk), nkFriendPorts(nk), nkProfileStore(nk, logger), userId),
  });
}

/** `{ userId, muted }`: mutes or unmutes a player for every device of the caller. */
export function rpcMuteSet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const input = parse(payload);
  const target = input["userId"];
  return JSON.stringify(
    setMute(
      nkSocialStore(nk),
      ctx.userId ?? null,
      typeof target === "string" ? target : "",
      input["muted"] === true,
    ),
  );
}

export function rpcMutesGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? null;
  if (userId === null) {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  return JSON.stringify({ ok: true, mutes: nkSocialStore(nk).mutes(userId) });
}
