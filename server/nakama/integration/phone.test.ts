import { Client } from "@heroiclabs/nakama-js";
import { afterAll, describe, expect, it } from "vitest";

import {
  NAKAMA_HOST,
  NAKAMA_PORT,
  SERVER_KEY,
  rpc,
  scriptedClient,
  type ScriptedClient,
} from "./nakama-client";

interface Verified {
  readonly ok: boolean;
  readonly result?: "linked" | "signIn";
  readonly token?: string;
  readonly error?: { readonly code: string };
}

describe.skipIf(NAKAMA_HOST === undefined)("phone sign-in against Nakama (GDD 10.2, #99)", () => {
  const clients: ScriptedClient[] = [];
  afterAll(() => {
    for (const c of clients) c.close();
  });

  it("a number links on one device and signs in to that account on another", async () => {
    const suffix = `${String(Date.now())}p`;
    // The local provider's code is the number's last six digits.
    const phone = `+92300${suffix.slice(-8, -1)}`;
    const code = phone.slice(-6);
    const first = await scriptedClient("ph1", suffix);
    const second = await scriptedClient("ph2", suffix);
    clients.push(first, second);

    expect(await rpc<{ ok: boolean }>(first, "phone_start", { phone })).toMatchObject({ ok: true });
    const wrong = await rpc<Verified>(first, "phone_verify", {
      phone,
      code: "000000" === code ? "111111" : "000000",
    });
    expect(wrong.error?.code).toBe("auth.otp_wrong_code");
    expect(await rpc<Verified>(first, "phone_verify", { phone, code })).toMatchObject({
      ok: true,
      result: "linked",
    });
    expect(await rpc<{ linked: boolean }>(first, "phone_status", {})).toMatchObject({
      linked: true,
    });

    // A second device waits out the resend gap, then gets a sign-in token for the first account.
    await new Promise((resolve) => setTimeout(resolve, 61_000));
    expect(await rpc<{ ok: boolean }>(second, "phone_start", { phone })).toMatchObject({
      ok: true,
    });
    const signIn = await rpc<Verified>(second, "phone_verify", { phone, code });
    expect(signIn).toMatchObject({ ok: true, result: "signIn" });
    // The guest on the second device hands over its progress, then signs in with the token.
    const merge = await rpc<{ ok: boolean; code: string }>(second, "account_merge_prepare", {});
    expect(merge.ok).toBe(true);
    const client = new Client(SERVER_KEY, NAKAMA_HOST ?? "", NAKAMA_PORT, false);
    const session = await client.authenticateCustom(signIn.token ?? "", false);
    expect(session.user_id).toBe(first.session.user_id);
    const merged = await client.rpc(session, "account_merge", { code: merge.code });
    expect(merged.payload).toMatchObject({ ok: true, mergedFrom: second.session.user_id });
    // The second phone's device id now opens the first account at every start.
    const again = await client.authenticateDevice(`hulla-it-ph2-${suffix}`, false);
    expect(again.user_id).toBe(first.session.user_id);
    // The token works once; a bare phone id never does.
    await expect(client.authenticateCustom(signIn.token ?? "", false)).rejects.toBeTruthy();
    await expect(client.authenticateCustom(`ph_${"0".repeat(64)}`, false)).rejects.toBeTruthy();

    expect(await rpc<{ linked: boolean }>(first, "phone_status", { remove: true })).toMatchObject({
      linked: false,
    });
  }, 120_000);
});
