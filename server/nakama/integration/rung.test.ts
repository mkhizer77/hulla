import { OPCODES, type TableState, type TurnStart } from "@hulla/protocol/generated";
import { fairDealCommitment } from "@hulla/rules-engine";
import { hexToBytes } from "@hulla/shared-ts";
import { afterAll, describe, expect, it } from "vitest";

import { NAKAMA_HOST, rpc, scriptedClient, setTableId, type ScriptedClient } from "./nakama-client";

import type { Card } from "@hulla/rules-engine";

interface RoundEnd {
  readonly roundId: string;
  readonly winnerTeam: number;
  readonly counts: readonly number[];
  readonly dealPoints: number;
  readonly fairness: { commitment: string; seed: string; deckOrder: Card[] };
  readonly series: { roundsPlayed: number; teams: { points: number[]; dealsWon: number[] } };
}

describe.skipIf(NAKAMA_HOST === undefined)(
  "four friends switch a room to Rung against Nakama",
  () => {
    const clients: ScriptedClient[] = [];
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("moves a Thulla room to Rung in the same match, plays a deal and moves back", async () => {
      const suffix = String(Date.now());
      for (let i = 0; i < 4; i += 1) clients.push(await scriptedClient(`r${String(i)}`, suffix));
      const [host] = clients;
      if (host === undefined) throw new Error("no host");
      const created = await rpc<{ ok: boolean; matchId: string; tableId: string }>(
        host,
        "table_create",
        { gameId: "thulla", mode: "private" },
      );
      expect(created.ok).toBe(true);
      setTableId(created.tableId);
      const matchId = created.matchId;
      for (const c of clients) {
        await c.socket.joinMatch(matchId);
        await c.next((e) => e.type === "table_state");
      }

      // The room changes game; nobody leaves the match, so the voice room (keyed on it) stays.
      await host.send(matchId, "change_game", OPCODES.change_game.opcode, { gameId: "rung" });
      for (const c of clients) {
        const table = (
          await c.next(
            (e) => e.type === "table_state" && (e.payload as TableState).gameId === "rung",
          )
        ).payload as TableState;
        expect(table.seats.filter((s) => s.playerId !== null)).toHaveLength(4);
      }

      for (const c of clients)
        await c.send(matchId, "ready", OPCODES.ready.opcode, { ready: true });
      // Four sockets: the host starts only once the table has heard everyone's ready.
    await host.next(
      (e) =>
        e.type === "table_state" &&
        (e.payload as TableState).gameId === "rung" &&
        (e.payload as TableState).seats.every((s) => s.ready),
    );
    await host.send(matchId, "start_round", OPCODES.start_round.opcode, {});
      const opened = await host.next((e) => e.type === "rung_round_start");
      const roundId = (opened.payload as { roundId: string }).roundId;

      // Every client answers its own turn_start: the first suit, the first legal card, or stop.
      const handled = clients.map(() => 0);
      let clientSeq = 0;
      const ended = (): boolean => host.received.some((e) => e.type === "rung_round_end");
      const deadline = Date.now() + 150_000;
      while (!ended()) {
        if (Date.now() > deadline) throw new Error("the deal did not end in time");
        for (const [index, c] of clients.entries()) {
          for (let i = handled[index] ?? 0; i < c.received.length; i += 1) {
            const e = c.received[i];
            if (e?.type !== "turn_start") continue;
            const turn = e.payload as TurnStart;
            clientSeq += 1;
            const base = { roundId, clientSeq };
            if (turn.phase === "call") {
              await c.send(matchId, "call_trump", OPCODES.call_trump.opcode, {
                ...base,
                suit: turn.suits?.[0],
              });
            } else if (turn.phase === "courtChoice") {
              await c.send(matchId, "court_choice", OPCODES.court_choice.opcode, {
                ...base,
                playOn: false,
              });
            } else {
              await c.send(matchId, "play_card", OPCODES.play_card.opcode, {
                ...base,
                card: turn.legal[0],
              });
            }
          }
          handled[index] = c.received.length;
        }
        await new Promise((resolve) => setTimeout(resolve, 40));
      }

      const end = (await host.next((e) => e.type === "rung_round_end")).payload as RoundEnd;
      // Nothing was refused, and nobody had to be played for.
      for (const c of clients) {
        expect(c.received.filter((e) => e.type === "move_rejected" || e.type === "error")).toEqual(
          [],
        );
        await c.next((e) => e.type === "rung_round_end");
      }
      const plays = host.received.filter((e) => e.type === "rung_card_played");
      expect(plays.every((e) => !(e.payload as { auto: boolean }).auto)).toBe(true);
      // Each seat was sent its own thirteen cards once, and the four hands are the whole deck.
      const hands = clients.map((c) => c.received.filter((e) => e.type === "rung_hand"));
      expect(hands.map((h) => h.length)).toEqual([1, 1, 1, 1]);
      const dealt = hands.flatMap((h) => (h[0]?.payload as { cards: Card[] }).cards);
      expect(new Set(dealt).size).toBe(52);
      expect([...dealt].sort()).toEqual([...end.fairness.deckOrder].sort());
      // The commitment published before the deal matches the seed and deck revealed after it.
      expect(fairDealCommitment(hexToBytes(end.fairness.seed), end.fairness.deckOrder)).toBe(
        (opened.payload as { commitment: string }).commitment,
      );
      expect(end.counts[end.winnerTeam]).toBeGreaterThanOrEqual(7);
      expect(end.series.roundsPlayed).toBe(1);
      expect(end.series.teams.points[end.winnerTeam]).toBe(end.dealPoints);

      // Back to Thulla in the same match.
      await host.send(matchId, "change_game", OPCODES.change_game.opcode, { gameId: "thulla" });
      for (const c of clients) {
        const table = (
          await c.next(
            (e) =>
              e.type === "table_state" &&
              (e.payload as TableState).gameId === "thulla" &&
              e.seq > 0 &&
              c.received.indexOf(e) > c.received.findIndex((x) => x.type === "rung_round_end"),
          )
        ).payload as TableState;
        expect(table.seats.filter((s) => s.playerId !== null)).toHaveLength(4);
      }
    }, 180_000);
  },
);
