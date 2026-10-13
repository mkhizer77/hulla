import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import { forbiddenField, log, REDACTED, type FieldLogger } from "../infra/log";
import { runtimeFrom } from "../infra/nk";

import { fakeLogger, memoryNk, type LogLine } from "./fake-nk";

/** A logger shaped like Nakama's: fields arrive through `withFields`, the message alone through `info`. */
function nakamaLike(): {
  lines: { level: string; message: string; fields: Record<string, string> }[];
  logger: FieldLogger;
} {
  const lines: { level: string; message: string; fields: Record<string, string> }[] = [];
  const make = (fields: Record<string, string>): FieldLogger => ({
    debug: (message: string) => lines.push({ level: "debug", message, fields }),
    info: (message: string) => lines.push({ level: "info", message, fields }),
    warn: (message: string) => lines.push({ level: "warn", message, fields }),
    error: (message: string) => lines.push({ level: "error", message, fields }),
    withFields: (more) => make({ ...fields, ...more }),
  });
  return { lines, logger: make({}) };
}

function walk(dir: string, out: string[] = []): string[] {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      if (entry !== "__tests__") walk(full, out);
    } else if (entry.endsWith(".ts")) out.push(full);
  }
  return out;
}

describe("structured logs without PII, hands or chat text (ARCHITECTURE 12.6)", () => {
  it("sends fields through the logger's field API as strings and the message alone otherwise", () => {
    const { lines, logger } = nakamaLike();
    log.info(logger, "table created", { tableId: "t_ABCDEF", seats: 4, open: true });
    log.warn(logger, "plain");
    expect(lines).toEqual([
      {
        level: "info",
        message: "table created",
        fields: { tableId: "t_ABCDEF", seats: "4", open: "true" },
      },
      { level: "warn", message: "plain", fields: {} },
    ]);
  });

  it("redacts a hand, a card, a name or chat text instead of logging it", () => {
    expect(forbiddenField("hand", ["AS"])).toBe("key hand");
    expect(forbiddenField("led", "KS")).toBe("card in led");
    expect(forbiddenField("pile", ["2S", "3S"])).toBe("card list in pile");
    expect(forbiddenField("displayName", "Ali")).toBe("key displayName");
    expect(forbiddenField("text", "hi")).toBe("key text");
    expect(forbiddenField("count", 3)).toBeNull();
    const { lines, logger } = nakamaLike();
    log.error(logger, "move dropped", {
      seat: 1,
      hand: ["AS", "KS"],
      displayName: "Ali",
      card: "AS",
    });
    expect(lines[0]?.fields).toEqual({
      seat: "1",
      hand: REDACTED,
      displayName: REDACTED,
      card: REDACTED,
    });
    const plain: LogLine[] = [];
    log.info(fakeLogger(plain), "chat refused", { text: "salaam", code: "x" });
    expect(plain[0]?.fields).toEqual({ text: REDACTED, code: "x" });
    const runtime = runtimeFrom(memoryNk().nk, fakeLogger(plain));
    runtime.logger.warn("via runtime", { hands: [["AS"]], seat: 2 });
    expect(plain[1]).toMatchObject({ level: "warn", fields: { hands: REDACTED, seat: 2 } });
  });

  it("every call site goes through the helper, and none names a forbidden field", () => {
    const root = join(__dirname, "..");
    const direct: string[] = [];
    const forbidden: string[] = [];
    for (const file of walk(root)) {
      if (file.endsWith("infra/log.ts")) continue;
      const source = readFileSync(file, "utf8");
      if (/\blogger\.(info|warn|error|debug)\(/.test(source))
        direct.push(file.slice(root.length + 1));
      for (const match of source.matchAll(/log\.(?:info|warn|error|debug)\([^;]*?\{([^}]*)\}/gs)) {
        const keys = [...(match[1] ?? "").matchAll(/(?:^|[\s,{])([a-zA-Z_]+)\s*[:,}]/g)].map(
          (k) => k[1] ?? "",
        );
        for (const key of keys) {
          if (forbiddenField(key, "x") !== null)
            forbidden.push(`${file.slice(root.length + 1)}: ${key}`);
        }
      }
    }
    expect(direct).toEqual([]);
    expect(forbidden).toEqual([]);
  });
});
