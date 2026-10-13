import { createVerify, generateKeyPairSync } from "node:crypto";

import { describe, expect, it } from "vitest";

import { CONTENT_FILES, CONTENT_MANIFEST } from "../content";
import { canonicalManifest, rpcContentGet, rpcContentManifest } from "../rpc/content";

import { fakeLogger, memoryNk } from "./fake-nk";

type Json = Record<string, unknown>;
const parse = (s: string): Json => JSON.parse(s) as Json;

describe("the content manifest and files (ADR-0011)", () => {
  it("bundles every file the manifest lists, with matching ids and versions", () => {
    expect(CONTENT_MANIFEST.files.length).toBeGreaterThanOrEqual(10);
    for (const entry of CONTENT_MANIFEST.files) {
      const file = CONTENT_FILES[entry.path] as { id?: string; version?: number } | undefined;
      expect(file, entry.path).toBeDefined();
      expect(file?.id).toBe(entry.id);
      expect(file?.version).toBe(entry.version);
    }
    expect(Object.keys(CONTENT_FILES).sort()).toEqual(
      CONTENT_MANIFEST.files.map((f) => f.path).sort(),
    );
  });

  it("serves the manifest unsigned on a development box and signed when the key is set", () => {
    const m = memoryNk();
    const log = fakeLogger();
    const plain = parse(rpcContentManifest({ env: {} } as unknown as nkruntime.Context, log, m.nk));
    expect(plain).toMatchObject({
      ok: true,
      signature: null,
      algorithm: null,
      manifest: { id: "content-manifest" },
    });
    const { privateKey, publicKey } = generateKeyPairSync("rsa", { modulusLength: 2048 });
    const pem = privateKey.export({ type: "pkcs1", format: "pem" });
    const signed = parse(
      rpcContentManifest(
        {
          env: { HULLA_CONTENT_SIGNING_KEY: pem, HULLA_CONTENT_SIGNING_KEY_ID: "k1" },
        } as unknown as nkruntime.Context,
        log,
        m.nk,
      ),
    );
    expect(signed).toMatchObject({ ok: true, algorithm: "RSA-SHA256", keyId: "k1" });
    const verify = createVerify("RSA-SHA256").update(signed["signedBytes"] as string);
    expect(verify.verify(publicKey, signed["signature"] as string, "base64")).toBe(true);
    expect(signed["signedBytes"]).toBe(canonicalManifest());
    const tampered = createVerify("RSA-SHA256").update(`${canonicalManifest()} `);
    expect(tampered.verify(publicKey, signed["signature"] as string, "base64")).toBe(false);
  });

  it("serves a listed file with its hash and refuses an unknown path", () => {
    const m = memoryNk();
    const log = fakeLogger();
    const got = parse(
      rpcContentGet(
        { env: {} } as unknown as nkruntime.Context,
        log,
        m.nk,
        JSON.stringify({ path: "emotes/v1.json" }),
      ),
    );
    expect(got).toMatchObject({ ok: true, path: "emotes/v1.json", document: { id: "v1" } });
    expect(got["sha256"]).toMatch(/^[0-9a-f]{64}$/);
    expect(
      parse(
        rpcContentGet(
          { env: {} } as unknown as nkruntime.Context,
          log,
          m.nk,
          JSON.stringify({ path: "../secrets.json" }),
        ),
      ),
    ).toMatchObject({
      error: { code: "content.pack_unknown" },
    });
  });
});
