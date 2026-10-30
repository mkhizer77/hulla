import { describe, expect, it } from "vitest";

import { CUSTOM_ID_PREFIX } from "../services/phone_otp";
import { MAX_SUGGESTIONS, suggestFriends, type SuggestionPorts } from "../services/suggestions";

function ports(
  over: Partial<SuggestionPorts> = {},
): SuggestionPorts & { readonly asked: string[][] } {
  const asked: string[][] = [];
  const byCustom: Record<string, string> = {
    [`${CUSTOM_ID_PREFIX}h:+923001234567`]: "zain",
    [`${CUSTOM_ID_PREFIX}h:+923331112222`]: "friend",
    [`${CUSTOM_ID_PREFIX}h:+923339998888`]: "me",
  };
  const byFacebook: Record<string, string> = { "1001": "hira", "1002": "zain", "1003": "blocked" };
  return {
    asked,
    hashPhone: (phone) => `h:${phone}`,
    accountsByCustomId(ids) {
      asked.push([...ids]);
      return ids.map((id) => byCustom[id]).filter((v): v is string => v !== undefined);
    },
    accountsByFacebookId: (ids) =>
      ids.map((id) => byFacebook[id]).filter((v): v is string => v !== undefined),
    knownIds: () => new Set(["friend"]),
    excluded: () => ["blocked"],
    profile: (id) => ({ displayName: id.toUpperCase(), avatar: null }),
    ...over,
  };
}

describe("people you may know (owner decision, 30 Oct)", () => {
  it("matches verified numbers from contacts and Facebook friends who use the app, contacts first", () => {
    const p = ports();
    const found = suggestFriends(p, "me", {
      phones: [
        "+92 300 123-4567",
        "+923331112222",
        "+923339998888",
        "0300 1234567",
        "not a number",
      ],
      facebookIds: ["1001", "1002", "1003", "x"],
    });
    expect(found).toEqual([
      { userId: "zain", displayName: "ZAIN", avatar: null, source: "contacts" },
      { userId: "hira", displayName: "HIRA", avatar: null, source: "facebook" },
    ]);
    // Only international numbers are looked up, each once, as the phone sign-in's peppered hash.
    expect(p.asked).toEqual([
      [
        `${CUSTOM_ID_PREFIX}h:+923001234567`,
        `${CUSTOM_ID_PREFIX}h:+923331112222`,
        `${CUSTOM_ID_PREFIX}h:+923339998888`,
      ],
    ]);
  });

  it("leaves out friends, blocks and the player, and returns nothing for an empty or bad body", () => {
    expect(suggestFriends(ports(), "me", {})).toEqual([]);
    expect(suggestFriends(ports(), "me", "nope")).toEqual([]);
    expect(suggestFriends(ports(), "me", { phones: "+923331112222", facebookIds: [1003] })).toEqual(
      [],
    );
  });

  it("caps the answer", () => {
    const many = Array.from({ length: 80 }, (_, i) => String(2000 + i));
    const found = suggestFriends(
      ports({ accountsByFacebookId: (ids) => ids.map((id) => `u${id}`) }),
      "me",
      { facebookIds: many },
    );
    expect(found).toHaveLength(MAX_SUGGESTIONS);
  });
});
