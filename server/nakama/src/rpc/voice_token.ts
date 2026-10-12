import {
  ErrorCodes,
  ruleError,
  sha256,
  stateFromSeed,
  utf8Encode,
  Xoshiro128StarStar,
  type RuleError,
} from "@hulla/shared-ts";

import { runtimeFrom, type ServerRuntime } from "../infra/nk";
import { buildRtcToken, isAgoraId, type AgoraConfig } from "../services/agora_token";

/** Tokens last an hour; a client asks again before then (GDD 11.4). */
export const VOICE_TOKEN_SECONDS = 3_600;

export interface TableMembership {
  readonly present: boolean;
  readonly seat: number | null;
  readonly tableId: string;
}

export interface VoiceTokenDeps {
  readonly runtime: ServerRuntime;
  readonly userId: string | null;
  readonly config: AgoraConfig | null;
  membership(matchId: string): TableMembership | null;
}

export type VoiceTokenResponse =
  | {
      readonly ok: true;
      readonly provider: "agora";
      readonly channel: string;
      readonly uid: 0;
      readonly token: string;
      readonly expiresTs: number;
    }
  | { readonly ok: false; readonly error: RuleError };

/** A short-lived token for the caller's table channel; spectators may listen, seats may speak. */
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
  if (deps.config === null) return { ok: false, error: ruleError(ErrorCodes.voice.unavailable) };
  const membership = deps.membership(matchId);
  if (!membership?.present) {
    return { ok: false, error: ruleError(ErrorCodes.voice.notAtTable) };
  }
  const now = deps.runtime.nowMs();
  const rng = Xoshiro128StarStar.fromState(
    stateFromSeed(sha256(utf8Encode(deps.runtime.uuidv4()))),
  );
  const token = buildRtcToken(deps.config, {
    channel: membership.tableId,
    account: "",
    issueTs: Math.floor(now / 1000),
    expireSeconds: VOICE_TOKEN_SECONDS,
    salt: rng.nextInt(99_999_999) + 1,
    publishAudio: membership.seat !== null,
  });
  return {
    ok: true,
    provider: "agora",
    channel: membership.tableId,
    uid: 0,
    token,
    expiresTs: now + VOICE_TOKEN_SECONDS * 1000,
  };
}

/** Reads the Agora credentials from the runtime environment; absent means voice is off. */
export function agoraConfigFrom(env: Readonly<Record<string, string>>): AgoraConfig | null {
  const appId = env["AGORA_APP_ID"] ?? "";
  const appCertificate = env["AGORA_APP_CERTIFICATE"] ?? "";
  if (!isAgoraId(appId) || !isAgoraId(appCertificate)) return null;
  return { appId, appCertificate };
}

export function rpcVoiceToken(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  return JSON.stringify(
    voiceToken(
      {
        runtime: runtimeFrom(nk, logger),
        userId: ctx.userId ?? null,
        config: agoraConfigFrom(ctx.env),
        membership(matchId) {
          try {
            const answer = JSON.parse(
              nk.matchSignal(
                matchId,
                JSON.stringify({ kind: "has_user", userId: ctx.userId ?? "" }),
              ),
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
    ),
  );
}
