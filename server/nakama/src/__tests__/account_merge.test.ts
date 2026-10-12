import { stateFromSeed, Xoshiro128StarStar } from "@hulla/shared-ts";
import { describe, expect, it } from "vitest";

import {
  addStats,
  MERGE_CODE_TTL_MS,
  mergeProfiles,
  prepareMerge,
  redeemMerge,
  walletChangeset,
  type MergeCode,
  type MergeCodeStore,
  type MergePorts,
} from "../services/account_merge";
import { EMPTY_STATS, type Profile } from "../services/profiles";

const T0 = 1_760_200_000_000;

function profile(over: Partial<Profile>): Profile {
  return {
    displayName: "Guest AB12",
    handle: "h",
    friendCode: "AB12CD34",
    avatar: "a01",
    frame: "f00",
    flag: null,
    title: null,
    bio: "",
    stats: EMPTY_STATS,
    privacy: { showOnline: true, visibility: "everyone" },
    nameChanges: 0,
    createdTs: T0,
    updatedTs: T0,
    ...over,
  };
}

function codes(): MergeCodeStore & { readonly map: Map<string, MergeCode> } {
  const map = new Map<string, MergeCode>();
  return {
    map,
    read: (c) => map.get(c) ?? null,
    write(c, v) {
      if (map.has(c)) return false;
      map.set(c, v);
      return true;
    },
    remove: (c) => void map.delete(c),
  };
}

function ports(
  store = codes(),
): MergePorts & { readonly log: string[]; readonly profiles: Map<string, Profile> } {
  const profiles = new Map<string, Profile>([
    [
      "A",
      profile({
        displayName: "Ali Khan",
        avatar: "a05",
        flag: "PK",
        bio: "chai",
        stats: {
          ...EMPTY_STATS,
          rounds: 4,
          escapes: 3,
          currentStreak: 2,
          bestStreak: 3,
          suits: { S: 5, H: 1, C: 0, D: 2 },
        },
        nameChanges: 1,
      }),
    ],
    [
      "B",
      profile({
        displayName: "Guest ZZ99",
        handle: "b_handle",
        friendCode: "ZZ99ZZ99",
        stats: {
          ...EMPTY_STATS,
          rounds: 1,
          bhabhis: 1,
          bestStreak: 1,
          suits: { S: 1, H: 1, C: 1, D: 1 },
        },
      }),
    ],
  ]);
  const log: string[] = [];
  return {
    codes: store,
    log,
    profiles,
    readProfile: (u) => profiles.get(u) ?? null,
    writeProfile: (u, p) => void profiles.set(u, p),
    readWallet: (u) => (u === "A" ? { coins: 120, gems: 3, debt: -5 } : {}),
    creditWallet: (u, c, from) => void log.push(`credit ${u} ${JSON.stringify(c)} from ${from}`),
    moveRounds: (from, to) => (log.push(`move ${from}->${to}`), 4),
    deleteAccount: (u) => void log.push(`delete ${u}`),
  };
}

describe("guest merge into a linked account (GDD 10.2)", () => {
  it("adds stats, keeps the larger streak and name-change count, and the linked identity", () => {
    const a = profile({
      stats: {
        ...EMPTY_STATS,
        rounds: 4,
        escapes: 3,
        currentStreak: 2,
        bestStreak: 3,
        biggestPickup: 7,
      },
    });
    const b = profile({
      stats: {
        ...EMPTY_STATS,
        rounds: 1,
        bhabhis: 1,
        currentStreak: 0,
        bestStreak: 1,
        biggestPickup: 9,
      },
    });
    expect(addStats(a.stats, b.stats)).toMatchObject({
      rounds: 5,
      escapes: 3,
      bhabhis: 1,
      currentStreak: 2,
      bestStreak: 3,
      biggestPickup: 9,
    });
    const p = ports();
    const merged = mergeProfiles(p.profiles.get("B")!, p.profiles.get("A")!, T0 + 1);
    expect(merged).toMatchObject({
      displayName: "Ali Khan",
      handle: "b_handle",
      friendCode: "ZZ99ZZ99",
      avatar: "a05",
      flag: "PK",
      bio: "chai",
      nameChanges: 1,
      updatedTs: T0 + 1,
    });
    expect(merged.stats).toMatchObject({
      rounds: 5,
      escapes: 3,
      bhabhis: 1,
      suits: { S: 6, H: 2, C: 1, D: 3 },
    });
    const named = mergeProfiles(
      profile({ displayName: "Sara" }),
      profile({ displayName: "Ali Khan" }),
      T0,
    );
    expect(named.displayName).toBe("Sara");
    expect(walletChangeset({ coins: 120, gems: 3, debt: -5, odd: Number.NaN })).toEqual({
      coins: 120,
      gems: 3,
    });
  });

  it("issues a one-time code as the guest and redeems it as the linked account", () => {
    const store = codes();
    const rng = Xoshiro128StarStar.fromState(stateFromSeed(new Uint8Array([1])));
    expect(prepareMerge(store, rng, null, T0)).toMatchObject({ ok: false });
    const prepared = prepareMerge(store, rng, "A", T0);
    expect(prepared.ok).toBe(true);
    if (!prepared.ok) return;
    expect(prepared.code).toMatch(/^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$/);
    expect(prepared.expiresTs).toBe(T0 + MERGE_CODE_TTL_MS);
    const p = ports(store);
    expect(redeemMerge(p, "A", prepared.code, T0 + 1)).toMatchObject({
      error: { code: "auth.merge_code_invalid", detail: { reason: "same_account" } },
    });
    const again = prepareMerge(store, rng, "A", T0);
    if (!again.ok) throw new Error("prepare");
    const merged = redeemMerge(p, "B", again.code.toLowerCase(), T0 + 2);
    expect(merged).toMatchObject({
      ok: true,
      mergedFrom: "A",
      rounds: 4,
      profile: { displayName: "Ali Khan", stats: { rounds: 5 } },
    });
    expect(p.log).toEqual(['credit B {"coins":120,"gems":3} from A', "move A->B", "delete A"]);
    expect(redeemMerge(p, "B", again.code, T0 + 3)).toMatchObject({
      error: { code: "auth.merge_code_invalid" },
    });
    const stale = prepareMerge(store, rng, "A", T0);
    if (!stale.ok) throw new Error("prepare");
    expect(redeemMerge(p, "B", stale.code, T0 + MERGE_CODE_TTL_MS)).toMatchObject({
      error: { code: "auth.merge_code_invalid" },
    });
    expect(store.map.has(stale.code)).toBe(false);
  });
});
