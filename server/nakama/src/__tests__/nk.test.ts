import { describe, expect, it } from "vitest";

import { runtimeFrom } from "../infra/nk";

import { fakeLogger, type LogLine } from "./fake-nk";

describe("runtimeFrom", () => {
  it("delegates ids, hashing, the clock and every log level to nk and the logger", () => {
    const nk = {
      uuidv4: () => "id-1",
      sha256Hash: (input: string) => `sha:${input}`,
    } as unknown as nkruntime.Nakama;
    const lines: LogLine[] = [];
    const before = Date.now();
    const runtime = runtimeFrom(nk, fakeLogger(lines));

    expect(runtime.uuidv4()).toBe("id-1");
    expect(runtime.sha256Hex("seed")).toBe("sha:seed");
    expect(runtime.nowMs()).toBeGreaterThanOrEqual(before);

    runtime.logger.debug("d", { n: 1 });
    runtime.logger.info("i");
    runtime.logger.warn("w", { n: 3 });
    runtime.logger.error("e");
    expect(lines.map((line) => [line.level, line.message, line.fields])).toEqual([
      ["debug", "d", { n: 1 }],
      ["info", "i", undefined],
      ["warn", "w", { n: 3 }],
      ["error", "e", undefined],
    ]);
  });
});
