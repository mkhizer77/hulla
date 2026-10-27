import { afterEach, describe, expect, it, vi } from "vitest";

import { rpcAdminLookFeedback, rpcLookFeedback } from "../rpc/look_feedback";
import { DEFAULT_CONFIG, resolveFlag } from "../services/config";
import {
  COMMENT_MAX,
  FEEDBACK_LOOKS,
  LOOK_FEEDBACK_COLLECTION,
  cleanComment,
  summarise,
  type LookFeedback,
} from "../services/look_feedback";
import { SYSTEM_USER_ID } from "../services/reports";
import { AUDIT_COLLECTION } from "../services/sanctions";

import { fakeLogger, memoryNk } from "./fake-nk";

type Json = Record<string, unknown>;
const T0 = 1_760_200_000_000;
const ctxFor = (userId: string, env: Record<string, string> = {}): nkruntime.Context =>
  ({ userId, env }) as unknown as nkruntime.Context;
const SERVER_KEY = { env: {} } as unknown as nkruntime.Context;

const send = (m: ReturnType<typeof memoryNk>, userId: string, body: unknown): Json =>
  JSON.parse(
    rpcLookFeedback(
      ctxFor(userId),
      fakeLogger(),
      m.nk,
      typeof body === "string" ? body : JSON.stringify(body),
    ),
  ) as Json;

const summary = (m: ReturnType<typeof memoryNk>, ctx: nkruntime.Context): Json =>
  JSON.parse(rpcAdminLookFeedback(ctx, fakeLogger(), m.nk)) as Json;

afterEach(() => {
  vi.useRealTimers();
});

describe("look feedback from players", () => {
  it("stores one server-only answer per player and look", () => {
    vi.useFakeTimers();
    vi.setSystemTime(T0);
    const m = memoryNk();
    const answer = send(m, "u1", {
      look: "basant",
      rating: 4,
      keep: true,
      comment: "  bright\u0007 kites\n ",
      rounds: 12,
    });
    expect(answer).toEqual({ ok: true });
    expect(m.object(LOOK_FEEDBACK_COLLECTION, "basant", "u1")).toEqual({
      look: "basant",
      rating: 4,
      keep: true,
      comment: "bright kites",
      rounds: 12,
      at: T0,
    });
    const stored = m.nk.storageRead([
      { collection: LOOK_FEEDBACK_COLLECTION, key: "basant", userId: "u1" },
    ])[0];
    expect(stored).toMatchObject({ permissionRead: 0, permissionWrite: 0 });
  });

  it("replaces an earlier answer on the same look and keeps the other look", () => {
    vi.useFakeTimers();
    vi.setSystemTime(T0);
    const m = memoryNk();
    send(m, "u1", { look: "pop", rating: 2, keep: false, comment: "too loud" });
    send(m, "u1", { look: "basant", rating: 5, keep: true });
    vi.setSystemTime(T0 + 1000);
    send(m, "u1", { look: "pop", rating: 3, keep: true, rounds: 5000 });
    expect(m.object(LOOK_FEEDBACK_COLLECTION, "pop", "u1")).toEqual({
      look: "pop",
      rating: 3,
      keep: true,
      rounds: 999,
      at: T0 + 1000,
    });
    expect(m.object(LOOK_FEEDBACK_COLLECTION, "basant", "u1")).toMatchObject({ rating: 5 });
  });

  it("refuses a caller without a session", () => {
    const m = memoryNk();
    expect(send(m, "", { look: "pop", rating: 3, keep: true })).toMatchObject({
      ok: false,
      error: { code: "table.not_authorised" },
    });
  });

  it.each([
    ["json", "{not json"],
    ["json", "[1]"],
    ["look", { look: "club", rating: 3, keep: true }],
    ["look", { rating: 3, keep: true }],
    ["rating", { look: "pop", rating: 0, keep: true }],
    ["rating", { look: "pop", rating: 6, keep: true }],
    ["rating", { look: "pop", rating: 3.5, keep: true }],
    ["rating", { look: "pop", rating: "4", keep: true }],
    ["keep", { look: "pop", rating: 3 }],
    ["keep", { look: "pop", rating: 3, keep: "yes" }],
    ["comment", { look: "pop", rating: 3, keep: true, comment: 7 }],
    ["comment", { look: "pop", rating: 3, keep: true, comment: "x".repeat(COMMENT_MAX + 1) }],
    ["rounds", { look: "pop", rating: 3, keep: true, rounds: -1 }],
    ["rounds", { look: "pop", rating: 3, keep: true, rounds: 1.5 }],
    ["rounds", { look: "pop", rating: 3, keep: true, rounds: "2" }],
  ])("refuses an invalid %s", (reason, body) => {
    const m = memoryNk();
    expect(send(m, "u1", body)).toEqual({
      ok: false,
      error: { code: "table.invalid_payload", detail: { reason } },
    });
    expect(m.nk.storageList(undefined, LOOK_FEEDBACK_COLLECTION, 100).objects).toHaveLength(0);
  });

  it("counts comment length after trimming, in characters, and drops an empty comment", () => {
    expect(cleanComment(`  ${"ک".repeat(COMMENT_MAX)}  `)).toHaveLength(COMMENT_MAX);
    expect(cleanComment("\u0000\u009f")).toBe("");
    const m = memoryNk();
    send(m, "u1", { look: "pop", rating: 3, keep: true, comment: "   " });
    expect(m.object(LOOK_FEEDBACK_COLLECTION, "pop", "u1")).not.toHaveProperty("comment");
  });

  it("names the test looks in one exported list", () => {
    expect(FEEDBACK_LOOKS).toEqual(["basant", "pop"]);
  });
});

describe("the owner's feedback summary", () => {
  it("summarises each look without user ids and walks every page", () => {
    vi.useFakeTimers();
    const m = memoryNk();
    for (let i = 0; i < 150; i += 1) {
      vi.setSystemTime(T0 + i);
      send(m, `user-${String(i).padStart(3, "0")}`, {
        look: "basant",
        rating: i % 2 === 0 ? 5 : 4,
        keep: i % 3 === 0,
        ...(i < 120 ? {} : { comment: `note ${String(i)}` }),
      });
    }
    send(m, "user-000", { look: "pop", rating: 2, keep: false, comment: "dark" });
    const answer = summary(m, SERVER_KEY);
    expect(answer["ok"]).toBe(true);
    const looks = answer["looks"] as Record<string, Json>;
    expect(looks["basant"]).toMatchObject({ count: 150, averageRating: 4.5, keepShare: 0.33 });
    const comments = looks["basant"]?.["comments"] as Json[];
    expect(comments).toHaveLength(30);
    expect(comments[0]).toEqual({ comment: "note 149", rating: 4, keep: false, at: T0 + 149 });
    expect(looks["pop"]).toMatchObject({ count: 1, averageRating: 2, keepShare: 0 });
    expect(JSON.stringify(answer)).not.toContain("user-");
    const audits = m.nk.storageList(SYSTEM_USER_ID, AUDIT_COLLECTION, 100).objects ?? [];
    expect(audits.map((o): unknown => o.value["action"])).toContain("look_feedback_read");
  });

  it("answers zeroes for a look nobody rated", () => {
    const answer = summary(memoryNk(), SERVER_KEY);
    expect(answer).toEqual({
      ok: true,
      looks: {
        basant: { count: 0, averageRating: 0, keepShare: 0, comments: [] },
        pop: { count: 0, averageRating: 0, keepShare: 0, comments: [] },
      },
    });
  });

  it("keeps only the newest 200 comments", () => {
    const answers: LookFeedback[] = Array.from({ length: 250 }, (_, i) => ({
      look: "pop",
      rating: 3,
      keep: true,
      comment: `c${String(i)}`,
      at: T0 + i,
    }));
    const comments = summarise(answers)["pop"]?.comments ?? [];
    expect(comments).toHaveLength(200);
    expect(comments[0]?.comment).toBe("c249");
    expect(comments[199]?.comment).toBe("c50");
  });

  it("is open to the server key and listed owners only", () => {
    const m = memoryNk();
    expect(summary(m, ctxFor("u1"))).toMatchObject({
      ok: false,
      error: { code: "table.not_authorised" },
    });
    expect(summary(m, ctxFor("owner", { HULLA_ADMIN_USER_IDS: "owner" }))).toMatchObject({
      ok: true,
    });
  });
});

describe("the playtest switch", () => {
  it("looks_test is on by default", () => {
    expect(resolveFlag(DEFAULT_CONFIG, "looks_test", { region: "eu", cohort: "*" })).toBe(true);
  });
});
