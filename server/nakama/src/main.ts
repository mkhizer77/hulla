import {
  thullaMatchInit,
  thullaMatchJoin,
  thullaMatchJoinAttempt,
  thullaMatchLeave,
  thullaMatchLoop,
  thullaMatchSignal,
  thullaMatchTerminate,
} from "./games/thulla/match";
import { rpcHello } from "./rpc/hello";
import { rpcTableCreate } from "./rpc/table_create";

/**
 * Nakama calls this once at start-up; every RPC, hook and match handler registers here. Handlers
 * are passed by identifier and names are string literals: Nakama reads both out of this
 * function's source and looks the functions up on the global object.
 */
function InitModule(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  _nk: nkruntime.Nakama,
  initializer: nkruntime.Initializer,
): void {
  initializer.registerRpc("hello", rpcHello);
  initializer.registerRpc("table_create", rpcTableCreate);
  initializer.registerMatch("thulla", {
    matchInit: thullaMatchInit,
    matchJoinAttempt: thullaMatchJoinAttempt,
    matchJoin: thullaMatchJoin,
    matchLeave: thullaMatchLeave,
    matchLoop: thullaMatchLoop,
    matchTerminate: thullaMatchTerminate,
    matchSignal: thullaMatchSignal,
  });
  logger.info("Hulla runtime loaded", { env: ctx.env["HULLA_ENV"] ?? "local" });
}

// Nakama looks InitModule up on the global object. The explicit assignment keeps the bundler
// from dropping the function and lets the tests load it the same way Nakama does.
(globalThis as unknown as { InitModule: nkruntime.InitModule }).InitModule = InitModule;
