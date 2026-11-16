import { OPCODES, type TableState } from "@hulla/protocol/generated";
import { afterAll, describe, expect, it } from "vitest";

import { NAKAMA_HOST, rpc, scriptedClient, setTableId, type ScriptedClient } from "./nakama-client";

interface ErrorPayload {
  readonly code: string;
  readonly detail?: Record<string, unknown>;
}

describe.skipIf(NAKAMA_HOST === undefined)("switching a room's game against Nakama", () => {
  const clients: ScriptedClient[] = [];
  afterAll(() => {
    for (const c of clients) c.close();
  });

  it("the table runs under the one match module and change_game reaches the table core", async () => {
    const suffix = String(Date.now());
    const host = await scriptedClient("gh", suffix);
    const guest = await scriptedClient("gg", suffix);
    clients.push(host, guest);
    const created = await rpc<{ ok: boolean; matchId: string; tableId: string }>(
      host,
      "table_create",
      { gameId: "thulla", mode: "private" },
    );
    expect(created.ok).toBe(true);
    setTableId(created.tableId);
    await host.socket.joinMatch(created.matchId);
    await guest.socket.joinMatch(created.matchId);
    const table = (await guest.next((e) => e.type === "table_state")).payload as TableState;
    expect(table.gameId).toBe("thulla");
    expect(table.queuedGame).toBeUndefined();

    const errorAfter = async (c: ScriptedClient, payload: object): Promise<ErrorPayload> => {
      await c.send(created.matchId, "change_game", OPCODES.change_game.opcode, payload);
      const error = await c.next((e) => e.type === "error" && !(e as { seen?: boolean }).seen);
      (error as { seen?: boolean }).seen = true;
      return error.payload as ErrorPayload;
    };

    // Only the host may switch.
    expect((await errorAfter(guest, { gameId: "rung" })).code).toBe("table.not_host");
    // Rung is in the protocol but has no server host yet, so the switch is refused by name.
    expect(await errorAfter(host, { gameId: "rung" })).toMatchObject({
      code: "table.invalid_payload",
      detail: { reason: "game_id" },
    });
    expect(await errorAfter(host, { gameId: "thulla" })).toMatchObject({
      code: "table.invalid_payload",
      detail: { reason: "same_game" },
    });
    // A game the protocol does not know fails the generated check inside Nakama.
    expect(await errorAfter(host, { gameId: "ludo" })).toMatchObject({
      code: "table.invalid_payload",
      detail: { reason: "schema" },
    });
  });
});
