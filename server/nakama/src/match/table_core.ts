import { createChecks, OPCODES } from "@hulla/protocol/generated";
import {
  ErrorCodes,
  ruleError,
  tableId as makeTableId,
  type ErrorCode,
  type RuleError,
} from "@hulla/shared-ts";

import {
  canStart,
  changedRuleKeys,
  emptySlot,
  freeSlot,
  humansPresent,
  isHost,
  migrateHost,
  presencePayload,
  settleSeatsAfterRound,
} from "./lobby";
import { emitter, replayFor, type Emitter } from "./log";
import {
  announceRound,
  applyRoundMove,
  compactSeats,
  fireDeadlines,
  roundRecord,
  startRound,
} from "./round";
import {
  DEFAULT_TABLE_CONFIG,
  newBatch,
  type Batch,
  type CreateTableParams,
  type GameHost,
  type Persistence,
  type PresenceRecord,
  type TableCloseReason,
  type TableConfig,
  type TableIds,
  type TableState,
} from "./types";
import { activeSeats, displayNameOf, spectatorCount, tableStateFor } from "./view";

import type { RoundDeps } from "./round";
import type { Check, JoinTable, MessageEnvelope, RulesSelection } from "@hulla/protocol/generated";

const PROTOCOL_MAJOR = 1;

/** Read through a function so TypeScript does not narrow `phase` across the mutations above. */
function closing<S, M>(state: TableState<S, M>): boolean {
  return state.phase === "closing";
}

export interface PresenceInfo {
  readonly sessionId: string;
  readonly userId: string;
  readonly username: string;
}

export type JoinDecision =
  { readonly accept: true } | { readonly accept: false; readonly code: ErrorCode };

export interface TableCoreDeps {
  readonly ids: TableIds;
  readonly persist: Persistence;
  readonly config?: Partial<TableConfig>;
}

/**
 * The generic match handler behind every game (ARCHITECTURE 7.2, 8.3 to 8.6). Every operation
 * mutates the given state in place and returns what to send; the Nakama binding does the IO.
 */
export interface TableCore<S, M> {
  readonly config: TableConfig;
  create(params: CreateTableParams, now: number): TableState<S, M>;
  joinAttempt(
    state: TableState<S, M>,
    presence: PresenceInfo,
    metadata: Record<string, unknown>,
    now: number,
  ): JoinDecision;
  join(state: TableState<S, M>, presences: readonly PresenceInfo[], now: number): Batch;
  leave(state: TableState<S, M>, presences: readonly PresenceInfo[], now: number): Batch;
  /** One decoded socket message; `expectedType` is the type the opcode announced. */
  inbound(
    state: TableState<S, M>,
    sessionId: string,
    raw: unknown,
    now: number,
    expectedType?: string,
  ): Batch;
  tick(state: TableState<S, M>, now: number): Batch;
  close(state: TableState<S, M>, reason: TableCloseReason, now: number): Batch;
}

const opcodeTable = OPCODES as Record<
  string,
  { readonly direction: "c2s" | "s2c"; readonly game?: string } | undefined
>;

function errorPayload(error: RuleError, refSeq: number): object {
  return error.detail === undefined
    ? { code: error.code, refSeq }
    : { code: error.code, detail: error.detail, refSeq };
}

/** Join metadata arrives as strings from the socket; make the booleans real before validating. */
function normaliseMetadata(metadata: Record<string, unknown>): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(metadata)) {
    out[key] = key === "asSpectator" && typeof value === "string" ? value === "true" : value;
  }
  return out;
}

export function tableCore<S, M, V, E, R>(
  host: GameHost<S, M, V, E, R>,
  deps: TableCoreDeps,
): TableCore<S, M> {
  const config: TableConfig = { ...DEFAULT_TABLE_CONFIG, ...deps.config };
  const checks = createChecks();
  const round: RoundDeps<S, M, V, E, R> = { host, config, persist: deps.persist };

  const emitFor = (state: TableState<S, M>, now: number, batch: Batch = newBatch()): Emitter =>
    emitter(
      state,
      now,
      () => (state.round === null ? null : host.stateHash(state.round.state)),
      batch,
    );

  const broadcastTableState = (state: TableState<S, M>, emit: Emitter): void => {
    for (const presence of state.presences.values()) {
      emit.session(presence.sessionId, "table_state", tableStateFor(state, host, presence));
    }
  };

  const newToken = (): string =>
    deps.ids.uuid().replace(/-/g, "") + deps.ids.uuid().replace(/-/g, "");

  const closeTable = (
    state: TableState<S, M>,
    emit: Emitter,
    reason: TableCloseReason,
    now: number,
  ): void => {
    if (state.phase === "closing") return;
    if (state.round !== null && state.round.result === null) {
      deps.persist.roundRecord(roundRecord(round, state, state.round, now, false));
    }
    state.phase = "closing";
    state.closeReason = reason;
    state.rematchDeadlineTs = null;
    emit.public("table_closed", { reason });
    emit.batch.end = true;
  };

  const hostChanged = (
    state: TableState<S, M>,
    emit: Emitter,
    reason: "transfer" | "host_left",
  ): void => {
    if (state.hostSeat !== null) emit.public("host_changed", { seat: state.hostSeat, reason });
  };

  const dealRound = (
    state: TableState<S, M>,
    emit: Emitter,
    now: number,
    requester: string | null,
  ): void => {
    compactSeats(state);
    const started = startRound(
      round,
      state,
      [deps.ids.uuid(), deps.ids.uuid(), deps.ids.uuid(), deps.ids.uuid()],
      now,
    );
    if (!started.ok) {
      if (requester !== null) emit.session(requester, "error", errorPayload(started.error, 0));
      return;
    }
    state.round = started.value;
    state.phase = "inRound";
    state.rematchDeadlineTs = null;
    state.log = [];
    for (const slot of state.seats) {
      slot.rematch = null;
      slot.timeouts = 0;
    }
    broadcastTableState(state, emit);
    announceRound(round, state, emit, now);
  };

  const finishRound = (state: TableState<S, M>, emit: Emitter, now: number): void => {
    if (state.phase !== "inRound") return;
    const ended = state.round;
    if (ended === null) return;
    if (ended.result === null) return;
    state.phase = "roundEnded";
    state.rematchDeadlineTs = now + config.rematchWindowMs;
    const hostBefore = state.hostSeat;
    settleSeatsAfterRound(state);
    if (state.hostSeat !== hostBefore) hostChanged(state, emit, "host_left");
    if (state.mode !== "private" && humansPresent(state) < 2) {
      closeTable(state, emit, "dissolved", now);
      return;
    }
    broadcastTableState(state, emit);
  };

  /** The rematch vote closed: continue with the yes votes or return to the lobby (R52, OQ-005). */
  const resolveRematch = (state: TableState<S, M>, emit: Emitter, now: number): void => {
    if (state.phase !== "roundEnded") return;
    state.rematchDeadlineTs = null;
    const yes = activeSeats(state.seats).filter((seat) => state.seats[seat]?.rematch === true);
    if (yes.length >= host.module.seats.min) {
      for (const seat of activeSeats(state.seats)) {
        const slot = state.seats[seat];
        if (slot === undefined) continue;
        if (slot.rematch === true) {
          slot.ready = true;
          continue;
        }
        const presence = slot.sessionId === null ? undefined : state.presences.get(slot.sessionId);
        freeSlot(state, seat);
        if (presence !== undefined) {
          presence.seat = null;
          emit.public("presence", presencePayload(presence, null, "spectating"));
        }
      }
      if (state.hostSeat !== null && state.seats[state.hostSeat]?.playerId === null) {
        migrateHost(state);
        hostChanged(state, emit, "host_left");
      }
      state.phase = "lobby";
      state.round = null;
      dealRound(state, emit, now, null);
      return;
    }
    if (state.mode === "private") {
      state.phase = "lobby";
      state.round = null;
      for (const slot of state.seats) {
        slot.ready = false;
        slot.rematch = null;
      }
      broadcastTableState(state, emit);
      return;
    }
    closeTable(state, emit, "dissolved", now);
  };

  const sendError = (emit: Emitter, sessionId: string, error: RuleError, refSeq: number): void => {
    emit.session(sessionId, "error", errorPayload(error, refSeq));
  };

  const handleMove = (
    state: TableState<S, M>,
    presence: PresenceRecord,
    type: string,
    payload: Record<string, unknown>,
    check: Check | undefined,
    refSeq: number,
    emit: Emitter,
    now: number,
  ): void => {
    const current = state.round;
    if (state.phase !== "inRound" || current === null) {
      sendError(emit, presence.sessionId, ruleError(ErrorCodes.table.notInRound), refSeq);
      return;
    }
    const seat = presence.seat;
    const slot = seat === null ? undefined : state.seats[seat];
    if (seat === null || slot === undefined || slot.left) {
      sendError(emit, presence.sessionId, ruleError(ErrorCodes.table.notAuthorised), refSeq);
      return;
    }
    const clientSeq = payload["clientSeq"];
    if (typeof clientSeq !== "number" || !Number.isInteger(clientSeq) || clientSeq < 0) {
      sendError(
        emit,
        presence.sessionId,
        ruleError(ErrorCodes.table.invalidPayload, { reason: "client_seq" }),
        refSeq,
      );
      return;
    }
    if (payload["roundId"] !== current.roundId) {
      emit.seat(seat, "move_rejected", {
        roundId: current.roundId,
        clientSeq,
        error: ruleError(ErrorCodes.table.staleSequence, { reason: "round_id" }),
      });
      return;
    }
    const ack = current.acks[`${String(seat)}:${String(clientSeq)}`];
    if (ack !== undefined) {
      emit.session(presence.sessionId, ack.type, ack.payload);
      return;
    }
    const decoded = host.decodeMove(type, payload, check);
    if (!decoded.ok) {
      const rejection = {
        type: "move_rejected",
        payload: { roundId: current.roundId, clientSeq, error: decoded.error },
      };
      current.acks[`${String(seat)}:${String(clientSeq)}`] = rejection;
      emit.seat(seat, rejection.type, rejection.payload);
      return;
    }
    applyRoundMove(round, state, emit, seat, decoded.value, clientSeq, now);
    finishRound(state, emit, now);
  };

  const handleShell = (
    state: TableState<S, M>,
    presence: PresenceRecord,
    type: string,
    payload: Record<string, unknown>,
    refSeq: number,
    emit: Emitter,
    now: number,
  ): void => {
    const seat = presence.seat;
    const slot = seat === null ? undefined : state.seats[seat];
    const reject = (error: RuleError): void => {
      sendError(emit, presence.sessionId, error, refSeq);
    };
    const inLobby = state.phase === "lobby" || state.phase === "roundEnded";
    switch (type) {
      case "ping": {
        emit.session(presence.sessionId, "pong", { sentTs: payload["sentTs"], serverTs: now });
        return;
      }
      case "request_snapshot": {
        const sinceSeq = typeof payload["sinceSeq"] === "number" ? payload["sinceSeq"] : 0;
        emit.session(presence.sessionId, "snapshot", {
          state: tableStateFor(state, host, presence),
          events: replayFor(state, seat, sinceSeq),
        });
        return;
      }
      case "leave_table": {
        state.presences.delete(presence.sessionId);
        emit.batch.kicks.push(presence.sessionId);
        if (seat === null || slot === undefined) {
          emit.public("presence", presencePayload(presence, null, "left"));
          return;
        }
        slot.sessionId = null;
        slot.connected = false;
        if (state.phase === "inRound") {
          slot.left = true;
          slot.autoPlay = true;
        } else {
          freeSlot(state, seat);
        }
        emit.public("presence", presencePayload(presence, slot.displayName, "left"));
        if (isHost(state, seat) && state.phase !== "inRound") {
          migrateHost(state);
          hostChanged(state, emit, "host_left");
        }
        if (state.presences.size === 0) state.emptySinceTs = now;
        broadcastTableState(state, emit);
        return;
      }
      case "ready": {
        if (slot === undefined) {
          reject(ruleError(ErrorCodes.table.notAuthorised));
          return;
        }
        if (state.phase !== "lobby") {
          reject(ruleError(ErrorCodes.table.locked, { reason: "phase" }));
          return;
        }
        slot.ready = payload["ready"] === true;
        broadcastTableState(state, emit);
        return;
      }
      case "set_rules": {
        if (!isHost(state, seat)) {
          reject(ruleError(ErrorCodes.table.notHost));
          return;
        }
        if (!inLobby) {
          reject(ruleError(ErrorCodes.table.locked, { reason: "phase" }));
          return;
        }
        const rules = payload["rules"] as RulesSelection;
        if (rules.gameId !== state.gameId) {
          reject(ruleError(ErrorCodes.table.invalidPayload, { reason: "game_id" }));
          return;
        }
        const resolved = host.resolveRules(rules, state.mode);
        if (!resolved.ok) {
          reject(resolved.error);
          return;
        }
        const changedKeys = changedRuleKeys(state.rules, rules);
        state.rules = rules;
        emit.public("rules_changed", { rules, changedBy: seat, changedKeys });
        broadcastTableState(state, emit);
        return;
      }
      case "kick": {
        if (!isHost(state, seat)) {
          reject(ruleError(ErrorCodes.table.notHost));
          return;
        }
        const target = payload["seat"];
        const victim = typeof target === "number" ? state.seats[target] : undefined;
        if (typeof target !== "number" || victim === undefined) {
          reject(ruleError(ErrorCodes.table.invalidPayload, { reason: "seat" }));
          return;
        }
        if (victim.playerId === null || target === seat) {
          reject(ruleError(ErrorCodes.table.invalidPayload, { reason: "seat" }));
          return;
        }
        state.kicked.push(victim.playerId);
        const victimPresence =
          victim.sessionId === null ? undefined : state.presences.get(victim.sessionId);
        if (victimPresence !== undefined) {
          state.presences.delete(victimPresence.sessionId);
          emit.batch.kicks.push(victimPresence.sessionId);
        }
        const kickedPresence: PresenceRecord = {
          sessionId: victim.sessionId ?? "",
          userId: victim.playerId,
          username: victim.displayName ?? "",
          seat: target,
        };
        emit.public("presence", presencePayload(kickedPresence, victim.displayName, "kicked"));
        victim.sessionId = null;
        victim.connected = false;
        if (state.phase === "inRound") {
          victim.left = true;
          victim.autoPlay = true;
        } else {
          freeSlot(state, target);
        }
        broadcastTableState(state, emit);
        return;
      }
      case "transfer_host": {
        if (!isHost(state, seat)) {
          reject(ruleError(ErrorCodes.table.notHost));
          return;
        }
        const target = payload["seat"];
        const next = typeof target === "number" ? state.seats[target] : undefined;
        if (typeof target !== "number" || next === undefined) {
          reject(ruleError(ErrorCodes.table.invalidPayload, { reason: "seat" }));
          return;
        }
        if (next.playerId === null || !next.connected) {
          reject(ruleError(ErrorCodes.table.invalidPayload, { reason: "seat" }));
          return;
        }
        state.hostSeat = target;
        hostChanged(state, emit, "transfer");
        broadcastTableState(state, emit);
        return;
      }
      case "start_round": {
        if (!isHost(state, seat)) {
          reject(ruleError(ErrorCodes.table.notHost));
          return;
        }
        if (!inLobby) {
          reject(ruleError(ErrorCodes.table.locked, { reason: "phase" }));
          return;
        }
        const ready = canStart(state, host);
        if (!ready.ok) {
          reject(ready.error);
          return;
        }
        if (state.phase === "roundEnded") {
          state.phase = "lobby";
          state.round = null;
          state.rematchDeadlineTs = null;
        }
        dealRound(state, emit, now, presence.sessionId);
        return;
      }
      case "vote_rematch": {
        if (slot === undefined) {
          reject(ruleError(ErrorCodes.table.notAuthorised));
          return;
        }
        if (state.phase !== "roundEnded") {
          reject(ruleError(ErrorCodes.table.notInRound));
          return;
        }
        slot.rematch = payload["yes"] === true;
        broadcastTableState(state, emit);
        const pending = activeSeats(state.seats).filter((s) => state.seats[s]?.rematch === null);
        if (pending.length === 0) resolveRematch(state, emit, now);
        return;
      }
      default:
        reject(ruleError(ErrorCodes.table.invalidPayload, { reason: "unsupported_type", type }));
    }
  };

  return {
    config,

    create(params, now) {
      return {
        tableId: makeTableId(params.code),
        code: params.code,
        gameId: params.gameId,
        mode: params.mode,
        phase: "lobby",
        lockedByHost: false,
        hostSeat: null,
        rules: params.rules ?? { gameId: params.gameId },
        seats: Array.from({ length: host.module.seats.max }, emptySlot),
        presences: new Map(),
        admissions: new Map(),
        kicked: [],
        series: null,
        round: null,
        roundsStarted: 0,
        seq: 0,
        log: [],
        rematchDeadlineTs: null,
        createdTs: now,
        emptySinceTs: now,
        closeReason: null,
      };
    },

    joinAttempt(state, presence, metadata) {
      if (state.phase === "closing") return { accept: false, code: ErrorCodes.table.notFound };
      if (state.kicked.includes(presence.userId)) {
        return { accept: false, code: ErrorCodes.table.banned };
      }
      const meta = normaliseMetadata(metadata);
      if (!checks.payload["join_table"]?.(meta)) {
        return { accept: false, code: ErrorCodes.table.invalidPayload };
      }
      const parsed = { data: meta as JoinTable };
      const own = state.seats.findIndex((slot) => slot.playerId === presence.userId);
      if (own !== -1) {
        state.admissions.set(presence.sessionId, { kind: "seat", seat: own, reattach: true });
        return { accept: true };
      }
      const pending = [...state.admissions.values()];
      const reserved = new Set(pending.flatMap((a) => (a.kind === "seat" ? [a.seat] : [])));
      const free = state.seats.findIndex(
        (slot, index) => slot.playerId === null && !reserved.has(index),
      );
      const wantsSeat =
        parsed.data.asSpectator !== true && state.phase !== "inRound" && !state.lockedByHost;
      if (wantsSeat && free !== -1) {
        state.admissions.set(presence.sessionId, { kind: "seat", seat: free, reattach: false });
        return { accept: true };
      }
      const spectatorsAllowed = state.rules.settings?.spectators !== false;
      const spectators =
        spectatorCount(state.presences) + pending.filter((a) => a.kind === "spectator").length;
      if (spectatorsAllowed && spectators < config.maxSpectators) {
        state.admissions.set(presence.sessionId, { kind: "spectator" });
        return { accept: true };
      }
      return { accept: false, code: free === -1 ? ErrorCodes.table.full : ErrorCodes.table.locked };
    },

    join(state, presences, now) {
      const emit = emitFor(state, now);
      for (const info of presences) {
        const admission = state.admissions.get(info.sessionId) ?? { kind: "spectator" };
        state.admissions.delete(info.sessionId);
        const record: PresenceRecord = { ...info, seat: null };
        const displayName = displayNameOf(info.username);
        let status: "joined" | "reconnected" | "spectating" = "spectating";
        if (admission.kind === "seat") {
          const slot = state.seats[admission.seat];
          if (slot === undefined) continue;
          if (admission.reattach) {
            const previous =
              slot.sessionId === null ? undefined : state.presences.get(slot.sessionId);
            if (previous !== undefined && previous.sessionId !== info.sessionId) {
              state.presences.delete(previous.sessionId);
              emit.batch.kicks.push(previous.sessionId);
            }
            status = "reconnected";
          } else {
            slot.playerId = info.userId;
            slot.displayName = displayName;
            slot.joinedTs = now;
            slot.ready = false;
            slot.left = false;
            status = "joined";
            state.hostSeat ??= admission.seat;
          }
          slot.sessionId = info.sessionId;
          slot.connected = true;
          slot.disconnectedTs = null;
          slot.autoPlay = false;
          slot.timeouts = 0;
          slot.resumeToken = newToken();
          record.seat = admission.seat;
        }
        state.presences.set(info.sessionId, record);
        state.emptySinceTs = null;
        emit.session(info.sessionId, "table_state", tableStateFor(state, host, record));
        emit.public("presence", presencePayload(record, displayName, status));
        const current = state.round;
        if (
          status === "reconnected" &&
          current?.deadline !== null &&
          current !== null &&
          record.seat !== null
        ) {
          const deadline = current.deadline;
          if (deadline.seats.includes(record.seat)) {
            emit.seat(
              record.seat,
              "turn_start",
              host.turnStart(
                { roundId: current.roundId, state: current.state },
                record.seat,
                deadline.humanTs ?? 0,
                deadline.kind === "simultaneousWindow",
              ),
            );
          }
        }
      }
      return emit.batch;
    },

    leave(state, presences, now) {
      const emit = emitFor(state, now);
      for (const info of presences) {
        const record = state.presences.get(info.sessionId);
        if (record === undefined) continue;
        state.presences.delete(info.sessionId);
        const seat = record.seat;
        const slot = seat === null ? undefined : state.seats[seat];
        if (seat === null || slot === undefined) {
          emit.public("presence", presencePayload({ ...record, seat: null }, null, "left"));
          continue;
        }
        if (slot.sessionId !== info.sessionId) {
          emit.public("presence", presencePayload({ ...record, seat: null }, null, "left"));
          continue;
        }
        slot.sessionId = null;
        slot.connected = false;
        slot.disconnectedTs = now;
        if (state.phase === "inRound") {
          slot.autoPlay = true;
          const current = state.round;
          if (current !== null && current.deadline?.seats.includes(seat) === true) {
            current.deadline = {
              ...current.deadline,
              autoTs: Math.max(current.deadline.autoTs, now + config.autoPlayDelayMs),
            };
          }
          emit.public(
            "presence",
            presencePayload(
              record,
              slot.displayName,
              "disconnected",
              now + config.reconnectWindowMs,
            ),
          );
        } else {
          const wasHost = isHost(state, seat);
          freeSlot(state, seat);
          emit.public("presence", presencePayload(record, slot.displayName, "left"));
          if (wasHost) {
            migrateHost(state);
            hostChanged(state, emit, "host_left");
          }
          broadcastTableState(state, emit);
        }
      }
      if (state.presences.size === 0) state.emptySinceTs = now;
      return emit.batch;
    },

    inbound(state, sessionId, raw, now, expectedType) {
      const emit = emitFor(state, now);
      const presence = state.presences.get(sessionId);
      if (presence === undefined || state.phase === "closing") return emit.batch;
      let value: unknown = raw;
      if (typeof raw === "string") {
        try {
          value = JSON.parse(raw);
        } catch {
          sendError(
            emit,
            sessionId,
            ruleError(ErrorCodes.table.invalidPayload, { reason: "json" }),
            0,
          );
          return emit.batch;
        }
      }
      if (!checks.envelope(value)) {
        sendError(
          emit,
          sessionId,
          ruleError(ErrorCodes.table.invalidPayload, { reason: "envelope" }),
          0,
        );
        return emit.batch;
      }
      const envelope = value as MessageEnvelope;
      const refSeq = envelope.seq;
      if ((envelope.v as number) !== PROTOCOL_MAJOR) {
        sendError(
          emit,
          sessionId,
          ruleError(ErrorCodes.table.unsupportedVersion, { v: envelope.v }),
          refSeq,
        );
        return emit.batch;
      }
      if (envelope.tableId !== state.tableId) {
        sendError(
          emit,
          sessionId,
          ruleError(ErrorCodes.table.invalidPayload, { reason: "table_id" }),
          refSeq,
        );
        return emit.batch;
      }
      if (expectedType !== undefined && expectedType !== envelope.type) {
        sendError(
          emit,
          sessionId,
          ruleError(ErrorCodes.table.invalidPayload, { reason: "opcode" }),
          refSeq,
        );
        return emit.batch;
      }
      const type = envelope.type;
      const payload = envelope.payload as Record<string, unknown>;
      const slot = presence.seat === null ? undefined : state.seats[presence.seat];
      if (
        slot !== undefined &&
        slot.connected &&
        !slot.left &&
        type !== "ping" &&
        type !== "request_snapshot"
      ) {
        slot.timeouts = 0;
        if (slot.autoPlay) {
          slot.autoPlay = false;
          emit.public("presence", presencePayload(presence, slot.displayName, "reconnected"));
        }
      }
      const entry = opcodeTable[type];
      const check = checks.payload[type];
      if (host.isMoveType(type)) {
        handleMove(state, presence, type, payload, check, refSeq, emit, now);
        return emit.batch;
      }
      if (
        entry === undefined ||
        check === undefined ||
        entry.direction !== "c2s" ||
        entry.game !== undefined
      ) {
        sendError(
          emit,
          sessionId,
          ruleError(ErrorCodes.table.invalidPayload, { reason: "unsupported_type", type }),
          refSeq,
        );
        return emit.batch;
      }
      if (!check(payload)) {
        sendError(
          emit,
          sessionId,
          ruleError(ErrorCodes.table.invalidPayload, { reason: "schema", type }),
          refSeq,
        );
        return emit.batch;
      }
      handleShell(state, presence, type, payload, refSeq, emit, now);
      return emit.batch;
    },

    tick(state, now) {
      const emit = emitFor(state, now);
      if (state.phase === "closing") return emit.batch;
      for (const [index, slot] of state.seats.entries()) {
        if (slot.playerId === null || slot.connected || slot.disconnectedTs === null) continue;
        if (now - slot.disconnectedTs < config.reconnectWindowMs) continue;
        slot.disconnectedTs = null;
        if (state.phase === "inRound") {
          if (state.mode !== "private") slot.left = true;
          if (isHost(state, index) && activeSeats(state.seats).length > 0) {
            migrateHost(state);
            hostChanged(state, emit, "host_left");
          }
        }
      }
      if (state.phase === "inRound") {
        const escalated = fireDeadlines(round, state, emit, now);
        for (const seat of escalated) {
          const slot = state.seats[seat];
          const sessionId = slot?.sessionId ?? null;
          const presence = sessionId === null ? undefined : state.presences.get(sessionId);
          if (presence !== undefined && slot !== undefined) {
            emit.public("presence", presencePayload(presence, slot.displayName, "auto_play"));
          }
        }
        finishRound(state, emit, now);
      }
      if (
        state.phase === "roundEnded" &&
        state.rematchDeadlineTs !== null &&
        now >= state.rematchDeadlineTs
      ) {
        resolveRematch(state, emit, now);
      }
      if (
        !closing(state) &&
        state.emptySinceTs !== null &&
        state.presences.size === 0 &&
        now - state.emptySinceTs >= config.emptyTtlMs[state.mode]
      ) {
        closeTable(state, emit, "empty", now);
      }
      return emit.batch;
    },

    close(state, reason, now) {
      const emit = emitFor(state, now);
      closeTable(state, emit, reason, now);
      return emit.batch;
    },
  };
}
