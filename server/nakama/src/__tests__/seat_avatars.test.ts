import { describe, expect, it } from "vitest";

import { AVATAR_IDS, avatarForAccount } from "../content/avatars";
import { thullaMatchInit, thullaMatchJoin, thullaMatchJoinAttempt } from "../games/thulla/match";
import { fillSeatAvatars, pickSeatAvatar } from "../match/avatars";
import { emptySlot } from "../match/lobby";
import { bootstrapProfile, nkProfileStore } from "../services/profiles";

import { fakeDispatcher, nkPresence } from "./fake-match";
import { fakeLogger, memoryNk } from "./fake-nk";

import type { SeatSlot } from "../match/types";

const log = fakeLogger();
const ctx = { matchId: "m1.n1", env: {} } as unknown as nkruntime.Context;
const T0 = Date.UTC(2026, 9, 13, 12);
const rng = { nextUint32: () => 7, nextInt: (n: number) => 7 % n };

function seatedSlots(avatars: readonly (string | null)[]): SeatSlot[] {
  return avatars.map((avatar, i) => ({ ...emptySlot(), playerId: `u${String(i)}`, avatar }));
}

describe("seat avatars (GDD 15.6)", () => {
  it("picks the player's own avatar unless another seat shows it, then the first free one", () => {
    expect(pickSeatAvatar(seatedSlots([null, null, null]), 0, "a03")).toBe("a03");
    expect(pickSeatAvatar(seatedSlots(["a03", null, null]), 1, "a03")).toBe("a01");
    expect(pickSeatAvatar(seatedSlots(["a01", "a03", null]), 2, "a03")).toBe("a02");
    expect(pickSeatAvatar(seatedSlots(["a01", "a02", null]), 2, null)).toBe("a03");
  });

  it("fills only the seats that have none, in seat order, and reports whether anything changed", () => {
    const seats = seatedSlots(["a02", null, null]);
    seats[1]!.playerId = null;
    const state = { seats } as unknown as Parameters<typeof fillSeatAvatars>[0];
    expect(fillSeatAvatars(state, () => "a02")).toBe(true);
    expect(seats.map((s) => s.avatar)).toEqual(["a02", null, "a01"]);
    expect(fillSeatAvatars(state, () => "a02")).toBe(false);
  });

  it("spreads new accounts across the set instead of giving everyone the first avatar", () => {
    const picks = new Set(["u1", "u2", "u3", "u4", "u5", "u6", "u7", "u8"].map(avatarForAccount));
    expect(picks.size).toBeGreaterThan(1);
    for (const id of picks) expect(AVATAR_IDS).toContain(id);
  });

  it("gives two players who chose the same avatar different ones at the table, and keeps them on reconnect", () => {
    const m = memoryNk().nk;
    const store = nkProfileStore(m, log);
    for (const userId of ["u1", "u2", "u3"]) {
      const profile = bootstrapProfile(store, rng, userId, `user_${userId}`, T0);
      store.write(userId, { ...profile, avatar: userId === "u3" ? "a05" : "a01" });
    }
    const d = fakeDispatcher();
    let state = thullaMatchInit(ctx, log, m, {
      code: "ABCDEF",
      gameId: "thulla",
      mode: "private",
    }).state;
    const presences = ["u1", "u2", "u3"].map((userId, i) => ({ ...nkPresence(i), userId }));
    for (const presence of presences) {
      state =
        thullaMatchJoinAttempt(ctx, log, m, d.dispatcher, 1, state, presence, {})?.state ?? state;
      state = thullaMatchJoin(ctx, log, m, d.dispatcher, 1, state, [presence])?.state ?? state;
    }
    expect(state.seats.slice(0, 3).map((s) => s.avatar)).toEqual(["a01", "a02", "a05"]);

    const sent = d.broadcasts.filter((b) => b.envelope.type === "table_state");
    const last = sent[sent.length - 1]?.envelope.payload as { seats: { avatar: string | null }[] };
    expect(last.seats.slice(0, 3).map((s) => s.avatar)).toEqual(["a01", "a02", "a05"]);

    // u2 comes back on a new session with the resume token: the seat keeps its avatar.
    const again = { ...presences[1]!, sessionId: "s-again" };
    const resume = { resumeToken: state.seats[1]?.resumeToken ?? "" };
    state =
      thullaMatchJoinAttempt(ctx, log, m, d.dispatcher, 2, state, again, resume)?.state ?? state;
    state = thullaMatchJoin(ctx, log, m, d.dispatcher, 2, state, [again])?.state ?? state;
    expect(state.seats[1]?.avatar).toBe("a02");
  });
});
