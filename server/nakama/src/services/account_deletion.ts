import { ErrorCodes, ruleError, type RuleError } from "@hulla/shared-ts";

import { log } from "../infra/log";

/**
 * In-app account deletion with a 14-day grace period and cancel (GDD 10.2, 25.4; ARCHITECTURE 12.8).
 * A request waits in `deletion_requests`; a scheduled purge tombstones the player id in its round
 * logs, frees its friend code and deletes the Nakama account, which removes storage, wallet,
 * friends and chat. Reports keep their context under the moderation policy (#48).
 */
export const DELETION_COLLECTION = "deletion_requests";
export const ROUND_RESULTS_COLLECTION = "round_results";
export const GRACE_PERIOD_MS = 14 * 24 * 3_600_000;
export const TOMBSTONE = "deleted";

export interface DeletionRequest {
  readonly userId: string;
  readonly requestedTs: number;
  readonly deleteAtTs: number;
}

export interface DeletionStore {
  read(userId: string): DeletionRequest | null;
  write(request: DeletionRequest): void;
  remove(userId: string): void;
  listDue(now: number): readonly DeletionRequest[];
  /** What the listing sees, for a dry run: collection, key, owner and value keys per object. */
  inspect?(): readonly { collection: string; key: string; owner: string; keys: string[] }[];
}

/** What the purge touches besides the request itself. */
export interface PurgePorts {
  /** The player's rounds as `tableId:roundId` keys of the round logs. */
  roundKeys(userId: string): readonly string[];
  tombstone(roundKey: string, userId: string): void;
  freeFriendCode(userId: string): void;
  deleteAccount(userId: string): void;
}

export type DeleteResponse =
  | { readonly ok: true; readonly deleteAtTs: number; readonly alreadyPending: boolean }
  | { readonly ok: false; readonly error: RuleError };

export function requestDeletion(
  store: DeletionStore,
  userId: string | null,
  now: number,
): DeleteResponse {
  if (userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  const pending = store.read(userId);
  if (pending !== null) return { ok: true, deleteAtTs: pending.deleteAtTs, alreadyPending: true };
  const request: DeletionRequest = { userId, requestedTs: now, deleteAtTs: now + GRACE_PERIOD_MS };
  store.write(request);
  return { ok: true, deleteAtTs: request.deleteAtTs, alreadyPending: false };
}

export type CancelResponse =
  | { readonly ok: true; readonly cancelled: boolean }
  | { readonly ok: false; readonly error: RuleError };

export function cancelDeletion(store: DeletionStore, userId: string | null): CancelResponse {
  if (userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  const pending = store.read(userId);
  if (pending === null) return { ok: true, cancelled: false };
  store.remove(userId);
  return { ok: true, cancelled: true };
}

export interface PurgeReport {
  readonly purged: readonly string[];
  readonly failed: readonly { readonly userId: string; readonly error: string }[];
  /** Requests found due; a run deletes at most `MAX_PURGE_PER_RUN` of them. */
  readonly due: number;
}

export const MAX_PURGE_PER_RUN = 50;

/** Deletes every account whose grace period has passed; one failure never stops the others. */
export function purgeDue(store: DeletionStore, ports: PurgePorts, now: number): PurgeReport {
  const purged: string[] = [];
  const failed: { userId: string; error: string }[] = [];
  const dueRequests = store.listDue(now);
  for (const request of dueRequests.slice(0, MAX_PURGE_PER_RUN)) {
    try {
      for (const key of ports.roundKeys(request.userId)) ports.tombstone(key, request.userId);
      ports.freeFriendCode(request.userId);
      ports.deleteAccount(request.userId);
      store.remove(request.userId);
      purged.push(request.userId);
    } catch (error: unknown) {
      failed.push({ userId: request.userId, error: String(error) });
    }
  }
  return { purged, failed, due: dueRequests.length };
}

export const SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000";

export function nkDeletionStore(nk: nkruntime.Nakama): DeletionStore {
  return {
    read(userId) {
      const objects = nk.storageRead([
        { collection: DELETION_COLLECTION, key: userId, userId: SYSTEM_USER_ID },
      ]);
      const first = objects[0];
      return first === undefined ? null : (first.value as unknown as DeletionRequest);
    },
    write(request) {
      nk.storageWrite([
        {
          collection: DELETION_COLLECTION,
          key: request.userId,
          userId: SYSTEM_USER_ID,
          value: request,
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
    remove(userId) {
      nk.storageDelete([{ collection: DELETION_COLLECTION, key: userId, userId: SYSTEM_USER_ID }]);
    },
    inspect() {
      const list = nk.storageList(SYSTEM_USER_ID, DELETION_COLLECTION, 20);
      return (list.objects ?? []).map((o) => ({
        collection: o.collection,
        key: o.key,
        owner: o.userId,
        keys: Object.keys(o.value),
      }));
    },
    listDue(now) {
      // Every object is checked for collection and shape: a listing must never be trusted to
      // contain only requests, and nothing but a well-formed request may delete an account.
      const due: DeletionRequest[] = [];
      const seen = new Set<string>();
      let cursor: string | undefined;
      for (let page = 0; page < 100; page += 1) {
        const list = nk.storageList(SYSTEM_USER_ID, DELETION_COLLECTION, 100, cursor);
        const objects = list.objects ?? [];
        for (const object of objects) {
          if (seen.has(object.key)) continue;
          seen.add(object.key);
          if (object.collection !== DELETION_COLLECTION) continue;
          const value = object.value as Partial<DeletionRequest>;
          if (
            typeof value.userId !== "string" ||
            value.userId !== object.key ||
            typeof value.deleteAtTs !== "number" ||
            typeof value.requestedTs !== "number"
          ) {
            continue;
          }
          if (value.deleteAtTs <= now) {
            due.push({
              userId: value.userId,
              requestedTs: value.requestedTs,
              deleteAtTs: value.deleteAtTs,
            });
          }
        }
        // Nakama may hand back a cursor on the last page; a short page or a repeated cursor ends it.
        if (
          objects.length < 100 ||
          list.cursor === undefined ||
          list.cursor === "" ||
          list.cursor === cursor
        ) {
          break;
        }
        cursor = list.cursor;
      }
      return due;
    },
  };
}

export function nkPurgePorts(nk: nkruntime.Nakama, logger: nkruntime.Logger): PurgePorts {
  return {
    roundKeys(userId) {
      const keys = new Set<string>();
      let cursor: string | undefined;
      for (let page = 0; page < 100; page += 1) {
        const list = nk.storageList(userId, ROUND_RESULTS_COLLECTION, 100, cursor);
        const objects = list.objects ?? [];
        for (const object of objects) keys.add(object.key);
        if (
          objects.length < 100 ||
          list.cursor === undefined ||
          list.cursor === "" ||
          list.cursor === cursor
        ) {
          break;
        }
        cursor = list.cursor;
      }
      return [...keys];
    },
    tombstone(roundKey, userId) {
      const objects = nk.storageRead([
        { collection: "round_logs", key: roundKey, userId: SYSTEM_USER_ID },
      ]);
      const first = objects[0];
      if (first === undefined) return;
      const record = first.value as { players?: (string | null)[] };
      const players = (record.players ?? []).map((p) => (p === userId ? TOMBSTONE : p));
      nk.storageWrite([
        {
          collection: "round_logs",
          key: roundKey,
          userId: SYSTEM_USER_ID,
          value: { ...record, players },
          version: first.version,
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
    freeFriendCode(userId) {
      const objects = nk.storageRead([{ collection: "profiles", key: "profile", userId }]);
      const code = (objects[0]?.value as { friendCode?: unknown } | undefined)?.friendCode;
      if (typeof code !== "string") return;
      try {
        nk.storageDelete([{ collection: "friend_codes", key: code, userId: SYSTEM_USER_ID }]);
      } catch (error: unknown) {
        log.warn(logger, "friend code not freed", { error: String(error) });
      }
    },
    deleteAccount(userId) {
      try {
        nk.sessionLogout(userId);
      } catch (error: unknown) {
        log.debug(logger, "no sessions to log out", { error: String(error) });
      }
      nk.accountDeleteId(userId, true);
    },
  };
}
