import { OPCODES, type TurnStart } from "@hulla/protocol/generated";
import { afterAll, describe, expect, it } from "vitest";

import { NAKAMA_HOST, rpc, scriptedClient, setTableId, type ScriptedClient } from "./nakama-client";

interface Created {
  readonly ok: boolean;
  readonly matchId: string;
  readonly tableId: string;
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
      const next = await me.next(
        (e) =>
          e.type === "round_end" ||
          (e.type === "turn_start" && !(e as { handled?: boolean }).handled),
        60_000,
      );
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
