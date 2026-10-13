import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { CONTENT_FILES, CONTENT_MANIFEST } from "../content";
import { log } from "../infra/log";

/** The bytes the signature covers: the manifest without the schema pointer, keys in order. */
export function canonicalManifest(): string {
  return JSON.stringify({
    id: CONTENT_MANIFEST.id,
    version: CONTENT_MANIFEST.version,
    digest: CONTENT_MANIFEST.digest,
    files: CONTENT_MANIFEST.files,
  });
}

export const SIGNING_KEY_ENV = "HULLA_CONTENT_SIGNING_KEY";
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
 * The manifest the client pins for its session (ADR-0011, GDD 18.9): signed with RSA-SHA256
 * when the box holds a private key, unsigned on a development box, which clients accept only in
 * development builds.
 */
export function rpcContentManifest(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
): string {
  const key = ctx.env[SIGNING_KEY_ENV] ?? "";
  const keyId = ctx.env[SIGNING_KEY_ID_ENV] ?? "";
  const canonical = canonicalManifest();
  let signature: string | null = null;
  if (key !== "") {
    try {
      signature = nk.rsaSha256Hash(canonical, key);
    } catch (error: unknown) {
      log.error(logger, "content manifest not signed", { error: String(error) });
    }
  }
  return JSON.stringify({
    ok: true,
    manifest: {
      id: CONTENT_MANIFEST.id,
      version: CONTENT_MANIFEST.version,
      digest: CONTENT_MANIFEST.digest,
      files: CONTENT_MANIFEST.files,
    },
    signature,
    algorithm: signature === null ? null : "RSA-SHA256",
    keyId: signature === null ? null : keyId,
    signedBytes: canonical,
  });
}

/** `{ path }`: one bundled content file; the client checks its hash against the manifest. */
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
  });
}
