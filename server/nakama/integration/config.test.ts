import { describe, expect, it } from "vitest";

import {
  NAKAMA_HOST,
  NAKAMA_PORT,
  rpc,
  scriptedClient,
  type ScriptedClient,
} from "./nakama-client";

interface Created {
  readonly ok: boolean;
  readonly matchId: string;
}

async function configSet(body: object): Promise<Record<string, unknown>> {
  const url = `http://${NAKAMA_HOST ?? ""}:${NAKAMA_PORT}/v2/rpc/config_set?http_key=local-only-http-key&unwrap`;
  return (await (
    await fetch(url, { method: "POST", body: JSON.stringify(body) })
  ).json()) as Record<string, unknown>;
}

describe.skipIf(NAKAMA_HOST === undefined)("remote config against Nakama (GDD 22.5, 20.15)", () => {
  let client: ScriptedClient | null = null;
  it("a flag flips without a restart, and an old client is told to update and cannot join", async () => {
    const suffix = String(Date.now());
    client = await scriptedClient("cfg", suffix);
    const base = await rpc<{ ok: boolean; version: number; flags: Record<string, unknown> }>(
      client,
      "config_get",
      { clientVersion: "0.1.0" },
    );
    expect(base).toMatchObject({ ok: true, flags: { quick_play: true } });
    const raised = await configSet({
      version: base.version + 1,
      minClientVersion: "9.0.0",
      flags: { quick_play: [{ value: false }], shop: [{ value: false }] },
      updatedTs: 0,
    });
    expect(raised).toMatchObject({ ok: true });
    const after = await rpc<{
      minClientVersion: string;
      updateRequired: boolean;
      flags: Record<string, unknown>;
    }>(client, "config_get", { clientVersion: "0.1.0" });
    expect(after).toMatchObject({
      minClientVersion: "9.0.0",
      updateRequired: true,
      flags: { quick_play: false },
    });
    const created = await rpc<Created>(client, "table_create", {
      gameId: "thulla",
      mode: "private",
    });
    await expect(
      client.socket.joinMatch(created.matchId, undefined, { clientVersion: "0.1.0" }),
    ).rejects.toBeDefined();
    const restored = await configSet({
      version: base.version + 2,
      minClientVersion: "0.1.0",
      flags: { quick_play: [{ value: true }], voice: [{ value: true }], shop: [{ value: false }] },
      updatedTs: 0,
    });
    expect(restored).toMatchObject({ ok: true });
    await client.socket.joinMatch(created.matchId, undefined, { clientVersion: "0.1.0" });
    client.close();
  }, 60_000);
});
