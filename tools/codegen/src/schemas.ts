import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

export interface OpcodeEntry {
  readonly type: string;
  readonly opcode: number;
  readonly direction: "c2s" | "s2c";
  readonly visibility: "public" | "seat" | "recipient";
  readonly schema: string;
  readonly game?: string;
}

export const PROTOCOL_BASE = "https://hulla.gg/schemas/protocol/";

export type JsonSchema = Record<string, unknown>;

/** Every schema under `schemas/`, keyed by its `$id`, with every `$ref` made absolute. */
export function loadSchemas(schemasDir: string): Map<string, JsonSchema> {
  const byId = new Map<string, JsonSchema>();
  const walk = (dir: string): void => {
    for (const entry of readdirSync(dir).sort()) {
      const full = join(dir, entry);
      if (statSync(full).isDirectory()) {
        walk(full);
      } else if (entry.endsWith(".json") && entry !== "opcodes.json") {
        const schema = JSON.parse(readFileSync(full, "utf8")) as JsonSchema;
        const id = String(schema["$id"]);
        byId.set(id, absolutizeRefs(schema, id) as JsonSchema);
      }
    }
  };
  walk(schemasDir);
  return byId;
}

export function loadOpcodes(schemasDir: string): readonly OpcodeEntry[] {
  return (
    JSON.parse(readFileSync(join(schemasDir, "opcodes.json"), "utf8")) as {
      messages: OpcodeEntry[];
    }
  ).messages;
}

/** Relative refs become absolute URLs against the schema's `$id`; absolute ones stay as they are. */
export function absolutizeRefs(node: unknown, baseId: string): unknown {
  if (Array.isArray(node)) {
    return node.map((item) => absolutizeRefs(item, baseId));
  }
  if (node !== null && typeof node === "object") {
    const out: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(node)) {
      out[key] =
        key === "$ref" && typeof value === "string"
          ? new URL(value, baseId).href
          : absolutizeRefs(value, baseId);
    }
    return out;
  }
  return node;
}

/** `shell/table.json#/definitions/join_table` → an absolute `$ref`. */
export function entryRef(entry: OpcodeEntry): string {
  const [file, pointer] = entry.schema.split("#");
  return `${PROTOCOL_BASE}${file ?? ""}#${pointer ?? ""}`;
}

export function pascal(type: string): string {
  return type
    .split("_")
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join("");
}

export function camel(type: string): string {
  const p = pascal(type);
  return p.charAt(0).toLowerCase() + p.slice(1);
}

/** One root schema whose properties name every message payload, the envelope and the Thulla view. */
export function buildRoot(opcodes: readonly OpcodeEntry[]): JsonSchema {
  const properties: Record<string, unknown> = {
    envelope: { $ref: `${PROTOCOL_BASE}envelope.json` },
    thullaView: { $ref: `${PROTOCOL_BASE}games/thulla/views.json#/definitions/view` },
    thullaRules: { $ref: "https://hulla.gg/schemas/games/thulla/rules.json" },
    thullaMove: { $ref: `${PROTOCOL_BASE}games/thulla/moves.json#/definitions/move` },
    seriesState: { $ref: `${PROTOCOL_BASE}common.json#/definitions/seriesState` },
    seatStats: { $ref: `${PROTOCOL_BASE}common.json#/definitions/seatStats` },
    ruleError: { $ref: `${PROTOCOL_BASE}shell/errors.json#/definitions/ruleError` },
    profile: { $ref: `${PROTOCOL_BASE}shell/profile.json#/definitions/profile` },
    profileStats: { $ref: `${PROTOCOL_BASE}shell/profile.json#/definitions/profileStats` },
    profileUpdate: { $ref: `${PROTOCOL_BASE}shell/profile.json#/definitions/profile_update` },
    profileGet: { $ref: `${PROTOCOL_BASE}shell/profile.json#/definitions/profile_get` },
  };
  for (const entry of opcodes) {
    properties[`${camel(entry.type)}Payload`] = { $ref: entryRef(entry) };
  }
  return {
    $schema: "http://json-schema.org/draft-07/schema#",
    $id: `${PROTOCOL_BASE}protocol.json`,
    title: "Protocol",
    type: "object",
    additionalProperties: false,
    properties,
  };
}

/** A json-schema-ref-parser resolver that answers from the loaded schemas instead of the network. */
export function resolverFor(byId: Map<string, JsonSchema>): Record<string, unknown> {
  return {
    hulla: {
      order: 1,
      canRead: (file: { url: string }) => file.url.startsWith("https://hulla.gg/"),
      read: (file: { url: string }) => {
        const url = file.url.split("#")[0] ?? file.url;
        const schema = byId.get(url);
        if (schema === undefined) {
          throw new Error(`unknown schema ${url}`);
        }
        return JSON.stringify(schema);
      },
    },
  };
}
