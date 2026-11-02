/**
 * Who sends phone codes (GDD 10.2, OQ-041). WhatsApp goes straight to Meta's Cloud API: the server
 * makes the code and keeps only its hash, so each code costs Meta's authentication rate and no
 * per-verification fee. SMS stays on Twilio Verify, which makes, sends and checks its own code.
 */
export type OtpChannel = "whatsapp" | "sms";

/** The order a send tries the channels in when the player did not ask for one. */
export const OTP_CHANNELS: readonly OtpChannel[] = ["whatsapp", "sms"];

export type ProviderResult<T> =
  { readonly ok: true; readonly value: T } | { readonly ok: false; readonly reason: string };

export interface OtpProvider {
  readonly name: string;
  readonly channel: OtpChannel;
  /** Sends a code to the number; `ref` is whatever the provider needs to check it later. */
  send(phone: string): ProviderResult<{ readonly ref: string | null }>;
  /** True when the code is the one sent; false for a wrong code. */
  check(phone: string, code: string, ref: string | null): ProviderResult<boolean>;
}

/** One provider per channel, or null where that channel is not set up. */
export type OtpProviders = Readonly<Record<OtpChannel, OtpProvider | null>>;

export const NO_PROVIDERS: OtpProviders = { whatsapp: null, sms: null };

export interface HttpPort {
  request(
    url: string,
    method: string,
    headers: Record<string, string>,
    body: string,
  ): { code: number; body: string };
  base64(text: string): string;
}

/** Six digits from the first 48 random bits of a v4 uuid (Nakama's runtime has no other CSPRNG). */
export function codeFromUuid(uuid: string): string {
  const bits = Number.parseInt(uuid.replace(/-/g, "").slice(0, 12), 16);
  return String((Number.isNaN(bits) ? 0 : bits) % 1_000_000).padStart(6, "0");
}

/** Codes the server makes itself: a random six digits, and a peppered hash so none is stored. */
export interface CodePort {
  newCode(): string;
  hashCode(phone: string, code: string): string;
}

const form = (fields: Record<string, string>): string =>
  Object.entries(fields)
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`)
    .join("&");

const ok2xx = (code: number): boolean => code >= 200 && code < 300;

/** Twilio Verify by SMS: Twilio makes, sends and checks the code (env TWILIO_*; owner's account). */
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
    channel: "sms",
    send(phone) {
      try {
        const res = http.request(
          `${base}/Verifications`,
          "POST",
          headers,
          form({ To: phone, Channel: "sms" }),
        );
        if (ok2xx(res.code)) return { ok: true, value: { ref: null } };
        return { ok: false, reason: `http_${String(res.code)}` };
      } catch {
        return { ok: false, reason: "request_failed" };
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
        if (!ok2xx(res.code)) return { ok: false, reason: `http_${String(res.code)}` };
        const body = JSON.parse(res.body) as { status?: unknown };
        return { ok: true, value: body.status === "approved" };
      } catch {
        return { ok: false, reason: "request_failed" };
      }
    },
  };
}

export interface WhatsAppConfig {
  readonly token: string;
  readonly phoneNumberId: string;
  /** An approved authentication template with a copy-code button. */
  readonly template: string;
  readonly language: string;
  readonly apiVersion: string;
}

/** Meta's error code from a failed Cloud API call (132001: no such template, and so on). */
function metaErrorCode(body: string): string {
  try {
    const parsed = JSON.parse(body) as { error?: { code?: unknown } };
    const code = parsed.error?.code;
    return typeof code === "number" || typeof code === "string" ? `_${String(code)}` : "";
  } catch {
    return "";
  }
}

/**
 * WhatsApp Cloud API: sends the authentication template with the code in the body and in the
 * copy-code button. Meta answers 200 once it accepts the message; a number that is not on WhatsApp
 * fails later, so the player's way out is "send by SMS instead" after the resend wait.
 */
export function whatsappCloud(
  http: HttpPort,
  codes: CodePort,
  config: WhatsAppConfig,
): OtpProvider {
  const url = `https://graph.facebook.com/${config.apiVersion}/${config.phoneNumberId}/messages`;
  const headers = {
    Authorization: `Bearer ${config.token}`,
    "Content-Type": "application/json",
  };
  return {
    name: "meta",
    channel: "whatsapp",
    send(phone) {
      const code = codes.newCode();
      const body = JSON.stringify({
        messaging_product: "whatsapp",
        recipient_type: "individual",
        to: phone.replace(/^\+/, ""),
        type: "template",
        template: {
          name: config.template,
          language: { code: config.language },
          components: [
            { type: "body", parameters: [{ type: "text", text: code }] },
            {
              type: "button",
              sub_type: "url",
              index: "0",
              parameters: [{ type: "text", text: code }],
            },
          ],
        },
      });
      try {
        const res = http.request(url, "POST", headers, body);
        if (ok2xx(res.code)) return { ok: true, value: { ref: codes.hashCode(phone, code) } };
        return { ok: false, reason: `http_${String(res.code)}${metaErrorCode(res.body)}` };
      } catch {
        return { ok: false, reason: "request_failed" };
      }
    },
    check: (phone, code, ref) => ({
      ok: true,
      value: ref !== null && codes.hashCode(phone, code) === ref,
    }),
  };
}

/**
 * A development box's provider: nothing is sent and the code is the last six digits of the number,
 * so a tester can sign in from any number. On WhatsApp a number ending in 9 fails to send, so the
 * fallback to SMS can be tried. Only HULLA_ENV=local gets it.
 */
export function localProvider(channel: OtpChannel = "sms"): OtpProvider {
  const codeFor = (phone: string): string => phone.slice(-6).padStart(6, "0");
  return {
    name: "local",
    channel,
    send: (phone) =>
      channel === "whatsapp" && phone.endsWith("9")
        ? { ok: false, reason: "local_not_on_whatsapp" }
        : { ok: true, value: { ref: null } },
    check: (phone, code) => ({ ok: true, value: code === codeFor(phone) }),
  };
}

export function providersFromEnv(
  env: Readonly<Record<string, string>>,
  http: HttpPort,
  codes: CodePort,
): OtpProviders {
  const get = (name: string, fallback = ""): string => {
    const value = env[name] ?? "";
    return value === "" ? fallback : value;
  };
  const sid = get("TWILIO_ACCOUNT_SID");
  const token = get("TWILIO_AUTH_TOKEN");
  const service = get("TWILIO_VERIFY_SID");
  const waToken = get("WHATSAPP_TOKEN");
  const waNumber = get("WHATSAPP_PHONE_NUMBER_ID");
  const waTemplate = get("WHATSAPP_TEMPLATE");
  const sms =
    sid !== "" && token !== "" && service !== "" ? twilioVerify(http, sid, token, service) : null;
  const whatsapp =
    waToken !== "" && waNumber !== "" && waTemplate !== ""
      ? whatsappCloud(http, codes, {
          token: waToken,
          phoneNumberId: waNumber,
          template: waTemplate,
          language: get("WHATSAPP_TEMPLATE_LANGUAGE", "en"),
          apiVersion: get("WHATSAPP_API_VERSION", "v24.0"),
        })
      : null;
  if (sms !== null || whatsapp !== null) return { whatsapp, sms };
  if (get("HULLA_ENV", "local") === "local") {
    return { whatsapp: localProvider("whatsapp"), sms: localProvider("sms") };
  }
  return NO_PROVIDERS;
}
