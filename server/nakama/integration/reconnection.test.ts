import {
  OPCODES,
  type MessageEnvelope,
  type TableState,
  type TurnStart,
} from "@hulla/protocol/generated";
import { afterAll, describe, expect, it } from "vitest";

import { NAKAMA_HOST, rpc, scriptedClient, setTableId, type ScriptedClient } from "./nakama-client";

interface Created {
  readonly ok: boolean;
  readonly matchId: string;
  readonly code: string;
  readonly tableId: string;
}

interface Snapshot {
  readonly state: TableState;
  readonly events: MessageEnvelope[];
}

function at<T>(items: readonly T[], index: number): T {
  const item = items[index];
  if (item === undefined) throw new Error(`no item ${String(index)}`);
  return item;
}

const isPublic = (e: MessageEnvelope): boolean =>
  (OPCODES as Record<string, { visibility: string } | undefined>)[e.type]?.visibility === "public";

/** Plays first legal cards until round_end; `pause` lets a test interrupt after a given number of plays. */
async function play(
  c: ScriptedClient,
  matchId: string,
  roundId: string,
  pause?: { readonly after: number; readonly then: () => Promise<void> },
): Promise<number> {
  let handled = 0;
  for (let guard = 0; guard < 200; guard += 1) {
    const next = await c
      .next(
        (e) =>
          e.type === "round_end" ||
          (e.type === "turn_start" &&
            (e.payload as TurnStart).roundId === roundId &&
            !(e as { handled?: boolean }).handled),
        90_000,
      )
      .catch(() => null);
    if (next === null) throw new Error(`${c.name} stalled waiting for a turn`);
    if (next.type === "round_end") return handled;
    (next as { handled?: boolean }).handled = true;
    const legal = (next.payload as TurnStart).legal;
    if (legal.length === 0) continue;
    handled += 1;
    await c.send(matchId, "play_card", OPCODES.play_card.opcode, {
      roundId,
      clientSeq: 100 + handled,
      card: legal[0],
    });
    if (pause?.after === handled) {
      await pause.then();
      return handled;
    }
  }
  return handled;
}

async function table(
  clients: ScriptedClient[],
  count: number,
  suffix: string,
): Promise<{ matchId: string; seatOf: Map<string, number> }> {
  for (let i = 0; i < count; i += 1) clients.push(await scriptedClient(`r${String(i)}`, suffix));
  const host = at(clients, 0);
  const created = await rpc<Created>(host, "table_create", { gameId: "thulla", mode: "private" });
  expect(created.ok).toBe(true);
  setTableId(created.tableId);
  const seatOf = new Map<string, number>();
  for (const c of clients) {
    await c.socket.joinMatch(created.matchId);
    const state = (await c.next((e) => e.type === "table_state")).payload as TableState;
    const own = state.seats.find((s) => s.playerId === c.session.user_id);
    if (own === undefined) throw new Error(`${c.name} has no seat`);
    seatOf.set(c.name, own.seat);
  }
  return { matchId: created.matchId, seatOf };
}

describe.skipIf(NAKAMA_HOST === undefined)(
  "reconnection, absence and host migration (GDD 9.8, 9.9)",
  () => {
    const clients: ScriptedClient[] = [];
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("a seat that drops mid-round is auto-played, regains control on return, and a snapshot fills the gap", async () => {
      const suffix = `${String(Date.now())}r`;
      const { matchId, seatOf } = await table(clients, 4, suffix);
      const [p0, p1, p2, p3] = clients;
      if (p0 === undefined || p1 === undefined || p2 === undefined || p3 === undefined)
        throw new Error("clients");
      for (const c of [p0, p1, p2, p3])
        await c.send(matchId, "ready", OPCODES.ready.opcode, { ready: true });
      await p0.next(
        (e) =>
          e.type === "table_state" &&
          (e.payload as TableState).seats.filter((s) => s.playerId !== null).every((s) => s.ready),
      );
      await p0.send(matchId, "start_round", OPCODES.start_round.opcode, {});
      const roundId = (
        (await p0.next((e) => e.type === "round_start")).payload as { roundId: string }
      ).roundId;
      const seat2 = seatOf.get("r2") ?? -1;

      // p2 plays its opening card, then drops; its seat is auto-played until it returns.
      let returned: ScriptedClient | null = null;
      let droppedAtSeq = 0;
      const drop = async (): Promise<void> => {
        droppedAtSeq = at(p2.received, p2.received.length - 1).seq;
        p2.socket.disconnect(false);
        await p0.next(
          (e) =>
            e.type === "presence" &&
            (e.payload as { seat: number; status: string }).seat === seat2 &&
            (e.payload as { status: string }).status === "disconnected",
          20_000,
        );
        await p0.next(
          (e) =>
            e.type === "move_applied" &&
            (e.payload as { seat: number; auto?: boolean }).seat === seat2 &&
            (e.payload as { auto?: boolean }).auto === true,
          60_000,
        );
        returned = await scriptedClient("r2", suffix);
        clients.push(returned);
        await returned.socket.joinMatch(matchId);
        await returned.next((e) => e.type === "table_state");
      };
      const p2Play = play(p2, matchId, roundId, { after: 1, then: drop });
      const others = [p0, p1, p3].map((c) => play(c, matchId, roundId));
      await p2Play;
      const back = returned as ScriptedClient | null;
      if (back === null) throw new Error("p2 never returned");

      // Back within the window: the presence says so and its next moves are its own, not the policy's.
      // It travels on the host's socket, so it is awaited rather than assumed delivered.
      await p0.next(
        (e) =>
          e.type === "presence" &&
          (e.payload as { seat: number; status: string }).seat === seat2 &&
          (e.payload as { status: string }).status === "reconnected",
        20_000,
      );
      const backPlays = play(back, matchId, roundId);
      await Promise.all([...others, backPlays]);
      const human = p0.received.filter(
        (e) =>
          e.type === "move_applied" &&
          (e.payload as { seat: number }).seat === seat2 &&
          (e.payload as { auto?: boolean }).auto !== true,
      );
      expect(human.length).toBeGreaterThan(1);
      const lastAuto = p0.received.findIndex(
        (e) =>
          e.type === "presence" &&
          (e.payload as { status: string; seat: number }).status === "reconnected" &&
          (e.payload as { seat: number }).seat === seat2,
      );
      const autoAfterReturn = p0.received
        .slice(lastAuto + 1)
        .filter(
          (e) =>
            e.type === "move_applied" &&
            (e.payload as { seat: number }).seat === seat2 &&
            (e.payload as { auto?: boolean }).auto === true,
        );
      expect(autoAfterReturn).toHaveLength(0);

      // The gap: a snapshot since the last seq p2 saw replays exactly the public stream p0 saw.
      await back.send(matchId, "request_snapshot", OPCODES.request_snapshot.opcode, {
        sinceSeq: droppedAtSeq,
      });
      const snapshot = (await back.next((e) => e.type === "snapshot")).payload as Snapshot;
      const expectedPublic = p0.received
        .filter((e) => isPublic(e) && e.seq > droppedAtSeq)
        .map((e) => `${String(e.seq)}:${e.type}`);
      const replayedPublic = snapshot.events
        .filter(isPublic)
        .map((e) => `${String(e.seq)}:${e.type}`);
      expect(replayedPublic).toEqual(expectedPublic);
      expect(snapshot.state.lastSeq).toBe(
        at(expectedPublic, expectedPublic.length - 1).split(":")[0] === undefined
          ? 0
          : Number(at(expectedPublic, expectedPublic.length - 1).split(":")[0]),
      );
      for (const e of snapshot.events) {
        if (e.type === "turn_start") expect((e.payload as TurnStart).seat).toBe(seat2);
      }
      const seatViews = snapshot.state.round?.seats ?? [];
      seatViews.forEach((s, index) => {
        if (index !== seat2) expect(s.hand).toBeNull();
      });
    }, 240_000);

    it("the longest-seated player becomes host when the host leaves, and a kicked player cannot rejoin", async () => {
      const suffix = `${String(Date.now())}h`;
      const trio: ScriptedClient[] = [];
      const { matchId } = await table(trio, 3, suffix);
      clients.push(...trio);
      const [host, second, third] = trio;
      if (host === undefined || second === undefined || third === undefined)
        throw new Error("clients");

      // Kick the third; its seat frees and it is refused on the way back.
      await host.send(matchId, "kick", OPCODES.kick.opcode, { seat: 2 });
      const kicked = await second.next(
        (e) => e.type === "presence" && (e.payload as { status: string }).status === "kicked",
      );
      expect((kicked.payload as { seat: number }).seat).toBe(2);
      const again = await scriptedClient("r2", suffix);
      clients.push(again);
      await expect(again.socket.joinMatch(matchId)).rejects.toBeDefined();

      // The host leaves; the second joiner, longest seated of the rest, takes over.
      await host.send(matchId, "leave_table", OPCODES.leave_table.opcode, {});
      const changed = await second.next((e) => e.type === "host_changed", 20_000);
      expect(changed.payload).toEqual({ seat: 1, reason: "host_left" });
      const state = (
        await second.next(
          (e) => e.type === "table_state" && (e.payload as TableState).hostSeat === 1,
          20_000,
        )
      ).payload as TableState;
      expect(state.seats[0]?.playerId).toBeNull();
      expect(state.hostSeat).toBe(1);
    }, 120_000);
  },
);
