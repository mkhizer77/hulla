import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { log } from "../infra/log";
import { isFilterLists, nkFilterStore } from "../services/chat_filter";

/** The lists in force: storage when the owner uploaded a set, the bundled content otherwise. */
export function rpcChatFiltersGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  if (ctx.userId !== undefined && ctx.userId !== "") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  return JSON.stringify({ ok: true, lists: nkFilterStore(nk).read() });
}

/** Replaces the lists without a restart; the server key only (`?http_key=`), never a session. */
export function rpcChatFiltersSet(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (ctx.userId !== undefined && ctx.userId !== "") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  let input: unknown;
  try {
    input = JSON.parse(payload);
  } catch {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "json" }),
    });
  }
  if (!isFilterLists(input)) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.content.schemaInvalid, { reason: "chat_filters" }),
    });
  }
  nkFilterStore(nk).write(input);
  log.info(logger, "chat filters replaced", { id: input.id, version: input.version });
  return JSON.stringify({ ok: true, id: input.id, version: input.version });
}
