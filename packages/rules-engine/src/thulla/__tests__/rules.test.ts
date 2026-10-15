import { Ajv } from "ajv";
import * as fc from "fast-check";
import { describe, expect, it } from "vitest";

import {
  THULLA_PRESETS,
  THULLA_RULES_SCHEMA,
  defaultRules,
  resolveRules,
  validateRules,
  type RulesMode,
  type ThullaPreset,
} from "../rules";

const MODES: RulesMode[] = ["private", "public", "ranked"];
const PRESETS = Object.keys(THULLA_PRESETS) as ThullaPreset[];

function expectError(result: ReturnType<typeof validateRules>, reason: string): void {
  expect(result.ok).toBe(false);
  if (!result.ok) {
    expect(result.error.code).toBe("rules.invalid_rules");
    expect(result.error.detail?.["reason"]).toBe(reason);
  }
}

describe("Thulla rules", () => {
  it("R31_hand_taking_defaults_to_leader_only_in_private_and_public_and_off_in_ranked", () => {
    expect(defaultRules("private").handTaking).toBe("leaderOnly");
    expect(defaultRules("public").handTaking).toBe("leaderOnly");
    expect(defaultRules("ranked").handTaking).toBe("off");
  });

  it("R52_public_tables_play_single_rounds_and_private_rooms_keep_a_session", () => {
    expect(defaultRules("private").format).toBe("session");
    expect(defaultRules("public").format).toBe("single");
  });

  it("toggle defaults match the GDD 6.13 table", () => {
    expect(defaultRules("private")).toEqual({
      deckCount: 1,
      openingSimultaneous: true,
      openingThullaApplies: false,
      thullaEndsTrick: true,
      emptyHandLeader: "drawDiscard",
      handTaking: "leaderOnly",
      twoPlayerEndgame: "drawOpponent",
      confirmedFastForward: true,
      bigBhabhiPenalty: false,
      format: "session",
    });
  });

  it("presets carry the GDD overrides: R15 hardcore, R42 classic Pagat, R31 family", () => {
    const hardcore = resolveRules("private", "hardcore");
    expect(hardcore.ok && hardcore.value.openingThullaApplies).toBe(true);
    expect(hardcore.ok && hardcore.value.thullaEndsTrick).toBe(false);
    expect(hardcore.ok && hardcore.value.emptyHandLeader).toBe("drawNextPlayer");
    const classic = resolveRules("public", "classicPagat");
    expect(classic.ok && classic.value.twoPlayerEndgame).toBe("drawDiscard");
    const family = resolveRules("public", "family");
    expect(family.ok && family.value.handTaking).toBe("leaderOnly");
    const standard = resolveRules("ranked", "standard");
    expect(standard.ok && standard.value).toEqual(defaultRules("ranked"));
  });

  it("explicit overrides win over the preset and are validated", () => {
    const rules = resolveRules("private", "hardcore", { thullaEndsTrick: true });
    expect(rules.ok && rules.value.thullaEndsTrick).toBe(true);
    expectError(resolveRules("private", "standard", { thullaEndsTrick: "yes" }), "invalid_value");
    expectError(resolveRules("private", "standard", { turnTimer: 20 }), "unknown_key");
    expectError(resolveRules("private", "standard", [] as unknown), "not_an_object");
  });

  it("R3_two_decks_are_rejected_until_v1_2", () => {
    expectError(resolveRules("private", "standard", { deckCount: 2 }), "invalid_value");
  });

  it("validateRules rejects unknown keys, missing keys, bad values and non-objects", () => {
    expectError(validateRules({ ...defaultRules("private"), loserTitle: "Donkey" }), "unknown_key");
    const missing = Object.fromEntries(
      Object.entries(defaultRules("private")).filter(([key]) => key !== "format"),
    );
    expectError(validateRules(missing), "missing_key");
    expectError(
      validateRules({ ...defaultRules("private"), handTaking: "maybe" }),
      "invalid_value",
    );
    expectError(validateRules(null), "not_an_object");
    expectError(validateRules("standard"), "not_an_object");
    expect(validateRules(defaultRules("public")).ok).toBe(true);
  });

  describe("rulesSchema (JSON Schema draft-07, ADR-0021)", () => {
    const ajv = new Ajv({ strict: true });
    const validate = ajv.compile(THULLA_RULES_SCHEMA);

    it("validates every preset in every mode", () => {
      for (const mode of MODES) {
        for (const preset of PRESETS) {
          const rules = resolveRules(mode, preset);
          expect(rules.ok).toBe(true);
          expect(rules.ok && validate(rules.value)).toBe(true);
        }
      }
    });

    it("rejects an unknown toggle and a value outside the options", () => {
      expect(validate({ ...defaultRules("private"), turnTimer: 20 })).toBe(false);
      expect(validate({ ...defaultRules("private"), handTaking: "sometimes" })).toBe(false);
      expect(validate({ ...defaultRules("private"), deckCount: 2 })).toBe(false);
    });

    it("agrees with validateRules on random full objects", () => {
      const base = defaultRules("private");
      const value = fc.oneof(
        fc.constantFrom(
          1,
          2,
          true,
          false,
          "drawDiscard",
          "drawNextPlayer",
          "instantExit",
          "off",
          "leaderOnly",
          "anyone",
          "drawOpponent",
          "thullaEndsRound",
          "single",
          "session",
          "bestOf3",
          "firstTo3Bhabhis",
        ),
        fc.string(),
      );
      const keys = Object.keys(base);
      fc.assert(
        fc.property(
          fc.dictionary(fc.constantFrom(...keys, "extra"), value, { maxKeys: 11 }),
          (overrides) => {
            const candidate = { ...base, ...overrides };
            expect(validateRules(candidate).ok).toBe(validate(candidate));
          },
        ),
      );
    });
  });
});
