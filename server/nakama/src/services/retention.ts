import { OTP_COLLECTION, PHONE_TOKENS_COLLECTION } from "./phone_otp";
import { REPORT_INDEX_COLLECTION, REPORTS_COLLECTION, SYSTEM_USER_ID } from "./reports";

const DAY = 24 * 60 * 60 * 1000;

/** The round logs' collection (`match/nakama_match.ts`); named here so services don't import the match layer. */
export const ROUND_LOGS = "round_logs";

/**
 * How long server-owned records stay (privacy policy, `docs/legal/data-inventory.md`, #87). Data a
 * player owns goes with their account; these are the records the system keeps about rounds,
 * reports and phone codes.
 */
export const RETENTION = {
  roundLogs: 180 * DAY,
  reports: 365 * DAY,
  /** Phone-code state, rate counters keyed by number, account or IP address, and sign-in tokens. */
  phoneCodes: DAY,
} as const;

/** Whether a stored object has outlived its collection's retention; unknown shapes are kept. */
export function expired(
  collection: string,
  value: Record<string, unknown>,
  nowMs: number,
): boolean {
  const number = (key: string): number | null =>
    typeof value[key] === "number" ? value[key] : null;
  switch (collection) {
    case ROUND_LOGS: {
      const ts = number("endedTs") ?? number("startedTs");
      return ts !== null && nowMs - ts > RETENTION.roundLogs;
    }
    case REPORTS_COLLECTION: {
      const ts = number("createdTs");
      return ts !== null && nowMs - ts > RETENTION.reports;
    }
    case REPORT_INDEX_COLLECTION: {
      const entries = Array.isArray(value["entries"])
        ? (value["entries"] as { ts?: unknown }[])
        : [];
      return entries.every((e) => typeof e.ts === "number" && nowMs - e.ts > RETENTION.reports);
    }
    case OTP_COLLECTION: {
      if (Array.isArray(value["times"])) {
        return (value["times"] as unknown[]).every(
          (t) => typeof t === "number" && nowMs - t > RETENTION.phoneCodes,
        );
      }
      const ts = number("expiresTs");
      return ts !== null && nowMs - ts > RETENTION.phoneCodes;
    }
    case PHONE_TOKENS_COLLECTION: {
      const ts = number("expiresTs");
      return ts !== null && nowMs - ts > RETENTION.phoneCodes;
    }
    default:
      return false;
  }
}

export const PURGED_COLLECTIONS = [
  ROUND_LOGS,
  REPORTS_COLLECTION,
  REPORT_INDEX_COLLECTION,
  OTP_COLLECTION,
  PHONE_TOKENS_COLLECTION,
] as const;

const PAGE = 100;
/** One run touches at most this many pages per collection; the next run carries on. */
const PAGES_MAX = 50;

/** Deletes every expired system-owned object in the purged collections; returns the count per collection. */
export function purgeExpired(nk: nkruntime.Nakama, nowMs: number): Record<string, number> {
  const counts: Record<string, number> = {};
  for (const collection of PURGED_COLLECTIONS) {
    const stale: string[] = [];
    let cursor: string | undefined;
    for (let page = 0; page < PAGES_MAX; page += 1) {
      const result = nk.storageList(SYSTEM_USER_ID, collection, PAGE, cursor);
      const objects = result.objects ?? [];
      for (const object of objects) {
        if (object.collection !== collection) continue;
        if (expired(collection, object.value, nowMs) && !stale.includes(object.key)) {
          stale.push(object.key);
        }
      }
      // Nakama can hand back the same cursor on the last page; stop rather than loop.
      if (
        objects.length < PAGE ||
        result.cursor === undefined ||
        result.cursor === "" ||
        result.cursor === cursor
      ) {
        break;
      }
      cursor = result.cursor;
    }
    if (stale.length > 0) {
      nk.storageDelete(stale.map((key) => ({ collection, key, userId: SYSTEM_USER_ID })));
    }
    counts[collection] = stale.length;
  }
  return counts;
}
