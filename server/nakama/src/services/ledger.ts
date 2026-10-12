import { ErrorCodes, ruleError, type RuleError } from "@hulla/shared-ts";

/**
 * Coins and gems in an append-only ledger (GDD 13.2, 13.9, 20.9, D2): every change is a Nakama
 * wallet update with the ledger on, keyed by a source id so a retry never pays twice. Nothing
 * here, or anywhere else in the server, moves value from one account to another.
 */
export const LEDGER_KEYS_COLLECTION = "ledger_keys";
export const CURRENCIES = ["coins", "gems"] as const;
export type Currency = (typeof CURRENCIES)[number];

export interface LedgerEntry {
  readonly userId: string;
  /** Unique per cause: `${tableId}:${roundId}:round`, `level:${n}`, a purchase id. */
  readonly sourceId: string;
  readonly reason: string;
  readonly coins?: number;
  readonly gems?: number;
  readonly metadata?: Readonly<Record<string, string | number | boolean>>;
}

export interface Balances {
  readonly coins: number;
  readonly gems: number;
}

export interface LedgerPorts {
  /** Whether this source id was applied before. */
  applied(userId: string, sourceId: string): boolean;
  /** True when the key was new and is now taken; false when the source id was already applied. */
  claim(userId: string, sourceId: string): boolean;
  update(
    userId: string,
    changeset: Readonly<Record<string, number>>,
    metadata: Record<string, unknown>,
  ): Balances;
  balances(userId: string): Balances;
}

export type LedgerResponse =
  | { readonly ok: true; readonly applied: boolean; readonly balances: Balances }
  | { readonly ok: false; readonly error: RuleError };

function changesetOf(entry: LedgerEntry, sign: 1 | -1): Record<string, number> {
  const changeset: Record<string, number> = {};
  if (entry.coins !== undefined && entry.coins !== 0) changeset["coins"] = sign * entry.coins;
  if (entry.gems !== undefined && entry.gems !== 0) changeset["gems"] = sign * entry.gems;
  return changeset;
}

function valid(entry: LedgerEntry): RuleError | null {
  if (entry.userId === "" || entry.sourceId === "" || entry.reason === "") {
    return ruleError(ErrorCodes.table.invalidPayload, { reason: "ledger_entry" });
  }
  for (const amount of [entry.coins, entry.gems]) {
    if (amount !== undefined && (!Number.isInteger(amount) || amount < 0)) {
      return ruleError(ErrorCodes.table.invalidPayload, { reason: "amount" });
    }
  }
  return null;
}

/** Adds to one account; the same source id a second time changes nothing. */
export function credit(ports: LedgerPorts, entry: LedgerEntry): LedgerResponse {
  const problem = valid(entry);
  if (problem !== null) return { ok: false, error: problem };
  const changeset = changesetOf(entry, 1);
  if (Object.keys(changeset).length === 0) {
    return { ok: true, applied: false, balances: ports.balances(entry.userId) };
  }
  if (!ports.claim(entry.userId, entry.sourceId)) {
    return { ok: true, applied: false, balances: ports.balances(entry.userId) };
  }
  const balances = ports.update(entry.userId, changeset, {
    reason: entry.reason,
    sourceId: entry.sourceId,
    ...(entry.metadata ?? {}),
  });
  return { ok: true, applied: true, balances };
}

/** Takes from one account, refusing to go below zero; idempotent the same way. */
export function spend(ports: LedgerPorts, entry: LedgerEntry): LedgerResponse {
  const problem = valid(entry);
  if (problem !== null) return { ok: false, error: problem };
  const changeset = changesetOf(entry, -1);
  if (Object.keys(changeset).length === 0) {
    return { ok: true, applied: false, balances: ports.balances(entry.userId) };
  }
  const current = ports.balances(entry.userId);
  if (ports.applied(entry.userId, entry.sourceId)) {
    return { ok: true, applied: false, balances: current };
  }
  if (
    current.coins + (changeset["coins"] ?? 0) < 0 ||
    current.gems + (changeset["gems"] ?? 0) < 0
  ) {
    return { ok: false, error: ruleError(ErrorCodes.economy.insufficientFunds) };
  }
  if (!ports.claim(entry.userId, entry.sourceId)) {
    return { ok: true, applied: false, balances: current };
  }
  const balances = ports.update(entry.userId, changeset, {
    reason: entry.reason,
    sourceId: entry.sourceId,
    ...(entry.metadata ?? {}),
  });
  return { ok: true, applied: true, balances };
}

export function balancesOf(wallet: Readonly<Record<string, number>>): Balances {
  return { coins: wallet["coins"] ?? 0, gems: wallet["gems"] ?? 0 };
}

export function nkLedgerPorts(nk: nkruntime.Nakama): LedgerPorts {
  const read = (userId: string): Balances => {
    const wallet: unknown = nk.accountGetId(userId).wallet;
    if (typeof wallet === "string") {
      try {
        return balancesOf(JSON.parse(wallet) as Record<string, number>);
      } catch {
        return { coins: 0, gems: 0 };
      }
    }
    return balancesOf((wallet as Record<string, number> | undefined) ?? {});
  };
  return {
    applied(userId, sourceId) {
      return (
        nk.storageRead([{ collection: LEDGER_KEYS_COLLECTION, key: sourceId, userId }]).length > 0
      );
    },
    claim(userId, sourceId) {
      try {
        nk.storageWrite([
          {
            collection: LEDGER_KEYS_COLLECTION,
            key: sourceId,
            userId,
            value: { ts: Date.now() },
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
    update(userId, changeset, metadata) {
      const result = nk.walletUpdate(userId, { ...changeset }, metadata, true);
      return balancesOf(result.updated);
    },
    balances: read,
  };
}

export interface LedgerLine {
  readonly id: string;
  readonly changeset: Readonly<Record<string, number>>;
  readonly reason: string;
  readonly sourceId: string;
  readonly ts: number;
}

/** The newest ledger lines for the wallet screen. */
export function ledgerLines(nk: nkruntime.Nakama, userId: string, limit: number): LedgerLine[] {
  const list = nk.walletLedgerList(userId, limit);
  return list.items.map((item) => ({
    id: item.id,
    changeset: item.changeset,
    reason: typeof item.metadata["reason"] === "string" ? item.metadata["reason"] : "",
    sourceId: typeof item.metadata["sourceId"] === "string" ? item.metadata["sourceId"] : "",
    ts: item.createTime,
  }));
}
