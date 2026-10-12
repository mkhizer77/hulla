import { tableMatchHandler } from "../../match/nakama_match";

import { thullaHost } from "./host";

import type { TableState } from "../../match/types";
import type { ThullaMove, ThullaState } from "@hulla/rules-engine";

export type ThullaTableState = TableState<ThullaState, ThullaMove>;

const handler = tableMatchHandler(thullaHost);

/*
 * Nakama resolves match callbacks by their global function names and reads them out of the
 * `registerMatch` call in InitModule, so each one is a named top-level function and the
 * registration lists them by identifier (see main.ts).
 */

export function thullaMatchInit(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  params: Record<string, unknown>,
): { state: ThullaTableState; tickRate: number; label: string } {
  return handler.matchInit(ctx, logger, nk, params);
}

export function thullaMatchJoinAttempt(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  dispatcher: nkruntime.MatchDispatcher,
  tick: number,
  state: ThullaTableState,
  presence: nkruntime.Presence,
  metadata: Record<string, unknown>,
): { state: ThullaTableState; accept: boolean; rejectMessage?: string } | null {
  return handler.matchJoinAttempt(ctx, logger, nk, dispatcher, tick, state, presence, metadata);
}

export function thullaMatchJoin(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  dispatcher: nkruntime.MatchDispatcher,
  tick: number,
  state: ThullaTableState,
  presences: nkruntime.Presence[],
): { state: ThullaTableState } | null {
  return handler.matchJoin(ctx, logger, nk, dispatcher, tick, state, presences);
}

export function thullaMatchLeave(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  dispatcher: nkruntime.MatchDispatcher,
  tick: number,
  state: ThullaTableState,
  presences: nkruntime.Presence[],
): { state: ThullaTableState } | null {
  return handler.matchLeave(ctx, logger, nk, dispatcher, tick, state, presences);
}

export function thullaMatchLoop(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  dispatcher: nkruntime.MatchDispatcher,
  tick: number,
  state: ThullaTableState,
  messages: nkruntime.MatchMessage[],
): { state: ThullaTableState } | null {
  return handler.matchLoop(ctx, logger, nk, dispatcher, tick, state, messages);
}

export function thullaMatchTerminate(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  dispatcher: nkruntime.MatchDispatcher,
  tick: number,
  state: ThullaTableState,
  graceSeconds: number,
): { state: ThullaTableState } | null {
  return handler.matchTerminate(ctx, logger, nk, dispatcher, tick, state, graceSeconds);
}

export function thullaMatchSignal(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  dispatcher: nkruntime.MatchDispatcher,
  tick: number,
  state: ThullaTableState,
  data: string,
): { state: ThullaTableState; data?: string } | null {
  return handler.matchSignal(ctx, logger, nk, dispatcher, tick, state, data);
}
