import { Client } from "@heroiclabs/nakama-js";
import { OPCODES } from "@hulla/protocol/generated";
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

import type { Profile, TableState } from "@hulla/protocol/generated";

describe.skipIf(NAKAMA_HOST === undefined)(
  "spectating a friend's private room against Nakama (GDD 7.7, 9.10)",
  () => {
    const clients: ScriptedClient[] = [];
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("a stranger is refused, a friend watches without a hand and the host seats them", async () => {
      const suffix = String(Date.now());
      const host = await scriptedClient("sh", suffix);
      const guest = await scriptedClient("sg", suffix);
      const friend = await scriptedClient("sf", suffix);
      const stranger = await scriptedClient("sx", suffix);
      clients.push(host, guest, friend, stranger);
      const api = new Client(SERVER_KEY, NAKAMA_HOST ?? "", NAKAMA_PORT, false);
      const hostCode =
        (await rpc<{ profile?: Profile }>(host, "profile_get", {})).profile?.friendCode ?? "";
      await rpc<object>(friend, "friend_add_by_code", { friendCode: hostCode });
      await api.addFriends(host.session, [friend.session.user_id ?? ""]);

      const created = await rpc<{ ok: boolean; matchId: string; tableId: string }>(
        host,
        "table_create",
        { gameId: "thulla", mode: "private" },
      );
      setTableId(created.tableId);
      await host.socket.joinMatch(created.matchId);
      await guest.socket.joinMatch(created.matchId);
      await host.next(
        (e) =>
          e.type === "presence" &&
          (e.payload as { playerId: string }).playerId === guest.session.user_id &&
          (e.payload as { status: string }).status === "joined",
      );

      await expect(
        stranger.socket.joinMatch(created.matchId, undefined, { asSpectator: "true" }),
      ).rejects.toBeDefined();

      await friend.socket.joinMatch(created.matchId, undefined, { asSpectator: "true" });
      const watching = (await friend.next((e) => e.type === "table_state")).payload as TableState;
      expect(watching.spectators).toEqual([
        {
          userId: friend.session.user_id,
          displayName: expect.any(String) as string,
          seatNext: false,
        },
      ]);
      expect(watching.resumeToken).toBeNull();

      await host.send(created.matchId, "seat_spectator", OPCODES.seat_spectator.opcode, {
        userId: friend.session.user_id,
      });
      const hostError = await host.next((e) => e.type === "error", 1500).catch(() => null);
      expect(hostError, JSON.stringify(hostError?.payload ?? null)).toBeNull();
      const seated = (
        await friend.next(
          (e) => e.type === "table_state" && (e.payload as TableState).spectators.length === 0,
        )
      ).payload as TableState;
      const own = seated.seats.find((s) => s.playerId === friend.session.user_id);
      expect(own).toMatchObject({ seat: 2, connected: true });
      expect(seated.resumeToken).not.toBeNull();
      const list = await rpc<{
        friends: { userId: string; table: { watchable: boolean; seated: number } | null }[];
      }>(friend, "table_list_friends", {});
      expect(list.friends[0]?.table).toMatchObject({ watchable: true, seated: 3 });
    }, 60_000);
  },
);
