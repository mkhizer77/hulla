import { afterEach, describe, expect, it, vi } from "vitest";

import {
  beforeAuthenticateCustom,
  nkOtpStore,
  rpcPhoneStart,
  rpcPhoneStatus,
  rpcPhoneVerify,
} from "../rpc/phone";
import {
  callingCodeOf,
  EMPTY_PHONE,
  localProvider,
  maskPhone,
  normalisePhone,
  OTP_LIMITS,
  providerFromEnv,
  redeemToken,
  startOtp,
  twilioVerify,
  verifyOtp,
  type OtpPorts,
  type OtpProvider,
  type OtpStore,
  type PhoneState,
} from "../services/phone_otp";

import { fakeLogger, memoryNk } from "./fake-nk";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;
const T0 = Date.UTC(2026, 9, 14, 9);
const PK = "+923001234567";

function memoryStore(): OtpStore & {
  tokens: Map<string, { customId: string; expiresTs: number }>;
} {
  const phones = new Map<string, PhoneState>();
  const times = new Map<string, number[]>();
  const tokens = new Map<string, { customId: string; expiresTs: number }>();
  return {
    tokens,
    phone: (key) => phones.get(key) ?? EMPTY_PHONE,
    writePhone: (key, state) => void phones.set(key, state),
    times: (kind, key) => [...(times.get(`${kind}:${key}`) ?? [])],
    writeTimes: (kind, key, list) => void times.set(`${kind}:${key}`, [...list]),
    token: (t) => tokens.get(t) ?? null,
    writeToken: (t, customId, expiresTs) => void tokens.set(t, { customId, expiresTs }),
    removeToken: (t) => void tokens.delete(t),
  };
}

function ports(overrides: Partial<OtpPorts> = {}) {
  const links = new Map<string, string>();
  const counted: { metric: string; tags: Record<string, string>; cents?: number }[] = [];
  let tokens = 0;
  const base: OtpPorts = {
    store: memoryStore(),
    provider: localProvider(),
    hashPhone: (phone) => `h(${phone})`,
    linkedAccount: (customId) => links.get(customId) ?? null,
    link: (userId, customId) => void links.set(customId, userId),
    unlink: (_userId, customId) => void links.delete(customId),
    newToken: () => `t${String((tokens += 1))}`,
    count: (metric, tags, cents) =>
      void counted.push({ metric, tags, ...(cents === undefined ? {} : { cents }) }),
    dailyBudget: 2000,
    ...overrides,
  };
  return { ports: base, links, counted };
}

describe("phone numbers (GDD 10.2)", () => {
  it("normalises what players type and knows the markets", () => {
    expect(normalisePhone("+92 300 123-4567")).toBe(PK);
    expect(normalisePhone("0092 (300) 1234567")).toBe(PK);
    expect(normalisePhone("03001234567")).toBeNull();
    expect(normalisePhone("+0123")).toBeNull();
    expect(callingCodeOf(PK)).toBe("92");
    expect(callingCodeOf("+8801712345678")).toBe("880");
    expect(callingCodeOf("+14155550100")).toBe("1");
    expect(callingCodeOf("+2348012345678")).toBeNull();
    expect(maskPhone(PK)).toBe("+92 ••• 67");
  });
});

describe("sending a code", () => {
  it("sends, counts the cost and makes the number wait before another", () => {
    const { ports: p, counted } = ports();
    const sent = startOtp(p, "guest", "1.2.3.4", "+92 300 1234567", T0);
    expect(sent).toMatchObject({
      ok: true,
      phone: "+92 ••• 67",
      expiresTs: T0 + OTP_LIMITS.codeTtlMs,
    });
    expect(counted).toContainEqual({
      metric: "sent",
      tags: { country: "PK", provider: "local" },
      cents: 15,
    });
    expect(startOtp(p, "guest", "1.2.3.4", PK, T0 + 10_000)).toMatchObject({
      ok: false,
      error: { code: "auth.otp_rate_limited", detail: { retryAfterTs: T0 + OTP_LIMITS.resendMs } },
    });
  });

  it("refuses bad numbers, other countries and a missing provider", () => {
    expect(startOtp(ports().ports, "g", "", "12345", T0)).toMatchObject({
      error: { code: "auth.otp_invalid_phone" },
    });
    expect(startOtp(ports().ports, "g", "", "+2348012345678", T0)).toMatchObject({
      error: { code: "auth.otp_country" },
    });
    expect(startOtp(ports({ provider: null }).ports, "g", "", PK, T0)).toMatchObject({
      error: { code: "auth.otp_unavailable" },
    });
    const failing: OtpProvider = {
      name: "down",
      send: () => ({ ok: false, reason: "x" }),
      check: () => ({ ok: false, reason: "x" }),
    };
    expect(startOtp(ports({ provider: failing }).ports, "g", "", PK, T0)).toMatchObject({
      error: { code: "auth.otp_unavailable" },
    });
  });

  it("stops a number after three codes in 15 minutes", () => {
    const { ports: p, counted } = ports();
    for (let i = 0; i < 3; i += 1)
      expect(startOtp(p, `g${String(i)}`, "", PK, T0 + i * 61_000).ok).toBe(true);
    expect(startOtp(p, "g9", "", PK, T0 + 4 * 61_000)).toMatchObject({
      error: { code: "auth.otp_rate_limited" },
    });
    expect(startOtp(p, "g9", "", PK, T0 + 16 * 60_000).ok).toBe(true);
    expect(counted.some((c) => c.metric === "refused" && c.tags["reason"] === "rate_limited")).toBe(
      true,
    );
  });

  it("stops one account after five numbers in an hour, and one address after ten", () => {
    const { ports: p } = ports();
    for (let i = 0; i < 5; i += 1)
      expect(startOtp(p, "farmer", "", `+9230012345${String(10 + i)}`, T0).ok).toBe(true);
    expect(startOtp(p, "farmer", "", "+923001234599", T0).ok).toBe(false);
    const { ports: q } = ports();
    for (let i = 0; i < 10; i += 1)
      expect(startOtp(q, `a${String(i)}`, "9.9.9.9", `+9230012345${String(10 + i)}`, T0).ok).toBe(
        true,
      );
    expect(startOtp(q, "a99", "9.9.9.9", "+923001234599", T0).ok).toBe(false);
    expect(startOtp(q, "a99", "8.8.8.8", "+923001234599", T0).ok).toBe(true);
  });

  it("stops everyone once the day's budget is spent", () => {
    const { ports: p } = ports({ dailyBudget: 2 });
    expect(startOtp(p, "a", "", "+923001234501", T0).ok).toBe(true);
    expect(startOtp(p, "b", "", "+923001234502", T0).ok).toBe(true);
    expect(startOtp(p, "c", "", "+923001234503", T0)).toMatchObject({
      error: { detail: { reason: "budget" } },
    });
    expect(startOtp(p, "c", "", "+923001234503", T0 + 24 * 3_600_000).ok).toBe(true);
  });
});

describe("checking a code", () => {
  it("links a new number to the caller, and again is fine", () => {
    const { ports: p, links } = ports();
    startOtp(p, "guest", "", PK, T0);
    expect(verifyOtp(p, "guest", PK, "234567", T0 + 1000)).toEqual({
      ok: true,
      result: "linked",
      phone: "+92 ••• 67",
    });
    expect(links.get(`ph_h(${PK})`)).toBe("guest");
    expect(verifyOtp(p, "guest", PK, "234567", T0 + 2000)).toMatchObject({
      error: { code: "auth.otp_expired" },
    });
    startOtp(p, "guest", "", PK, T0 + 120_000);
    expect(verifyOtp(p, "guest", PK, "234567", T0 + 121_000)).toMatchObject({
      ok: true,
      result: "linked",
    });
  });

  it("gives a one-time sign-in token for a number another account holds", () => {
    const { ports: p } = ports();
    startOtp(p, "phone-owner", "", PK, T0);
    verifyOtp(p, "phone-owner", PK, "234567", T0);
    startOtp(p, "new-device-guest", "", PK, T0 + 120_000);
    const signIn = verifyOtp(p, "new-device-guest", PK, "234567", T0 + 121_000);
    expect(signIn).toMatchObject({ ok: true, result: "signIn", token: "otp:t1" });
    const store = p.store;
    expect(redeemToken(store, "otp:t1", T0 + 122_000)).toEqual({
      ok: true,
      customId: `ph_h(${PK})`,
    });
    expect(redeemToken(store, "otp:t1", T0 + 122_000)).toMatchObject({
      ok: false,
      code: "auth.otp_expired",
    });
    expect(redeemToken(store, `ph_h(${PK})`, T0)).toMatchObject({ ok: false });
    expect(redeemToken(store, "some-other-custom-id", T0)).toBeNull();
  });

  it("locks a code after five wrong guesses, and a code only works for the account it was sent to", () => {
    const { ports: p } = ports();
    startOtp(p, "guest", "", PK, T0);
    expect(verifyOtp(p, "someone-else", PK, "234567", T0)).toMatchObject({
      error: { code: "auth.otp_expired" },
    });
    for (let i = 0; i < OTP_LIMITS.maxAttempts; i += 1) {
      expect(verifyOtp(p, "guest", PK, "000000", T0)).toMatchObject({
        error: { code: "auth.otp_wrong_code" },
      });
    }
    expect(verifyOtp(p, "guest", PK, "234567", T0)).toMatchObject({
      error: { detail: { reason: "locked" } },
    });
    expect(verifyOtp(p, "guest", PK, "12", T0)).toMatchObject({
      error: { code: "auth.otp_wrong_code" },
    });
  });

  it("an expired code is refused", () => {
    const { ports: p } = ports();
    startOtp(p, "guest", "", PK, T0);
    expect(verifyOtp(p, "guest", PK, "234567", T0 + OTP_LIMITS.codeTtlMs + 1)).toMatchObject({
      error: { code: "auth.otp_expired" },
    });
  });
});

describe("providers", () => {
  it("Twilio Verify sends and checks with basic auth and a form body", () => {
    const requests: { url: string; headers: Record<string, string>; body: string }[] = [];
    const twilio = twilioVerify(
      {
        request: (url, _method, headers, body) => {
          requests.push({ url, headers, body });
          return url.endsWith("VerificationCheck")
            ? {
                code: 200,
                body: JSON.stringify({
                  status: body.includes("Code=123456") ? "approved" : "pending",
                }),
              }
            : { code: 201, body: "{}" };
        },
        base64: (text) => Buffer.from(text).toString("base64"),
      },
      "AC1",
      "secret",
      "VA1",
    );
    expect(twilio.send(PK)).toEqual({ ok: true, value: { ref: null } });
    expect(requests[0]?.url).toBe("https://verify.twilio.com/v2/Services/VA1/Verifications");
    expect(requests[0]?.body).toBe("To=%2B923001234567&Channel=sms");
    expect(requests[0]?.headers["Authorization"]).toBe(
      `Basic ${Buffer.from("AC1:secret").toString("base64")}`,
    );
    expect(twilio.check(PK, "123456", null)).toEqual({ ok: true, value: true });
    expect(twilio.check(PK, "654321", null)).toEqual({ ok: true, value: false });
  });

  it("chooses Twilio when configured, the local provider only on a local box", () => {
    const http = { request: () => ({ code: 200, body: "{}" }), base64: (t: string) => t };
    expect(
      providerFromEnv(
        {
          TWILIO_ACCOUNT_SID: "a",
          TWILIO_AUTH_TOKEN: "b",
          TWILIO_VERIFY_SID: "c",
          HULLA_ENV: "prod",
        },
        http,
      )?.name,
    ).toBe("twilio");
    expect(providerFromEnv({}, http)?.name).toBe("local");
    expect(providerFromEnv({ HULLA_ENV: "phase0" }, http)).toBeNull();
  });
});

describe("phone sign-in against the in-memory Nakama", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  const ctx = (userId: string, env: Record<string, string> = {}) =>
    ({ userId, username: userId, env, clientIp: "1.2.3.4" }) as unknown as nkruntime.Context;

  it("links on one device, signs in on another through the custom hook, and can be removed", () => {
    const m = memoryNk();
    const log = fakeLogger();
    m.user("owner");
    m.user("guest2");
    expect(
      parse(rpcPhoneStart(ctx("owner"), log, m.nk, JSON.stringify({ phone: PK }))),
    ).toMatchObject({ ok: true });
    expect(
      parse(rpcPhoneVerify(ctx("owner"), log, m.nk, JSON.stringify({ phone: PK, code: "234567" }))),
    ).toMatchObject({
      ok: true,
      result: "linked",
    });
    expect(parse(rpcPhoneStatus(ctx("owner"), log, m.nk, "{}"))).toEqual({
      ok: true,
      linked: true,
    });
    // Same number on a second device's guest, after the resend wait: a token for the owner's account.
    vi.useFakeTimers();
    vi.setSystemTime(Date.now() + 2 * 60_000);
    rpcPhoneStart(ctx("guest2"), log, m.nk, JSON.stringify({ phone: PK }));
    const verified = parse(
      rpcPhoneVerify(ctx("guest2"), log, m.nk, JSON.stringify({ phone: PK, code: "234567" })),
    );
    expect(verified).toMatchObject({ ok: true, result: "signIn" });
    const request = {
      account: { id: String(verified["token"]) },
      create: true,
    } as nkruntime.AuthenticateCustomRequest;
    const swapped = beforeAuthenticateCustom(ctx(""), log, m.nk, request);
    expect(swapped.create).toBe(false);
    expect(m.nk.authenticateCustom(swapped.account?.id ?? "", undefined, false).userId).toBe(
      "owner",
    );
    expect(() => beforeAuthenticateCustom(ctx(""), log, m.nk, request)).toThrow("auth.otp_expired");
    expect(nkOtpStore(m.nk).token(String(verified["token"]).slice(4))).toBeNull();
    expect(
      parse(rpcPhoneStatus(ctx("owner"), log, m.nk, JSON.stringify({ remove: true }))),
    ).toEqual({ ok: true, linked: false });
    expect(parse(rpcPhoneStatus(ctx(""), log, m.nk, "{}"))).toMatchObject({ ok: false });
  });

  it("is unavailable on a server without a provider or a pepper", () => {
    const m = memoryNk();
    m.user("u");
    const out = parse(
      rpcPhoneStart(
        ctx("u", { HULLA_ENV: "phase0" }),
        fakeLogger(),
        m.nk,
        JSON.stringify({ phone: PK }),
      ),
    );
    expect(out).toMatchObject({ ok: false, error: { code: "auth.otp_unavailable" } });
    expect(parse(rpcPhoneVerify(ctx(""), fakeLogger(), m.nk, "{}"))).toMatchObject({ ok: false });
  });
});
