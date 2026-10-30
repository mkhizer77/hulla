import { CUSTOM_ID_PREFIX, normalisePhone } from "./phone_otp";

/**
 * People you may know (owner decision, 30 Oct): players whose verified number is in your contacts, or
 * who signed in with Facebook and are your Facebook friends. The numbers and ids a phone sends are
 * only looked up, never stored; a number matches through the same peppered hash the phone sign-in
 * keeps (OQ-041), so the database still holds no numbers. Friends, pending requests, blocks and you
 * are left out.
 */
export const MAX_PHONES = 3_000;
export const MAX_FACEBOOK_IDS = 1_000;
export const MAX_SUGGESTIONS = 50;

export type SuggestionSource = "contacts" | "facebook";

export interface Suggestion {
  readonly userId: string;
  readonly displayName: string;
  readonly avatar: string | null;
  readonly source: SuggestionSource;
}

export interface SuggestionPorts {
  /** sha256 hex of an E.164 number with the server pepper, as the phone sign-in stores it. */
  hashPhone(phone: string): string;
  /** Accounts holding these Nakama custom ids. */
  accountsByCustomId(customIds: readonly string[]): readonly string[];
  /** Accounts linked to these Facebook user ids. */
  accountsByFacebookId(facebookIds: readonly string[]): readonly string[];
  /** Everyone already on the player's friend graph: friends, requests either way, blocks. */
  knownIds(userId: string): ReadonlySet<string>;
  /** Blocked either way. */
  excluded(userId: string): readonly string[];
  profile(userId: string): { readonly displayName: string; readonly avatar: string | null } | null;
}

function strings(value: unknown, max: number): string[] {
  if (!Array.isArray(value)) return [];
  return value.filter((v): v is string => typeof v === "string" && v.length <= 64).slice(0, max);
}

export function suggestFriends(
  ports: SuggestionPorts,
  userId: string,
  input: unknown,
): Suggestion[] {
  const body =
    typeof input === "object" && input !== null ? (input as Record<string, unknown>) : {};
  const phones = [
    ...new Set(
      strings(body["phones"], MAX_PHONES)
        .map(normalisePhone)
        .filter((p): p is string => p !== null),
    ),
  ];
  const facebookIds = [
    ...new Set(
      strings(body["facebookIds"], MAX_FACEBOOK_IDS).filter((id) => /^\d{1,32}$/.test(id)),
    ),
  ];
  const skip = new Set<string>([userId, ...ports.knownIds(userId), ...ports.excluded(userId)]);
  const found = new Map<string, SuggestionSource>();
  const customIds = phones.map((p) => `${CUSTOM_ID_PREFIX}${ports.hashPhone(p)}`);
  for (const id of customIds.length === 0 ? [] : ports.accountsByCustomId(customIds)) {
    if (!skip.has(id)) found.set(id, "contacts");
  }
  for (const id of facebookIds.length === 0 ? [] : ports.accountsByFacebookId(facebookIds)) {
    if (!skip.has(id) && !found.has(id)) found.set(id, "facebook");
  }
  const out: Suggestion[] = [];
  for (const [id, source] of found) {
    if (out.length >= MAX_SUGGESTIONS) break;
    const profile = ports.profile(id);
    if (profile === null) continue;
    out.push({ userId: id, displayName: profile.displayName, avatar: profile.avatar, source });
  }
  return out;
}
