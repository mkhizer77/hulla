import { describe, expect, it } from "vitest";

import { fakeInitializer, fakeLogger, fakeNk, type LogLine } from "./fake-nk";

// main.ts defines InitModule as a script-level global for Nakama; the test loads it the same way.
async function loadInitModule(): Promise<nkruntime.InitModule> {
  await import("../main");
  const init = (globalThis as unknown as { InitModule?: nkruntime.InitModule }).InitModule;
  if (init === undefined) throw new Error("InitModule is not on the global object");
  return init;
}

describe("InitModule", () => {
  it("registers the hello RPC and the RPC answers JSON", async () => {
    const init = await loadInitModule();
    const { rpcs, initializer } = fakeInitializer();
    const lines: LogLine[] = [];
    init(
      { env: { HULLA_ENV: "test" } } as unknown as nkruntime.Context,
      fakeLogger(lines),
      fakeNk(),
      initializer,
    );
    expect([...rpcs.keys()]).toEqual(["hello"]);
    const rpc = rpcs.get("hello");
    const body = rpc?.({} as nkruntime.Context, fakeLogger(), fakeNk(), "");
    expect(JSON.parse(String(body))).toMatchObject({ ok: true, protocolMajor: 1 });
    expect(lines[0]).toMatchObject({
      level: "info",
      message: "Hulla runtime loaded",
      fields: { env: "test" },
    });
  });
});
