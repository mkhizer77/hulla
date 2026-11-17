import { sha256, stateFromSeed, utf8Encode, Xoshiro128StarStar } from "@hulla/shared-ts";

import { log } from "../infra/log";
import {
  nkPurgePorts,
  ROUND_RESULTS_COLLECTION,
  SYSTEM_USER_ID,
} from "../services/account_deletion";
import {
  MERGE_CODES_COLLECTION,
  mergeEntitlements,
  mergeInventory,
  mergeProgress,
  prepareMerge,
  redeemMerge,
  type MergeCode,
  type MergeCodeStore,
  type MergePorts,
} from "../services/account_merge";
import { nkCatalogStore } from "../services/catalog";
import { credit, nkLedgerPorts } from "../services/ledger";
import { nkProfileStore } from "../services/profiles";
import { nkProgressStore } from "../services/progression";
import { nkEntitlementStore } from "../services/purchases";

export const MERGES_COLLECTION = "merges";

/** A guest signs in by device id only; anything else linked makes it a full account. */
function isGuest(nk: nkruntime.Nakama, userId: string): boolean {
  try {
    const account = nk.accountGetId(userId);
    const user = account.user as unknown as Record<string, unknown>;
    const linked = [
      account.email,
      account.customId,
      user["facebookId"],
      user["googleId"],
      user["appleId"],
      user["gamecenterId"],
      user["steamId"],
    ];
    return linked.every((id) => typeof id !== "string" || id === "");
  } catch {
    return false;
  }
}

function nkMergeCodes(nk: nkruntime.Nakama): MergeCodeStore {
  return {
    read(code) {
      const objects = nk.storageRead([
        { collection: MERGE_CODES_COLLECTION, key: code, userId: SYSTEM_USER_ID },
      ]);
      const first = objects[0];
      return first === undefined ? null : (first.value as unknown as MergeCode);
    },
    write(code, value) {
      try {
        nk.storageWrite([
          {
            collection: MERGE_CODES_COLLECTION,
            key: code,
            userId: SYSTEM_USER_ID,
            value: value,
            version: "*",
            permissionRead: 0,
            permissionWrite: 0,
          },
        ]);
        return true;
      } catch {
        return false;
      }
    },
    remove(code) {
      try {
        nk.storageDelete([
          { collection: MERGE_CODES_COLLECTION, key: code, userId: SYSTEM_USER_ID },
        ]);
      } catch {
        // already gone
      }
    },
  };
}

function walletOf(nk: nkruntime.Nakama, userId: string): Readonly<Record<string, number>> {
  const wallet: unknown = nk.accountGetId(userId).wallet;
  if (typeof wallet === "string") {
    try {
      return JSON.parse(wallet) as Record<string, number>;
    } catch {
      return {};
    }
  }
  return typeof wallet === "object" && wallet !== null ? (wallet as Record<string, number>) : {};
}

function nkMergePorts(nk: nkruntime.Nakama, logger: nkruntime.Logger): MergePorts {
  const profiles = nkProfileStore(nk, logger);
  const purge = nkPurgePorts(nk, logger);
  return {
    codes: nkMergeCodes(nk),
    readProfile: (userId) => profiles.read(userId),
    writeProfile: (userId, profile) => {
      profiles.write(userId, profile);
    },
    isGuest: (userId) => isGuest(nk, userId),
    readWallet: (userId) => walletOf(nk, userId),
    creditWallet(userId, changeset, fromUserId) {
      const result = credit(nkLedgerPorts(nk), {
        userId,
        sourceId: `merge:${fromUserId}`,
        reason: "guest_merge",
        coins: changeset["coins"] ?? 0,
        gems: changeset["gems"] ?? 0,
        metadata: { from: fromUserId },
      });
      if (!result.ok) throw new Error(`guest wallet not merged: ${result.error.code}`);
    },
    lastWalletMergeTs(userId) {
      const value: unknown = nk.storageRead([
        { collection: MERGES_COLLECTION, key: "wallet", userId },
      ])[0]?.value;
      const ts = (value as { ts?: unknown } | undefined)?.ts;
      return typeof ts === "number" ? ts : null;
    },
    markWalletMerge(userId, now) {
      nk.storageWrite([
        {
          collection: MERGES_COLLECTION,
          key: "wallet",
          userId,
          value: { ts: now },
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
    moveOwned(fromUserId, toUserId) {
      const now = Date.now();
      const catalog = nkCatalogStore(nk);
      catalog.writeInventory(
        toUserId,
        mergeInventory(catalog.inventory(toUserId), catalog.inventory(fromUserId), now),
      );
      const entitlements = nkEntitlementStore(nk);
      entitlements.write(
        toUserId,
        mergeEntitlements(entitlements.read(toUserId), entitlements.read(fromUserId), now),
      );
      const progress = nkProgressStore(nk);
      progress.write(
        toUserId,
        mergeProgress(progress.read(toUserId), progress.read(fromUserId), now),
      );
    },
    moveRounds(fromUserId, toUserId) {
      let moved = 0;
      for (const key of purge.roundKeys(fromUserId)) {
        const objects = nk.storageRead([
          { collection: ROUND_RESULTS_COLLECTION, key, userId: fromUserId },
        ]);
        const first = objects[0];
        if (first === undefined) continue;
        nk.storageWrite([
          {
            collection: ROUND_RESULTS_COLLECTION,
            key,
            userId: toUserId,
            value: first.value,
            permissionRead: 1,
            permissionWrite: 0,
          },
        ]);
        const log = nk.storageRead([{ collection: "round_logs", key, userId: SYSTEM_USER_ID }])[0];
        if (log !== undefined) {
          const record = log.value as { players?: (string | null)[] };
          nk.storageWrite([
            {
              collection: "round_logs",
              key,
              userId: SYSTEM_USER_ID,
              value: {
                ...record,
                players: (record.players ?? []).map((p) => (p === fromUserId ? toUserId : p)),
              },
              version: log.version,
              permissionRead: 0,
              permissionWrite: 0,
            },
          ]);
        }
        moved += 1;
      }
      return moved;
    },
    deleteAccount(userId) {
      purge.freeFriendCode(userId);
      purge.deleteAccount(userId);
    },
    devicesOf: (userId) => nk.accountGetId(userId).devices.map((d) => d.id),
    linkDevices(userId, deviceIds) {
      for (const deviceId of deviceIds) {
        try {
          nk.linkDevice(userId, deviceId);
        } catch (error: unknown) {
          log.warn(logger, "device not moved to the merged account", { error: String(error) });
        }
      }
    },
  };
}

/** As the guest: a one-time code the linked account redeems within ten minutes. */
export function rpcAccountMergePrepare(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const rng = Xoshiro128StarStar.fromState(stateFromSeed(sha256(utf8Encode(nk.uuidv4()))));
  return JSON.stringify(
    prepareMerge(nkMergeCodes(nk), rng, ctx.userId ?? null, Date.now(), (id) => isGuest(nk, id)),
  );
}

/** As the linked account: absorbs the guest named by the code and deletes it. */
export function rpcAccountMerge(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const code = ((): string => {
    try {
      const input = (payload === "" ? {} : JSON.parse(payload)) as { code?: unknown };
      return typeof input.code === "string" ? input.code : "";
    } catch {
      return "";
    }
  })();
  const response = redeemMerge(nkMergePorts(nk, logger), ctx.userId ?? null, code, Date.now());
  if (response.ok)
    log.info(logger, "guest merged", { into: ctx.userId ?? "", rounds: response.rounds });
  return JSON.stringify(response);
}
