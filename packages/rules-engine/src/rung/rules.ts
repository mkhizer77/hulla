import {
  ErrorCodes,
  err,
  ok,
  ruleError,
  type JsonObject,
  type Result,
  type RuleError,
} from "@hulla/shared-ts";

/** Bumped on any change that alters legality or outcomes; rounds pin it (ADR-0003). */
export const RUNG_RULES_VERSION = "1.0.0";

export type RungCollect = "double" | "single";
export type RungRedeal = "off" | "noHonours";
export type RungScoring = "points" | "courts";
export type RungPointsTable = "rung" | "hokm";

/**
 * The toggles that change legality or outcomes (Rung design 2.11). Timers, the direction the
 * table draws play in and the bot level are table settings: the engine always counts seats up.
 */
export interface RungRules {
  /** RG15 to RG20: every trick counts, or two in a row by one player take the pile. */
  readonly collect: RungCollect;
  /** RG21: the winner of tricks one and two does not take the pile (Double Sar). */
  readonly firstTwo: boolean;
  /** RG22: two aces in a row do not take the pile (Double Sar). */
  readonly aceRule: boolean;
  /** RG23: tricks eleven and twelve do not take the pile (Double Sar). */
  readonly lastTwo: boolean;
  /** RG7: the caller may ask for new cards when the first five hold no honour. */
  readonly redeal: RungRedeal;
  /** RG26: a team at seven against zero may play on for the bigger prize. */
  readonly chase: boolean;
  /** RG32 and RG33: first to a target, or an open tally of courts. */
  readonly scoring: RungScoring;
  readonly pointsTable: RungPointsTable;
  readonly target: 5 | 7 | 10;
  /** RG33: what a court by the dealer's team is worth in the courts tally. */
  readonly goonCourt: 3 | 10;
  /** RG37: after the caller's team scores a court the call passes to the caller's partner. */
  readonly courtPassesCall: boolean;
}

export type RungMode = "private" | "public" | "ranked";

const OPTIONS = {
  collect: ["double", "single"],
  firstTwo: [true, false],
  aceRule: [true, false],
  lastTwo: [true, false],
  redeal: ["off", "noHonours"],
  chase: [true, false],
  scoring: ["points", "courts"],
  pointsTable: ["rung", "hokm"],
  target: [5, 7, 10],
  goonCourt: [3, 10],
  courtPassesCall: [true, false],
} as const satisfies { readonly [K in keyof RungRules]: readonly RungRules[K][] };

const KEYS = Object.keys(OPTIONS) as readonly (keyof RungRules)[];

/** The Rung preset (Double Sar); Quick Play plays to five, everything else to seven (RG32). */
export function defaultRungRules(mode: RungMode): RungRules {
  return {
    collect: "double",
    firstTwo: false,
    aceRule: false,
    lastTwo: false,
    redeal: "noHonours",
    chase: true,
    scoring: "points",
    pointsTable: "rung",
    target: mode === "public" ? 5 : 7,
    goonCourt: 3,
    courtPassesCall: true,
  };
}

export type RungPreset = "rung" | "courtPiece" | "hokm";

/** The three rule sets (Rung design 3) as overrides of the defaults. */
export const RUNG_PRESETS: Readonly<Record<RungPreset, Partial<RungRules>>> = {
  rung: {},
  courtPiece: { collect: "single" },
  hokm: {
    collect: "single",
    chase: false,
    redeal: "off",
    pointsTable: "hokm",
    target: 7,
    courtPassesCall: false,
  },
};

/** JSON Schema draft-07 (ADR-0021) for the toggles, served to clients and used by content validation. */
export const RUNG_RULES_SCHEMA: JsonObject = {
  $schema: "http://json-schema.org/draft-07/schema#",
  $id: "https://hulla.gg/schemas/games/rung/rules.json",
  title: "Rung rules",
  type: "object",
  additionalProperties: false,
  properties: {
    collect: { type: "string", enum: [...OPTIONS.collect], default: "double" },
    firstTwo: { type: "boolean", default: false },
    aceRule: { type: "boolean", default: false },
    lastTwo: { type: "boolean", default: false },
    redeal: { type: "string", enum: [...OPTIONS.redeal], default: "noHonours" },
    chase: { type: "boolean", default: true },
    scoring: { type: "string", enum: [...OPTIONS.scoring], default: "points" },
    pointsTable: { type: "string", enum: [...OPTIONS.pointsTable], default: "rung" },
    target: { type: "integer", enum: [...OPTIONS.target], default: 7 },
    goonCourt: { type: "integer", enum: [...OPTIONS.goonCourt], default: 3 },
    courtPassesCall: { type: "boolean", default: true },
  },
};

function isAllowed(key: keyof RungRules, value: unknown): boolean {
  return (OPTIONS[key] as readonly unknown[]).includes(value);
}

/**
 * Accepts any value. Unknown keys, values outside the options and combinations that mean nothing
 * (a pile rule without a pile) are `rules.invalid_rules` with a reason, never silently fixed.
 */
export function validateRungRules(input: unknown): Result<RungRules, RuleError> {
  if (typeof input !== "object" || input === null || Array.isArray(input)) {
    return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "not_an_object" }));
  }
  const candidate = input as Record<string, unknown>;
  for (const key of Object.keys(candidate)) {
    if (!(KEYS as readonly string[]).includes(key)) {
      return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "unknown_key", key }));
    }
  }
  for (const key of KEYS) {
    if (!(key in candidate)) {
      return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "missing_key", key }));
    }
    if (!isAllowed(key, candidate[key])) {
      return err(
        ruleError(ErrorCodes.rules.invalidRules, {
          reason: "invalid_value",
          key,
          value: String(candidate[key]),
        }),
      );
    }
  }
  // Every key is present and every value is one of its options, checked above.
  const rules = candidate as unknown as RungRules;
  if (rules.collect === "single") {
    for (const key of ["firstTwo", "aceRule", "lastTwo"] as const) {
      if (rules[key]) {
        return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "double_sar_only", key }));
      }
    }
  }
  return ok(rules);
}

/** Mode defaults, then a preset, then explicit overrides; the courts tally is for private rooms. */
export function resolveRungRules(
  mode: RungMode,
  preset: RungPreset = "rung",
  overrides: unknown = {},
): Result<RungRules, RuleError> {
  if (typeof overrides !== "object" || overrides === null || Array.isArray(overrides)) {
    return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "not_an_object" }));
  }
  for (const key of Object.keys(overrides)) {
    if (!(KEYS as readonly string[]).includes(key)) {
      return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "unknown_key", key }));
    }
  }
  const rules = validateRungRules({
    ...defaultRungRules(mode),
    ...RUNG_PRESETS[preset],
    ...overrides,
  });
  if (rules.ok && rules.value.scoring === "courts" && mode !== "private") {
    return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "courts_private_only" }));
  }
  return rules;
}
