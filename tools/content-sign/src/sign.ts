import { createSign } from "node:crypto";

import type { Manifest } from "./manifest";

/**
 * The bytes the manifest signature covers: the manifest without the schema pointer, keys in
 * order. Matches `canonicalManifest` in server/nakama/src/rpc/content.ts, which serves them as
 * `signedBytes` beside the signature.
 */
export function signedBytes(manifest: Manifest): string {
  return JSON.stringify({
    id: manifest.id,
    version: manifest.version,
    digest: manifest.digest,
    files: manifest.files,
  });
}

/**
 * RSA-SHA256 (PKCS #1 v1.5) over the signed bytes, base64. Signing happens at deploy time, not on
 * the box: Nakama's `rsaSha256Hash` returns the raw signature as a string that does not survive
 * the trip to JSON (ADR-0011).
 */
export function signManifest(manifest: Manifest, privateKeyPem: string): string {
  return createSign("RSA-SHA256").update(signedBytes(manifest)).sign(privateKeyPem, "base64");
}
