import { afterAll, describe, expect, it } from "vitest";

import { NAKAMA_HOST, scriptedClient, setTableId, type ScriptedClient } from "./nakama-client";

import type { TableState } from "@hulla/protocol/generated";

interface Matched {
  readonly match_id: string;
  readonly token: string;
}

const query = (lane: string): string =>
  `+properties.game:thulla +properties.mode:quick +properties.lane:${lane}`;

function at<T>(items: readonly T[], index: number): T {
  const item = items[index];
  if (item === undefined) throw new Error(`no item ${String(index)}`);
  return item;
}

function matchedOn(c: ScriptedClient, label: string): Promise<Matched> {
  return new Promise<Matched>((resolve, reject) => {
    const timer = setTimeout(() => {
      reject(new Error(`${label}: no match in time`));
    }, 60_000);
    c.socket.onmatchmakermatched = (m) => {
      clearTimeout(timer);
      resolve({ match_id: m.match_id, token: m.token });
    };
  });
}

async function search(c: ScriptedClient, lane: string, min: number, max: number): Promise<Matched> {
  const matched = matchedOn(c, c.name);
  await c.socket.addMatchmaker(
    query(lane),
    min,
    max,
    { game: "thulla", mode: "quick", lane },
    { seats: max },
  );
  return matched;
}

async function seated(c: ScriptedClient, matched: Matched): Promise<TableState> {
  await c.socket.joinMatch(matched.match_id, matched.token);
  const state = (await c.next((e) => e.type === "table_state")).payload as TableState;
  setTableId(`t_${state.code}`);
  return state;
}

function tableStates(c: ScriptedClient): TableState[] {
  return c.received.filter((e) => e.type === "table_state").map((e) => e.payload as TableState);
}

describe.skipIf(NAKAMA_HOST === undefined)(
  "Quick Play matchmaking against Nakama (GDD 7.2, 9.6)",
  () => {
    const clients: ScriptedClient[] = [];
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("four searchers fill a 4-seat table that deals itself after the countdown", async () => {
      const suffix = `${String(Date.now())}a`;
      const four = await Promise.all(
        [0, 1, 2, 3].map((i) => scriptedClient(`q${String(i)}`, suffix)),
      );
      clients.push(...four);
      const started = Date.now();
      const matches = await Promise.all(four.map((c) => search(c, `full-${suffix}`, 3, 4)));
      expect(new Set(matches.map((m) => m.match_id)).size).toBe(1);
      const joined = await Promise.all(four.map((c, i) => seated(c, at(matches, i))));
      expect(at(joined, 0).mode).toBe("public");
      const round = await at(four, 0).next((e) => e.type === "round_start", 20_000);
      expect(round.type).toBe("round_start");
      const states = tableStates(at(four, 0));
      expect(states.some((st) => st.autoStart !== undefined)).toBe(true);
      expect(Date.now() - started).toBeLessThan(30_000);
      expect(at(states, states.length - 1).seats.filter((s) => s.playerId !== null)).toHaveLength(
        4,
      );
    }, 90_000);

    it("three searchers start at the 20 s mark with a countdown", async () => {
      const suffix = `${String(Date.now())}b`;
      const three = await Promise.all(
        [0, 1, 2].map((i) => scriptedClient(`t${String(i)}`, suffix)),
      );
      clients.push(...three);
      const started = Date.now();
      const matches = await Promise.all(three.map((c) => search(c, `short-${suffix}`, 3, 4)));
      expect(Date.now() - started).toBeGreaterThan(10_000);
      expect(new Set(matches.map((m) => m.match_id)).size).toBe(1);
      await Promise.all(three.map((c, i) => seated(c, at(matches, i))));
      const round = await at(three, 0).next((e) => e.type === "round_start", 40_000);
      expect((round.payload as { seatCount: number }).seatCount).toBe(3);
    }, 120_000);

    it("a party of two lands at the same table, flagged, next to two singles", async () => {
      const suffix = `${String(Date.now())}c`;
      const [leader, friend, s1, s2] = await Promise.all(
        ["pl", "pf", "ps1", "ps2"].map((n) => scriptedClient(n, suffix)),
      );
      if (leader === undefined || friend === undefined || s1 === undefined || s2 === undefined) {
        throw new Error("clients");
      }
      clients.push(leader, friend, s1, s2);
      const party = await leader.socket.createParty(true, 2);
      await friend.socket.joinParty(party.party_id);
      const lane = `party-${suffix}`;
      const partyMatched = matchedOn(leader, "party leader");
      const friendMatched = matchedOn(friend, "party friend");
      await leader.socket.addMatchmakerParty(
        party.party_id,
        query(lane),
        3,
        4,
        { game: "thulla", mode: "quick", lane },
        { seats: 4 },
      );
      const singles = await Promise.all([s1, s2].map((c) => search(c, lane, 3, 4)));
      const pm = await partyMatched;
      const fm = await friendMatched;
      expect(new Set([pm.match_id, fm.match_id, ...singles.map((m) => m.match_id)]).size).toBe(1);
      await Promise.all([
        seated(leader, pm),
        seated(friend, fm),
        seated(s1, at(singles, 0)),
        seated(s2, at(singles, 1)),
      ]);
      await leader.next((e) => e.type === "round_start", 20_000);
      const states = tableStates(leader);
      const partySeats = at(states, states.length - 1).seats.filter(
        (s) => s.party !== null && s.party !== undefined,
      );
      expect(partySeats).toHaveLength(2);
      expect(new Set(partySeats.map((s) => s.party)).size).toBe(1);
      expect(partySeats.map((s) => s.playerId).sort()).toEqual(
        [leader.session.user_id, friend.session.user_id].sort(),
      );
    }, 90_000);
  },
);
