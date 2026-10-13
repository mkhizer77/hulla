import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import { buildManifest, nextManifest, readManifest, sha256Hex, serialise } from "../manifest";

function tree(files: Record<string, object>): string {
  const dir = mkdtempSync(join(tmpdir(), "hulla-manifest-"));
  for (const [path, doc] of Object.entries(files)) {
    mkdirSync(join(dir, path, ".."), { recursive: true });
    writeFileSync(join(dir, path), JSON.stringify(doc));
  }
  return dir;
}

const repoContent = join(import.meta.dirname, "..", "..", "..", "..", "content");

describe("the content manifest (ADR-0011)", () => {
  it("lists every file but itself with type, id, version and hash, in a stable order", () => {
    const dir = tree({
      "presets/thulla/b.json": { id: "b", version: 2 },
      "presets/thulla/a.json": { id: "a", version: 1 },
      "emotes/v1.json": { id: "emotes", version: 1 },
      "manifest.json": { id: "content-manifest", version: 9, files: [] },
    });
    const manifest = buildManifest(dir);
    expect(manifest.files.map((f) => [f.path, f.type, f.id, f.version])).toEqual([
      ["emotes/v1.json", "emotes", "emotes", 1],
      ["presets/thulla/a.json", "presets", "a", 1],
      ["presets/thulla/b.json", "presets", "b", 2],
    ]);
    expect(manifest.files[0]?.sha256).toBe(sha256Hex(JSON.stringify({ id: "emotes", version: 1 })));
    expect(buildManifest(dir)).toEqual(manifest);
  });

  it("bumps the version only when a file changes", () => {
    const dir = tree({ "emotes/v1.json": { id: "emotes", version: 1 } });
    const first = nextManifest(dir, null);
    expect(first.version).toBe(1);
    writeFileSync(join(dir, "manifest.json"), serialise(first));
    const same = nextManifest(dir, readManifest(dir));
    expect(same.version).toBe(1);
    writeFileSync(join(dir, "emotes", "v1.json"), JSON.stringify({ id: "emotes", version: 2 }));
    const changed = nextManifest(dir, readManifest(dir));
    expect(changed.version).toBe(2);
    expect(changed.digest).not.toBe(first.digest);
  });

  it("matches the committed manifest of the repository", () => {
    const committed = readManifest(repoContent);
    if (committed === null) throw new Error("content/manifest.json is missing");
    const fresh = nextManifest(repoContent, committed);
    expect(serialise(fresh)).toBe(serialise(committed));
  });
});
