import { ErrorCodes, ruleError, sha1Hex, utf8Encode, type RuleError } from "@hulla/shared-ts";

import { itemById, owns, startTrial, type CatalogStore } from "./catalog";
import { type LedgerPorts } from "./ledger";
import { DEFAULT_TZ_OFFSET_MINUTES, localDay, type ProgressStore } from "./progression";

/**
 * Rewarded ads (GDD 13.6, 20.9; ARCHITECTURE 8.10): a reward is granted only from AppLovin's
 * server-to-server callback, whose token is the SHA-1 of the event id and the account's event
 * key. Each event id pays once (the ledger key), each placement stops at its daily cap, and the
 * caps come from the remote configuration. Interstitials are the client's to show; the server
 * tells it the rules and whether the account holds Remove Ads.
 */
export const AD_REWARDS_COLLECTION = "ad_rewards";
export const EVENT_KEY_ENV = "HULLA_APPLOVIN_EVENT_KEY";
export const PLACEMENTS = ["double_coins", "reroll", "theme_trial"] as const;
export type Placement = (typeof PLACEMENTS)[number];
export const TRIAL_MS = 24 * 3600 * 1000;

/** The caps of GDD 13.6, and the configuration flag that can change each one. */
export const DEFAULT_CAPS: Readonly<Record<Placement, number>> = {
  double_coins: 5,
  reroll: 2,
  theme_trial: 1,
};
export const CAP_FLAGS: Readonly<Record<Placement, string>> = {
  double_coins: "ad_cap_double_coins",
  reroll: "ad_cap_reroll",
  theme_trial: "ad_cap_theme_trial",
};

export interface AdRecord {
  readonly day: string;
  readonly counts: Readonly<Record<Placement, number>>;
  /** Extra mission re-rolls earned today and not yet spent. */
  readonly rerollCredits: number;
  /** `tableId:roundId` of the rounds doubled today. */
  readonly doubled: readonly string[];
  readonly tzOffsetMinutes: number;
  readonly updatedTs: number;
}

export const EMPTY_AD_RECORD: AdRecord = {
  day: "",
  counts: { double_coins: 0, reroll: 0, theme_trial: 0 },
  rerollCredits: 0,
  doubled: [],
  tzOffsetMinutes: DEFAULT_TZ_OFFSET_MINUTES,
  updatedTs: 0,
};

/** The record for today: counts, credits and doubled rounds reset at local midnight. */
export function forDay(record: AdRecord, now: number): AdRecord {
  const day = localDay(now, record.tzOffsetMinutes);
  if (record.day === day) return record;
  return { ...EMPTY_AD_RECORD, tzOffsetMinutes: record.tzOffsetMinutes, day, updatedTs: now };
}

/** AppLovin's `{EVENT_TOKEN}`: sha1 of the event id followed by the event key, as hex. */
export function eventToken(eventId: string, eventKey: string): string {
  return sha1Hex(utf8Encode(`${eventId}${eventKey}`));
}

export function verifyEventToken(eventId: string, token: string, eventKey: string): boolean {
  if (eventId === "" || token === "" || eventKey === "") return false;
  const expected = eventToken(eventId, eventKey);
  const given = token.trim().toLowerCase();
  if (given.length !== expected.length) return false;
  let diff = 0;
  for (let i = 0; i < expected.length; i += 1) {
    diff |= expected.charCodeAt(i) ^ given.charCodeAt(i);
  }
  return diff === 0;
}

export interface CustomData {
  readonly placement: Placement;
  readonly tableId?: string;
  readonly roundId?: string;
  readonly itemId?: string;
}

const ID = /^[A-Za-z0-9_.:-]{1,128}$/;

/**
 * The client's custom data on the ad: `double_coins:<tableId>:<roundId>`, `reroll` or
 * `theme_trial:<itemId>`. Null for anything else.
 */
export function parseCustomData(raw: string): CustomData | null {
  const parts = raw.split(":");
  const placement = parts[0];
  if (placement === "double_coins") {
    const tableId = parts[1] ?? "";
    const roundId = parts.slice(2).join(":");
    if (parts.length < 3 || !ID.test(tableId) || !ID.test(roundId)) return null;
    return { placement, tableId, roundId };
  }
  if (placement === "reroll") return parts.length === 1 ? { placement } : null;
  if (placement === "theme_trial") {
    const itemId = parts[1] ?? "";
    return parts.length === 2 && ID.test(itemId) ? { placement, itemId } : null;
  }
  return null;
}

export interface AdPorts {
  readonly ledger: LedgerPorts;
  readonly progress: Pick<ProgressStore, "read">;
  readonly catalog: CatalogStore;
  cap(placement: Placement): number;
}

export type AdRewardResponse =
  | {
      readonly ok: true;
      readonly applied: boolean;
      readonly placement: Placement;
      readonly coins?: number;
      readonly rerollCredits?: number;
      readonly itemId?: string;
      readonly until?: number;
      readonly remaining: number;
    }
  | { readonly ok: false; readonly error: RuleError };

/** Grants one verified callback once, within the placement's daily cap. */
export function applyAdReward(
  ports: AdPorts,
  record: AdRecord,
  userId: string,
  data: CustomData,
  eventId: string,
  now: number,
): { readonly record: AdRecord; readonly response: AdRewardResponse } {
  const today = forDay(record, now);
  const cap = ports.cap(data.placement);
  const used = today.counts[data.placement];
  const refuse = (error: RuleError): { record: AdRecord; response: AdRewardResponse } => ({
    record: today,
    response: { ok: false, error },
  });
  // AppLovin retries a callback it did not hear back from: the same event id pays nothing twice.
  if (ports.ledger.applied(userId, `ad:${eventId}`)) {
    return {
      record: today,
      response: {
        ok: true,
        applied: false,
        placement: data.placement,
        remaining: Math.max(0, cap - used),
      },
    };
  }
  if (used >= cap) {
    return refuse(ruleError(ErrorCodes.economy.dailyCapReached, { reason: data.placement, cap }));
  }
  const remainingAfter = Math.max(0, cap - used - 1);
  if (data.placement === "double_coins") {
    const last = ports.progress.read(userId).lastReward;
    if (last === null || last.tableId !== data.tableId || last.roundId !== data.roundId) {
      return refuse(ruleError(ErrorCodes.table.invalidPayload, { reason: "round" }));
    }
    const key = `${data.tableId ?? ""}:${data.roundId ?? ""}`;
    if (today.doubled.includes(key)) {
      return refuse(ruleError(ErrorCodes.economy.alreadyGranted, { reason: "round" }));
    }
    if (!ports.ledger.claim(userId, `ad:${eventId}`)) {
      return {
        record: today,
        response: { ok: true, applied: false, placement: data.placement, remaining: cap - used },
      };
    }
    if (last.coins > 0) {
      ports.ledger.update(
        userId,
        { coins: last.coins },
        {
          reason: "rewarded_ad",
          sourceId: `ad:${eventId}`,
          placement: data.placement,
          roundId: last.roundId,
        },
      );
    }
    const next: AdRecord = {
      ...today,
      counts: { ...today.counts, double_coins: used + 1 },
      doubled: [...today.doubled, key],
      updatedTs: now,
    };
    return {
      record: next,
      response: {
        ok: true,
        applied: true,
        placement: data.placement,
        coins: last.coins,
        remaining: remainingAfter,
      },
    };
  }
  if (data.placement === "theme_trial") {
    const catalog = ports.catalog.catalog();
    const item = itemById(catalog, data.itemId ?? "");
    if (item?.category !== "table") {
      return refuse(ruleError(ErrorCodes.economy.itemUnknown, { itemId: data.itemId ?? "" }));
    }
    const inventory = ports.catalog.inventory(userId);
    if (owns(inventory, catalog, item.id, now)) {
      return refuse(ruleError(ErrorCodes.economy.itemOwned, { itemId: item.id }));
    }
    if (!ports.ledger.claim(userId, `ad:${eventId}`)) {
      return {
        record: today,
        response: { ok: true, applied: false, placement: data.placement, remaining: cap - used },
      };
    }
    const until = now + TRIAL_MS;
    ports.catalog.writeInventory(userId, startTrial(inventory, item.id, until, now));
    const next: AdRecord = {
      ...today,
      counts: { ...today.counts, theme_trial: used + 1 },
      updatedTs: now,
    };
    return {
      record: next,
      response: {
        ok: true,
        applied: true,
        placement: data.placement,
        itemId: item.id,
        until,
        remaining: remainingAfter,
      },
    };
  }
  if (!ports.ledger.claim(userId, `ad:${eventId}`)) {
    return {
      record: today,
      response: { ok: true, applied: false, placement: data.placement, remaining: cap - used },
    };
  }
  const next: AdRecord = {
    ...today,
    counts: { ...today.counts, reroll: used + 1 },
    rerollCredits: today.rerollCredits + 1,
    updatedTs: now,
  };
  return {
    record: next,
    response: {
      ok: true,
      applied: true,
      placement: data.placement,
      rerollCredits: next.rerollCredits,
      remaining: remainingAfter,
    },
  };
}

/** Spends one earned re-roll; the same record when there is none. */
export function spendRerollCredit(record: AdRecord, now: number): AdRecord {
  if (record.rerollCredits <= 0) return record;
  return { ...record, rerollCredits: record.rerollCredits - 1, updatedTs: now };
}

export interface AdsView {
  readonly rewardedEnabled: boolean;
  readonly caps: Readonly<Record<Placement, number>>;
  readonly used: Readonly<Record<Placement, number>>;
  readonly rerollCredits: number;
  readonly interstitials: {
    readonly enabled: boolean;
    readonly fromRound: number;
    readonly intervalSeconds: number;
  };
}

export function isAdRecord(value: unknown): value is AdRecord {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v["day"] === "string" && typeof v["counts"] === "object" && v["counts"] !== null;
}

export interface AdStore {
  read(userId: string): AdRecord;
  write(userId: string, record: AdRecord): void;
}

export function nkAdStore(nk: nkruntime.Nakama): AdStore {
  return {
    read(userId) {
      const objects = nk.storageRead([{ collection: AD_REWARDS_COLLECTION, key: "state", userId }]);
      const value: unknown = objects[0]?.value;
      if (!isAdRecord(value)) return EMPTY_AD_RECORD;
      const counts = value.counts as Partial<Record<Placement, unknown>>;
      return {
        day: value.day,
        counts: {
          double_coins: typeof counts.double_coins === "number" ? counts.double_coins : 0,
          reroll: typeof counts.reroll === "number" ? counts.reroll : 0,
          theme_trial: typeof counts.theme_trial === "number" ? counts.theme_trial : 0,
        },
        rerollCredits: typeof value.rerollCredits === "number" ? value.rerollCredits : 0,
        doubled: Array.isArray(value.doubled)
          ? value.doubled.filter((d): d is string => typeof d === "string")
          : [],
        tzOffsetMinutes:
          typeof value.tzOffsetMinutes === "number"
            ? value.tzOffsetMinutes
            : DEFAULT_TZ_OFFSET_MINUTES,
        updatedTs: typeof value.updatedTs === "number" ? value.updatedTs : 0,
      };
    },
    write(userId, record) {
      nk.storageWrite([
        {
          collection: AD_REWARDS_COLLECTION,
          key: "state",
          userId,
          value: {
            day: record.day,
            counts: record.counts,
            rerollCredits: record.rerollCredits,
            doubled: [...record.doubled],
            tzOffsetMinutes: record.tzOffsetMinutes,
            updatedTs: record.updatedTs,
          },
          permissionRead: 1,
          permissionWrite: 0,
        },
      ]);
    },
  };
}
