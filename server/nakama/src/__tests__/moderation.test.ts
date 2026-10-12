import { describe, expect, it } from "vitest";

import {
  EMPTY_BLOCKS,
  excludeBlocked,
  excludedIds,
  isBlockedEitherWay,
  MATCHMAKER_EXCLUDE_CAP,
  setBlock,
  type BlockPorts,
  type BlockRecord,
  type BlockStore,
} from "../services/blocks";
import { EMPTY_STATS, type Profile } from "../services/profiles";
import {
  AUTO_MUTE_MS,
  CHAT_RETENTION_MS,
  countsTowardMute,
  createReport,
  isMuted,
  REPORT_WINDOW_MS,
  type ChatLine,
  type Moderation,
  type Report,
  type ReportIndexEntry,
  type ReportStore,
} from "../services/reports";
import { addFriendByCode, type FriendPorts, type SocialStore } from "../services/social";

const T0 = 1_760_200_000_000;

function blockStore(): BlockStore & { readonly records: Map<string, BlockRecord> } {
  const records = new Map<string, BlockRecord>();
  return {
    records,
    read: (u) => records.get(u) ?? EMPTY_BLOCKS,
    write: (u, r) => void records.set(u, r),
  };
}

function socialStore(): SocialStore & { readonly mut: Map<string, readonly string[]> } {
  const mut = new Map<string, readonly string[]>();
  return {
    mut,
    userIdForFriendCode: (code) => (code === "AB72CD34" ? "u2" : null),
    favourites: () => [],
    writeFavourites: () => undefined,
    recent: () => [],
    writeRecent: () => undefined,
    mutes: (u) => mut.get(u) ?? [],
    writeMutes: (u, ids) => void mut.set(u, ids),
  };
}

function ports(): BlockPorts & { readonly calls: string[] } {
  const calls: string[] = [];
  return {
    calls,
    block: (u, t) => void calls.push(`block ${u} ${t}`),
    unblock: (u, t) => void calls.push(`unblock ${u} ${t}`),
  };
}

describe("blocking is absolute (GDD 10.11, 24.4)", () => {
  it("records both directions, mutes both ways, and undoes all of it on unblock", () => {
    const blocks = blockStore();
    const social = socialStore();
    const p = ports();
    expect(setBlock(blocks, social, p, "u1", "u2", true)).toEqual({ ok: true, blocked: ["u2"] });
    expect(blocks.read("u2")).toEqual({ blocked: [], blockedBy: ["u1"] });
    expect(social.mutes("u1")).toEqual(["u2"]);
    expect(social.mutes("u2")).toEqual(["u1"]);
    expect(isBlockedEitherWay(blocks, "u2", "u1")).toBe(true);
    expect(excludedIds(blocks, "u2")).toEqual(["u1"]);
    expect(setBlock(blocks, social, p, "u1", "u2", true)).toEqual({ ok: true, blocked: ["u2"] });
    expect(setBlock(blocks, social, p, "u1", "u2", false)).toEqual({ ok: true, blocked: [] });
    expect(blocks.read("u2")).toEqual({ blocked: [], blockedBy: [] });
    expect(social.mutes("u1")).toEqual([]);
    expect(social.mutes("u2")).toEqual([]);
    expect(p.calls).toEqual(["block u1 u2", "block u1 u2", "unblock u1 u2"]);
    expect(setBlock(blocks, social, p, "u1", "u1", true)).toMatchObject({
      error: { code: "table.invalid_payload" },
    });
    expect(setBlock(blocks, social, p, null, "u2", true)).toMatchObject({
      error: { code: "table.not_authorised" },
    });
  });

  it("stops friend requests in either direction", () => {
    const blocks = blockStore();
    setBlock(blocks, socialStore(), ports(), "u2", "u1", true);
    const friendPorts: FriendPorts = {
      friendCount: () => 0,
      isFriend: () => false,
      isBlocked: (u, o) => isBlockedEitherWay(blocks, u, o),
      addFriend: () => {
        throw new Error("must not be called");
      },
    };
    expect(addFriendByCode(socialStore(), friendPorts, "u1", "AB72CD34")).toMatchObject({
      error: { code: "social.blocked" },
    });
  });

  it("writes the owner's id and the exclusions into the matchmaker ticket", () => {
    const ticket = excludeBlocked(
      { query: "+properties.game:thulla ", stringProperties: { game: "thulla" } },
      "u1",
      ["u2", "u3"],
    );
    expect(ticket).toEqual({
      query: "+properties.game:thulla -properties.uid:u2 -properties.uid:u3",
      stringProperties: { game: "thulla", uid: "u1" },
    });
    expect(excludeBlocked({ query: "*", stringProperties: {} }, "u1", []).query).toBe("*");
    const many = Array.from({ length: MATCHMAKER_EXCLUDE_CAP + 5 }, (_, i) => `b${String(i)}`);
    const capped = excludeBlocked({ query: "", stringProperties: {} }, "u1", many);
    expect(capped.query.split(" ")).toHaveLength(MATCHMAKER_EXCLUDE_CAP);
    expect(capped.query).toContain("-properties.uid:b104");
    expect(capped.query).not.toContain("-properties.uid:b0 ");
  });
});

function profile(name: string): Profile {
  return {
    displayName: name,
    handle: name.toLowerCase(),
    friendCode: "X",
    avatar: "a01",
    frame: "f00",
    flag: null,
    title: null,
    bio: "hi",
    stats: EMPTY_STATS,
    privacy: { showOnline: true, visibility: "everyone" },
    nameChanges: 0,
    createdTs: T0,
    updatedTs: T0,
  };
}

interface MemoryReports extends ReportStore {
  readonly reports: Report[];
  readonly muted: Map<string, Moderation>;
}

function reportStore(chat: readonly ChatLine[] = []): MemoryReports {
  const reports: Report[] = [];
  const index = new Map<string, readonly ReportIndexEntry[]>();
  const muted = new Map<string, Moderation>();
  let ids = 0;
  return {
    reports,
    muted,
    newId: () => {
      ids += 1;
      return `r${String(ids)}`;
    },
    writeReport: (r) => void reports.push(r),
    index: (t) => index.get(t) ?? [],
    writeIndex: (t, e) => void index.set(t, e),
    chatLog: () => chat,
    moderation: (u) => muted.get(u) ?? null,
    writeModeration: (u, m) => void muted.set(u, m),
    profile: (u) => (u === "bad" ? profile("Bad") : null),
  };
}

const report = (
  store: ReportStore,
  reporterId: string | null,
  targetId: string,
  now: number,
  tableId: string | null = "t_AAAAAA",
  reason = "abusive_text",
): ReturnType<typeof createReport> =>
  createReport(store, { reporterId, targetId, reason, tableId, roundId: "r_1", now });

describe("reports with context and the auto-mute (GDD 10.11, 24.3)", () => {
  it("attaches the table's chat from the last seven days and the target's card", () => {
    const store = reportStore([
      { userId: "bad", text: "old", ts: T0 - CHAT_RETENTION_MS - 1 },
      { userId: "bad", text: "**** you", ts: T0 - 1000 },
      { userId: "u1", text: "stop", ts: T0 - 500 },
    ]);
    const filed = report(store, "u1", "bad", T0);
    expect(filed).toEqual({
      ok: true,
      reportId: "r1",
      duplicate: false,
      chatLines: 2,
      autoMuted: false,
    });
    expect(store.reports[0]).toMatchObject({
      reporterId: "u1",
      targetId: "bad",
      reason: "abusive_text",
      tableId: "t_AAAAAA",
      roundId: "r_1",
      targetProfile: { displayName: "Bad", handle: "bad", avatar: "a01", bio: "hi" },
      status: "open",
    });
    expect(store.reports[0]?.chat.map((l) => l.text)).toEqual(["**** you", "stop"]);
    expect(report(store, "u1", "bad", T0 + 1000, null)).toMatchObject({
      duplicate: true,
      chatLines: 0,
    });
    expect(report(store, null, "bad", T0)).toMatchObject({
      error: { code: "table.not_authorised" },
    });
    expect(report(store, "u1", "u1", T0)).toMatchObject({
      error: { code: "table.invalid_payload" },
    });
    expect(report(store, "u1", "bad", T0, null, "because")).toMatchObject({
      error: { code: "table.invalid_payload", detail: { reason: "reason" } },
    });
  });

  it("mutes after three distinct reporters within a day, counting one report per table", () => {
    const store = reportStore();
    expect(report(store, "u1", "bad", T0, "t_A")).toMatchObject({ autoMuted: false });
    expect(report(store, "u2", "bad", T0 + 1, "t_A")).toMatchObject({ autoMuted: false });
    expect(report(store, "u3", "bad", T0 + 2, "t_A")).toMatchObject({ autoMuted: false });
    expect(store.muted.get("bad")).toBeUndefined();
    expect(report(store, "u4", "bad", T0 + 3, "t_B")).toMatchObject({ autoMuted: false });
    expect(report(store, "u5", "bad", T0 + 4, null)).toMatchObject({ autoMuted: true });
    expect(store.muted.get("bad")).toEqual({
      mutedUntilTs: T0 + 4 + AUTO_MUTE_MS,
      reason: "auto_reports",
    });
    expect(isMuted(store.moderation("bad"), T0 + 5)).toBe(true);
    expect(isMuted(store.moderation("bad"), T0 + 5 + AUTO_MUTE_MS)).toBe(false);
    expect(report(store, "u6", "bad", T0 + 6, "t_C")).toMatchObject({ autoMuted: false });
  });

  it("forgets reports older than a day", () => {
    const entries: ReportIndexEntry[] = [
      { reporterId: "u1", tableId: "t_A", ts: T0 - REPORT_WINDOW_MS - 1 },
      { reporterId: "u2", tableId: "t_B", ts: T0 - 10 },
      { reporterId: "u3", tableId: null, ts: T0 - 5 },
      { reporterId: "u3", tableId: null, ts: T0 - 4 },
    ];
    expect(countsTowardMute(entries, T0)).toBe(2);
  });
});
