import { ErrorCodes, ruleError, type RuleError } from "@hulla/shared-ts";

import { presetsFor, type Preset } from "../content/presets";
import { gameEntry } from "../match/registry";

export type PresetsResponse =
  | { readonly ok: true; readonly gameId: string; readonly presets: readonly Preset[] }
  | { readonly ok: false; readonly error: RuleError };

/** The presets for a game from content (GDD 6.13); the client renders them as chips. */
export function presetsGet(payload: string): PresetsResponse {
  let gameId: unknown = "thulla";
  if (payload !== "") {
    try {
      const parsed: unknown = JSON.parse(payload);
      if (typeof parsed === "object" && parsed !== null && "gameId" in parsed) {
        gameId = parsed.gameId;
      }
    } catch {
      return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "json" }) };
    }
  }
  if (typeof gameId !== "string" || gameEntry(gameId) === null) {
    return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "game_id" }) };
  }
  return { ok: true, gameId, presets: presetsFor(gameId) };
}

export function rpcPresetsGet(
  _ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  _nk: nkruntime.Nakama,
  payload: string,
): string {
  return JSON.stringify(presetsGet(payload));
}
