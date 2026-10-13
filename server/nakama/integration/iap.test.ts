import { afterAll, describe, expect, it } from "vitest";

import { NAKAMA_HOST, rpc, scriptedClient, type ScriptedClient } from "./nakama-client";

interface Products {
  readonly ok: boolean;
  readonly products: readonly { id: string; usd: number }[];
  readonly entitlements: { removeAds: boolean };
}

describe.skipIf(NAKAMA_HOST === undefined)(
  "store products and receipts against Nakama (GDD 13.4)",
  () => {
    const clients: ScriptedClient[] = [];
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("lists the products, refuses a receipt without store credentials, and restores nothing for a new account", async () => {
      const buyer = await scriptedClient("iap", String(Date.now()));
      clients.push(buyer);
      const products = await rpc<Products>(buyer, "economy_products", {});
      expect(products.ok).toBe(true);
      expect(products.products.map((p) => p.id)).toContain("remove_ads");
      expect(products.products).toHaveLength(9);
      expect(products.entitlements.removeAds).toBe(false);
      const refused = await rpc<{ ok: boolean; error?: { code: string } }>(buyer, "iap_verify", {
        store: "google",
        receipt: JSON.stringify({ json: "{}", signature: "not-a-signature" }),
      });
      // The local and CI boxes hold no store credentials (runbook step 3).
      expect(refused).toMatchObject({ ok: false, error: { code: "economy.store_unavailable" } });
      expect(await rpc<object>(buyer, "iap_restore", {})).toMatchObject({
        ok: true,
        purchases: [],
        entitlements: { removeAds: false },
      });
      expect(await rpc<object>(buyer, "wallet_get", {})).toMatchObject({
        ok: true,
        entitlements: { removeAds: false, frames: [] },
      });
    });
  },
);
