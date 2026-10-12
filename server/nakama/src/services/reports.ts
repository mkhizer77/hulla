import { ErrorCodes, ruleError, type RuleError } from "@hulla/shared-ts";

import type { Profile } from "./profiles";

/**
 * Reports with automatic context (GDD 10.11, 24.3): the table, the round and the table's recent
 * chat ride along; three reports from distinct players within 24 hours, one per table, mute the
 * target for 24 hours pending review. Chat is kept seven days for this and nothing else.
 */
export const REPORTS_COLLECTION = "reports";
export const REPORT_INDEX_COLLECTION = "report_index";
export const CHAT_LOG_COLLECTION = "chat_log";
export const MODERATION_COLLECTION = "moderation";
export const SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000";
export const CHAT_RETENTION_MS = 7 * 24 * 60 * 60 * 1000;
export const CHAT_LOG_CAP = 200;
export const REPORT_WINDOW_MS = 24 * 60 * 60 * 1000;
export const AUTO_MUTE_REPORTS = 3;
export const AUTO_MUTE_MS = 24 * 60 * 60 * 1000;
export const REPORT_INDEX_CAP = 100;

export const REPORT_REASONS = [
  "cheating",
  "abusive_text",
  "abusive_voice",
  "inappropriate_profile",
  "spam",
  "underage",
] as const;
export type ReportReason = (typeof REPORT_REASONS)[number];

export interface ChatLine {
  readonly userId: string;
  readonly text: string;
  readonly ts: number;
}

export interface ReportInput {
  readonly reporterId: string | null;
  readonly targetId: string;
  readonly reason: string;
  readonly tableId: string | null;
  readonly roundId: string | null;
  readonly now: number;
}

export interface Report {
  readonly id: string;
  readonly reporterId: string;
  readonly targetId: string;
  readonly reason: ReportReason;
  readonly tableId: string | null;
  readonly roundId: string | null;
  readonly chat: readonly ChatLine[];
  readonly targetProfile: {
    readonly displayName: string;
    readonly handle: string;
    readonly avatar: string;
    readonly bio: string;
  } | null;
  readonly createdTs: number;
  readonly status: "open";
}

export interface ReportIndexEntry {
  readonly reporterId: string;
  readonly tableId: string | null;
  readonly ts: number;
}

export interface Moderation {
  readonly mutedUntilTs: number;
  readonly voiceSuspendedUntilTs?: number;
  readonly bannedUntilTs?: number;
  readonly reason: string;
  /** Every sanction ever applied, newest last (services/sanctions.ts). */
  readonly strikes?: readonly {
    readonly action: string;
    readonly reason: string;
    readonly untilTs: number | null;
    readonly by: string;
    readonly ts: number;
  }[];
}

export interface ReportStore {
  newId(): string;
  writeReport(report: Report): void;
  index(targetId: string): readonly ReportIndexEntry[];
  writeIndex(targetId: string, entries: readonly ReportIndexEntry[]): void;
  chatLog(tableId: string): readonly ChatLine[];
  moderation(userId: string): Moderation | null;
  writeModeration(userId: string, moderation: Moderation): void;
  profile(userId: string): Profile | null;
}

export type ReportResponse =
  | {
      readonly ok: true;
      readonly reportId: string;
      readonly duplicate: boolean;
      readonly chatLines: number;
      readonly autoMuted: boolean;
    }
  | { readonly ok: false; readonly error: RuleError };

export function isReportReason(value: unknown): value is ReportReason {
  return typeof value === "string" && (REPORT_REASONS as readonly string[]).includes(value);
}

/** Distinct reporters in the window, one per table (GDD 24.3). */
export function countsTowardMute(entries: readonly ReportIndexEntry[], now: number): number {
  const recent = entries.filter((e) => now - e.ts < REPORT_WINDOW_MS);
  const reporters = new Set<string>();
  const tables = new Set<string>();
  let count = 0;
  for (const entry of recent) {
    if (reporters.has(entry.reporterId)) continue;
    if (entry.tableId !== null && tables.has(entry.tableId)) continue;
    reporters.add(entry.reporterId);
    if (entry.tableId !== null) tables.add(entry.tableId);
    count += 1;
  }
  return count;
}

export function createReport(store: ReportStore, input: ReportInput): ReportResponse {
  if (input.reporterId === null) {
    return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  }
  if (input.targetId === "" || input.targetId === input.reporterId) {
    return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "user_id" }) };
  }
  if (!isReportReason(input.reason)) {
    return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "reason" }) };
  }
  const entries = store.index(input.targetId);
  const duplicate = entries.some(
    (e) => e.reporterId === input.reporterId && input.now - e.ts < REPORT_WINDOW_MS,
  );
  const chat =
    input.tableId === null
      ? []
      : store.chatLog(input.tableId).filter((line) => input.now - line.ts < CHAT_RETENTION_MS);
  const profile = store.profile(input.targetId);
  const report: Report = {
    id: store.newId(),
    reporterId: input.reporterId,
    targetId: input.targetId,
    reason: input.reason,
    tableId: input.tableId,
    roundId: input.roundId,
    chat,
    targetProfile:
      profile === null
        ? null
        : {
            displayName: profile.displayName,
            handle: profile.handle,
            avatar: profile.avatar,
            bio: profile.bio,
          },
    createdTs: input.now,
    status: "open",
  };
  store.writeReport(report);
  let autoMuted = false;
  if (!duplicate) {
    const next = [
      ...entries.filter((e) => input.now - e.ts < REPORT_WINDOW_MS).slice(-(REPORT_INDEX_CAP - 1)),
      { reporterId: input.reporterId, tableId: input.tableId, ts: input.now },
    ];
    store.writeIndex(input.targetId, next);
    const current = store.moderation(input.targetId);
    const alreadyMuted = current !== null && current.mutedUntilTs > input.now;
    if (!alreadyMuted && countsTowardMute(next, input.now) >= AUTO_MUTE_REPORTS) {
      store.writeModeration(input.targetId, {
        mutedUntilTs: input.now + AUTO_MUTE_MS,
        reason: "auto_reports",
      });
      autoMuted = true;
    }
  }
  return { ok: true, reportId: report.id, duplicate, chatLines: chat.length, autoMuted };
}

export function isMuted(moderation: Moderation | null, now: number): boolean {
  return moderation !== null && moderation.mutedUntilTs > now;
}

function isChatLine(value: unknown): value is ChatLine {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v["userId"] === "string" && typeof v["text"] === "string" && typeof v["ts"] === "number"
  );
}

export function readChatLog(nk: nkruntime.Nakama, tableId: string): readonly ChatLine[] {
  const objects = nk.storageRead([
    { collection: CHAT_LOG_COLLECTION, key: tableId, userId: SYSTEM_USER_ID },
  ]);
  const value = objects[0]?.value as { messages?: unknown } | undefined;
  return Array.isArray(value?.messages) ? value.messages.filter(isChatLine) : [];
}

/** The chat hook appends every accepted message, masked as delivered, 200 lines deep. */
export function appendChatLog(nk: nkruntime.Nakama, tableId: string, line: ChatLine): void {
  const messages = [...readChatLog(nk, tableId), line].slice(-CHAT_LOG_CAP);
  nk.storageWrite([
    {
      collection: CHAT_LOG_COLLECTION,
      key: tableId,
      userId: SYSTEM_USER_ID,
      value: { messages, updatedTs: line.ts },
      permissionRead: 0,
      permissionWrite: 0,
    },
  ]);
}

export function readModeration(nk: nkruntime.Nakama, userId: string): Moderation | null {
  const objects = nk.storageRead([{ collection: MODERATION_COLLECTION, key: "state", userId }]);
  const value = objects[0]?.value as Partial<Moderation> | undefined;
  if (typeof value?.mutedUntilTs !== "number") return null;
  return {
    mutedUntilTs: value.mutedUntilTs,
    reason: typeof value.reason === "string" ? value.reason : "",
    ...(typeof value.voiceSuspendedUntilTs === "number"
      ? { voiceSuspendedUntilTs: value.voiceSuspendedUntilTs }
      : {}),
    ...(typeof value.bannedUntilTs === "number" ? { bannedUntilTs: value.bannedUntilTs } : {}),
    ...(Array.isArray(value.strikes) ? { strikes: value.strikes } : {}),
  };
}

export function nkReportStore(
  nk: nkruntime.Nakama,
  profiles: { read(userId: string): Profile | null },
): ReportStore {
  return {
    newId: () => nk.uuidv4(),
    writeReport(report) {
      nk.storageWrite([
        {
          collection: REPORTS_COLLECTION,
          key: report.id,
          userId: SYSTEM_USER_ID,
          value: report,
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
    index(targetId) {
      const objects = nk.storageRead([
        { collection: REPORT_INDEX_COLLECTION, key: targetId, userId: SYSTEM_USER_ID },
      ]);
      const value = objects[0]?.value as { entries?: unknown } | undefined;
      return Array.isArray(value?.entries) ? (value.entries as ReportIndexEntry[]) : [];
    },
    writeIndex(targetId, entries) {
      nk.storageWrite([
        {
          collection: REPORT_INDEX_COLLECTION,
          key: targetId,
          userId: SYSTEM_USER_ID,
          value: { entries: [...entries] },
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
    chatLog: (tableId) => readChatLog(nk, tableId),
    moderation: (userId) => readModeration(nk, userId),
    writeModeration(userId, moderation) {
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
    },
    profile: (userId) => profiles.read(userId),
  };
}

/** The newest reports for triage; a guarded page walk, newest first. */
export function listReports(nk: nkruntime.Nakama, limit: number): Report[] {
  const out: Report[] = [];
  let cursor: string | undefined;
  for (let page = 0; page < 10 && out.length < limit; page += 1) {
    const result = nk.storageList(SYSTEM_USER_ID, REPORTS_COLLECTION, 100, cursor);
    const objects = result.objects ?? [];
    for (const object of objects) {
      if (object.collection !== REPORTS_COLLECTION) continue;
      const value = object.value as Report;
      if (typeof value.targetId === "string" && !out.some((r) => r.id === value.id))
        out.push(value);
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
  return out.sort((a, b) => b.createdTs - a.createdTs).slice(0, limit);
}
