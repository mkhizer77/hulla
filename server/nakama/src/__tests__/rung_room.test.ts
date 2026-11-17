import { rungFirstDealer, rungModule, type RungState } from "@hulla/rules-engine";
import { describe, expect, it } from "vitest";

import { rungHost } from "../games/rung/host";
import { emptySlot } from "../match/lobby";
import { hostFor } from "../match/registry";
import { compactSeats, roundSeed, startRound } from "../match/round";
import {
  tableCore,
  type JoinDecision,
  type PresenceInfo,
  type SocialGate,
  type TableCore,
} from "../match/table_core";
import { DEFAULT_TABLE_CONFIG } from "../match/types";

import { T0, harness, presence } from "./table-helpers";

import type { AnyGameHost, Batch, Envelope, RoundRecord, TableState } from "../match/types";
import type { RulesSelection } from "@hulla/protocol/generated";

type AnyTable = TableState<unknown, unknown>;

interface Room {
  readonly state: AnyTable;
  readonly records: RoundRecord[];
  readonly sent: Batch["outbound"];
  now: number;
  attempt(p: PresenceInfo, metadata?: Record<string, unknown>): JoinDecision;
  join(p: PresenceInfo, metadata?: Record<string, unknown>): Batch;
  /** The socket dropped: Nakama's matchLeave. */
  drop(p: PresenceInfo): Batch;
  send(sessionId: string, type: string, payload: object): Batch;
  tick(advanceMs?: number): Batch;
}

const NO_FRIENDS: SocialGate = { friendsAmong: () => [] };

/** A table on the real registry; `matched` makes it the public table the matchmaker opens. */
function room(
  options: {
    readonly gameId?: "thulla" | "rung";
    readonly players?: number;
    readonly matched?: boolean;
    readonly rules?: RulesSelection;
    readonly social?: SocialGate;
  } = {},
): Room {
  const gameId = options.gameId ?? "rung";
  let uuids = 0;
  const records: RoundRecord[] = [];
  const sent: Batch["outbound"] = [];
  const coreOf = (id: string): TableCore<unknown, unknown> => {
    const host = hostFor(id);
    if (host === null) throw new Error(`no host for ${id}`);
    return tableCore(host, {
      ids: {
        uuid: () => {
          uuids += 1;
          return `00000000-0000-4000-8000-${String(uuids).padStart(12, "0")}`;
        },
      },
      persist: { roundRecord: (record) => records.push(record) },
      games: (other): AnyGameHost | null => hostFor(other),
      ...(options.social === undefined ? {} : { social: options.social }),
    });
  };
  const state = coreOf(gameId).create(
    {
      code: "ABCDEF",
      gameId,
      mode: options.matched === true ? "public" : "private",
      rules: options.rules ?? { gameId },
      ...(options.matched === true ? { autoStart: { expected: 4 }, parties: [] } : {}),
    },
    T0,
  );
  const keep = (batch: Batch): Batch => {
    sent.push(...batch.outbound);
    return batch;
  };
  const r: Room = {
    state,
    records,
    sent,
    now: T0,
    attempt: (p, metadata = {}) => coreOf(state.gameId).joinAttempt(state, p, metadata, r.now),
    join(p, metadata = {}) {
      const decision = r.attempt(p, metadata);
      if (!decision.accept) throw new Error(`join rejected: ${decision.code}`);
      return keep(coreOf(state.gameId).join(state, [p], r.now));
    },
    drop: (p) => keep(coreOf(state.gameId).leave(state, [p], r.now)),
    send(sessionId, type, payload) {
      const envelope: Envelope = { v: 1, type, tableId: state.tableId, seq: 1, ts: r.now, payload };
      return keep(
        coreOf(state.gameId).inbound(state, sessionId, JSON.stringify(envelope), r.now, type),
      );
    },
    tick(advanceMs = 0) {
      r.now += advanceMs;
      return keep(coreOf(state.gameId).tick(state, r.now));
    },
  };
  for (let i = 0; i < (options.players ?? 4); i += 1) r.join(presence(i));
  return r;
}

function rung(r: Room): RungState {
  const state = r.state.round?.state;
  if (state === undefined) throw new Error("no round");
  return state as RungState;
}

function seatOf(r: Room, userId: string): number {
  return r.state.seats.findIndex((s) => s.playerId === userId);
}

function baziOver(r: Room): boolean {
  return r.state.series?.finished === true;
}

/** The host deals in a private room. */
function start(r: Room): void {
  for (const slot of r.state.seats) {
    if (slot.sessionId !== null) r.send(slot.sessionId, "ready", { ready: true });
  }
  r.send("s0", "start_round", {});
  if (r.state.round === null) throw new Error("the round did not start");
}

/** A matched table deals itself once everyone has arrived. */
function dealt(r: Room): void {
  for (let i = 0; i < 40 && r.state.round === null; i += 1) r.tick(1_000);
  if (r.state.round === null) throw new Error("the table did not deal");
}

let clientSeq = 0;

/** Plays the deal out: people at the table play the timeout rule's move, the rest is left to the table. */
function playDeal(r: Room): void {
  for (let i = 0; i < 2_000 && r.state.phase === "inRound"; i += 1) {
    const state = rung(r);
    const seat = state.turnSeat;
    const slot = seat === null ? undefined : r.state.seats[seat];
    const dueTs = r.state.round?.deadline?.notBeforeTs ?? 0;
    if (seat !== null && slot?.sessionId != null && slot.connected && r.now >= dueTs) {
      const move = rungModule.onDeadline(state, seat);
      if (move === null) throw new Error("no move");
      const { type, ...rest } = move;
      clientSeq += 1;
      r.send(slot.sessionId, type, { roundId: r.state.round?.roundId, clientSeq, ...rest });
    }
    r.tick(500);
  }
  if (r.state.phase === "inRound") throw new Error("the deal did not end");
}

describe("Rung Quick Play: a leaver's seat stays with a bot to the end of the bazi (RGE11)", () => {
  it("RGE11 one player leaves mid-deal: the table deals on, a bot votes yes, and the bazi pays who stayed", () => {
    const q = room({ matched: true });
    dealt(q);
    const leaver = seatOf(q, "u3");
    q.send("s3", "leave_table", {});
    playDeal(q);
    // The deal ended with the bazi still open: the bot keeps the seat and the table stays up.
    expect(q.state.series?.finished).toBe(false);
    expect(q.state.phase).toBe("roundEnded");
    expect(q.state.seats[leaver]).toMatchObject({ bot: true, cover: true, rematch: true });
    expect(q.state.seats.filter((s) => s.playerId !== null)).toHaveLength(4);
    // Someone who turns up now may watch; the bot's seat is not theirs to take mid-bazi.
    expect(q.attempt(presence(9))).toEqual({ accept: true });
    expect(q.state.admissions["s9"]).toMatchObject({ kind: "spectator" });
    // Nobody votes: the bazi is played on anyway when the window closes.
    q.tick(DEFAULT_TABLE_CONFIG.rematchWindowMs + 1_000);
    expect(q.state.phase).toBe("inRound");
    expect(q.state.round?.index).toBe(2);
    for (let deals = 0; deals < 20 && !baziOver(q); deals += 1) {
      playDeal(q);
      if (!baziOver(q)) q.tick(DEFAULT_TABLE_CONFIG.rematchWindowMs + 1_000);
    }
    const last = q.records[q.records.length - 1];
    expect(last?.series?.finished).toBe(true);
    expect(last?.completed).toBe(true);
    // The three who stayed are on the bazi's last record; the leaver's seat is a bot's, so it
    // earns nothing for the deals the leaver did not play.
    expect(last?.players.filter((p) => p !== null).sort()).toEqual(["u0", "u1", "u2"]);
    expect(last?.players[leaver]).toBeNull();
    expect(q.records[0]?.players[leaver]).toBe("u3");
    expect(q.records.slice(1).every((record) => record.players[leaver] === null)).toBe(true);
    // The bazi is over: the bot gives the seat up, and a new bazi needs four people again.
    expect(q.state.seats[leaver]?.playerId).toBeNull();
    expect(q.state.phase).toBe("roundEnded");
  });

  it("RGE11 a no vote or a drop between deals hands the seat to a bot instead of closing the table", () => {
    const q = room({ matched: true });
    dealt(q);
    playDeal(q);
    expect(q.state.series?.finished).toBe(false);
    const quitter = seatOf(q, "u1");
    const dropped = seatOf(q, "u2");
    q.send("s1", "vote_rematch", { yes: false });
    q.drop(presence(2));
    expect(q.state.seats[dropped]).toMatchObject({ bot: true, cover: true });
    q.send("s0", "vote_rematch", { yes: true });
    q.send("s3", "vote_rematch", { yes: true });
    expect(q.state.phase).toBe("inRound");
    expect(q.state.seats[quitter]).toMatchObject({ bot: true, cover: true });
    // The one who said no stays to watch; nobody who joins takes the bot's seat mid-bazi.
    expect(q.state.presences["s1"]?.seat).toBeNull();
    expect(
      q.state.seats
        .filter((s) => !s.bot)
        .map((s) => s.playerId)
        .sort(),
    ).toEqual(["u0", "u3"]);
    expect(q.state.closeReason).toBeNull();
  });

  it("RGE11 the table closes once nobody is left to finish the bazi", () => {
    const q = room({ matched: true });
    dealt(q);
    playDeal(q);
    for (let i = 0; i < 4; i += 1) q.send(`s${String(i)}`, "leave_table", {});
    q.tick(1_000);
    expect(q.state.phase).toBe("closing");
    expect(q.state.closeReason).toBe("dissolved");
  });

  it("a private room is not covered this way: a seat left between deals is free for the bench", () => {
    const r = room();
    start(r);
    playDeal(r);
    r.send("s3", "leave_table", {});
    expect(r.state.seats[3]?.playerId).toBeNull();
  });
});

describe("a private room with bots waits for its people", () => {
  it("the last person dropping in the lobby does not close the room before the empty-room time", () => {
    const r = room({ players: 1 });
    for (let i = 0; i < 3; i += 1) r.send("s0", "add_bot", {});
    expect(r.state.seats.filter((s) => s.bot)).toHaveLength(3);
    r.drop(presence(0));
    r.tick(1_000);
    r.tick(10 * 60_000);
    expect(r.state.phase).toBe("lobby");
    // The same person comes back by the code and is the host again.
    r.join({ ...presence(0), sessionId: "s0b" });
    expect(r.state.seats.filter((s) => s.bot)).toHaveLength(3);
    expect(r.state.hostSeat).toBe(seatOf(r, "u0"));
    r.drop({ ...presence(0), sessionId: "s0b" });
    r.tick(DEFAULT_TABLE_CONFIG.emptyTtlMs.private + 1_000);
    expect(r.state.closeReason).toBe("empty");
  });

  it("the last person dropping between deals does not close the room either", () => {
    const r = room({ players: 1 });
    for (let i = 0; i < 3; i += 1) r.send("s0", "add_bot", {});
    start(r);
    playDeal(r);
    expect(r.state.phase).toBe("roundEnded");
    r.drop(presence(0));
    r.tick(1_000);
    r.tick(DEFAULT_TABLE_CONFIG.rematchWindowMs + 1_000);
    expect(r.state.phase).toBe("lobby");
    expect(r.state.closeReason).toBeNull();
  });

  it("a matched bot table still ends when its player has gone", () => {
    const h = harness({ bots: 2 });
    h.join(presence(0));
    h.send("s0", "leave_table", {});
    h.tick(1_000);
    expect(h.state.closeReason).toBe("dissolved");
  });
});

describe("the bench of a private room (Rung design 8.3)", () => {
  it("a fifth person who comes to a full Rung room by its code gets in and waits for a seat", () => {
    const r = room({
      social: NO_FRIENDS,
      rules: { gameId: "rung", settings: { spectators: false } },
    });
    expect(r.attempt(presence(4))).toEqual({ accept: true });
    r.join(presence(4));
    expect(r.state.presences["s4"]).toMatchObject({ seat: null, seatNext: true });
    expect(r.state.bench).toEqual(["u4"]);
    // The same holds once the cards are out.
    start(r);
    expect(r.attempt(presence(5))).toEqual({ accept: true });
  });

  it("a benched player who reconnects is let back in without the watcher's friend check", () => {
    const r = room({ gameId: "thulla", players: 5, social: NO_FRIENDS });
    r.send("s0", "change_game", { gameId: "rung" });
    expect(r.state.presences["s4"]).toMatchObject({ seat: null, seatNext: true });
    start(r);
    r.drop(presence(4));
    const back = { ...presence(4), sessionId: "s4b" };
    expect(r.attempt(back, { asSpectator: "true" })).toEqual({ accept: true });
    r.join(back);
    expect(r.state.presences["s4b"]).toMatchObject({ seat: null, seatNext: true });
  });

  it("a plain watcher is still checked, and leaving the room gives up the bench", () => {
    const r = room({ social: NO_FRIENDS });
    expect(r.attempt(presence(7), { asSpectator: "true" })).toEqual({
      accept: false,
      code: "social.not_friends",
    });
    r.join(presence(4));
    r.send("s4", "leave_table", {});
    expect(r.state.bench).toEqual([]);
    expect(r.attempt({ ...presence(4), sessionId: "s4b" }, { asSpectator: "true" })).toEqual({
      accept: false,
      code: "social.not_friends",
    });
  });

  it("a benched player takes the seat a leaver frees and is off the bench", () => {
    const r = room();
    r.join(presence(4));
    start(r);
    r.send("s2", "leave_table", {});
    playDeal(r);
    expect(seatOf(r, "u4")).toBe(2);
    expect(r.state.bench).toEqual([]);
  });

  it("a matched table has no bench: a stranger without a seat is refused", () => {
    const q = room({ matched: true, social: NO_FRIENDS });
    dealt(q);
    expect(q.attempt(presence(8))).toEqual({ accept: false, code: "social.not_friends" });
    expect(q.state.bench ?? []).toEqual([]);
  });
});

describe("Rung: a seat that drops keeps its turn timer (RGE1, RG43)", () => {
  it("RGE1 a caller who drops for four seconds and returns still names the rung", () => {
    const r = room();
    start(r);
    const caller = rung(r).callerSeat;
    const who = presence(caller);
    r.drop(who);
    expect(r.state.seats[caller]?.autoPlay).toBe(false);
    for (let i = 0; i < 4; i += 1) r.tick(1_000);
    expect(rung(r).trump).toBeNull();
    r.join({ ...who, sessionId: "back" });
    expect(r.state.seats[caller]).toMatchObject({ connected: true, autoPlay: false });
    r.tick(5_000);
    expect(rung(r).trump).toBeNull();
    r.send("back", "call_trump", { roundId: r.state.round?.roundId, clientSeq: 1, suit: "H" });
    expect(rung(r).trump).toBe("H");
  });

  it("RGE1 RG9 a caller who stays away is called for when the call timer ends, then the bot has the seat", () => {
    const r = room();
    start(r);
    const dealtTs = r.now;
    const caller = rung(r).callerSeat;
    const policy = rungModule.onDeadline(rung(r), caller);
    r.drop(presence(caller));
    for (let i = 0; i < 19; i += 1) r.tick(1_000);
    expect(rung(r).trump).toBeNull();
    r.tick(1_000);
    // The timeout rule's suit (RG9), not a second timeout and not a pick of the bot's.
    expect(policy).toMatchObject({ type: "call_trump", suit: rung(r).trump });
    expect(r.now - dealtTs).toBe(20_000);
    expect(r.state.seats[caller]).toMatchObject({ autoPlay: true, timeouts: 1 });
    // The caller leads (RG10): covered now, the seat plays within seconds, not a full timer.
    const calledTs = r.now;
    for (let i = 0; i < 20 && rung(r).trick.plays.length === 0; i += 1) r.tick(500);
    expect(rung(r).trick.plays[0]?.seat).toBe(caller);
    expect(r.now - calledTs).toBeLessThanOrEqual(6_000);
  });

  it("RG43 with the timer off a seat twenty seconds away is played by the bot", () => {
    const r = room({ rules: { gameId: "rung", settings: { turnTimerSeconds: null } } });
    start(r);
    const caller = rung(r).callerSeat;
    r.drop(presence(caller));
    for (let i = 0; i < 19; i += 1) r.tick(1_000);
    expect(rung(r).trump).toBeNull();
    expect(r.state.seats[caller]?.autoPlay).toBe(false);
    r.tick(1_000);
    expect(r.state.seats[caller]?.autoPlay).toBe(true);
    r.tick(200);
    expect(rung(r).trump).not.toBeNull();
  });

  it("R46 Thulla's auto-play still takes a dropped seat at once", () => {
    const h = harness();
    for (let i = 0; i < 3; i += 1) h.join(presence(i));
    for (let i = 0; i < 3; i += 1) h.send(`s${String(i)}`, "ready", { ready: true });
    h.send("s0", "start_round", {});
    h.core.leave(h.state, [presence(1)], h.now);
    expect(h.state.seats[1]?.autoPlay).toBe(true);
  });
});

describe("Rung timers (RG41)", () => {
  it("RG41 the call runs twenty seconds, a turn the table's turn seconds, the court choice eight", () => {
    const q = room({ matched: true });
    dealt(q);
    const call = q.state.round?.deadline;
    expect((call?.humanTs ?? 0) - (call?.notBeforeTs ?? 0)).toBe(20_000);
    const caller = rung(q).callerSeat;
    const session = q.state.seats[caller]?.sessionId ?? "";
    q.send(session, "call_trump", { roundId: q.state.round?.roundId, clientSeq: 1, suit: "S" });
    const turn = q.state.round?.deadline;
    expect((turn?.humanTs ?? 0) - (turn?.notBeforeTs ?? 0)).toBe(
      DEFAULT_TABLE_CONFIG.defaultTurnSeconds.public * 1000,
    );
    expect(rungHost.phaseSeconds?.("courtChoice", 15)).toBe(8);
    expect(rungHost.phaseSeconds?.("courtChoice", 60)).toBe(8);
    // Relaxed tables give the caller their long turn too.
    expect(rungHost.phaseSeconds?.("call", 60)).toBe(60);
    expect(rungHost.phaseSeconds?.("play", 60)).toBe(60);
  });
});

describe("the first dealer (RG4, RG38)", () => {
  const deal = (r: Room, n: number): { dealer: number; seed: Uint8Array } => {
    const uuids = [0, 1, 2, 3].map((i) => `seed-${String(n)}-${String(i)}`);
    const host = hostFor(r.state.gameId);
    if (host === null) throw new Error("no host");
    const round = startRound(
      { host, config: DEFAULT_TABLE_CONFIG, persist: { roundRecord: () => undefined } },
      r.state,
      uuids,
      r.now,
    );
    if (!round.ok) throw new Error("no round");
    return { dealer: round.value.dealerSeat, seed: roundSeed(uuids) };
  };

  it("RG4 the first dealer of a bazi comes from the round seed, not from the host's seat", () => {
    const dealers = new Set<number>();
    for (let n = 0; n < 24; n += 1) {
      const r = room();
      expect(r.state.hostSeat).toBe(0);
      const { dealer, seed } = deal(r, n);
      expect(dealer).toBe(rungFirstDealer(seed));
      expect(r.state.series?.nextDealerSeat).toBe(dealer);
      dealers.add(dealer);
    }
    expect(dealers.size).toBe(4);
  });

  it("RG4 the same seed deals from the same seat, so a replay names the same dealer", () => {
    expect(deal(room(), 5).dealer).toBe(deal(room(), 5).dealer);
    const r = room();
    start(r);
    playDeal(r);
    const record = r.records[0];
    const seed = Uint8Array.from(
      (record?.seedHex.match(/../g) ?? []).map((byte) => Number.parseInt(byte, 16)),
    );
    expect(record?.dealerSeat).toBe(rungFirstDealer(seed));
  });

  it("RG38 a new bazi draws a new dealer; a bazi under way keeps the engine's next dealer", () => {
    const r = room();
    const first = deal(r, 1);
    const series = r.state.series;
    if (series === null) throw new Error("no series");
    // Mid-bazi the dealer is whoever RG35 to RG37 named, whatever the seed says.
    r.state.series = { ...series, roundsPlayed: 1, nextDealerSeat: (first.dealer + 1) % 4 };
    expect(deal(r, 2).dealer).toBe((first.dealer + 1) % 4);
    r.state.series = { ...series, roundsPlayed: 4, finished: true, nextDealerSeat: 0 };
    const next = deal(r, 3);
    expect(next.dealer).toBe(rungFirstDealer(next.seed));
    expect(r.state.series.roundsPlayed).toBe(0);
  });

  it("R5 Thulla is unchanged: the host's seat deals the first round", () => {
    const r = room({ gameId: "thulla", players: 4 });
    r.send("s0", "transfer_host", { seat: 2 });
    expect(deal(r, 9).dealer).toBe(2);
  });
});

describe("seats that are promised, and bots' ids", () => {
  it("a bot keeps its seat until the joiner arrives, and the reservation runs out", () => {
    const r = room({ players: 2 });
    r.send("s0", "add_bot", {});
    r.send("s0", "add_bot", {});
    expect(r.attempt(presence(2))).toEqual({ accept: true });
    // The client never arrives: the bot was not sent away for nothing.
    expect(r.state.seats.filter((s) => s.bot)).toHaveLength(2);
    expect(r.state.admissions["s2"]).toMatchObject({ kind: "seat", seat: 3 });
    r.tick(DEFAULT_TABLE_CONFIG.admissionTtlMs - 1_000);
    expect(Object.keys(r.state.admissions)).toEqual(["s2"]);
    r.tick(2_000);
    expect(r.state.admissions).toEqual({});
    // The seat is there for the next person, who takes it from the bot on arrival.
    r.join(presence(3));
    expect(r.state.seats.map((s) => s.playerId)).toEqual(["u0", "u1", "bot_2", "u3"]);
  });

  it("a joiner whose seat went into a round meanwhile waits for the next one", () => {
    const r = room({ players: 1 });
    for (let i = 0; i < 3; i += 1) r.send("s0", "add_bot", {});
    expect(r.attempt(presence(1))).toEqual({ accept: true });
    start(r);
    const core = tableCore(rungHost as unknown as AnyGameHost, {
      ids: { uuid: () => "unused" },
      persist: { roundRecord: () => undefined },
    });
    core.join(r.state, [presence(1)], r.now);
    expect(r.state.seats.filter((s) => s.bot)).toHaveLength(3);
    expect(r.state.presences["s1"]).toMatchObject({ seat: null, seatNext: true });
  });

  it("bots keep ids that name their seats when a deal closes the gaps", () => {
    const h = harness({ bots: 3 });
    h.join(presence(0));
    h.state.seats[0] = emptySlot();
    compactSeats(h.state);
    const ids = h.state.seats.flatMap((s) => (s.bot ? [s.playerId] : []));
    expect(ids).toEqual(["bot_0", "bot_1"]);
    expect(
      new Set(h.state.seats.flatMap((s) => (s.playerId === null ? [] : [s.playerId]))).size,
    ).toBe(3);
  });
});
