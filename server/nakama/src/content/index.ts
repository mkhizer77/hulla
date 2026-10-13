import achievements from "../../../../content/achievements/v1.json";
import avatars from "../../../../content/avatars/v1.json";
import catalog from "../../../../content/catalog/v1.json";
import chatFilters from "../../../../content/chat-filters/v1.json";
import dailyRewards from "../../../../content/daily-rewards/v1.json";
import emotes from "../../../../content/emotes/v1.json";
import manifest from "../../../../content/manifest.json";
import missions from "../../../../content/missions/v1.json";
import classicPagat from "../../../../content/presets/thulla/classicPagat.json";
import family from "../../../../content/presets/thulla/family.json";
import hardcore from "../../../../content/presets/thulla/hardcore.json";
import standard from "../../../../content/presets/thulla/standard.json";
import quickChat from "../../../../content/quick-chat/v1.en.json";
import basant from "../../../../content/seasons/basant-2026.json";

/**
 * Every content file the server bundles, by its manifest path, so `content_get` can serve the
 * base content without a CDN (ADR-0011). The manifest lists the same files with their hashes.
 */
export const CONTENT_FILES: Readonly<Record<string, object>> = {
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
  "quick-chat/v1.en.json": quickChat,
  "seasons/basant-2026.json": basant,
};

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
