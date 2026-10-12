import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";

import { THULLA_RULES_SCHEMA } from "@hulla/rules-engine";
import { Ajv, type ValidateFunction } from "ajv";
import { describe, expect, it } from "vitest";

import { PROTOCOL_MAJOR, loadOpcodes, loadSchemas } from "../registry";

const schemas = loadSchemas();
const opcodes = loadOpcodes();
const ajv = new Ajv({
  strict: true,
  strictRequired: false,
  allowUnionTypes: true,
  allErrors: true,
});
for (const [, schema] of schemas) {
  ajv.addSchema(schema);
}
const base = "https://hulla.gg/schemas/protocol/";
const validateEnvelope = ajv.getSchema(`${base}envelope.json`);

/** Every message type in GDD section 20.6, both directions. */
const GDD_MESSAGES = [
  "join_table",
  "leave_table",
  "ready",
  "set_rules",
  "add_bot",
  "remove_bot",
  "kick",
  "transfer_host",
  "start_round",
  "play_card",
  "take_hand",
  "cancel_take",
  "vote_rematch",
  "chat_text",
  "quick_chat",
  "emote",
  "request_snapshot",
  "ping",
  "table_state",
  "rules_changed",
  "presence",
  "error",
  "round_start",
  "turn_start",
  "move_applied",
  "move_rejected",
  "trick_resolved",
  "pickup",
  "escape",
  "private_draw",
  "confirmed",
  "round_end",
  "snapshot",
  "pong",
];

function payloadValidator(type: string): ValidateFunction {
  const entry = opcodes.find((candidate) => candidate.type === type);
  if (entry === undefined) throw new Error(`no opcode entry for ${type}`);
  const [file, pointer] = entry.schema.split("#");
  const validator = ajv.getSchema(`${base}${file ?? ""}#${pointer ?? ""}`);
  if (validator === undefined) throw new Error(`no schema for ${entry.schema}`);
  return validator;
}

describe("protocol schemas", () => {
  it("every schema file compiles as draft-07 and declares its $id under the protocol base", () => {
    expect(schemas.size).toBeGreaterThanOrEqual(10);
    for (const [path, schema] of schemas) {
      expect(schema["$schema"], path).toBe("http://json-schema.org/draft-07/schema#");
      expect(String(schema["$id"]), path).toMatch(/^https:\/\/hulla\.gg\/schemas\//);
    }
    expect(validateEnvelope).toBeDefined();
  });

  it("the registry lists every GDD 20.6 message with a unique opcode and a resolvable schema", () => {
    const types = new Set(opcodes.map((entry) => entry.type));
    for (const type of GDD_MESSAGES) {
      expect(types.has(type), type).toBe(true);
    }
    expect(new Set(opcodes.map((entry) => entry.opcode)).size).toBe(opcodes.length);
    expect(new Set(opcodes.map((entry) => entry.type)).size).toBe(opcodes.length);
    for (const entry of opcodes) {
      expect(() => payloadValidator(entry.type), entry.type).not.toThrow();
      expect(entry.direction === "c2s" ? entry.opcode < 100 : entry.opcode >= 100, entry.type).toBe(
        true,
      );
    }
  });

  it("messages that carry hidden information are seat-only", () => {
    const byType = new Map(opcodes.map((entry) => [entry.type, entry] as const));
    expect(byType.get("turn_start")?.visibility).toBe("seat");
    expect(byType.get("private_draw")?.visibility).toBe("seat");
    expect(byType.get("table_state")?.visibility).toBe("recipient");
    expect(byType.get("trick_resolved")?.visibility).toBe("public");
  });

  it("every example validates against the envelope and its payload schema", () => {
    const dir = join(import.meta.dirname, "..", "..", "examples");
    const files = readdirSync(dir)
      .filter((name) => name.endsWith(".json"))
      .sort();
    expect(files.length).toBeGreaterThanOrEqual(10);
    for (const name of files) {
      const message = JSON.parse(readFileSync(join(dir, name), "utf8")) as {
        type: string;
        v: number;
        payload: unknown;
      };
      expect(
        validateEnvelope?.(message),
        `${name}: ${JSON.stringify(validateEnvelope?.errors)}`,
      ).toBe(true);
      expect(message.v).toBe(PROTOCOL_MAJOR);
      const validate = payloadValidator(message.type);
      expect(validate(message.payload), `${name}: ${JSON.stringify(validate.errors)}`).toBe(true);
    }
  });

  it("rejects an unknown field and a bad card so clients cannot diverge silently", () => {
    const play = payloadValidator("play_card");
    expect(play({ roundId: "r_1", clientSeq: 1, card: "KH" })).toBe(true);
    expect(play({ roundId: "r_1", clientSeq: 1, card: "10H" })).toBe(false);
    expect(play({ roundId: "r_1", clientSeq: 1, card: "KH", extra: 1 })).toBe(false);
    const draw = payloadValidator("private_draw");
    expect(
      draw({ roundId: "r_1", seat: 0, card: "8D", source: "discard", from: null, autoLead: true }),
    ).toBe(true);
    expect(
      draw({
        roundId: "r_1",
        seat: 0,
        card: "8D",
        source: "somewhere",
        from: null,
        autoLead: true,
      }),
    ).toBe(false);
  });

  it("games/thulla/rules.json equals the engine's rules schema", () => {
    const rules = schemas.get("games/thulla/rules.json");
    expect(rules).toEqual(THULLA_RULES_SCHEMA);
  });
});
