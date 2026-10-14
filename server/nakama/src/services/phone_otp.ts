import { ErrorCodes, ruleError, type RuleError } from "@hulla/shared-ts";

/**
 * Phone OTP sign-in and linking (GDD 10.2, #99). A verified number links to the account as a
 * Nakama custom id made from the number and a server-side pepper, so the database never holds the
 * number itself. On a new device the guest verifies the number and gets a one-time token that the
 * custom sign-in hook trades for the linked account. Limits per number, account, address and day
 * keep SMS pumping and brute force out; every send is counted with its estimated cost for the
 * dashboard. The SMS provider is behind `OtpProvider` (the contract is the owner's, OQ-041).
 */
export const OTP_COLLECTION = "phone_otp";
export const PHONE_TOKENS_COLLECTION = "phone_tokens";
export const SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000";
export const CUSTOM_ID_PREFIX = "ph_";
export const TOKEN_PREFIX = "otp:";

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

export const OTP_LIMITS = {
  /** A code lives this long, and wrong guesses lock it after this many. */
  codeTtlMs: 10 * MINUTE,
  maxAttempts: 5,
  /** Seconds before the same number can ask again. */
  resendMs: 60_000,
  perPhone: [
    { windowMs: 15 * MINUTE, max: 3 },
    { windowMs: DAY, max: 6 },
  ],
  perAccount: [
    { windowMs: HOUR, max: 5 },
    { windowMs: DAY, max: 10 },
  ],
  perAddress: [{ windowMs: HOUR, max: 10 }],
  tokenTtlMs: 5 * MINUTE,
} as const;

/**
 * Calling codes for the markets of GDD 1.4 with an estimated cost per verification in US cents
 * (provider fee plus the SMS), for the dashboard until the contract sets real prices (OQ-041).
 * Numbers elsewhere are refused: SMS pumping fraud targets premium destinations.
 */
export const OTP_COUNTRIES: Readonly<
  Record<string, { readonly country: string; readonly costCents: number }>
> = {
  "92": { country: "PK", costCents: 15 },
  "91": { country: "IN", costCents: 8 },
  "880": { country: "BD", costCents: 20 },
  "44": { country: "GB", costCents: 9 },
  "971": { country: "AE", costCents: 12 },
  "966": { country: "SA", costCents: 12 },
  "974": { country: "QA", costCents: 12 },
  "965": { country: "KW", costCents: 12 },
  "968": { country: "OM", costCents: 12 },
  "973": { country: "BH", costCents: 12 },
  "1": { country: "US", costCents: 6 },
  "61": { country: "AU", costCents: 9 },
  "64": { country: "NZ", costCents: 9 },
  "353": { country: "IE", costCents: 9 },
  "31": { country: "NL", costCents: 12 },
  "49": { country: "DE", costCents: 12 },
  "60": { country: "MY", costCents: 8 },
  "65": { country: "SG", costCents: 8 },
  "977": { country: "NP", costCents: 18 },
};

export type OtpProblem =
  | "invalid_phone"
  | "country"
  | "rate_limited"
  | "budget"
  | "wrong_code"
  | "expired"
  | "locked"
  | "unavailable";

export function otpError(problem: OtpProblem, detail: Record<string, unknown> = {}): RuleError {
  const code = {
    invalid_phone: ErrorCodes.auth.otpInvalidPhone,
    country: ErrorCodes.auth.otpCountry,
    rate_limited: ErrorCodes.auth.otpRateLimited,
    budget: ErrorCodes.auth.otpRateLimited,
    wrong_code: ErrorCodes.auth.otpWrongCode,
    expired: ErrorCodes.auth.otpExpired,
    locked: ErrorCodes.auth.otpExpired,
    unavailable: ErrorCodes.auth.otpUnavailable,
  }[problem];
  return ruleError(code, { reason: problem, ...detail });
}

/** E.164 from what a player types: "+92 300 123-4567" → "+923001234567"; null if it isn't one. */
export function normalisePhone(input: string): string | null {
  const trimmed = input.trim();
  const digits = trimmed.replace(/[\s().-]/g, "");
  const e164 = digits.startsWith("00") ? `+${digits.slice(2)}` : digits;
  return /^\+[1-9]\d{7,14}$/.test(e164) ? e164 : null;
}

/** The calling code a number starts with (longest match), or null when it is not a market we serve. */
export function callingCodeOf(e164: string): string | null {
  for (const length of [3, 2, 1]) {
    const code = e164.slice(1, 1 + length);
    if (code in OTP_COUNTRIES) return code;
  }
  return null;
}

/** The number shown back to the player: country code and the last two digits only. */
export function maskPhone(e164: string): string {
  const code = callingCodeOf(e164) ?? "";
  return `+${code} ••• ${e164.slice(-2)}`;
}

/* ------------------------------------------------------------------- limits */

export interface Window {
  readonly windowMs: number;
  readonly max: number;
}

/** Whether one more event fits every window, given the times of the earlier ones. */
export function allowed(
  times: readonly number[],
  windows: readonly Window[],
  now: number,
): boolean {
  return windows.every((w) => times.filter((t) => now - t < w.windowMs).length < w.max);
}

/** The times worth keeping: those inside the longest window. */
export function prune(times: readonly number[], windows: readonly Window[], now: number): number[] {
  const longest = Math.max(...windows.map((w) => w.windowMs));
  return times.filter((t) => now - t < longest);
}

/* --------------------------------------------------------------- the provider */

export type ProviderResult<T> =
  { readonly ok: true; readonly value: T } | { readonly ok: false; readonly reason: string };

export interface OtpProvider {
  readonly name: string;
  /** Sends a code to the number; `ref` is whatever the provider needs to check it later. */
  send(phone: string): ProviderResult<{ readonly ref: string | null }>;
  /** True when the code is the one sent; false for a wrong code. */
  check(phone: string, code: string, ref: string | null): ProviderResult<boolean>;
}

export interface HttpPort {
  request(
    url: string,
    method: string,
    headers: Record<string, string>,
    body: string,
  ): { code: number; body: string };
  base64(text: string): string;
}

const form = (fields: Record<string, string>): string =>
  Object.entries(fields)
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`)
    .join("&");

/** Twilio Verify: Twilio generates, sends and checks the code (env TWILIO_*; owner's account). */
export function twilioVerify(
  http: HttpPort,
  accountSid: string,
  authToken: string,
  serviceSid: string,
): OtpProvider {
  const base = `https://verify.twilio.com/v2/Services/${serviceSid}`;
  const headers = {
    Authorization: `Basic ${http.base64(`${accountSid}:${authToken}`)}`,
    "Content-Type": "application/x-www-form-urlencoded",
  };
  return {
    name: "twilio",
    send(phone) {
      try {
        const res = http.request(
          `${base}/Verifications`,
          "POST",
          headers,
          form({ To: phone, Channel: "sms" }),
        );
        if (res.code >= 200 && res.code < 300) return { ok: true, value: { ref: null } };
        return { ok: false, reason: `http_${String(res.code)}` };
      } catch (error: unknown) {
        return { ok: false, reason: String(error) };
      }
    },
    check(phone, code) {
      try {
        const res = http.request(
          `${base}/VerificationCheck`,
          "POST",
          headers,
          form({ To: phone, Code: code }),
        );
        if (res.code === 404) return { ok: true, value: false };
        if (res.code < 200 || res.code >= 300)
          return { ok: false, reason: `http_${String(res.code)}` };
        const body = JSON.parse(res.body) as { status?: unknown };
        return { ok: true, value: body.status === "approved" };
      } catch (error: unknown) {
        return { ok: false, reason: String(error) };
      }
    },
  };
}

/**
 * A development box's provider: nothing is sent and the code is the last six digits of the number,
 * so a tester can sign in from any number. Only HULLA_ENV=local gets it.
 */
export function localProvider(): OtpProvider {
  const codeFor = (phone: string): string => phone.slice(-6).padStart(6, "0");
  return {
    name: "local",
    send: () => ({ ok: true, value: { ref: null } }),
    check: (phone, code) => ({ ok: true, value: code === codeFor(phone) }),
  };
}

export function providerFromEnv(
  env: Readonly<Record<string, string>>,
  http: HttpPort,
): OtpProvider | null {
  const sid = env["TWILIO_ACCOUNT_SID"] ?? "";
  const token = env["TWILIO_AUTH_TOKEN"] ?? "";
  const service = env["TWILIO_VERIFY_SID"] ?? "";
  if (sid !== "" && token !== "" && service !== "") return twilioVerify(http, sid, token, service);
  if ((env["HULLA_ENV"] ?? "local") === "local") return localProvider();
  return null;
}

/* -------------------------------------------------------------------- state */

export interface PhoneState {
  readonly sends: readonly number[];
  readonly expiresTs: number;
  readonly attempts: number;
  readonly ref: string | null;
  /** The account that asked; a code is good only for the account it was sent to. */
  readonly requestedBy: string | null;
}

export const EMPTY_PHONE: PhoneState = {
  sends: [],
  expiresTs: 0,
  attempts: 0,
  ref: null,
  requestedBy: null,
};

export interface OtpStore {
  phone(key: string): PhoneState;
  writePhone(key: string, state: PhoneState): void;
  times(kind: "account" | "address" | "day", key: string): number[];
  writeTimes(kind: "account" | "address" | "day", key: string, times: readonly number[]): void;
  token(token: string): { readonly customId: string; readonly expiresTs: number } | null;
  writeToken(token: string, customId: string, expiresTs: number): void;
  removeToken(token: string): void;
}

export interface OtpPorts {
  readonly store: OtpStore;
  readonly provider: OtpProvider | null;
  /** sha256 hex of the number with the pepper; also the storage key, so no number is stored. */
  hashPhone(phone: string): string;
  /** The account a phone custom id is linked to, or null. */
  linkedAccount(customId: string): string | null;
  link(userId: string, customId: string): void;
  unlink(userId: string, customId: string): void;
  newToken(): string;
  count(
    metric: "sent" | "verified" | "refused",
    tags: Record<string, string>,
    cents?: number,
  ): void;
  /** The day's send budget, from remote config. */
  readonly dailyBudget: number;
}

export interface Refused {
  readonly ok: false;
  readonly error: RuleError;
}

export type StartResponse =
  | {
      readonly ok: true;
      readonly phone: string;
      readonly expiresTs: number;
      readonly resendAfterTs: number;
    }
  | { readonly ok: false; readonly error: RuleError };

function refuse(
  ports: OtpPorts,
  problem: OtpProblem,
  detail: Record<string, unknown> = {},
): Refused {
  ports.count("refused", { reason: problem });
  return { ok: false, error: otpError(problem, detail) };
}

/** Sends a code to a number for the signed-in account (a guest is fine). */
export function startOtp(
  ports: OtpPorts,
  userId: string,
  address: string,
  input: unknown,
  now: number,
): StartResponse {
  const phone = typeof input === "string" ? normalisePhone(input) : null;
  if (phone === null) return refuse(ports, "invalid_phone");
  const calling = callingCodeOf(phone);
  const market = calling === null ? undefined : OTP_COUNTRIES[calling];
  if (market === undefined) return refuse(ports, "country");
  if (ports.provider === null) return refuse(ports, "unavailable");
  const key = ports.hashPhone(phone);
  const state = ports.store.phone(key);
  const last = state.sends[state.sends.length - 1] ?? 0;
  if (now - last < OTP_LIMITS.resendMs) {
    return refuse(ports, "rate_limited", { retryAfterTs: last + OTP_LIMITS.resendMs });
  }
  const account = ports.store.times("account", userId);
  const fromAddress = address === "" ? [] : ports.store.times("address", address);
  const today = new Date(now).toISOString().slice(0, 10);
  const day = ports.store.times("day", today);
  if (
    !allowed(state.sends, OTP_LIMITS.perPhone, now) ||
    !allowed(account, OTP_LIMITS.perAccount, now) ||
    !allowed(fromAddress, OTP_LIMITS.perAddress, now)
  ) {
    return refuse(ports, "rate_limited");
  }
  if (day.length >= ports.dailyBudget) return refuse(ports, "budget");
  const sent = ports.provider.send(phone);
  if (!sent.ok) return refuse(ports, "unavailable", { provider: ports.provider.name });
  ports.store.writePhone(key, {
    sends: [...prune(state.sends, OTP_LIMITS.perPhone, now), now],
    expiresTs: now + OTP_LIMITS.codeTtlMs,
    attempts: 0,
    ref: sent.value.ref,
    requestedBy: userId,
  });
  ports.store.writeTimes("account", userId, [...prune(account, OTP_LIMITS.perAccount, now), now]);
  if (address !== "") {
    ports.store.writeTimes("address", address, [
      ...prune(fromAddress, OTP_LIMITS.perAddress, now),
      now,
    ]);
  }
  ports.store.writeTimes("day", today, [...day, now]);
  ports.count("sent", { country: market.country, provider: ports.provider.name }, market.costCents);
  return {
    ok: true,
    phone: maskPhone(phone),
    expiresTs: now + OTP_LIMITS.codeTtlMs,
    resendAfterTs: now + OTP_LIMITS.resendMs,
  };
}

export type VerifyResponse =
  | { readonly ok: true; readonly result: "linked"; readonly phone: string }
  | {
      readonly ok: true;
      readonly result: "signIn";
      readonly phone: string;
      readonly token: string;
      readonly expiresTs: number;
    }
  | { readonly ok: false; readonly error: RuleError };

/**
 * Checks a code. A number nobody has yet links to the caller; one linked to the caller stays so;
 * one linked to another account answers with a one-time sign-in token for that account.
 */
export function verifyOtp(
  ports: OtpPorts,
  userId: string,
  phoneInput: unknown,
  codeInput: unknown,
  now: number,
): VerifyResponse {
  const phone = typeof phoneInput === "string" ? normalisePhone(phoneInput) : null;
  if (phone === null) return refuse(ports, "invalid_phone");
  const code = typeof codeInput === "string" ? codeInput.trim() : "";
  if (!/^\d{4,8}$/.test(code)) return refuse(ports, "wrong_code");
  if (ports.provider === null) return refuse(ports, "unavailable");
  const key = ports.hashPhone(phone);
  const state = ports.store.phone(key);
  if (state.requestedBy !== userId || state.expiresTs <= now) return refuse(ports, "expired");
  if (state.attempts >= OTP_LIMITS.maxAttempts) return refuse(ports, "locked");
  const checked = ports.provider.check(phone, code, state.ref);
  if (!checked.ok) return refuse(ports, "unavailable", { provider: ports.provider.name });
  if (!checked.value) {
    ports.store.writePhone(key, { ...state, attempts: state.attempts + 1 });
    return refuse(ports, "wrong_code", {
      attemptsLeft: OTP_LIMITS.maxAttempts - state.attempts - 1,
    });
  }
  ports.store.writePhone(key, {
    ...state,
    expiresTs: 0,
    attempts: 0,
    ref: null,
    requestedBy: null,
  });
  const country = OTP_COUNTRIES[callingCodeOf(phone) ?? ""]?.country ?? "other";
  const customId = `${CUSTOM_ID_PREFIX}${key}`;
  const owner = ports.linkedAccount(customId);
  if (owner === null) ports.link(userId, customId);
  if (owner === null || owner === userId) {
    ports.count("verified", { country, result: "linked" });
    return { ok: true, result: "linked", phone: maskPhone(phone) };
  }
  const token = ports.newToken();
  ports.store.writeToken(token, customId, now + OTP_LIMITS.tokenTtlMs);
  ports.count("verified", { country, result: "sign_in" });
  return {
    ok: true,
    result: "signIn",
    phone: maskPhone(phone),
    token: `${TOKEN_PREFIX}${token}`,
    expiresTs: now + OTP_LIMITS.tokenTtlMs,
  };
}

/**
 * The custom sign-in hook's half (registerBeforeAuthenticateCustom): an `otp:` token becomes the
 * linked phone's custom id, once and before it expires; a bare phone id is never accepted from a
 * client. Returns the custom id to sign in with, or the error to throw.
 */
export function redeemToken(
  store: OtpStore,
  customId: string,
  now: number,
):
  | { readonly ok: true; readonly customId: string }
  | { readonly ok: false; readonly code: string }
  | null {
  if (customId.startsWith(CUSTOM_ID_PREFIX)) return { ok: false, code: ErrorCodes.auth.otpExpired };
  if (!customId.startsWith(TOKEN_PREFIX)) return null;
  const token = customId.slice(TOKEN_PREFIX.length);
  const entry = store.token(token);
  store.removeToken(token);
  if (entry === null || entry.expiresTs <= now)
    return { ok: false, code: ErrorCodes.auth.otpExpired };
  return { ok: true, customId: entry.customId };
}
