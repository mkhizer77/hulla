import {
  generateRoomCode,
  sha256,
  stateFromSeed,
  utf8Encode,
  Xoshiro128StarStar,
} from "@hulla/shared-ts";

import { runtimeFrom, type ServerRuntime } from "../infra/nk";
import { gameEntry } from "../match/registry";

import type { CreateTableParams } from "../match/types";

/** One matched ticket as the hook sees it. */
export interface MatchedEntry {
  readonly userId: string;
  readonly partyId: string | null;
  readonly properties: Readonly<Record<string, string>>;
}

export interface MatchmakerDeps {
  readonly runtime: ServerRuntime;
  matchCreate(module: string, params: CreateTableParams): string;
}

/**
 * The matchmaker found a group (GDD 7.2, 9.6): open a public table for exactly them, with the
 * standard preset locked, that deals itself once they are seated. Parties keep their id so the
 * table shows who queued together. Nakama's interval settings decide when a group below the
 * requested size is accepted (ARCHITECTURE 8.1, OQ-006); the client widens the query over time.
 */
export function tableForMatch(deps: MatchmakerDeps, entries: readonly MatchedEntry[]): string {
  const requested = entries[0]?.properties["game"] ?? "thulla";
  const gameId: CreateTableParams["gameId"] =
    gameEntry(requested) === null ? "thulla" : (requested as CreateTableParams["gameId"]);
  const entry = gameEntry(gameId);
  if (entry === null) throw new Error("no game is registered");
  const byParty = new Map<string, string[]>();
  for (const e of entries) {
    if (e.partyId === null || e.partyId === "") continue;
    byParty.set(e.partyId, [...(byParty.get(e.partyId) ?? []), e.userId]);
  }
  const parties = [...byParty.entries()]
    .filter(([, members]) => members.length > 1)
    .map(([id, members]) => ({ id, members }));
  const seed = sha256(utf8Encode(deps.runtime.uuidv4() + deps.runtime.uuidv4()));
  const code = generateRoomCode(Xoshiro128StarStar.fromState(stateFromSeed(seed)));
  return deps.matchCreate(entry.matchName, {
    code,
    gameId,
    mode: "public",
    rules: { gameId, preset: "standard" },
    autoStart: { expected: entries.length },
    parties,
  });
}

/** Registered with `registerMatchmakerMatched`; a named top-level function for Nakama. */
export function matchmakerMatched(
  _ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  matches: nkruntime.MatchmakerResult[],
): string {
  const entries: MatchedEntry[] = matches.map((m) => ({
    userId: m.presence.userId,
    partyId: m.partyId ?? null,
    properties: m.properties,
  }));
  const matchId = tableForMatch(
    {
      runtime: runtimeFrom(nk, logger),
      matchCreate: (module, params) => nk.matchCreate(module, params),
    },
    entries,
  );
  logger.info("matchmade table", { matchId, players: entries.length });
  return matchId;
}
