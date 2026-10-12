import { describe, expect, it } from "vitest";

import { beforeChannelMessageSend } from "../hooks/before_channel_message";
import { beforeMatchmakerAdd } from "../hooks/before_matchmaker_add";
import {
  rpcAdminChatLogsPurge,
  rpcBlockSet,
  rpcBlocksGet,
  rpcReportCreate,
  rpcReportsList,
} from "../rpc/moderation";
import { rpcFriendAddByCode } from "../rpc/social";
import { CHAT_LOG_COLLECTION, CHAT_RETENTION_MS, SYSTEM_USER_ID } from "../services/reports";

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

describe("blocks, the matchmaker ticket and reports against an in-memory Nakama", () => {
  it("a block lands on the friend graph, mutes both ways, filters tickets and refuses requests", () => {
    const m = memoryNk();
    const log = fakeLogger();
    m.nk.friendsAdd("u1", "user_u1", ["u2"], [], {});
    m.nk.friendsAdd("u2", "user_u2", ["u1"], [], {});
    expect(m.friends.get("u1")?.get("u2")).toBe(0);
    expect(
      parse(rpcBlockSet(ctxFor("u1"), log, m.nk, JSON.stringify({ userId: "u2", blocked: true }))),
    ).toEqual({
      ok: true,
      blocked: ["u2"],
    });
    expect(m.friends.get("u1")?.get("u2")).toBe(3);
    expect(m.friends.get("u2")?.get("u1")).toBeUndefined();
    expect(m.object("mutes", "list", "u2")).toEqual({ ids: ["u1"] });
    expect(parse(rpcBlocksGet(ctxFor("u1"), log, m.nk))).toEqual({ ok: true, blocked: ["u2"] });
    expect(parse(rpcBlocksGet(ADMIN, log, m.nk))).toMatchObject({ ok: false });

    const envelope = {
      matchmakerAdd: {
        minCount: 3,
        maxCount: 4,
        query: "+properties.game:thulla",
        stringProperties: { game: "thulla" },
        numericProperties: {},
        countMultiple: 1,
      },
    } as unknown as nkruntime.EnvelopeMatchmakerAdd;
    const ticket = beforeMatchmakerAdd(ctxFor("u2"), log, m.nk, envelope);
    expect(ticket.matchmakerAdd.query).toBe("+properties.game:thulla -properties.uid:u1");
    expect(ticket.matchmakerAdd.stringProperties).toEqual({ game: "thulla", uid: "u2" });
    expect(beforeMatchmakerAdd(ADMIN, log, m.nk, envelope)).toBe(envelope);

    m.nk.storageWrite([
      {
        collection: "friend_codes",
        key: "AB72CD34",
        userId: SYSTEM_USER_ID,
        value: { userId: "u1" },
      },
    ]);
    expect(
      parse(
        rpcFriendAddByCode(ctxFor("u2"), log, m.nk, JSON.stringify({ friendCode: "AB72CD34" })),
      ),
    ).toMatchObject({ error: { code: "social.blocked" } });
    expect(
      parse(rpcBlockSet(ctxFor("u1"), log, m.nk, JSON.stringify({ userId: "u2", blocked: false }))),
    ).toEqual({
      ok: true,
      blocked: [],
    });
    expect(m.object("mutes", "list", "u2")).toEqual({ ids: [] });
  });

  it("reports carry the room's chat, three mute the target, and the lists serve the server key", () => {
    const m = memoryNk();
    const log = fakeLogger();
    const room = m.nk.matchCreate("thulla", { code: "AAAAAA", mode: "private", gameId: "thulla" });
    beforeChannelMessageSend(ctxFor("bad"), log, m.nk, message(`2...${room}`, "you are shit"));
    expect(m.object(CHAT_LOG_COLLECTION, room, SYSTEM_USER_ID)).toMatchObject({
      messages: [{ userId: "bad", text: "you are ****" }],
    });
    const first = parse(
      rpcReportCreate(
        ctxFor("r1"),
        log,
        m.nk,
        JSON.stringify({ userId: "bad", reason: "abusive_text", tableId: room, roundId: "r_1" }),
      ),
    );
    expect(first).toMatchObject({ ok: true, chatLines: 1, autoMuted: false, duplicate: false });
    parse(
      rpcReportCreate(ctxFor("r2"), log, m.nk, JSON.stringify({ userId: "bad", reason: "spam" })),
    );
    const third = parse(
      rpcReportCreate(
        ctxFor("r3"),
        log,
        m.nk,
        JSON.stringify({ userId: "bad", reason: "cheating" }),
      ),
    );
    expect(third).toMatchObject({ ok: true, autoMuted: true });
    expect(() =>
      beforeChannelMessageSend(ctxFor("bad"), log, m.nk, message(`2...${room}`, "still here")),
    ).toThrow("social.muted");
    expect(
      parse(
        rpcReportCreate(ctxFor("r3"), log, m.nk, JSON.stringify({ userId: "bad", reason: "nope" })),
      ),
    ).toMatchObject({
      error: { code: "table.invalid_payload" },
    });

    const listed = parse(rpcReportsList(ADMIN, log, m.nk, JSON.stringify({ limit: 2 })));
    expect((listed["reports"] as Json[]).length).toBe(2);
    expect(parse(rpcReportsList(ctxFor("r1"), log, m.nk, "{}"))).toMatchObject({ ok: false });

    expect(parse(rpcAdminChatLogsPurge(ADMIN, log, m.nk, "{}"))).toEqual({ ok: true, purged: 0 });
    expect(
      parse(
        rpcAdminChatLogsPurge(
          ADMIN,
          log,
          m.nk,
          JSON.stringify({ now: Date.now() + CHAT_RETENTION_MS + 1 }),
        ),
      ),
    ).toEqual({ ok: true, purged: 1 });
    expect(m.object(CHAT_LOG_COLLECTION, room, SYSTEM_USER_ID)).toBeUndefined();
    expect(parse(rpcAdminChatLogsPurge(ctxFor("r1"), log, m.nk, "{}"))).toMatchObject({
      ok: false,
    });
  });
});
