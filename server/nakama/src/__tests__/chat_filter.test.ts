import { describe, expect, it } from "vitest";

import { BUNDLED_FILTERS } from "../content/filters";
import { checkChatMessage, type RateStore } from "../hooks/before_channel_message";
import {
  buildMatcher,
  filterText,
  isFilterLists,
  isGuestAccount,
  normaliseForms,
  normaliseWord,
  roomOfChannel,
} from "../services/chat_filter";

const matcher = buildMatcher(BUNDLED_FILTERS);

function rateStore(): RateStore {
  const ts = new Map<string, readonly number[]>();
  return { read: (u) => ts.get(u) ?? [], write: (u, t) => void ts.set(u, t) };
}

describe("the profanity filter (GDD 11.1)", () => {
  it("undoes the usual spelling evasions", () => {
    expect(normaliseWord("S.H.I.T")).toBe("shit");
    expect(normaliseWord("sh1t")).toBe("shit");
    expect(normaliseWord("shiiiit")).toBe("shit");
    expect(normaliseForms("$hit!")).toEqual(["shiti", "shit"]);
    expect(normaliseWord("sh!t")).toBe("shit");
    expect(normaliseWord("(zorblat)")).toBe("zorblat");
    expect(normaliseForms("zorblat!")).toEqual(["zorblati", "zorblat"]);
    expect(normaliseForms("a$$")).toEqual(["as", "a"]);
    expect(normaliseWord("B-h-e-n-c-h-o-d")).toBe("bhenchod");
  });

  it("masks English and Roman Urdu words and keeps the rest of the line", () => {
    expect(filterText(matcher, "that was shit yaar")).toEqual({
      text: "that was **** yaar",
      hits: 1,
    });
    expect(filterText(matcher, "oye BHENCHOD kya kar raha hai")).toEqual({
      text: "oye ******** kya kar raha hai",
      hits: 1,
    });
    expect(filterText(matcher, "sh1t sh.it shiiit")).toMatchObject({ hits: 3 });
    expect(filterText(matcher, "a$$ $hit!")).toMatchObject({ hits: 1 });
    expect(filterText(matcher, "ullu ka pattha tu")).toEqual({
      text: "**** ** ****** tu",
      hits: 3,
    });
    expect(filterText(matcher, "ullu bola")).toMatchObject({ hits: 0 });
  });

  it("covers Urdu script, Devanagari and Bengali", () => {
    expect(filterText(matcher, "ye حرامی hai")).toMatchObject({ hits: 1 });
    expect(filterText(matcher, "arre चुतिया")).toMatchObject({ hits: 1 });
    expect(filterText(matcher, "তুই হারামি")).toMatchObject({ hits: 1 });
  });

  it("leaves clean text and lookalike words alone", () => {
    expect(filterText(matcher, "assassin class shift")).toEqual({
      text: "assassin class shift",
      hits: 0,
    });
    expect(filterText(matcher, "salaam sab ko, kya haal hai?")).toMatchObject({ hits: 0 });
  });

  it("masks in Standard, blocks in Family, and locks guests at public tables", () => {
    const standard = checkChatMessage(
      rateStore(),
      "u1",
      JSON.stringify({ text: "ye shit hai" }),
      1_000,
      {
        matcher,
        strictness: "standard",
        guestLocked: false,
      },
    );
    expect(standard).toEqual({
      ok: true,
      content: JSON.stringify({ text: "ye **** hai" }),
      masked: 1,
    });
    expect(
      checkChatMessage(rateStore(), "u1", JSON.stringify({ text: "ye shit hai" }), 1_000, {
        matcher,
        strictness: "family",
        guestLocked: false,
      }),
    ).toEqual({ ok: false, code: "content.filtered" });
    expect(
      checkChatMessage(rateStore(), "u1", JSON.stringify({ text: "salaam" }), 1_000, {
        matcher,
        strictness: "family",
        guestLocked: false,
      }),
    ).toMatchObject({ ok: true, masked: 0 });
    expect(
      checkChatMessage(rateStore(), "u1", JSON.stringify({ text: "salaam" }), 1_000, {
        matcher,
        strictness: "standard",
        guestLocked: true,
      }),
    ).toEqual({ ok: false, code: "auth.not_linked" });
  });

  it("recognises guests, rooms and well-formed lists", () => {
    expect(
      isGuestAccount({
        email: "",
        customId: "",
        user: { appleId: "", googleId: "", facebookId: "" },
      }),
    ).toBe(true);
    expect(isGuestAccount({ email: "", customId: "", user: { googleId: "g1" } })).toBe(false);
    expect(isGuestAccount({ email: "a@b.c", user: {} })).toBe(false);
    expect(roomOfChannel("2...abc-def.nakama1")).toBe("abc-def.nakama1");
    expect(roomOfChannel("3.group..x")).toBeNull();
    expect(roomOfChannel("junk")).toBeNull();
    expect(isFilterLists(BUNDLED_FILTERS)).toBe(true);
    expect(isFilterLists({ id: "x", version: 1, languages: [] })).toBe(false);
    expect(isFilterLists({ id: "x", version: 1, languages: [{ lang: "en", words: ["a"] }] })).toBe(
      false,
    );
  });
});
