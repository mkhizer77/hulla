import { THULLA_RULES_VERSION } from "@hulla/rules-engine";

import { runtimeFrom, type ServerRuntime } from "../infra/nk";
import { nkConfigStore } from "../services/config";
import { isDraining } from "../services/ops";

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

/**
 * The handler registered with Nakama. It must stay a named top-level function: Nakama reads the
 * name out of the `registerRpc` call and looks the function up on the global object per call.
 */
export function rpcHello(
  _ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  return JSON.stringify({
    ...hello(runtimeFrom(nk, logger)),
    minClientVersion: nkConfigStore(nk).read().minClientVersion,
    draining: isDraining(nk),
  });
}
