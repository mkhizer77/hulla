import { runtimeFrom } from "./infra/nk";
import { hello } from "./rpc/hello";

/** Nakama calls this once at start-up; every RPC, hook and match handler registers here. */
function InitModule(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  _nk: nkruntime.Nakama,
  initializer: nkruntime.Initializer,
): void {
  initializer.registerRpc("hello", (_ctx, rpcLogger, rpcNk) => {
    return JSON.stringify(hello(runtimeFrom(rpcNk, rpcLogger)));
  });
  logger.info("Hulla runtime loaded", { env: ctx.env["HULLA_ENV"] ?? "local" });
}

// Nakama looks InitModule up on the global object. The explicit assignment keeps the bundler
// from dropping the function and lets the tests load it the same way Nakama does.
(globalThis as unknown as { InitModule: nkruntime.InitModule }).InitModule = InitModule;
