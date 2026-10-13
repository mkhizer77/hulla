import { hexToBytes, stateFromSeed, stateHash, Xoshiro128StarStar } from "@hulla/shared-ts";
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
  type Harness,
} from "./table-helpers";
import { toyModule, type ToyMove, type ToyState, type ToyView } from "./toy-game";

import type { Batch } from "../match/types";
import type { TableStateFor } from "../match/view";

const roundIdOf = (h: ReturnType<typeof harness>): string => h.state.round?.roundId ?? "";

const roundState = (h: Harness): ToyState => {
  if (h.state.round === null) throw new Error("no round");
  return h.state.round.state;
};

describe("starting a round", () => {
  it("needs the host, enough seats and every seat ready", () => {
    const h = harness();
    h.join(presence(0));
    h.join(presence(1));
    expect(sentTo(h.send("s1", "start_round", {}), "s1")[0]?.payload).toMatchObject({
      code: "table.not_host",
    });
    expect(sentTo(h.send("s0", "start_round", {}), "s0")[0]?.payload).toMatchObject({
      code: "rules.invalid_seat_count",
      detail: { seats: 2, min: 3 },
    });
    h.join(presence(2));
    expect(sentTo(h.send("s0", "start_round", {}), "s0")[0]?.payload).toMatchObject({
      code: "table.not_authorised",
      detail: { reason: "seats_not_ready" },
    });
  });

  it("deals, sends each seat its own view, announces the round and opens a simultaneous window", () => {
    const h = harness();
    const batch = started(h, 3);
    expect(h.state.phase).toBe("inRound");
    expect(h.state.round?.roundId).toBe("r_1");
    expect(h.state.round?.seatCount).toBe(3);
    const views = ["s0", "s1", "s2"].map((s) =>
      payloadOf<TableStateFor<ToyView>>(sentTo(batch, s)[0]),
    );
    expect(views.map((v) => v.status)).toEqual(["inRound", "inRound", "inRound"]);
    expect(views.map((v) => v.round?.viewer)).toEqual([0, 1, 2]);
    expect(views.map((v) => v.round?.secret)).toEqual(h.state.round?.state.secrets);
    expect(broadcasts(batch).map((e) => e.type)).toEqual(["round_start"]);
    const turns = batch.outbound.filter((o) => o.envelope.type === "turn_start");
    expect(turns).toHaveLength(3);
    expect(turns[0]?.envelope.payload).toMatchObject({ seat: 0, simultaneous: true, legal: [1] });
    expect(h.state.round?.deadline).toMatchObject({ kind: "simultaneousWindow", seats: [0, 1, 2] });
  });

  it("compacts the seats so a round is dealt to 0..n-1 (R3, R4)", () => {
    const h = harness();
    for (let i = 0; i < 4; i += 1) h.join(presence(i));
    h.core.leave(h.state, [presence(1)], h.now);
    for (const s of ["s0", "s2", "s3"]) h.send(s, "ready", { ready: true });
    h.send("s0", "start_round", {});
    expect(h.state.seats.map((s) => s.playerId)).toEqual(["u0", "u2", "u3", null]);
    expect(h.state.presences["s3"]?.seat).toBe(2);
    expect(h.state.hostSeat).toBe(0);
  });
});

describe("the move pipeline", () => {
  it("rejects malformed envelopes with catalogue codes", () => {
    const h = harness();
    opened(h);
    const roundId = roundIdOf(h);
    const bad = h.core.inbound(h.state, "s0", "{not json", h.now);
    expect(sentTo(bad, "s0")[0]?.payload).toEqual({
      code: "table.invalid_payload",
      detail: { reason: "json" },
      refSeq: 0,
    });
    const envelope = { v: 2, type: "ping", tableId: h.state.tableId, seq: 7, ts: 1, payload: {} };
    const version = h.core.inbound(h.state, "s0", JSON.stringify(envelope), h.now);
    expect(sentTo(version, "s0")[0]?.payload).toMatchObject({ code: "table.invalid_payload" });
    const foreign = h.core.inbound(
      h.state,
      "s0",
      JSON.stringify({ ...envelope, v: 1, tableId: "t_ZZZZZZ" }),
      h.now,
    );
    expect(sentTo(foreign, "s0")[0]?.payload).toMatchObject({
      detail: { reason: "table_id" },
      refSeq: 7,
    });
    const opcode = h.core.inbound(
      h.state,
      "s0",
      JSON.stringify({ ...envelope, v: 1 }),
      h.now,
      "ready",
    );
    expect(sentTo(opcode, "s0")[0]?.payload).toMatchObject({ detail: { reason: "opcode" } });
    expect(sentTo(h.send("s0", "chat_text", { text: "hi" }), "s0")[0]?.payload).toMatchObject({
      detail: { reason: "unsupported_type", type: "chat_text" },
    });
    expect(sentTo(h.send("s0", "ready", { ready: "yes" }), "s0")[0]?.payload).toMatchObject({
      detail: { reason: "schema", type: "ready" },
    });
    expect(h.core.inbound(h.state, "nobody", "{}", h.now).outbound).toEqual([]);
    expect(sentTo(h.send("s0", "play_card", { roundId, card: 1 }), "s0")[0]?.payload).toMatchObject(
      {
        detail: { reason: "client_seq" },
      },
    );
  });

  it("applies a legal move, logs it with a hash and acknowledges it publicly", () => {
    const h = harness();
    opened(h);
    const roundId = roundIdOf(h);
    const before = h.state.seq;
    const batch = h.send("s0", "play_card", { roundId, clientSeq: 2, card: 2 });
    const applied = broadcasts(batch)[0];
    expect(applied).toMatchObject({
      type: "move_applied",
      seq: before + 1,
      payload: { roundId, seat: 0, move: { n: 2 }, total: 5, clientSeq: 2, auto: false },
    });
    expect(h.state.round?.state.total).toBe(5);
    expect(h.state.round?.moves.at(-1)).toMatchObject({ seat: 0, clientSeq: 2, auto: false });
    const logged = h.state.log.find((m) => m.seq === before + 1);
    expect(logged?.stateHash).toBe(stateHash(roundState(h)));
    const peek = batch.outbound.find((o) => o.envelope.type === "private_draw");
    expect(peek?.to).toEqual({ kind: "session", sessionId: "s1" });
    expect(peek?.envelope.seq).toBe(before + 1);
    expect(batch.outbound.find((o) => o.envelope.type === "turn_start")?.to).toEqual({
      kind: "session",
      sessionId: "s1",
    });
  });

  it("returns move_rejected with the engine's code and leaves the state untouched (R47)", () => {
    const h = harness();
    opened(h);
    const roundId = roundIdOf(h);
    const hashBefore = stateHash(roundState(h));
    const batch = h.send("s0", "play_card", { roundId, clientSeq: 2, card: 5 });
    expect(sentTo(batch, "s0")[0]).toMatchObject({
      type: "move_rejected",
      payload: { roundId, clientSeq: 2, error: { code: "rules.illegal_move", detail: { n: 5 } } },
    });
    expect(broadcasts(batch)).toEqual([]);
    expect(stateHash(roundState(h))).toBe(hashBefore);
    const wrongTurn = h.send("s1", "play_card", { roundId, clientSeq: 2, card: 1 });
    expect(sentTo(wrongTurn, "s1")[0]?.payload).toMatchObject({
      error: { code: "rules.illegal_move" },
    });
  });

  it("re-sends the original answer for a repeated (seat, clientSeq) and applies nothing", () => {
    const h = harness();
    opened(h);
    const roundId = roundIdOf(h);
    h.send("s0", "play_card", { roundId, clientSeq: 2, card: 2 });
    const seq = h.state.seq;
    const again = h.send("s0", "play_card", { roundId, clientSeq: 2, card: 1 });
    expect(h.state.seq).toBe(seq);
    expect(h.state.round?.state.total).toBe(5);
    expect(broadcasts(again)).toEqual([]);
    expect(sentTo(again, "s0")[0]).toMatchObject({
      type: "move_applied",
      payload: { clientSeq: 2, move: { n: 2 } },
    });
    h.send("s1", "play_card", { roundId, clientSeq: 9, card: 7 });
    const rejectedAgain = h.send("s1", "play_card", { roundId, clientSeq: 9, card: 1 });
    expect(sentTo(rejectedAgain, "s1")[0]).toMatchObject({
      type: "move_rejected",
      payload: { clientSeq: 9 },
    });
    expect(h.state.round?.state.total).toBe(5);
  });

  it("rejects a move for another round, from a spectator, or when no round runs", () => {
    const h = harness();
    expect(sentTo(h.join(presence(9), { asSpectator: "true" }), "s9")).toHaveLength(1);
    opened(h);
    const stale = h.send("s0", "play_card", { roundId: "r_0", clientSeq: 3, card: 1 });
    expect(sentTo(stale, "s0")[0]).toMatchObject({
      type: "move_rejected",
      payload: { roundId: "r_1", clientSeq: 3, error: { code: "table.stale_sequence" } },
    });
    const spectator = h.send("s9", "play_card", { roundId: "r_1", clientSeq: 1, card: 1 });
    expect(sentTo(spectator, "s9")[0]?.payload).toMatchObject({ code: "table.not_authorised" });
    const lobby = harness();
    seated(lobby, 3);
    expect(
      sentTo(lobby.send("s0", "play_card", { roundId: "r_1", clientSeq: 1, card: 1 }), "s0")[0]
        ?.payload,
    ).toMatchObject({
      code: "table.not_in_round",
    });
  });
});

describe("deadlines and auto-play", () => {
  it("times a seat out at the human deadline, after the presentation delay (R43, R44)", () => {
    const h = harness({ presentationDelayMs: 1_200 });
    opened(h);
    const turn = h.state.round?.deadline;
    expect(turn).toMatchObject({
      kind: "perSeatTimer",
      seats: [0],
      humanTs: h.now + 1_200 + 20_000,
    });
    expect(types(h.tick(21_000))).toEqual([]);
    const fired = h.tick(200);
    expect(broadcasts(fired)[0]).toMatchObject({
      type: "move_applied",
      payload: { seat: 0, auto: true },
    });
    expect(h.state.seats[0]?.timeouts).toBe(1);
    expect(h.state.round?.deadline?.seats).toEqual([1]);
  });

  it("hands a seat to auto-play after three timeouts and gives it back when the player acts (R45)", () => {
    const h = harness({ config: { autoPlayDelayMs: 100 }, target: 100 });
    opened(h);
    const roundId = roundIdOf(h);
    for (let i = 0; i < 2; i += 1) {
      h.tick(20_000);
      for (const seat of [1, 2]) {
        const current = h.state.round?.state.turnSeat;
        if (current === seat)
          h.send(`s${String(seat)}`, "play_card", { roundId, clientSeq: 10 + i, card: 1 });
      }
    }
    expect(h.state.seats[0]?.timeouts).toBe(2);
    const third = h.tick(20_000);
    expect(h.state.seats[0]?.autoPlay).toBe(true);
    expect(broadcasts(third).map((e) => e.type)).toContain("presence");
    expect(broadcasts(third).find((e) => e.type === "presence")?.payload).toMatchObject({
      seat: 0,
      status: "auto_play",
    });
    h.send("s1", "play_card", { roundId, clientSeq: 20, card: 1 });
    h.send("s2", "play_card", { roundId, clientSeq: 20, card: 1 });
    expect(h.state.round?.state.turnSeat).toBe(0);
    const quick = h.tick(100);
    expect(broadcasts(quick)[0]).toMatchObject({
      type: "move_applied",
      payload: { seat: 0, auto: true },
    });
    const back = h.send("s0", "ping", { sentTs: 1 });
    expect(h.state.seats[0]?.autoPlay).toBe(true);
    expect(types(back)).toEqual(["pong"]);
    const acted = h.send("s0", "request_snapshot", { sinceSeq: 0 });
    expect(types(acted)).toEqual(["snapshot"]);
    const vote = h.send("s0", "play_card", { roundId, clientSeq: 30, card: 1 });
    expect(h.state.seats[0]?.autoPlay).toBe(false);
    expect(broadcasts(vote)[0]).toMatchObject({
      type: "presence",
      payload: { seat: 0, status: "reconnected" },
    });
  });

  it("with the timer off a connected seat never times out but an absent seat acts after 3 s (OQ-003)", () => {
    const h = harness({ settings: { turnTimerSeconds: null } });
    opened(h);
    expect(h.state.round?.deadline?.humanTs).toBeNull();
    expect(types(h.tick(600_000))).toEqual([]);
    const gone = h.core.leave(h.state, [presence(0)], h.now);
    expect(broadcasts(gone)[0]).toMatchObject({
      type: "presence",
      payload: { seat: 0, status: "disconnected", deadlineTs: h.now + 30_000 },
    });
    expect(h.state.seats[0]).toMatchObject({ connected: false, autoPlay: true });
    expect(types(h.tick(2_900))).toEqual([]);
    expect(broadcasts(h.tick(100))[0]).toMatchObject({
      type: "move_applied",
      payload: { seat: 0, auto: true },
    });
  });

  it("plays out a simultaneous window for every seat that missed it", () => {
    const h = harness();
    started(h, 3);
    h.send("s1", "play_card", { roundId: roundIdOf(h), clientSeq: 1, card: 1 });
    const fired = h.tick(10_000);
    expect(
      broadcasts(fired)
        .filter((e) => e.type === "move_applied")
        .map((e) => (e.payload as { seat: number }).seat),
    ).toEqual([0, 2]);
    expect(h.state.round?.state.phase).toBe("play");
    expect(h.state.round?.deadline).toMatchObject({ kind: "perSeatTimer", seats: [0] });
  });
});

describe("reconnection and snapshots", () => {
  it("reattaches a returning player, rotates the token and repeats its turn_start", () => {
    const h = harness();
    opened(h);
    const token = h.state.seats[0]?.resumeToken;
    h.core.leave(h.state, [presence(0)], h.now);
    const again = { sessionId: "s0b", userId: "u0", username: "Player0" };
    expect(h.core.joinAttempt(h.state, again, { resumeToken: token }, h.now)).toEqual({
      accept: true,
    });
    const batch = h.core.join(h.state, [again], h.now);
    expect(h.state.seats[0]).toMatchObject({ sessionId: "s0b", connected: true, autoPlay: false });
    expect(h.state.seats[0]?.resumeToken).not.toBe(token);
    expect(sentTo(batch, "s0b").map((e) => e.type)).toEqual(["table_state", "turn_start"]);
    expect(broadcasts(batch)[0]?.payload).toMatchObject({ seat: 0, status: "reconnected" });
    expect(h.core.inbound(h.state, "s0", "{}", h.now).outbound).toEqual([]);
  });

  it("lets a player who left mid-round rejoin their seat before the round ends (R46)", () => {
    const h = harness();
    opened(h, 4);
    h.send("s3", "leave_table", {});
    expect(h.state.seats[3]).toMatchObject({ left: true, autoPlay: true });
    const again = { sessionId: "s3b", userId: "u3", username: "Player3" };
    expect(h.core.joinAttempt(h.state, again, { code: h.state.code }, h.now)).toEqual({
      accept: true,
    });
    const batch = h.core.join(h.state, [again], h.now);
    expect(h.state.seats[3]).toMatchObject({
      playerId: "u3",
      sessionId: "s3b",
      left: false,
      autoPlay: false,
      connected: true,
    });
    expect(broadcasts(batch)[0]?.payload).toMatchObject({ seat: 3, status: "reconnected" });
  });

  it("gives a seat that returns during its own turn a full turn timer again (GDD 9.8)", () => {
    const h = harness();
    opened(h);
    const turn = h.state.round?.state.turnSeat ?? 0;
    const before = h.state.round?.deadline?.humanTs ?? 0;
    expect(before).toBeGreaterThan(h.now);
    const session = `s${String(turn)}`;
    const token = h.state.seats[turn]?.resumeToken;
    h.core.leave(h.state, [presence(turn)], h.now);
    // Almost the whole turn passes while the seat is away; it returns a second before the deadline.
    h.now = before - 1_000;
    const again = {
      sessionId: `${session}b`,
      userId: `u${String(turn)}`,
      username: `Player${String(turn)}`,
    };
    expect(h.core.joinAttempt(h.state, again, { resumeToken: token }, h.now)).toEqual({
      accept: true,
    });
    const batch = h.core.join(h.state, [again], h.now);
    const extended = h.state.round?.deadline?.humanTs ?? 0;
    expect(extended).toBeGreaterThanOrEqual(h.now + 20_000);
    const turnStart = sentTo(batch, `${session}b`).find((e) => e.type === "turn_start");
    expect(turnStart?.payload).toMatchObject({ seat: turn, deadlineTs: extended });
    // The old deadline passes without the policy playing for the returned seat.
    const ticked = h.tick(1_500);
    expect(ticked.outbound.filter((o) => o.envelope.type === "move_applied")).toEqual([]);
    expect(h.state.seats[turn]).toMatchObject({ autoPlay: false, timeouts: 0 });
  });

  it("a second device is refused while the seat's session is live, and the resume token takes it over", () => {
    const h = harness();
    seated(h, 3);
    const twin = { sessionId: "s0b", userId: "u0", username: "Player0" };
    expect(h.core.joinAttempt(h.state, twin, {}, h.now)).toEqual({
      accept: false,
      code: "auth.device_limit",
    });
    const token = h.state.seats[0]?.resumeToken ?? "";
    expect(h.core.joinAttempt(h.state, twin, { resumeToken: token }, h.now)).toEqual({
      accept: true,
    });
    const batch = h.core.join(h.state, [twin], h.now);
    expect(batch.kicks).toEqual(["s0"]);
    expect(h.state.presences["s0"]).toBeUndefined();
    expect(h.state.seats[0]?.sessionId).toBe("s0b");
  });

  it("answers request_snapshot with the viewer's state and only the messages it may see", () => {
    const h = harness();
    opened(h);
    const roundId = roundIdOf(h);
    const since = h.state.seq;
    h.send("s0", "play_card", { roundId, clientSeq: 2, card: 1 });
    h.send("s1", "play_card", { roundId, clientSeq: 2, card: 1 });
    const batch = h.send("s2", "request_snapshot", { sinceSeq: since });
    const snapshot = payloadOf<{
      state: TableStateFor<ToyView>;
      events: { type: string; seq: number }[];
    }>(sentTo(batch, "s2")[0]);
    expect(snapshot.state.round?.viewer).toBe(2);
    expect(snapshot.state.lastSeq).toBe(h.state.seq);
    expect(snapshot.events.map((e) => e.type)).toEqual([
      "move_applied",
      "move_applied",
      "private_draw",
      "turn_start",
    ]);
    expect(snapshot.events.every((e) => e.seq > since)).toBe(true);
    const other = payloadOf<{ events: { type: string }[] }>(
      sentTo(h.send("s0", "request_snapshot", { sinceSeq: since }), "s0")[0],
    );
    expect(other.events.map((e) => e.type)).toEqual(["move_applied", "move_applied"]);
  });

  it("snapshots every N messages and at phase changes, and each reproduces the log's hash", () => {
    const h = harness({ config: { snapshotEvery: 3 } });
    opened(h);
    const roundId = roundIdOf(h);
    for (let i = 0; i < 4; i += 1) {
      const seat = h.state.round?.state.turnSeat ?? 0;
      h.send(`s${String(seat)}`, "play_card", { roundId, clientSeq: 100 + i, card: 1 });
    }
    const snapshots = h.state.round?.snapshots ?? [];
    expect(snapshots.length).toBeGreaterThanOrEqual(4);
    for (const snapshot of snapshots) {
      const logged = h.state.log.filter((m) => m.seq === snapshot.seq).at(-1);
      expect(logged?.stateHash).toBe(snapshot.stateHash);
      expect(stateHash(snapshot.state)).toBe(snapshot.stateHash);
    }
    expect(new Set(h.state.log.filter((m) => m.seat === null).map((m) => m.seq)).size).toBe(
      h.state.log.filter((m) => m.seat === null).length,
    );
  });
});

describe("round end, series and rematch", () => {
  const playToEnd = (h: Harness): Batch => {
    const roundId = roundIdOf(h);
    let last = h.send("s0", "ping", { sentTs: 0 });
    let guard = 0;
    while (h.state.phase === "inRound" && guard < 50) {
      const seat = h.state.round?.state.turnSeat ?? 0;
      last = h.send(`s${String(seat)}`, "play_card", { roundId, clientSeq: 200 + guard, card: 2 });
      guard += 1;
    }
    return last;
  };

  it("ends the round with round_end, updates the series, records the round and opens the vote", () => {
    const h = harness();
    opened(h);
    const last = playToEnd(h);
    expect(h.state.phase).toBe("roundEnded");
    const end = broadcasts(last).find((e) => e.type === "round_end");
    expect(end?.payload).toMatchObject({
      roundId: "r_1",
      seedHex: h.state.round?.seedHex,
      series: { roundsPlayed: 1 },
    });
    expect(h.state.series?.roundsPlayed).toBe(1);
    expect(h.records).toHaveLength(1);
    expect(h.records[0]).toMatchObject({
      roundId: "r_1",
      completed: true,
      players: ["u0", "u1", "u2"],
    });
    const view = payloadOf<TableStateFor<ToyView>>(sentTo(last, "s1").at(-1));
    expect(view.rematch).toEqual({ deadlineTs: h.now + 20_000, votes: [] });
    expect(view.round?.phase).toBe("ended");
    expect(h.state.seats.slice(0, 3).every((s) => !s.ready && s.rematch === null)).toBe(true);
  });

  it("replays to the recorded final hash from the seed and the move log alone (ADR-0009)", () => {
    const h = harness();
    opened(h);
    playToEnd(h);
    const record = h.records[0];
    if (record === undefined) throw new Error("no record");
    const seed = hexToBytes(record.seedHex);
    const rng = Xoshiro128StarStar.fromState(stateFromSeed(seed));
    const created = toyModule.createRound(
      { rules: { target: 10 }, seatCount: 3, dealerSeat: 0, seed },
      rng,
    );
    if (!created.ok) throw new Error(created.error.code);
    let state = created.value;
    for (const entry of record.moves) {
      const applied = toyModule.applyMove(state, entry.seat, entry.move as ToyMove, rng);
      if (!applied.ok) throw new Error(applied.error.code);
      state = applied.state;
    }
    expect(stateHash(state)).toBe(record.finalStateHash);
  });

  it("deals the next round at once when every seat votes yes, with the dealer rotated", () => {
    const h = harness();
    opened(h);
    playToEnd(h);
    h.send("s0", "vote_rematch", { yes: true });
    h.send("s1", "vote_rematch", { yes: true });
    const last = h.send("s2", "vote_rematch", { yes: true });
    expect(h.state.phase).toBe("inRound");
    expect(h.state.round?.roundId).toBe("r_2");
    expect(h.state.series?.nextDealerSeat).toBe(1);
    expect(broadcasts(last).map((e) => e.type)).toContain("round_start");
  });

  it("continues with the yes votes when enough voted and the rest become spectators (OQ-005)", () => {
    const h = harness();
    opened(h, 4);
    playToEnd(h);
    for (const s of ["s0", "s1", "s2"]) h.send(s, "vote_rematch", { yes: true });
    const resolved = h.tick(20_000);
    expect(h.state.phase).toBe("inRound");
    expect(h.state.round?.seatCount).toBe(3);
    expect(h.state.presences["s3"]?.seat).toBeNull();
    expect(broadcasts(resolved).find((e) => e.type === "presence")?.payload).toMatchObject({
      playerId: "u3",
      status: "spectating",
    });
  });

  it("returns a private table to the lobby and dissolves a public one when the vote fails", () => {
    const priv = harness();
    opened(priv);
    playToEnd(priv);
    priv.send("s0", "vote_rematch", { yes: false });
    priv.send("s1", "vote_rematch", { yes: true });
    priv.send("s2", "vote_rematch", { yes: true });
    expect(priv.state.phase).toBe("lobby");
    expect(priv.state.round).toBeNull();
    expect(priv.state.series?.roundsPlayed).toBe(1);

    const pub = harness({ mode: "public" });
    opened(pub);
    playToEnd(pub);
    pub.send("s0", "vote_rematch", { yes: true });
    const closed = pub.tick(20_000);
    expect(broadcasts(closed).at(-1)).toMatchObject({
      type: "table_closed",
      payload: { reason: "dissolved" },
    });
    expect(closed.end).toBe(true);
  });

  it("the host may also just deal again after a round, and seats that left are freed", () => {
    const h = harness();
    opened(h, 4);
    h.send("s3", "leave_table", {});
    expect(h.state.seats[3]).toMatchObject({ left: true, autoPlay: true, connected: false });
    expect(h.state.round?.seatCount).toBe(4);
    playToEnd(h);
    expect(h.state.seats[3]?.playerId).toBeNull();
    expect(h.state.seats.slice(0, 3).map((s) => s.playerId)).toEqual(["u0", "u1", "u2"]);
    for (const s of ["s1", "s2"]) h.send(s, "ready", { ready: true });
    expect(sentTo(h.send("s0", "start_round", {}), "s0")[0]?.type).not.toBe("error");
    expect(h.state.round?.roundId).toBe("r_2");
  });

  it("a public table removes seats that never came back and dissolves under two humans (R46)", () => {
    const h = harness({ mode: "public", config: { autoPlayDelayMs: 0 } });
    opened(h);
    h.core.leave(h.state, [presence(1), presence(2)], h.now);
    h.tick(30_000);
    expect(h.state.seats[1]?.left).toBe(true);
    let guard = 0;
    while (h.state.phase === "inRound" && guard < 60) {
      if (h.state.round?.state.turnSeat === 0) {
        h.send("s0", "play_card", { roundId: roundIdOf(h), clientSeq: 300 + guard, card: 2 });
      }
      h.tick(1);
      guard += 1;
    }
    expect(h.state.phase).toBe("closing");
    expect(h.state.closeReason).toBe("dissolved");
  });

  it("closing mid-round records the round as incomplete", () => {
    const h = harness();
    opened(h);
    const batch = h.core.close(h.state, "server", h.now);
    expect(broadcasts(batch)[0]).toMatchObject({
      type: "table_closed",
      payload: { reason: "server" },
    });
    expect(h.records[0]).toMatchObject({ completed: false, roundId: "r_1" });
    expect(h.core.close(h.state, "server", h.now).outbound).toEqual([]);
  });
});
