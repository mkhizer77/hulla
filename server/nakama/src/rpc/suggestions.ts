import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { excludedIds, nkBlockStore } from "../services/blocks";
import { nkProfileStore } from "../services/profiles";
import { suggestFriends, type SuggestionPorts } from "../services/suggestions";

import { phonePepperOf } from "./phone";

const BATCH = 500;

/** `SELECT id FROM users WHERE <column> IN (...)` in batches; Nakama's runtime SQL takes plain values. */
function idsWhere(
  nk: nkruntime.Nakama,
  column: "custom_id" | "facebook_id",
  values: readonly string[],
): string[] {
  const ids: string[] = [];
  for (let i = 0; i < values.length; i += BATCH) {
    const batch = values.slice(i, i + BATCH);
    const marks = batch.map((_, n) => `$${String(n + 1)}`).join(",");
    const rows = nk.sqlQuery(`SELECT id FROM users WHERE ${column} IN (${marks})`, batch);
    for (const row of rows) {
      const id: unknown = row["id"];
      if (typeof id === "string") ids.push(id);
    }
  }
  return ids;
}

/** The friend graph in every state (friends, requests both ways, blocked), so they are not suggested again. */
function knownIds(nk: nkruntime.Nakama, userId: string): Set<string> {
  const known = new Set<string>();
  let cursor: string | undefined;
  for (let page = 0; page < 20; page += 1) {
    const list = nk.friendsList(userId, 100, undefined, cursor);
    for (const f of list.friends ?? []) {
      if (f.user?.userId !== undefined) known.add(f.user.userId);
    }
    if (list.cursor === undefined || list.cursor === "" || list.cursor === cursor) break;
    cursor = list.cursor;
  }
  return known;
}

export function nkSuggestionPorts(
  ctx: nkruntime.Context,
  nk: nkruntime.Nakama,
  logger: nkruntime.Logger,
): SuggestionPorts | null {
  const pepper = phonePepperOf(ctx.env);
  if (pepper === null) return null;
  const profiles = nkProfileStore(nk, logger);
  const blocks = nkBlockStore(nk);
  return {
    hashPhone: (phone) => nk.sha256Hash(`${pepper}:${phone}`),
    accountsByCustomId: (ids) => idsWhere(nk, "custom_id", ids),
    accountsByFacebookId: (ids) => idsWhere(nk, "facebook_id", ids),
    knownIds: (userId) => knownIds(nk, userId),
    excluded: (userId) => excludedIds(blocks, userId),
    profile(userId) {
      const profile = profiles.read(userId);
      if (profile !== null) return { displayName: profile.displayName, avatar: profile.avatar };
      const user = nk.usersGetId([userId])[0];
      return user === undefined
        ? null
        : { displayName: user.displayName === "" ? user.username : user.displayName, avatar: null };
    },
  };
}

/** `{ phones?: string[], facebookIds?: string[] }` → `{ ok, suggestions }` (people you may know). */
export function rpcFriendSuggestions(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (ctx.userId === undefined || ctx.userId === "") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  let input: unknown;
  try {
    input = payload === "" ? {} : JSON.parse(payload);
  } catch {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "json" }),
    });
  }
  const ports = nkSuggestionPorts(ctx, nk, logger);
  if (ports === null) return JSON.stringify({ ok: true, suggestions: [] });
  return JSON.stringify({ ok: true, suggestions: suggestFriends(ports, ctx.userId, input) });
}
