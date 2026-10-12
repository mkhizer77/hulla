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

export function u16le(value: number): Uint8Array {
  return new Uint8Array([value & 0xff, (value >>> 8) & 0xff]);
}

export function u32le(value: number): Uint8Array {
  return new Uint8Array([
    value & 0xff,
    (value >>> 8) & 0xff,
    (value >>> 16) & 0xff,
    (value >>> 24) & 0xff,
  ]);
}

/** A 16-bit little-endian length followed by the bytes. */
export function lengthPrefixed(bytes: Uint8Array): Uint8Array {
  return concat(u16le(bytes.length), bytes);
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

export function adler32(bytes: Uint8Array): number {
  let a = 1;
  let b = 0;
  for (const byte of bytes) {
    a = (a + byte) % 65521;
    b = (b + a) % 65521;
  }
  return ((b << 16) | a) >>> 0;
}

/**
 * A zlib stream made of stored (uncompressed) deflate blocks: what a token consumer inflates
 * without a compressor on our side. Nakama's runtime has no zlib.
 */
export function zlibStored(data: Uint8Array): Uint8Array {
  const blocks: Uint8Array[] = [new Uint8Array([0x78, 0x01])];
  const max = 65535;
  let offset = 0;
  do {
    const chunk = data.subarray(offset, Math.min(offset + max, data.length));
    const last = offset + chunk.length >= data.length;
    const len = chunk.length;
    blocks.push(new Uint8Array([last ? 1 : 0]), u16le(len), u16le(~len & 0xffff), chunk);
    offset += chunk.length;
  } while (offset < data.length);
  const checksum = adler32(data);
  blocks.push(
    new Uint8Array([
      (checksum >>> 24) & 0xff,
      (checksum >>> 16) & 0xff,
      (checksum >>> 8) & 0xff,
      checksum & 0xff,
    ]),
  );
  return concat(...blocks);
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

export { utf8Encode };
