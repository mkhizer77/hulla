import { createChecks } from "@hulla/protocol/generated";
import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { log } from "../infra/log";
import { metrics, METRICS } from "../infra/metrics";
import { nkConfigStore, resolveFlag } from "../services/config";
import {
  codeFromUuid,
  NO_PROVIDERS,
  providersFromEnv,
  type OtpChannel,
} from "../services/otp_providers";
import {
  CUSTOM_ID_PREFIX,
  EMPTY_PHONE,
  OTP_COLLECTION,
  PHONE_TOKENS_COLLECTION,
  redeemToken,
  startOtp,
  SYSTEM_USER_ID,
  verifyOtp,
  type OtpPorts,
  type OtpStore,
  type PhoneState,
} from "../services/phone_otp";

export const DEFAULT_DAILY_BUDGET = 2000;

function parse(payload: string): Record<string, unknown> {
  try {
    const value: unknown = payload === "" ? {} : JSON.parse(payload);
    return typeof value === "object" && value !== null && !Array.isArray(value)
      ? (value as Record<string, unknown>)
      : {};
  } catch {
    return {};
  }
}

function read<T>(nk: nkruntime.Nakama, key: string, fallback: T): T {
  const objects = nk.storageRead([{ collection: OTP_COLLECTION, key, userId: SYSTEM_USER_ID }]);
  const value = objects[0]?.value as T | undefined;
  return value ?? fallback;
}

function write(nk: nkruntime.Nakama, key: string, value: object): void {
  nk.storageWrite([
    {
      collection: OTP_COLLECTION,
      key,
      userId: SYSTEM_USER_ID,
      value,
      permissionRead: 0,
      permissionWrite: 0,
    },
  ]);
}

export function nkOtpStore(nk: nkruntime.Nakama): OtpStore {
  return {
    phone: (key) => read<PhoneState>(nk, `phone:${key}`, EMPTY_PHONE),
    writePhone: (key, state) => {
      write(nk, `phone:${key}`, state);
    },
    times: (kind, key) => [...read<{ times: number[] }>(nk, `${kind}:${key}`, { times: [] }).times],
    writeTimes: (kind, key, times) => {
      write(nk, `${kind}:${key}`, { times });
    },
    token(token) {
      const objects = nk.storageRead([
        { collection: PHONE_TOKENS_COLLECTION, key: token, userId: SYSTEM_USER_ID },
      ]);
      const value = objects[0]?.value as { customId?: unknown; expiresTs?: unknown } | undefined;
      return typeof value?.customId === "string" && typeof value.expiresTs === "number"
        ? { customId: value.customId, expiresTs: value.expiresTs }
        : null;
    },
    writeToken(token, customId, expiresTs) {
      nk.storageWrite([
        {
          collection: PHONE_TOKENS_COLLECTION,
          key: token,
          userId: SYSTEM_USER_ID,
          value: { customId, expiresTs },
          permissionRead: 0,
          permissionWrite: 0,
        },
      ]);
    },
    removeToken(token) {
      try {
        nk.storageDelete([
          { collection: PHONE_TOKENS_COLLECTION, key: token, userId: SYSTEM_USER_ID },
        ]);
      } catch {
        // already gone
      }
    },
  };
}

/** The pepper keeps the numbers out of reach of anyone holding only the database (OQ-041). */
export function phonePepperOf(env: Readonly<Record<string, string>>): string | null {
  const pepper = env["HULLA_PHONE_PEPPER"] ?? "";
  if (pepper !== "") return pepper;
  return (env["HULLA_ENV"] ?? "local") === "local" ? "local-only-phone-pepper" : null;
}

export function nkOtpPorts(ctx: nkruntime.Context, nk: nkruntime.Nakama): OtpPorts {
  const env = ctx.env;
  const pepper = phonePepperOf(env);
  const budget = resolveFlag(nkConfigStore(nk).read(), "otp_daily_budget", {
    region: env["HULLA_REGION"] ?? "local",
    cohort: "",
  });
  const http = {
    request: (url: string, method: string, headers: Record<string, string>, body: string) => {
      const res = nk.httpRequest(url, method as nkruntime.RequestMethod, headers, body);
      return { code: res.code, body: res.body };
    },
    base64: (text: string) => nk.base64Encode(text),
  };
  const codes = {
    newCode: () => codeFromUuid(nk.uuidv4()),
    hashCode: (phone: string, code: string) =>
      nk.sha256Hash(`${pepper ?? ""}:code:${phone}:${code}`),
  };
  return {
    store: nkOtpStore(nk),
    providers: pepper === null ? NO_PROVIDERS : providersFromEnv(env, http, codes),
    hashPhone: (phone) => nk.sha256Hash(`${pepper ?? ""}:${phone}`),
    linkedAccount(customId) {
      try {
        return nk.authenticateCustom(customId, undefined, false).userId;
      } catch {
        return null;
      }
    },
    link: (userId, customId) => {
      nk.linkCustom(userId, customId);
    },
    unlink: (userId, customId) => {
      nk.unlinkCustom(userId, customId);
    },
    newToken: () => nk.uuidv4(),
    count(metric, tags, cents) {
      const name = {
        sent: METRICS.otpSent,
        failed: METRICS.otpSendFailed,
        verified: METRICS.otpVerified,
        refused: METRICS.otpRefused,
      }[metric];
      metrics.count(nk, name, tags);
      if (cents !== undefined && cents > 0) {
        // Counters take whole numbers, and a WhatsApp code costs a fraction of a cent.
        metrics.count(
          nk,
          METRICS.otpCostMicroUsd,
          { country: tags["country"] ?? "other", channel: tags["channel"] ?? "sms" },
          Math.round(cents * 10_000),
        );
      }
    },
    dailyBudget: typeof budget === "number" ? budget : DEFAULT_DAILY_BUDGET,
  };
}

const unauthorised = (): string =>
  JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });

/**
 * `{ phone, channel? }`: sends a code to a number for the signed-in account (GDD 10.2), on
 * WhatsApp first unless the channel is "sms"; the answer says which channel was used.
 */
export function rpcPhoneStart(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return unauthorised();
  const input = parse(payload);
  if (!createChecks().phoneStart(input)) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.table.invalidPayload, { reason: "schema" }),
    });
  }
  const response = startOtp(
    nkOtpPorts(ctx, nk),
    userId,
    ctx.clientIp ?? "",
    input["phone"],
    Date.now(),
    input["channel"] as OtpChannel | undefined,
  );
  if (!response.ok)
    log.info(logger, "otp refused", { reason: String(response.error.detail?.["reason"]) });
  return JSON.stringify(response);
}

/** `{ phone, code }`: links the number, or answers with a sign-in token for its account. */
export function rpcPhoneVerify(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return unauthorised();
  const input = parse(payload);
  return JSON.stringify(
    verifyOtp(nkOtpPorts(ctx, nk), userId, input["phone"], input["code"], Date.now()),
  );
}

/** Whether the account has a phone, and `{ remove: true }` takes it off (GDD 10.2, 25). */
export function rpcPhoneStatus(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  const userId = ctx.userId ?? "";
  if (userId === "") return unauthorised();
  // Accounts without a custom id come back without the field, whatever the typings say.
  const account = nk.accountGetId(userId) as { customId?: string };
  const customId = account.customId ?? "";
  const linked = customId.startsWith(CUSTOM_ID_PREFIX);
  if (linked && parse(payload)["remove"] === true) {
    try {
      nk.unlinkCustom(userId, customId);
      return JSON.stringify({ ok: true, linked: false });
    } catch (error: unknown) {
      log.warn(logger, "phone not unlinked", { error: String(error) });
      return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.auth.otpUnavailable) });
    }
  }
  return JSON.stringify({ ok: true, linked });
}

/**
 * Registered with `registerBeforeAuthenticateCustom`: an `otp:` token from phone_verify signs in
 * to the account its number is linked to, once; a client never signs in with a phone id directly.
 */
export function beforeAuthenticateCustom(
  _ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  data: nkruntime.AuthenticateCustomRequest,
): nkruntime.AuthenticateCustomRequest {
  const id = data.account?.id ?? "";
  const redeemed = redeemToken(nkOtpStore(nk), id, Date.now());
  if (redeemed === null) return data;
  if (!redeemed.ok) {
    log.info(logger, "phone sign-in refused");
    throw new Error(redeemed.code);
  }
  return { ...data, account: { ...data.account, id: redeemed.customId }, create: false };
}
