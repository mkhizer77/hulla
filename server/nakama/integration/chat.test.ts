import { Client } from "@heroiclabs/nakama-js";
import { OPCODES, type TableState } from "@hulla/protocol/generated";
import { afterAll, describe, expect, it } from "vitest";

import { BUNDLED_FILTERS } from "../src/content/filters";

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

      // The profanity filter masks in Standard rooms, in English and Roman Urdu (GDD 11.1).
      const masked = new Promise<string>((resolve) => {
        other.socket.onchannelmessage = (m) => {
          resolve(JSON.stringify(m.content));
        };
      });
      await new Promise((r) => setTimeout(r, 2_100));
      await host.socket.writeChatMessage(channel.id, { text: "ye to sh1t hai, bhenchod" });
      expect(await masked).toContain("ye to **** hai, ********");

      // The lists reload without a restart: a word uploaded through the admin RPC is masked at once.
      const reloaded = new Promise<string>((resolve) => {
        other.socket.onchannelmessage = (m) => {
          resolve(JSON.stringify(m.content));
        };
      });
      const url = `http://${NAKAMA_HOST ?? ""}:${NAKAMA_PORT}/v2/rpc/chat_filters_set?http_key=local-only-http-key&unwrap`;
      const upload = await fetch(url, {
        method: "POST",
        body: JSON.stringify({
          id: "test-lists",
          version: 99,
          languages: [{ lang: "en", words: ["shit", "zorblat"] }],
        }),
      });
      expect(await upload.json()).toMatchObject({ ok: true, version: 99 });
      await new Promise((r) => setTimeout(r, 2_100));
      await host.socket.writeChatMessage(channel.id, { text: "zorblat is new" });
      expect(await reloaded).toContain("******* is new");
      const restore = await fetch(url, {
        method: "POST",
        body: JSON.stringify(BUNDLED_FILTERS),
      });
      expect(await restore.json()).toMatchObject({ ok: true });

      // Mutes follow the account: the list is read back on any device.
      expect(
        await rpc<object>(host, "mute_set", { userId: other.session.user_id, muted: true }),
      ).toEqual({
        ok: true,
        mutes: [other.session.user_id],
      });
      expect(await rpc<object>(host, "mutes_get", {})).toEqual({
        ok: true,
        mutes: [other.session.user_id],
      });
    }, 90_000);

    it("a guest cannot free-text at a public table, and can once the account is linked", async () => {
      const suffix = `${String(Date.now())}g`;
      const guest = await scriptedClient("guest", suffix);
      const peer = await scriptedClient("peer", suffix);
      clients.push(guest, peer);
      const created = await rpc<Created>(guest, "table_create", {
        gameId: "thulla",
        mode: "public",
      });
      await guest.socket.joinMatch(created.matchId);
      await peer.socket.joinMatch(created.matchId);
      const channel = await guest.socket.joinChat(created.matchId, ROOM, false, false);
      await peer.socket.joinChat(created.matchId, ROOM, false, false);
      await expect(
        guest.socket.writeChatMessage(channel.id, { text: "hello strangers" }),
      ).rejects.toBeDefined();
      const api = new Client(SERVER_KEY, NAKAMA_HOST ?? "", NAKAMA_PORT, false);
      await api.linkEmail(guest.session, {
        email: `guest-${suffix}@example.com`,
        password: "a-long-test-password",
      });
      await guest.socket.writeChatMessage(channel.id, { text: "hello friends" });
    }, 60_000);
  },
);
