import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { nkLedgerPorts } from "../services/ledger";
import { payWelcome } from "../services/welcome";

/** The client finished the tutorial (GDD 17.2): its one-time reward, once per account. */
export function rpcTutorialComplete(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  const coins = payWelcome(nkLedgerPorts(nk), userId, "tutorial");
  return JSON.stringify({ ok: true, applied: coins > 0, coins });
}
