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

import type { Profile } from "@hulla/protocol/generated";

interface ProfileResponse {
  readonly ok: boolean;
  readonly profile?: Profile;
  readonly error?: { code: string };
}

interface Created {
  readonly ok: boolean;
  readonly matchId: string;
  readonly tableId: string;
}

describe.skipIf(NAKAMA_HOST === undefined)(
  "account linking, guest merge and one table session (GDD 10.2)",
  () => {
    const clients: ScriptedClient[] = [];
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("a linked second device restores the profile, a guest merges into the linked account, and a second table session is refused", async () => {
      const suffix = String(Date.now());
      const linked = await scriptedClient("linked", suffix);
      const guest = await scriptedClient("guest", suffix);
      clients.push(linked, guest);
      const api = new Client(SERVER_KEY, NAKAMA_HOST ?? "", NAKAMA_PORT, false);

      // Linking a second device to the account and signing in from it lands on the same profile.
      const own = (await rpc<ProfileResponse>(linked, "profile_get", {})).profile;
      if (own === undefined) throw new Error("no profile");
      await api.linkDevice(linked.session, { id: `hulla-it-second-${suffix}` });
      const secondDevice = await api.authenticateDevice(`hulla-it-second-${suffix}`, false);
      expect(secondDevice.user_id).toBe(linked.session.user_id);
      const restored = await api.rpc(secondDevice, "profile_get", {});
      expect((restored.payload as ProfileResponse).profile?.friendCode).toBe(own.friendCode);

      // The guest earned a name and a flag; after the merge the linked account carries them and the guest is gone.
      await rpc<ProfileResponse>(guest, "profile_update", { displayName: "Noor", flag: "BD" });
      const prepared = await rpc<{ ok: boolean; code: string }>(guest, "account_merge_prepare", {});
      expect(prepared.ok).toBe(true);
      const merged = await rpc<{ ok: boolean; mergedFrom: string; profile: Profile }>(
        linked,
        "account_merge",
        { code: prepared.code },
      );
      expect(merged).toMatchObject({
        ok: true,
        mergedFrom: guest.session.user_id,
        profile: { displayName: "Noor", flag: "BD", friendCode: own.friendCode },
      });
      expect(
        await rpc<{ ok: boolean }>(linked, "account_merge", { code: prepared.code }),
      ).toMatchObject({ ok: false, error: { code: "auth.merge_code_invalid" } });
      await expect(api.authenticateDevice(`hulla-it-guest-${suffix}`, false)).rejects.toBeDefined();

      // One active table session: a second device joining the same seat is refused until the first drops.
      const created = await rpc<Created>(linked, "table_create", {
        gameId: "thulla",
        mode: "private",
      });
      setTableId(created.tableId);
      await linked.socket.joinMatch(created.matchId);
      await linked.next((e) => e.type === "table_state");
      const twin = api.createSocket(false, false);
      await twin.connect(secondDevice, false);
      await expect(twin.joinMatch(created.matchId)).rejects.toBeDefined();
      linked.socket.disconnect(false);
      await new Promise((resolve) => setTimeout(resolve, 500));
      const joined = await twin.joinMatch(created.matchId);
      expect(joined.match_id).toBe(created.matchId);
      twin.disconnect(false);
    }, 60_000);
  },
);
