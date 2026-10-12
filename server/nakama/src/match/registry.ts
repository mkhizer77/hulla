import { thullaHost } from "../games/thulla/host";

import type { GameHost } from "./types";
import type { GameId } from "@hulla/rules-engine";

/** A registered game: its host for the table core and the Nakama match module name. */
export interface GameEntry {
  readonly matchName: string;
  // Hosts differ in their state, move, view, event and rules types; the core treats them alike.
  readonly host: GameHost<unknown, unknown, unknown, unknown, unknown>;
}

const GAMES: Readonly<Partial<Record<GameId, GameEntry>>> = {
  thulla: {
    matchName: "thulla",
    host: thullaHost,
  },
};

/** Maps a game id to its registration; null for games that are not on the server yet (Rung). */
export function gameEntry(gameId: string): GameEntry | null {
  return (GAMES as Readonly<Record<string, GameEntry | undefined>>)[gameId] ?? null;
}

export function isGameId(value: unknown): value is GameId {
  return typeof value === "string" && gameEntry(value) !== null;
}
