import classicPagat from "../../../../content/presets/thulla/classicPagat.json";
import family from "../../../../content/presets/thulla/family.json";
import hardcore from "../../../../content/presets/thulla/hardcore.json";
import standard from "../../../../content/presets/thulla/standard.json";

import type { RulesSelection } from "@hulla/protocol/generated";

/** A preset as `presets_get` serves it: the content file without its schema pointer. */
export interface Preset {
  readonly id: NonNullable<RulesSelection["preset"]>;
  readonly gameId: RulesSelection["gameId"];
  readonly name: string;
  readonly description: string;
  readonly order: number;
  readonly overrides: Record<string, unknown>;
  readonly settings: NonNullable<RulesSelection["settings"]>;
}

const files = [standard, family, hardcore, classicPagat] as const;

/** Bundled from `content/presets` at build time; `pnpm content:validate` keeps them honest. */
export const PRESETS: readonly Preset[] = [...files]
  .map((file) => ({
    id: file.id as Preset["id"],
    gameId: file.gameId as Preset["gameId"],
    name: file.name,
    description: file.description,
    order: file.order,
    overrides: file.overrides,
    settings: file.settings as Preset["settings"],
  }))
  .sort((a, b) => a.order - b.order);

export function presetsFor(gameId: string): readonly Preset[] {
  return PRESETS.filter((p) => p.gameId === gameId);
}
