import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import { ALL_ERROR_CODES, ErrorCodes, errorArea, localisationKey, ruleError } from "../errors";

describe("error catalogue", () => {
  it("every code is area.snake_case and the area is one of the catalogue groups", () => {
    const areas = Object.keys(ErrorCodes);
    for (const code of ALL_ERROR_CODES) {
      expect(code).toMatch(/^[a-z]+\.[a-z0-9_]+$/);
      expect(areas).toContain(errorArea(code));
    }
  });

  it("codes are unique", () => {
    expect(new Set(ALL_ERROR_CODES).size).toBe(ALL_ERROR_CODES.length);
  });

  it("every code has a localisation key derived from it", () => {
    for (const code of ALL_ERROR_CODES) {
      expect(localisationKey(code)).toBe(`error.${code}`);
    }
  });

  it("ruleError carries optional structured detail", () => {
    expect(ruleError(ErrorCodes.rules.mustFollowSuit)).toEqual({ code: "rules.must_follow_suit" });
    expect(ruleError(ErrorCodes.rules.mustFollowSuit, { ledSuit: "H" })).toEqual({
      code: "rules.must_follow_suit",
      detail: { ledSuit: "H" },
    });
  });

  it("the endgame hand-take rejection from the GDD vectors exists", () => {
    expect(ErrorCodes.rules.handTakeDisabledEndgame).toBe("rules.hand_take_disabled_endgame");
  });

  it("every code a player can see has plain words in the English string table (GDD 14.13)", () => {
    // Content-pipeline codes only reach the deploy tooling and the ad network's callback, never a player.
    const internal = new Set<string>([
      ErrorCodes.content.manifestUnsigned,
      ErrorCodes.content.manifestDowngrade,
      ErrorCodes.content.packUnknown,
      ErrorCodes.content.schemaInvalid,
      ErrorCodes.economy.adRewardInvalid,
    ]);
    const file = join(import.meta.dirname, "..", "..", "..", "..", "content", "strings", "en.json");
    const strings = (JSON.parse(readFileSync(file, "utf8")) as { strings: Record<string, string> })
      .strings;
    const missing = ALL_ERROR_CODES.filter(
      (code) => !internal.has(code) && strings[localisationKey(code)] === undefined,
    );
    expect(missing).toEqual([]);
  });
});
