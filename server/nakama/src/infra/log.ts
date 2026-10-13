/**
 * Structured logging (ARCHITECTURE 12.6): one message, fields as JSON keys through the logger's
 * field API (Nakama's `info(format, ...args)` would fold an object into the message), no PII, no
 * hands, no chat text. A field the policy forbids is redacted, never dropped silently, so a test
 * can see it and the line still tells what happened.
 */
export interface FieldLogger {
  debug(message: string, ...args: unknown[]): unknown;
  info(message: string, ...args: unknown[]): unknown;
  warn(message: string, ...args: unknown[]): unknown;
  error(message: string, ...args: unknown[]): unknown;
  withFields?(fields: Record<string, string>): FieldLogger;
}

export type Level = "debug" | "info" | "warn" | "error";

export const REDACTED = "[redacted]";

const FORBIDDEN_KEYS = new Set([
  "name",
  "displayName",
  "handle",
  "username",
  "email",
  "text",
  "content",
  "message",
  "hand",
  "hands",
  "cards",
  "deck",
  "deckOrder",
  "token",
  "password",
  "secret",
  "ip",
  "deviceId",
]);

const CARD = /^(10|[2-9TJQKA])[SHCD]$/;

/** Why a field must not be logged, or null when it may. */
export function forbiddenField(key: string, value: unknown): string | null {
  if (FORBIDDEN_KEYS.has(key)) return `key ${key}`;
  if (typeof value === "string" && CARD.test(value)) return `card in ${key}`;
  if (Array.isArray(value) && value.some((v) => typeof v === "string" && CARD.test(v))) {
    return `card list in ${key}`;
  }
  return null;
}

function flatten(fields: Record<string, unknown>): Record<string, string> {
  const out: Record<string, string> = {};
  for (const [key, value] of Object.entries(fields)) {
    if (value === undefined) continue;
    if (forbiddenField(key, value) !== null) {
      out[key] = REDACTED;
      continue;
    }
    out[key] = typeof value === "string" ? value : JSON.stringify(value);
  }
  return out;
}

function write(
  logger: FieldLogger,
  level: Level,
  message: string,
  fields?: Record<string, unknown>,
): void {
  if (fields === undefined || Object.keys(fields).length === 0) {
    logger[level](message);
    return;
  }
  if (logger.withFields !== undefined) {
    logger.withFields(flatten(fields))[level](message);
    return;
  }
  const safe: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(fields)) {
    safe[key] = forbiddenField(key, value) === null ? value : REDACTED;
  }
  logger[level](message, safe);
}

export const log = {
  debug: (logger: FieldLogger, message: string, fields?: Record<string, unknown>): void => {
    write(logger, "debug", message, fields);
  },
  info: (logger: FieldLogger, message: string, fields?: Record<string, unknown>): void => {
    write(logger, "info", message, fields);
  },
  warn: (logger: FieldLogger, message: string, fields?: Record<string, unknown>): void => {
    write(logger, "warn", message, fields);
  },
  error: (logger: FieldLogger, message: string, fields?: Record<string, unknown>): void => {
    write(logger, "error", message, fields);
  },
};
