import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import { rpcProgressGet, rpcWalletGet } from "../rpc/economy";
import { credit, nkLedgerPorts, spend, type Balances, type LedgerPorts } from "../services/ledger";

import { fakeLogger, memoryNk } from "./fake-nk";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const ctxFor = (userId: string): nkruntime.Context =>
  ({ userId, username: `user_${userId}`, env: {} }) as unknown as nkruntime.Context;

function memoryPorts(): LedgerPorts & { readonly updates: string[] } {
  const keys = new Set<string>();
  const balances = new Map<string, Balances>();
  const updates: string[] = [];
  return {
    updates,
    applied: (u, s) => keys.has(`${u}/${s}`),
    claim: (u, s) => {
      const k = `${u}/${s}`;
      if (keys.has(k)) return false;
      keys.add(k);
      return true;
    },
    update(u, changeset) {
      const current = balances.get(u) ?? { coins: 0, gems: 0 };
      const next = {
        coins: current.coins + (changeset["coins"] ?? 0),
        gems: current.gems + (changeset["gems"] ?? 0),
      };
      balances.set(u, next);
      updates.push(`${u} ${JSON.stringify(changeset)}`);
      return next;
    },
    balances: (u) => balances.get(u) ?? { coins: 0, gems: 0 },
  };
}

describe("the append-only ledger (GDD 13.2, 13.9, D2)", () => {
  it("credits once per source id: a replayed transaction is a no-op", () => {
    const ports = memoryPorts();
    expect(
      credit(ports, { userId: "u1", sourceId: "t:r_1:round", reason: "round", coins: 60 }),
    ).toEqual({
      ok: true,
      applied: true,
      balances: { coins: 60, gems: 0 },
    });
    expect(
      credit(ports, { userId: "u1", sourceId: "t:r_1:round", reason: "round", coins: 60 }),
    ).toEqual({
      ok: true,
      applied: false,
      balances: { coins: 60, gems: 0 },
    });
    expect(
      credit(ports, { userId: "u1", sourceId: "level:2", reason: "level_up", coins: 50, gems: 0 }),
    ).toMatchObject({ applied: true, balances: { coins: 110 } });
    expect(ports.updates).toHaveLength(2);
    expect(credit(ports, { userId: "u1", sourceId: "x", reason: "r", coins: -5 })).toMatchObject({
      error: { code: "table.invalid_payload" },
    });
    expect(credit(ports, { userId: "", sourceId: "x", reason: "r", coins: 5 })).toMatchObject({
      ok: false,
    });
    expect(credit(ports, { userId: "u1", sourceId: "empty", reason: "r" })).toMatchObject({
      applied: false,
    });
  });

  it("spends down to zero and never below, once per source id", () => {
    const ports = memoryPorts();
    credit(ports, { userId: "u1", sourceId: "seed", reason: "test", coins: 100, gems: 5 });
    expect(
      spend(ports, { userId: "u1", sourceId: "buy:1", reason: "shop", coins: 70 }),
    ).toMatchObject({ applied: true, balances: { coins: 30, gems: 5 } });
    expect(
      spend(ports, { userId: "u1", sourceId: "buy:1", reason: "shop", coins: 70 }),
    ).toMatchObject({ applied: false, balances: { coins: 30 } });
    expect(
      spend(ports, { userId: "u1", sourceId: "buy:2", reason: "shop", coins: 31 }),
    ).toMatchObject({ error: { code: "economy.insufficient_funds" } });
    expect(
      spend(ports, { userId: "u1", sourceId: "buy:3", reason: "shop", gems: 6 }),
    ).toMatchObject({ ok: false });
    expect(ports.balances("u1")).toEqual({ coins: 30, gems: 5 });
  });

  it("keeps balances equal to the sum of the ledger over random histories", () => {
    let seed = 12345;
    const next = (): number => {
      seed = (seed * 1_103_515_245 + 12_345) % 2_147_483_648;
      return seed / 2_147_483_648;
    };
    for (let run = 0; run < 25; run += 1) {
      const m = memoryNk();
      const ports = nkLedgerPorts(m.nk);
      const users = ["a", "b", "c"];
      for (let i = 0; i < 40; i += 1) {
        const userId = users[Math.floor(next() * users.length)] ?? "a";
        const sourceId = `s${String(Math.floor(next() * 30))}`;
        const coins = Math.floor(next() * 80);
        const gems = next() < 0.3 ? Math.floor(next() * 5) : 0;
        if (next() < 0.7) credit(ports, { userId, sourceId, reason: "test", coins, gems });
        else spend(ports, { userId, sourceId: `spend:${sourceId}`, reason: "test", coins, gems });
      }
      for (const userId of users) {
        const summed = m.ledger
          .filter((l) => l.userId === userId)
          .reduce(
            (acc, l) => ({
              coins: acc.coins + (l.changeset["coins"] ?? 0),
              gems: acc.gems + (l.changeset["gems"] ?? 0),
            }),
            { coins: 0, gems: 0 },
          );
        expect(ports.balances(userId)).toEqual(summed);
        expect(summed.coins).toBeGreaterThanOrEqual(0);
        expect(summed.gems).toBeGreaterThanOrEqual(0);
      }
    }
  });

  it("has no transfer: wallets change only through the ledger, for one account at a time", () => {
    const walk = (dir: string, out: string[] = []): string[] => {
      for (const entry of readdirSync(dir)) {
        const full = join(dir, entry);
        if (statSync(full).isDirectory()) {
          if (entry !== "__tests__") walk(full, out);
        } else if (entry.endsWith(".ts")) out.push(full);
      }
      return out;
    };
    const root = join(__dirname, "..");
    const touching = walk(root).filter((file) =>
      /wallets?Update|multiUpdate/.test(readFileSync(file, "utf8")),
    );
    expect(touching.map((f) => f.slice(root.length + 1)).sort()).toEqual([
      "rpc/account_merge.ts",
      "services/ledger.ts",
    ]);
    const ledgerSource = readFileSync(join(root, "services/ledger.ts"), "utf8");
    expect(ledgerSource).not.toMatch(/fromUserId|toUserId|transfer/i);
    const rpcs =
      readFileSync(join(root, "main.ts"), "utf8").match(/registerRpc\("([a-z_]+)"/g) ?? [];
    expect(rpcs.some((r) => /transfer|gift|send_coins|send_gems/.test(r))).toBe(false);
  });

  it("serves balances, the ledger and progress through the RPCs", () => {
    const m = memoryNk();
    const log = fakeLogger();
    credit(nkLedgerPorts(m.nk), {
      userId: "u1",
      sourceId: "t:r_1:round",
      reason: "round",
      coins: 60,
    });
    expect(parse(rpcWalletGet(ctxFor("u1"), log, m.nk))).toMatchObject({
      ok: true,
      balances: { coins: 60, gems: 0 },
      ledger: [{ changeset: { coins: 60 }, reason: "round", sourceId: "t:r_1:round" }],
    });
    expect(
      parse(rpcWalletGet({ env: {} } as unknown as nkruntime.Context, log, m.nk)),
    ).toMatchObject({ ok: false });
    const progress = parse(
      rpcProgressGet(ctxFor("u1"), log, m.nk, JSON.stringify({ tzOffsetMinutes: 330 })),
    );
    expect(progress).toMatchObject({
      ok: true,
      xp: 0,
      level: 1,
      loginStreak: 1,
      titles: ["naya_khiladi"],
      toNext: 125,
    });
    expect(m.object("progress", "state", "u1")).toMatchObject({ tzOffsetMinutes: 330 });
    expect(
      parse(rpcProgressGet(ctxFor("u1"), log, m.nk, JSON.stringify({ tzOffsetMinutes: 99_999 }))),
    ).toMatchObject({ ok: true });
  });
});
