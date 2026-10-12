import type { ServerLogger, ServerRuntime } from "../infra/nk";

export interface LogLine {
  readonly level: "debug" | "info" | "warn" | "error";
  readonly message: string;
  readonly fields: Record<string, unknown> | undefined;
}

/** A deterministic runtime for unit tests: fixed clock, counted uuids, remembered log lines. */
export function fakeRuntime(
  options: { now?: number } = {},
): ServerRuntime & { readonly lines: LogLine[] } {
  const lines: LogLine[] = [];
  let uuids = 0;
  const log =
    (level: LogLine["level"]) =>
    (message: string, fields?: Record<string, unknown>): void => {
      lines.push({ level, message, fields });
    };
  const logger: ServerLogger = {
    debug: log("debug"),
    info: log("info"),
    warn: log("warn"),
    error: log("error"),
  };
  return {
    uuidv4: () => {
      uuids += 1;
      return `00000000-0000-4000-8000-${String(uuids).padStart(12, "0")}`;
    },
    sha256Hex: (input) => `sha256(${input})`,
    nowMs: () => options.now ?? 1_760_200_000_000,
    logger,
    lines,
  };
}

/** The pieces of Nakama's Initializer the module registers against, recorded for assertions. */
export interface FakeInitializer {
  readonly rpcs: Map<string, nkruntime.RpcFunction>;
  readonly matches: Map<string, nkruntime.MatchHandler>;
  readonly initializer: nkruntime.Initializer;
}

export function fakeInitializer(): FakeInitializer {
  const rpcs = new Map<string, nkruntime.RpcFunction>();
  const matches = new Map<string, nkruntime.MatchHandler>();
  const initializer = {
    registerRpc: (id: string, fn: nkruntime.RpcFunction) => {
      rpcs.set(id, fn);
    },
    registerMatch: (name: string, handler: nkruntime.MatchHandler) => {
      matches.set(name, handler);
    },
  } as unknown as nkruntime.Initializer;
  return { rpcs, matches, initializer };
}

export function fakeNk(): nkruntime.Nakama {
  return {
    uuidv4: () => "11111111-2222-4333-8444-555555555555",
    sha256Hash: (input: string) => `sha256(${input})`,
  } as unknown as nkruntime.Nakama;
}

export function fakeLogger(lines: LogLine[] = []): nkruntime.Logger {
  const push = (level: LogLine["level"]) => (message: string, fields?: Record<string, unknown>) => {
    lines.push({ level, message, fields });
  };
  return {
    debug: push("debug"),
    info: push("info"),
    warn: push("warn"),
    error: push("error"),
  } as unknown as nkruntime.Logger;
}
