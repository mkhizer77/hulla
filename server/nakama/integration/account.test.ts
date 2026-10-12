import { Client } from "@heroiclabs/nakama-js";
import { afterAll, describe, expect, it } from "vitest";

import {
  NAKAMA_HOST,
  NAKAMA_PORT,
  rpc,
  scriptedClient,
  SERVER_KEY,
  type ScriptedClient,
} from "./nakama-client";

interface DeleteResponse {
  readonly ok: boolean;
  readonly deleteAtTs?: number;
  readonly alreadyPending?: boolean;
  readonly cancelled?: boolean;
}

interface ExportResponse {
  readonly ok: boolean;
  readonly profile?: { friendCode: string } | null;
  readonly deletion?: { deleteAtTs: number } | null;
  readonly account?: Record<string, unknown>;
}

async function adminPurge(now: number): Promise<{ purged: string[]; failed: unknown[] }> {
  const url = `http://${NAKAMA_HOST ?? ""}:${NAKAMA_PORT}/v2/rpc/admin_purge_deleted?http_key=local-only-http-key&unwrap`;
  const response = await fetch(url, { method: "POST", body: JSON.stringify({ now }) });
  return (await response.json()) as { purged: string[]; failed: unknown[] };
}

describe.skipIf(NAKAMA_HOST === undefined)(
  "account deletion and export against Nakama (GDD 10.2, 25.4)",
  () => {
    const clients: ScriptedClient[] = [];
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("schedules, exports, cancels, then deletes after the grace period so the session is gone", async () => {
      const suffix = String(Date.now());
      const c = await scriptedClient("del", suffix);
      clients.push(c);
      const first = await rpc<DeleteResponse>(c, "account_delete", {});
      expect(first.ok).toBe(true);
      const deleteAtTs = first.deleteAtTs ?? 0;
      expect(deleteAtTs - Date.now()).toBeGreaterThan(13 * 24 * 3_600_000);
      expect(await rpc<DeleteResponse>(c, "account_delete", {})).toMatchObject({
        ok: true,
        alreadyPending: true,
        deleteAtTs,
      });

      const exported = await rpc<ExportResponse>(c, "account_export", {});
      expect(exported.ok).toBe(true);
      expect(exported.profile?.friendCode).toMatch(/^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$/);
      expect(exported.deletion?.deleteAtTs).toBe(deleteAtTs);
      expect(Object.keys(exported.account ?? {}).length).toBeGreaterThan(0);

      expect(await rpc<DeleteResponse>(c, "account_delete_cancel", {})).toEqual({
        ok: true,
        cancelled: true,
      });
      expect(await rpc<DeleteResponse>(c, "account_delete_cancel", {})).toEqual({
        ok: true,
        cancelled: false,
      });

      // Not yet due: the purge leaves the account alone; past the grace it goes, and the session with it.
      const again = await rpc<DeleteResponse>(c, "account_delete", {});
      const due = again.deleteAtTs ?? 0;
      const early = await adminPurge(due - 1);
      expect(early.purged).not.toContain(c.session.user_id);
      const late = await adminPurge(due + 1);
      expect(late.purged, JSON.stringify(late)).toContain(c.session.user_id);

      // Gone: the profile with it, the export refuses, and the device cannot sign back in. The old
      // token lives until it expires, which is why the request record sits under the system user.
      expect(await rpc<{ ok: boolean }>(c, "profile_get", {})).toMatchObject({
        ok: false,
        error: { code: "profile.not_found" },
      });
      await expect(rpc<ExportResponse>(c, "account_export", {})).rejects.toBeDefined();
      const client = new Client(SERVER_KEY, NAKAMA_HOST ?? "", NAKAMA_PORT, false);
      await expect(
        client.authenticateDevice(`hulla-it-del-${suffix}`, false),
      ).rejects.toBeDefined();
    }, 60_000);
  },
);
