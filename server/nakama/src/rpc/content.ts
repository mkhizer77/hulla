import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { CONTENT_FILES, CONTENT_MANIFEST, CONTENT_TEXT } from "../content";

/** The bytes the signature covers: the manifest without the schema pointer, keys in order. */
export function canonicalManifest(): string {
  return JSON.stringify({
    id: CONTENT_MANIFEST.id,
    version: CONTENT_MANIFEST.version,
    digest: CONTENT_MANIFEST.digest,
    files: CONTENT_MANIFEST.files,
  });
}

export const SIGNATURE_ENV = "HULLA_CONTENT_SIGNATURE";
export const SIGNING_KEY_ID_ENV = "HULLA_CONTENT_SIGNING_KEY_ID";

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

/**
 * The content manifest (ADR-0011) with the RSA-SHA256 signature over `signedBytes`. The signature
 * is made at deploy time (`pnpm content:sign`) and set on the box as HULLA_CONTENT_SIGNATURE, so
 * the private key never sits on the server; Nakama's own `rsaSha256Hash` returns raw bytes that do
 * not survive JSON. Unsigned on a development box, which clients accept only in development builds.
 */
export function rpcContentManifest(ctx: nkruntime.Context): string {
  const signature = (ctx.env[SIGNATURE_ENV] ?? "").trim();
  const signed = signature !== "";
  return JSON.stringify({
    ok: true,
    manifest: {
      id: CONTENT_MANIFEST.id,
      version: CONTENT_MANIFEST.version,
      digest: CONTENT_MANIFEST.digest,
      files: CONTENT_MANIFEST.files,
    },
    signature: signed ? signature : null,
    algorithm: signed ? "RSA-SHA256" : null,
    keyId: signed ? (ctx.env[SIGNING_KEY_ID_ENV] ?? "") : null,
    signedBytes: canonicalManifest(),
  });
}

/**
 * `{ path }`: one bundled content file. `text` is the exact bytes the manifest hashes, which the
 * client checks before it caches the file; `document` is the same file parsed.
 */
export function rpcContentGet(
  _ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  _nk: nkruntime.Nakama,
  payload: string,
): string {
  const path = parse(payload)["path"];
  const file = typeof path === "string" ? CONTENT_FILES[path] : undefined;
  const entry = CONTENT_MANIFEST.files.find((f) => f.path === path);
  if (file === undefined || entry === undefined) {
    return JSON.stringify({
      ok: false,
      error: ruleError(ErrorCodes.content.packUnknown, { reason: "path" }),
    });
  }
  return JSON.stringify({
    ok: true,
    path: entry.path,
    sha256: entry.sha256,
    version: entry.version,
    document: file,
    text: CONTENT_TEXT[entry.path],
  });
}
