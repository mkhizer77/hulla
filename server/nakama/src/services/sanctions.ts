import { MODERATION_COLLECTION, readModeration, SYSTEM_USER_ID, type Moderation } from "./reports";

/**
 * Strikes and bans with expiry (GDD 24.3, ARCHITECTURE 8.11): a warning, a text mute, a voice
 * suspension or an account ban, each until a time or for good; device bans stop the phone itself.
 * Every admin action is written to the audit log.
 */
export const DEVICE_BANS_COLLECTION = "device_bans";
export const AUDIT_COLLECTION = "audit";
export const PERMANENT_TS = 8_640_000_000_000_000;
export const STRIKE_CAP = 50;

export const SANCTION_ACTIONS = ["warning", "mute", "voice_suspend", "ban", "clear"] as const;
export type SanctionAction = (typeof SANCTION_ACTIONS)[number];

export interface Strike {
  readonly action: SanctionAction;
  readonly reason: string;
  readonly untilTs: number | null;
  readonly by: string;
  readonly ts: number;
}

export interface SanctionInput {
  readonly action: SanctionAction;
  readonly reason: string;
  /** Hours from now; null with `permanent` false leaves the current expiry as it is. */
  readonly hours: number | null;
  readonly permanent: boolean;
  readonly by: string;
  readonly now: number;
}

export function isSanctionAction(value: unknown): value is SanctionAction {
  return typeof value === "string" && (SANCTION_ACTIONS as readonly string[]).includes(value);
}

export function untilFor(input: Pick<SanctionInput, "hours" | "permanent" | "now">): number | null {
  if (input.permanent) return PERMANENT_TS;
  if (input.hours === null || input.hours <= 0) return null;
  return input.now + input.hours * 60 * 60 * 1000;
}

export const EMPTY_MODERATION: Moderation = { mutedUntilTs: 0, reason: "" };

/** The record after an action; `clear` lifts every active sanction and keeps the history. */
export function applySanction(current: Moderation | null, input: SanctionInput): Moderation {
  const base = current ?? EMPTY_MODERATION;
  const until = untilFor(input);
  const strike: Strike = {
    action: input.action,
    reason: input.reason,
    untilTs: until,
    by: input.by,
    ts: input.now,
  };
  const strikes = [...(base.strikes ?? []).slice(-(STRIKE_CAP - 1)), strike];
  switch (input.action) {
    case "warning":
      return { ...base, strikes, reason: input.reason };
    case "mute":
      return { ...base, strikes, mutedUntilTs: until ?? base.mutedUntilTs, reason: input.reason };
    case "voice_suspend":
      return {
        ...base,
        strikes,
        voiceSuspendedUntilTs: until ?? base.voiceSuspendedUntilTs ?? 0,
        reason: input.reason,
      };
    case "ban":
      return {
        ...base,
        strikes,
        bannedUntilTs: until ?? base.bannedUntilTs ?? 0,
        reason: input.reason,
      };
    case "clear":
      return {
        ...base,
        strikes,
        mutedUntilTs: 0,
        voiceSuspendedUntilTs: 0,
        bannedUntilTs: 0,
        reason: input.reason,
      };
  }
}

export function isBanned(moderation: Moderation | null, now: number): boolean {
  return moderation !== null && (moderation.bannedUntilTs ?? 0) > now;
}

export function isVoiceSuspended(moderation: Moderation | null, now: number): boolean {
  return moderation !== null && (moderation.voiceSuspendedUntilTs ?? 0) > now;
}

export interface SanctionsView {
  readonly mutedUntilTs: number | null;
  readonly voiceSuspendedUntilTs: number | null;
  readonly bannedUntilTs: number | null;
  readonly warnings: number;
}

/** What the player may see of their own record: active sanctions and their expiry. */
export function sanctionsView(moderation: Moderation | null, now: number): SanctionsView {
  const until = (ts: number | undefined): number | null =>
    ts !== undefined && ts > now ? ts : null;
  return {
    mutedUntilTs: until(moderation?.mutedUntilTs),
    voiceSuspendedUntilTs: until(moderation?.voiceSuspendedUntilTs),
    bannedUntilTs: until(moderation?.bannedUntilTs),
    warnings: (moderation?.strikes ?? []).filter((s) => s.action === "warning").length,
  };
}

export function writeModeration(
  nk: nkruntime.Nakama,
  userId: string,
  moderation: Moderation,
): void {
  nk.storageWrite([
    {
      collection: MODERATION_COLLECTION,
      key: "state",
      userId,
      value: moderation,
      permissionRead: 1,
      permissionWrite: 0,
    },
  ]);
}

export function isDeviceBanned(nk: nkruntime.Nakama, deviceId: string, now: number): boolean {
  const objects = nk.storageRead([
    { collection: DEVICE_BANS_COLLECTION, key: deviceId, userId: SYSTEM_USER_ID },
  ]);
  const value = objects[0]?.value as { untilTs?: unknown } | undefined;
  return typeof value?.untilTs === "number" && value.untilTs > now;
}

export function banDevices(
  nk: nkruntime.Nakama,
  deviceIds: readonly string[],
  untilTs: number,
  reason: string,
): void {
  if (deviceIds.length === 0) return;
  nk.storageWrite(
    deviceIds.map((deviceId) => ({
      collection: DEVICE_BANS_COLLECTION,
      key: deviceId,
      userId: SYSTEM_USER_ID,
      value: { untilTs, reason },
      permissionRead: 0,
      permissionWrite: 0,
    })),
  );
}

export function liftDevices(nk: nkruntime.Nakama, deviceIds: readonly string[]): void {
  if (deviceIds.length === 0) return;
  nk.storageDelete(
    deviceIds.map((deviceId) => ({
      collection: DEVICE_BANS_COLLECTION,
      key: deviceId,
      userId: SYSTEM_USER_ID,
    })),
  );
}

export interface AuditEntry {
  readonly id: string;
  readonly actorId: string;
  readonly action: string;
  readonly targetId: string | null;
  readonly detail: Record<string, unknown>;
  readonly ts: number;
}

export function audit(nk: nkruntime.Nakama, entry: Omit<AuditEntry, "id">): AuditEntry {
  const full: AuditEntry = { id: nk.uuidv4(), ...entry };
  nk.storageWrite([
    {
      collection: AUDIT_COLLECTION,
      key: `${String(entry.ts).padStart(15, "0")}-${full.id}`,
      userId: SYSTEM_USER_ID,
      value: full,
      permissionRead: 0,
      permissionWrite: 0,
    },
  ]);
  return full;
}

/** The newest audit rows, a guarded page walk. */
export function listAudit(nk: nkruntime.Nakama, limit: number): AuditEntry[] {
  const out: AuditEntry[] = [];
  let cursor: string | undefined;
  for (let page = 0; page < 10; page += 1) {
    const result = nk.storageList(SYSTEM_USER_ID, AUDIT_COLLECTION, 100, cursor);
    const objects = result.objects ?? [];
    for (const object of objects) {
      if (object.collection !== AUDIT_COLLECTION) continue;
      const value = object.value as AuditEntry;
      if (typeof value.action === "string" && !out.some((e) => e.id === value.id)) out.push(value);
    }
    if (
      objects.length < 100 ||
      result.cursor === undefined ||
      result.cursor === "" ||
      result.cursor === cursor
    ) {
      break;
    }
    cursor = result.cursor;
  }
  return out.sort((a, b) => b.ts - a.ts).slice(0, limit);
}

export { readModeration };
