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
  readonly tableId: string;
}

/** The longest the table may stay silent mid-round; the pre-deal countdown is about five seconds. */
const SILENCE_LIMIT_MS = 30_000;

/**
 * The player's next turn, or the end of the round. Once the player has escaped there is no turn
 * left to wait for and the three bots play on at their own pace, so the wait is long; what must
 * not happen is the table going silent, and that fails at once with what was last received.
 */
async function turnOrEnd(me: ScriptedClient): Promise<MessageEnvelope> {
  const wanted = me.next(
    (e) =>
      e.type === "round_end" || (e.type === "turn_start" && !(e as { handled?: boolean }).handled),
    200_000,
  );
  wanted.catch(() => undefined);
  let seen = me.received.length;
  let watchdog: ReturnType<typeof setInterval> | undefined;
  const silent = new Promise<never>((_resolve, reject) => {
    watchdog = setInterval(() => {
      if (me.received.length === seen) {
        const last = me.received
          .slice(-5)
          .map((e) => e.type)
          .join(",");
        reject(
          new Error(`the table sent nothing for 30 s after ${String(seen)} messages: ${last}`),
        );
      }
      seen = me.received.length;
    }, SILENCE_LIMIT_MS);
  });
  try {
    return await Promise.race([wanted, silent]);
  } finally {
    clearInterval(watchdog);
  }
}

describe.skipIf(NAKAMA_HOST === undefined)("a Quick Play round with bots against Nakama", () => {
  let player: ScriptedClient | undefined;
  afterAll(() => player?.close());

  it("deals for one player and three bots, and the bots play the round out (GDD 8)", async () => {
    const me = await scriptedClient("solo", String(Date.now()));
    player = me;
    const created = await rpc<Created>(me, "quick_play_bots", { seats: 4 });
    expect(created).toMatchObject({ ok: true });
    setTableId(created.tableId);
    await me.socket.joinMatch(created.matchId);

    const ended = me.next((e) => e.type === "round_end", 200_000);
    let clientSeq = 0;
    for (let guard = 0; guard < 200; guard += 1) {
      const next = await turnOrEnd(me);
      if (next.type === "round_end") break;
      (next as { handled?: boolean }).handled = true;
      const turn = next.payload as TurnStart;
      const card = turn.legal[0];
      if (card === undefined) continue;
      clientSeq += 1;
      await me.send(created.matchId, "play_card", OPCODES.play_card.opcode, {
        roundId: turn.roundId,
        clientSeq,
        card,
      });
    }
    const end = (await ended).payload as { bhabhiSeat: number };
    expect(end.bhabhiSeat).toBeGreaterThanOrEqual(0);
    const botPlays = me.received.filter(
      (e) => e.type === "move_applied" && (e.payload as { auto?: boolean }).auto === true,
    );
    expect(botPlays.length).toBeGreaterThan(10);
  });
});

describe.skipIf(NAKAMA_HOST === undefined)("a private Thulla room with bots against Nakama", () => {
  let host: ScriptedClient | undefined;
  afterAll(() => host?.close());

  it("a host alone adds two bots, takes one away and back, and plays a whole round", async () => {
    const me = await scriptedClient("tb", String(Date.now()));
    host = me;
    const created = await rpc<Created>(me, "table_create", { gameId: "thulla", mode: "private" });
    expect(created).toMatchObject({ ok: true });
    setTableId(created.tableId);
    await me.socket.joinMatch(created.matchId);
    await me.next((e) => e.type === "table_state");
    const bots = (n: number) => (e: MessageEnvelope) =>
      e.type === "table_state" &&
      (e.payload as TableState).seats.filter((s) => s.bot === true).length === n;

    await me.send(created.matchId, "add_bot", OPCODES.add_bot.opcode, {});
    await me.send(created.matchId, "add_bot", OPCODES.add_bot.opcode, {});
    const two = (await me.next(bots(2))).payload as TableState;
    const botSeat = two.seats.findIndex((s) => s.bot === true);
    await me.send(created.matchId, "remove_bot", OPCODES.remove_bot.opcode, { seat: botSeat });
    await me.next(bots(1));
    await me.send(created.matchId, "add_bot", OPCODES.add_bot.opcode, {});
    const full = (await me.next(bots(2))).payload as TableState;
    expect(new Set(full.seats.flatMap((s) => s.displayName ?? [])).size).toBe(3);

    await me.send(created.matchId, "ready", OPCODES.ready.opcode, { ready: true });
    await me.next(
      (e) =>
        e.type === "table_state" &&
        (e.payload as TableState).seats.every((s) => s.playerId === null || s.ready),
    );
    const ended = me.next((e) => e.type === "round_end", 200_000);
    await me.send(created.matchId, "start_round", OPCODES.start_round.opcode, {});
    let clientSeq = 0;
    for (let guard = 0; guard < 200; guard += 1) {
      const next = await turnOrEnd(me);
      if (next.type === "round_end") break;
      (next as { handled?: boolean }).handled = true;
      const turn = next.payload as TurnStart;
      const card = turn.legal[0];
      if (card === undefined) continue;
      clientSeq += 1;
      await me.send(created.matchId, "play_card", OPCODES.play_card.opcode, {
        roundId: turn.roundId,
        clientSeq,
        card,
      });
    }
    const end = (await ended).payload as { bhabhiSeat: number };
    expect(end.bhabhiSeat).toBeGreaterThanOrEqual(0);
    const botPlays = me.received.filter(
      (e) => e.type === "move_applied" && (e.payload as { auto?: boolean }).auto === true,
    );
    expect(botPlays.length).toBeGreaterThan(5);
  }, 240_000);
});
