import { writeFileSync } from "node:fs";
import { join } from "node:path";

import { MANIFEST_FILE, nextManifest, readManifest, serialise } from "./manifest";

const contentDir = join(import.meta.dirname, "..", "..", "..", "content");
const previous = readManifest(contentDir);
const manifest = nextManifest(contentDir, previous);
writeFileSync(join(contentDir, MANIFEST_FILE), serialise(manifest));
console.log(
  `content manifest: version ${String(manifest.version)}, ${String(manifest.files.length)} files, digest ${manifest.digest.slice(0, 12)}`,
);
