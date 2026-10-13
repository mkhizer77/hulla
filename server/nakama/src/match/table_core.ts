import { createChecks, OPCODES } from "@hulla/protocol/generated";
import {
  ErrorCodes,
  ruleError,
  tableId as makeTableId,
  type ErrorCode,
  type RuleError,
  type SeatIndex,
} from "@hulla/shared-ts";

import { BUNDLED_CHAT, type ChatContent } from "../content/chat";

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
  turnSeconds,
} from "./round";
import {
  DEFAULT_TABLE_CONFIG,
  newBatch,
  without,
  type Batch,
  type CreateTableParams,
  type GameHost,
  type Persistence,
  type PresenceRecord,
  type SeatSlot,
  type TableCloseReason,
  type TableConfig,
  type TableIds,
  type TableState,
} from "./types";
import { activeSeats, displayNameOf, freeSeat, spectatorCount, tableStateFor } from "./view";

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
  readonly node?: string;
}

export type JoinDecision =
  { readonly accept: true } | { readonly accept: false; readonly code: ErrorCode };

export interface TableCoreDeps {
  readonly ids: TableIds;
  readonly persist: Persistence;
  readonly config?: Partial<TableConfig>;
  /** Phrase and emote ids the table accepts; the bundled content unless a test says otherwise. */
  readonly chat?: ChatContent;
  /** Spectators are friends of someone seated (GDD 7.7); absent, anyone may watch (tests). */
  readonly social?: SocialGate;
  /** The names players chose (profile, account); absent, the Nakama username shows (tests). */
  readonly names?: NameSource;
}

export interface NameSource {
  displayNameOf(userId: string): string | null;
}

export interface SocialGate {
  mayWatch(userId: string, seatedIds: readonly string[]): boolean;
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
const JOIN_KEYS = ["code", "resumeToken", "asSpectator", "clientVersion"] as const;

/** Only the join fields are validated: a matchmaker token join may carry ticket properties too. */
function normaliseMetadata(metadata: Record<string, unknown>): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const key of JOIN_KEYS) {
    const value = metadata[key];
    if (value === undefined) continue;
    out[key] = key === "asSpectator" && typeof value === "string" ? value === "true" : value;
  }
  return out;
}

export function tableCore<S, M, V, E, R>(
  host: GameHost<S, M, V, E, R>,
  deps: TableCoreDeps,
): TableCore<S, M> {
  const config: TableConfig = { ...DEFAULT_TABLE_CONFIG, ...deps.config };
  const chat = deps.chat ?? BUNDLED_CHAT;
  /** The chosen name when there is one (2 to 20 characters), else the username (GDD 17.2). */
  const nameOf = (userId: string, username: string): string | null => {
    const chosen = deps.names?.displayNameOf(userId) ?? null;
    return (chosen === null ? null : displayNameOf(chosen)) ?? displayNameOf(username);
  };
  const checks = createChecks();
  const round: RoundDeps<S, M, V, E, R> = { host, config, persist: deps.persist };

  const emitFor = (state: TableState<S, M>, now: number, batch: Batch = newBatch()): Emitter =>
    emitter(
      state,
      now,
      () => (state.round === null ? null : host.stateHash(state.round.state)),
      batch,
    );

  /** Everyone expected has arrived: count down to the first deal (GDD 9.5). */
  const armAutoStart = (state: TableState<S, M>, now: number): void => {
    const auto = state.autoStart;
    if (auto === null) return;
    if (auto.startsAtTs !== null || state.phase !== "lobby") return;
    if (activeSeats(state.seats).length >= auto.expected) {
      auto.startsAtTs = now + config.autoStartCountdownMs;
    }
  };

  const broadcastTableState = (state: TableState<S, M>, emit: Emitter): void => {
    for (const presence of Object.values(state.presences)) {
      emit.session(presence.sessionId, "table_state", tableStateFor(state, host, presence));
    }
  };

  const normalised = (selection: RulesSelection): RulesSelection =>
    host.normaliseRules?.(selection) ?? selection;

  const newToken = (): string =>
    deps.ids.uuid().replace(/-/g, "") + deps.ids.uuid().replace(/-/g, "");

  const occupySlot = (
    slot: SeatSlot,
    playerId: string,
    displayName: string | null,
    now: number,
    party: string | null,
  ): void => {
    slot.playerId = playerId;
    slot.displayName = displayName;
    slot.joinedTs = now;
    slot.party = party;
    slot.ready = false;
    slot.left = false;
  };

  const attachSession = (slot: SeatSlot, sessionId: string): void => {
    slot.sessionId = sessionId;
    // A player who left mid-round and comes back before it ends takes the seat over from auto-play.
    slot.left = false;
    slot.connected = true;
    slot.disconnectedTs = null;
    slot.autoPlay = false;
    slot.timeouts = 0;
    slot.resumeToken = newToken();
  };

  /**
   * A seat back within the window gets a full turn timer if it is its turn (GDD 9.8), so the
   * policy does not play for it the moment it returns.
   */
  const freshTurnTimer = (state: TableState<S, M>, seat: SeatIndex, now: number): void => {
    const round = state.round;
    const deadline = round?.deadline ?? null;
    if (round === null || deadline === null) return;
    if (deadline.kind !== "perSeatTimer" || deadline.humanTs === null) return;
    if (!deadline.seats.includes(seat)) return;
    const seconds = turnSeconds(state, config);
    if (seconds === null) return;
    round.deadline = { ...deadline, humanTs: Math.max(deadline.humanTs, now + seconds * 1000) };
  };

  /** Moves a spectator into a free seat; the caller broadcasts the table afterwards. */
  const seatSpectator = (
    state: TableState<S, M>,
    record: PresenceRecord,
    seat: SeatIndex,
    emit: Emitter,
    now: number,
  ): void => {
    const slot = state.seats[seat];
    if (slot === undefined) return;
    occupySlot(
      slot,
      record.userId,
      record.displayName ?? displayNameOf(record.username),
      now,
      null,
    );
    attachSession(slot, record.sessionId);
    const seated: PresenceRecord = { ...record, seat, seatNext: false };
    state.presences[record.sessionId] = seated;
    state.hostSeat ??= seat;
    emit.public("presence", presencePayload(seated, slot.displayName, "joined"));
  };

  /** Spectators who asked for a seat take the free ones in arrival order (GDD 7.6, 9.10). */
  const seatWaitingSpectators = (state: TableState<S, M>, emit: Emitter, now: number): void => {
    for (const record of Object.values(state.presences)) {
      if (record.seat !== null || record.seatNext !== true) continue;
      const free = freeSeat(state.seats);
      if (free === null) return;
      seatSpectator(state, record, free, emit, now);
    }
  };

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
    seatWaitingSpectators(state, emit, now);
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
        const presence = slot.sessionId === null ? undefined : state.presences[slot.sessionId];
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
        state.presences = without(state.presences, presence.sessionId);
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
        if (Object.keys(state.presences).length === 0) state.emptySinceTs = now;
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
        const input = payload["rules"] as RulesSelection;
        const rules = host.normaliseRules?.(input) ?? input;
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
      case "seat_spectator": {
        if (!isHost(state, seat)) {
          reject(ruleError(ErrorCodes.table.notHost));
          return;
        }
        const target = payload["userId"];
        const watcher = Object.values(state.presences).find(
          (p) => p.seat === null && p.userId === target,
        );
        if (typeof target !== "string" || watcher === undefined) {
          reject(ruleError(ErrorCodes.table.invalidPayload, { reason: "user_id" }));
          return;
        }
        if (state.phase === "inRound") {
          state.presences[watcher.sessionId] = { ...watcher, seatNext: true };
          broadcastTableState(state, emit);
          return;
        }
        const free = freeSeat(state.seats);
        if (free === null) {
          reject(ruleError(ErrorCodes.table.full));
          return;
        }
        seatSpectator(state, watcher, free, emit, now);
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
          victim.sessionId === null ? undefined : state.presences[victim.sessionId];
        if (victimPresence !== undefined) {
          state.presences = without(state.presences, victimPresence.sessionId);
          emit.batch.kicks.push(victimPresence.sessionId);
        }
        const kickedPresence: PresenceRecord = {
          sessionId: victim.sessionId ?? "",
          userId: victim.playerId,
          username: victim.displayName ?? "",
          node: "",
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
      case "quick_chat": {
        if (seat === null || slot === undefined) {
          reject(ruleError(ErrorCodes.table.notAuthorised));
          return;
        }
        const pack = typeof payload["pack"] === "string" ? payload["pack"] : chat.defaultPack;
        const phraseId = String(payload["phraseId"]);
        if (!chat.isPhrase(pack, phraseId)) {
          reject(ruleError(ErrorCodes.content.packUnknown, { pack, phraseId }));
          return;
        }
        if (now - slot.quickChatTs < config.quickChatCooldownMs) {
          reject(ruleError(ErrorCodes.network.rateLimited, { kind: "quick_chat" }));
          return;
        }
        slot.quickChatTs = now;
        emit.public("quick_chat_sent", { seat, phraseId, pack });
        return;
      }
      case "emote": {
        if (seat === null || slot === undefined) {
          reject(ruleError(ErrorCodes.table.notAuthorised));
          return;
        }
        const emoteId = String(payload["emoteId"]);
        if (!chat.isEmote(emoteId)) {
          reject(ruleError(ErrorCodes.content.packUnknown, { emoteId }));
          return;
        }
        if (now - slot.emoteTs < config.emoteCooldownMs) {
          reject(ruleError(ErrorCodes.network.rateLimited, { kind: "emote" }));
          return;
        }
        const targetSeat = payload["targetSeat"];
        const targetSlot = typeof targetSeat === "number" ? state.seats[targetSeat] : undefined;
        const target =
          typeof targetSeat === "number" && targetSlot?.playerId != null ? targetSeat : null;
        slot.emoteTs = now;
        emit.public(
          "emote_sent",
          target === null ? { seat, emoteId } : { seat, emoteId, targetSeat: target },
        );
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
        rules: normalised(params.rules ?? { gameId: params.gameId }),
        seats: Array.from({ length: host.module.seats.max }, emptySlot),
        presences: {},
        admissions: {},
        kicked: [],
        series: null,
        round: null,
        roundsStarted: 0,
        seq: 0,
        log: [],
        rematchDeadlineTs: null,
        autoStart:
          params.autoStart === undefined
            ? null
            : {
                expected: params.autoStart.expected,
                joinDeadlineTs: now + config.autoStartJoinWindowMs,
                startsAtTs: null,
                parties: Object.fromEntries(
                  (params.parties ?? []).flatMap((party) =>
                    party.members.map((m) => [m, party.id]),
                  ),
                ),
              },
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
      if (parsed.data.code !== undefined && parsed.data.code !== state.code) {
        return { accept: false, code: ErrorCodes.table.invalidCode };
      }
      const own = state.seats.findIndex((slot) => slot.playerId === presence.userId);
      if (own !== -1) {
        // One active table session per account (GDD 10.2): a live session is only replaced by a
        // client that proves it held the seat with the resume token; another device is refused.
        const slot = state.seats[own];
        const live =
          slot?.sessionId !== null &&
          slot?.sessionId !== undefined &&
          state.presences[slot.sessionId] !== undefined;
        if (live && parsed.data.resumeToken !== slot.resumeToken) {
          return { accept: false, code: ErrorCodes.auth.deviceLimit };
        }
        state.admissions[presence.sessionId] = { kind: "seat", seat: own, reattach: true };
        return { accept: true };
      }
      const pending = Object.values(state.admissions);
      const reserved = new Set(pending.flatMap((a) => (a.kind === "seat" ? [a.seat] : [])));
      const free = state.seats.findIndex(
        (slot, index) => slot.playerId === null && !reserved.has(index),
      );
      const wantsSeat =
        parsed.data.asSpectator !== true && state.phase !== "inRound" && !state.lockedByHost;
      if (wantsSeat && free !== -1) {
        state.admissions[presence.sessionId] = { kind: "seat", seat: free, reattach: false };
        return { accept: true };
      }
      const spectatorsAllowed = state.rules.settings?.spectators !== false;
      const spectators =
        spectatorCount(state.presences) + pending.filter((a) => a.kind === "spectator").length;
      if (!spectatorsAllowed || spectators >= config.maxSpectators) {
        return {
          accept: false,
          code: free === -1 ? ErrorCodes.table.full : ErrorCodes.table.locked,
        };
      }
      const seatedIds = state.seats.flatMap((s) => (s.playerId === null ? [] : [s.playerId]));
      if (deps.social !== undefined && !deps.social.mayWatch(presence.userId, seatedIds)) {
        return { accept: false, code: ErrorCodes.social.notFriends };
      }
      // A late arrival who wanted a seat takes the next free one when the round ends (GDD 7.6).
      const late = parsed.data.asSpectator !== true && state.phase === "inRound";
      state.admissions[presence.sessionId] = late
        ? { kind: "spectator", seatNext: true }
        : { kind: "spectator" };
      return { accept: true };
    },

    join(state, presences, now) {
      const emit = emitFor(state, now);
      for (const info of presences) {
        const admission = state.admissions[info.sessionId] ?? { kind: "spectator" };
        state.admissions = without(state.admissions, info.sessionId);
        const displayName = nameOf(info.userId, info.username);
        const record: PresenceRecord = { ...info, node: info.node ?? "", seat: null, displayName };
        let status: "joined" | "reconnected" | "spectating" = "spectating";
        if (admission.kind === "spectator" && admission.seatNext === true) record.seatNext = true;
        if (admission.kind === "seat") {
          const slot = state.seats[admission.seat];
          if (slot === undefined) continue;
          if (admission.reattach) {
            const previous = slot.sessionId === null ? undefined : state.presences[slot.sessionId];
            if (previous !== undefined && previous.sessionId !== info.sessionId) {
              state.presences = without(state.presences, previous.sessionId);
              emit.batch.kicks.push(previous.sessionId);
            }
            status = "reconnected";
          } else {
            occupySlot(
              slot,
              info.userId,
              displayName,
              now,
              state.autoStart?.parties[info.userId] ?? null,
            );
            status = "joined";
            state.hostSeat ??= admission.seat;
          }
          attachSession(slot, info.sessionId);
          record.seat = admission.seat;
          if (status === "reconnected") freshTurnTimer(state, admission.seat, now);
        }
        state.presences[info.sessionId] = record;
        state.emptySinceTs = null;
        const countdownBefore = state.autoStart?.startsAtTs ?? null;
        armAutoStart(state, now);
        const countdownArmed =
          countdownBefore === null && (state.autoStart?.startsAtTs ?? null) !== null;
        emit.session(info.sessionId, "table_state", tableStateFor(state, host, record));
        if (countdownArmed) {
          for (const other of Object.values(state.presences)) {
            if (other.sessionId !== info.sessionId) {
              emit.session(other.sessionId, "table_state", tableStateFor(state, host, other));
            }
          }
        }
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
        const record = state.presences[info.sessionId];
        if (record === undefined) continue;
        state.presences = without(state.presences, info.sessionId);
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
      if (Object.keys(state.presences).length === 0) state.emptySinceTs = now;
      return emit.batch;
    },

    inbound(state, sessionId, raw, now, expectedType) {
      const emit = emitFor(state, now);
      const presence = state.presences[sessionId];
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
          const presence = sessionId === null ? undefined : state.presences[sessionId];
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
      const auto = state.autoStart;
      if (auto !== null && state.phase === "lobby") {
        const seated = activeSeats(state.seats).length;
        if (auto.startsAtTs === null && now >= auto.joinDeadlineTs) {
          if (seated >= host.module.seats.min) {
            auto.startsAtTs = now + config.autoStartCountdownMs;
            broadcastTableState(state, emit);
          } else {
            closeTable(state, emit, "dissolved", now);
          }
        } else if (auto.startsAtTs !== null && now >= auto.startsAtTs) {
          if (seated >= host.module.seats.min) {
            for (const seat of activeSeats(state.seats)) {
              const slot = state.seats[seat];
              if (slot !== undefined) slot.ready = true;
            }
            state.autoStart = null;
            dealRound(state, emit, now, null);
          } else {
            closeTable(state, emit, "dissolved", now);
          }
        }
      }
      if (
        !closing(state) &&
        state.emptySinceTs !== null &&
        Object.keys(state.presences).length === 0 &&
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
