import { describe, expect, it } from "vitest";

import { BUNDLED_CATALOG } from "../content/catalog";
import { thullaMatchInit, thullaMatchJoin, thullaMatchJoinAttempt } from "../games/thulla/match";
import { rpcAdminGrant } from "../rpc/admin";
import {
  rpcCatalogSet,
  rpcEconomyCatalog,
  rpcEconomyPurchase,
  rpcInventoryEquip,
} from "../rpc/shop";
import {
  catalogView,
  EMPTY_INVENTORY,
  equip,
  equippedFor,
  ownedEmotePacks,
  grant,
  owns,
  purchase,
  spotlight,
  type Inventory,
} from "../services/catalog";
import { credit, nkLedgerPorts } from "../services/ledger";

import { fakeDispatcher, nkPresence } from "./fake-match";
import { fakeLogger, memoryNk } from "./fake-nk";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const ctxFor = (userId: string): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env: {} }) as unknown as nkruntime.Context;
const SERVER_KEY = { env: {} } as unknown as nkruntime.Context;
const T0 = Date.UTC(2026, 9, 13, 12);
const WEEK = 7 * 24 * 60 * 60 * 1000;

describe("the cosmetic catalog (GDD 12.11, 13.4)", () => {
  it("lists the emote packs a player may send from: the starter pack and what they bought", () => {
    expect(ownedEmotePacks(EMPTY_INVENTORY, BUNDLED_CATALOG, 0)).toEqual(["emotes_launch"]);
    const bought = { ...EMPTY_INVENTORY, owned: { emotes_shaadi: 1 } };
    expect(ownedEmotePacks(bought, BUNDLED_CATALOG, 0)).toEqual(["emotes_launch", "emotes_shaadi"]);
  });

  it("prices common and rare items in coins and epic and legendary in gems, within the GDD bands", () => {
    const items = BUNDLED_CATALOG.items;
    expect(items.length).toBeGreaterThanOrEqual(30);
    expect(new Set(items.map((i) => i.id)).size).toBe(items.length);
    for (const item of items) {
      if (item.starter === true || item.earnOnly === true) {
        expect(item.price).toEqual({});
        continue;
      }
      if (item.rarity === "common" || item.rarity === "rare") {
        expect(item.price.gems, item.id).toBeUndefined();
        expect(item.price.coins, item.id).toBeGreaterThan(0);
      } else {
        expect(item.price.coins, item.id).toBeUndefined();
        expect(item.price.gems, item.id).toBeGreaterThan(0);
      }
      if (item.category === "cardBack" && item.price.gems !== undefined)
        expect(item.price.gems).toBeLessThanOrEqual(600);
      if (item.category === "table" && item.price.gems !== undefined)
        expect(item.price.gems).toBeLessThanOrEqual(1200);
    }
    for (const category of [
      "cardBack",
      "cardFace",
      "table",
      "emotePack",
      "voPack",
      "celebration",
      "soundSet",
    ]) {
      expect(
        items.some((i) => i.category === category && i.starter === true),
        category,
      ).toBe(true);
      expect(
        items.some((i) => i.category === category && i.price.coins !== undefined) ||
          category === "voPack",
        category,
      ).toBe(true);
    }
  });

  it("buys once per item through the ledger, refuses owned and unknown items, and equips", () => {
    const m = memoryNk();
    const ledger = nkLedgerPorts(m.nk);
    credit(ledger, { userId: "u1", sourceId: "seed", reason: "test", coins: 1200, gems: 400 });
    let inv: Inventory = EMPTY_INVENTORY;
    expect(owns(inv, BUNDLED_CATALOG, "back_classic_blue")).toBe(true);
    const common = purchase(inv, BUNDLED_CATALOG, ledger, "u1", "back_truck_art", T0);
    expect(common.response).toMatchObject({
      ok: true,
      applied: true,
      balances: { coins: 200, gems: 400 },
    });
    inv = common.inventory;
    expect(
      purchase(inv, BUNDLED_CATALOG, ledger, "u1", "back_truck_art", T0).response,
    ).toMatchObject({ error: { code: "economy.item_owned" } });
    const retry = purchase(
      { ...inv, owned: {} },
      BUNDLED_CATALOG,
      ledger,
      "u1",
      "back_truck_art",
      T0,
    );
    expect(retry.response).toMatchObject({ ok: true, applied: false, balances: { coins: 200 } });
    const epic = purchase(inv, BUNDLED_CATALOG, ledger, "u1", "back_jaali", T0);
    expect(epic.response).toMatchObject({
      ok: true,
      applied: true,
      balances: { coins: 200, gems: 100 },
    });
    inv = epic.inventory;
    expect(purchase(inv, BUNDLED_CATALOG, ledger, "u1", "back_monsoon", T0).response).toMatchObject(
      { error: { code: "economy.insufficient_funds" } },
    );
    expect(purchase(inv, BUNDLED_CATALOG, ledger, "u1", "nope", T0).response).toMatchObject({
      error: { code: "economy.item_unknown" },
    });
    expect(equip(inv, BUNDLED_CATALOG, "back_monsoon", null, T0).response).toMatchObject({
      error: { code: "table.not_authorised" },
    });
    const global = equip(inv, BUNDLED_CATALOG, "back_jaali", null, T0);
    expect(global.response).toMatchObject({ ok: true, equipped: { cardBack: "back_jaali" } });
    const perGame = equip(global.inventory, BUNDLED_CATALOG, "back_truck_art", "thulla", T0);
    expect(perGame.response).toMatchObject({
      ok: true,
      perGame: { thulla: { cardBack: "back_truck_art" } },
    });
    expect(equippedFor(perGame.inventory, BUNDLED_CATALOG, "thulla")).toMatchObject({
      cardBack: "back_truck_art",
      table: "table_charpai",
      cardFace: "face_classic",
    });
    expect(equippedFor(perGame.inventory, BUNDLED_CATALOG, "rung")).toMatchObject({
      cardBack: "back_jaali",
    });
    const granted = grant(perGame.inventory, "table_marble", T0);
    expect(owns(granted, BUNDLED_CATALOG, "table_marble")).toBe(true);
    expect(grant(granted, "table_marble", T0 + 1)).toBe(granted);
    const view = catalogView(granted, BUNDLED_CATALOG, T0);
    expect(view.items.find((i) => i.id === "back_jaali")).toMatchObject({
      owned: true,
      equipped: true,
    });
    expect(view.items.find((i) => i.id === "back_truck_art")).toMatchObject({
      owned: true,
      equipped: false,
    });
    expect(view.spotlight).toHaveLength(3);
  });

  it("rotates the spotlight weekly, the same for everyone, never a starter", () => {
    const a = spotlight(BUNDLED_CATALOG, T0);
    expect(spotlight(BUNDLED_CATALOG, T0 + 60_000)).toEqual(a);
    expect(spotlight(BUNDLED_CATALOG, T0 + WEEK)).not.toEqual(a);
    for (const id of a)
      expect(BUNDLED_CATALOG.items.find((i) => i.id === id)?.starter).toBeUndefined();
  });

  it("runs through the RPCs with a support grant, and the equipped items reach the seat", () => {
    const m = memoryNk();
    const log = fakeLogger();
    expect(
      parse(
        rpcEconomyPurchase(ctxFor("u1"), log, m.nk, JSON.stringify({ itemId: "back_truck_art" })),
      ),
    ).toMatchObject({ error: { code: "economy.insufficient_funds" } });
    expect(parse(rpcAdminGrant(ctxFor("u1"), log, m.nk, "{}"))).toMatchObject({ ok: false });
    expect(
      parse(
        rpcAdminGrant(
          SERVER_KEY,
          log,
          m.nk,
          JSON.stringify({
            userId: "u1",
            coins: 1000,
            gems: 300,
            reason: "support",
            sourceId: "ticket-1",
          }),
        ),
      ),
    ).toMatchObject({ ok: true, balances: { coins: 1000, gems: 300 } });
    expect(
      parse(
        rpcAdminGrant(
          SERVER_KEY,
          log,
          m.nk,
          JSON.stringify({ userId: "u1", coins: 1000, reason: "support", sourceId: "ticket-1" }),
        ),
      ),
    ).toMatchObject({ ok: true, applied: false });
    expect(
      parse(
        rpcEconomyPurchase(ctxFor("u1"), log, m.nk, JSON.stringify({ itemId: "back_truck_art" })),
      ),
    ).toMatchObject({ ok: true, applied: true });
    expect(
      parse(rpcEconomyPurchase(ctxFor("u1"), log, m.nk, JSON.stringify({ itemId: "back_jaali" }))),
    ).toMatchObject({ ok: true, applied: true, balances: { coins: 0, gems: 0 } });
    expect(
      parse(rpcInventoryEquip(ctxFor("u1"), log, m.nk, JSON.stringify({ itemId: "back_jaali" }))),
    ).toMatchObject({ ok: true, equipped: { cardBack: "back_jaali" } });
    const view = parse(rpcEconomyCatalog(ctxFor("u1"), log, m.nk));
    expect(
      (view["items"] as Json[]).filter((i) => i["owned"] === true).length,
    ).toBeGreaterThanOrEqual(8);
    expect(parse(rpcCatalogSet(ctxFor("u1"), log, m.nk, "{}"))).toMatchObject({ ok: false });
    expect(
      parse(
        rpcCatalogSet(SERVER_KEY, log, m.nk, JSON.stringify({ ...BUNDLED_CATALOG, version: 2 })),
      ),
    ).toEqual({ ok: true, id: "catalog-v1", version: 2 });

    const ctx = { matchId: "m1.n1", env: {} } as unknown as nkruntime.Context;
    const d = fakeDispatcher();
    const init = thullaMatchInit(ctx, log, m.nk, {
      code: "ABCDEF",
      gameId: "thulla",
      mode: "private",
    });
    let state = init.state;
    const presence = { ...nkPresence(0), userId: "u1" };
    state =
      thullaMatchJoinAttempt(ctx, log, m.nk, d.dispatcher, 1, state, presence, {})?.state ?? state;
    state = thullaMatchJoin(ctx, log, m.nk, d.dispatcher, 1, state, [presence])?.state ?? state;
    expect(state.seats[0]?.cosmetics).toMatchObject({
      cardBack: "back_jaali",
      table: "table_charpai",
    });
    const tableState = d.broadcasts.find((b) => b.envelope.type === "table_state");
    expect(
      (tableState?.envelope.payload as { seats: { cosmetics: unknown }[] }).seats[0]?.cosmetics,
    ).toMatchObject({ cardBack: "back_jaali" });
  });
});
