import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { nkLookFeedbackStore, parseFeedback, summarise } from "../services/look_feedback";
import { audit } from "../services/sanctions";

import { isAdmin } from "./admin";

function parse(payload: string): Record<string, unknown> | null {
  try {
    const value: unknown = payload === "" ? {} : JSON.parse(payload);
    return typeof value === "object" && value !== null && !Array.isArray(value)
      ? (value as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}

const refused = (): string =>
  JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });

const invalid = (reason: string): string =>
  JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.invalidPayload, { reason }) });

/**
 * `{ look, rating, keep, comment?, rounds? }`: the caller's answer on a test look; a later answer
 * on the same look replaces the earlier one.
 */
export function rpcLookFeedback(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return refused();
  const input = parse(payload);
  if (input === null) return invalid("json");
  const parsed = parseFeedback(input);
  if (!parsed.ok) return invalid(parsed.reason);
  nkLookFeedbackStore(nk).write(userId, { ...parsed.feedback, at: Date.now() });
  return JSON.stringify({ ok: true });
}

/** Per look: answers, average rating, the share that would keep it and the newest comments. */
export function rpcAdminLookFeedback(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  if (!isAdmin(ctx)) return refused();
  const answers = nkLookFeedbackStore(nk).listAll();
  audit(nk, {
    actorId: ctx.userId === undefined || ctx.userId === "" ? "server_key" : ctx.userId,
    action: "look_feedback_read",
    targetId: null,
    detail: { answers: answers.length },
    ts: Date.now(),
  });
  return JSON.stringify({ ok: true, looks: summarise(answers) });
}
