import { OPCODES, type TableState, type TurnStart } from "@hulla/protocol/generated";
import { createThullaRound, fairDealCommitment, resolveRules } from "@hulla/rules-engine";
import { hexToBytes, stateFromSeed, Xoshiro128StarStar } from "@hulla/shared-ts";
import { afterAll, describe, expect, it } from "vitest";

import { NAKAMA_HOST, rpc, scriptedClient, setTableId, type ScriptedClient } from "./nakama-client";

interface Created {
  readonly ok: boolean;
  readonly matchId: string;
  readonly code: string;
  readonly tableId: string;
}

const SEATS = 4;

describe.skipIf(NAKAMA_HOST === undefined)(
  "a scripted four-seat Thulla round against Nakama",
  () => {
    const clients: ScriptedClient[] = [];
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("plays to round_end with the legal mask, an absent seat under auto-play and no hand leaking", async () => {
      const suffix = String(Date.now());
      for (let i = 0; i < SEATS; i += 1)
        clients.push(await scriptedClient(`p${String(i)}`, suffix));
      const [host] = clients;
      if (host === undefined) throw new Error("no host");

      const created = await rpc<Created>(host, "table_create", {
        gameId: "thulla",
        mode: "private",
      });
      expect(created).toMatchObject({ ok: true });
      setTableId(created.tableId);
      const matchId = created.matchId;

      for (const c of clients) await c.socket.joinMatch(matchId);
      const seatOf = new Map<string, number>();
      for (const c of clients) {
        const state = (await c.next((e) => e.type === "table_state")).payload as TableState;
        expect(state.code).toBe(created.code);
        const own = state.seats.find((s) => s.playerId === c.session.user_id);
        if (own === undefined) throw new Error(`${c.name} has no seat`);
        seatOf.set(c.name, own.seat);
      }
      for (const c of clients)
        await c.send(matchId, "ready", OPCODES.ready.opcode, { ready: true });
      await host.next(
        (e) =>
          e.type === "table_state" &&
          (e.payload as TableState).seats.filter((s) => s.playerId !== null).every((s) => s.ready),
      );
      await host.send(matchId, "start_round", OPCODES.start_round.opcode, {});

      const starts = await Promise.all(clients.map((c) => c.next((e) => e.type === "round_start")));
      const roundId = (starts[0]?.payload as { roundId: string }).roundId;
      const commitment = (starts[0]?.payload as { commitment: string }).commitment;

      // Seat 3 drops off the socket right after the deal; the policy plays it until it returns.
      const absent = clients[3];
      if (absent === undefined) throw new Error("no fourth client");
      absent.socket.disconnect(false);
      const autoPlayed = await host.next(
        (e) =>
          e.type === "move_applied" &&
          (e.payload as { seat: number; auto?: boolean }).seat === seatOf.get("p3") &&
          (e.payload as { auto?: boolean }).auto === true,
        40_000,
      );
      expect(autoPlayed.type).toBe("move_applied");
      const returned = await scriptedClient("p3", suffix);
      clients.push(returned);
      await returned.socket.joinMatch(matchId);
      await returned.next((e) => e.type === "table_state");
      const players = [clients[0], clients[1], clients[2], returned].filter(
        (c): c is ScriptedClient => c !== undefined,
      );

      const play = async (c: ScriptedClient): Promise<void> => {
        let handled = 0;
        for (let guard = 0; guard < 200; guard += 1) {
          const turn = await c
            .next(
              (e) =>
                e.type === "round_end" ||
                (e.type === "turn_start" &&
                  (e.payload as TurnStart).roundId === roundId &&
                  !(e as { handled?: boolean }).handled),
              60_000,
            )
            .catch(() => null);
          if (turn === null || turn.type === "round_end") return;
          (turn as { handled?: boolean }).handled = true;
          const legal = (turn.payload as TurnStart).legal;
          if (legal.length === 0) continue;
          handled += 1;
          await c.send(matchId, "play_card", OPCODES.play_card.opcode, {
            roundId,
            clientSeq: 100 + handled,
            card: legal[0],
          });
        }
      };
      const ends = Promise.all(players.map((c) => c.next((e) => e.type === "round_end", 200_000)));
      await Promise.all(players.map(play));
      const endPayloads = (await ends).map(
        (e) =>
          e.payload as {
            fairness: { commitment: string; seed: string; deckOrder: string[] };
            bhabhiSeat: number;
          },
      );
      const end = endPayloads[0];
      if (end === undefined) throw new Error("no round_end");
      expect(end.fairness.commitment).toBe(commitment);
      expect(
        fairDealCommitment(hexToBytes(end.fairness.seed), end.fairness.deckOrder as never),
      ).toBe(commitment);

      // The deal replays from the revealed seed: the deck order is what the engine shuffles for it.
      const rules = resolveRules("private");
      if (!rules.ok) throw new Error(rules.error.code);
      const seed = hexToBytes(end.fairness.seed);
      const dealerSeat = (starts[0]?.payload as { dealerSeat: number }).dealerSeat;
      const replay = createThullaRound(
        { rules: rules.value, seatCount: SEATS, dealerSeat, seed },
        Xoshiro128StarStar.fromState(stateFromSeed(seed)),
      );
      if (!replay.ok) throw new Error(replay.error.code);
      expect(replay.value.deckOrder).toEqual(end.fairness.deckOrder);
      expect(replay.value.commitment).toBe(commitment);

      // Redaction at the socket: no client ever saw another seat's hand or legal mask.
      for (const c of players) {
        const mine = seatOf.get(c.name);
        for (const e of c.received) {
          if (e.type === "table_state") {
            const round = (e.payload as TableState).round;
            if (round === null) continue;
            round.seats.forEach((s, index) => {
              if (index !== mine) expect(s.hand).toBeNull();
            });
          }
          if (e.type === "turn_start") expect((e.payload as TurnStart).seat).toBe(mine);
        }
        const seqs = c.received
          .filter((e) => OPCODES[e.type as keyof typeof OPCODES].visibility === "public")
          .map((e) => e.seq);
        for (let i = 1; i < seqs.length; i += 1) expect(seqs[i]).toBe((seqs[i - 1] ?? 0) + 1);
      }
    });
  },
);
