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

import type { Profile } from "@hulla/protocol/generated";

interface Friends {
  readonly ok: boolean;
  readonly friends: {
    userId: string;
    state: string;
    lastSeenTs: number | null;
    table: { matchId: string; code: string; joinable: boolean; open: number } | null;
  }[];
}

async function friendsOf(c: ScriptedClient): Promise<Friends["friends"]> {
  return (await rpc<Friends>(c, "table_list_friends", {})).friends;
}

describe.skipIf(NAKAMA_HOST === undefined)(
  "presence and joinable tables against Nakama (GDD 10.4)",
  () => {
    const clients: ScriptedClient[] = [];
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("a friend shows online, in the lobby with the table, away, then offline with last seen", async () => {
      const suffix = String(Date.now());
      const ali = await scriptedClient("pa", suffix);
      const sara = await scriptedClient("ps", suffix);
      clients.push(ali, sara);
      const aliId = ali.session.user_id ?? "";
      const api = new Client(SERVER_KEY, NAKAMA_HOST ?? "", NAKAMA_PORT, false);
      const aliCode =
        (await rpc<{ profile?: Profile }>(ali, "profile_get", {})).profile?.friendCode ?? "";
      await rpc<object>(sara, "friend_add_by_code", { friendCode: aliCode });
      await api.addFriends(ali.session, [sara.session.user_id ?? ""]);

      const [online] = await friendsOf(sara);
      expect(online).toMatchObject({ userId: aliId, state: "online", table: null });

      const created = await rpc<{ ok: boolean; matchId: string; code: string }>(
        ali,
        "table_create",
        {
          gameId: "thulla",
          mode: "private",
        },
      );
      await ali.socket.joinMatch(created.matchId);
      await ali.next((e) => e.type === "table_state");
      const [seated] = await friendsOf(sara);
      expect(seated).toMatchObject({ userId: aliId, state: "lobby" });
      expect(seated?.table).toMatchObject({
        matchId: created.matchId,
        code: created.code,
        joinable: true,
        open: 5,
      });

      expect(await rpc<object>(ali, "presence_set", { away: true })).toEqual({
        ok: true,
        away: true,
      });
      expect((await friendsOf(sara))[0]).toMatchObject({ state: "away" });
      expect(await rpc<object>(ali, "presence_set", { away: false })).toEqual({
        ok: true,
        away: false,
      });

      await ali.socket.leaveMatch(created.matchId);
      await sara.next(() => false, 500).catch(() => undefined);
      expect((await friendsOf(sara))[0]).toMatchObject({ state: "online", table: null });

      ali.close();
      await sara.next(() => false, 1500).catch(() => undefined);
      const gone = (await friendsOf(sara))[0];
      expect(gone).toMatchObject({ state: "offline" });
      expect(gone?.lastSeenTs).toBeGreaterThan(0);
      const [fromAli] = await friendsOf(ali);
      expect(fromAli).toMatchObject({ userId: sara.session.user_id, state: "online", table: null });
    }, 60_000);
  },
);
