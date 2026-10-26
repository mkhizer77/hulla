import { ErrorCodes, ruleError, type RuleError } from "@hulla/shared-ts";

import { log } from "../infra/log";
import { runtimeFrom, type ServerRuntime } from "../infra/nk";
import { buildRoomToken, liveKitConfigFrom, type LiveKitConfig } from "../services/livekit_token";
import { recordMissionMetrics } from "../services/missions";
import { isVoiceSuspended, readModeration } from "../services/sanctions";

/**
 * Long enough to join; LiveKit refreshes the token of a connected client, and a client removed from the
 * room (left or kicked) can't come back on an old one for longer than this (ADR-0024).
 */
export const VOICE_TOKEN_SECONDS = 600;

export interface TableMembership {
  readonly present: boolean;
  readonly seat: number | null;
  readonly tableId: string;
}

export interface VoiceTokenDeps {
  readonly runtime: ServerRuntime;
  readonly userId: string | null;
  readonly config: LiveKitConfig | null;
  /** A voice suspension from moderation (GDD 24.3). */
  readonly suspended?: boolean;
  membership(matchId: string): TableMembership | null;
}

export type VoiceTokenResponse =
  | {
      readonly ok: true;
      readonly provider: "livekit";
      /** The LiveKit server the client connects to. */
      readonly url: string;
      /** The table's room. */
      readonly channel: string;
      /** The identity the token is bound to (the Nakama user id); speaking rings map back to seats by it. */
      readonly account: string;
      readonly token: string;
      readonly expiresTs: number;
    }
  | { readonly ok: false; readonly error: RuleError };

/**
 * A short-lived token for the caller's table channel, bound to their user id so a token can't be
 * lent to another account; spectators may listen, seats may speak.
 */
export function voiceToken(deps: VoiceTokenDeps, payload: string): VoiceTokenResponse {
  if (deps.userId === null) return { ok: false, error: ruleError(ErrorCodes.table.notAuthorised) };
  let matchId: unknown;
  try {
    const parsed: unknown = payload === "" ? {} : JSON.parse(payload);
    matchId =
      typeof parsed === "object" && parsed !== null
        ? (parsed as { matchId?: unknown }).matchId
        : undefined;
  } catch {
    return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "json" }) };
  }
  if (typeof matchId !== "string" || matchId === "") {
    return { ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason: "match_id" }) };
  }
  if (deps.suspended === true) return { ok: false, error: ruleError(ErrorCodes.voice.suspended) };
  if (deps.config === null) return { ok: false, error: ruleError(ErrorCodes.voice.unavailable) };
  const membership = deps.membership(matchId);
  if (!membership?.present) {
    return { ok: false, error: ruleError(ErrorCodes.voice.notAtTable) };
  }
  const now = deps.runtime.nowMs();
  const token = buildRoomToken(deps.config, {
    // The match id, not the table code: a code can come back for a new table.
    room: matchId,
    identity: deps.userId,
    issueTs: Math.floor(now / 1000),
    expireSeconds: VOICE_TOKEN_SECONDS,
    publishAudio: membership.seat !== null,
  });
  return {
    ok: true,
    provider: "livekit",
    url: deps.config.url,
    channel: matchId,
    account: deps.userId,
    token,
    expiresTs: now + VOICE_TOKEN_SECONDS * 1000,
  };
}

export function rpcVoiceToken(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const response = voiceTokenFor(ctx, logger, nk, payload);
  if (response.ok) noteVoiceInPrivateRoom(ctx, logger, nk, response.channel);
  return JSON.stringify(response);
}

/** Voice in a private room counts for the mission of GDD 12.5. */
function noteVoiceInPrivateRoom(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  matchId: string,
): void {
  const userId = ctx.userId ?? "";
  if (userId === "") return;
  try {
    const match = nk.matchGet(matchId);
    const label =
      match?.label === undefined ? null : (JSON.parse(match.label) as { mode?: unknown });
    if (label?.mode !== "private") return;
    recordMissionMetrics(nk, userId, { voice_private: 1 }, Date.now());
  } catch (error: unknown) {
    log.warn(logger, "voice mission not noted", { error: String(error) });
  }
}

function voiceTokenFor(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): VoiceTokenResponse {
  return voiceToken(
    {
      runtime: runtimeFrom(nk, logger),
      userId: ctx.userId ?? null,
      config: liveKitConfigFrom(ctx.env),
      suspended: isVoiceSuspended(readModeration(nk, ctx.userId ?? ""), Date.now()),
      membership(matchId) {
        try {
          const answer = JSON.parse(
            nk.matchSignal(matchId, JSON.stringify({ kind: "has_user", userId: ctx.userId ?? "" })),
          ) as { ok?: boolean; present?: boolean; seat?: number | null; tableId?: string };
          if (answer.ok !== true || typeof answer.tableId !== "string") return null;
          return {
            present: answer.present === true,
            seat: answer.seat ?? null,
            tableId: answer.tableId,
          };
        } catch {
          return null;
        }
      },
    },
    payload,
  );
}
