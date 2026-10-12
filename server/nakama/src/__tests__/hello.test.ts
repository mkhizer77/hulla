import { THULLA_RULES_VERSION } from "@hulla/rules-engine";
import { describe, expect, it } from "vitest";

import { hello } from "../rpc/hello";

import { fakeRuntime } from "./fake-nk";

describe("hello", () => {
  it("answers with the server, rules and protocol versions and the server clock", () => {
    const runtime = fakeRuntime({ now: 42 });
    expect(hello(runtime)).toEqual({
      ok: true,
      server: "0.1.0",
      rulesVersion: THULLA_RULES_VERSION,
      protocolMajor: 1,
      serverTs: 42,
    });
  });
});
