import { ErrorCodes, ruleError, type RuleError } from "@hulla/shared-ts";

import {
  lookupCode,
  nkCodeStore,
  type CodeEntry,
  type CodeStore,
} from "../services/table_registry";

export interface TableJoinDeps {
  readonly store: CodeStore;
  readonly userId: string | null;
  readonly now: number;
  matchExists(matchId: string): boolean;
}

export type TableJoinResponse =
  | {
      readonly ok: true;
      readonly matchId: string;
      readonly code: string;
      readonly tableId: string;
      readonly region: string;
    }
  | { readonly ok: false; readonly error: RuleError };

/** Resolves a room code to the match to join (GDD 9.3); the match itself decides the seat. */
export function tableJoinByCode(deps: TableJoinDeps, payload: string): TableJoinResponse {
  if (deps.userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  let code: unknown;
  try {
    const parsed: unknown = payload === "" ? {} : JSON.parse(payload);
    code =
      typeof parsed === "object" && parsed !== null
        ? (parsed as { code?: unknown }).code
        : undefined;
  } catch {
    return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "json" }) };
  }
  if (typeof code !== "string") {
    return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "code" }) };
  }
  const found = lookupCode(deps.store, code, deps.now, (id) => deps.matchExists(id));
  if (!found.ok) return found;
  const entry: CodeEntry & { readonly code: string } = found.value;
  return {
    ok: true,
    matchId: entry.matchId,
    code: entry.code,
    tableId: entry.tableId,
    region: entry.region,
  };
}

export function rpcTableJoinByCode(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  return JSON.stringify(
    tableJoinByCode(
      {
        store: nkCodeStore(nk, logger),
        userId: ctx.userId ?? null,
        now: Date.now(),
        matchExists: (matchId) => nk.matchGet(matchId) !== null,
      },
      payload,
    ),
  );
}
