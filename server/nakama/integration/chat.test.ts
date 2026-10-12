import { Client } from "@heroiclabs/nakama-js";
import { OPCODES, type TableState } from "@hulla/protocol/generated";
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
  readonly code: string;
  readonly tableId: string;
}

function at<T>(items: readonly T[], index: number): T {
  const item = items[index];
  if (item === undefined) throw new Error(`no item ${String(index)}`);
  return item;
}

const ROOM = 1;

describe.skipIf(NAKAMA_HOST === undefined)(
  "quick chat, emotes and the table channel against Nakama",
  () => {
    const clients: ScriptedClient[] = [];
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("phrases and emotes reach every seat and spectator; floods and text abuse are refused", async () => {
      const suffix = `${String(Date.now())}q`;
      for (let i = 0; i < 3; i += 1) clients.push(await scriptedClient(`c${String(i)}`, suffix));
      const spectator = await scriptedClient("spec", suffix);
      clients.push(spectator);
      const host = at(clients, 0);
      const created = await rpc<Created>(host, "table_create", {
        gameId: "thulla",
        mode: "private",
      });
      setTableId(created.tableId);
      for (const c of clients.slice(0, 3)) {
        await c.socket.joinMatch(created.matchId);
        await c.next((e) => e.type === "table_state");
      }
      // Spectators are friends of someone seated (GDD 7.7): the host befriends the watcher first.
      const hostProfile = await rpc<{ profile?: { friendCode: string } }>(host, "profile_get", {});
      await rpc<object>(spectator, "friend_add_by_code", {
        friendCode: hostProfile.profile?.friendCode ?? "",
      });
      await new Client(SERVER_KEY, NAKAMA_HOST ?? "", NAKAMA_PORT, false).addFriends(host.session, [
        spectator.session.user_id ?? "",
      ]);
      await spectator.socket.joinMatch(created.matchId, undefined, { asSpectator: "true" });
      const specState = (await spectator.next((e) => e.type === "table_state"))
        .payload as TableState;
      expect(specState.spectatorCount).toBe(1);

      await at(clients, 1).send(created.matchId, "quick_chat", OPCODES.quick_chat.opcode, {
        phraseId: "T02",
      });
      for (const c of clients) {
        const got = await c.next((e) => e.type === "quick_chat_sent", 10_000);
        expect(got.payload).toEqual({ seat: 1, phraseId: "T02", pack: "v1" });
      }
      await at(clients, 2).send(created.matchId, "emote", OPCODES.emote.opcode, {
        emoteId: "turtle",
        targetSeat: 0,
      });
      for (const c of clients) {
        const got = await c.next((e) => e.type === "emote_sent", 10_000);
        expect(got.payload).toEqual({ seat: 2, emoteId: "turtle", targetSeat: 0 });
      }
      await at(clients, 1).send(created.matchId, "quick_chat", OPCODES.quick_chat.opcode, {
        phraseId: "T03",
      });
      const refused = await at(clients, 1).next((e) => e.type === "error", 10_000);
      expect(refused.payload).toMatchObject({ code: "network.rate_limited" });
      await spectator.send(created.matchId, "quick_chat", OPCODES.quick_chat.opcode, {
        phraseId: "T01",
      });
      const notAllowed = await spectator.next((e) => e.type === "error", 10_000);
      expect(notAllowed.payload).toMatchObject({ code: "table.not_authorised" });

      // Text chat on the table's room channel: the before-hook limits the pace and blocks links.
      const channel = await host.socket.joinChat(created.matchId, ROOM, false, false);
      const other = at(clients, 1);
      const otherChannel = await other.socket.joinChat(created.matchId, ROOM, false, false);
      const heard = new Promise<string>((resolve) => {
        other.socket.onchannelmessage = (m) => {
          resolve(JSON.stringify(m.content));
        };
      });
      await host.socket.writeChatMessage(channel.id, { text: "salaam sab ko" });
      expect(await heard).toContain("salaam sab ko");
      await expect(
        host.socket.writeChatMessage(channel.id, { text: "too soon" }),
      ).rejects.toBeDefined();
      await expect(
        other.socket.writeChatMessage(otherChannel.id, { text: "see https://example.com" }),
      ).rejects.toBeDefined();
    }, 90_000);
  },
);
