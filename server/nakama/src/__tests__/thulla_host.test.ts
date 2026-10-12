import { check_envelope, payloadChecks } from "@hulla/protocol/generated";
import {
  createThullaRound,
  fairDealCommitment,
  legalMoves,
  resolveRules,
} from "@hulla/rules-engine";
import { hexToBytes, stateFromSeed, stateHash, Xoshiro128StarStar } from "@hulla/shared-ts";
import { describe, expect, it } from "vitest";

import { decodeThullaMove, encodeThullaEvents, thullaHost } from "../games/thulla/host";
import { tableCore, type TableCore } from "../match/table_core";

import type { Batch, Envelope, RoundRecord, TableState } from "../match/types";
import type { ThullaMove, ThullaState, ThullaView } from "@hulla/rules-engine";

const T0 = 1_760_200_000_000;
const SEATS = 4;

interface Sent {
  readonly envelope: Envelope;
  readonly to: string | null;
}

interface Table {
  readonly core: TableCore<ThullaState, ThullaMove>;
  readonly state: TableState<ThullaState, ThullaMove>;
  readonly sent: Sent[];
  readonly records: RoundRecord[];
  now: number;
  send(session: string, type: string, payload: object): Batch;
  tick(advanceMs: number): Batch;
}

function table(): Table {
  const records: RoundRecord[] = [];
  const sent: Sent[] = [];
  let uuids = 0;
  const core = tableCore(thullaHost, {
    ids: {
      uuid: () => {
        uuids += 1;
        return `0000000${String(uuids % 10)}-0000-4000-8000-${String(uuids).padStart(12, "0")}`;
      },
    },
    persist: { roundRecord: (record) => records.push(record) },
    config: { autoPlayDelayMs: 100 },
  });
  const state = core.create({ code: "ABCDEF", gameId: "thulla", mode: "private" }, T0);
  const collect = (batch: Batch): Batch => {
    for (const o of batch.outbound) {
      sent.push({ envelope: o.envelope, to: o.to.kind === "all" ? null : o.to.sessionId });
    }
    return batch;
  };
  const t: Table = {
    core,
    state,
    sent,
    records,
    now: T0,
    send(session, type, payload) {
      const envelope: Envelope = { v: 1, type, tableId: state.tableId, seq: 1, ts: t.now, payload };
      return collect(core.inbound(state, session, JSON.stringify(envelope), t.now, type));
    },
    tick(advanceMs) {
      t.now += advanceMs;
      return collect(core.tick(state, t.now));
    },
  };
  for (let i = 0; i < SEATS; i += 1) {
    const p = {
      sessionId: `s${String(i)}`,
      userId: `u${String(i)}`,
      username: `Player${String(i)}`,
    };
    core.joinAttempt(state, p, {}, t.now);
    collect(core.join(state, [p], t.now));
  }
  for (let i = 0; i < SEATS; i += 1) t.send(`s${String(i)}`, "ready", { ready: true });
  return t;
}

/** Every seat with a legal play plays its first legal card; absent seats are left to the core. */
function playRound(t: Table, absent: readonly number[] = [], maxSteps = 600): number {
  let clientSeq = 0;
  let steps = 0;
  while (t.state.phase === "inRound" && steps < maxSteps) {
    const round = t.state.round;
    if (round === null) break;
    let acted = false;
    for (let seat = 0; seat < SEATS; seat += 1) {
      if (absent.includes(seat)) continue;
      const legal = legalMoves(round.state, seat);
      const card = legal.find(
        (m): m is Extract<ThullaMove, { type: "play_card" }> => m.type === "play_card",
      );
      if (card === undefined) continue;
      clientSeq += 1;
      t.send(`s${String(seat)}`, "play_card", {
        roundId: round.roundId,
        clientSeq,
        card: card.card,
      });
      acted = true;
      break;
    }
    if (!acted) t.tick(1_500);
    steps += 1;
  }
  return steps;
}

describe("the Thulla host at the table core", () => {
  it("plays a full four-seat round whose every message validates against the protocol", () => {
    const t = table();
    t.send("s0", "start_round", {});
    expect(t.state.phase).toBe("inRound");
    const steps = playRound(t);
    expect(t.state.phase).toBe("roundEnded");
    expect(steps).toBeLessThan(600);
    let checked = 0;
    for (const { envelope } of t.sent) {
      expect(check_envelope(envelope)).toBe(true);
      const check = payloadChecks[envelope.type];
      if (check === undefined) throw new Error(`no check for ${envelope.type}`);
      if (!check(envelope.payload)) {
        throw new Error(
          `${envelope.type} failed: ${JSON.stringify(envelope.payload).slice(0, 300)}`,
        );
      }
      checked += 1;
    }
    expect(checked).toBe(t.sent.length);
    const types = new Set(t.sent.map((s) => s.envelope.type));
    expect([...types]).toEqual(
      expect.arrayContaining([
        "round_start",
        "turn_start",
        "move_applied",
        "trick_resolved",
        "round_end",
      ]),
    );
  });

  it("never sends another seat's hand, legal mask or private draw (GDD 24.2)", () => {
    const t = table();
    t.send("s0", "start_round", {});
    playRound(t);
    const seatOf = (session: string | null): number | null =>
      session === null ? null : Number(session.slice(1));
    for (const { envelope, to } of t.sent) {
      const viewer = seatOf(to);
      if (envelope.type === "table_state") {
        const view = (envelope.payload as { round: ThullaView | null }).round;
        if (view === null) continue;
        view.seats.forEach((seat, index) => {
          if (index === viewer) expect(seat.hand).not.toBeNull();
          else expect(seat.hand).toBeNull();
        });
        if (view.phase !== "ended") {
          expect(view.seedHex).toBeNull();
          expect(view.deckOrder).toBeNull();
        }
      }
      if (envelope.type === "turn_start") {
        expect((envelope.payload as { seat: number }).seat).toBe(viewer);
      }
      if (envelope.type === "private_draw") {
        const draw = envelope.payload as { seat: number; from: number | null };
        expect([draw.seat, draw.from]).toContain(viewer);
      }
    }
  });

  it("publishes a commitment the revealed seed and deck order reproduce, and replays to the final hash (R6, ADR-0009)", () => {
    const t = table();
    t.send("s0", "start_round", {});
    playRound(t);
    const start = t.sent.find((s) => s.envelope.type === "round_start")?.envelope.payload as {
      commitment: string;
      dealerSeat: number;
    };
    const end = t.sent.find((s) => s.envelope.type === "round_end")?.envelope.payload as {
      fairness: { commitment: string; seed: string; deckOrder: string[] };
      series: { roundsPlayed: number };
    };
    expect(end.fairness.commitment).toBe(start.commitment);
    expect(fairDealCommitment(hexToBytes(end.fairness.seed), end.fairness.deckOrder as never)).toBe(
      start.commitment,
    );
    expect(end.series.roundsPlayed).toBe(1);

    const record = t.records[0];
    if (record === undefined) throw new Error("no record");
    const seed = hexToBytes(record.seedHex);
    const rng = Xoshiro128StarStar.fromState(stateFromSeed(seed));
    const rules = resolveRules("private");
    if (!rules.ok) throw new Error(rules.error.code);
    const created = createThullaRound(
      { rules: rules.value, seatCount: SEATS, dealerSeat: start.dealerSeat, seed },
      rng,
    );
    if (!created.ok) throw new Error(created.error.code);
    let state = created.value;
    for (const entry of record.moves) {
      const applied = thullaHost.module.applyMove(state, entry.seat, entry.move as ThullaMove, rng);
      if (!applied.ok) throw new Error(applied.error.code);
      state = applied.state;
    }
    expect(stateHash(state)).toBe(record.finalStateHash);
    expect(state.phase).toBe("ended");
  });

  it("lets the auto-play policy play an absent seat to the end of the round (R44, R46)", () => {
    const t = table();
    t.send("s0", "start_round", {});
    t.core.leave(t.state, [{ sessionId: "s3", userId: "u3", username: "Player3" }], t.now);
    expect(t.state.seats[3]).toMatchObject({ connected: false, autoPlay: true });
    playRound(t, [3]);
    expect(t.state.phase).toBe("roundEnded");
    const autoPlays = t.sent.filter(
      (s) =>
        s.envelope.type === "move_applied" &&
        (s.envelope.payload as { seat: number; auto?: boolean }).seat === 3,
    );
    expect(autoPlays.length).toBeGreaterThan(0);
    expect(autoPlays.every((s) => (s.envelope.payload as { auto?: boolean }).auto === true)).toBe(
      true,
    );
    expect(t.state.seats[3]?.playerId).toBe("u3");
  });

  it("rejects an illegal card with the engine's code and a malformed move with the catalogue code", () => {
    const t = table();
    t.send("s0", "start_round", {});
    const round = t.state.round;
    if (round === null) throw new Error("no round");
    const leader = round.state.leaderSeat;
    const other = (leader + 1) % SEATS;
    const wrong = t.send(`s${String(other)}`, "play_card", {
      roundId: round.roundId,
      clientSeq: 1,
      card: "AS",
    });
    const rejection = wrong.outbound.find((o) => o.envelope.type === "move_rejected");
    expect(rejection?.envelope.payload).toMatchObject({
      clientSeq: 1,
      error: { code: expect.stringMatching(/^rules\./) as string },
    });
    const bad = t.send(`s${String(leader)}`, "play_card", {
      roundId: round.roundId,
      clientSeq: 2,
      card: "1Z",
    });
    expect(
      bad.outbound.find((o) => o.envelope.type === "move_rejected")?.envelope.payload,
    ).toMatchObject({
      error: { code: "table.invalid_payload", detail: { reason: "schema", type: "play_card" } },
    });
    expect(decodeThullaMove("cancel_take", { roundId: "r_1", clientSeq: 3 })).toMatchObject({
      ok: false,
    });
    expect(decodeThullaMove("take_hand", { roundId: "r_1", clientSeq: 3 })).toEqual({
      ok: true,
      value: { type: "take_hand" },
    });
  });

  it("encodes a take and an engine play-out as auto moves and a thulla as the long presentation", () => {
    const ctx = {
      roundId: "r_1",
      seedHex: "00",
      state: { trickIndex: 4, rulesVersion: "1" } as ThullaState,
      series: null,
      result: null,
      move: { seat: 2, clientSeq: 7, auto: false },
    };
    const messages = encodeThullaEvents(ctx, [
      { type: "hand_taken", taker: 2, target: 3, cardCount: 5 },
      { type: "card_played", seat: 2, card: "AS", trickIndex: 4, thulla: false },
      { type: "card_played", seat: 3, card: "KS", trickIndex: 4, thulla: true },
    ]);
    expect(
      messages.map((m) => [
        m.type,
        (m.payload as { auto?: boolean; clientSeq?: number }).clientSeq ?? null,
        (m.payload as { auto?: boolean }).auto,
      ]),
    ).toEqual([
      ["move_applied", 7, false],
      ["hand_taken", null, undefined],
      ["move_applied", null, true],
      ["move_applied", null, true],
    ]);
    expect(
      thullaHost.presentationDelayMs([
        { type: "card_played", seat: 3, card: "KS", trickIndex: 4, thulla: true },
      ]),
    ).toBe(1_200);
    expect(thullaHost.presentationDelayMs([{ type: "escape", seat: 1, rank: 1 }])).toBe(0);
  });
});
