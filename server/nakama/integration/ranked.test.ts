import { OPCODES, type TableState, type TurnStart } from "@hulla/protocol/generated";
import { afterAll, describe, expect, it } from "vitest";

import { NAKAMA_HOST, rpc, scriptedClient, setTableId, type ScriptedClient } from "./nakama-client";

interface Matched {
  readonly match_id: string;
  readonly token: string;
}

interface RankedCard {
  readonly ok: boolean;
  readonly season: { readonly id: string };
  readonly rating: number;
  readonly league: string | null;
  readonly placement: { readonly played: number; readonly of: number };
  readonly matchmaking: { readonly band: number };
}

function matchedOn(c: ScriptedClient): Promise<Matched> {
  return new Promise<Matched>((resolve, reject) => {
    const timer = setTimeout(() => {
      reject(new Error(`${c.name}: no ranked match in time`));
    }, 60_000);
    c.socket.onmatchmakermatched = (m) => {
      clearTimeout(timer);
      resolve({ match_id: m.match_id, token: m.token });
    };
  });
}

/** What the client sends; the server's hook replaces the rating, band and query. */
async function queue(c: ScriptedClient): Promise<Matched> {
  const matched = matchedOn(c);
  await c.socket.addMatchmaker(
    "+properties.mode:ranked",
    2,
    8,
    { game: "thulla", mode: "ranked" },
    { rating: 9999, band: 150 },
  );
  return matched;
}

describe.skipIf(NAKAMA_HOST === undefined)("Ranked against Nakama (GDD 7.5, R53)", () => {
  const clients: ScriptedClient[] = [];
  afterAll(() => {
    for (const c of clients) c.close();
  });

  it("four solo players queue, play a ranked table to the end and see placement move", async () => {
    const suffix = `${String(Date.now())}r`;
    const four = await Promise.all(
      [0, 1, 2, 3].map((i) => scriptedClient(`rk${String(i)}`, suffix)),
    );
    clients.push(...four);
    const [leader, friend] = four as [ScriptedClient, ScriptedClient];
    const before = await rpc<RankedCard>(leader, "ranked_get", {});
    expect(before).toMatchObject({
      ok: true,
      rating: 1200,
      league: null,
      placement: { played: 0 },
    });

    // A party is refused before it reaches the matchmaker.
    const party = await leader.socket.createParty(true, 2);
    await friend.socket.joinParty(party.party_id);
    await expect(
      leader.socket.addMatchmakerParty(party.party_id, "*", 4, 4, { mode: "ranked" }, {}),
    ).rejects.toBeTruthy();
    await leader.socket.leaveParty(party.party_id);

    const matches = await Promise.all(four.map(queue));
    expect(new Set(matches.map((m) => m.match_id)).size).toBe(1);
    const matchId = matches[0]?.match_id ?? "";
    const states = await Promise.all(
      four.map(async (c, i) => {
        await c.socket.joinMatch(matchId, matches[i]?.token);
        return (await c.next((e) => e.type === "table_state")).payload as TableState;
      }),
    );
    setTableId(`t_${states[0]?.code ?? ""}`);
    expect(states[0]?.mode).toBe("ranked");
    expect(states[0]?.rules.settings).toMatchObject({ cardMemoryAssist: false });

    const start = await leader.next((e) => e.type === "round_start", 30_000);
    const roundId = (start.payload as { roundId: string }).roundId;
    const play = async (c: ScriptedClient): Promise<void> => {
      let sent = 0;
      for (let guard = 0; guard < 200; guard += 1) {
        const next = await c.next(
          (e) =>
            e.type === "round_end" ||
            (e.type === "turn_start" &&
              (e.payload as TurnStart).roundId === roundId &&
              !(e as { handled?: boolean }).handled),
          60_000,
        );
        if (next.type === "round_end") return;
        (next as { handled?: boolean }).handled = true;
        const legal = (next.payload as TurnStart).legal;
        if (legal.length === 0) continue;
        sent += 1;
        await c.send(matchId, "play_card", OPCODES.play_card.opcode, {
          roundId,
          clientSeq: 100 + sent,
          card: legal[0],
        });
      }
    };
    await Promise.all(four.map(play));

    const after = await Promise.all(four.map((c) => rpc<RankedCard>(c, "ranked_get", {})));
    for (const card of after) {
      expect(card.placement).toEqual({ played: 1, of: 10 });
      expect(card.league).toBeNull();
    }
    const ratings = after.map((card) => card.rating);
    expect(ratings.some((r) => r > 1200)).toBe(true);
    expect(ratings.some((r) => r < 1200)).toBe(true);
    const board = await rpc<{ ok: boolean; rows: unknown[] }>(leader, "ranked_board", {});
    expect(board.ok).toBe(true);
  }, 240_000);
});
