import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import { createChecks } from "../../generated/ts";

const examplesDir = join(import.meta.dirname, "..", "..", "examples");

interface Example {
  readonly type: string;
  readonly payload: Record<string, unknown>;
}

const examples = readdirSync(examplesDir)
  .filter((f) => f.endsWith(".json"))
  .map((f) => ({
    file: f,
    envelope: JSON.parse(readFileSync(join(examplesDir, f), "utf8")) as Example,
  }));

describe("standalone checks (ADR-0022)", () => {
  const checks = createChecks();
  it("builds a fresh set of functions on every call, so none lives in the frozen globals", () => {
    expect(createChecks().envelope).not.toBe(checks.envelope);
  });

  it("accept every example envelope and payload", () => {
    for (const { file, envelope } of examples) {
      expect(checks.envelope(envelope), file).toBe(true);
      const check = checks.payload[envelope.type];
      expect(check, file).toBeDefined();
      expect(check?.(envelope.payload), file).toBe(true);
    }
  });

  it("cover every message type in the opcode table", () => {
    const opcodes = JSON.parse(
      readFileSync(join(import.meta.dirname, "..", "..", "schemas", "opcodes.json"), "utf8"),
    ) as { messages: { type: string }[] };
    for (const { type } of opcodes.messages) expect(checks.payload[type], type).toBeDefined();
  });

  it("reject a tampered payload, an unknown field and a bad envelope", () => {
    const play = examples.find((e) => e.envelope.type === "play_card");
    if (play === undefined) throw new Error("no play_card example");
    expect(checks.payload["play_card"]?.({ ...play.envelope.payload, card: "1Z" })).toBe(false);
    expect(checks.payload["play_card"]?.({ ...play.envelope.payload, extra: 1 })).toBe(false);
    expect(checks.envelope({ ...play.envelope, v: 2 })).toBe(false);
    expect(checks.envelope("not an object")).toBe(false);
    expect(checks.thullaView(null)).toBe(false);
    expect(checks.thullaView({ viewer: { kind: "spectator" } })).toBe(false);
  });
});
