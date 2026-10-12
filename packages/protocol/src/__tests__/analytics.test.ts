import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import { ANALYTICS_EVENT_NAMES } from "../../generated/ts";

const schema = JSON.parse(
  readFileSync(
    join(import.meta.dirname, "..", "..", "schemas", "analytics", "events.json"),
    "utf8",
  ),
) as { definitions: Record<string, { properties: Record<string, { type: string }> }> };

/** Parameter names that would carry personal data, chat or hidden cards (GDD 23.6, ARCHITECTURE 12.4). */
const FORBIDDEN =
  /(^|_)(name|names|username|handle|email|phone|text|message|chat_content|hand|hands|card|cards_list|deck|seed)$/;

describe("analytics events (GDD 23.3)", () => {
  it("lists every event in the schema", () => {
    expect([...ANALYTICS_EVENT_NAMES].sort()).toEqual(Object.keys(schema.definitions).sort());
  });

  it("no event carries a name, chat text, a hand or a card list", () => {
    for (const [event, def] of Object.entries(schema.definitions)) {
      for (const [param, spec] of Object.entries(def.properties)) {
        expect(FORBIDDEN.test(param), `${event}.${param}`).toBe(false);
        expect(spec.type, `${event}.${param} is a scalar`).not.toBe("array");
        expect(spec.type, `${event}.${param} is a scalar`).not.toBe("object");
      }
    }
  });
});
