import { sha256, utf8Encode } from "@hulla/shared-ts";

/** Byte helpers for token formats; pure so they run in Nakama's runtime and in tests alike. */

export function concat(...parts: readonly Uint8Array[]): Uint8Array {
  const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
  let offset = 0;
  for (const part of parts) {
    out.set(part, offset);
    offset += part.length;
  }
  return out;
}

export function hmacSha256(key: Uint8Array, message: Uint8Array): Uint8Array {
  const blockSize = 64;
  const normalised = key.length > blockSize ? sha256(key) : key;
  const padded = new Uint8Array(blockSize);
  padded.set(normalised);
  const inner = new Uint8Array(blockSize);
  const outer = new Uint8Array(blockSize);
  for (let i = 0; i < blockSize; i += 1) {
    const byte = padded[i] ?? 0;
    inner[i] = byte ^ 0x36;
    outer[i] = byte ^ 0x5c;
  }
  return sha256(concat(outer, sha256(concat(inner, message))));
}

const BASE64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

export function base64(bytes: Uint8Array): string {
  let out = "";
  for (let i = 0; i < bytes.length; i += 3) {
    const a = bytes[i] ?? 0;
    const b = bytes[i + 1];
    const c = bytes[i + 2];
    const triple = (a << 16) | ((b ?? 0) << 8) | (c ?? 0);
    out += BASE64.charAt((triple >>> 18) & 63) + BASE64.charAt((triple >>> 12) & 63);
    out += b === undefined ? "=" : BASE64.charAt((triple >>> 6) & 63);
    out += c === undefined ? "=" : BASE64.charAt(triple & 63);
  }
  return out;
}

/** Base64 with the URL alphabet and no padding (RFC 4648 §5), as JWTs use. */
export function base64Url(bytes: Uint8Array): string {
  return base64(bytes).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

export { utf8Encode };
