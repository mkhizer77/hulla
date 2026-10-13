import { log } from "./log";

/**
 * The narrow surface of Nakama's runtime the services and the table core depend on, so unit tests
 * can fake it and nothing else in the server touches `nk` directly (CODING_RULES 3.5).
 */
export interface ServerLogger {
  debug(message: string, fields?: Record<string, unknown>): void;
  info(message: string, fields?: Record<string, unknown>): void;
  warn(message: string, fields?: Record<string, unknown>): void;
  error(message: string, fields?: Record<string, unknown>): void;
}

export interface ServerRuntime {
  /** Four of these make a 256-bit round seed (ADR-0009). */
  uuidv4(): string;
  sha256Hex(input: string): string;
  nowMs(): number;
  readonly logger: ServerLogger;
}

/** Wraps Nakama's `nk` and `logger` behind the narrow interface. */
export function runtimeFrom(nk: nkruntime.Nakama, logger: nkruntime.Logger): ServerRuntime {
  return {
    uuidv4: () => nk.uuidv4(),
    sha256Hex: (input) => nk.sha256Hash(input),
    nowMs: () => Date.now(),
    logger: {
      debug: (message, fields) => {
        log.debug(logger, message, fields);
      },
      info: (message, fields) => {
        log.info(logger, message, fields);
      },
      warn: (message, fields) => {
        log.warn(logger, message, fields);
      },
      error: (message, fields) => {
        log.error(logger, message, fields);
      },
    },
  };
}
