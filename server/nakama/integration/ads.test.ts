import { afterAll, describe, expect, it } from "vitest";

import {
  NAKAMA_HOST,
  NAKAMA_PORT,
  rpc,
  scriptedClient,
  type ScriptedClient,
} from "./nakama-client";

/** AppLovin's callback shape: a GET with the macros in the query string and the http key. */
async function callback(params: Record<string, string>): Promise<Record<string, unknown>> {
  const query = new URLSearchParams({ http_key: "local-only-http-key", unwrap: "", ...params });
  const url = `http://${NAKAMA_HOST ?? ""}:${NAKAMA_PORT}/v2/rpc/ad_reward?${query.toString()}`;
  return (await (await fetch(url)).json()) as Record<string, unknown>;
}

describe.skipIf(NAKAMA_HOST === undefined)("rewarded ads against Nakama (GDD 13.6)", () => {
  const clients: ScriptedClient[] = [];
  afterAll(() => {
    for (const c of clients) c.close();
  });

  it("shows the caps and rules, and the callback is refused without the event key or from a session", async () => {
    const player = await scriptedClient("ads", String(Date.now()));
    clients.push(player);
    expect(await rpc<object>(player, "ads_get", { tzOffsetMinutes: 300 })).toMatchObject({
      ok: true,
      caps: { double_coins: 5, reroll: 2, theme_trial: 1 },
      used: { double_coins: 0, reroll: 0, theme_trial: 0 },
      rerollCredits: 0,
      interstitials: { enabled: true, fromRound: 4, intervalSeconds: 240 },
    });
    // The local box holds no AppLovin event key (runbook), so every callback is turned away.
    expect(
      await callback({
        user_id: player.session.user_id ?? "",
        event_id: "evt-1",
        token: "0".repeat(40),
        custom: "reroll",
      }),
    ).toMatchObject({ ok: false, error: { code: "economy.ads_unavailable" } });
    expect(
      await rpc<{ ok: boolean; error?: { code: string } }>(player, "ad_reward", {
        user_id: player.session.user_id ?? "",
        event_id: "evt-2",
        token: "0".repeat(40),
        custom: "reroll",
      }),
    ).toMatchObject({ ok: false, error: { code: "table.not_authorised" } });
  });
});
