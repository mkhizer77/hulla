import { createHash } from "node:crypto";
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative } from "node:path";

/**
 * The content manifest (ADR-0011, GDD 18.9): every JSON file under `content/` except the manifest
 * itself, with its type (the folder), id, version, SHA-256 and size. Deterministic: the same
 * tree always yields the same manifest, so CI can fail on drift; `digest` names the set.
 */
export const MANIFEST_FILE = "manifest.json";
export const MANIFEST_SCHEMA = "../tools/content-schemas/manifest.schema.json";

export interface ManifestEntry {
  readonly path: string;
  readonly type: string;
  readonly id: string;
  readonly version: number;
  readonly sha256: string;
  readonly bytes: number;
}

export interface Manifest {
  readonly schema: string;
  readonly id: "content-manifest";
  readonly version: number;
  readonly digest: string;
  readonly files: readonly ManifestEntry[];
}

function walk(dir: string, out: string[] = []): string[] {
  for (const entry of readdirSync(dir).sort()) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) walk(full, out);
    else if (entry.endsWith(".json")) out.push(full);
  }
  return out;
}

export function sha256Hex(bytes: Buffer | string): string {
  return createHash("sha256").update(bytes).digest("hex");
}

/** Builds the manifest from a content directory; `previousVersion` carries the counter forward. */
export function buildManifest(contentDir: string, previousVersion = 0): Manifest {
  const files: ManifestEntry[] = [];
  for (const full of walk(contentDir)) {
    const path = relative(contentDir, full).split("\\").join("/");
    if (path === MANIFEST_FILE) continue;
    const bytes = readFileSync(full);
    const document = JSON.parse(bytes.toString("utf8")) as Record<string, unknown>;
    const id = typeof document["id"] === "string" ? document["id"] : path;
    const version = typeof document["version"] === "number" ? document["version"] : 1;
    files.push({
      path,
      type: path.split("/")[0] ?? "content",
      id,
      version,
      sha256: sha256Hex(bytes),
      bytes: bytes.length,
    });
  }
  files.sort((a, b) => a.path.localeCompare(b.path));
  const digest = sha256Hex(files.map((f) => `${f.path}:${f.sha256}`).join("\n"));
  return {
    schema: MANIFEST_SCHEMA,
    id: "content-manifest",
    version: Math.max(1, previousVersion),
    digest,
    files,
  };
}

/** The manifest version moves up by one whenever the file set or any hash changes. */
export function nextManifest(contentDir: string, previous: Manifest | null): Manifest {
  const fresh = buildManifest(contentDir, previous?.version ?? 1);
  if (previous !== null && previous.digest === fresh.digest)
    return { ...fresh, version: previous.version };
  return { ...fresh, version: previous === null ? 1 : previous.version + 1 };
}

export function readManifest(contentDir: string): Manifest | null {
  try {
    return JSON.parse(readFileSync(join(contentDir, MANIFEST_FILE), "utf8")) as Manifest;
  } catch {
    return null;
  }
}

export function serialise(manifest: Manifest): string {
  return `${JSON.stringify(manifest, null, 2)}\n`;
}
