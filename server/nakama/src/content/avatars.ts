import avatars from "../../../../content/avatars/v1.json";

const avatarIds = new Set(avatars.avatars.map((a) => a.id));
const frameIds = new Set(avatars.frames.map((f) => f.id));

/** Presets a profile may pick; unlockables join through the catalog later (GDD 10.1). */
export const DEFAULT_AVATAR = avatars.avatars[0]?.id ?? "a01";
export const DEFAULT_FRAME = avatars.frames[0]?.id ?? "f00";
export const isAvatar = (id: string): boolean => avatarIds.has(id);
export const isFrame = (id: string): boolean => frameIds.has(id);
