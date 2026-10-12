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
export const THULLA_RULES_VERSION = "1.0.0";

export type EmptyHandLeader = "drawDiscard" | "drawNextPlayer" | "instantExit";
export type HandTaking = "off" | "leaderOnly" | "anyone";
export type TwoPlayerEndgame = "drawOpponent" | "drawDiscard" | "thullaEndsRound";
export type SeriesFormat =
  "single" | "bestOf3" | "bestOf5" | "bestOf7" | "firstTo3Bhabhis" | "session";

/**
 * The toggles of GDD 6.13 that change legality or outcomes. Timers, the loser's title, card-count
 * display, the memory assist, spectators and voice are table settings, not engine rules.
 * A type alias rather than an interface so the state that embeds it stays hashable as JSON.
 */
export interface ThullaRules {
  /** 2 is reserved for 7–8 seats in v1.2; only 1 is accepted today (owner decision, 12 Oct 2026). */
  readonly deckCount: 1;
  readonly openingSimultaneous: boolean;
  readonly openingThullaApplies: boolean;
  readonly thullaEndsTrick: boolean;
  readonly emptyHandLeader: EmptyHandLeader;
  readonly handTaking: HandTaking;
  readonly twoPlayerEndgame: TwoPlayerEndgame;
  readonly confirmedFastForward: boolean;
  readonly bigBhabhiPenalty: boolean;
  readonly format: SeriesFormat;
}

export type RulesMode = "private" | "public" | "ranked";

const OPTIONS = {
  deckCount: [1],
  openingSimultaneous: [true, false],
  openingThullaApplies: [true, false],
  thullaEndsTrick: [true, false],
  emptyHandLeader: ["drawDiscard", "drawNextPlayer", "instantExit"],
  handTaking: ["off", "leaderOnly", "anyone"],
  twoPlayerEndgame: ["drawOpponent", "drawDiscard", "thullaEndsRound"],
  confirmedFastForward: [true, false],
  bigBhabhiPenalty: [true, false],
  format: ["single", "bestOf3", "bestOf5", "bestOf7", "firstTo3Bhabhis", "session"],
} as const satisfies { readonly [K in keyof ThullaRules]: readonly ThullaRules[K][] };

const KEYS = Object.keys(OPTIONS) as readonly (keyof ThullaRules)[];

/** Defaults per mode from the GDD 6.13 table. Public and Ranked turn hand-taking off (R31). */
export function defaultRules(mode: RulesMode): ThullaRules {
  return {
    deckCount: 1,
    openingSimultaneous: true,
    openingThullaApplies: false,
    thullaEndsTrick: true,
    emptyHandLeader: "drawDiscard",
    handTaking: mode === "private" ? "leaderOnly" : "off",
    twoPlayerEndgame: "drawOpponent",
    confirmedFastForward: true,
    bigBhabhiPenalty: false,
    format: mode === "private" ? "session" : "single",
  };
}

export type ThullaPreset = "standard" | "family" | "hardcore" | "classicPagat";

/** Engine-side overrides of the GDD presets; their table settings live in content/presets. */
export const THULLA_PRESETS: Readonly<Record<ThullaPreset, Partial<ThullaRules>>> = {
  standard: {},
  family: { handTaking: "leaderOnly" },
  hardcore: {
    openingThullaApplies: true,
    thullaEndsTrick: false,
    emptyHandLeader: "drawNextPlayer",
  },
  classicPagat: { twoPlayerEndgame: "drawDiscard" },
};

/** JSON Schema draft-07 (ADR-0021) for the toggles, served to clients and used by content validation. */
export const THULLA_RULES_SCHEMA: JsonObject = {
  $schema: "http://json-schema.org/draft-07/schema#",
  $id: "https://hulla.gg/schemas/games/thulla/rules.json",
  title: "Thulla rules",
  type: "object",
  additionalProperties: false,
  properties: {
    deckCount: { type: "integer", enum: [1], default: 1 },
    openingSimultaneous: { type: "boolean", default: true },
    openingThullaApplies: { type: "boolean", default: false },
    thullaEndsTrick: { type: "boolean", default: true },
    emptyHandLeader: { type: "string", enum: [...OPTIONS.emptyHandLeader], default: "drawDiscard" },
    handTaking: { type: "string", enum: [...OPTIONS.handTaking], default: "leaderOnly" },
    twoPlayerEndgame: {
      type: "string",
      enum: [...OPTIONS.twoPlayerEndgame],
      default: "drawOpponent",
    },
    confirmedFastForward: { type: "boolean", default: true },
    bigBhabhiPenalty: { type: "boolean", default: false },
    format: { type: "string", enum: [...OPTIONS.format], default: "session" },
  },
};

function isAllowed(key: keyof ThullaRules, value: unknown): boolean {
  return (OPTIONS[key] as readonly unknown[]).includes(value);
}

/** Accepts any value; unknown keys and values outside the options are `rules.invalid_rules`. */
export function validateRules(input: unknown): Result<ThullaRules, RuleError> {
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
  return ok(candidate as unknown as ThullaRules);
}

/** Mode defaults, then a preset, then explicit overrides; the result is validated. */
export function resolveRules(
  mode: RulesMode,
  preset: ThullaPreset = "standard",
  overrides: unknown = {},
): Result<ThullaRules, RuleError> {
  if (typeof overrides !== "object" || overrides === null || Array.isArray(overrides)) {
    return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "not_an_object" }));
  }
  for (const key of Object.keys(overrides)) {
    if (!(KEYS as readonly string[]).includes(key)) {
      return err(ruleError(ErrorCodes.rules.invalidRules, { reason: "unknown_key", key }));
    }
  }
  return validateRules({ ...defaultRules(mode), ...THULLA_PRESETS[preset], ...overrides });
}
