import { afterAll, describe, expect, it } from "vitest";

import { NAKAMA_HOST, rpc, scriptedClient, type ScriptedClient } from "./nakama-client";

interface Created {
  readonly ok: boolean;
  readonly matchId: string;
  readonly code: string;
}

interface Invited {
  readonly ok: boolean;
  readonly sent?: string[];
  readonly skipped?: Record<string, string>;
  readonly remaining?: number;
  readonly error?: { code: string };
}

async function until(check: () => boolean, timeoutMs = 10_000): Promise<void> {
  const start = Date.now();
  while (!check()) {
    if (Date.now() - start > timeoutMs) throw new Error("timed out");
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
}

describe.skipIf(NAKAMA_HOST === undefined)(
  "push registration and table invites (GDD 10.5, 10.10)",
  () => {
    const clients: ScriptedClient[] = [];
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("registers a device, keeps settings, and an invite reaches the friend as an in-app notice", async () => {
      const suffix = String(Date.now());
      const host = await scriptedClient("inv-h", suffix);
      const guest = await scriptedClient("inv-g", suffix);
      clients.push(host, guest);
      const guestId = guest.session.user_id ?? "";
      const hostId = host.session.user_id ?? "";

      expect(
        await rpc<{ ok: boolean; devices: number }>(guest, "push_register", {
          installId: `it-${suffix}`,
          token: "not-a-real-token",
          platform: "android",
          locale: "ur",
          tzOffsetMinutes: 300,
        }),
      ).toMatchObject({ ok: true, devices: 1 });
      expect(
        await rpc<{ ok: boolean; settings: { types: Record<string, boolean> } }>(
          guest,
          "push_settings_set",
          { types: { friend_online: true }, quietHours: false },
        ),
      ).toMatchObject({
        ok: true,
        settings: { types: { friend_online: true }, quietHours: false },
      });
      expect(await rpc<object>(guest, "push_settings_get", {})).toMatchObject({
        ok: true,
        settings: { types: { friend_online: true, table_invite: true } },
      });

      // Not seated: nothing to invite to.
      expect(await rpc<Invited>(guest, "table_invite", { userIds: [hostId] })).toMatchObject({
        ok: false,
        error: { code: "table.not_seated" },
      });

      const created = await rpc<Created>(host, "table_create", { mode: "private", seats: 4 });
      expect(created.ok).toBe(true);
      await host.socket.joinMatch(created.matchId);
      await host.next((e) => e.type === "table_state");

      const invited = await rpc<Invited>(host, "table_invite", { userIds: [guestId, hostId] });
      expect(invited).toMatchObject({ ok: true, sent: [guestId], skipped: { [hostId]: "self" } });
      await until(() => guest.notifications.some((n) => n.code === 1004));
      const notice = guest.notifications.find((n) => n.code === 1004);
      expect(notice).toMatchObject({
        subject: "table_invite",
        sender_id: hostId,
        content: {
          matchId: created.matchId,
          code: created.code,
          gameId: "thulla",
          openSeats: expect.any(Number) as number,
        },
      });
      // The Join action: the code still resolves while the table is open.
      expect(
        await rpc<{ ok: boolean; matchId: string }>(guest, "table_join_by_code", {
          code: created.code,
        }),
      ).toMatchObject({ ok: true, matchId: created.matchId });
      expect(
        await rpc<{ ok: boolean }>(guest, "push_unregister", { installId: `it-${suffix}` }),
      ).toMatchObject({ ok: true, devices: 0 });
    });
  },
);
