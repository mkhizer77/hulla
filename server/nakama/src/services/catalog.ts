import {
  ErrorCodes,
  ruleError,
  sha256,
  stateFromSeed,
  utf8Encode,
  Xoshiro128StarStar,
  type RuleError,
} from "@hulla/shared-ts";

import { BUNDLED_CATALOG, type Catalog, type CatalogItem, type Category } from "../content/catalog";

import { spend, type LedgerPorts } from "./ledger";
import { localWeek } from "./missions";

/**
 * The cosmetic catalog, the shop spotlight and each player's inventory (GDD 12.11, 13.4, 14.1):
 * common and rare items cost coins, epic and legendary gems, every purchase goes through the
 * ledger once, and equipping is global or per game. Nothing here changes a game outcome.
 */
export const CATALOG_COLLECTION = "content_catalog";
export const INVENTORY_COLLECTION = "inventory";
export const SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000";
export const SPOTLIGHT_SIZE = 3;

export const CATEGORIES: readonly Category[] = [
  "cardBack",
  "cardFace",
  "table",
  "emotePack",
  "voPack",
  "celebration",
  "soundSet",
];

export interface Inventory {
  /** Item id to the time it was bought or granted. */
  readonly owned: Readonly<Record<string, number>>;
  /** Category to the item equipped everywhere. */
  readonly equipped: Readonly<Record<string, string>>;
  /** Game id to category to item, when a game has its own choice. */
  readonly perGame: Readonly<Record<string, Readonly<Record<string, string>>>>;
  /** Item id to the end of a 24-hour trial from a rewarded ad (GDD 13.6). */
  readonly trials?: Readonly<Record<string, number>>;
  readonly updatedTs: number;
}

export const EMPTY_INVENTORY: Inventory = { owned: {}, equipped: {}, perGame: {}, updatedTs: 0 };

export function itemById(catalog: Catalog, id: string): CatalogItem | null {
  return catalog.items.find((i) => i.id === id) ?? null;
}

export function starters(catalog: Catalog): readonly CatalogItem[] {
  return catalog.items.filter((i) => i.starter === true);
}

/**
 * Everyone owns the starter items; the free path is never empty (GDD 13.1). With `now`, a running
 * trial counts as owned for equipping; without it (a purchase), only what was bought or granted.
 */
export function owns(inventory: Inventory, catalog: Catalog, id: string, now?: number): boolean {
  if (id in inventory.owned) return true;
  if (now !== undefined && (inventory.trials?.[id] ?? 0) > now) return true;
  const item = itemById(catalog, id);
  return item?.starter === true;
}

/** A 24-hour trial of a table theme from a rewarded ad; equip works until it ends. */
export function startTrial(
  inventory: Inventory,
  itemId: string,
  until: number,
  now: number,
): Inventory {
  const trials: Record<string, number> = {};
  for (const [id, end] of Object.entries(inventory.trials ?? {})) if (end > now) trials[id] = end;
  trials[itemId] = until;
  return { ...inventory, trials, updatedTs: now };
}

/** Three items a week, the same for everyone, never starters (GDD 13.3 "the shop rotates weekly"). */
export function spotlight(catalog: Catalog, now: number): readonly string[] {
  const week = localWeek(now, 0);
  const rng = Xoshiro128StarStar.fromState(stateFromSeed(sha256(utf8Encode(`spotlight:${week}`))));
  const pool = catalog.items.filter((i) => i.starter !== true).map((i) => i.id);
  for (let i = pool.length - 1; i > 0; i -= 1) {
    const j = rng.nextInt(i + 1);
    const a = pool[i];
    const b = pool[j];
    if (a !== undefined && b !== undefined) {
      pool[i] = b;
      pool[j] = a;
    }
  }
  return pool.slice(0, SPOTLIGHT_SIZE);
}

export type PurchaseResponse =
  | {
      readonly ok: true;
      readonly applied: boolean;
      readonly itemId: string;
      readonly balances: { coins: number; gems: number };
    }
  | { readonly ok: false; readonly error: RuleError };

/** Buys one item once: the ledger key is the player and the item, so a retry pays nothing twice. */
export function purchase(
  inventory: Inventory,
  catalog: Catalog,
  ledger: LedgerPorts,
  userId: string,
  itemId: string,
  now: number,
): { inventory: Inventory; response: PurchaseResponse } {
  const item = itemById(catalog, itemId);
  if (item === null) {
    return {
      inventory,
      response: { ok: false, error: ruleError(ErrorCodes.economy.itemUnknown, { itemId }) },
    };
  }
  if (owns(inventory, catalog, itemId)) {
    return {
      inventory,
      response: { ok: false, error: ruleError(ErrorCodes.economy.itemOwned, { itemId }) },
    };
  }
  const coins = item.price.coins ?? 0;
  const gems = item.price.gems ?? 0;
  const paid = spend(ledger, {
    userId,
    sourceId: `purchase:${itemId}`,
    reason: "purchase",
    coins,
    gems,
    metadata: { itemId, rarity: item.rarity },
  });
  if (!paid.ok) return { inventory, response: paid };
  const next: Inventory = {
    ...inventory,
    owned: { ...inventory.owned, [itemId]: now },
    updatedTs: now,
  };
  return {
    inventory: next,
    response: { ok: true, applied: paid.applied, itemId, balances: paid.balances },
  };
}

/** A grant from a level, the pass, an achievement or support: owned without a charge. */
export function grant(inventory: Inventory, itemId: string, now: number): Inventory {
  if (itemId in inventory.owned) return inventory;
  return { ...inventory, owned: { ...inventory.owned, [itemId]: now }, updatedTs: now };
}

export type EquipResponse =
  | {
      readonly ok: true;
      readonly equipped: Inventory["equipped"];
      readonly perGame: Inventory["perGame"];
    }
  | { readonly ok: false; readonly error: RuleError };

/** Equips an owned item for its category, everywhere or for one game (GDD 12.11). */
export function equip(
  inventory: Inventory,
  catalog: Catalog,
  itemId: string,
  gameId: string | null,
  now: number,
): { inventory: Inventory; response: EquipResponse } {
  const item = itemById(catalog, itemId);
  if (item === null) {
    return {
      inventory,
      response: { ok: false, error: ruleError(ErrorCodes.economy.itemUnknown, { itemId }) },
    };
  }
  if (!owns(inventory, catalog, itemId, now)) {
    return {
      inventory,
      response: {
        ok: false,
        error: ruleError(ErrorCodes.table.notAuthorised, { reason: "not_owned" }),
      },
    };
  }
  if (gameId !== null && item.gameIds !== undefined && !item.gameIds.includes(gameId)) {
    return {
      inventory,
      response: {
        ok: false,
        error: ruleError(ErrorCodes.table.invalidPayload, { reason: "game_id" }),
      },
    };
  }
  let next: Inventory;
  if (gameId === null) {
    next = {
      ...inventory,
      equipped: { ...inventory.equipped, [item.category]: itemId },
      updatedTs: now,
    };
  } else {
    const forGame = { ...(inventory.perGame[gameId] ?? {}), [item.category]: itemId };
    next = { ...inventory, perGame: { ...inventory.perGame, [gameId]: forGame }, updatedTs: now };
  }
  return {
    inventory: next,
    response: { ok: true, equipped: next.equipped, perGame: next.perGame },
  };
}

export type SeatCosmetics = Partial<Record<Category, string>>;

/** What a seat shows at a table of `gameId`: the game's choice, else the global one, else the starter. */
export function equippedFor(
  inventory: Inventory,
  catalog: Catalog,
  gameId: string,
  now?: number,
): SeatCosmetics {
  const out: SeatCosmetics = {};
  for (const category of CATEGORIES) {
    const chosen = inventory.perGame[gameId]?.[category] ?? inventory.equipped[category];
    const fallback = starters(catalog).find((i) => i.category === category)?.id;
    const id = chosen !== undefined && owns(inventory, catalog, chosen, now) ? chosen : fallback;
    if (id !== undefined) out[category] = id;
  }
  return out;
}

export interface CatalogView {
  readonly id: string;
  readonly version: number;
  readonly spotlight: readonly string[];
  readonly items: readonly (CatalogItem & {
    readonly owned: boolean;
    readonly equipped: boolean;
    /** The end of a running trial from a rewarded ad. */
    readonly trialUntil?: number;
  })[];
  readonly equipped: Inventory["equipped"];
  readonly perGame: Inventory["perGame"];
}

export function catalogView(inventory: Inventory, catalog: Catalog, now: number): CatalogView {
  const global = equippedFor(inventory, catalog, "", now);
  return {
    id: catalog.id,
    version: catalog.version,
    spotlight: spotlight(catalog, now),
    items: catalog.items.map((item) => {
      const trialUntil = inventory.trials?.[item.id];
      return {
        ...item,
        owned: owns(inventory, catalog, item.id),
        equipped: global[item.category] === item.id,
        ...(trialUntil !== undefined && trialUntil > now ? { trialUntil } : {}),
      };
    }),
    equipped: inventory.equipped,
    perGame: inventory.perGame,
  };
}

export function isCatalog(value: unknown): value is Catalog {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  const items = v["items"];
  return (
    typeof v["id"] === "string" &&
    typeof v["version"] === "number" &&
    Array.isArray(items) &&
    items.length > 0 &&
    items.every((i: unknown) => {
      if (typeof i !== "object" || i === null) return false;
      const d = i as Record<string, unknown>;
      return (
        typeof d["id"] === "string" &&
        CATEGORIES.includes(d["category"] as Category) &&
        typeof d["rarity"] === "string" &&
        typeof d["price"] === "object" &&
        typeof d["address"] === "string"
      );
    })
  );
}

export interface CatalogStore {
  catalog(): Catalog;
  writeCatalog(catalog: Catalog): void;
  inventory(userId: string): Inventory;
  writeInventory(userId: string, inventory: Inventory): void;
}

function isInventory(value: unknown): value is Inventory {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v["owned"] === "object" && typeof v["equipped"] === "object";
}

export function nkCatalogStore(nk: nkruntime.Nakama): CatalogStore {
  return {
    catalog() {
      try {
        const objects = nk.storageRead([
          { collection: CATALOG_COLLECTION, key: "current", userId: SYSTEM_USER_ID },
        ]);
        const value: unknown = objects[0]?.value;
        return isCatalog(value) ? value : BUNDLED_CATALOG;
      } catch {
        return BUNDLED_CATALOG;
      }
    },
    writeCatalog(catalog) {
      nk.storageWrite([
        {
          collection: CATALOG_COLLECTION,
          key: "current",
          userId: SYSTEM_USER_ID,
          value: { id: catalog.id, version: catalog.version, items: catalog.items },
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
    inventory(userId) {
      const objects = nk.storageRead([{ collection: INVENTORY_COLLECTION, key: "state", userId }]);
      const value: unknown = objects[0]?.value;
      return isInventory(value) ? { ...EMPTY_INVENTORY, ...value } : EMPTY_INVENTORY;
    },
    writeInventory(userId, inventory) {
      nk.storageWrite([
        {
          collection: INVENTORY_COLLECTION,
          key: "state",
          userId,
          value: inventory,
          permissionRead: 1,
          permissionWrite: 0,
        },
      ]);
    },
  };
}
