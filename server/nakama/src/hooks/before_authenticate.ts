import { ErrorCodes } from "@hulla/shared-ts";

import { log } from "../infra/log";
import { isDeviceBanned } from "../services/sanctions";

/** A banned device cannot sign in at all (GDD 24.3); registered with `registerBeforeAuthenticateDevice`. */
export function beforeAuthenticateDevice(
  _ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  data: nkruntime.AuthenticateDeviceRequest,
): nkruntime.AuthenticateDeviceRequest {
  const deviceId = data.account?.id ?? "";
  if (deviceId !== "" && isDeviceBanned(nk, deviceId, Date.now())) {
    log.info(logger, "banned device refused", { deviceId });
    throw new Error(ErrorCodes.auth.banned);
  }
  return data;
}
