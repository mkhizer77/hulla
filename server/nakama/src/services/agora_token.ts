import {
  base64,
  concat,
  hmacSha256,
  lengthPrefixed,
  u16le,
  u32le,
  utf8Encode,
  zlibStored,
} from "./bytes";

/**
 * Agora AccessToken2 ("007") for an RTC channel, built here because the runtime has no zlib
 * (ADR-0012; the byte layout mirrors Agora's reference builder and the unit test checks it
 * against that builder). Nothing here touches the network; the RPC supplies clock and salt.
 */
export interface AgoraConfig {
  readonly appId: string;
  readonly appCertificate: string;
}

export interface RtcTokenInput {
  readonly channel: string;
  /** The user account; empty means any uid may use the token. */
  readonly account: string;
  /** Unix seconds. */
  readonly issueTs: number;
  /** Seconds the token and its privileges stay valid. */
  readonly expireSeconds: number;
  /** 1 to 99,999,999, random per token. */
  readonly salt: number;
  readonly publishAudio: boolean;
}

const VERSION = "007";
const SERVICE_RTC = 1;
const PRIVILEGE_JOIN = 1;
const PRIVILEGE_PUBLISH_AUDIO = 2;

function privilegeMap(entries: readonly (readonly [number, number])[]): Uint8Array {
  const sorted = [...entries].sort((a, b) => a[0] - b[0]);
  return concat(
    u16le(sorted.length),
    ...sorted.map(([key, value]) => concat(u16le(key), u32le(value))),
  );
}

/** The exact packing of the reference builder, so Agora accepts the token. */
export function buildRtcToken(config: AgoraConfig, input: RtcTokenInput): string {
  const privileges: (readonly [number, number])[] = [[PRIVILEGE_JOIN, input.expireSeconds]];
  if (input.publishAudio) privileges.push([PRIVILEGE_PUBLISH_AUDIO, input.expireSeconds]);
  const service = concat(
    u16le(SERVICE_RTC),
    privilegeMap(privileges),
    lengthPrefixed(utf8Encode(input.channel)),
    lengthPrefixed(utf8Encode(input.account)),
  );
  const signingInfo = concat(
    lengthPrefixed(utf8Encode(config.appId)),
    u32le(input.issueTs),
    u32le(input.expireSeconds),
    u32le(input.salt),
    u16le(1),
    service,
  );
  let signing = hmacSha256(u32le(input.issueTs), utf8Encode(config.appCertificate));
  signing = hmacSha256(u32le(input.salt), signing);
  const signature = hmacSha256(signing, signingInfo);
  const content = concat(lengthPrefixed(signature), signingInfo);
  return `${VERSION}${base64(zlibStored(content))}`;
}

export function isAgoraId(value: string): boolean {
  return /^[0-9a-fA-F]{32}$/.test(value);
}
