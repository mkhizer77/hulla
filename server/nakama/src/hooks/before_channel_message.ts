import { ErrorCodes } from "@hulla/shared-ts";

/**
 * Table text chat rides Nakama channels (ADR-0018). Before a message is delivered: at most 140
 * characters, no links or phone numbers, one message per 2 s and ten per minute per account
 * (GDD 11.1). The filters come with M2. Timestamps live in storage because hook VMs share nothing.
 */
export const CHAT_RATE_COLLECTION = "chat_rate";
export const CHAT_MAX_LENGTH = 140;
export const CHAT_MIN_GAP_MS = 2_000;
export const CHAT_PER_MINUTE = 10;

export interface RateStore {
  read(userId: string): readonly number[];
  write(userId: string, timestamps: readonly number[]): void;
}

const LINK_OR_PHONE =
  /(https?:\/\/|www\.|\b[a-z0-9-]+\.(com|net|org|io|gg|pk|in|bd|co)\b|\+?\d[\d\s-]{8,}\d)/i;

export type ChatVerdict = { readonly ok: true } | { readonly ok: false; readonly code: string };

/** The text a client sent, as Nakama hands it to the hook: `{"text": "..."}` in `content`. */
export function checkChatMessage(
  store: RateStore,
  userId: string,
  content: string,
  now: number,
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
  const recent = store.read(userId).filter((ts) => now - ts < 60_000);
  const last = recent[recent.length - 1];
  if ((last !== undefined && now - last < CHAT_MIN_GAP_MS) || recent.length >= CHAT_PER_MINUTE) {
    return { ok: false, code: ErrorCodes.network.rateLimited };
  }
  store.write(userId, [...recent, now]);
  return { ok: true };
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

/** Registered with `registerRtBefore("ChannelMessageSend", ...)`; throwing rejects the message. */
export function beforeChannelMessageSend(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  envelope: nkruntime.EnvelopeChannelMessageSend,
): nkruntime.EnvelopeChannelMessageSend {
  const userId = ctx.userId ?? "";
  const verdict = checkChatMessage(
    nkRateStore(nk),
    userId,
    envelope.channelMessageSend.content,
    Date.now(),
  );
  if (!verdict.ok) {
    logger.debug("chat message refused", { userId, code: verdict.code });
    throw new Error(verdict.code);
  }
  return envelope;
}
