import { describe, expect, it } from "vitest";

import { broadcasts, harness, payloadOf, presence, seated, sentTo, types } from "./table-helpers";

import type { ToyView } from "./toy-game";
import type { TableStateFor } from "../match/view";

describe("lobby: joining and seats", () => {
  it("seats the first joiner as host, hands it a resume token and announces it", () => {
    const h = harness();
    const batch = h.join(presence(0));
    expect(h.state.hostSeat).toBe(0);
    expect(h.state.seats[0]).toMatchObject({
      playerId: "u0",
      displayName: "Player0",
      connected: true,
    });
    const state = payloadOf<TableStateFor<ToyView>>(sentTo(batch, "s0")[0]);
    expect(state.status).toBe("open");
    expect(state.resumeToken).toHaveLength(64);
    expect(state.seats).toHaveLength(4);
    expect(broadcasts(batch).map((e) => e.type)).toEqual(["presence"]);
    expect(payloadOf(broadcasts(batch)[0])).toEqual({
      seat: 0,
      playerId: "u0",
      displayName: "Player0",
      status: "joined",
    });
  });

  it("fills the lowest free seat, then seats spectators and rejects a full table", () => {
    const h = harness({ config: { maxSpectators: 1 } });
    for (let i = 0; i < 4; i += 1) h.join(presence(i));
    expect(h.core.joinAttempt(h.state, presence(4), {}, h.now)).toEqual({ accept: true });
    expect(h.state.admissions["s4"]).toEqual({ kind: "spectator" });
    h.core.join(h.state, [presence(4)], h.now);
    expect(h.core.joinAttempt(h.state, presence(5), {}, h.now)).toEqual({
      accept: false,
      code: "table.full",
    });
  });

  it("seats a player who asked to spectate as a spectator and shows them no secret", () => {
    const h = harness();
    h.join(presence(0));
    const batch = h.join(presence(1), { asSpectator: "true" });
    expect(h.state.presences["s1"]?.seat).toBeNull();
    const state = payloadOf<TableStateFor<ToyView>>(sentTo(batch, "s1")[0]);
    expect(state.spectatorCount).toBe(1);
    expect(state.resumeToken).toBeNull();
    expect(broadcasts(batch)[0]?.payload).toMatchObject({ seat: null, status: "spectating" });
  });

  it("refuses a kicked player and a closed table", () => {
    const h = harness();
    h.join(presence(0));
    h.join(presence(1));
    const kick = h.send("s0", "kick", { seat: 1 });
    expect(kick.kicks).toEqual(["s1"]);
    expect(broadcasts(kick)[0]?.payload).toMatchObject({
      seat: 1,
      playerId: "u1",
      status: "kicked",
    });
    expect(h.state.seats[1]?.playerId).toBeNull();
    expect(h.core.joinAttempt(h.state, presence(1), {}, h.now)).toEqual({
      accept: false,
      code: "table.banned",
    });
    h.core.close(h.state, "host_closed", h.now);
    expect(h.core.joinAttempt(h.state, presence(2), {}, h.now)).toEqual({
      accept: false,
      code: "table.not_found",
    });
  });

  it("only the host may kick, and never itself or an empty seat", () => {
    const h = harness();
    seated(h, 3);
    expect(sentTo(h.send("s1", "kick", { seat: 2 }), "s1")[0]?.payload).toMatchObject({
      code: "table.not_host",
    });
    expect(sentTo(h.send("s0", "kick", { seat: 0 }), "s0")[0]?.payload).toMatchObject({
      code: "table.invalid_payload",
      detail: { reason: "seat" },
    });
    expect(sentTo(h.send("s0", "kick", { seat: 3 }), "s0")[0]?.payload).toMatchObject({
      detail: { reason: "seat" },
    });
  });
});

describe("lobby: ready, rules and host", () => {
  it("broadcasts a table_state to everyone when a seat toggles ready", () => {
    const h = harness();
    h.join(presence(0));
    h.join(presence(1));
    const batch = h.send("s1", "ready", { ready: true });
    expect(h.state.seats[1]?.ready).toBe(true);
    expect(types(batch)).toEqual(["table_state", "table_state"]);
    expect(sentTo(batch, "s0")).toHaveLength(1);
  });

  it("rejects ready from a spectator and outside the lobby", () => {
    const h = harness();
    h.join(presence(0));
    h.join(presence(1), { asSpectator: "true" });
    expect(sentTo(h.send("s1", "ready", { ready: true }), "s1")[0]?.payload).toMatchObject({
      code: "table.not_authorised",
    });
  });

  it("lets only the host change rules, validates them and reports the changed keys", () => {
    const h = harness();
    seated(h, 3);
    const rules = { gameId: "thulla", preset: "family", overrides: { target: 4 } };
    expect(sentTo(h.send("s1", "set_rules", { rules }), "s1")[0]?.payload).toMatchObject({
      code: "table.not_host",
    });
    const bad = h.send("s0", "set_rules", {
      rules: { gameId: "thulla", overrides: { target: -1 } },
    });
    expect(sentTo(bad, "s0")[0]?.payload).toMatchObject({ code: "rules.invalid_rules" });
    const wrongGame = h.send("s0", "set_rules", { rules: { gameId: "rung" } });
    expect(sentTo(wrongGame, "s0")[0]?.payload).toMatchObject({ detail: { reason: "game_id" } });
    const ok = h.send("s0", "set_rules", { rules });
    expect(broadcasts(ok)[0]).toMatchObject({
      type: "rules_changed",
      payload: { rules, changedBy: 0, changedKeys: ["overrides.target", "preset"] },
    });
    expect(h.state.rules).toEqual(rules);
    expect(types(ok).filter((t) => t === "table_state")).toHaveLength(3);
  });

  it("transfers the host and migrates it to the longest-seated player when the host leaves", () => {
    const h = harness();
    h.join(presence(0));
    h.now += 10;
    h.join(presence(1));
    h.now += 10;
    h.join(presence(2));
    const refused = h.send("s0", "transfer_host", { seat: 3 });
    expect(sentTo(refused, "s0")[0]?.payload).toMatchObject({ detail: { reason: "seat" } });
    const transfer = h.send("s0", "transfer_host", { seat: 2 });
    expect(h.state.hostSeat).toBe(2);
    expect(broadcasts(transfer)[0]).toMatchObject({
      type: "host_changed",
      payload: { seat: 2, reason: "transfer" },
    });
    const left = h.core.leave(h.state, [presence(2)], h.now);
    expect(h.state.hostSeat).toBe(0);
    expect(h.state.seats[2]?.playerId).toBeNull();
    expect(broadcasts(left).map((e) => e.type)).toEqual(["presence", "host_changed"]);
    expect(broadcasts(left)[1]?.payload).toEqual({ seat: 0, reason: "host_left" });
  });

  it("closes an empty table after its mode's idle time", () => {
    const h = harness({ mode: "public" });
    h.join(presence(0));
    h.core.leave(h.state, [presence(0)], h.now);
    expect(types(h.tick(59_000))).toEqual([]);
    const closed = h.tick(1_000);
    expect(broadcasts(closed)[0]).toMatchObject({
      type: "table_closed",
      payload: { reason: "empty" },
    });
    expect(closed.end).toBe(true);
    expect(h.state.phase).toBe("closing");
    expect(types(h.tick(1))).toEqual([]);
  });
});
