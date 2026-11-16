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
 * Quick Play found nobody in time (owner decision, 29 Oct; Rung after 15 s, Rung design 6.5): a
 * public table with the game's default rules for the caller and up to three bots, which deals itself as soon as the player sits. The table is
 * not in the code registry, so nobody else can join it by code; the ticket carries its own code.
 */
export function quickPlayBots(deps: QuickPlayBotsDeps, payload: string): TableCreateResponse {
  if (deps.userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  let seats = 4;
  let gameId: CreateTableParams["gameId"] = "thulla";
  try {
    const parsed: unknown = payload === "" ? {} : JSON.parse(payload);
    const asked = (parsed as Record<string, unknown> | null)?.["seats"];
    if (typeof asked === "number" && Number.isInteger(asked)) seats = asked;
    if ((parsed as Record<string, unknown> | null)?.["gameId"] === "rung") gameId = "rung";
  } catch {
    return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "json" }) };
  }
  const entry = gameEntry(gameId);
  if (entry === null) return { ok: false, error: ruleError(ErrorCodes.table.notFound) };
  const bots = botsFor(
    Math.max(entry.host.module.seats.min, Math.min(entry.host.module.seats.max, seats)),
  );
  const seed = sha256(utf8Encode(deps.runtime.uuidv4() + deps.userId));
  const code = generateRoomCode(Xoshiro128StarStar.fromState(stateFromSeed(seed)));
  const matchId = deps.matchCreate(entry.matchName, {
    code,
    gameId,
    mode: "public",
    rules: { gameId, preset: gameId === "rung" ? "rung" : "standard" },
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
