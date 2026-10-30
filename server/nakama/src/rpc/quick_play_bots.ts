import {
  ErrorCodes,
  generateRoomCode,
  ruleError,
  sha256,
  stateFromSeed,
  tableId,
  utf8Encode,
  Xoshiro128StarStar,
} from "@hulla/shared-ts";

import { log } from "../infra/log";
import { runtimeFrom, type ServerRuntime } from "../infra/nk";
import { botsFor } from "../match/bots";
import { gameEntry } from "../match/registry";
import { isDraining } from "../services/ops";

import type { TableCreateResponse } from "./table_create";
import type { CreateTableParams } from "../match/types";

export interface QuickPlayBotsDeps {
  readonly runtime: ServerRuntime;
  readonly userId: string | null;
  matchCreate(module: string, params: CreateTableParams): string;
}

/**
 * Quick Play found nobody in 30 s (owner decision, 29 Oct): a public table with the standard preset
 * for the caller and up to three bots, which deals itself as soon as the player sits. The table is
 * not in the code registry, so nobody else can join it by code; the ticket carries its own code.
 */
export function quickPlayBots(deps: QuickPlayBotsDeps, payload: string): TableCreateResponse {
  if (deps.userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  let seats = 4;
  try {
    const parsed: unknown = payload === "" ? {} : JSON.parse(payload);
    const asked = (parsed as Record<string, unknown> | null)?.["seats"];
    if (typeof asked === "number" && Number.isInteger(asked)) seats = asked;
  } catch {
    return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "json" }) };
  }
  const entry = gameEntry("thulla");
  if (entry === null) return { ok: false, error: ruleError(ErrorCodes.table.notFound) };
  const bots = botsFor(
    Math.max(entry.host.module.seats.min, Math.min(entry.host.module.seats.max, seats)),
  );
  const seed = sha256(utf8Encode(deps.runtime.uuidv4() + deps.userId));
  const code = generateRoomCode(Xoshiro128StarStar.fromState(stateFromSeed(seed)));
  const matchId = deps.matchCreate(entry.matchName, {
    code,
    gameId: "thulla",
    mode: "public",
    rules: { gameId: "thulla", preset: "standard" },
    autoStart: { expected: bots + 1 },
    parties: [],
    bots,
  });
  return { ok: true, matchId, code, tableId: tableId(code) };
}

/** Registered as `quick_play_bots`; a named top-level function because Nakama resolves it by name. */
export function rpcQuickPlayBots(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (isDraining(nk)) {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.draining) });
  }
  const response = quickPlayBots(
    {
      runtime: runtimeFrom(nk, logger),
      userId: ctx.userId ?? null,
      matchCreate: (module, params) => nk.matchCreate(module, params),
    },
    payload,
  );
  if (response.ok)
    log.info(logger, "bot table created", { tableId: response.tableId, userId: ctx.userId ?? "" });
  return JSON.stringify(response);
}
