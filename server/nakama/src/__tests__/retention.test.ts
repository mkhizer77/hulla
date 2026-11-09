import { describe, expect, it } from "vitest";

import { ROUND_LOG_COLLECTION } from "../match/nakama_match";
import { rpcAdminRetentionPurge } from "../rpc/moderation";
import { OTP_COLLECTION, PHONE_TOKENS_COLLECTION } from "../services/phone_otp";
import { REPORT_INDEX_COLLECTION, REPORTS_COLLECTION, SYSTEM_USER_ID } from "../services/reports";
import { RETENTION, ROUND_LOGS, expired } from "../services/retention";

import { fakeLogger, memoryNk } from "./fake-nk";

const DAY = 86_400_000;
const NOW = 1_800_000_000_000;

describe("retention (privacy policy, #87)", () => {
  it("names the match layer's round log collection", () => {
    expect(ROUND_LOGS).toBe(ROUND_LOG_COLLECTION);
  });

  it("decides per collection, and keeps shapes it doesn't know", () => {
    expect(expired(ROUND_LOGS, { endedTs: NOW - RETENTION.roundLogs - 1 }, NOW)).toBe(true);
    expect(expired(ROUND_LOGS, { endedTs: NOW - RETENTION.roundLogs + DAY }, NOW)).toBe(false);
    expect(expired(REPORTS_COLLECTION, { createdTs: NOW - 366 * DAY }, NOW)).toBe(true);
    expect(expired(REPORTS_COLLECTION, { createdTs: NOW - 300 * DAY }, NOW)).toBe(false);
    expect(expired(REPORT_INDEX_COLLECTION, { entries: [{ ts: NOW - 400 * DAY }] }, NOW)).toBe(
      true,
    );
    expect(
      expired(
        REPORT_INDEX_COLLECTION,
        { entries: [{ ts: NOW - 400 * DAY }, { ts: NOW - DAY }] },
        NOW,
      ),
    ).toBe(false);
    expect(expired(OTP_COLLECTION, { times: [NOW - 2 * DAY] }, NOW)).toBe(true);
    expect(expired(OTP_COLLECTION, { times: [NOW - 2 * DAY, NOW - 60_000] }, NOW)).toBe(false);
    expect(expired(OTP_COLLECTION, { expiresTs: NOW - 2 * DAY }, NOW)).toBe(true);
    expect(expired(PHONE_TOKENS_COLLECTION, { expiresTs: NOW - 60_000 }, NOW)).toBe(false);
    expect(expired(ROUND_LOGS, { other: 1 }, NOW)).toBe(false);
    expect(expired("profiles", { createdTs: 0 }, NOW)).toBe(false);
  });

  it("admin_retention_purge erases what is due, server key only", () => {
    const m = memoryNk();
    const write = (collection: string, key: string, value: Record<string, unknown>): void => {
      m.nk.storageWrite([
        { collection, key, userId: SYSTEM_USER_ID, value, permissionRead: 0, permissionWrite: 0 },
      ]);
    };
    write(ROUND_LOGS, "old", { endedTs: NOW - 200 * DAY });
    write(ROUND_LOGS, "new", { endedTs: NOW - DAY });
    write(REPORTS_COLLECTION, "r1", { createdTs: NOW - 400 * DAY });
    write(OTP_COLLECTION, "address:203.0.113.9", { times: [NOW - 3 * DAY] });
    write(OTP_COLLECTION, "account:u1", { times: [NOW - 1000] });

    const refused = JSON.parse(
      rpcAdminRetentionPurge(
        { userId: "u1", env: {} } as unknown as nkruntime.Context,
        fakeLogger(),
        m.nk,
        "{}",
      ),
    ) as { ok: boolean };
    expect(refused.ok).toBe(false);

    const answer = JSON.parse(
      rpcAdminRetentionPurge(
        { env: {} } as unknown as nkruntime.Context,
        fakeLogger(),
        m.nk,
        JSON.stringify({ now: NOW }),
      ),
    ) as { ok: boolean; purged: Record<string, number> };
    expect(answer).toMatchObject({ ok: true, purged: { round_logs: 1, reports: 1, phone_otp: 1 } });
    const left = (collection: string, key: string): boolean =>
      m.nk.storageRead([{ collection, key, userId: SYSTEM_USER_ID }]).length > 0;
    expect(left(ROUND_LOGS, "old")).toBe(false);
    expect(left(ROUND_LOGS, "new")).toBe(true);
    expect(left(OTP_COLLECTION, "address:203.0.113.9")).toBe(false);
    expect(left(OTP_COLLECTION, "account:u1")).toBe(true);
  });
});
