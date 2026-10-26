import { describe, expect, it } from "vitest";

import {
  broadcasts,
  harness,
  opened,
  payloadOf,
  presence,
  seated,
  sentTo,
  started,
  types,
} from "./table-helpers";

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
    expect(kick.voiceRemovals).toEqual(["u1"]);
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

  it("shares a seat's mic in table_state, ignores a repeat and clears it on disconnect", () => {
    const h = harness();
    started(h);
    const batch = h.send("s1", "set_mic", { mic: true });
    expect(h.state.seats[1]?.mic).toBe(true);
    expect(types(batch)).toEqual(["table_state", "table_state", "table_state"]);
    const shown = payloadOf<TableStateFor<ToyView>>(sentTo(batch, "s0")[0]);
    expect(shown.seats[1]?.mic).toBe(true);
    expect(types(h.send("s1", "set_mic", { mic: true }, 2))).toEqual([]);
    h.core.leave(h.state, [presence(1)], h.now);
    expect(h.state.seats[1]?.mic).toBe(false);
  });

  it("refuses a mic from a spectator", () => {
    const h = harness();
    h.join(presence(0));
    h.join(presence(1), { asSpectator: "true" });
    expect(sentTo(h.send("s1", "set_mic", { mic: true }), "s1")[0]?.payload).toMatchObject({
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

describe("spectators are friends of the table (GDD 7.6, 7.7, 9.10)", () => {
  const friendsOf = (userId: string, seatedIds: readonly string[]): boolean =>
    userId === "u9" && seatedIds.includes("u0");

  it("refuses a stranger who wants to watch and admits a friend of a seated player", () => {
    const h = harness({ social: { mayWatch: friendsOf } });
    h.join(presence(0));
    h.join(presence(1));
    expect(h.core.joinAttempt(h.state, presence(8), { asSpectator: "true" }, h.now)).toEqual({
      accept: false,
      code: "social.not_friends",
    });
    const batch = h.join(presence(9), { asSpectator: "true" });
    const state = payloadOf<TableStateFor<ToyView>>(sentTo(batch, "s9")[0]);
    expect(state.spectators).toEqual([{ userId: "u9", displayName: "Player9", seatNext: false }]);
  });

  it("keeps the spectators toggle and the cap ahead of the friend check", () => {
    const h = harness({ settings: { spectators: false }, social: { mayWatch: friendsOf } });
    h.join(presence(0));
    expect(h.core.joinAttempt(h.state, presence(9), { asSpectator: "true" }, h.now)).toEqual({
      accept: false,
      code: "table.locked",
    });
  });

  it("lets the host seat a spectator in the lobby, and only the host", () => {
    const h = harness();
    h.join(presence(0));
    h.join(presence(1));
    h.join(presence(9), { asSpectator: "true" });
    expect(
      sentTo(h.send("s1", "seat_spectator", { userId: "u9" }), "s1")[0]?.payload,
    ).toMatchObject({
      code: "table.not_host",
    });
    expect(
      sentTo(h.send("s0", "seat_spectator", { userId: "nobody" }), "s0")[0]?.payload,
    ).toMatchObject({
      code: "table.invalid_payload",
    });
    const batch = h.send("s0", "seat_spectator", { userId: "u9" });
    expect(h.state.presences["s9"]?.seat).toBe(2);
    expect(h.state.seats[2]).toMatchObject({ playerId: "u9", sessionId: "s9", connected: true });
    expect(broadcasts(batch)[0]?.payload).toMatchObject({ seat: 2, status: "joined" });
    const state = payloadOf<TableStateFor<ToyView>>(sentTo(batch, "s9").at(-1));
    expect(state.spectators).toEqual([]);
    expect(state.resumeToken).not.toBeNull();
  });

  it("seats a late arrival and a host-picked spectator when the round ends, in arrival order", () => {
    const h = harness();
    opened(h);
    h.join(presence(7));
    h.join(presence(8), { asSpectator: "true" });
    expect(h.state.presences["s7"]?.seatNext).toBe(true);
    expect(h.state.presences["s8"]?.seatNext).toBeUndefined();
    h.send("s0", "seat_spectator", { userId: "u8" });
    expect(h.state.presences["s8"]?.seatNext).toBe(true);
    expect(h.state.seats.filter((s) => s.playerId !== null)).toHaveLength(3);
    const roundId = h.state.round?.roundId ?? "";
    let guard = 0;
    let last = h.tick();
    while (h.state.phase === "inRound" && guard < 50) {
      const seat = h.state.round?.state.turnSeat ?? 0;
      last = h.send(`s${String(seat)}`, "play_card", { roundId, clientSeq: 200 + guard, card: 2 });
      guard += 1;
    }
    expect(h.state.phase).toBe("roundEnded");
    expect(h.state.seats).toHaveLength(4);
    expect(h.state.seats[3]).toMatchObject({ playerId: "u7" });
    expect(h.state.presences["s7"]).toMatchObject({ seat: 3, seatNext: false });
    // No seat left for the second one: still watching, still first in line for the next round.
    expect(h.state.presences["s8"]).toMatchObject({ seat: null, seatNext: true });
    const joined = broadcasts(last).filter(
      (e) => e.type === "presence" && (e.payload as { status: string }).status === "joined",
    );
    expect(joined.map((e) => (e.payload as { seat: number }).seat)).toEqual([3]);
  });
});

describe("lobby: voice follows the table (ADR-0024)", () => {
  it("drops a player from voice when they leave, but not a seat that only lost its socket mid-round", () => {
    const h = harness();
    for (let i = 0; i < 3; i += 1) h.join(presence(i));
    expect(h.send("s2", "leave_table", {}).voiceRemovals).toEqual(["u2"]);
    expect(h.core.leave(h.state, [presence(1)], h.now).voiceRemovals).toEqual(["u1"]);
    const watcher = h.join(presence(3), { asSpectator: "true" });
    expect(watcher.voiceRemovals).toEqual([]);
    expect(h.core.leave(h.state, [presence(3)], h.now).voiceRemovals).toEqual(["u3"]);
  });
});
