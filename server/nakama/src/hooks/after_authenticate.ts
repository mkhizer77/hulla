import { sha256, stateFromSeed, utf8Encode, Xoshiro128StarStar } from "@hulla/shared-ts";

import { nkPresenceStore, touch } from "../services/presence";
import { bootstrapProfile, nkProfileStore } from "../services/profiles";
import { forDay, nkProgressStore, touchLogin } from "../services/progression";

/** Every sign-in path lands here: a first sign-in gets a profile and its friend code (ARCHITECTURE 8.10). */
function bootstrap(ctx: nkruntime.Context, logger: nkruntime.Logger, nk: nkruntime.Nakama): void {
  const userId = ctx.userId;
  if (userId === undefined || userId === "") return;
  try {
    touch(nkPresenceStore(nk), userId, Date.now());
  } catch (error: unknown) {
    logger.warn("presence not touched", { error: String(error) });
  }
  try {
    const progress = nkProgressStore(nk);
    const now = Date.now();
    const before = progress.read(userId);
    const after = touchLogin(forDay(before, now), now);
    if (after !== before) progress.write(userId, after);
  } catch (error: unknown) {
    logger.warn("login streak not updated", { error: String(error) });
  }
  const store = nkProfileStore(nk, logger);
  if (store.read(userId) !== null) return;
  const handle = ctx.username ?? userId.slice(0, 8);
  const rng = Xoshiro128StarStar.fromState(stateFromSeed(sha256(utf8Encode(nk.uuidv4()))));
  const profile = bootstrapProfile(store, rng, userId, handle, Date.now());
  try {
    nk.accountUpdateId(userId, null, profile.displayName, null, null, null, profile.avatar, null);
  } catch (error: unknown) {
    logger.warn("display name not mirrored", { error: String(error) });
  }
  logger.info("profile created", { userId });
}

export function afterAuthenticateDevice(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): void {
  bootstrap(ctx, logger, nk);
}

export function afterAuthenticateApple(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): void {
  bootstrap(ctx, logger, nk);
}

export function afterAuthenticateGoogle(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): void {
  bootstrap(ctx, logger, nk);
}

export function afterAuthenticateFacebook(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): void {
  bootstrap(ctx, logger, nk);
}

export function afterAuthenticateCustom(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): void {
  bootstrap(ctx, logger, nk);
}
