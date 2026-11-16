import { describe, expect, it } from "vitest";

import {
  RUNG_PRESETS,
  RUNG_RULES_SCHEMA,
  defaultRungRules,
  resolveRungRules,
  validateRungRules,
} from "../rules";

function code(result: { ok: boolean; error?: { code: string; detail?: unknown } }): unknown {
  return result.ok ? null : [result.error?.code, result.error?.detail];
}

describe("Rung rules", () => {
  it("RG32_the_default_is_double_sar_to_seven_and_to_five_in_quick_play", () => {
    expect(defaultRungRules("private")).toMatchObject({
      collect: "double",
      scoring: "points",
      target: 7,
    });
    expect(defaultRungRules("public").target).toBe(5);
    expect(defaultRungRules("ranked").target).toBe(7);
  });

  it("RG15_RG16_the_three_presets_resolve_to_their_rule_sets", () => {
    const rung = resolveRungRules("private", "rung");
    const courtPiece = resolveRungRules("private", "courtPiece");
    const hokm = resolveRungRules("private", "hokm");
    expect(rung.ok && rung.value.collect).toBe("double");
    expect(courtPiece.ok && courtPiece.value).toMatchObject({ collect: "single", chase: true });
    expect(hokm.ok && hokm.value).toMatchObject({
      collect: "single",
      chase: false,
      redeal: "off",
      pointsTable: "hokm",
      target: 7,
      courtPassesCall: false,
    });
    expect(Object.keys(RUNG_PRESETS)).toEqual(["rung", "courtPiece", "hokm"]);
  });

  it("overrides apply on top of a preset", () => {
    const rules = resolveRungRules("private", "rung", { aceRule: true, target: 10 });
    expect(rules.ok && rules.value).toMatchObject({ aceRule: true, target: 10 });
  });

  it("rejects what is not a rule set, with a reason", () => {
    const rules = defaultRungRules("private");
    expect(code(validateRungRules(null))).toEqual([
      "rules.invalid_rules",
      { reason: "not_an_object" },
    ]);
    expect(code(validateRungRules({ ...rules, bots: true }))).toEqual([
      "rules.invalid_rules",
      { reason: "unknown_key", key: "bots" },
    ]);
    expect(code(validateRungRules({ ...rules, target: 6 }))).toEqual([
      "rules.invalid_rules",
      { reason: "invalid_value", key: "target", value: "6" },
    ]);
    const missing = Object.fromEntries(Object.entries(rules).filter(([key]) => key !== "chase"));
    expect(code(validateRungRules(missing))).toEqual([
      "rules.invalid_rules",
      { reason: "missing_key", key: "chase" },
    ]);
    expect(code(resolveRungRules("private", "rung", []))).toEqual([
      "rules.invalid_rules",
      { reason: "not_an_object" },
    ]);
    expect(code(resolveRungRules("private", "rung", { bots: true }))).toEqual([
      "rules.invalid_rules",
      { reason: "unknown_key", key: "bots" },
    ]);
  });

  it("RG21_RG22_RG23_pile_rules_are_refused_in_single_sar", () => {
    for (const key of ["firstTwo", "aceRule", "lastTwo"]) {
      expect(code(resolveRungRules("private", "courtPiece", { [key]: true }))).toEqual([
        "rules.invalid_rules",
        { reason: "double_sar_only", key },
      ]);
    }
    expect(resolveRungRules("private", "rung", { firstTwo: true, aceRule: true }).ok).toBe(true);
  });

  it("RG33_the_courts_tally_is_for_private_rooms_only", () => {
    expect(resolveRungRules("private", "rung", { scoring: "courts" }).ok).toBe(true);
    for (const mode of ["public", "ranked"] as const) {
      expect(code(resolveRungRules(mode, "rung", { scoring: "courts" }))).toEqual([
        "rules.invalid_rules",
        { reason: "courts_private_only" },
      ]);
    }
  });

  it("the schema names every toggle and nothing else", () => {
    const properties = RUNG_RULES_SCHEMA["properties"] as Record<string, unknown>;
    expect(Object.keys(properties).sort()).toEqual(Object.keys(defaultRungRules("private")).sort());
  });
});
