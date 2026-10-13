import file from "../../../../content/products/v1.json";

export type ProductKind = "consumable" | "nonConsumable";
export type PassGrant = "premium" | "plus";

export interface ProductGrants {
  readonly gems?: number;
  readonly items?: readonly string[];
  readonly frames?: readonly string[];
  readonly entitlements?: readonly "removeAds"[];
  readonly pass?: PassGrant;
}

export interface Product {
  /** The product id on both stores. */
  readonly id: string;
  readonly name: string;
  readonly kind: ProductKind;
  /** The USD reference tier (GDD 13.4); the stores set local prices. */
  readonly usd: number;
  readonly grants: ProductGrants;
}

export interface Products {
  readonly id: string;
  readonly version: number;
  readonly products: readonly Product[];
}

/** The store products from content: Remove Ads, the gem packs, the pass and the starter bundle. */
export const BUNDLED_PRODUCTS: Products = {
  id: file.id,
  version: file.version,
  products: file.products as Product[],
};
