import { afterAll, describe, expect, it } from "vitest";

import { NAKAMA_HOST, rpc, scriptedClient, type ScriptedClient } from "./nakama-client";

import type { Profile } from "@hulla/protocol/generated";

interface ProfileResponse {
  readonly ok: boolean;
  readonly profile?: Profile;
  readonly error?: { code: string };
}

describe.skipIf(NAKAMA_HOST === undefined)("profiles against Nakama (GDD 10.1)", () => {
  const clients: ScriptedClient[] = [];
  afterAll(() => {
    for (const c of clients) c.close();
  });

  it("bootstraps on first sign-in, edits with a unique handle, and respects friends-only visibility", async () => {
    const suffix = String(Date.now());
    const ali = await scriptedClient("ali", suffix);
    const sara = await scriptedClient("sara", suffix);
    clients.push(ali, sara);

    const own = await rpc<ProfileResponse>(ali, "profile_get", {});
    expect(own.ok, JSON.stringify(own)).toBe(true);
    expect(own.profile?.friendCode).toMatch(/^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$/);
    expect(own.profile?.displayName).toBe(`Guest ${(own.profile?.friendCode ?? "").slice(0, 4)}`);
    expect(own.profile?.stats.rounds).toBe(0);

    const handle = `ali_${suffix.slice(-6)}`;
    const edited = await rpc<ProfileResponse>(ali, "profile_update", {
      displayName: "علی خان",
      handle,
      avatar: "a03",
      flag: "PK",
      bio: "chai first",
    });
    expect(edited.ok, JSON.stringify(edited)).toBe(true);
    expect(edited.profile).toMatchObject({
      displayName: "علی خان",
      handle,
      avatar: "a03",
      flag: "PK",
      bio: "chai first",
      // Leaving the generated guest name is not a rename (GDD 9.4).
      nameChanges: 0,
    });
    const taken = await rpc<ProfileResponse>(sara, "profile_update", { handle });
    expect(taken).toMatchObject({ ok: false, error: { code: "profile.handle_taken" } });
    const renamed = await rpc<ProfileResponse>(ali, "profile_update", { displayName: "Ali" });
    expect(renamed).toMatchObject({ ok: true, profile: { displayName: "Ali", nameChanges: 1 } });
    const locked = await rpc<ProfileResponse>(ali, "profile_update", { displayName: "Ali K" });
    expect(locked).toMatchObject({ ok: false, error: { code: "profile.name_change_locked" } });

    const seen = await rpc<ProfileResponse>(sara, "profile_get", { handle });
    expect(seen.ok, JSON.stringify(seen)).toBe(true);
    expect(seen.profile?.displayName).toBe("Ali");
    expect(seen.profile?.friendCode).toBeUndefined();
    await rpc<ProfileResponse>(ali, "profile_update", {
      privacy: { showOnline: false, visibility: "friends" },
    });
    expect(
      await rpc<ProfileResponse>(sara, "profile_get", { userId: ali.session.user_id }),
    ).toMatchObject({ ok: false, error: { code: "profile.not_visible" } });
    expect((await rpc<ProfileResponse>(ali, "profile_get", {})).profile?.privacy).toEqual({
      showOnline: false,
      visibility: "friends",
    });
  }, 60_000);
});
