import { sha256, utf8Encode } from "@hulla/shared-ts";

import avatars from "../../../../content/avatars/v1.json";

const avatarIds = new Set(avatars.avatars.map((a) => a.id));
const frameIds = new Set(avatars.frames.map((f) => f.id));
const earnOnlyFrames = new Set(
  avatars.frames.filter((f) => "earnOnly" in f && f.earnOnly).map((f) => f.id),
);

/** Presets a profile may pick; unlockables join through the catalog later (GDD 10.1). */
export const AVATAR_IDS: readonly string[] = avatars.avatars.map((a) => a.id);
export const DEFAULT_AVATAR = AVATAR_IDS[0] ?? "a01";
export const DEFAULT_FRAME = avatars.frames[0]?.id ?? "f00";
export const isAvatar = (id: string): boolean => avatarIds.has(id);
export const isFrame = (id: string): boolean => frameIds.has(id);
/** Ranked and reward frames: a profile shows one only once it is owned. */
export const isEarnOnlyFrame = (id: string): boolean => earnOnlyFrames.has(id);

/** A new account's starting avatar: spread across the set by the user id, so tables are not all the first one. */
export function avatarForAccount(userId: string): string {
  const digest = sha256(utf8Encode(userId));
  const n = ((digest[0] ?? 0) << 8) | (digest[1] ?? 0);
  return AVATAR_IDS[n % AVATAR_IDS.length] ?? DEFAULT_AVATAR;
}
