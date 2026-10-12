import { describe, expect, it } from "vitest";

import { BUNDLED_FILTERS } from "../content/filters";
import { beforeChannelMessageSend } from "../hooks/before_channel_message";
import { rpcChatFiltersGet, rpcChatFiltersSet } from "../rpc/chat_filters";
import { rpcMuteSet, rpcMutesGet } from "../rpc/social";
import { FILTERS_COLLECTION, FILTERS_KEY, SYSTEM_USER_ID } from "../services/chat_filter";

import { fakeLogger, memoryNk } from "./fake-nk";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const ctxFor = (userId: string): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env: {} }) as unknown as nkruntime.Context;
const ADMIN = { env: {} } as unknown as nkruntime.Context;

function message(channelId: string, text: string): nkruntime.EnvelopeChannelMessageSend {
  return {
    channelMessageSend: { channelId, content: JSON.stringify({ text }) },
  };
}

describe("the chat hook, the filter lists and mutes against an in-memory Nakama", () => {
  it("masks in a standard room, blocks in a family room, locks guests at public tables", () => {
    const m = memoryNk();
    const log = fakeLogger();
    const standard = m.nk.matchCreate("thulla", {
      code: "AAAAAA",
      mode: "private",
      gameId: "thulla",
    });
    const family = m.nk.matchCreate("thulla", {
      code: "BBBBBB",
      mode: "private",
      gameId: "thulla",
      rules: { gameId: "thulla", preset: "family" },
    });
    const publicTable = m.nk.matchCreate("thulla", {
      code: "CCCCCC",
      mode: "public",
      gameId: "thulla",
    });

    const masked = beforeChannelMessageSend(
      ctxFor("u1"),
      log,
      m.nk,
      message(`2...${standard}`, "ye sh1t hai"),
    );
    expect(JSON.parse(masked.channelMessageSend.content)).toEqual({ text: "ye **** hai" });
    expect(() =>
      beforeChannelMessageSend(ctxFor("u2"), log, m.nk, message(`2...${family}`, "ye shit hai")),
    ).toThrow("content.filtered");
    expect(() =>
      beforeChannelMessageSend(ctxFor("u3"), log, m.nk, message(`2...${publicTable}`, "salaam")),
    ).toThrow("auth.not_linked");
    m.user("u3", { email: "u3@example.com" });
    expect(
      beforeChannelMessageSend(ctxFor("u3"), log, m.nk, message(`2...${publicTable}`, "salaam")),
    ).toBeDefined();
    expect(
      beforeChannelMessageSend(ctxFor("u4"), log, m.nk, message("junk", "salaam")),
    ).toBeDefined();
    expect(
      beforeChannelMessageSend(ctxFor("u5"), log, m.nk, message("2...gone.n1", "salaam")),
    ).toBeDefined();
  });

  it("serves and replaces the lists for the server key only", () => {
    const m = memoryNk();
    const log = fakeLogger();
    expect(parse(rpcChatFiltersGet(ADMIN, log, m.nk))).toMatchObject({
      ok: true,
      lists: { id: BUNDLED_FILTERS.id },
    });
    expect(parse(rpcChatFiltersGet(ctxFor("u1"), log, m.nk))).toMatchObject({ ok: false });
    expect(parse(rpcChatFiltersSet(ctxFor("u1"), log, m.nk, "{}"))).toMatchObject({ ok: false });
    expect(parse(rpcChatFiltersSet(ADMIN, log, m.nk, "{"))).toMatchObject({
      error: { code: "table.invalid_payload" },
    });
    expect(
      parse(
        rpcChatFiltersSet(ADMIN, log, m.nk, JSON.stringify({ id: "x", version: 1, languages: [] })),
      ),
    ).toMatchObject({
      error: { code: "content.schema_invalid" },
    });
    const uploaded = parse(
      rpcChatFiltersSet(
        ADMIN,
        log,
        m.nk,
        JSON.stringify({ id: "new", version: 2, languages: [{ lang: "en", words: ["zorblat"] }] }),
      ),
    );
    expect(uploaded).toEqual({ ok: true, id: "new", version: 2 });
    expect(m.object(FILTERS_COLLECTION, FILTERS_KEY, SYSTEM_USER_ID)).toMatchObject({ id: "new" });
    expect(parse(rpcChatFiltersGet(ADMIN, log, m.nk))).toMatchObject({ lists: { id: "new" } });
    const room = m.nk.matchCreate("thulla", { code: "DDDDDD", mode: "private", gameId: "thulla" });
    const out = beforeChannelMessageSend(
      ctxFor("u1"),
      log,
      m.nk,
      message(`2...${room}`, "zorblat!"),
    );
    expect(JSON.parse(out.channelMessageSend.content)).toEqual({ text: "********" });
  });

  it("keeps mutes per account", () => {
    const m = memoryNk();
    const log = fakeLogger();
    expect(
      parse(rpcMuteSet(ctxFor("u1"), log, m.nk, JSON.stringify({ userId: "u2", muted: true }))),
    ).toEqual({
      ok: true,
      mutes: ["u2"],
    });
    expect(parse(rpcMutesGet(ctxFor("u1"), log, m.nk))).toEqual({ ok: true, mutes: ["u2"] });
    expect(parse(rpcMuteSet(ctxFor("u1"), log, m.nk, "{}"))).toMatchObject({
      error: { code: "table.invalid_payload" },
    });
    expect(parse(rpcMutesGet(ADMIN, log, m.nk))).toMatchObject({ ok: false });
  });
});
