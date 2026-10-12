import { describe, expect, it } from "vitest";

import {
  EMPTY_PRESENCE,
  listFriendPresence,
  parseLabel,
  seatReleased,
  seatTaken,
  setAway,
  touch,
  type PresencePorts,
  type PresenceRecord,
  type PresenceStore,
  type TableLabel,
} from "../services/presence";
import { EMPTY_STATS, type Profile } from "../services/profiles";

const T0 = 1_760_200_000_000;

function memoryStore(): PresenceStore & { readonly writes: string[] } {
  const records = new Map<string, PresenceRecord>();
  const writes: string[] = [];
  return {
    writes,
    read: (u) => records.get(u) ?? EMPTY_PRESENCE,
    readMany: (ids) => {
      const found = new Map<string, PresenceRecord>();
      for (const id of ids) {
        const record = records.get(id);
        if (record !== undefined) found.set(id, record);
      }
      return found;
    },
    write: (u, r) => {
      writes.push(u);
      records.set(u, r);
    },
  };
}

function profile(name: string, showOnline = true): Profile {
  return {
    displayName: name,
    handle: name.toLowerCase(),
    friendCode: "X",
    avatar: "a01",
    frame: "f00",
    flag: null,
    title: null,
    bio: "",
    stats: EMPTY_STATS,
    privacy: { showOnline, visibility: "everyone" },
    nameChanges: 0,
    createdTs: T0,
    updatedTs: T0,
  };
}

const lobby: TableLabel = {
  code: "ABCDEF",
  gameId: "thulla",
  mode: "private",
  status: "open",
  seated: 2,
  open: 4,
  watching: 0,
  watchable: true,
};
const inRound: TableLabel = { ...lobby, code: "GHJKLM", status: "inRound", open: 0 };

function ports(overrides: Partial<PresencePorts> = {}): PresencePorts {
  return {
    friendIds: () => ["ali", "sara", "bilal", "noor", "zed", "hid"],
    onlineIds: () => new Set(["ali", "sara", "bilal", "hid"]),
    profiles: () =>
      new Map([
        ["ali", profile("Ali")],
        ["sara", profile("Sara")],
        ["bilal", profile("Bilal")],
        ["noor", profile("Noor")],
        ["hid", profile("Hidden", false)],
      ]),
    label: (matchId) => (matchId === "m-lobby" ? lobby : matchId === "m-round" ? inRound : null),
    favourites: () => ["sara"],
    ...overrides,
  };
}

describe("presence (GDD 10.4) and the friends list for Home (GDD 14.10)", () => {
  it("tracks seats, stays quiet on repeats and releases only the table it was told", () => {
    const store = memoryStore();
    seatTaken(store, "ali", "m-lobby", T0);
    seatTaken(store, "ali", "m-lobby", T0 + 1);
    expect(store.writes).toEqual(["ali"]);
    expect(store.read("ali")).toEqual({
      table: { matchId: "m-lobby", joinedTs: T0 },
      away: false,
      lastSeenTs: T0,
    });
    seatReleased(store, "ali", "other", T0 + 2);
    expect(store.read("ali").table).not.toBeNull();
    seatReleased(store, "ali", "m-lobby", T0 + 3);
    expect(store.read("ali")).toEqual({ table: null, away: false, lastSeenTs: T0 + 3 });
    touch(store, "ali", T0 + 4);
    expect(store.read("ali").lastSeenTs).toBe(T0 + 4);
    expect(setAway(store, "ali", true, T0 + 5)).toEqual({ ok: true, away: true });
    expect(store.read("ali").away).toBe(true);
    expect(setAway(store, null, true, T0)).toMatchObject({
      error: { code: "table.not_authorised" },
    });
  });

  it("reads the match label and tolerates junk", () => {
    expect(parseLabel(JSON.stringify(lobby))).toEqual(lobby);
    expect(parseLabel("{")).toBeNull();
    expect(parseLabel('{"status":"open"}')).toBeNull();
    expect(parseLabel('{"code":"ABCDEF","status":"open"}')).toMatchObject({ open: 0, seated: 0 });
  });

  it("orders joinable lobbies first, then in round, online, away, offline with last seen, hidden last", () => {
    const store = memoryStore();
    seatTaken(store, "ali", "m-lobby", T0);
    seatTaken(store, "sara", "m-round", T0);
    setAway(store, "bilal", true, T0);
    touch(store, "noor", T0 - 5000);
    seatTaken(store, "hid", "m-lobby", T0);
    const friends = listFriendPresence(store, ports(), "me");
    expect(friends.map((f) => [f.userId, f.state])).toEqual([
      ["ali", "lobby"],
      ["sara", "round"],
      ["bilal", "away"],
      ["noor", "offline"],
      ["zed", "offline"],
      ["hid", "hidden"],
    ]);
    expect(friends[0]?.table).toEqual({ ...lobby, matchId: "m-lobby", joinable: true });
    expect(friends[1]?.table).toEqual({ ...inRound, matchId: "m-round", joinable: false });
    expect(friends[1]?.favourite).toBe(true);
    expect(friends[3]).toMatchObject({ displayName: "Noor", lastSeenTs: T0 - 5000, table: null });
    expect(friends[4]).toMatchObject({ displayName: null, lastSeenTs: null });
    expect(friends[5]).toMatchObject({ table: null, lastSeenTs: null });
  });

  it("drops a table pointer whose match is gone and ranks fuller lobbies lower", () => {
    const store = memoryStore();
    seatTaken(store, "ali", "m-gone", T0);
    seatTaken(store, "sara", "m-lobby", T0);
    seatTaken(store, "bilal", "m-two", T0);
    const friends = listFriendPresence(
      store,
      ports({
        friendIds: () => ["ali", "sara", "bilal"],
        onlineIds: () => new Set(["ali", "sara", "bilal"]),
        label: (id) =>
          id === "m-lobby" ? lobby : id === "m-two" ? { ...lobby, code: "TWOTWO", open: 2 } : null,
      }),
      "me",
    );
    expect(friends.map((f) => [f.userId, f.state, f.table?.open ?? null])).toEqual([
      ["sara", "lobby", 4],
      ["bilal", "lobby", 2],
      ["ali", "online", null],
    ]);
    expect(listFriendPresence(store, ports({ friendIds: () => [] }), "me")).toEqual([]);
  });
});
