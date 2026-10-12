import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

/** The protocol major in every envelope; additive changes only within it (ADR-0005). */
export const PROTOCOL_MAJOR = 1;

/** Who may receive a message: everyone at the table, one seat only, or seats and spectators alike. */
export type Visibility = "public" | "seat" | "recipient";

export interface OpcodeEntry {
  readonly type: string;
  readonly opcode: number;
  readonly direction: "c2s" | "s2c";
  readonly visibility: Visibility;
  /** `<file>#/definitions/<name>` relative to `schemas/`. */
  readonly schema: string;
  readonly game?: "thulla";
}

const schemasDir = join(import.meta.dirname, "..", "schemas");

export function loadOpcodes(): readonly OpcodeEntry[] {
  const file = JSON.parse(readFileSync(join(schemasDir, "opcodes.json"), "utf8")) as {
    readonly messages: readonly OpcodeEntry[];
  };
  return file.messages;
}

/** Every schema file under `schemas/`, keyed by its path relative to that folder. */
export function loadSchemas(): ReadonlyMap<string, Record<string, unknown>> {
  const out = new Map<string, Record<string, unknown>>();
  const walk = (dir: string, prefix: string): void => {
    for (const entry of readdirSync(dir).sort()) {
      const full = join(dir, entry);
      const rel = prefix === "" ? entry : `${prefix}/${entry}`;
      if (statSync(full).isDirectory()) {
        walk(full, rel);
      } else if (entry.endsWith(".json") && entry !== "opcodes.json") {
        out.set(rel, JSON.parse(readFileSync(full, "utf8")) as Record<string, unknown>);
      }
    }
  };
  walk(schemasDir, "");
  return out;
}
