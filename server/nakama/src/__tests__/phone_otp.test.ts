import { afterEach, describe, expect, it, vi } from "vitest";

import {
  beforeAuthenticateCustom,
  nkOtpStore,
  rpcPhoneStart,
  rpcPhoneStatus,
  rpcPhoneVerify,
} from "../rpc/phone";
import {
  codeFromUuid,
  localProvider,
  NO_PROVIDERS,
  providersFromEnv,
  twilioVerify,
  whatsappCloud,
  type OtpProvider,
} from "../services/otp_providers";
import {
  callingCodeOf,
  EMPTY_PHONE,
  maskPhone,
  normalisePhone,
  OTP_LIMITS,
  redeemToken,
  startOtp,
  verifyOtp,
  type OtpPorts,
  type OtpStore,
  type PhoneState,
} from "../services/phone_otp";

import { fakeLogger, memoryNk } from "./fake-nk";

type Json = Record<string, unknown>;
const CODES = {
  newCode: () => "123456",
  hashCode: (phone: string, code: string) => `hash(${phone}:${code})`,
};
const TWILIO_ENV = { TWILIO_ACCOUNT_SID: "a", TWILIO_AUTH_TOKEN: "b", TWILIO_VERIFY_SID: "c" };
const WHATSAPP_ENV = {
  WHATSAPP_TOKEN: "t",
  WHATSAPP_PHONE_NUMBER_ID: "1",
  WHATSAPP_TEMPLATE: "hulla_code",
};
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
    providers: { whatsapp: null, sms: localProvider() },
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
    expect(sent).toMatchObject({ channel: "sms", channels: ["sms"], fallback: false });
    expect(counted).toContainEqual({
      metric: "sent",
      tags: { country: "PK", provider: "local", channel: "sms" },
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
    expect(startOtp(ports({ providers: NO_PROVIDERS }).ports, "g", "", PK, T0)).toMatchObject({
      error: { code: "auth.otp_unavailable" },
    });
    const failing: OtpProvider = {
      name: "down",
      channel: "sms",
      send: () => ({ ok: false, reason: "x" }),
      check: () => ({ ok: false, reason: "x" }),
    };
    const down = { whatsapp: null, sms: failing };
    expect(startOtp(ports({ providers: down }).ports, "g", "", PK, T0)).toMatchObject({
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

  it("chooses Meta and Twilio when configured, the local providers only on a local box", () => {
    const http = { request: () => ({ code: 200, body: "{}" }), base64: (t: string) => t };
    const names = (env: Record<string, string>) => {
      const p = providersFromEnv(env, http, CODES);
      return [p.whatsapp?.name ?? null, p.sms?.name ?? null];
    };
    expect(names({ ...TWILIO_ENV, HULLA_ENV: "prod" })).toEqual([null, "twilio"]);
    expect(names({ ...WHATSAPP_ENV, HULLA_ENV: "prod" })).toEqual(["meta", null]);
    expect(names({ ...WHATSAPP_ENV, ...TWILIO_ENV })).toEqual(["meta", "twilio"]);
    expect(names({})).toEqual(["local", "local"]);
    expect(names({ HULLA_ENV: "phase0" })).toEqual([null, null]);
  });

  it("WhatsApp sends the authentication template and keeps only the code's hash", () => {
    const requests: { url: string; headers: Record<string, string>; body: string }[] = [];
    let answer = { code: 200, body: JSON.stringify({ messages: [{ id: "wamid.1" }] }) };
    const meta = whatsappCloud(
      {
        request: (url, _method, headers, body) => {
          requests.push({ url, headers, body });
          return answer;
        },
        base64: (t) => t,
      },
      CODES,
      {
        token: "tok",
        phoneNumberId: "555",
        template: "hulla_code",
        language: "en",
        apiVersion: "v24.0",
      },
    );
    const sent = meta.send(PK);
    expect(sent).toEqual({ ok: true, value: { ref: `hash(${PK}:123456)` } });
    expect(requests[0]?.url).toBe("https://graph.facebook.com/v24.0/555/messages");
    expect(requests[0]?.headers["Authorization"]).toBe("Bearer tok");
    expect(JSON.parse(requests[0]?.body ?? "")).toEqual({
      messaging_product: "whatsapp",
      recipient_type: "individual",
      to: "923001234567",
      type: "template",
      template: {
        name: "hulla_code",
        language: { code: "en" },
        components: [
          { type: "body", parameters: [{ type: "text", text: "123456" }] },
          {
            type: "button",
            sub_type: "url",
            index: "0",
            parameters: [{ type: "text", text: "123456" }],
          },
        ],
      },
    });
    const ref = sent.ok ? sent.value.ref : null;
    expect(meta.check(PK, "123456", ref)).toEqual({ ok: true, value: true });
    expect(meta.check(PK, "654321", ref)).toEqual({ ok: true, value: false });
    expect(meta.check(PK, "123456", null)).toEqual({ ok: true, value: false });
    answer = { code: 404, body: JSON.stringify({ error: { code: 132001 } }) };
    expect(meta.send(PK)).toEqual({ ok: false, reason: "http_404_132001" });
    answer = { code: 500, body: "not json" };
    expect(meta.send(PK)).toEqual({ ok: false, reason: "http_500" });
  });

  it("a request that throws is a failed send, not a crash", () => {
    const http = {
      request: () => {
        throw new Error("timeout");
      },
      base64: (t: string) => t,
    };
    const meta = whatsappCloud(http, CODES, {
      token: "t",
      phoneNumberId: "1",
      template: "x",
      language: "en",
      apiVersion: "v24.0",
    });
    expect(meta.send(PK)).toEqual({ ok: false, reason: "request_failed" });
    const twilio = twilioVerify(http, "AC1", "secret", "VA1");
    expect(twilio.send(PK)).toEqual({ ok: false, reason: "request_failed" });
    expect(twilio.check(PK, "123456", null)).toEqual({ ok: false, reason: "request_failed" });
  });

  it("makes six-digit codes from a uuid's random bits", () => {
    expect(codeFromUuid("00000000-0000-4000-8000-000000000000")).toBe("000000");
    expect(codeFromUuid("ffffffff-ffff-4fff-bfff-ffffffffffff")).toBe(
      String(0xffffffffffff % 1_000_000).padStart(6, "0"),
    );
    expect(codeFromUuid("not-a-uuid")).toMatch(/^\d{6}$/);
  });
});

describe("WhatsApp first, SMS as the fallback (OQ-041)", () => {
  const both = { whatsapp: localProvider("whatsapp"), sms: localProvider("sms") };
  /** A WhatsApp provider whose code is always 111111, to tell the channels apart at check time. */
  const fixedWhatsApp: OtpProvider = {
    name: "meta",
    channel: "whatsapp",
    send: () => ({ ok: true, value: { ref: "wa" } }),
    check: (_phone, code, ref) => ({ ok: true, value: ref === "wa" && code === "111111" }),
  };

  it("sends on WhatsApp when it is set up and counts the cost on that channel", () => {
    const { ports: p, counted } = ports({ providers: both });
    expect(startOtp(p, "guest", "", PK, T0)).toMatchObject({
      ok: true,
      channel: "whatsapp",
      channels: ["whatsapp", "sms"],
      fallback: false,
    });
    expect(counted).toContainEqual({
      metric: "sent",
      tags: { country: "PK", provider: "local", channel: "whatsapp" },
      cents: 1,
    });
    expect(verifyOtp(p, "guest", PK, "234567", T0 + 1000)).toMatchObject({ result: "linked" });
    expect(counted).toContainEqual({
      metric: "verified",
      tags: { country: "PK", channel: "whatsapp", result: "linked" },
    });
  });

  it("falls back to SMS in the same send when WhatsApp refuses the message", () => {
    const { ports: p, counted } = ports({ providers: both });
    const number = "+923001234569";
    expect(startOtp(p, "guest", "", number, T0)).toMatchObject({
      ok: true,
      channel: "sms",
      fallback: true,
    });
    expect(counted).toContainEqual({
      metric: "failed",
      tags: { channel: "whatsapp", provider: "local", reason: "local_not_on_whatsapp" },
    });
    expect(counted.filter((c) => c.metric === "sent")).toEqual([
      { metric: "sent", tags: { country: "PK", provider: "local", channel: "sms" }, cents: 15 },
    ]);
    expect(p.store.phone(`h(${number})`).sends).toHaveLength(1);
    expect(verifyOtp(p, "guest", number, "234569", T0)).toMatchObject({ result: "linked" });
  });

  it("sends by SMS instead only after the resend wait, and it counts against the limits", () => {
    const { ports: p } = ports({ providers: { whatsapp: fixedWhatsApp, sms: localProvider() } });
    expect(startOtp(p, "guest", "", PK, T0)).toMatchObject({ channel: "whatsapp" });
    expect(startOtp(p, "guest", "", PK, T0 + 10_000, "sms")).toMatchObject({
      ok: false,
      error: { code: "auth.otp_rate_limited", detail: { retryAfterTs: T0 + OTP_LIMITS.resendMs } },
    });
    expect(startOtp(p, "guest", "", PK, T0 + 61_000, "sms")).toMatchObject({
      ok: true,
      channel: "sms",
      fallback: false,
    });
    // The SMS code is the live one now; the WhatsApp code no longer works.
    expect(verifyOtp(p, "guest", PK, "111111", T0 + 62_000)).toMatchObject({
      error: { code: "auth.otp_wrong_code" },
    });
    expect(startOtp(p, "guest", "", PK, T0 + 122_000, "sms").ok).toBe(true);
    expect(startOtp(p, "guest", "", PK, T0 + 183_000, "sms")).toMatchObject({
      error: { code: "auth.otp_rate_limited" },
    });
  });

  it("checks the code with the channel it went out on", () => {
    const { ports: p } = ports({ providers: { whatsapp: fixedWhatsApp, sms: localProvider() } });
    startOtp(p, "guest", "", PK, T0);
    expect(verifyOtp(p, "guest", PK, "234567", T0)).toMatchObject({
      error: { code: "auth.otp_wrong_code" },
    });
    expect(verifyOtp(p, "guest", PK, "111111", T0)).toMatchObject({ result: "linked" });
  });

  it("a code stored before channels existed is checked by SMS", () => {
    const { ports: p } = ports({ providers: { whatsapp: fixedWhatsApp, sms: localProvider() } });
    p.store.writePhone(`h(${PK})`, {
      sends: [T0],
      expiresTs: T0 + OTP_LIMITS.codeTtlMs,
      attempts: 0,
      ref: null,
      requestedBy: "guest",
    });
    expect(verifyOtp(p, "guest", PK, "234567", T0)).toMatchObject({ result: "linked" });
  });

  it("asks for SMS where only WhatsApp is set up: unavailable, and nothing is spent", () => {
    const { ports: p, counted } = ports({ providers: { whatsapp: fixedWhatsApp, sms: null } });
    expect(startOtp(p, "guest", "", PK, T0, "sms")).toMatchObject({
      error: { code: "auth.otp_unavailable", detail: { channel: "sms" } },
    });
    expect(counted.some((c) => c.metric === "sent")).toBe(false);
    expect(startOtp(p, "guest", "", PK, T0, "whatsapp")).toMatchObject({
      channel: "whatsapp",
      channels: ["whatsapp"],
    });
  });

  it("asks for WhatsApp where only SMS is set up: SMS, without calling it a fallback", () => {
    const { ports: p } = ports();
    expect(startOtp(p, "guest", "", PK, T0, "whatsapp")).toMatchObject({
      channel: "sms",
      fallback: false,
    });
  });

  it("is unavailable when every channel fails, and the failed send is not counted", () => {
    const broken: OtpProvider = {
      ...fixedWhatsApp,
      send: () => ({ ok: false, reason: "http_400" }),
    };
    const { ports: p, counted } = ports({ providers: { whatsapp: broken, sms: null } });
    expect(startOtp(p, "guest", "", PK, T0)).toMatchObject({
      error: { code: "auth.otp_unavailable", detail: { provider: "meta" } },
    });
    expect(p.store.phone(`h(${PK})`).sends).toHaveLength(0);
    expect(counted.some((c) => c.metric === "sent")).toBe(false);
    expect(verifyOtp(p, "guest", PK, "111111", T0)).toMatchObject({
      error: { code: "auth.otp_expired" },
    });
    expect(
      verifyOtp(ports({ providers: NO_PROVIDERS }).ports, "g", PK, "111111", T0),
    ).toMatchObject({
      error: { code: "auth.otp_unavailable" },
    });
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

  const PROD = { HULLA_ENV: "phase0", HULLA_PHONE_PEPPER: "pepper" };

  it("sends on WhatsApp through Meta and checks the code the server made", () => {
    const m = memoryNk();
    m.user("u");
    const env = { ...PROD, ...WHATSAPP_ENV, ...TWILIO_ENV };
    const added: { name: string; tags: Record<string, string>; delta: number }[] = [];
    const nk = {
      ...m.nk,
      metricsCounterAdd: (name: string, tags: Record<string, string>, delta: number) =>
        void added.push({ name, tags, delta }),
    } as nkruntime.Nakama;
    const started = parse(
      rpcPhoneStart(ctx("u", env), fakeLogger(), nk, JSON.stringify({ phone: PK })),
    );
    expect(started).toMatchObject({ ok: true, channel: "whatsapp", channels: ["whatsapp", "sms"] });
    expect(m.requests).toHaveLength(1);
    expect(m.requests[0]?.url).toBe("https://graph.facebook.com/v24.0/1/messages");
    const sent = JSON.parse(m.requests[0]?.body ?? "{}") as {
      template: { components: { parameters: { text: string }[] }[] };
    };
    const code = sent.template.components[0]?.parameters[0]?.text ?? "";
    expect(code).toMatch(/^\d{6}$/);
    // Only the hash of the code is stored.
    expect(JSON.stringify(nkOtpStore(m.nk).phone(nk.sha256Hash(`pepper:${PK}`)))).not.toContain(
      `"${code}"`,
    );
    expect(added).toContainEqual({
      name: "hulla_otp_cost_microusd",
      tags: { country: "PK", channel: "whatsapp" },
      delta: 10_000,
    });
    expect(
      parse(rpcPhoneVerify(ctx("u", env), fakeLogger(), nk, JSON.stringify({ phone: PK, code }))),
    ).toMatchObject({ ok: true, result: "linked" });
  });

  it("falls back to Twilio SMS when Meta refuses, and says so", () => {
    const m = memoryNk();
    m.user("u");
    m.http = (url) =>
      url.startsWith("https://graph.facebook.com/")
        ? { code: 400, headers: [], body: JSON.stringify({ error: { code: 132001 } }) }
        : { code: 201, headers: [], body: "{}" };
    const env = { ...PROD, ...WHATSAPP_ENV, ...TWILIO_ENV };
    const started = parse(
      rpcPhoneStart(ctx("u", env), fakeLogger(), m.nk, JSON.stringify({ phone: PK })),
    );
    expect(started).toMatchObject({ ok: true, channel: "sms", fallback: true });
    expect(m.requests.map((r) => r.url)).toEqual([
      "https://graph.facebook.com/v24.0/1/messages",
      "https://verify.twilio.com/v2/Services/c/Verifications",
    ]);
  });

  it("refuses a channel it does not know", () => {
    const m = memoryNk();
    m.user("u");
    expect(
      parse(
        rpcPhoneStart(ctx("u"), fakeLogger(), m.nk, JSON.stringify({ phone: PK, channel: "fax" })),
      ),
    ).toMatchObject({ ok: false, error: { code: "table.invalid_payload" } });
    expect(parse(rpcPhoneStart(ctx("u"), fakeLogger(), m.nk, "[]"))).toMatchObject({
      error: { code: "table.invalid_payload" },
    });
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
