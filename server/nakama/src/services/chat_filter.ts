import { BUNDLED_FILTERS, type FilterLists } from "../content/filters";

/**
 * The profanity filter (GDD 11.1): English, Roman Urdu, Roman Hindi, Urdu script, Devanagari and
 * Bengali, matched after the usual spelling evasions are undone. Standard masks the word, Family
 * blocks the message. The lists live in storage so the owner can replace them without a restart.
 */
export const FILTERS_COLLECTION = "chat_filters";
export const FILTERS_KEY = "lists";
export const SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000";

export type Strictness = "standard" | "family";

const LOOKALIKES: Record<string, string> = {
  "0": "o",
  "1": "i",
  "3": "e",
  "4": "a",
  "5": "s",
  "7": "t",
  "8": "b",
  "@": "a",
  $: "s",
  "!": "i",
  "|": "i",
};

const SEPARATORS = /[.\-_*'`"~^+=:;,()[\]{}<>/\\]/g;

const EDGE_PUNCTUATION = ".,?;:'\"()[]{}<>-_*~^+=/\\%&#`";
const EDGE_LOOKALIKES = "!|@$";

function trimEdges(input: string, set: string): string {
  let start = 0;
  let end = input.length;
  while (start < end && set.includes(input.charAt(start))) start += 1;
  while (end > start && set.includes(input.charAt(end - 1))) end -= 1;
  return input.slice(start, end);
}

function fold(input: string): string {
  let out = "";
  let previous = "";
  for (const ch of input.replace(SEPARATORS, "")) {
    const mapped = LOOKALIKES[ch] ?? ch;
    if (mapped === previous) continue;
    out += mapped;
    previous = mapped;
  }
  return out;
}

/**
 * Lower case, plain punctuation trimmed from the ends, lookalikes undone, separators dropped and
 * runs of a letter collapsed to one: "S.H.1.T" and "sh!t" both become "shit".
 */
export function normaliseWord(input: string): string {
  return fold(trimEdges(input.toLowerCase(), EDGE_PUNCTUATION));
}

/**
 * The forms a token is checked under: as written, and with trailing symbols that double as
 * letters dropped, so "zorblat!" is "zorblat" while "a$$" still reads as "ass".
 */
export function normaliseForms(input: string): string[] {
  const trimmed = trimEdges(input.toLowerCase(), EDGE_PUNCTUATION);
  const primary = fold(trimmed);
  let end = trimmed.length;
  while (end > 0 && EDGE_LOOKALIKES.includes(trimmed.charAt(end - 1))) end -= 1;
  const secondary = fold(trimmed.slice(0, end));
  return secondary === primary || secondary === "" ? [primary] : [primary, secondary];
}

export interface Matcher {
  readonly id: string;
  readonly version: number;
  /** Normalised single words. */
  readonly words: ReadonlySet<string>;
  /** Normalised phrases of two or more words, joined by one space. */
  readonly phrases: readonly string[];
}

export function buildMatcher(lists: FilterLists): Matcher {
  const words = new Set<string>();
  const phrases: string[] = [];
  for (const language of lists.languages) {
    for (const raw of language.words) {
      const parts = raw
        .trim()
        .split(/\s+/)
        .map(normaliseWord)
        .filter((p) => p !== "");
      if (parts.length === 0) continue;
      if (parts.length === 1) words.add(parts[0] ?? "");
      else phrases.push(parts.join(" "));
    }
  }
  return { id: lists.id, version: lists.version, words, phrases };
}

export interface FilterResult {
  readonly text: string;
  readonly hits: number;
}

function mask(token: string): string {
  return "*".repeat(Array.from(token).length);
}

/** Masks every listed word; the caller blocks instead when the table is Family. */
export function filterText(matcher: Matcher, text: string): FilterResult {
  const tokens = text.split(/(\s+)/);
  const normalised = tokens.map((t) => (/^\s+$/.test(t) || t === "" ? null : normaliseWord(t)));
  const flagged = new Set<number>();
  tokens.forEach((t, i) => {
    if (normalised[i] === null) return;
    if (normaliseForms(t).some((form) => form !== "" && matcher.words.has(form))) flagged.add(i);
  });
  const wordIndexes = normalised.flatMap((n, i) => (n === null ? [] : [i]));
  for (const phrase of matcher.phrases) {
    const parts = phrase.split(" ");
    for (let start = 0; start + parts.length <= wordIndexes.length; start += 1) {
      const window = wordIndexes.slice(start, start + parts.length);
      if (window.every((index, k) => normalised[index] === parts[k])) {
        for (const index of window) flagged.add(index);
      }
    }
  }
  const out = tokens.map((t, i) => (flagged.has(i) ? mask(t) : t)).join("");
  return { text: out, hits: flagged.size };
}

export interface FilterStore {
  read(): FilterLists;
  write(lists: FilterLists): void;
}

export function isFilterLists(value: unknown): value is FilterLists {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  if (typeof v["id"] !== "string" || typeof v["version"] !== "number") return false;
  const languages = v["languages"];
  if (!Array.isArray(languages) || languages.length === 0) return false;
  return languages.every((l: unknown) => {
    if (typeof l !== "object" || l === null) return false;
    const lang = l as Record<string, unknown>;
    const words = lang["words"];
    return (
      typeof lang["lang"] === "string" &&
      Array.isArray(words) &&
      words.every((w: unknown) => typeof w === "string" && w.length >= 2 && w.length <= 40)
    );
  });
}

/** Storage first, the bundled lists otherwise; `chat_filters_set` replaces them live. */
export function nkFilterStore(nk: nkruntime.Nakama): FilterStore {
  return {
    read() {
      try {
        const objects = nk.storageRead([
          { collection: FILTERS_COLLECTION, key: FILTERS_KEY, userId: SYSTEM_USER_ID },
        ]);
        const value: unknown = objects[0]?.value;
        return isFilterLists(value) ? value : BUNDLED_FILTERS;
      } catch {
        return BUNDLED_FILTERS;
      }
    },
    write(lists) {
      nk.storageWrite([
        {
          collection: FILTERS_COLLECTION,
          key: FILTERS_KEY,
          userId: SYSTEM_USER_ID,
          value: { id: lists.id, version: lists.version, languages: lists.languages },
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
  };
}

/** A guest is a device-only account: nothing linked that survives the phone (GDD 9.4). */
export function isGuestAccount(account: {
  readonly email?: string;
  readonly customId?: string;
  readonly user: {
    readonly appleId?: string;
    readonly googleId?: string;
    readonly facebookId?: string;
  };
}): boolean {
  const linked = [
    account.email,
    account.customId,
    account.user.appleId,
    account.user.googleId,
    account.user.facebookId,
  ];
  return linked.every((id) => id === undefined || id === "");
}

/** The room a channel id names: Nakama writes `mode.subject.subcontext.label`. */
export function roomOfChannel(channelId: string): string | null {
  const parts = channelId.split(".");
  if (parts.length < 4 || parts[0] !== "2") return null;
  const label = parts.slice(3).join(".");
  return label === "" ? null : label;
}
