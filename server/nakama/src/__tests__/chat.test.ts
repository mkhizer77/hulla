import { describe, expect, it } from "vitest";

import { BUNDLED_CHAT, EMOTES, QUICK_CHAT_PHRASES } from "../content/chat";
import { checkChatMessage, type RateStore } from "../hooks/before_channel_message";

import { broadcasts, harness, presence, seated, sentTo } from "./table-helpers";

function memoryRate(): RateStore & { readonly map: Map<string, readonly number[]> } {
  const map = new Map<string, readonly number[]>();
  return { map, read: (u) => map.get(u) ?? [], write: (u, ts) => void map.set(u, ts) };
}

describe("quick chat and emotes as match data (GDD 11.2, 11.3)", () => {
  it("ships the 48 phrases, the 16 launch emotes and the paid packs from content", () => {
    expect(QUICK_CHAT_PHRASES).toHaveLength(48);
    expect(new Set(QUICK_CHAT_PHRASES.map((p) => p.id)).size).toBe(48);
    expect(EMOTES.filter((e) => e.pack === "emotes_launch")).toHaveLength(16);
    expect(EMOTES.filter((e) => e.pack === "emotes_cricket").length).toBeGreaterThan(0);
    expect(EMOTES.filter((e) => e.pack === "emotes_shaadi").length).toBeGreaterThan(0);
    expect(BUNDLED_CHAT.emotePack("six")).toBe("emotes_launch");
    expect(BUNDLED_CHAT.emotePack("wicket")).toBe("emotes_cricket");
    expect(BUNDLED_CHAT.freeEmotePacks).toEqual(["emotes_launch"]);
    expect(BUNDLED_CHAT.isPhrase("v1", "T01")).toBe(true);
    expect(BUNDLED_CHAT.isPhrase("v2", "T01")).toBe(false);
    expect(BUNDLED_CHAT.isEmote("chai")).toBe(true);
  });

  it("reaches every seat and spectator with the sender's seat, and refuses spectators as senders", () => {
    const h = harness();
    seated(h, 3);
    h.join(presence(9), { asSpectator: "true" });
    const sent = h.send("s1", "quick_chat", { phraseId: "T02" });
    expect(broadcasts(sent)).toHaveLength(1);
    expect(broadcasts(sent)[0]).toMatchObject({
      type: "quick_chat_sent",
      payload: { seat: 1, phraseId: "T02", pack: "v1" },
    });
    const emote = h.send("s2", "emote", { emoteId: "turtle", targetSeat: 0 });
    expect(broadcasts(emote)[0]).toMatchObject({
      type: "emote_sent",
      payload: { seat: 2, emoteId: "turtle", targetSeat: 0 },
    });
    const nobody = h.send("s0", "emote", { emoteId: "clap", targetSeat: 5 });
    expect(broadcasts(nobody)[0]?.payload).toEqual({ seat: 0, emoteId: "clap" });
    expect(sentTo(h.send("s9", "quick_chat", { phraseId: "T01" }), "s9")[0]?.payload).toMatchObject(
      { code: "table.not_authorised" },
    );
  });

  it("rejects floods, unknown phrases and unknown emotes with catalogue errors", () => {
    const h = harness();
    seated(h, 3);
    h.send("s0", "quick_chat", { phraseId: "T01" });
    const flood = h.send("s0", "quick_chat", { phraseId: "T03" });
    expect(broadcasts(flood)).toEqual([]);
    expect(sentTo(flood, "s0")[0]?.payload).toMatchObject({
      code: "network.rate_limited",
      detail: { kind: "quick_chat" },
    });
    h.now += 3_000;
    expect(broadcasts(h.send("s0", "quick_chat", { phraseId: "T03" }))).toHaveLength(1);
    expect(sentTo(h.send("s1", "quick_chat", { phraseId: "T99" }), "s1")[0]?.payload).toMatchObject(
      { code: "content.pack_unknown" },
    );
    expect(
      sentTo(h.send("s1", "quick_chat", { phraseId: "T01", pack: "v9" }), "s1")[0]?.payload,
    ).toMatchObject({ code: "content.pack_unknown" });
    h.send("s2", "emote", { emoteId: "fire" });
    expect(sentTo(h.send("s2", "emote", { emoteId: "heart" }), "s2")[0]?.payload).toMatchObject({
      code: "network.rate_limited",
      detail: { kind: "emote" },
    });
    expect(sentTo(h.send("s1", "emote", { emoteId: "chappal" }), "s1")[0]?.payload).toMatchObject({
      code: "content.pack_unknown",
    });
    expect(sentTo(h.send("s1", "emote", { emoteId: "" }), "s1")[0]?.payload).toMatchObject({
      detail: { reason: "schema" },
    });
  });
});

describe("emote packs (GDD 11.3, 12.11)", () => {
  it("lets only a pack's owners send its emotes, while every seat sees them", () => {
    const h = harness();
    seated(h, 3);
    const refused = h.send("s1", "emote", { emoteId: "wicket" });
    expect(broadcasts(refused)).toEqual([]);
    expect(sentTo(refused, "s1")[0]?.payload).toMatchObject({
      code: "table.not_authorised",
      detail: { reason: "not_owned", emotePack: "emotes_cricket" },
    });
    expect(broadcasts(h.send("s1", "emote", { emoteId: "chai" }))).toHaveLength(1);
    const owner = h.state.seats[2];
    if (owner === undefined) throw new Error("seat 2");
    owner.emotePacks = ["emotes_launch", "emotes_cricket"];
    const sent = h.send("s2", "emote", { emoteId: "wicket", targetSeat: 1 });
    expect(broadcasts(sent)[0]).toMatchObject({
      type: "emote_sent",
      payload: { seat: 2, emoteId: "wicket", targetSeat: 1 },
    });
    h.now += 3_000;
    expect(sentTo(h.send("s2", "emote", { emoteId: "laddoo" }), "s2")[0]?.payload).toMatchObject({
      detail: { reason: "not_owned", emotePack: "emotes_shaadi" },
    });
  });
});

describe("the text chat hook (GDD 11.1)", () => {
  const T0 = 1_760_200_000_000;
  const msg = (text: string): string => JSON.stringify({ text });

  it("allows one message per two seconds and ten per minute", () => {
    const store = memoryRate();
    expect(checkChatMessage(store, "u1", msg("salaam"), T0)).toMatchObject({ ok: true });
    expect(checkChatMessage(store, "u1", msg("again"), T0 + 1_000)).toEqual({
      ok: false,
      code: "network.rate_limited",
    });
    expect(checkChatMessage(store, "u2", msg("other"), T0 + 1_000)).toMatchObject({ ok: true });
    let now = T0;
    for (let i = 1; i < 10; i += 1) {
      now += 2_000;
      expect(checkChatMessage(store, "u1", msg(`m${String(i)}`), now)).toMatchObject({ ok: true });
    }
    expect(checkChatMessage(store, "u1", msg("eleventh"), now + 2_000)).toEqual({
      ok: false,
      code: "network.rate_limited",
    });
    expect(checkChatMessage(store, "u1", msg("later"), T0 + 61_000)).toMatchObject({ ok: true });
  });

  it("refuses empty, long, link and phone-number messages and bad JSON", () => {
    const store = memoryRate();
    expect(checkChatMessage(store, "u1", msg(""), T0)).toEqual({
      ok: false,
      code: "table.invalid_payload",
    });
    expect(checkChatMessage(store, "u1", msg("x".repeat(141)), T0)).toEqual({
      ok: false,
      code: "table.invalid_payload",
    });
    expect(checkChatMessage(store, "u1", msg("join https://example.com now"), T0)).toEqual({
      ok: false,
      code: "table.invalid_payload",
    });
    expect(checkChatMessage(store, "u1", msg("call 0300 1234567"), T0)).toEqual({
      ok: false,
      code: "table.invalid_payload",
    });
    expect(checkChatMessage(store, "u1", "{", T0)).toEqual({
      ok: false,
      code: "table.invalid_payload",
    });
    expect(checkChatMessage(store, "u1", msg("chai break? 5 min"), T0)).toMatchObject({ ok: true });
  });
});
