import { Client } from "@heroiclabs/nakama-js";
import { afterAll, describe, expect, it } from "vitest";

import {
  NAKAMA_HOST,
  NAKAMA_PORT,
  rpc,
  scriptedClient,
  SERVER_KEY,
  setTableId,
  type ScriptedClient,
} from "./nakama-client";

interface Created {
  readonly ok: boolean;
  readonly matchId: string;
  readonly tableId: string;
}

const ROOM = 1;

const query = (lane: string): string =>
  `+properties.game:thulla +properties.mode:quick +properties.lane:${lane}`;

function search(
  c: ScriptedClient,
  lane: string,
): { matched: Promise<string>; ticket: Promise<void> } {
  let resolve: (id: string) => void = () => undefined;
  const matched = new Promise<string>((r) => {
    resolve = r;
  });
  c.socket.onmatchmakermatched = (m) => {
    resolve(m.match_id);
  };
  const ticket = c.socket
    .addMatchmaker(query(lane), 2, 2, { game: "thulla", mode: "quick", lane }, { seats: 2 })
    .then(() => undefined);
  return { matched, ticket };
}

const settled = (p: Promise<unknown>, ms: number): Promise<"matched" | "pending"> =>
  Promise.race([
    p.then(() => "matched" as const),
    new Promise<"pending">((r) =>
      setTimeout(() => {
        r("pending");
      }, ms),
    ),
  ]);

describe.skipIf(NAKAMA_HOST === undefined)(
  "blocking and reporting against Nakama (GDD 10.11, 24.3)",
  () => {
    const clients: ScriptedClient[] = [];
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("a blocked pair is muted both ways, cannot send requests and never matches", async () => {
      const suffix = String(Date.now());
      const ali = await scriptedClient("ba", suffix);
      const bilal = await scriptedClient("bb", suffix);
      const cat = await scriptedClient("bc", suffix);
      const dan = await scriptedClient("bd", suffix);
      clients.push(ali, bilal, cat, dan);
      const bilalId = bilal.session.user_id ?? "";
      const aliId = ali.session.user_id ?? "";

      expect(await rpc<object>(ali, "block_set", { userId: bilalId, blocked: true })).toEqual({
        ok: true,
        blocked: [bilalId],
      });
      expect(await rpc<object>(bilal, "mutes_get", {})).toEqual({ ok: true, mutes: [aliId] });
      expect(await rpc<object>(ali, "mutes_get", {})).toEqual({ ok: true, mutes: [bilalId] });
      const aliCode = (await rpc<{ profile?: { friendCode: string } }>(ali, "profile_get", {}))
        .profile?.friendCode;
      expect(await rpc<object>(bilal, "friend_add_by_code", { friendCode: aliCode })).toMatchObject(
        {
          ok: false,
          error: { code: "social.blocked" },
        },
      );

      // Two strangers on one lane match; the blocked pair on another lane never does.
      const free = [search(cat, `free${suffix}`), search(dan, `free${suffix}`)];
      const blocked = [search(ali, `blk${suffix}`), search(bilal, `blk${suffix}`)];
      await Promise.all([...free, ...blocked].map((s) => s.ticket));
      const freeMatch = await Promise.all(free.map((s) => s.matched));
      expect(freeMatch[0]).toBe(freeMatch[1]);
      expect(await settled(Promise.race(blocked.map((s) => s.matched)), 12_000)).toBe("pending");

      expect(await rpc<object>(ali, "block_set", { userId: bilalId, blocked: false })).toEqual({
        ok: true,
        blocked: [],
      });
      expect(await rpc<object>(bilal, "mutes_get", {})).toEqual({ ok: true, mutes: [] });
    }, 90_000);

    it("a report carries the table's chat and three reports mute the target", async () => {
      const suffix = `${String(Date.now())}r`;
      const bad = await scriptedClient("rbad", suffix);
      const reporters = [
        await scriptedClient("r1", suffix),
        await scriptedClient("r2", suffix),
        await scriptedClient("r3", suffix),
      ];
      clients.push(bad, ...reporters);
      const badId = bad.session.user_id ?? "";
      const created = await rpc<Created>(bad, "table_create", {
        gameId: "thulla",
        mode: "private",
      });
      setTableId(created.tableId);
      await bad.socket.joinMatch(created.matchId);
      const channel = await bad.socket.joinChat(created.matchId, ROOM, false, false);
      await bad.socket.writeChatMessage(channel.id, { text: "you are shit at this" });

      const first = await rpc<{ ok: boolean; chatLines: number; autoMuted: boolean }>(
        reporters[0] ?? bad,
        "report_create",
        { userId: badId, reason: "abusive_text", tableId: created.matchId },
      );
      expect(first).toMatchObject({ ok: true, chatLines: 1, autoMuted: false, duplicate: false });
      await rpc<object>(reporters[1] ?? bad, "report_create", { userId: badId, reason: "spam" });
      const third = await rpc<{ autoMuted: boolean }>(reporters[2] ?? bad, "report_create", {
        userId: badId,
        reason: "abusive_voice",
      });
      expect(third).toMatchObject({ ok: true, autoMuted: true });

      await new Promise((r) => setTimeout(r, 2_100));
      await expect(
        bad.socket.writeChatMessage(channel.id, { text: "still here" }),
      ).rejects.toBeDefined();

      const url = `http://${NAKAMA_HOST ?? ""}:${NAKAMA_PORT}/v2/rpc/reports_list?http_key=local-only-http-key&unwrap`;
      const listed = (await (await fetch(url, { method: "POST", body: "{}" })).json()) as {
        reports: { targetId: string; chat: { text: string }[] }[];
      };
      const mine = listed.reports.filter((r) => r.targetId === badId);
      expect(mine).toHaveLength(3);
      expect(mine.some((r) => r.chat.some((l) => l.text === "you are **** at this"))).toBe(true);
      expect(await rpc<object>(bad, "reports_list", {})).toMatchObject({
        ok: false,
        error: { code: "table.not_authorised" },
      });
    }, 90_000);

    it("a banned account cannot join a table, a banned device cannot sign in, and clear lifts both", async () => {
      const suffix = `${String(Date.now())}b`;
      const host = await scriptedClient("bh", suffix);
      const banned = await scriptedClient("bz", suffix);
      clients.push(host, banned);
      const bannedId = banned.session.user_id ?? "";
      const created = await rpc<Created>(host, "table_create", {
        gameId: "thulla",
        mode: "private",
      });
      await host.socket.joinMatch(created.matchId);

      const admin = async (id: string, body: object): Promise<Record<string, unknown>> => {
        const url = `http://${NAKAMA_HOST ?? ""}:${NAKAMA_PORT}/v2/rpc/${id}?http_key=local-only-http-key&unwrap`;
        return (await (
          await fetch(url, { method: "POST", body: JSON.stringify(body) })
        ).json()) as Record<string, unknown>;
      };
      const sanction = await admin("admin_sanction", {
        userId: bannedId,
        action: "ban",
        hours: 1,
        device: true,
        reason: "cheating",
      });
      expect(sanction).toMatchObject({
        ok: true,
        sanctions: { bannedUntilTs: expect.any(Number) as number },
      });
      await expect(banned.socket.joinMatch(created.matchId)).rejects.toBeDefined();
      const client = new Client(SERVER_KEY, NAKAMA_HOST ?? "", NAKAMA_PORT, false);
      await expect(client.authenticateDevice(`hulla-it-bz-${suffix}`, false)).rejects.toBeDefined();
      expect(await rpc<object>(host, "sanctions_get", {})).toMatchObject({
        ok: true,
        bannedUntilTs: null,
      });

      expect(
        await admin("admin_sanction", { userId: bannedId, action: "clear", reason: "appeal" }),
      ).toMatchObject({
        ok: true,
        sanctions: { bannedUntilTs: null },
      });
      const back = await client.authenticateDevice(`hulla-it-bz-${suffix}`, false);
      expect(back.user_id).toBe(bannedId);
      const audit = await admin("admin_audit_list", { limit: 5 });
      expect((audit["entries"] as { action: string }[]).map((e) => e.action)).toContain(
        "sanction_ban",
      );
      expect(await rpc<object>(host, "admin_audit_list", {})).toMatchObject({ ok: false });
    }, 90_000);
  },
);
