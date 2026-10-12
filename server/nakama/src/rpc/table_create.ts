import { check_rules_selection } from "@hulla/protocol/generated";
import {
  ErrorCodes,
  generateRoomCode,
  ruleError,
  sha256,
  stateFromSeed,
  tableId,
  utf8Encode,
  Xoshiro128StarStar,
  type RuleError,
} from "@hulla/shared-ts";

import { runtimeFrom, type ServerRuntime } from "../infra/nk";
import { gameEntry } from "../match/registry";

import type { CreateTableParams, TableMode } from "../match/types";
import type { RulesSelection } from "@hulla/protocol/generated";

export interface TableCreateDeps {
  readonly runtime: ServerRuntime;
  readonly userId: string | null;
  matchCreate(module: string, params: CreateTableParams): string;
}

export type TableCreateResponse =
  | { readonly ok: true; readonly matchId: string; readonly code: string; readonly tableId: string }
  | { readonly ok: false; readonly error: RuleError };

const MODES: readonly TableMode[] = ["private", "public", "ranked"];

function fail(error: RuleError): TableCreateResponse {
  return { ok: false, error };
}

/**
 * Creates an authoritative table match for the caller (GDD 9.3, ARCHITECTURE 8.9). The code
 * registry, join by code, presets and the voice token follow in the table RPCs.
 */
export function tableCreate(deps: TableCreateDeps, payload: string): TableCreateResponse {
  if (deps.userId === null) return fail(ruleError(ErrorCodes.table.notAuthorised));
  let input: Record<string, unknown>;
  try {
    const parsed: unknown = payload === "" ? {} : JSON.parse(payload);
    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
      return fail(ruleError(ErrorCodes.table.invalidPayload, { reason: "json" }));
    }
    input = parsed as Record<string, unknown>;
  } catch {
    return fail(ruleError(ErrorCodes.table.invalidPayload, { reason: "json" }));
  }
  const gameId = input["gameId"] ?? "thulla";
  const entry = typeof gameId === "string" ? gameEntry(gameId) : null;
  if (entry === null || typeof gameId !== "string") {
    return fail(ruleError(ErrorCodes.table.invalidPayload, { reason: "game_id" }));
  }
  const mode = input["mode"] ?? "private";
  if (!MODES.includes(mode as TableMode)) {
    return fail(ruleError(ErrorCodes.table.invalidPayload, { reason: "mode" }));
  }
  const rulesInput = input["rules"] ?? { gameId };
  if (!check_rules_selection(rulesInput) || (rulesInput as RulesSelection).gameId !== gameId) {
    return fail(ruleError(ErrorCodes.table.invalidPayload, { reason: "rules" }));
  }
  const rules = rulesInput as RulesSelection;
  const resolved = entry.host.resolveRules(rules, mode as TableMode);
  if (!resolved.ok) return fail(resolved.error);
  const seed = sha256(utf8Encode(deps.runtime.uuidv4() + deps.runtime.uuidv4()));
  const code = generateRoomCode(Xoshiro128StarStar.fromState(stateFromSeed(seed)));
  const matchId = deps.matchCreate(entry.matchName, {
    code,
    gameId: rules.gameId,
    mode: mode as TableMode,
    rules,
  });
  return { ok: true, matchId, code, tableId: tableId(code) };
}

/** Registered as `table_create`; a named top-level function because Nakama resolves it by name. */
export function rpcTableCreate(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const response = tableCreate(
    {
      runtime: runtimeFrom(nk, logger),
      userId: ctx.userId ?? null,
      matchCreate: (module, params) => nk.matchCreate(module, params),
    },
    payload,
  );
  if (response.ok) {
    logger.info("table created", { tableId: response.tableId, userId: ctx.userId ?? "" });
  }
  return JSON.stringify(response);
}
