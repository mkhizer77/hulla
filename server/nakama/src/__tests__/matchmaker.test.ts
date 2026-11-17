import { describe, expect, it } from "vitest";

import { tableForMatch, type MatchedEntry } from "../hooks/matchmaker_matched";

import { fakeRuntime } from "./fake-nk";

function entry(
  userId: string,
  partyId: string | null = null,
  properties: Record<string, string> = {},
): MatchedEntry {
  return { userId, partyId, properties: { game: "thulla", mode: "quick", ...properties } };
}

describe("the matchmaker hook", () => {
  it("opens a public Thulla table for exactly the matched players, parties kept", () => {
    const created: { module: string; params: unknown }[] = [];
    const matchId = tableForMatch(
      {
        runtime: fakeRuntime(),
        matchCreate(module, params) {
          created.push({ module, params });
          return "m_quick";
        },
      },
      [entry("u1", "party-a"), entry("u2", "party-a"), entry("u3"), entry("u4", "party-solo")],
    );
    expect(matchId).toBe("m_quick");
    expect(created[0]?.module).toBe("hulla_table");
    expect(created[0]?.params).toMatchObject({
      gameId: "thulla",
      mode: "public",
      rules: { gameId: "thulla", preset: "standard" },
      autoStart: { expected: 4 },
      parties: [{ id: "party-a", members: ["u1", "u2"] }],
    });
    expect((created[0]?.params as { code: string }).code).toMatch(
      /^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{6}$/,
    );
  });

  it("opens a Rung table for Rung tickets and falls back to Thulla for a game it does not know", () => {
    const params: unknown[] = [];
    const deps = {
      runtime: fakeRuntime(),
      matchCreate: (_m: string, p: unknown) => (params.push(p), "m"),
    };
    tableForMatch(deps, [
      entry("u1", "party-a", { game: "rung" }),
      entry("u2", "party-a", { game: "rung" }),
      entry("u3", null, { game: "rung" }),
      entry("u4", null, { game: "rung" }),
    ]);
    expect(params[0]).toMatchObject({
      gameId: "rung",
      mode: "public",
      rules: { gameId: "rung", preset: "rung" },
      autoStart: { expected: 4 },
      parties: [{ id: "party-a", members: ["u1", "u2"] }],
    });
    tableForMatch(deps, [entry("u1", null, { game: "ludo" }), entry("u2", null, { game: "ludo" })]);
    expect(params[1]).toMatchObject({ gameId: "thulla", rules: { preset: "standard" } });
    // Ranked is Thulla's only: a Rung ticket marked ranked still opens a public Rung table.
    tableForMatch(deps, [entry("u1", null, { game: "rung", mode: "ranked" })]);
    expect(params[2]).toMatchObject({ gameId: "rung", mode: "public" });
  });
});
