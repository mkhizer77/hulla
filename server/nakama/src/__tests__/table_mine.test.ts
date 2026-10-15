import { describe, expect, it } from "vitest";

import { currentTable, rpcTableMine } from "../rpc/table_mine";
import { nkPresenceStore, seatTaken } from "../services/presence";

import { fakeLogger, memoryNk } from "./fake-nk";

type Json = Record<string, unknown>;

const reply = (fields: Json) => () => JSON.stringify({ ok: true, ...fields });

describe("my current table, for Rejoin on Home", () => {
  it("names the table while the seat is still mine, connected or not", () => {
    const store = nkPresenceStore(memoryNk().nk);
    seatTaken(store, "u1", "m1", 1000);
    const signals: string[] = [];
    const table = currentTable(store, "u1", (matchId, data) => {
      signals.push(`${matchId} ${data}`);
      return JSON.stringify({
        ok: true,
        tableId: "t1",
        seated: true,
        present: false,
        code: "QK7",
        mode: "public",
        status: "inRound",
      });
    });
    expect(table).toEqual({
      matchId: "m1",
      tableId: "t1",
      code: "QK7",
      mode: "public",
      status: "inRound",
    });
    expect(signals).toEqual(['m1 {"kind":"has_user","userId":"u1"}']);
  });

  it("is null once the seat is freed, the table closed, or the match gone", () => {
    const store = nkPresenceStore(memoryNk().nk);
    expect(currentTable(store, "u1", reply({ seated: true }))).toBeNull();
    seatTaken(store, "u1", "m1", 1000);
    expect(currentTable(store, "u1", reply({ seated: false, status: "open" }))).toBeNull();
    expect(currentTable(store, "u1", reply({ seated: true, status: "closed" }))).toBeNull();
    expect(
      currentTable(store, "u1", () => {
        throw new Error("match not found");
      }),
    ).toBeNull();
  });

  it("needs a signed-in caller", () => {
    const m = memoryNk();
    const out = JSON.parse(
      rpcTableMine({ env: {} } as unknown as nkruntime.Context, fakeLogger(), m.nk),
    ) as Json;
    expect(out).toMatchObject({ ok: false, error: { code: "table.not_authorised" } });
    const mine = JSON.parse(
      rpcTableMine({ env: {}, userId: "u9" } as unknown as nkruntime.Context, fakeLogger(), m.nk),
    ) as Json;
    expect(mine).toEqual({ ok: true, table: null });
  });
});
