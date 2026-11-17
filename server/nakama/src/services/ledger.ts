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
  /** Takes the key with no wallet change, for grants that carry no currency. True when it was new. */
  claim(userId: string, sourceId: string): boolean;
  /**
   * Takes the key and changes the wallet in one transaction, so neither happens without the
   * other. Null when the source id was already applied; throws when the wallet refuses.
   */
  apply(
    userId: string,
    sourceId: string,
    changeset: Readonly<Record<string, number>>,
    metadata: Record<string, unknown>,
  ): Balances | null;
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
  const balances = ports.apply(entry.userId, entry.sourceId, changeset, {
    reason: entry.reason,
    sourceId: entry.sourceId,
    ...(entry.metadata ?? {}),
  });
  if (balances === null) {
    return { ok: true, applied: false, balances: ports.balances(entry.userId) };
  }
  return { ok: true, applied: true, balances };
}

/**
 * Takes a source id for a grant that may or may not carry currency: with a changeset the key and
 * the wallet change land together, without one only the key is taken. False when already applied.
 */
export function claimWith(
  ports: LedgerPorts,
  userId: string,
  sourceId: string,
  changeset: Readonly<Record<string, number>>,
  metadata: Record<string, unknown>,
): boolean {
  const amounts: Record<string, number> = {};
  for (const [currency, amount] of Object.entries(changeset)) {
    if (amount !== 0) amounts[currency] = amount;
  }
  if (Object.keys(amounts).length === 0) return ports.claim(userId, sourceId);
  return ports.apply(userId, sourceId, amounts, { ...metadata, sourceId }) !== null;
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
  let balances: Balances | null;
  try {
    balances = ports.apply(entry.userId, entry.sourceId, changeset, {
      reason: entry.reason,
      sourceId: entry.sourceId,
      ...(entry.metadata ?? {}),
    });
  } catch {
    // Another spend got there between the check and the write: nothing was taken and no key is held.
    return { ok: false, error: ruleError(ErrorCodes.economy.insufficientFunds) };
  }
  if (balances === null) return { ok: true, applied: false, balances: current };
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
  const applied = (userId: string, sourceId: string): boolean =>
    nk.storageRead([{ collection: LEDGER_KEYS_COLLECTION, key: sourceId, userId }]).length > 0;
  const keyWrite = (userId: string, sourceId: string): nkruntime.StorageWriteRequest => ({
    collection: LEDGER_KEYS_COLLECTION,
    key: sourceId,
    userId,
    value: { ts: Date.now() },
    version: "*",
    permissionRead: 0,
    permissionWrite: 0,
  });
  return {
    applied,
    claim(userId, sourceId) {
      try {
        nk.storageWrite([keyWrite(userId, sourceId)]);
        return true;
      } catch {
        return false;
      }
    },
    apply(userId, sourceId, changeset, metadata) {
      try {
        const result = nk.multiUpdate(
          null,
          [keyWrite(userId, sourceId)],
          null,
          [{ userId, changeset: { ...changeset }, metadata }],
          true,
        );
        const ack = result.walletUpdateAcks[0];
        return ack === undefined ? read(userId) : balancesOf(ack.updated);
      } catch (error) {
        // The key was there already, or the wallet refused; only the first is "already applied".
        if (applied(userId, sourceId)) return null;
        throw error;
      }
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
