import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { ageLimitsOf, checkBirthYear, limitsFor, nkAgeStore, type AgeStore } from "../services/age";

/**
 * `age_set { birthYear }` stores the year once, from the first-launch question (GDD 24.6, #87); a
 * different year later is refused, so a player can't answer again to lift a limit (support can).
 * `age_get` says what the caller's age allows. Both answer `{ ok, known, strangersLocked, personalisedAds }`.
 */
export function setAge(store: AgeStore, userId: string, payload: string, nowMs: number): string {
  let input: unknown;
  try {
    input = payload === "" ? {} : JSON.parse(payload);
  } catch {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "json" }),
    });
  }
  const year =
    typeof input === "object" && input !== null
      ? (input as { birthYear?: unknown }).birthYear
      : undefined;
  const check = checkBirthYear(year, nowMs);
  if (!check.ok) {
    return JSON.stringify({
      ok: false,
      error:
        check.reason === "too_young"
          ? ruleError(ErrorCodes.profile.tooYoung)
          : ruleError(ErrorCodes.table.invalidPayload, { reason: "birth_year" }),
    });
  }
  const existing = store.read(userId);
  if (existing !== null && existing.birthYear !== check.birthYear) {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.profile.ageLocked) });
  }
  const record = existing ?? { birthYear: check.birthYear, setTs: nowMs };
  if (existing === null) store.write(userId, record);
  return JSON.stringify({ ok: true, ...limitsFor(record, nowMs) });
}

export function rpcAgeSet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "")
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  return setAge(nkAgeStore(nk), userId, payload, Date.now());
}

export function rpcAgeGet(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "")
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  return JSON.stringify({ ok: true, ...ageLimitsOf(nk, userId, Date.now()) });
}
