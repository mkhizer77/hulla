import { thullaHost } from "../games/thulla/host";

import type { AnyGameHost } from "./types";
import type { GameId } from "@hulla/rules-engine";

/**
 * The one Nakama match module every table runs under. A room outlives the game it plays, so the
 * match is not named after a game: the callbacks look the host up from the table's `gameId`.
 * The name must not be a Lua module: Nakama asks its Lua runtime first, and a plain `table` finds
 * Lua's own library there and fails with "match_init not found".
 */
export const TABLE_MATCH = "hulla_table";

/** A registered game: its host for the table core and the Nakama match module name. */
export interface GameEntry {
  readonly matchName: string;
  // Hosts differ in their state, move, view, event and rules types; the core treats them alike.
  readonly host: AnyGameHost;
}

const GAMES: Readonly<Partial<Record<GameId, GameEntry>>> = {
  thulla: {
    matchName: TABLE_MATCH,
    host: thullaHost,
  },
};

/** Maps a game id to its registration; null for games that are not on the server yet (Rung). */
export function gameEntry(gameId: string): GameEntry | null {
  return (GAMES as Readonly<Record<string, GameEntry | undefined>>)[gameId] ?? null;
}

/** The host for a game id, for the match callbacks and for switching a room's game. */
export function hostFor(gameId: string): AnyGameHost | null {
  return gameEntry(gameId)?.host ?? null;
}

export function isGameId(value: unknown): value is GameId {
  return typeof value === "string" && gameEntry(value) !== null;
}
