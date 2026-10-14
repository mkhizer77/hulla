import { ErrorCodes, ruleError, type RuleError } from "@hulla/shared-ts";

/**
 * Remote configuration on the server (GDD 22.5, 20.15, ARCHITECTURE 12.2): feature flags and
 * numbers per region and cohort, and the minimum client version, in one storage object the
 * owner replaces live with `config_set`. The bundled defaults apply until then; Firebase Remote
 * Config carries the client-side experiments and is the Unity part.
 */
export const CONFIG_COLLECTION = "remote_config";
export const CONFIG_KEY = "current";
export const SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000";

export interface FlagRule {
  /** `*` or a region id such as `eu` or `asia`. */
  readonly region?: string;
  /** `*` or a cohort id; cohorts come from the client's analytics assignment. */
  readonly cohort?: string;
  readonly value: boolean | number | string;
}

export interface RemoteConfig {
  readonly version: number;
  /** Clients older than this are told to update and cannot join a table (GDD 20.15). */
  readonly minClientVersion: string;
  readonly flags: Readonly<Record<string, readonly FlagRule[]>>;
  readonly updatedTs: number;
}

export const DEFAULT_CONFIG: RemoteConfig = {
  version: 1,
  minClientVersion: "0.1.0",
  flags: {
    maintenance: [{ value: false }],
    quick_play: [{ value: true }],
    voice: [{ value: true }],
    shop: [{ value: false }],
    rewarded_ads: [{ value: false }],
    interstitials: [{ value: true }],
    // The placements and caps of GDD 13.6; the server enforces the rewarded caps per local day.
    ad_cap_double_coins: [{ value: 5 }],
    ad_cap_reroll: [{ value: 2 }],
    ad_cap_theme_trial: [{ value: 1 }],
    interstitial_from_round: [{ value: 4 }],
    interstitial_interval_seconds: [{ value: 240 }],
    turn_seconds_private: [{ value: 20 }],
    turn_seconds_public: [{ value: 15 }],
    backfill_delay_seconds: [{ value: 20 }],
    daily_soft_cap_coins: [{ value: 1500 }],
    // Phone sign-in sends at most this many codes a day across all players (cost cap, OQ-041).
    otp_daily_budget: [{ value: 2000 }],
  },
  updatedTs: 0,
};

/** Numeric triples with an optional pre-release tag: `1.2.3`, `1.2.3-dev`. */
const VERSION = /^(\d+)\.(\d+)\.(\d+)(?:-[0-9A-Za-z.-]+)?$/;

export function parseVersion(value: unknown): readonly [number, number, number] | null {
  if (typeof value !== "string") return null;
  const m = VERSION.exec(value.trim());
  if (m === null) return null;
  return [Number(m[1]), Number(m[2]), Number(m[3])];
}

/** Negative when `a` is older than `b`, zero when equal, positive when newer. */
export function compareVersions(a: string, b: string): number {
  const pa = parseVersion(a) ?? [0, 0, 0];
  const pb = parseVersion(b) ?? [0, 0, 0];
  for (let i = 0; i < 3; i += 1) {
    const d = (pa[i] ?? 0) - (pb[i] ?? 0);
    if (d !== 0) return d;
  }
  return 0;
}

/** A client that sends no version is treated as current: the rule bites only on a known old build. */
export function clientTooOld(clientVersion: unknown, config: RemoteConfig): boolean {
  const parsed = parseVersion(clientVersion);
  if (parsed === null) return false;
  return compareVersions(String(clientVersion), config.minClientVersion) < 0;
}

export interface Audience {
  readonly region: string;
  readonly cohort: string;
}

function matches(rule: FlagRule, audience: Audience): boolean {
  const region = rule.region ?? "*";
  const cohort = rule.cohort ?? "*";
  return (
    (region === "*" || region === audience.region) && (cohort === "*" || cohort === audience.cohort)
  );
}

/** The most specific matching rule wins: region and cohort, then region, then cohort, then `*`. */
export function resolveFlag(
  config: RemoteConfig,
  name: string,
  audience: Audience,
): boolean | number | string | null {
  const rules = config.flags[name];
  if (rules === undefined) return null;
  const specificity = (r: FlagRule): number =>
    ((r.region ?? "*") === "*" ? 0 : 2) + ((r.cohort ?? "*") === "*" ? 0 : 1);
  let best: FlagRule | null = null;
  for (const rule of rules) {
    if (!matches(rule, audience)) continue;
    if (best === null || specificity(rule) > specificity(best)) best = rule;
  }
  return best === null ? null : best.value;
}

export function resolveAll(
  config: RemoteConfig,
  audience: Audience,
): Record<string, boolean | number | string> {
  const out: Record<string, boolean | number | string> = {};
  for (const name of Object.keys(config.flags)) {
    const value = resolveFlag(config, name, audience);
    if (value !== null) out[name] = value;
  }
  return out;
}

export function isRemoteConfig(value: unknown): value is RemoteConfig {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  if (typeof v["version"] !== "number" || parseVersion(v["minClientVersion"]) === null)
    return false;
  const flags = v["flags"];
  if (typeof flags !== "object" || flags === null) return false;
  return Object.values(flags as Record<string, unknown>).every(
    (rules) =>
      Array.isArray(rules) &&
      rules.every((r: unknown) => {
        if (typeof r !== "object" || r === null) return false;
        const rule = r as Record<string, unknown>;
        const value = rule["value"];
        const okValue =
          typeof value === "boolean" || typeof value === "number" || typeof value === "string";
        const okRegion = rule["region"] === undefined || typeof rule["region"] === "string";
        const okCohort = rule["cohort"] === undefined || typeof rule["cohort"] === "string";
        return okValue && okRegion && okCohort;
      }),
  );
}

export type ConfigSetResponse =
  | { readonly ok: true; readonly version: number }
  | { readonly ok: false; readonly error: RuleError };

export function validateConfig(input: unknown): ConfigSetResponse {
  if (!isRemoteConfig(input)) {
    return {
      ok: false,
      error: ruleError(ErrorCodes.content.schemaInvalid, { reason: "remote_config" }),
    };
  }
  return { ok: true, version: input.version };
}

export interface ConfigStore {
  read(): RemoteConfig;
  write(config: RemoteConfig): void;
}

export function nkConfigStore(nk: nkruntime.Nakama): ConfigStore {
  return {
    read() {
      try {
        const objects = nk.storageRead([
          { collection: CONFIG_COLLECTION, key: CONFIG_KEY, userId: SYSTEM_USER_ID },
        ]);
        const value: unknown = objects[0]?.value;
        return isRemoteConfig(value) ? value : DEFAULT_CONFIG;
      } catch {
        return DEFAULT_CONFIG;
      }
    },
    write(config) {
      nk.storageWrite([
        {
          collection: CONFIG_COLLECTION,
          key: CONFIG_KEY,
          userId: SYSTEM_USER_ID,
          value: config,
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
  };
}
