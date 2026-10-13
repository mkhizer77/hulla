import { describe, expect, it } from "vitest";

import { EMPTY_STATS, type Profile } from "../services/profiles";
import {
  addFriendByCode,
  FAVOURITE_CAP,
  FRIEND_CAP,
  normaliseFriendCode,
  RECENT_CAP,
  recordRecentPlayers,
  setFavourite,
  setMute,
  socialOverview,
  type FriendPorts,
  type RecentPlayer,
  type SocialStore,
} from "../services/social";

const T0 = 1_760_200_000_000;

function store(): SocialStore & {
  readonly fav: Map<string, readonly string[]>;
  readonly rec: Map<string, readonly RecentPlayer[]>;
} {
  const fav = new Map<string, readonly string[]>();
  const rec = new Map<string, readonly RecentPlayer[]>();
  const mut = new Map<string, readonly string[]>();
  const by = new Map<string, readonly string[]>();
  return {
    fav,
    rec,
    favouritedBy: (u) => by.get(u) ?? [],
    writeFavouritedBy: (u, ids) => void by.set(u, ids),
    userIdForFriendCode: (code) => (code === "AB72CD34" ? "u2" : code === "ZZ99ZZ99" ? "u1" : null),
    favourites: (u) => fav.get(u) ?? [],
    writeFavourites: (u, ids) => void fav.set(u, ids),
    recent: (u) => rec.get(u) ?? [],
    writeRecent: (u, p) => void rec.set(u, p),
    mutes: (u) => mut.get(u) ?? [],
    writeMutes: (u, ids) => void mut.set(u, ids),
  };
}

function ports(count = 0, friends: string[] = []): FriendPorts & { readonly added: string[] } {
  const added: string[] = [];
  return {
    added,
    friendCount: () => count,
    isFriend: (_u, other) => friends.includes(other),
    addFriend: (_u, other) => void added.push(other),
  };
}

describe("friends by code, favourites and recent players (GDD 10.3, 10.6)", () => {
  it("adds by friend code, never yourself, and stops at the cap", () => {
    const s = store();
    const p = ports();
    expect(addFriendByCode(s, p, "u1", "ab72cd34")).toEqual({ ok: true, userId: "u2" });
    expect(p.added).toEqual(["u2"]);
    expect(addFriendByCode(s, p, "u1", "ZZ99ZZ99")).toMatchObject({
      error: { code: "social.self" },
    });
    expect(addFriendByCode(s, p, "u1", "NOPE1234")).toMatchObject({
      error: { code: "profile.not_found" },
    });
    expect(addFriendByCode(s, p, "u1", "short")).toMatchObject({
      error: { code: "profile.not_found" },
    });
    expect(normaliseFriendCode(" ab72-cd34 ")).toBe("AB72CD34");
    expect(normaliseFriendCode("AB72CD3O")).toBeUndefined();
    expect(addFriendByCode(s, ports(FRIEND_CAP), "u1", "AB72CD34")).toMatchObject({
      error: { code: "social.friend_cap", detail: { cap: 500 } },
    });
    expect(addFriendByCode(s, p, null, "AB72CD34")).toMatchObject({
      error: { code: "table.not_authorised" },
    });
  });

  it("pins only mutual friends, ten at most, and unpins quietly", () => {
    const s = store();
    const friends = Array.from({ length: 12 }, (_, i) => `f${String(i)}`);
    const p = ports(12, friends);
    expect(setFavourite(s, p, "u1", "stranger", true)).toMatchObject({
      error: { code: "social.not_friends" },
    });
    for (let i = 0; i < FAVOURITE_CAP; i += 1) {
      expect(setFavourite(s, p, "u1", `f${String(i)}`, true)).toMatchObject({ ok: true });
    }
    expect(setFavourite(s, p, "u1", "f10", true)).toMatchObject({
      error: { code: "social.favourite_cap", detail: { cap: 10 } },
    });
    expect(setFavourite(s, p, "u1", "f0", true)).toMatchObject({
      ok: true,
      favourites: expect.arrayContaining(["f0"]) as string[],
    });
    const unpinned = setFavourite(s, p, "u1", "f0", false);
    expect(unpinned).toMatchObject({ ok: true });
    if (unpinned.ok) expect(unpinned.favourites).toHaveLength(9);
    expect(setFavourite(s, p, "u1", "f10", true)).toMatchObject({ ok: true });
  });

  it("records the last fifty co-players, most recent first, with round counts", () => {
    const s = store();
    recordRecentPlayers(s, ["u1", "u2", "u3"], T0);
    recordRecentPlayers(s, ["u1", "u2", "u4", "u4"], T0 + 1000);
    expect(s.recent("u1")).toEqual([
      { userId: "u2", lastPlayedTs: T0 + 1000, rounds: 2 },
      { userId: "u4", lastPlayedTs: T0 + 1000, rounds: 1 },
      { userId: "u3", lastPlayedTs: T0, rounds: 1 },
    ]);
    expect(s.recent("u3")).toEqual([
      { userId: "u1", lastPlayedTs: T0, rounds: 1 },
      { userId: "u2", lastPlayedTs: T0, rounds: 1 },
    ]);
    for (let i = 0; i < 60; i += 1) recordRecentPlayers(s, ["u1", `x${String(i)}`], T0 + 2000 + i);
    expect(s.recent("u1")).toHaveLength(RECENT_CAP);
    expect(s.recent("u1")[0]?.userId).toBe("x59");
  });

  it("builds cards with friend and favourite flags from the profiles", () => {
    const s = store();
    recordRecentPlayers(s, ["u1", "u2", "u3"], T0);
    s.writeFavourites("u1", ["u2"]);
    const profiles = {
      read: (u: string): Profile | null =>
        u === "u2"
          ? {
              displayName: "Sara",
              handle: "sara_k",
              friendCode: "X",
              avatar: "a03",
              frame: "f00",
              flag: null,
              title: null,
              bio: "",
              stats: EMPTY_STATS,
              privacy: { showOnline: true, visibility: "everyone" },
              nameChanges: 0,
              createdTs: T0,
              updatedTs: T0,
            }
          : null,
    };
    const overview = socialOverview(s, ports(1, ["u2"]), profiles, "u1");
    expect(overview.favourites).toEqual(["u2"]);
    expect(overview.recent).toEqual([
      {
        userId: "u2",
        displayName: "Sara",
        handle: "sara_k",
        avatar: "a03",
        lastPlayedTs: T0,
        rounds: 1,
        friend: true,
        favourite: true,
      },
      {
        userId: "u3",
        displayName: null,
        handle: null,
        avatar: null,
        lastPlayedTs: T0,
        rounds: 1,
        friend: false,
        favourite: false,
      },
    ]);
  });

  it("mutes and unmutes a player, never yourself", () => {
    const s = store();
    expect(setMute(s, "u1", "u2", true)).toEqual({ ok: true, mutes: ["u2"] });
    expect(setMute(s, "u1", "u2", true)).toEqual({ ok: true, mutes: ["u2"] });
    expect(setMute(s, "u1", "u3", true)).toEqual({ ok: true, mutes: ["u2", "u3"] });
    expect(setMute(s, "u1", "u2", false)).toEqual({ ok: true, mutes: ["u3"] });
    expect(setMute(s, "u1", "u1", true)).toMatchObject({
      error: { code: "table.invalid_payload" },
    });
    expect(setMute(s, null, "u2", true)).toMatchObject({ error: { code: "table.not_authorised" } });
  });
});
