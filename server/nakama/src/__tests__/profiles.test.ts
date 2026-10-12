import { stateFromSeed, Xoshiro128StarStar } from "@hulla/shared-ts";
import { describe, expect, it } from "vitest";

import { thullaHighlights } from "../games/thulla/host";
import { profileGet, profileUpdate, type ProfileDeps } from "../rpc/profile";
import {
  applyProfileUpdate,
  applyRoundToStats,
  bootstrapProfile,
  EMPTY_STATS,
  normaliseDisplayName,
  profileView,
  type Profile,
  type ProfileStore,
} from "../services/profiles";

import { fakeRuntime } from "./fake-nk";

import type { LoggedMessage, RoundRecord } from "../match/types";

const T0 = 1_760_200_000_000;

function memoryStore(): ProfileStore & {
  readonly profiles: Map<string, Profile>;
  readonly codes: Map<string, string>;
} {
  const profiles = new Map<string, Profile>();
  const codes = new Map<string, string>();
  return {
    profiles,
    codes,
    read: (u) => profiles.get(u) ?? null,
    write: (u, p) => void profiles.set(u, p),
    claimFriendCode(code, u) {
      if (codes.has(code)) return false;
      codes.set(code, u);
      return true;
    },
  };
}

const rng = (): Xoshiro128StarStar =>
  Xoshiro128StarStar.fromState(stateFromSeed(new Uint8Array([7, 7, 7])));

function deps(
  store = memoryStore(),
  userId: string | null = "u1",
): ProfileDeps & { readonly names: unknown[]; taken: Set<string> } {
  const names: unknown[] = [];
  const taken = new Set<string>();
  return {
    runtime: fakeRuntime({ now: T0 }),
    userId,
    store,
    names,
    taken,
    relation: (viewer, target) => (viewer === "u1" && target === "u2" ? "friend" : "other"),
    isOnline: (u) => u === "u2",
    userIdForHandle: (handle) => (handle === "sara_k" ? "u2" : null),
    setAccountNames(u, handle, displayName) {
      names.push({ u, handle, displayName });
      return handle === null || !taken.has(handle);
    },
  };
}

describe("profiles (GDD 10.1)", () => {
  it("bootstraps a guest profile once, with a unique 8-character friend code", () => {
    const store = memoryStore();
    const first = bootstrapProfile(store, rng(), "u1", "nakama_name", T0);
    expect(first.friendCode).toMatch(/^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$/);
    expect(first.displayName).toBe(`Guest ${first.friendCode.slice(0, 4)}`);
    expect(first).toMatchObject({
      handle: "nakama_name",
      avatar: "a01",
      frame: "f00",
      stats: EMPTY_STATS,
      nameChanges: 0,
    });
    expect(bootstrapProfile(store, rng(), "u1", "other", T0 + 1)).toBe(first);
    store.codes.set("TAKEN123", "u9");
    const second = bootstrapProfile(store, rng(), "u2", "n2", T0);
    expect(second.friendCode).not.toBe(first.friendCode);
  });

  it("accepts names in any script, two to twenty code points, and refuses control characters", () => {
    expect(normaliseDisplayName("  Ali  Khan ")).toBe("Ali Khan");
    expect(normaliseDisplayName("علی خان")).toBe("علی خان");
    expect(normaliseDisplayName("अली")).toBe("अली");
    expect(normaliseDisplayName("আলী খান")).toBe("আলী খান");
    expect(normaliseDisplayName("A")).toBeNull();
    expect(normaliseDisplayName("x".repeat(21))).toBeNull();
    expect(normaliseDisplayName("bad\u0000name")).toBeNull();
    expect(normaliseDisplayName("line\nbreak")).toBe("line break");
  });

  it("allows one free name change, validates handles, avatars, bio and privacy", () => {
    const store = memoryStore();
    const base = bootstrapProfile(store, rng(), "u1", "n1", T0);
    const first = applyProfileUpdate(
      base,
      {
        displayName: "Sara",
        handle: "sara_k",
        avatar: "a03",
        frame: "f01",
        flag: "PK",
        bio: " chai first ",
        privacy: { showOnline: false, visibility: "friends" },
      },
      T0 + 5,
    );
    expect(first.ok).toBe(true);
    if (!first.ok) return;
    expect(first.value).toMatchObject({
      displayName: "Sara",
      handle: "sara_k",
      avatar: "a03",
      frame: "f01",
      flag: "PK",
      bio: "chai first",
      nameChanges: 1,
      updatedTs: T0 + 5,
      privacy: { showOnline: false, visibility: "friends" },
    });
    expect(applyProfileUpdate(first.value, { displayName: "Sara" }, T0)).toMatchObject({
      ok: true,
    });
    expect(applyProfileUpdate(first.value, { displayName: "Sarah" }, T0)).toMatchObject({
      error: { code: "profile.name_change_locked" },
    });
    expect(applyProfileUpdate(base, { displayName: "x" }, T0)).toMatchObject({
      error: { code: "profile.invalid_name" },
    });
    expect(applyProfileUpdate(base, { handle: "no spaces" }, T0)).toMatchObject({
      error: { code: "profile.invalid_handle" },
    });
    expect(applyProfileUpdate(base, { avatar: "zzz" }, T0)).toMatchObject({
      error: { code: "profile.unknown_avatar" },
    });
    expect(applyProfileUpdate(base, { bio: "x".repeat(61) }, T0)).toMatchObject({
      error: { code: "profile.invalid_name" },
    });
  });

  it("shows the owner everything, friends the code, strangers the card, and hides friends-only profiles", () => {
    const store = memoryStore();
    const p = bootstrapProfile(store, rng(), "u1", "n1", T0);
    const own = profileView("u1", p, "self", false);
    expect(own).toMatchObject({
      friendCode: p.friendCode,
      privacy: p.privacy,
      nameChanges: 0,
      online: false,
    });
    const friend = profileView("u1", p, "friend", true);
    expect(friend?.friendCode).toBe(p.friendCode);
    expect(friend?.privacy).toBeUndefined();
    const stranger = profileView("u1", p, "other", true);
    expect(stranger?.friendCode).toBeUndefined();
    expect(stranger?.online).toBe(true);
    const hidden = { ...p, privacy: { showOnline: false, visibility: "friends" as const } };
    expect(profileView("u1", hidden, "other", true)).toBeNull();
    expect(profileView("u1", hidden, "friend", true)?.online).toBeUndefined();
  });

  it("serves and edits profiles through the RPCs with catalogue errors", () => {
    const store = memoryStore();
    bootstrapProfile(store, rng(), "u1", "n1", T0);
    bootstrapProfile(store, rng(), "u2", "sara_k", T0);
    bootstrapProfile(store, rng(), "u3", "n3", T0);
    const d = deps(store);
    expect(profileGet(d, "")).toMatchObject({
      ok: true,
      profile: { userId: "u1", friendCode: expect.any(String) as string },
    });
    expect(profileGet(d, JSON.stringify({ handle: "sara_k" }))).toMatchObject({
      ok: true,
      profile: { userId: "u2", online: true },
    });
    expect(profileGet(d, JSON.stringify({ handle: "nobody" }))).toMatchObject({
      error: { code: "profile.not_found" },
    });
    store.write("u3", {
      ...store.read("u3")!,
      privacy: { showOnline: true, visibility: "friends" },
    });
    expect(profileGet(d, JSON.stringify({ userId: "u3" }))).toMatchObject({
      error: { code: "profile.not_visible" },
    });
    expect(profileGet(d, "{")).toMatchObject({ error: { detail: { reason: "schema" } } });
    expect(profileGet(deps(store, null), "")).toMatchObject({
      error: { code: "table.not_authorised" },
    });

    const updated = profileUpdate(d, JSON.stringify({ displayName: "Ali", handle: "ali_k" }));
    expect(updated).toMatchObject({
      ok: true,
      profile: { displayName: "Ali", handle: "ali_k", nameChanges: 1 },
    });
    expect(d.names).toEqual([{ u: "u1", handle: "ali_k", displayName: "Ali" }]);
    d.taken.add("sara_k");
    expect(profileUpdate(d, JSON.stringify({ handle: "sara_k" }))).toMatchObject({
      error: { code: "profile.handle_taken" },
    });
    expect(store.read("u1")?.handle).toBe("ali_k");
    expect(profileUpdate(d, JSON.stringify({ extra: 1 }))).toMatchObject({
      error: { detail: { reason: "schema" } },
    });
  });

  it("builds the stats card from a round: escapes, streaks, thullas, pick-ups and suits", () => {
    const msg = (type: string, payload: object): LoggedMessage => ({
      seq: 1,
      ts: T0,
      type,
      payload,
      seat: null,
      stateHash: null,
    });
    const log = [
      msg("move_applied", { seat: 0, move: { type: "play_card", card: "AS" } }),
      msg("move_applied", { seat: 1, move: { type: "play_card", card: "KH" } }),
      msg("move_applied", { seat: 2, move: { type: "play_card", card: "2S" } }),
      msg("trick_resolved", { thullaBy: 1, picker: 0, cards: ["AS", "KH", "2S"] }),
      msg("pickup", { seat: 0, cards: ["AS", "KH", "2S"] }),
      msg("move_applied", { seat: 2, move: { type: "take_hand" } }),
      msg("pickup", { seat: 0, cards: ["3D", "4D", "5D", "6D", "7D"] }),
    ];
    const highlights = thullaHighlights(log, 3);
    expect(highlights[0]).toMatchObject({
      thullasReceived: 1,
      cardsPickedUp: 8,
      biggestPickup: 5,
      playedS: 1,
    });
    expect(highlights[1]).toMatchObject({ thullasGiven: 1, playedH: 1 });
    expect(highlights[2]).toMatchObject({ handsTaken: 1, playedS: 1 });
    const record: RoundRecord = {
      tableId: "t_A",
      roundId: "r_1",
      gameId: "thulla",
      rulesVersion: "1",
      rules: { gameId: "thulla" },
      mode: "private",
      dealerSeat: 0,
      seedHex: "00",
      players: ["u0", "u1", "u2"],
      moves: [],
      result: { gameId: "thulla", finishOrder: [1, 2, 0], loserSeat: 0, points: [-1, 3, 2] },
      series: null,
      finalStateHash: "x",
      highlights,
      startedTs: T0,
      endedTs: T0,
      completed: true,
    };
    const loser = applyRoundToStats({ ...EMPTY_STATS, currentStreak: 2, bestStreak: 2 }, 0, record);
    expect(loser).toMatchObject({
      rounds: 1,
      bhabhis: 1,
      escapes: 0,
      currentStreak: 0,
      bestStreak: 2,
      biggestPickup: 5,
      suits: { S: 1 },
    });
    const winner = applyRoundToStats(
      { ...EMPTY_STATS, currentStreak: 2, bestStreak: 2 },
      1,
      record,
    );
    expect(winner).toMatchObject({
      rounds: 1,
      escapes: 1,
      currentStreak: 3,
      bestStreak: 3,
      thullasGiven: 1,
      suits: { H: 1 },
    });
  });
});
