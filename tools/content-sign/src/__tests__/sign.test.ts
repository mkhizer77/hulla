import { createVerify, generateKeyPairSync } from "node:crypto";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import { readManifest } from "../manifest";
import { signManifest, signedBytes } from "../sign";

const repoContent = join(import.meta.dirname, "..", "..", "..", "..", "content");

describe("signing the content manifest (ADR-0011)", () => {
  it("signs the manifest without its schema pointer, keys in order", () => {
    const manifest = readManifest(repoContent);
    expect(manifest).not.toBeNull();
    if (manifest === null) return;
    const bytes = signedBytes(manifest);
    expect(Object.keys(JSON.parse(bytes) as object)).toEqual(["id", "version", "digest", "files"]);
    expect(bytes).not.toContain("schema");
  });

  it("produces a base64 RSA-SHA256 signature the public key verifies, and nothing else does", () => {
    const manifest = readManifest(repoContent);
    if (manifest === null) throw new Error("manifest missing");
    const { privateKey, publicKey } = generateKeyPairSync("rsa", { modulusLength: 2048 });
    const signature = signManifest(manifest, privateKey.export({ type: "pkcs1", format: "pem" }));
    expect(Buffer.from(signature, "base64")).toHaveLength(256);
    const ok = createVerify("RSA-SHA256").update(signedBytes(manifest));
    expect(ok.verify(publicKey, signature, "base64")).toBe(true);
    const bumped = { ...manifest, version: manifest.version + 1 };
    const tampered = createVerify("RSA-SHA256").update(signedBytes(bumped));
    expect(tampered.verify(publicKey, signature, "base64")).toBe(false);
  });
});
