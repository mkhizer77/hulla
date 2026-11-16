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

import { BOT_NAMES, botNames, botSlot, MAX_BOTS, renameClashingBots } from "./bots";
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
  acceptHandAsk,
  announceRound,
  applyRoundMove,
  compactSeats,
  fireDeadlines,
  resolveHandAsk,
  roundRecord,
  startRound,
  turnSeconds,
} from "./round";
import {
  DEFAULT_TABLE_CONFIG,
  newBatch,
  without,
  type AnyGameHost,
  type Batch,
  type CreateTableParams,
  type GameHost,
  type Persistence,
  type PresenceRecord,
  type RoundRuntime,
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

/** A Quick Play table opened for one player with bots (match/bots.ts): the bots take the first seats. */
function withBots(seats: SeatSlot[], params: CreateTableParams, now: number): SeatSlot[] {
  const count = Math.max(0, Math.min(MAX_BOTS, params.bots ?? 0, seats.length - 1));
  let seed = 0;
  for (const c of params.code) seed = (seed * 31 + c.charCodeAt(0)) % 100_003;
  const names = botNames(seed, count);
  for (let i = 0; i < count; i += 1) seats[i] = botSlot(seats, i, names[i] ?? "Bot", now);
  return seats;
}

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
  /** The other games a room may switch to (`change_game`); absent, a table keeps its game. */
  readonly games?: (gameId: string) => AnyGameHost | null;
}

export interface NameSource {
  displayNameOf(userId: string): string | null;
}

export interface SocialGate {
  /** Which of `playerIds` are friends of `userId`; a watcher needs at least one seated. */
  friendsAmong(userId: string, playerIds: readonly string[]): readonly string[];
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

/** With the turn timer off, a hand request waits this long for its answer (OQ-039). */
const HAND_ASK_NO_TIMER_MS = 30_000;

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

  /** Closes an absence: the time away counts toward R46's abandonment limit. */
  const returned = (slot: SeatSlot, now: number): void => {
    if (slot.awaySinceTs !== null) slot.absentMs += Math.max(0, now - slot.awaySinceTs);
    slot.awaySinceTs = null;
  };

  const attachSession = (slot: SeatSlot, sessionId: string, now: number): void => {
    slot.sessionId = sessionId;
    // A player who left mid-round and comes back before it ends takes the seat over from auto-play.
    slot.left = false;
    slot.connected = true;
    slot.disconnectedTs = null;
    returned(slot, now);
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

  /** Seated people, not bots: the players a watcher can be a friend of. */
  const seatedPlayerIds = (state: TableState<S, M>, except?: string): string[] =>
    state.seats.flatMap((s) =>
      s.playerId === null || s.bot || s.playerId === except ? [] : [s.playerId],
    );

  const friendsAmong = (userId: string, playerIds: readonly string[]): string[] =>
    deps.social === undefined || playerIds.length === 0
      ? []
      : [...deps.social.friendsAmong(userId, playerIds)];

  /** Someone new sat down: watchers who are their friends now name them too (GDD 7.7). */
  const noteSeated = (state: TableState<S, M>, playerId: string): void => {
    for (const record of Object.values(state.presences)) {
      if (record.seat !== null || record.userId === playerId) continue;
      const known = record.friendOf ?? [];
      if (known.includes(playerId)) continue;
      if (friendsAmong(record.userId, [playerId]).length > 0) {
        record.friendOf = [...known, playerId];
      }
    }
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
    renameClashingBots(state.seats, slot.displayName);
    attachSession(slot, record.sessionId, now);
    const seated: PresenceRecord = { ...record, seat, seatNext: false };
    state.presences[record.sessionId] = seated;
    noteSeated(state, record.userId);
    state.hostSeat ??= seat;
    emit.public("presence", presencePayload(seated, slot.displayName, "joined"));
  };

  /**
   * The seat a matched player takes. In a game of pairs (Rung, RG1) two players who queued together
   * are partners, so they sit opposite: the second takes the seat across from the first, and a
   * player whose partner is still on the way takes a seat that leaves the one across free.
   */
  const teamSeat = (
    state: TableState<S, M>,
    userId: string,
    open: (index: number) => boolean,
    teamSize: number,
  ): number => {
    const count = state.seats.length;
    const first = state.seats.findIndex((_, index) => open(index));
    const party = state.autoStart?.parties[userId];
    if (teamSize !== 2 || count !== 4 || first === -1) return first;
    const across = (index: number): number => (index + 2) % count;
    const partyOf = (id: string | null): string | undefined =>
      id === null ? undefined : state.autoStart?.parties[id];
    if (party !== undefined) {
      const mate = state.seats.findIndex((slot) => partyOf(slot.playerId) === party);
      if (mate !== -1 && open(across(mate))) return across(mate);
      const pair = state.seats.findIndex((_, index) => open(index) && open(across(index)));
      if (pair !== -1) return pair;
      return first;
    }
    // A solo pairs up with another solo when one is seated, and otherwise takes a seat that is not
    // across from a party member whose partner is still on the way.
    if (Object.keys(state.autoStart?.parties ?? {}).length === 0) return first;
    const acrossSolo = state.seats.findIndex((_, index) => {
      const opposite = state.seats[across(index)]?.playerId ?? null;
      return open(index) && opposite !== null && partyOf(opposite) === undefined;
    });
    if (acrossSolo !== -1) return acrossSolo;
    const kind = state.seats.findIndex((_, index) => {
      const opposite = state.seats[across(index)]?.playerId ?? null;
      return open(index) && !(partyOf(opposite) !== undefined && partnerMissing(state, opposite));
    });
    return kind === -1 ? first : kind;
  };

  /** Whether the party member `userId` still waits for a partner who has not sat down. */
  const partnerMissing = (state: TableState<S, M>, userId: string | null): boolean => {
    if (userId === null) return false;
    const party = state.autoStart?.parties[userId];
    if (party === undefined) return false;
    return !state.seats.some(
      (slot) =>
        slot.playerId !== null &&
        slot.playerId !== userId &&
        state.autoStart?.parties[slot.playerId] === party,
    );
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

  /**
   * Another registered game's host. The core is generic over one game's types; a switch replaces
   * the game and with it what those types stand for, so the host is taken as this core's own.
   */
  const otherGame = (gameId: string): GameHost<S, M, V, E, R> | null =>
    (deps.games?.(gameId) ?? null) as GameHost<S, M, V, E, R> | null;

  /**
   * Fits the seated players into `max` seats, keeping seat order and never unseating the host.
   * Players without a seat stay in the room as spectators first in line for the next free seat;
   * bots without a seat go.
   */
  const resizeSeats = (state: TableState<S, M>, max: number, emit: Emitter): void => {
    const occupied = state.seats.flatMap((slot, index) => (slot.playerId === null ? [] : [index]));
    const keep = occupied.slice(0, max);
    const hostSeat = state.hostSeat;
    if (hostSeat !== null && occupied.includes(hostSeat) && !keep.includes(hostSeat)) {
      keep[keep.length - 1] = hostSeat;
      keep.sort((a, b) => a - b);
    }
    for (const index of occupied) {
      if (keep.includes(index)) continue;
      const slot = state.seats[index];
      const sessionId = slot?.sessionId ?? null;
      const presence = sessionId === null ? undefined : state.presences[sessionId];
      if (presence === undefined) continue;
      presence.seat = null;
      presence.seatNext = true;
      presence.friendOf = friendsAmong(presence.userId, seatedPlayerIds(state, presence.userId));
      emit.public("presence", presencePayload(presence, null, "spectating"));
    }
    const kept = keep.map((index) => state.seats[index]).filter((slot) => slot !== undefined);
    state.seats = [...kept, ...Array.from({ length: max - kept.length }, emptySlot)];
    for (const presence of Object.values(state.presences)) {
      if (presence.seat === null) continue;
      const seat = keep.indexOf(presence.seat);
      presence.seat = seat === -1 ? null : seat;
    }
    const newHost = hostSeat === null ? -1 : keep.indexOf(hostSeat);
    state.hostSeat = newHost === -1 ? (kept.length > 0 ? 0 : null) : newHost;
  };

  /**
   * The room starts playing another game. The match, and so the room code, the chat channel and
   * the voice room, stay; each game's rules and score are kept for when the room comes back.
   */
  const switchGame = (
    state: TableState<S, M>,
    target: GameHost<S, M, V, E, R>,
    rules: RulesSelection,
    by: SeatIndex,
    emit: Emitter,
    now: number,
  ): void => {
    const hostPlayer = state.seats[by]?.playerId ?? null;
    state.rulesByGame = { ...(state.rulesByGame ?? {}), [state.gameId]: state.rules };
    if (state.series !== null) {
      state.seriesByGame = { ...(state.seriesByGame ?? {}), [state.gameId]: state.series };
    }
    state.gameId = target.module.id;
    state.rules = rules;
    state.series = state.seriesByGame?.[state.gameId] ?? null;
    state.queuedGame = null;
    state.phase = "lobby";
    state.round = null;
    state.rematchDeadlineTs = null;
    // Bots that filled seats stay only in a game that seats them at the host's word.
    if (target.fillWithBots !== true) {
      state.seats.forEach((slot, index) => {
        if (slot.bot) freeSlot(state, index);
      });
    }
    resizeSeats(state, target.module.seats.max, emit);
    seatWaitingSpectators(state, emit, now);
    for (const slot of state.seats) {
      slot.ready = slot.bot;
      slot.rematch = slot.bot ? true : null;
    }
    const changedBy = state.seats.findIndex(
      (s) => s.playerId !== null && s.playerId === hostPlayer,
    );
    emit.public("game_changed", {
      gameId: state.gameId,
      rules,
      changedBy: changedBy === -1 ? (state.hostSeat ?? 0) : changedBy,
    });
    for (const presence of Object.values(state.presences)) {
      emit.session(presence.sessionId, "table_state", tableStateFor(state, target, presence));
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
      slot.absentMs = 0;
      slot.awaySinceTs = slot.playerId !== null && !slot.connected ? now : null;
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
    // A bot table carries on for its one player; other public tables need two people.
    const fewest = state.seats.some((s) => s.bot) ? 1 : 2;
    if (state.mode !== "private" && humansPresent(state) < fewest) {
      closeTable(state, emit, "dissolved", now);
      return;
    }
    const queued = state.queuedGame ?? null;
    const target = queued === null ? null : otherGame(queued.gameId);
    if (queued !== null && target !== null) {
      switchGame(state, target, queued.rules, queued.by, emit, now);
      return;
    }
    state.queuedGame = null;
    broadcastTableState(state, emit);
  };

  /** The rematch vote closed: continue with the yes votes or return to the lobby (R52, OQ-005). */
  const resolveRematch = (state: TableState<S, M>, emit: Emitter, now: number): void => {
    if (state.phase !== "roundEnded") return;
    state.rematchDeadlineTs = null;
    const yes = activeSeats(state.seats).filter((seat) => state.seats[seat]?.rematch === true);
    if (yes.length >= host.module.seats.min) {
      const demoted: PresenceRecord[] = [];
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
          demoted.push(presence);
          emit.public("presence", presencePayload(presence, null, "spectating"));
        }
      }
      // A player who stays to watch is named by the friends still seated.
      for (const presence of demoted) {
        presence.friendOf = friendsAmong(presence.userId, seatedPlayerIds(state, presence.userId));
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
    if (type === "take_hand" && host.askHand !== undefined) {
      askHand(state, current, seat, clientSeq, check?.(payload) === true, emit, now);
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

  /**
   * A hand request (R32, R35): legal now by the game's own check, the target present, no other
   * request open and none refused since the last move. The target hears it and answers; the asker's
   * clientSeq is acknowledged when the request resolves.
   */
  const askHand = (
    state: TableState<S, M>,
    current: RoundRuntime<S, M>,
    seat: SeatIndex,
    clientSeq: number,
    valid: boolean,
    emit: Emitter,
    now: number,
  ): void => {
    const rejectMove = (error: RuleError, remember: boolean): void => {
      const rejection = {
        type: "move_rejected",
        payload: { roundId: current.roundId, clientSeq, error },
      };
      if (remember) current.acks[`${String(seat)}:${String(clientSeq)}`] = rejection;
      emit.seat(seat, rejection.type, rejection.payload);
    };
    if (!valid) {
      rejectMove(
        ruleError(ErrorCodes.table.invalidPayload, { reason: "schema", type: "take_hand" }),
        true,
      );
      return;
    }
    const open = current.handAsk;
    if (open !== null) {
      if (open.asker === seat && open.clientSeq === clientSeq) return;
      rejectMove(ruleError(ErrorCodes.rules.handAskPending), false);
      return;
    }
    if (current.askClosedAtMove === current.moves.length) {
      rejectMove(ruleError(ErrorCodes.rules.handTakeWindowClosed, { reason: "refused" }), true);
      return;
    }
    const asked = host.askHand?.(current.state, seat);
    if (!asked?.ok) {
      rejectMove(asked?.error ?? ruleError(ErrorCodes.rules.handTakeDisabled), true);
      return;
    }
    const target = state.seats[asked.value.target];
    if (target === undefined || target.left || !target.connected) {
      rejectMove(ruleError(ErrorCodes.rules.handTakeWindowClosed, { reason: "target_away" }), true);
      return;
    }
    const deadlineTs = current.deadline?.humanTs ?? now + HAND_ASK_NO_TIMER_MS;
    current.handAsk = {
      asker: seat,
      target: asked.value.target,
      cardCount: asked.value.cardCount,
      clientSeq,
      deadlineTs,
      askedTs: now,
    };
    emit.public("hand_asked", {
      roundId: current.roundId,
      asker: seat,
      target: asked.value.target,
      cardCount: asked.value.cardCount,
      deadlineTs,
    });
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
        emit.batch.voiceRemovals.push(presence.userId);
        if (seat === null || slot === undefined) {
          emit.public("presence", presencePayload(presence, null, "left"));
          return;
        }
        slot.sessionId = null;
        slot.connected = false;
        slot.mic = false;
        if (state.phase === "inRound") {
          slot.left = true;
          slot.autoPlay = true;
          slot.awaySinceTs ??= now;
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
      case "set_mic": {
        if (slot === undefined) {
          reject(ruleError(ErrorCodes.table.notAuthorised));
          return;
        }
        const mic = payload["mic"] === true;
        if (slot.mic === mic) return;
        slot.mic = mic;
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
      case "change_game": {
        if (seat === null || !isHost(state, seat)) {
          reject(ruleError(ErrorCodes.table.notHost));
          return;
        }
        // A matched table was matched for one game; only a private room switches.
        if (state.mode !== "private") {
          reject(ruleError(ErrorCodes.table.locked, { reason: "mode" }));
          return;
        }
        const gameId = String(payload["gameId"]);
        if (gameId === state.gameId) {
          // Naming the game already played takes back a queued switch.
          if ((state.queuedGame ?? null) === null) {
            reject(ruleError(ErrorCodes.table.invalidPayload, { reason: "same_game" }));
            return;
          }
          state.queuedGame = null;
          broadcastTableState(state, emit);
          return;
        }
        const target = otherGame(gameId);
        if (target === null) {
          reject(ruleError(ErrorCodes.table.invalidPayload, { reason: "game_id" }));
          return;
        }
        const input = (payload["rules"] as RulesSelection | undefined) ??
          state.rulesByGame?.[target.module.id] ?? { gameId: target.module.id };
        if (input.gameId !== target.module.id) {
          reject(ruleError(ErrorCodes.table.invalidPayload, { reason: "game_id" }));
          return;
        }
        const rules = target.normaliseRules?.(input) ?? input;
        const resolved = target.resolveRules(rules, state.mode);
        if (!resolved.ok) {
          reject(resolved.error);
          return;
        }
        if (state.phase === "inRound") {
          state.queuedGame = { gameId: target.module.id, rules, by: seat };
          broadcastTableState(state, emit);
          return;
        }
        switchGame(state, target, rules, seat, emit, now);
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
      case "remove_spectator": {
        if (!isHost(state, seat)) {
          reject(ruleError(ErrorCodes.table.notHost));
          return;
        }
        const target = payload["userId"];
        const watchers = Object.values(state.presences).filter(
          (p) => p.seat === null && p.userId === target,
        );
        const first = watchers[0];
        if (typeof target !== "string" || first === undefined) {
          reject(ruleError(ErrorCodes.table.invalidPayload, { reason: "user_id" }));
          return;
        }
        // Removed for the life of the table, like a kick (OQ-050): sent before the socket is
        // dropped, so the watcher hears why.
        if (!state.kicked.includes(target)) state.kicked.push(target);
        emit.public("presence", presencePayload(first, first.displayName ?? null, "removed"));
        for (const watcher of watchers) {
          state.presences = without(state.presences, watcher.sessionId);
          emit.batch.kicks.push(watcher.sessionId);
        }
        emit.batch.voiceRemovals.push(target);
        broadcastTableState(state, emit);
        return;
      }
      case "add_bot": {
        if (!isHost(state, seat)) {
          reject(ruleError(ErrorCodes.table.notHost));
          return;
        }
        if (state.mode !== "private") {
          reject(ruleError(ErrorCodes.table.locked, { reason: "mode" }));
          return;
        }
        if (host.fillWithBots !== true || !inLobby) {
          reject(
            ruleError(ErrorCodes.table.invalidPayload, { reason: inLobby ? "bots" : "in_round" }),
          );
          return;
        }
        const wanted = payload["seat"];
        const target =
          typeof wanted === "number" ? wanted : state.seats.findIndex((s) => s.playerId === null);
        const bots = state.seats.filter((s) => s.bot).length;
        if (state.seats[target]?.playerId !== null || bots >= MAX_BOTS) {
          reject(ruleError(ErrorCodes.table.invalidPayload, { reason: "seat" }));
          return;
        }
        const taken = new Set(state.seats.map((s) => s.displayName?.toLowerCase() ?? ""));
        const name =
          botNames(state.createdTs + target, BOT_NAMES.length).find(
            (n) => !taken.has(n.toLowerCase()),
          ) ?? "Bot";
        state.seats[target] = botSlot(state.seats, target, name, now);
        broadcastTableState(state, emit);
        return;
      }
      case "remove_bot": {
        if (!isHost(state, seat)) {
          reject(ruleError(ErrorCodes.table.notHost));
          return;
        }
        const target = payload["seat"];
        if (typeof target !== "number" || state.seats[target]?.bot !== true || !inLobby) {
          reject(ruleError(ErrorCodes.table.invalidPayload, { reason: "seat" }));
          return;
        }
        freeSlot(state, target);
        seatWaitingSpectators(state, emit, now);
        broadcastTableState(state, emit);
        return;
      }
      case "swap_seats": {
        if (!isHost(state, seat)) {
          reject(ruleError(ErrorCodes.table.notHost));
          return;
        }
        if (state.mode !== "private") {
          reject(ruleError(ErrorCodes.table.locked, { reason: "mode" }));
          return;
        }
        const a = payload["a"];
        const b = payload["b"];
        const first = typeof a === "number" ? state.seats[a] : undefined;
        const second = typeof b === "number" ? state.seats[b] : undefined;
        if (
          typeof a !== "number" ||
          typeof b !== "number" ||
          first === undefined ||
          second === undefined ||
          a === b ||
          !inLobby ||
          (first.playerId === null && second.playerId === null)
        ) {
          reject(
            ruleError(ErrorCodes.table.invalidPayload, { reason: inLobby ? "seat" : "in_round" }),
          );
          return;
        }
        state.seats[a] = second;
        state.seats[b] = first;
        for (const record of Object.values(state.presences)) {
          if (record.seat === a) record.seat = b;
          else if (record.seat === b) record.seat = a;
        }
        if (state.hostSeat === a) state.hostSeat = b;
        else if (state.hostSeat === b) state.hostSeat = a;
        // A bot's id names its seat; keep it in step so two bots never share one.
        for (const index of [a, b]) {
          const slot = state.seats[index];
          if (slot?.bot === true) slot.playerId = `bot_${String(index)}`;
        }
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
        emit.batch.voiceRemovals.push(victim.playerId);
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
        victim.mic = false;
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
      case "answer_hand": {
        const current = state.round;
        const ask = current?.handAsk ?? null;
        if (current === null || ask === null || seat === null || ask.target !== seat) {
          reject(ruleError(ErrorCodes.rules.handAskNone));
          return;
        }
        if (payload["roundId"] !== current.roundId) {
          reject(ruleError(ErrorCodes.table.staleSequence, { reason: "round_id" }));
          return;
        }
        if (payload["accept"] !== true) {
          resolveHandAsk(state, emit, false, "declined");
          return;
        }
        acceptHandAsk(round, state, emit, now);
        finishRound(state, emit, now);
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
        // Anyone sees a pack's emotes; only its owners may send them (GDD 11.3, 12.11).
        const emotePack = chat.emotePack(emoteId);
        if (emotePack !== null && !(slot.emotePacks ?? chat.freeEmotePacks).includes(emotePack)) {
          reject(ruleError(ErrorCodes.table.notAuthorised, { reason: "not_owned", emotePack }));
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
        seats: withBots(Array.from({ length: host.module.seats.max }, emptySlot), params, now),
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
      const wouldSit =
        parsed.data.asSpectator !== true && state.phase !== "inRound" && !state.lockedByHost;
      const noFreeSeat = !state.seats.some((slot, i) => slot.playerId === null && !reserved.has(i));
      if (wouldSit && noFreeSeat && host.fillWithBots === true) {
        // A person always outranks a bot that was only filling the seat (Rung design 6.5).
        const filler = state.seats.map((slot) => slot.bot).lastIndexOf(true);
        if (filler !== -1) freeSlot(state, filler);
      }
      const open = (index: number): boolean =>
        state.seats[index]?.playerId === null && !reserved.has(index);
      const free = teamSeat(state, presence.userId, open, host.module.seats.teams?.size ?? 0);
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
      const friendOf = friendsAmong(presence.userId, seatedPlayerIds(state));
      if (deps.social !== undefined && friendOf.length === 0) {
        return { accept: false, code: ErrorCodes.social.notFriends };
      }
      // A late arrival who wanted a seat takes the next free one when the round ends (GDD 7.6).
      const late = parsed.data.asSpectator !== true && state.phase === "inRound";
      state.admissions[presence.sessionId] = {
        kind: "spectator",
        ...(late ? { seatNext: true } : {}),
        ...(friendOf.length > 0 ? { friendOf } : {}),
      };
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
        if (admission.kind === "spectator" && admission.friendOf !== undefined) {
          record.friendOf = [...admission.friendOf];
        }
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
            renameClashingBots(state.seats, slot.displayName);
            noteSeated(state, info.userId);
            status = "joined";
            state.hostSeat ??= admission.seat;
          }
          attachSession(slot, info.sessionId, now);
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
        // The others learn a new occupant's avatar and a new watcher from the table state; the presence
        // event alone carries only the name, so a taken seat or a new watcher sends it to everyone.
        if (countdownArmed || status !== "reconnected") {
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
          emit.batch.voiceRemovals.push(record.userId);
          continue;
        }
        if (slot.sessionId !== info.sessionId) {
          emit.public("presence", presencePayload({ ...record, seat: null }, null, "left"));
          continue;
        }
        slot.sessionId = null;
        slot.connected = false;
        slot.mic = false;
        slot.disconnectedTs = now;
        if (state.phase === "inRound") {
          slot.autoPlay = true;
          slot.awaySinceTs ??= now;
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
          emit.batch.voiceRemovals.push(record.userId);
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
      // A bot table ends once its player has gone (left, not just dropped: they may reconnect),
      // and never deals to bots alone if the player never arrived.
      if (
        !closing(state) &&
        state.seats.some((s) => s.bot) &&
        !state.seats.some((s) => !s.bot && s.playerId !== null && !s.left) &&
        (state.phase !== "lobby" || auto?.startsAtTs !== null || now >= auto.joinDeadlineTs)
      ) {
        closeTable(state, emit, "dissolved", now);
      }
      if (auto !== null && state.phase === "lobby") {
        let seated = activeSeats(state.seats).length;
        if (
          auto.startsAtTs === null &&
          now >= auto.joinDeadlineTs &&
          host.fillWithBots === true &&
          seated > 0 &&
          seated < host.module.seats.min
        ) {
          // Someone who was matched never arrived: bots take the empty seats so the rest can play.
          const names = botNames(state.createdTs, BOT_NAMES.length);
          state.seats.forEach((slot, index) => {
            const bots = state.seats.filter((s) => s.bot).length;
            if (slot.playerId !== null || bots >= MAX_BOTS) return;
            const taken = new Set(state.seats.map((s) => s.displayName?.toLowerCase() ?? ""));
            const name = names.find((n) => !taken.has(n.toLowerCase())) ?? "Bot";
            state.seats[index] = botSlot(state.seats, index, name, now);
          });
          seated = activeSeats(state.seats).length;
        }
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
