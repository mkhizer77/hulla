import { createChecks } from "@hulla/protocol/generated";
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

import { log } from "../infra/log";
import { METRICS, metrics } from "../infra/metrics";
import { runtimeFrom, type ServerRuntime } from "../infra/nk";
import { gameEntry } from "../match/registry";
import { emitAnalyticsSafely } from "../services/analytics";
import { isDraining } from "../services/ops";
import { nkCodeStore, type CodeStore } from "../services/table_registry";

import type { CreateTableParams, TableMode } from "../match/types";
import type { RulesSelection, AnalyticsEvents } from "@hulla/protocol/generated";

export interface TableCreateDeps {
  readonly runtime: ServerRuntime;
  readonly userId: string | null;
  readonly region: string;
  readonly store: CodeStore;
  matchCreate(module: string, params: CreateTableParams): string;
}

/** A code lives a day unless the caller asks for less (tests) or the match ends first. */
export const DEFAULT_CODE_TTL_SECONDS = 24 * 3_600;
const MAX_CODE_ATTEMPTS = 5;

export type TableCreateResponse =
  | { readonly ok: true; readonly matchId: string; readonly code: string; readonly tableId: string }
  | { readonly ok: false; readonly error: RuleError };

/** Ranked tables come only from the matchmaker (GDD 7.5), so a code can never seat chosen rivals. */
const MODES: readonly TableMode[] = ["private", "public"];

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
  const checks = createChecks();
  if (!checks.rulesSelection(rulesInput) || (rulesInput as RulesSelection).gameId !== gameId) {
    return fail(ruleError(ErrorCodes.table.invalidPayload, { reason: "rules" }));
  }
  const rules = rulesInput as RulesSelection;
  const resolved = entry.host.resolveRules(rules, mode as TableMode);
  if (!resolved.ok) return fail(resolved.error);
  const ttlInput = input["codeTtlSeconds"];
  const ttlSeconds =
    typeof ttlInput === "number" &&
    Number.isInteger(ttlInput) &&
    ttlInput >= 1 &&
    ttlInput <= DEFAULT_CODE_TTL_SECONDS
      ? ttlInput
      : DEFAULT_CODE_TTL_SECONDS;
  const seed = sha256(utf8Encode(deps.runtime.uuidv4() + deps.runtime.uuidv4()));
  const rng = Xoshiro128StarStar.fromState(stateFromSeed(seed));
  const now = deps.runtime.nowMs();
  for (let attempt = 0; attempt < MAX_CODE_ATTEMPTS; attempt += 1) {
    const code = generateRoomCode(rng);
    const existing = deps.store.read(code);
    if (existing !== null && existing.expiresTs > now) continue;
    if (existing !== null) deps.store.remove(code);
    const matchId = deps.matchCreate(entry.matchName, {
      code,
      gameId: rules.gameId,
      mode: mode as TableMode,
      rules,
    });
    const registered = deps.store.create(code, {
      matchId,
      tableId: tableId(code),
      gameId: rules.gameId,
      mode: mode as TableMode,
      region: deps.region,
      createdTs: now,
      expiresTs: now + ttlSeconds * 1000,
    });
    // A lost race leaves a match nobody can find; it closes itself after the empty-table time.
    if (registered) return { ok: true, matchId, code, tableId: tableId(code) };
  }
  return fail(ruleError(ErrorCodes.table.invalidCode, { reason: "exhausted" }));
}

/** Registered as `table_create`; a named top-level function because Nakama resolves it by name. */
export function rpcTableCreate(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (isDraining(nk)) {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.draining) });
  }
  const response = tableCreate(
    {
      runtime: runtimeFrom(nk, logger),
      userId: ctx.userId ?? null,
      region: ctx.env["HULLA_REGION"] ?? "local",
      store: nkCodeStore(nk, logger),
      matchCreate: (module, params) => nk.matchCreate(module, params),
    },
    payload,
  );
  if (response.ok) {
    log.info(logger, "table created", { tableId: response.tableId, userId: ctx.userId ?? "" });
    metrics.count(nk, METRICS.tablesCreated, { game: "thulla" });
    emitAnalyticsSafely(logger, "table_create", tableCreateParams(payload), {
      tableId: response.tableId,
    });
  }
  return JSON.stringify(response);
}

/** The taxonomy's view of a create request: mode, preset and seats, nothing else. */
function tableCreateParams(payload: string): AnalyticsEvents["table_create"] {
  let input: Record<string, unknown> = {};
  try {
    const parsed: unknown = payload === "" ? {} : JSON.parse(payload);
    if (typeof parsed === "object" && parsed !== null) input = parsed as Record<string, unknown>;
  } catch {
    input = {};
  }
  const mode = input["mode"];
  const rules = input["rules"] as { preset?: unknown } | undefined;
  const voice = input["voiceMode"];
  return {
    mode: mode === "public" || mode === "ranked" ? mode : "private",
    ...(typeof rules?.preset === "string" ? { preset: rules.preset } : {}),
    ...(typeof input["seats"] === "number" ? { seats: input["seats"] } : {}),
    ...(voice === "openMic" || voice === "pushToTalk" || voice === "off"
      ? { voice_mode: voice }
      : {}),
  };
}
