import file from "../../../../content/catalog/v1.json";

export type Category =
  "cardBack" | "cardFace" | "table" | "emotePack" | "voPack" | "celebration" | "soundSet";
export type Rarity = "common" | "rare" | "epic" | "legendary";

export interface CatalogItem {
  readonly id: string;
  readonly name: string;
  readonly category: Category;
  readonly rarity: Rarity;
  readonly price: { readonly coins?: number; readonly gems?: number };
  /** `<group>/<name>` in Addressables (PROJECT_STRUCTURE 5). */
  readonly address: string;
  readonly starter?: boolean;
  readonly gameIds?: readonly string[];
  readonly season?: string;
}

export interface Catalog {
  readonly id: string;
  readonly version: number;
  readonly items: readonly CatalogItem[];
}

/** The launch catalog from content (GDD 12.11, 13.4); the art arrives with #66 to #68. */
export const BUNDLED_CATALOG: Catalog = {
  id: file.id,
  version: file.version,
  items: file.items as CatalogItem[],
};
