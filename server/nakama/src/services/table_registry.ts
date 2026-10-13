import { ErrorCodes, normaliseRoomCode, ruleError } from "@hulla/shared-ts";

import { log } from "../infra/log";

import type { TableMode } from "../match/types";
import type { GameId } from "@hulla/rules-engine";
import type { Result, RuleError } from "@hulla/shared-ts";

/** Room codes map to matches here (GDD 9.3, 20.7); a code outlives nothing: expiry and a dead match both end it. */
export const TABLE_CODES_COLLECTION = "table_codes";

/** Nakama's system user owns the registry; players never read or write it directly. */
export const SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000";

export interface CodeEntry {
  readonly matchId: string;
  readonly tableId: string;
  readonly gameId: GameId;
  readonly mode: TableMode;
  readonly region: string;
  readonly createdTs: number;
  readonly expiresTs: number;
}

export interface CodeStore {
  read(code: string): CodeEntry | null;
  /** False when the code is already taken. */
  create(code: string, entry: CodeEntry): boolean;
  remove(code: string): void;
}

export function nkCodeStore(nk: nkruntime.Nakama, logger: nkruntime.Logger): CodeStore {
  return {
    read(code) {
      const objects = nk.storageRead([
        { collection: TABLE_CODES_COLLECTION, key: code, userId: SYSTEM_USER_ID },
      ]);
      const first = objects[0];
      return first === undefined ? null : (first.value as unknown as CodeEntry);
    },
    create(code, entry) {
      try {
        nk.storageWrite([
          {
            collection: TABLE_CODES_COLLECTION,
            key: code,
            userId: SYSTEM_USER_ID,
            value: entry,
            version: "*",
            permissionRead: 0,
            permissionWrite: 0,
          },
        ]);
        return true;
      } catch (error: unknown) {
        log.warn(logger, "room code taken", { code, error: String(error) });
        return false;
      }
    },
    remove(code) {
      try {
        nk.storageDelete([
          { collection: TABLE_CODES_COLLECTION, key: code, userId: SYSTEM_USER_ID },
        ]);
      } catch (error: unknown) {
        log.warn(logger, "room code not removed", { code, error: String(error) });
      }
    },
  };
}

/** Resolves a typed code: normalised, present, unexpired and backed by a live match. */
export function lookupCode(
  store: CodeStore,
  input: string,
  now: number,
  matchExists: (matchId: string) => boolean,
): Result<CodeEntry & { readonly code: string }, RuleError> {
  const code = normaliseRoomCode(input);
  if (code === undefined) return { ok: false, error: ruleError(ErrorCodes.table.invalidCode) };
  const entry = store.read(code);
  if (entry === null) return { ok: false, error: ruleError(ErrorCodes.table.notFound) };
  if (entry.expiresTs <= now) {
    store.remove(code);
    return { ok: false, error: ruleError(ErrorCodes.table.invalidCode, { reason: "expired" }) };
  }
  if (!matchExists(entry.matchId)) {
    store.remove(code);
    return { ok: false, error: ruleError(ErrorCodes.table.notFound, { reason: "closed" }) };
  }
  return { ok: true, value: { ...entry, code } };
}
