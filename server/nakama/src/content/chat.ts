import emotes from "../../../../content/emotes/v1.json";
import pack from "../../../../content/quick-chat/v1.en.json";

import { BUNDLED_CATALOG } from "./catalog";

/** What a table may say: phrase ids per quick-chat pack and the emote ids (bundled from content). */
export interface ChatContent {
  readonly defaultPack: string;
  isPhrase(packId: string, phraseId: string): boolean;
  isEmote(emoteId: string): boolean;
  /** The catalog emote pack an emote belongs to; only its owners may send it (GDD 11.3). */
  emotePack(emoteId: string): string | null;
  /** The packs everyone may send from when a seat's inventory could not be read: the starter packs. */
  readonly freeEmotePacks: readonly string[];
}

const phraseIds = new Set(pack.phrases.map((p) => p.id));
const emotePacks = new Map(emotes.emotes.map((e) => [e.id, e.pack]));

export const BUNDLED_CHAT: ChatContent = {
  defaultPack: pack.id,
  isPhrase: (packId, phraseId) => packId === pack.id && phraseIds.has(phraseId),
  isEmote: (emoteId) => emotePacks.has(emoteId),
  emotePack: (emoteId) => emotePacks.get(emoteId) ?? null,
  freeEmotePacks: BUNDLED_CATALOG.items
    .filter((i) => i.category === "emotePack" && i.starter === true)
    .map((i) => i.id),
};

export const QUICK_CHAT_PHRASES = pack.phrases;
export const EMOTES = emotes.emotes;
