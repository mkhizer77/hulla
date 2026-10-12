import lists from "../../../../content/chat-filters/v1.json";

/** The bundled filter lists; a newer set in storage replaces them without a restart (GDD 11.1). */
export interface FilterLanguage {
  readonly lang: string;
  readonly words: readonly string[];
}

export interface FilterLists {
  readonly id: string;
  readonly version: number;
  readonly languages: readonly FilterLanguage[];
}

export const BUNDLED_FILTERS: FilterLists = {
  id: lists.id,
  version: lists.version,
  languages: lists.languages,
};
