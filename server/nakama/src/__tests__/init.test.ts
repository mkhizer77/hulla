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
    const { rpcs, matches, hooks, initializer } = fakeInitializer();
    const lines: LogLine[] = [];
    init(
      { env: { HULLA_ENV: "test" } } as unknown as nkruntime.Context,
      fakeLogger(lines),
      fakeNk(),
      initializer,
    );
    expect([...rpcs.keys()]).toEqual([
      "hello",
      "table_create",
      "table_join_by_code",
      "presets_get",
      "voice_token",
      "profile_get",
      "profile_update",
      "account_delete",
      "account_delete_cancel",
      "account_export",
      "admin_purge_deleted",
      "account_merge_prepare",
      "account_merge",
      "friend_add_by_code",
      "favourite_set",
      "social_overview",
      "table_list_friends",
      "presence_set",
      "mute_set",
      "mutes_get",
      "chat_filters_get",
      "chat_filters_set",
      "block_set",
      "blocks_get",
      "report_create",
      "reports_list",
      "admin_chat_logs_purge",
      "admin_table_inspect",
      "admin_round_replay",
      "admin_sanction",
      "admin_audit_list",
      "sanctions_get",
    ]);
    expect([...matches.keys()]).toEqual(["thulla"]);
    expect(hooks.get("rtBefore:ChannelMessageSend")?.name).toBe("beforeChannelMessageSend");
    expect(hooks.get("rtBefore:MatchmakerAdd")?.name).toBe("beforeMatchmakerAdd");
    expect(hooks.get("matchmakerMatched")?.name).toBe("matchmakerMatched");
    expect(hooks.get("afterAuthenticateDevice")?.name).toBe("afterAuthenticateDevice");
    expect(hooks.get("beforeAuthenticateDevice")?.name).toBe("beforeAuthenticateDevice");
    expect(hooks.get("afterAuthenticateFacebook")?.name).toBe("afterAuthenticateFacebook");
    const handler = matches.get("thulla");
    expect(Object.keys(handler ?? {}).sort()).toEqual([
      "matchInit",
      "matchJoin",
      "matchJoinAttempt",
      "matchLeave",
      "matchLoop",
      "matchSignal",
      "matchTerminate",
    ]);
    if (handler === undefined) throw new Error("thulla is not registered");
    const callbacks = [
      handler.matchInit,
      handler.matchJoinAttempt,
      handler.matchJoin,
      handler.matchLeave,
      handler.matchLoop,
      handler.matchTerminate,
      handler.matchSignal,
    ];
    expect(callbacks.every((fn) => fn.name.startsWith("thullaMatch"))).toBe(true);
    const rpc = rpcs.get("hello");
    const body = rpc?.({} as nkruntime.Context, fakeLogger(), fakeNk(), "");
    expect(JSON.parse(String(body))).toMatchObject({ ok: true, protocolMajor: 1 });
    expect(lines[0]).toMatchObject({
      level: "info",
      message: "Hulla runtime loaded",
      fields: { env: "test" },
    });
  });
  it("labels the environment local when HULLA_ENV is unset", async () => {
    const init = await loadInitModule();
    const lines: LogLine[] = [];
    init(
      { env: {} } as unknown as nkruntime.Context,
      fakeLogger(lines),
      fakeNk(),
      fakeInitializer().initializer,
    );
    expect(lines[0]).toMatchObject({ fields: { env: "local" } });
  });
});
