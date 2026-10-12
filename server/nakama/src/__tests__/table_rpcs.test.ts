import { describe, expect, it } from "vitest";

import { presetsGet } from "../rpc/presets_get";
import { DEFAULT_CODE_TTL_SECONDS, tableCreate, type TableCreateDeps } from "../rpc/table_create";
import { tableJoinByCode } from "../rpc/table_join_by_code";
import { agoraConfigFrom, voiceToken, VOICE_TOKEN_SECONDS } from "../rpc/voice_token";
import { lookupCode, type CodeEntry, type CodeStore } from "../services/table_registry";

import { fakeRuntime } from "./fake-nk";

function memoryStore(): CodeStore & { readonly entries: Map<string, CodeEntry> } {
  const entries = new Map<string, CodeEntry>();
  return {
    entries,
    read: (code) => entries.get(code) ?? null,
    create(code, entry) {
      if (entries.has(code)) return false;
      entries.set(code, entry);
      return true;
    },
    remove(code) {
      entries.delete(code);
    },
  };
}

const T0 = 1_760_200_000_000;

function createDeps(store = memoryStore()): TableCreateDeps & { readonly matches: Set<string> } {
  const matches = new Set<string>();
  return {
    runtime: fakeRuntime({ now: T0 }),
    userId: "u1",
    region: "eu-test",
    store,
    matches,
    matchCreate(module) {
      const id = `m_${module}_${String(matches.size + 1)}`;
      matches.add(id);
      return id;
    },
  };
}

describe("table_create and the code registry", () => {
  it("registers the code with region and expiry and the join RPC resolves it", () => {
    const store = memoryStore();
    const deps = createDeps(store);
    const created = tableCreate(deps, JSON.stringify({ mode: "public" }));
    expect(created.ok).toBe(true);
    if (!created.ok) return;
    expect(store.entries.get(created.code)).toMatchObject({
      matchId: created.matchId,
      region: "eu-test",
      mode: "public",
      expiresTs: T0 + DEFAULT_CODE_TTL_SECONDS * 1000,
    });
    const joined = tableJoinByCode(
      { store, userId: "u2", now: T0 + 1000, matchExists: (id) => deps.matches.has(id) },
      JSON.stringify({ code: created.code.toLowerCase() }),
    );
    expect(joined).toEqual({
      ok: true,
      matchId: created.matchId,
      code: created.code,
      tableId: created.tableId,
      region: "eu-test",
    });
  });

  it("rejects bad, unknown, expired and dead codes with catalogue errors", () => {
    const store = memoryStore();
    const deps = createDeps(store);
    const created = tableCreate(deps, JSON.stringify({ codeTtlSeconds: 1 }));
    if (!created.ok) throw new Error(created.error.code);
    const join = (code: string, now: number, alive = true) =>
      tableJoinByCode(
        { store, userId: "u2", now, matchExists: () => alive },
        JSON.stringify({ code }),
      );
    expect(join("ab", T0)).toMatchObject({ error: { code: "table.invalid_code" } });
    expect(join("ZZZZZZ", T0)).toMatchObject({ error: { code: "table.not_found" } });
    expect(join(created.code, T0 + 500)).toMatchObject({ ok: true });
    expect(join(created.code, T0 + 1000)).toMatchObject({
      error: { code: "table.invalid_code", detail: { reason: "expired" } },
    });
    expect(store.entries.has(created.code)).toBe(false);
    const again = tableCreate(deps, "");
    if (!again.ok) throw new Error(again.error.code);
    expect(join(again.code, T0, false)).toMatchObject({
      error: { code: "table.not_found", detail: { reason: "closed" } },
    });
    expect(
      tableJoinByCode({ store, userId: null, now: T0, matchExists: () => true }, "{}"),
    ).toMatchObject({ error: { code: "table.not_authorised" } });
    expect(
      tableJoinByCode({ store, userId: "u", now: T0, matchExists: () => true }, "{"),
    ).toMatchObject({ error: { detail: { reason: "json" } } });
    expect(
      tableJoinByCode({ store, userId: "u", now: T0, matchExists: () => true }, "{}"),
    ).toMatchObject({ error: { detail: { reason: "code" } } });
  });

  it("skips a live code that is already taken and reuses an expired one", () => {
    const store = memoryStore();
    const deps = createDeps(store);
    const first = tableCreate(deps, "");
    if (!first.ok) throw new Error(first.error.code);
    const other = tableCreate(createDeps(store), "");
    if (!other.ok) throw new Error(other.error.code);
    expect(other.code).not.toBe(first.code);
    const expired = lookupCode(
      store,
      first.code,
      T0 + DEFAULT_CODE_TTL_SECONDS * 1000 + 1,
      () => true,
    );
    expect(expired).toMatchObject({ ok: false });
  });
});

describe("presets_get", () => {
  it("returns the four Thulla presets from content in order", () => {
    const response = presetsGet("");
    expect(response.ok).toBe(true);
    if (!response.ok) return;
    expect(response.presets.map((p) => p.id)).toEqual([
      "standard",
      "family",
      "hardcore",
      "classicPagat",
    ]);
    expect(response.presets[1]).toMatchObject({
      overrides: { handTaking: "leaderOnly" },
      settings: { turnTimerSeconds: 30, loserTitle: "donkey", cardMemoryAssist: true },
      name: "preset.family.name",
    });
    expect(presetsGet(JSON.stringify({ gameId: "thulla" }))).toMatchObject({ ok: true });
    expect(presetsGet(JSON.stringify({ gameId: "rung" }))).toMatchObject({
      error: { detail: { reason: "game_id" } },
    });
    expect(presetsGet("{")).toMatchObject({ error: { detail: { reason: "json" } } });
  });
});

describe("voice_token", () => {
  const config = {
    appId: "970CA35de60c44645bbae8a215061b33",
    appCertificate: "5CFd2fd1755d40ecb72977518be15d3b",
  };
  const at = (present: boolean, seat: number | null) => () => ({
    present,
    seat,
    tableId: "t_ABCDEF",
  });

  it("issues an hour-long channel token to a presence at the table", () => {
    const response = voiceToken(
      { runtime: fakeRuntime({ now: T0 }), userId: "u1", config, membership: at(true, 2) },
      JSON.stringify({ matchId: "m1" }),
    );
    expect(response).toMatchObject({
      ok: true,
      provider: "agora",
      channel: "t_ABCDEF",
      uid: 0,
      expiresTs: T0 + VOICE_TOKEN_SECONDS * 1000,
    });
    if (response.ok) expect(response.token.startsWith("007")).toBe(true);
  });

  it("refuses without credentials, away from the table, without a session or a match id", () => {
    const deps = { runtime: fakeRuntime(), userId: "u1", config, membership: at(true, null) };
    expect(voiceToken({ ...deps, config: null }, JSON.stringify({ matchId: "m1" }))).toMatchObject({
      error: { code: "voice.unavailable" },
    });
    expect(
      voiceToken({ ...deps, membership: at(false, null) }, JSON.stringify({ matchId: "m1" })),
    ).toMatchObject({ error: { code: "voice.not_at_table" } });
    expect(
      voiceToken({ ...deps, membership: () => null }, JSON.stringify({ matchId: "m1" })),
    ).toMatchObject({ error: { code: "voice.not_at_table" } });
    expect(voiceToken({ ...deps, userId: null }, "{}")).toMatchObject({
      error: { code: "table.not_authorised" },
    });
    expect(voiceToken(deps, "{}")).toMatchObject({ error: { detail: { reason: "match_id" } } });
    expect(voiceToken(deps, "{")).toMatchObject({ error: { detail: { reason: "json" } } });
    expect(agoraConfigFrom({})).toBeNull();
    expect(
      agoraConfigFrom({ AGORA_APP_ID: config.appId, AGORA_APP_CERTIFICATE: "short" }),
    ).toBeNull();
    expect(
      agoraConfigFrom({ AGORA_APP_ID: config.appId, AGORA_APP_CERTIFICATE: config.appCertificate }),
    ).toEqual(config);
  });
});
