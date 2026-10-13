import { OPCODES } from "@hulla/protocol/generated";
import { ErrorCodes } from "@hulla/shared-ts";

import { log } from "../infra/log";
import { METRICS, metrics } from "../infra/metrics";
import { ROUND_RESULTS_COLLECTION } from "../services/account_deletion";
import { countRound, nkAchievementStore, settleUnlocks } from "../services/achievements";
import { emitAnalyticsSafely } from "../services/analytics";
import { clientTooOld, nkConfigStore } from "../services/config";
import { postRoundPoints } from "../services/leaderboards";
import { credit, nkLedgerPorts } from "../services/ledger";
import { metricsFromRound, recordMissionMetrics } from "../services/missions";
import {
  nkFriendIds,
  nkPresenceStore,
  seatReleased,
  seatTaken,
  type PresenceStore,
} from "../services/presence";
import { applyRoundToStats, nkAreFriends, nkProfileStore } from "../services/profiles";
import { applyReward, forDay, nkProgressStore, roundReward } from "../services/progression";
import { isBanned, readModeration } from "../services/sanctions";
import { nkSocialStore, recordRecentPlayers } from "../services/social";

import { analyticsFromBatch, type TableEvent } from "./analytics";
import { tableCore, type PresenceInfo, type TableCore } from "./table_core";
import { DEFAULT_TABLE_CONFIG } from "./types";
import { freeSeat, occupiedSeats, spectatorCount, tableStatus } from "./view";

import type {
  Batch,
  CreateTableParams,
  GameHost,
  Persistence,
  RoundRecord,
  TableConfig,
  TableState,
} from "./types";
import type { RulesSelection } from "@hulla/protocol/generated";

/** Nakama's system user owns records no player may read or write directly. */
const SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000";

export const ROUND_LOG_COLLECTION = "round_logs";

const opcodeByType: Record<string, number> = {};
const typeByOpcode: Record<number, string> = {};
for (const [type, entry] of Object.entries(OPCODES)) {
  opcodeByType[type] = entry.opcode;
  typeByOpcode[entry.opcode] = type;
}

export function storagePersistence(nk: nkruntime.Nakama, logger: nkruntime.Logger): Persistence {
  return {
    roundRecord(record: RoundRecord): void {
      try {
        nk.storageWrite([
          {
            collection: ROUND_LOG_COLLECTION,
            key: `${record.tableId}:${record.roundId}`,
            userId: SYSTEM_USER_ID,
            value: record,
            permissionRead: 0,
            permissionWrite: 0,
          },
        ]);
      } catch (error: unknown) {
        log.error(logger, "round record not written", {
          tableId: record.tableId,
          roundId: record.roundId,
          error: String(error),
        });
      }
      if (!record.completed) return;
      try {
        recordRecentPlayers(
          nkSocialStore(nk),
          record.players.filter((p): p is string => p !== null),
          record.endedTs,
        );
      } catch (error: unknown) {
        log.warn(logger, "recent players not recorded", { error: String(error) });
      }
      const store = nkProfileStore(nk, logger);
      const humans = record.players.filter((p): p is string => p !== null);
      record.players.forEach((playerId, seat) => {
        if (playerId === null) return;
        try {
          const profile = store.read(playerId);
          if (profile !== null) {
            store.write(playerId, {
              ...profile,
              stats: applyRoundToStats(profile.stats, seat, record),
            });
          }
          rewardPlayer(nk, logger, record, playerId, seat, humans);
          nk.storageWrite([
            {
              collection: ROUND_RESULTS_COLLECTION,
              key: `${record.tableId}:${record.roundId}`,
              userId: playerId,
              value: {
                tableId: record.tableId,
                roundId: record.roundId,
                gameId: record.gameId,
                seat,
                finish: record.result?.finishOrder.indexOf(seat) ?? null,
                loser: record.result?.loserSeat === seat,
                points: record.result?.points[seat] ?? 0,
                endedTs: record.endedTs,
              },
              permissionRead: 1,
              permissionWrite: 0,
            },
          ]);
        } catch (error: unknown) {
          log.warn(logger, "round result not recorded for a player", {
            seat,
            error: String(error),
          });
        }
      });
    },
  };
}

/**
 * XP, level-ups and coins for one seat of a completed round (GDD 12.2, 12.3), credited through
 * the ledger with ids derived from the round so a retry pays nothing twice.
 */
function rewardPlayer(
  nk: nkruntime.Nakama,
  logger: nkruntime.Logger,
  record: RoundRecord,
  playerId: string,
  seat: number,
  humans: readonly string[],
): void {
  try {
    const progressStore = nkProgressStore(nk);
    const before = progressStore.read(playerId);
    const current = forDay(before, record.endedTs);
    const withFriend = humans.some(
      (other) => other !== playerId && nkAreFriends(nk, playerId, other),
    );
    const reward = roundReward(record, seat, {
      withFriend,
      coinsToday: current.coinsToday,
      loginStreak: current.loginStreak,
    });
    if (reward === null) return;
    const applied = applyReward(current, reward, record, record.endedTs);
    progressStore.write(playerId, applied.progress);
    recordMissionMetrics(nk, playerId, metricsFromRound(record, seat, withFriend), record.endedTs);
    postRoundPoints(nk, logger, record, playerId, seat);
    const friendsAtTable = humans.filter(
      (other) => other !== playerId && nkAreFriends(nk, playerId, other),
    ).length;
    const achievements = nkAchievementStore(nk);
    const counted = countRound(achievements.read(playerId), record, seat, {
      friendsAtTable,
      playerIds: humans,
    });
    settleUnlocks(
      nk,
      nkLedgerPorts(nk),
      achievements,
      playerId,
      counted,
      achievements.list(),
      record.endedTs,
    );
    const ledger = nkLedgerPorts(nk);
    const prefix = `${record.tableId}:${record.roundId}`;
    credit(ledger, {
      userId: playerId,
      sourceId: `${prefix}:round`,
      reason: "round",
      coins: reward.coins,
    });
    for (const grant of applied.grants) {
      credit(ledger, {
        userId: playerId,
        sourceId: `level:${String(grant.level)}`,
        reason: "level_up",
        coins: grant.coins,
        gems: grant.gems,
        metadata: { level: grant.level },
      });
    }
  } catch (error: unknown) {
    log.warn(logger, "round reward not applied", { seat, error: String(error) });
  }
}

/** The match label other services query: code, game, mode, status and seat counts. */
export function labelFor<S, M>(
  state: TableState<S, M>,
  maxSpectators: number = DEFAULT_TABLE_CONFIG.maxSpectators,
): string {
  const watching = spectatorCount(state.presences);
  const status = tableStatus(state);
  return JSON.stringify({
    code: state.code,
    gameId: state.gameId,
    mode: state.mode,
    preset: state.rules.preset ?? null,
    status,
    seated: occupiedSeats(state.seats).length,
    open:
      freeSeat(state.seats) === null ? 0 : state.seats.filter((s) => s.playerId === null).length,
    watching,
    watchable:
      status !== "closed" && state.rules.settings?.spectators !== false && watching < maxSpectators,
  });
}

function info(presence: nkruntime.Presence): PresenceInfo {
  return {
    sessionId: presence.sessionId,
    userId: presence.userId,
    username: presence.username,
    node: presence.node,
  };
}

function presenceFor<S, M>(
  state: TableState<S, M>,
  sessionId: string,
  known: readonly nkruntime.Presence[],
): nkruntime.Presence | null {
  const match = known.find((p) => p.sessionId === sessionId);
  if (match !== undefined) return match;
  const record = state.presences[sessionId];
  if (record === undefined) return null;
  return {
    sessionId: record.sessionId,
    userId: record.userId,
    username: record.username,
    node: record.node,
  };
}

/** The same events feed the Prometheus counters and timers (GDD 20.13). */
function recordMetric(nk: nkruntime.Nakama, gameId: string, event: TableEvent): void {
  const tags = { game: gameId };
  const p = event.params as Record<string, unknown>;
  switch (event.name) {
    case "round_start":
      metrics.count(nk, METRICS.rounds, { ...tags, mode: String(p["mode"]) });
      break;
    case "move_played":
      metrics.count(nk, METRICS.moves, { ...tags, auto: p["auto"] === true ? "true" : "false" });
      if (p["timeout"] === true) metrics.count(nk, METRICS.timeouts, tags);
      break;
    case "thulla":
      metrics.count(nk, METRICS.thullas, tags);
      break;
    case "disconnect":
      metrics.count(nk, METRICS.disconnects, { ...tags, outcome: String(p["outcome"]) });
      break;
    case "reconnect":
      metrics.count(nk, METRICS.reconnects, tags);
      break;
    case "round_end":
      metrics.count(nk, METRICS.roundsEnded, tags);
      metrics.time(nk, METRICS.roundDuration, tags, Math.round(Number(p["duration"]) * 1000));
      break;
    default:
      break;
  }
}

/** Every public game message that the taxonomy names becomes one analytics log line. */
function logAnalytics<S, M>(
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  state: TableState<S, M>,
  batch: Batch,
  now: number,
): void {
  for (const event of analyticsFromBatch(state, batch, now)) {
    recordMetric(nk, state.gameId, event);
    emitAnalyticsSafely(logger, event.name, event.params, {
      gameId: state.gameId,
      tableId: state.tableId,
      mode: state.mode,
      ...(state.round === null ? {} : { roundId: state.round.roundId }),
      ...(event.seat === null ? {} : { seat: event.seat }),
    });
  }
}

function send<S, M>(
  dispatcher: nkruntime.MatchDispatcher,
  state: TableState<S, M>,
  batch: Batch,
  known: readonly nkruntime.Presence[],
): void {
  for (const out of batch.outbound) {
    const opcode = opcodeByType[out.envelope.type];
    if (opcode === undefined) continue;
    const data = JSON.stringify(out.envelope);
    if (out.to.kind === "all") {
      dispatcher.broadcastMessage(opcode, data, null, null, true);
      continue;
    }
    const presence = presenceFor(state, out.to.sessionId, known);
    if (presence !== null) dispatcher.broadcastMessage(opcode, data, [presence], null, true);
  }
  const kicks = batch.kicks
    .map((sessionId) => presenceFor(state, sessionId, known))
    .filter((p): p is nkruntime.Presence => p !== null);
  if (kicks.length > 0) dispatcher.matchKick(kicks);
}

function decodeData(nk: nkruntime.Nakama, data: ArrayBuffer): string {
  const raw: unknown = data;
  return typeof raw === "string" ? raw : nk.binaryToString(data);
}

function parseParams(params: Record<string, unknown>): CreateTableParams {
  const rules = params["rules"];
  const code = params["code"];
  const base = {
    code: typeof code === "string" ? code : "",
    gameId: params["gameId"] as CreateTableParams["gameId"],
    mode: (params["mode"] as CreateTableParams["mode"] | undefined) ?? "private",
  };
  const autoStart = params["autoStart"];
  const parties = params["parties"];
  const extras: Pick<CreateTableParams, "autoStart" | "parties"> = {};
  if (typeof autoStart === "object" && autoStart !== null) {
    const expected = Number((autoStart as { expected?: unknown }).expected ?? 0);
    Object.assign(extras, { autoStart: { expected } });
  }
  if (Array.isArray(parties)) {
    Object.assign(extras, { parties: parties as CreateTableParams["parties"] });
  }
  if (typeof rules === "string") {
    return { ...base, ...extras, rules: JSON.parse(rules) as RulesSelection };
  }
  if (typeof rules === "object" && rules !== null) {
    return { ...base, ...extras, rules: rules as RulesSelection };
  }
  return { ...base, ...extras };
}

function parseSignal(data: string): { readonly kind?: string } | null {
  try {
    return JSON.parse(data) as { readonly kind?: string };
  } catch {
    return null;
  }
}

export interface MatchHandlerOptions {
  readonly config?: Partial<TableConfig>;
}

/**
 * Nakama match callbacks for one game. Nakama looks each callback up by its global name, so the
 * game file wraps these in named top-level functions and registers those (see `main.ts`).
 */
export function tableMatchHandler<S, M, V, E, R>(
  host: GameHost<S, M, V, E, R>,
  options: MatchHandlerOptions = {},
): nkruntime.MatchHandler<TableState<S, M>> {
  const coreFor = (nk: nkruntime.Nakama, logger: nkruntime.Logger): TableCore<S, M> =>
    tableCore(host, {
      ids: { uuid: () => nk.uuidv4() },
      persist: storagePersistence(nk, logger),
      social: {
        mayWatch: (userId, seatedIds) => {
          const friends = nkFriendIds(nk, userId);
          return seatedIds.some((id) => friends.includes(id));
        },
      },
      ...(options.config === undefined ? {} : { config: options.config }),
    });

  const relabel = (
    dispatcher: nkruntime.MatchDispatcher,
    before: string,
    state: TableState<S, M>,
  ): void => {
    const after = labelFor(state);
    if (after !== before) dispatcher.matchLabelUpdate(after);
  };

  /** Presence (GDD 10.4): who sits at this table, kept in step with the seats. */
  const presenceSync = (
    ctx: nkruntime.Context,
    logger: nkruntime.Logger,
    nk: nkruntime.Nakama,
    state: TableState<S, M>,
    userIds: readonly string[],
    now: number,
  ): void => {
    const matchId = ctx.matchId;
    if (matchId === undefined || matchId === "") return;
    let store: PresenceStore | null = null;
    for (const userId of new Set(userIds)) {
      try {
        store ??= nkPresenceStore(nk);
        const seated = state.phase !== "closing" && state.seats.some((s) => s.playerId === userId);
        if (seated) seatTaken(store, userId, matchId, now);
        else seatReleased(store, userId, matchId, now);
      } catch (error: unknown) {
        log.warn(logger, "presence not updated", { userId, error: String(error) });
      }
    }
  };
  const seatedUserIds = (state: TableState<S, M>): string[] =>
    state.seats.map((s) => s.playerId).filter((id): id is string => id !== null);

  return {
    matchInit(_ctx, logger, nk, params) {
      const core = coreFor(nk, logger);
      const state = core.create(parseParams(params), Date.now());
      log.info(logger, "table created", {
        tableId: state.tableId,
        gameId: state.gameId,
        mode: state.mode,
      });
      return { state, tickRate: core.config.tickRate, label: labelFor(state) };
    },

    matchJoinAttempt(_ctx, logger, nk, _dispatcher, _tick, state, presence, metadata) {
      const now = Date.now();
      try {
        if (isBanned(readModeration(nk, presence.userId), now)) {
          return { state, accept: false, rejectMessage: ErrorCodes.auth.banned };
        }
      } catch (error: unknown) {
        log.warn(logger, "ban state not read", { error: String(error) });
      }
      // An old client is told to update and cannot join a table (GDD 20.15).
      try {
        if (clientTooOld(metadata["clientVersion"], nkConfigStore(nk).read())) {
          return { state, accept: false, rejectMessage: ErrorCodes.auth.updateRequired };
        }
      } catch (error: unknown) {
        log.warn(logger, "remote config not read", { error: String(error) });
      }
      const decision = coreFor(nk, logger).joinAttempt(state, info(presence), metadata, now);
      return decision.accept
        ? { state, accept: true }
        : { state, accept: false, rejectMessage: decision.code };
    },

    matchJoin(ctx, logger, nk, dispatcher, _tick, state, presences) {
      const before = labelFor(state);
      const now = Date.now();
      const batch = coreFor(nk, logger).join(state, presences.map(info), now);
      presenceSync(
        ctx,
        logger,
        nk,
        state,
        presences.map((p) => p.userId),
        now,
      );
      relabel(dispatcher, before, state);
      send(dispatcher, state, batch, presences);
      logAnalytics(logger, nk, state, batch, now);
      return { state };
    },

    matchLeave(ctx, logger, nk, dispatcher, _tick, state, presences) {
      const before = labelFor(state);
      const now = Date.now();
      const batch = coreFor(nk, logger).leave(state, presences.map(info), now);
      presenceSync(
        ctx,
        logger,
        nk,
        state,
        presences.map((p) => p.userId),
        now,
      );
      relabel(dispatcher, before, state);
      send(dispatcher, state, batch, presences);
      logAnalytics(logger, nk, state, batch, now);
      return { state };
    },

    matchLoop(ctx, logger, nk, dispatcher, _tick, state, messages) {
      const seatedBefore = seatedUserIds(state);
      const core = coreFor(nk, logger);
      const before = labelFor(state);
      const now = Date.now();
      const senders = messages.map((m) => m.sender);
      let end = false;
      for (const message of messages) {
        try {
          const batch = core.inbound(
            state,
            message.sender.sessionId,
            decodeData(nk, message.data),
            now,
            typeByOpcode[message.opCode],
          );
          send(dispatcher, state, batch, senders);
          logAnalytics(logger, nk, state, batch, now);
          end = end || batch.end;
        } catch (error: unknown) {
          metrics.count(nk, METRICS.messagesDropped, { game: state.gameId });
          log.error(logger, "message dropped", { tableId: state.tableId, error: String(error) });
        }
      }
      const batch = core.tick(state, now);
      send(dispatcher, state, batch, senders);
      logAnalytics(logger, nk, state, batch, now);
      relabel(dispatcher, before, state);
      const closing = end || batch.end || state.phase === "closing";
      if (closing) presenceSync(ctx, logger, nk, { ...state, phase: "closing" }, seatedBefore, now);
      return closing ? null : { state };
    },

    matchTerminate(ctx, logger, nk, dispatcher, _tick, state) {
      const now = Date.now();
      const seated = seatedUserIds(state);
      const batch = coreFor(nk, logger).close(state, "server", now);
      send(dispatcher, state, batch, []);
      presenceSync(ctx, logger, nk, { ...state, phase: "closing" }, seated, now);
      return { state };
    },

    matchSignal(_ctx, logger, nk, dispatcher, _tick, state, data) {
      const signal = parseSignal(data);
      if (signal === null) return { state, data: JSON.stringify({ ok: false }) };
      if (signal.kind === "has_user") {
        const userId = (signal as { userId?: unknown }).userId;
        const record = Object.values(state.presences).find((p) => p.userId === userId);
        return {
          state,
          data: JSON.stringify({
            ok: true,
            tableId: state.tableId,
            present: record !== undefined,
            seat: record?.seat ?? null,
          }),
        };
      }
      if (signal.kind === "close") {
        const batch = coreFor(nk, logger).close(state, "host_closed", Date.now());
        send(dispatcher, state, batch, []);
        return { state, data: JSON.stringify({ ok: true }) };
      }
      return {
        state,
        data: JSON.stringify({
          ok: true,
          tableId: state.tableId,
          phase: state.phase,
          seq: state.seq,
          seated: occupiedSeats(state.seats).length,
          roundId: state.round?.roundId ?? null,
          deadline: state.round?.deadline ?? null,
          seats: state.seats.map((s) => ({
            playerId: s.playerId,
            connected: s.connected,
            autoPlay: s.autoPlay,
            left: s.left,
            hasSession: s.sessionId !== null,
          })),
        }),
      };
    },
  };
}
