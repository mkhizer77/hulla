import { tableMatchHandler, type AnyTableState } from "../match/nakama_match";
import { hostFor } from "../match/registry";

const handler = tableMatchHandler(hostFor);

/*
 * One match module for every game: a room outlives the game it plays, so the callbacks look the
 * game up from the table's state. Nakama resolves match callbacks by their global function names
 * and reads them out of the `registerMatch` call in InitModule, so each one is a named top-level
 * function and the registration lists them by identifier (see main.ts).
 */

export function tableMatchInit(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  params: Record<string, unknown>,
): { state: AnyTableState; tickRate: number; label: string } {
  return handler.matchInit(ctx, logger, nk, params);
}

export function tableMatchJoinAttempt(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  dispatcher: nkruntime.MatchDispatcher,
  tick: number,
  state: AnyTableState,
  presence: nkruntime.Presence,
  metadata: Record<string, unknown>,
): { state: AnyTableState; accept: boolean; rejectMessage?: string } | null {
  return handler.matchJoinAttempt(ctx, logger, nk, dispatcher, tick, state, presence, metadata);
}

export function tableMatchJoin(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  dispatcher: nkruntime.MatchDispatcher,
  tick: number,
  state: AnyTableState,
  presences: nkruntime.Presence[],
): { state: AnyTableState } | null {
  return handler.matchJoin(ctx, logger, nk, dispatcher, tick, state, presences);
}

export function tableMatchLeave(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  dispatcher: nkruntime.MatchDispatcher,
  tick: number,
  state: AnyTableState,
  presences: nkruntime.Presence[],
): { state: AnyTableState } | null {
  return handler.matchLeave(ctx, logger, nk, dispatcher, tick, state, presences);
}

export function tableMatchLoop(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  dispatcher: nkruntime.MatchDispatcher,
  tick: number,
  state: AnyTableState,
  messages: nkruntime.MatchMessage[],
): { state: AnyTableState } | null {
  return handler.matchLoop(ctx, logger, nk, dispatcher, tick, state, messages);
}

export function tableMatchTerminate(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  dispatcher: nkruntime.MatchDispatcher,
  tick: number,
  state: AnyTableState,
  graceSeconds: number,
): { state: AnyTableState } | null {
  return handler.matchTerminate(ctx, logger, nk, dispatcher, tick, state, graceSeconds);
}

export function tableMatchSignal(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  dispatcher: nkruntime.MatchDispatcher,
  tick: number,
  state: AnyTableState,
  data: string,
): { state: AnyTableState; data?: string } | null {
  return handler.matchSignal(ctx, logger, nk, dispatcher, tick, state, data);
}
