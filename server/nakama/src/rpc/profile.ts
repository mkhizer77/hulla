import { createChecks } from "@hulla/protocol/generated";
import { ErrorCodes, ruleError, type RuleError } from "@hulla/shared-ts";

import { log } from "../infra/log";
import { runtimeFrom, type ServerRuntime } from "../infra/nk";
import { isUserOnline } from "../services/presence";
import {
  applyProfileUpdate,
  nkAreFriends,
  nkProfileStore,
  profileView,
  type ProfileStore,
  type Relation,
} from "../services/profiles";

import type { Profile as ProfileView, ProfileUpdate } from "@hulla/protocol/generated";

export interface ProfileDeps {
  readonly runtime: ServerRuntime;
  readonly userId: string | null;
  readonly store: ProfileStore;
  relation(viewerId: string, userId: string): Relation;
  isOnline(userId: string): boolean;
  userIdForHandle(handle: string): string | null;
  /** Mirrors the handle and display name onto the Nakama account; false when the handle is taken. */
  setAccountNames(userId: string, handle: string | null, displayName: string | null): boolean;
}

export type ProfileResponse =
  | { readonly ok: true; readonly profile: ProfileView }
  | { readonly ok: false; readonly error: RuleError };

function parse(payload: string): Record<string, unknown> | null {
  try {
    const parsed: unknown = payload === "" ? {} : JSON.parse(payload);
    return typeof parsed === "object" && parsed !== null && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}

/** The caller's own profile, or someone else's card as the privacy setting allows (GDD 10.1). */
export function profileGet(deps: ProfileDeps, payload: string): ProfileResponse {
  if (deps.userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  const input = parse(payload);
  const checks = createChecks();
  if (input === null || !checks.profileGet(input)) {
    return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "schema" }) };
  }
  const handle = input["handle"];
  const byHandle = typeof handle === "string" ? deps.userIdForHandle(handle) : null;
  if (typeof handle === "string" && byHandle === null) {
    return { ok: false, error: ruleError(ErrorCodes.profile.notFound) };
  }
  const target = byHandle ?? (typeof input["userId"] === "string" ? input["userId"] : deps.userId);
  const profile = deps.store.read(target);
  if (profile === null) return { ok: false, error: ruleError(ErrorCodes.profile.notFound) };
  const relation = target === deps.userId ? "self" : deps.relation(deps.userId, target);
  const view = profileView(target, profile, relation, deps.isOnline(target));
  if (view === null) return { ok: false, error: ruleError(ErrorCodes.profile.notVisible) };
  return { ok: true, profile: view };
}

/** Edits the caller's profile; the handle must be unique across accounts. */
export function profileUpdate(deps: ProfileDeps, payload: string): ProfileResponse {
  if (deps.userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  const input = parse(payload);
  const checks = createChecks();
  if (input === null || !checks.profileUpdate(input)) {
    return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "schema" }) };
  }
  const update = input as ProfileUpdate;
  const current = deps.store.read(deps.userId);
  if (current === null) return { ok: false, error: ruleError(ErrorCodes.profile.notFound) };
  const applied = applyProfileUpdate(current, update, deps.runtime.nowMs());
  if (!applied.ok) return applied;
  const next = applied.value;
  const handleChanged = next.handle !== current.handle;
  const nameChanged = next.displayName !== current.displayName;
  if (handleChanged || nameChanged) {
    const mirrored = deps.setAccountNames(
      deps.userId,
      handleChanged ? next.handle : null,
      nameChanged ? next.displayName : null,
    );
    if (!mirrored)
      return {
        ok: false,
        error: ruleError(ErrorCodes.profile.handleTaken, { handle: next.handle }),
      };
  }
  deps.store.write(deps.userId, next);
  const view = profileView(deps.userId, next, "self", deps.isOnline(deps.userId));
  return view === null
    ? { ok: false, error: ruleError(ErrorCodes.profile.notFound) }
    : { ok: true, profile: view };
}

function nkDeps(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): ProfileDeps {
  return {
    runtime: runtimeFrom(nk, logger),
    userId: ctx.userId ?? null,
    store: nkProfileStore(nk, logger),
    relation: (viewerId, userId) => (nkAreFriends(nk, viewerId, userId) ? "friend" : "other"),
    isOnline: (userId) => isUserOnline(nk, userId),
    userIdForHandle(handle) {
      try {
        return nk.usersGetUsername([handle])[0]?.userId ?? null;
      } catch {
        return null;
      }
    },
    setAccountNames(userId, handle, displayName) {
      try {
        nk.accountUpdateId(userId, handle, displayName, null, null, null, null, null);
        return true;
      } catch (error: unknown) {
        log.info(logger, "account names not updated", { error: String(error) });
        return false;
      }
    },
  };
}

export function rpcProfileGet(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  return JSON.stringify(profileGet(nkDeps(ctx, logger, nk), payload));
}

export function rpcProfileUpdate(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  return JSON.stringify(profileUpdate(nkDeps(ctx, logger, nk), payload));
}
