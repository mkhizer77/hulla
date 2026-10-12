import {
  ErrorCodes,
  ruleError,
  sha256,
  stateFromSeed,
  utf8Encode,
  Xoshiro128StarStar,
} from "@hulla/shared-ts";

import { maybeSnapshot, type Emitter } from "./log";
import { activeSeats, emptySeries } from "./view";

import type {
  Deadline,
  EncodeContext,
  GameHost,
  Persistence,
  RoundRecord,
  RoundRuntime,
  TableConfig,
  TableState,
} from "./types";
import type { RoundResult } from "@hulla/rules-engine";
import type { Result, RuleError, SeatIndex } from "@hulla/shared-ts";

export interface RoundDeps<S, M, V, E, R> {
  readonly host: GameHost<S, M, V, E, R>;
  readonly config: TableConfig;
  readonly persist: Persistence;
}

/** Seconds a connected seat has per turn, or null when the timer is off (R43; off only in private). */
export function turnSeconds<S, M>(state: TableState<S, M>, config: TableConfig): number | null {
  const chosen = state.rules.settings?.turnTimerSeconds;
  if (chosen === undefined) return config.defaultTurnSeconds[state.mode];
  if (chosen === null)
    return state.mode === "private" ? null : config.defaultTurnSeconds[state.mode];
  return chosen;
}

/** A 256-bit round seed from four runtime UUIDs (ARCHITECTURE 8.7). */
export function roundSeed(uuids: readonly string[]): Uint8Array {
  return sha256(utf8Encode(uuids.join("")));
}

/**
 * Compact the active seats into engine order 0..n-1 (GDD R3, R4): the lobby may have gaps after
 * players left. Returns the new seat list order as old indices.
 */
export function compactSeats<S, M>(state: TableState<S, M>): SeatIndex[] {
  const order = activeSeats(state.seats);
  const kept = order.map((index) => state.seats[index]).filter((s) => s !== undefined);
  const empty = state.seats.length - kept.length;
  state.seats = [
    ...kept,
    ...Array.from({ length: empty }, () => ({
      playerId: null,
      displayName: null,
      sessionId: null,
      ready: false,
      connected: false,
      autoPlay: false,
      left: false,
      resumeToken: null,
      joinedTs: 0,
      disconnectedTs: null,
      timeouts: 0,
      rematch: null,
      quickChatTs: 0,
      emoteTs: 0,
      party: null,
    })),
  ];
  for (const presence of Object.values(state.presences)) {
    presence.seat = presence.seat === null ? null : order.indexOf(presence.seat);
    if (presence.seat === -1) presence.seat = null;
  }
  state.hostSeat = state.hostSeat === null ? null : order.indexOf(state.hostSeat);
  if (state.hostSeat === -1) state.hostSeat = kept.length > 0 ? 0 : null;
  return order;
}

/** Deal a new round for the active seats; the caller checked who may start and when. */
export function startRound<S, M, V, E, R>(
  deps: RoundDeps<S, M, V, E, R>,
  state: TableState<S, M>,
  uuids: readonly string[],
  now: number,
): Result<RoundRuntime<S, M>, RuleError> {
  const rules = deps.host.resolveRules(state.rules, state.mode);
  if (!rules.ok) return rules;
  const seatCount = activeSeats(state.seats).length;
  if (state.series?.points.length !== seatCount) {
    state.series = emptySeries(seatCount, state.hostSeat ?? 0);
  }
  const seed = roundSeed(uuids);
  const rng = Xoshiro128StarStar.fromState(stateFromSeed(seed));
  const created = deps.host.module.createRound(
    { rules: rules.value, seatCount, dealerSeat: state.series.nextDealerSeat, seed },
    rng,
  );
  if (!created.ok) return created;
  state.roundsStarted += 1;
  const round: RoundRuntime<S, M> = {
    roundId: `r_${String(state.roundsStarted)}`,
    index: state.roundsStarted,
    seedHex: Array.from(seed, (b) => b.toString(16).padStart(2, "0")).join(""),
    dealerSeat: state.series.nextDealerSeat,
    rngState: rng.state(),
    seatCount,
    state: created.value,
    phase: deps.host.module.currentPhase(created.value),
    moves: [],
    acks: {},
    snapshots: [],
    sinceSnapshot: 0,
    deadline: null,
    startedTs: now,
    result: null,
  };
  return { ok: true, value: round };
}

export interface MoveOutcome {
  readonly ok: boolean;
  readonly error: RuleError | null;
}

/** Seats that may act now: every seat with a legal move (one for a turn, several in a window). */
function actingSeats<S, M, V, E, R>(
  host: GameHost<S, M, V, E, R>,
  state: S,
  seatCount: number,
): SeatIndex[] {
  const seats: SeatIndex[] = [];
  for (let seat = 0; seat < seatCount; seat += 1) {
    if (host.module.legalMoves(state, seat).length > 0) seats.push(seat);
  }
  return seats;
}

/** Arm the next deadline from the phase policy and tell the acting seats (ARCHITECTURE 8.4 step 8). */
export function armDeadline<S, M, V, E, R>(
  deps: RoundDeps<S, M, V, E, R>,
  state: TableState<S, M>,
  emit: Emitter,
  now: number,
  delayMs: number,
): void {
  const round = state.round;
  if (round === null) return;
  if (round.result !== null) return;
  const seats = actingSeats(deps.host, round.state, round.seatCount);
  if (seats.length === 0) {
    round.deadline = null;
    return;
  }
  const phase = deps.host.module.phases.find((p) => p.id === round.phase);
  const kind =
    phase?.deadline.kind === "simultaneousWindow" ? "simultaneousWindow" : "perSeatTimer";
  const seconds = turnSeconds(state, deps.config);
  const windowMs =
    seconds === null
      ? null
      : (kind === "simultaneousWindow" ? deps.config.openingSeconds : seconds) * 1000;
  const previous = round.deadline;
  const sameWindow =
    previous !== null && previous.kind === "simultaneousWindow" && kind === "simultaneousWindow";
  const notBeforeTs = sameWindow ? previous.notBeforeTs : now + delayMs;
  const deadline: Deadline = sameWindow
    ? { ...previous, seats }
    : {
        kind,
        seats,
        notBeforeTs,
        humanTs: windowMs === null ? null : notBeforeTs + windowMs,
        autoTs: notBeforeTs + deps.config.autoPlayDelayMs,
      };
  round.deadline = deadline;
  for (const seat of seats) {
    if (sameWindow && previous.seats.includes(seat)) continue;
    const slot = state.seats[seat];
    const deadlineTs = slot?.autoPlay === true ? deadline.autoTs : (deadline.humanTs ?? 0);
    emit.seat(
      seat,
      "turn_start",
      deps.host.turnStart(
        { roundId: round.roundId, state: round.state },
        seat,
        deadlineTs,
        kind === "simultaneousWindow",
      ),
    );
  }
}

function encodeAndLog<S, M, V, E, R>(
  deps: RoundDeps<S, M, V, E, R>,
  state: TableState<S, M>,
  emit: Emitter,
  ctx: EncodeContext<S>,
  events: readonly E[],
): { readonly ack: { type: string; payload: object } | null; readonly phaseChanged: boolean } {
  const round = state.round;
  if (round === null) return { ack: null, phaseChanged: false };
  const messages = deps.host.encodeEvents(ctx, events);
  let ack: { type: string; payload: object } | null = null;
  for (const message of messages) {
    if (message.seat === null) {
      emit.public(message.type, message.payload);
      ack ??= { type: message.type, payload: message.payload };
    } else {
      emit.seat(message.seat, message.type, message.payload);
    }
  }
  const phase = deps.host.module.currentPhase(round.state);
  const phaseChanged = phase !== round.phase;
  round.phase = phase;
  maybeSnapshot(state, (s) => deps.host.stateHash(s), deps.config.snapshotEvery, phaseChanged);
  return { ack, phaseChanged };
}

/** Publish the opening events of a freshly dealt round. */
export function announceRound<S, M, V, E, R>(
  deps: RoundDeps<S, M, V, E, R>,
  state: TableState<S, M>,
  emit: Emitter,
  now: number,
): void {
  const round = state.round;
  if (round === null) return;
  const events = deps.host.roundStartEvents(round.state);
  encodeAndLog(deps, state, emit, roundContext(round, null, null, null), events);
  maybeSnapshot(state, (s) => deps.host.stateHash(s), deps.config.snapshotEvery, true);
  armDeadline(deps, state, emit, now, deps.host.presentationDelayMs(events));
}

function roundContext<S, M>(
  round: RoundRuntime<S, M>,
  move: EncodeContext<S>["move"],
  result: RoundResult | null,
  series: EncodeContext<S>["series"],
): EncodeContext<S> {
  return {
    roundId: round.roundId,
    seedHex: round.seedHex,
    state: round.state,
    series,
    result,
    move,
  };
}

/**
 * Apply one move through the engine, log and broadcast its events, settle the round when it is
 * over and arm the next deadline. `clientSeq` is null for the auto-play policy.
 */
export function applyRoundMove<S, M, V, E, R>(
  deps: RoundDeps<S, M, V, E, R>,
  state: TableState<S, M>,
  emit: Emitter,
  seat: SeatIndex,
  move: M,
  clientSeq: number | null,
  now: number,
): MoveOutcome {
  const round = state.round;
  if (round === null) return { ok: false, error: ruleError(ErrorCodes.table.notInRound) };
  const rng = Xoshiro128StarStar.fromState(round.rngState);
  const applied = deps.host.module.applyMove(round.state, seat, move, rng);
  round.rngState = rng.state();
  if (!applied.ok) {
    if (clientSeq !== null) {
      const ack = {
        type: "move_rejected",
        payload: { roundId: round.roundId, clientSeq, error: applied.error },
      };
      round.acks[`${String(seat)}:${String(clientSeq)}`] = ack;
      emit.seat(seat, ack.type, ack.payload);
    }
    return { ok: false, error: applied.error };
  }
  round.state = applied.state;
  round.moves.push({ seat, move, auto: clientSeq === null, clientSeq, seq: state.seq + 1 });
  const over = deps.host.module.isRoundOver(round.state);
  let result: RoundResult | null = null;
  if (over) {
    result = deps.host.module.results(round.state);
    round.result = result;
    state.series = deps.host.module.seriesUpdate(
      state.series ?? emptySeries(round.seatCount, 0),
      result,
    );
  }
  const ctx = roundContext(
    round,
    { seat, clientSeq, auto: clientSeq === null },
    result,
    over ? state.series : null,
  );
  const { ack } = encodeAndLog(deps, state, emit, ctx, applied.events);
  if (clientSeq !== null && ack !== null) {
    round.acks[`${String(seat)}:${String(clientSeq)}`] = ack;
  }
  if (over) {
    maybeSnapshot(state, (s) => deps.host.stateHash(s), deps.config.snapshotEvery, true);
    round.deadline = null;
    deps.persist.roundRecord(roundRecord(deps, state, round, now, true));
  } else {
    armDeadline(deps, state, emit, now, deps.host.presentationDelayMs(applied.events));
  }
  return { ok: true, error: null };
}

export function roundRecord<S, M, V, E, R>(
  deps: RoundDeps<S, M, V, E, R>,
  state: TableState<S, M>,
  round: RoundRuntime<S, M>,
  now: number,
  completed: boolean,
): RoundRecord {
  return {
    tableId: state.tableId,
    roundId: round.roundId,
    gameId: state.gameId,
    rulesVersion: deps.host.module.version,
    rules: state.rules,
    mode: state.mode,
    dealerSeat: round.dealerSeat,
    seedHex: round.seedHex,
    players: state.seats.slice(0, round.seatCount).map((s) => s.playerId),
    moves: round.moves.map((m) => ({ seat: m.seat, move: m.move, auto: m.auto })),
    result: round.result,
    series: state.series,
    finalStateHash: deps.host.stateHash(round.state),
    highlights:
      deps.host.highlights?.(state.log, round.seatCount) ??
      Array.from({ length: round.seatCount }, () => ({})),
    startedTs: round.startedTs,
    endedTs: now,
    completed,
  };
}

/**
 * Fire deadlines whose time has come (ARCHITECTURE 8.3 matchLoop). Seats under auto-play act after
 * the auto-play delay; connected seats time out at the human deadline and, after three in a row,
 * fall under auto-play (R45). Returns the seats whose timeouts escalated to auto-play.
 */
export function fireDeadlines<S, M, V, E, R>(
  deps: RoundDeps<S, M, V, E, R>,
  state: TableState<S, M>,
  emit: Emitter,
  now: number,
): SeatIndex[] {
  const escalated: SeatIndex[] = [];
  for (let guard = 0; guard < state.seats.length * 2; guard += 1) {
    const round = state.round;
    if (round === null) break;
    if (round.result !== null || round.deadline === null) break;
    const deadline = round.deadline;
    if (now < deadline.notBeforeTs) break;
    const due = deadline.seats.find((seat) => {
      const slot = state.seats[seat];
      if (slot?.autoPlay === true) return now >= deadline.autoTs;
      return deadline.humanTs !== null && now >= deadline.humanTs;
    });
    if (due === undefined) break;
    const slot = state.seats[due];
    if (slot !== undefined && !slot.autoPlay) {
      slot.timeouts += 1;
      if (slot.timeouts >= 3) {
        slot.autoPlay = true;
        escalated.push(due);
      }
    }
    const move = deps.host.module.onDeadline(round.state, due);
    if (move === null) {
      round.deadline = { ...deadline, seats: deadline.seats.filter((s) => s !== due) };
      if (round.deadline.seats.length === 0) round.deadline = null;
      continue;
    }
    applyRoundMove(deps, state, emit, due, move, null, now);
  }
  return escalated;
}
