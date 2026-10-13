import { describe, expect, it } from "vitest";

import { beforeMatchmakerAdd } from "../hooks/before_matchmaker_add";
import { rpcHello } from "../rpc/hello";
import { rpcAdminDrain } from "../rpc/ops";
import { rpcTableCreate } from "../rpc/table_create";
import { isDraining, readDrain, writeDrain } from "../services/ops";

import { fakeLogger, memoryNk } from "./fake-nk";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const ctxFor = (userId: string): nkruntime.Context =>
  ({
    userId,
    username: `user_${userId}`,
    env: { HULLA_REGION: "test" },
  }) as unknown as nkruntime.Context;
const SERVER_KEY = { env: {} } as unknown as nkruntime.Context;

describe("the drain switch for deploys (GDD 20.15, ARCHITECTURE 15)", () => {
  it("refuses new tables and searches while draining, and running tables carry on", () => {
    const m = memoryNk();
    const log = fakeLogger();
    expect(isDraining(m.nk)).toBe(false);
    expect(parse(rpcAdminDrain(ctxFor("u1"), log, m.nk, "{}"))).toMatchObject({ ok: false });
    const before = parse(
      rpcTableCreate(ctxFor("u1"), log, m.nk, JSON.stringify({ mode: "private" })),
    );
    expect(before).toMatchObject({ ok: true });
    const on = parse(
      rpcAdminDrain(SERVER_KEY, log, m.nk, JSON.stringify({ on: true, reason: "deploy 0.2.0" })),
    );
    expect(on).toMatchObject({ ok: true, draining: true });
    expect(readDrain(m.nk)).toMatchObject({ draining: true, reason: "deploy 0.2.0" });
    expect(parse(rpcHello(ctxFor("u1"), log, m.nk))).toMatchObject({ draining: true });
    expect(
      parse(rpcTableCreate(ctxFor("u1"), log, m.nk, JSON.stringify({ mode: "private" }))),
    ).toMatchObject({
      error: { code: "table.draining" },
    });
    const envelope = {
      matchmakerAdd: {
        minCount: 3,
        maxCount: 4,
        query: "*",
        stringProperties: {},
        numericProperties: {},
        countMultiple: 1,
      },
    };
    expect(() => beforeMatchmakerAdd(ctxFor("u1"), log, m.nk, envelope)).toThrow("table.draining");
    expect(m.nk.matchGet(before["matchId"] as string)).not.toBeNull();
    const off = parse(rpcAdminDrain(SERVER_KEY, log, m.nk, JSON.stringify({ on: false })));
    expect(off).toMatchObject({ ok: true, draining: false });
    expect(
      parse(rpcTableCreate(ctxFor("u1"), log, m.nk, JSON.stringify({ mode: "private" }))),
    ).toMatchObject({ ok: true });
    expect(
      [...m.storage.values()]
        .filter((o) => o.collection === "audit")
        .map((o) => (o.value as { action: string }).action),
    ).toEqual(["drain_on", "drain_off"]);
    writeDrain(m.nk, { draining: true, since: 1, reason: "" });
    expect(parse(rpcAdminDrain(SERVER_KEY, log, m.nk, "{}"))).toMatchObject({
      draining: true,
      liveTables: 0,
    });
  });
});
