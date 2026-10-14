import { readFileSync } from "node:fs";
import { join } from "node:path";

import { readManifest } from "./manifest";
import { signManifest } from "./sign";

/**
 * Prints the signature of content/manifest.json for the box's HULLA_CONTENT_SIGNATURE. The private
 * key is a PEM file named by the first argument or HULLA_CONTENT_SIGNING_KEY_FILE.
 */
const keyFile = process.argv[2] ?? process.env["HULLA_CONTENT_SIGNING_KEY_FILE"] ?? "";
if (keyFile === "") {
  console.error("usage: pnpm content:sign <private-key.pem>");
  process.exit(2);
}
const manifest = readManifest(join(import.meta.dirname, "..", "..", "..", "content"));
if (manifest === null) {
  console.error("content/manifest.json is missing; run pnpm content:manifest");
  process.exit(1);
}
console.error(`content manifest version ${String(manifest.version)} signed`);
console.log(signManifest(manifest, readFileSync(keyFile, "utf8")));
