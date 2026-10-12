import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";

import { canonicalJson, type JsonValue } from "@hulla/shared-ts";
import { describe, expect, it } from "vitest";

import { OPCODES, envelopeSchema, payloadValidators, type MessageType } from "../../generated/ts";
import { loadOpcodes } from "../registry";

const dir = join(import.meta.dirname, "..", "..", "examples");
const examples = readdirSync(dir)
  .filter((name) => name.endsWith(".json"))
  .sort()
  .map((name) => ({
    name,
    message: JSON.parse(readFileSync(join(dir, name), "utf8")) as {
      type: MessageType;
      payload: JsonValue;
    },
  }));

describe("generated TypeScript", () => {
  it("the opcode table matches the registry", () => {
    for (const entry of loadOpcodes()) {
      expect(OPCODES[entry.type as MessageType]).toMatchObject({
        opcode: entry.opcode,
        direction: entry.direction,
        visibility: entry.visibility,
      });
    }
    expect(Object.keys(OPCODES)).toHaveLength(loadOpcodes().length);
    expect(Object.keys(payloadValidators).sort()).toEqual(Object.keys(OPCODES).sort());
  });

  it("every example round-trips through its zod validator to the same canonical JSON", () => {
    expect(examples.length).toBeGreaterThanOrEqual(10);
    for (const { name, message } of examples) {
      const envelope = envelopeSchema.safeParse(message);
      expect(
        envelope.success,
        `${name} envelope: ${JSON.stringify(envelope.success ? null : envelope.error.issues)}`,
      ).toBe(true);
      const validator = payloadValidators[message.type];
      const parsed = validator.safeParse(message.payload);
      expect(
        parsed.success,
        `${name}: ${JSON.stringify(parsed.success ? null : parsed.error.issues)}`,
      ).toBe(true);
      if (parsed.success) {
        expect(canonicalJson(parsed.data as JsonValue)).toBe(canonicalJson(message.payload));
      }
    }
  });

  it("validators reject an unknown field, a wrong type and a malformed card", () => {
    const play = payloadValidators.play_card;
    expect(play.safeParse({ roundId: "r_1", clientSeq: 1, card: "KH" }).success).toBe(true);
    expect(play.safeParse({ roundId: "r_1", clientSeq: 1, card: "KH", extra: true }).success).toBe(
      false,
    );
    expect(play.safeParse({ roundId: "r_1", clientSeq: "1", card: "KH" }).success).toBe(false);
    expect(play.safeParse({ roundId: "r_1", clientSeq: 1, card: "10H" }).success).toBe(false);
    expect(
      payloadValidators.turn_start.safeParse({
        roundId: "r_1",
        seat: 0,
        legal: [],
        canTakeHand: false,
        deadlineTs: 1,
      }).success,
    ).toBe(true);
    expect(
      payloadValidators.turn_start.safeParse({
        roundId: "r_1",
        seat: 0,
        legal: ["XX"],
        canTakeHand: false,
        deadlineTs: 1,
      }).success,
    ).toBe(false);
  });
});
