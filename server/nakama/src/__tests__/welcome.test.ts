import { describe, expect, it } from "vitest";

import { rpcTutorialComplete } from "../rpc/welcome";
import { nkLedgerPorts } from "../services/ledger";
import { payWelcome, WELCOME_REWARDS } from "../services/welcome";

import { fakeLogger, memoryNk } from "./fake-nk";

const ctxFor = (userId: string): nkruntime.Context =>
  ({ userId, env: {} }) as unknown as nkruntime.Context;

describe("the first session's one-time rewards (GDD 17.2, OQ-042)", () => {
  it("pays the tutorial and the first round once each", () => {
    const m = memoryNk();
    const ledger = nkLedgerPorts(m.nk);
    expect(payWelcome(ledger, "u1", "firstRound")).toBe(WELCOME_REWARDS.firstRound.coins);
    expect(payWelcome(ledger, "u1", "firstRound")).toBe(0);
    expect(payWelcome(ledger, "u1", "tutorial")).toBe(WELCOME_REWARDS.tutorial.coins);
    expect(m.wallets.get("u1")).toMatchObject({
      coins: WELCOME_REWARDS.firstRound.coins + WELCOME_REWARDS.tutorial.coins,
    });
  });

  it("tutorial_complete answers what it paid, once", () => {
    const m = memoryNk();
    const first = JSON.parse(rpcTutorialComplete(ctxFor("u1"), fakeLogger(), m.nk)) as Record<
      string,
      unknown
    >;
    expect(first).toEqual({ ok: true, applied: true, coins: WELCOME_REWARDS.tutorial.coins });
    const again = JSON.parse(rpcTutorialComplete(ctxFor("u1"), fakeLogger(), m.nk)) as Record<
      string,
      unknown
    >;
    expect(again).toEqual({ ok: true, applied: false, coins: 0 });
    expect(JSON.parse(rpcTutorialComplete(ctxFor(""), fakeLogger(), m.nk))).toMatchObject({
      ok: false,
    });
  });
});
