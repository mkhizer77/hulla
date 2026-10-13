import type { AnalyticsEventName, AnalyticsEvents } from "@hulla/protocol/generated";

/**
 * Server-side analytics emitters (GDD 23.3, ARCHITECTURE 12.4): every event is typed against the
 * generated map, carries the table context and goes out as one structured log line that the log
 * pipeline ships. No names, no chat text, no hands: a guard refuses them at runtime and a test
 * proves it.
 */
export const ANALYTICS_LOG = "analytics";

export interface AnalyticsContext {
  readonly gameId?: string;
  readonly tableId?: string;
  readonly roundId?: string;
  readonly seat?: number;
  readonly mode?: string;
}

const FORBIDDEN_KEYS = new Set([
  "name",
  "displayName",
  "handle",
  "username",
  "email",
  "text",
  "message",
  "hand",
  "hands",
  "deck",
  "deckOrder",
  "userId",
  "playerId",
  "ip",
  "deviceId",
]);

const CARD = /^(10|[2-9TJQKA])[SHCD]$/;

/** Why a parameter must not leave the server, or null when it may. */
export function forbiddenParam(key: string, value: unknown): string | null {
  if (FORBIDDEN_KEYS.has(key)) return `key ${key}`;
  if (typeof value === "string" && CARD.test(value)) return `card in ${key}`;
  if (Array.isArray(value)) {
    if (value.some((v) => typeof v === "string" && CARD.test(v))) return `card list in ${key}`;
    if (value.some((v) => typeof v === "object" && v !== null)) return `object list in ${key}`;
  }
  if (typeof value === "object" && value !== null) return `object in ${key}`;
  if (typeof value === "string" && value.length > 64) return `long text in ${key}`;
  return null;
}

export interface AnalyticsSink {
  info(message: string, fields?: Record<string, unknown>): void;
  /**
   * Nakama's logger takes fields through `withFields` (its `info(format, ...args)` would fold an
   * object into the message); a sink that has it gets real JSON fields, the test sink gets the
   * object.
   */
  withFields?(fields: Record<string, string>): { info(message: string): void };
}

/** Emits one typed event; throws on a parameter the policy forbids (a programmer error). */
export function emitAnalytics<E extends AnalyticsEventName>(
  sink: AnalyticsSink,
  name: E,
  params: AnalyticsEvents[E],
  context: AnalyticsContext = {},
): void {
  const fields: Record<string, unknown> = { event: name };
  for (const [key, value] of Object.entries({ ...context, ...params })) {
    const problem = forbiddenParam(key, value);
    if (problem !== null) throw new Error(`analytics ${name}: ${problem}`);
    fields[key] = value;
  }
  if (sink.withFields !== undefined) {
    const flat: Record<string, string> = {};
    for (const [key, value] of Object.entries(fields)) flat[key] = String(value);
    sink.withFields(flat).info(ANALYTICS_LOG);
    return;
  }
  sink.info(ANALYTICS_LOG, fields);
}

/** The same, but a bad parameter is logged as a warning rather than thrown, for hot paths. */
export function emitAnalyticsSafely<E extends AnalyticsEventName>(
  sink: AnalyticsSink & { warn(message: string, fields?: Record<string, unknown>): void },
  name: E,
  params: AnalyticsEvents[E],
  context: AnalyticsContext = {},
): void {
  try {
    emitAnalytics(sink, name, params, context);
  } catch (error: unknown) {
    sink.warn("analytics event dropped", { event: name, error: String(error) });
  }
}
