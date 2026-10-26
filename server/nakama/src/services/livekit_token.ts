import { base64Url, hmacSha256, utf8Encode } from "./bytes";

/**
 * A LiveKit access token (an HS256 JWT) for one table room (ADR-0024). Built here with the shared HMAC
 * so it runs in Nakama's runtime; the unit test checks it against Node's crypto. Nothing here touches
 * the network or the clock; the RPC supplies the time.
 */
export interface LiveKitConfig {
  /** The server clients connect to: `wss://` on a box, `ws://` for local development. */
  readonly url: string;
  /** Where the server reaches LiveKit's API: `LIVEKIT_API_URL`, else the client URL over HTTP. */
  readonly apiUrl: string;
  readonly apiKey: string;
  readonly apiSecret: string;
}

export interface RoomTokenInput {
  readonly room: string;
  /** The Nakama user id; LiveKit reports speakers by it. */
  readonly identity: string;
  /** Unix seconds. */
  readonly issueTs: number;
  readonly expireSeconds: number;
  /** A seat may send its microphone; a spectator only listens. */
  readonly publishAudio: boolean;
}

/** LiveKit refuses shorter secrets outside its development mode. */
const MIN_SECRET_LENGTH = 32;

export function buildRoomToken(config: LiveKitConfig, input: RoomTokenInput): string {
  const header = { alg: "HS256", typ: "JWT" };
  const claims = {
    iss: config.apiKey,
    sub: input.identity,
    nbf: input.issueTs,
    exp: input.issueTs + input.expireSeconds,
    video: {
      room: input.room,
      roomJoin: true,
      canSubscribe: true,
      canPublish: input.publishAudio,
      canPublishSources: input.publishAudio ? ["microphone"] : [],
      canPublishData: false,
      canUpdateOwnMetadata: false,
    },
  };
  const signingInput = `${encodeJson(header)}.${encodeJson(claims)}`;
  const signature = hmacSha256(utf8Encode(config.apiSecret), utf8Encode(signingInput));
  return `${signingInput}.${base64Url(signature)}`;
}

/** Reads the LiveKit settings from the runtime environment; anything missing means voice is off. */
export function liveKitConfigFrom(env: Readonly<Record<string, string>>): LiveKitConfig | null {
  const url = env["LIVEKIT_URL"] ?? "";
  const apiKey = env["LIVEKIT_API_KEY"] ?? "";
  const apiSecret = env["LIVEKIT_API_SECRET"] ?? "";
  if (!/^wss?:\/\/[^\s/]+/.test(url)) return null;
  if (apiKey === "" || apiSecret.length < MIN_SECRET_LENGTH) return null;
  const explicit = (env["LIVEKIT_API_URL"] ?? "").replace(/\/+$/, "");
  const apiUrl = /^https?:\/\/[^\s/]+/.test(explicit)
    ? explicit
    : url.replace(/^ws/, "http").replace(/\/+$/, "");
  return { url, apiUrl, apiKey, apiSecret };
}

function encodeJson(value: unknown): string {
  return base64Url(utf8Encode(JSON.stringify(value)));
}
