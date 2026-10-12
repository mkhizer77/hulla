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

interface ProfileResponse {
  readonly ok: boolean;
  readonly profile?: Profile;
}

interface Overview {
  readonly ok: boolean;
  readonly favourites: string[];
  readonly recent: { userId: string; friend: boolean; favourite: boolean }[];
}

describe.skipIf(NAKAMA_HOST === undefined)(
  "friends by code and favourites against Nakama (GDD 10.3)",
  () => {
    const clients: ScriptedClient[] = [];
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("a request by friend code is accepted through the friend API, then pinned as a favourite", async () => {
      const suffix = String(Date.now());
      const ali = await scriptedClient("fa", suffix);
      const sara = await scriptedClient("fs", suffix);
      clients.push(ali, sara);
      const aliId = ali.session.user_id ?? "";
      const saraId = sara.session.user_id ?? "";
      const api = new Client(SERVER_KEY, NAKAMA_HOST ?? "", NAKAMA_PORT, false);
      const saraCode =
        (await rpc<ProfileResponse>(sara, "profile_get", {})).profile?.friendCode ?? "";
      expect(saraCode).toHaveLength(8);

      expect(
        await rpc<{ ok: boolean }>(ali, "friend_add_by_code", {
          friendCode: saraCode.toLowerCase(),
        }),
      ).toEqual({ ok: true, userId: saraId });
      const incoming = await api.listFriends(sara.session, 2, 10);
      expect(incoming.friends?.map((f) => f.user?.id)).toContain(aliId);
      expect(
        await rpc<{ ok: boolean }>(ali, "favourite_set", {
          userId: saraId,
          pinned: true,
        }),
      ).toMatchObject({ ok: false, error: { code: "social.not_friends" } });

      await api.addFriends(sara.session, [aliId]);
      const mutual = await api.listFriends(ali.session, 0, 10);
      expect(mutual.friends?.map((f) => f.user?.id)).toContain(saraId);
      expect(
        await rpc<{ ok: boolean }>(ali, "favourite_set", {
          userId: saraId,
          pinned: true,
        }),
      ).toEqual({ ok: true, favourites: [saraId] });
      const overview = await rpc<Overview>(ali, "social_overview", {});
      expect(overview.favourites).toEqual([saraId]);
      expect(
        await rpc<{ ok: boolean }>(ali, "friend_add_by_code", {
          friendCode:
            (await rpc<ProfileResponse>(ali, "profile_get", {})).profile?.friendCode ?? "",
        }),
      ).toMatchObject({ ok: false, error: { code: "social.self" } });
    }, 60_000);
  },
);
