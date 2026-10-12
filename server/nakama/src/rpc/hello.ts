import { THULLA_RULES_VERSION } from "@hulla/rules-engine";

import type { ServerRuntime } from "../infra/nk";

export const SERVER_VERSION = "0.1.0";

export interface HelloResponse {
  readonly ok: true;
  readonly server: string;
  readonly rulesVersion: string;
  readonly protocolMajor: number;
  readonly serverTs: number;
}

/** The smoke test every environment answers; also what the deploy runbook calls to check a box. */
export function hello(runtime: ServerRuntime): HelloResponse {
  return {
    ok: true,
    server: SERVER_VERSION,
    rulesVersion: THULLA_RULES_VERSION,
    protocolMajor: 1,
    serverTs: runtime.nowMs(),
  };
}
