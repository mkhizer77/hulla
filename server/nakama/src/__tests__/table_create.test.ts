import { describe, expect, it } from "vitest";

import { tableCreate, type TableCreateDeps } from "../rpc/table_create";

import { fakeRuntime } from "./fake-nk";

import type { CodeEntry } from "../services/table_registry";

function deps(
  userId: string | null = "u1",
): TableCreateDeps & { readonly created: { module: string; params: unknown }[] } {
  const created: { module: string; params: unknown }[] = [];
  const entries = new Map<string, CodeEntry>();
  return {
    runtime: fakeRuntime(),
    userId,
    region: "test",
    store: {
      read: (code) => entries.get(code) ?? null,
      create(code, entry) {
        if (entries.has(code)) return false;
        entries.set(code, entry);
        return true;
      },
      remove(code) {
        entries.delete(code);
      },
    },
    created,
    matchCreate(module, params) {
      created.push({ module, params });
      return `m_${String(created.length)}`;
    },
  };
}

describe("table_create", () => {
  it("creates a Thulla match with a room code and the validated rules", () => {
    const d = deps();
    const response = tableCreate(
      d,
      JSON.stringify({ mode: "public", rules: { gameId: "thulla", preset: "family" } }),
    );
    expect(response).toMatchObject({ ok: true, matchId: "m_1" });
    if (!response.ok) return;
    expect(response.code).toMatch(/^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{6}$/);
    expect(response.tableId).toBe(`t_${response.code}`);
    expect(d.created[0]).toEqual({
      module: "thulla",
      params: {
        code: response.code,
        gameId: "thulla",
        mode: "public",
        rules: { gameId: "thulla", preset: "family" },
      },
    });
  });

  it("defaults to a private Thulla table on an empty payload", () => {
    const d = deps();
    expect(tableCreate(d, "")).toMatchObject({ ok: true });
    expect(d.created[0]?.params).toMatchObject({
      gameId: "thulla",
      mode: "private",
      rules: { gameId: "thulla" },
    });
  });

  it("rejects callers without a session, unknown games, bad modes, bad rules and bad JSON", () => {
    expect(tableCreate(deps(null), "")).toMatchObject({
      ok: false,
      error: { code: "table.not_authorised" },
    });
    expect(tableCreate(deps(), "{")).toMatchObject({ error: { detail: { reason: "json" } } });
    expect(tableCreate(deps(), "[]")).toMatchObject({ error: { detail: { reason: "json" } } });
    expect(tableCreate(deps(), JSON.stringify({ gameId: "rung" }))).toMatchObject({
      error: { detail: { reason: "game_id" } },
    });
    expect(tableCreate(deps(), JSON.stringify({ mode: "casual" }))).toMatchObject({
      error: { detail: { reason: "mode" } },
    });
    expect(tableCreate(deps(), JSON.stringify({ rules: { gameId: "rung" } }))).toMatchObject({
      error: { detail: { reason: "rules" } },
    });
    expect(
      tableCreate(
        deps(),
        JSON.stringify({ rules: { gameId: "thulla", overrides: { deckCount: 9 } } }),
      ),
    ).toMatchObject({
      error: { code: "rules.invalid_rules" },
    });
  });
});
