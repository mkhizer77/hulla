import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { nkPresenceStore, type PresenceStore } from "../services/presence";

/** The table a player still holds a seat at, so Home can offer Rejoin until that seat is gone. */
export interface MyTable {
  readonly matchId: string;
  readonly tableId: string;
  readonly code: string;
  readonly mode: string;
  readonly status: string;
}

/**
 * The seat pointer (presence storage) names the last table; the table itself answers whether the
 * seat is still theirs, connected or not. A closed table, a freed seat or a missing match is null.
 */
export function currentTable(
  store: PresenceStore,
  userId: string,
  signal: (matchId: string, data: string) => string,
): MyTable | null {
  const pointer = store.read(userId).table;
  if (pointer === null) return null;
  let reply: Record<string, unknown>;
  try {
    const value: unknown = JSON.parse(
      signal(pointer.matchId, JSON.stringify({ kind: "has_user", userId })),
    );
    if (typeof value !== "object" || value === null) return null;
    reply = value as Record<string, unknown>;
  } catch {
    return null;
  }
  if (reply["ok"] !== true || reply["seated"] !== true || reply["status"] === "closed") return null;
  return {
    matchId: pointer.matchId,
    tableId: typeof reply["tableId"] === "string" ? reply["tableId"] : "",
    code: typeof reply["code"] === "string" ? reply["code"] : "",
    mode: typeof reply["mode"] === "string" ? reply["mode"] : "",
    status: typeof reply["status"] === "string" ? reply["status"] : "",
  };
}

/** `{}` → `{ ok, table }`: the caller's table while their seat is held, else null. */
export function rpcTableMine(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? null;
  if (userId === null)
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  const table = currentTable(nkPresenceStore(nk), userId, (matchId, data) =>
    nk.matchSignal(matchId, data),
  );
  return JSON.stringify({ ok: true, table });
}
