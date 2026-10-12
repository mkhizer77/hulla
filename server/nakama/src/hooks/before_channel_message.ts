import { ErrorCodes } from "@hulla/shared-ts";

import {
  buildMatcher,
  filterText,
  isGuestAccount,
  nkFilterStore,
  roomOfChannel,
  type Matcher,
  type Strictness,
} from "../services/chat_filter";

/**
 * Table text chat rides Nakama channels (ADR-0018). Before a message is delivered: at most 140
 * characters, no links or phone numbers, one message per 2 s and ten per minute per account, the
 * profanity filter (Standard masks, Family blocks) and the guest rule: no free text with strangers
 * until the account is linked (GDD 9.4, 11.1). Timestamps live in storage because hook VMs share
 * nothing.
 */
export const CHAT_RATE_COLLECTION = "chat_rate";
export const CHAT_MAX_LENGTH = 140;
export const CHAT_MIN_GAP_MS = 2_000;
export const CHAT_PER_MINUTE = 10;

export interface RateStore {
  read(userId: string): readonly number[];
  write(userId: string, timestamps: readonly number[]): void;
}

export interface ChatPolicy {
  readonly matcher: Matcher | null;
  readonly strictness: Strictness;
  /** A guest at a public table: free text is off until the account is linked. */
  readonly guestLocked: boolean;
}

export const OPEN_POLICY: ChatPolicy = {
  matcher: null,
  strictness: "standard",
  guestLocked: false,
};

const LINK_OR_PHONE =
  /(https?:\/\/|www\.|\b[a-z0-9-]+\.(com|net|org|io|gg|pk|in|bd|co)\b|\+?\d[\d\s-]{8,}\d)/i;

export type ChatVerdict =
  | { readonly ok: true; readonly content: string; readonly masked: number }
  | { readonly ok: false; readonly code: string };

/** The text a client sent, as Nakama hands it to the hook: `{"text": "..."}` in `content`. */
export function checkChatMessage(
  store: RateStore,
  userId: string,
  content: string,
  now: number,
  policy: ChatPolicy = OPEN_POLICY,
): ChatVerdict {
  let text: unknown;
  try {
    text = (JSON.parse(content) as { text?: unknown }).text;
  } catch {
    return { ok: false, code: ErrorCodes.table.invalidPayload };
  }
  if (typeof text !== "string" || text.trim().length === 0 || text.length > CHAT_MAX_LENGTH) {
    return { ok: false, code: ErrorCodes.table.invalidPayload };
  }
  if (LINK_OR_PHONE.test(text)) return { ok: false, code: ErrorCodes.table.invalidPayload };
  if (policy.guestLocked) return { ok: false, code: ErrorCodes.auth.notLinked };
  const filtered = policy.matcher === null ? { text, hits: 0 } : filterText(policy.matcher, text);
  if (filtered.hits > 0 && policy.strictness === "family") {
    return { ok: false, code: ErrorCodes.content.filtered };
  }
  const recent = store.read(userId).filter((ts) => now - ts < 60_000);
  const last = recent[recent.length - 1];
  if ((last !== undefined && now - last < CHAT_MIN_GAP_MS) || recent.length >= CHAT_PER_MINUTE) {
    return { ok: false, code: ErrorCodes.network.rateLimited };
  }
  store.write(userId, [...recent, now]);
  return { ok: true, content: JSON.stringify({ text: filtered.text }), masked: filtered.hits };
}

export function nkRateStore(nk: nkruntime.Nakama): RateStore {
  return {
    read(userId) {
      const objects = nk.storageRead([{ collection: CHAT_RATE_COLLECTION, key: "chat", userId }]);
      const value = objects[0]?.value as { ts?: unknown } | undefined;
      return Array.isArray(value?.ts) ? (value.ts as number[]) : [];
    },
    write(userId, timestamps) {
      nk.storageWrite([
        {
          collection: CHAT_RATE_COLLECTION,
          key: "chat",
          userId,
          value: { ts: [...timestamps] },
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
  };
}

interface RoomLabel {
  readonly mode?: unknown;
  readonly preset?: unknown;
}

/** Family rooms block; guests at public tables may not free-text (GDD 9.4, 11.1). */
function policyFor(
  nk: nkruntime.Nakama,
  logger: nkruntime.Logger,
  userId: string,
  channelId: string,
): ChatPolicy {
  let strictness: Strictness = "standard";
  let publicTable = false;
  const room = roomOfChannel(channelId);
  if (room !== null) {
    try {
      const match = nk.matchGet(room);
      const label = match === null ? null : (JSON.parse(match.label) as RoomLabel);
      if (label?.preset === "family") strictness = "family";
      if (label !== null && label.mode !== "private") publicTable = true;
    } catch (error: unknown) {
      logger.warn("chat room not resolved", { error: String(error) });
    }
  }
  let guest = false;
  if (publicTable) {
    try {
      guest = isGuestAccount(nk.accountGetId(userId));
    } catch (error: unknown) {
      logger.warn("chat sender not resolved", { error: String(error) });
    }
  }
  return { matcher: buildMatcher(nkFilterStore(nk).read()), strictness, guestLocked: guest };
}

/** Registered with `registerRtBefore("ChannelMessageSend", ...)`; throwing rejects the message. */
export function beforeChannelMessageSend(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  envelope: nkruntime.EnvelopeChannelMessageSend,
): nkruntime.EnvelopeChannelMessageSend {
  const userId = ctx.userId ?? "";
  const policy = policyFor(nk, logger, userId, envelope.channelMessageSend.channelId);
  const verdict = checkChatMessage(
    nkRateStore(nk),
    userId,
    envelope.channelMessageSend.content,
    Date.now(),
    policy,
  );
  if (!verdict.ok) {
    logger.debug("chat message refused", { userId, code: verdict.code });
    throw new Error(verdict.code);
  }
  if (verdict.masked > 0) envelope.channelMessageSend.content = verdict.content;
  return envelope;
}
