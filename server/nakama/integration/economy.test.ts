import { afterAll, describe, expect, it } from "vitest";

import {
  NAKAMA_HOST,
  NAKAMA_PORT,
  rpc,
  scriptedClient,
  setTableId,
  type ScriptedClient,
} from "./nakama-client";

import type { TableState } from "@hulla/protocol/generated";

async function adminGrant(body: object): Promise<Record<string, unknown>> {
  const url = `http://${NAKAMA_HOST ?? ""}:${NAKAMA_PORT}/v2/rpc/admin_grant?http_key=local-only-http-key&unwrap`;
  return (await (
    await fetch(url, { method: "POST", body: JSON.stringify(body) })
  ).json()) as Record<string, unknown>;
}

describe.skipIf(NAKAMA_HOST === undefined)(
  "the shop and the collection against Nakama (GDD 12.11, 13.4)",
  () => {
    const clients: ScriptedClient[] = [];
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("buys a common item with coins and an epic with gems, both once, and the equipped deck shows at the table", async () => {
      const suffix = String(Date.now());
      const buyer = await scriptedClient("shop", suffix);
      clients.push(buyer);
      const userId = buyer.session.user_id ?? "";
      expect(
        await adminGrant({
          userId,
          coins: 1500,
          gems: 400,
          reason: "test",
          sourceId: `grant-${suffix}`,
        }),
      ).toMatchObject({ ok: true });
      const catalog = await rpc<{
        ok: boolean;
        spotlight: string[];
        items: { id: string; owned: boolean }[];
      }>(buyer, "economy_catalog", {});
      expect(catalog.spotlight).toHaveLength(3);
      expect(catalog.items.find((i) => i.id === "back_classic_blue")?.owned).toBe(true);

      const common = await rpc<{
        ok: boolean;
        applied: boolean;
        balances: { coins: number; gems: number };
      }>(buyer, "economy_purchase", { itemId: "back_truck_art" });
      expect(common).toMatchObject({
        ok: true,
        applied: true,
        balances: { coins: 500, gems: 400 },
      });
      expect(
        await rpc<object>(buyer, "economy_purchase", { itemId: "back_truck_art" }),
      ).toMatchObject({ error: { code: "economy.item_owned" } });
      const epic = await rpc<{
        ok: boolean;
        applied: boolean;
        balances: { coins: number; gems: number };
      }>(buyer, "economy_purchase", { itemId: "back_jaali" });
      expect(epic).toMatchObject({ ok: true, applied: true, balances: { coins: 500, gems: 100 } });
      expect(
        await rpc<object>(buyer, "economy_purchase", { itemId: "table_basant_night" }),
      ).toMatchObject({ error: { code: "economy.insufficient_funds" } });
      const wallet = await rpc<{
        balances: { coins: number; gems: number };
        ledger: { reason: string }[];
      }>(buyer, "wallet_get", {});
      expect(wallet.balances).toEqual({ coins: 500, gems: 100 });
      expect(wallet.ledger.filter((l) => l.reason === "purchase")).toHaveLength(2);

      expect(await rpc<object>(buyer, "inventory_equip", { itemId: "back_jaali" })).toMatchObject({
        ok: true,
        equipped: { cardBack: "back_jaali" },
      });
      const created = await rpc<{ ok: boolean; matchId: string; tableId: string }>(
        buyer,
        "table_create",
        { gameId: "thulla", mode: "private" },
      );
      setTableId(created.tableId);
      await buyer.socket.joinMatch(created.matchId);
      const state = (await buyer.next((e) => e.type === "table_state")).payload as TableState;
      expect(state.seats[0]?.cosmetics).toMatchObject({
        cardBack: "back_jaali",
        table: "table_charpai",
        cardFace: "face_classic",
      });
    }, 60_000);
  },
);
