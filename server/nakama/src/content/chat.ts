import emotes from "../../../../content/emotes/v1.json";
import pack from "../../../../content/quick-chat/v1.en.json";

/** What a table may say: phrase ids per quick-chat pack and the emote ids (bundled from content). */
export interface ChatContent {
  readonly defaultPack: string;
  isPhrase(packId: string, phraseId: string): boolean;
  isEmote(emoteId: string): boolean;
}

const phraseIds = new Set(pack.phrases.map((p) => p.id));
const emoteIds = new Set(emotes.emotes.map((e) => e.id));

export const BUNDLED_CHAT: ChatContent = {
  defaultPack: pack.id,
  isPhrase: (packId, phraseId) => packId === pack.id && phraseIds.has(phraseId),
  isEmote: (emoteId) => emoteIds.has(emoteId),
};

export const QUICK_CHAT_PHRASES = pack.phrases;
export const EMOTES = emotes.emotes;
