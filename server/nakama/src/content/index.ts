import achievements from "../../../../content/achievements/v1.json?raw";
import avatars from "../../../../content/avatars/v1.json?raw";
import catalog from "../../../../content/catalog/v1.json?raw";
import chatFilters from "../../../../content/chat-filters/v1.json?raw";
import dailyRewards from "../../../../content/daily-rewards/v1.json?raw";
import emotes from "../../../../content/emotes/v1.json?raw";
import manifest from "../../../../content/manifest.json";
import missions from "../../../../content/missions/v1.json?raw";
import classicPagat from "../../../../content/presets/thulla/classicPagat.json?raw";
import family from "../../../../content/presets/thulla/family.json?raw";
import hardcore from "../../../../content/presets/thulla/hardcore.json?raw";
import standard from "../../../../content/presets/thulla/standard.json?raw";
import products from "../../../../content/products/v1.json?raw";
import quickChat from "../../../../content/quick-chat/v1.en.json?raw";
import ranked from "../../../../content/ranked/v1.json?raw";
import basant from "../../../../content/seasons/basant-2026.json?raw";
import stringsEn from "../../../../content/strings/en.json?raw";

/**
 * Every content file the server bundles, by its manifest path, as the exact bytes the manifest
 * hashes, so `content_get` can serve the base content without a CDN and a client can check each
 * file against its hash (ADR-0011).
 */
export const CONTENT_TEXT: Readonly<Record<string, string>> = {
  "achievements/v1.json": achievements,
  "avatars/v1.json": avatars,
  "catalog/v1.json": catalog,
  "chat-filters/v1.json": chatFilters,
  "daily-rewards/v1.json": dailyRewards,
  "emotes/v1.json": emotes,
  "missions/v1.json": missions,
  "presets/thulla/classicPagat.json": classicPagat,
  "presets/thulla/family.json": family,
  "presets/thulla/hardcore.json": hardcore,
  "presets/thulla/standard.json": standard,
  "products/v1.json": products,
  "quick-chat/v1.en.json": quickChat,
  "ranked/v1.json": ranked,
  "seasons/basant-2026.json": basant,
  "strings/en.json": stringsEn,
};

/** The same files parsed, for checks that read fields. */
export const CONTENT_FILES: Readonly<Record<string, object>> = parseAll(CONTENT_TEXT);

function parseAll(texts: Readonly<Record<string, string>>): Record<string, object> {
  const parsed: Record<string, object> = {};
  for (const path of Object.keys(texts)) {
    parsed[path] = JSON.parse(texts[path] ?? "{}") as object;
  }
  return parsed;
}

export interface ContentManifest {
  readonly id: string;
  readonly version: number;
  readonly digest: string;
  readonly files: readonly {
    readonly path: string;
    readonly type: string;
    readonly id: string;
    readonly version: number;
    readonly sha256: string;
    readonly bytes: number;
  }[];
}

export const CONTENT_MANIFEST: ContentManifest = {
  id: manifest.id,
  version: manifest.version,
  digest: manifest.digest,
  files: manifest.files,
};
