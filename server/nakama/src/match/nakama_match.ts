import { OPCODES } from "@hulla/protocol/generated";

import { ROUND_RESULTS_COLLECTION } from "../services/account_deletion";
import { applyRoundToStats, nkProfileStore } from "../services/profiles";

import { tableCore, type PresenceInfo, type TableCore } from "./table_core";
import { freeSeat, occupiedSeats, tableStatus } from "./view";

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
        logger.error("round record not written", {
          tableId: record.tableId,
          roundId: record.roundId,
          error: String(error),
        });
      }
      if (!record.completed) return;
      const store = nkProfileStore(nk, logger);
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
          logger.warn("round result not recorded for a player", { seat, error: String(error) });
        }
      });
    },
  };
}

/** The match label other services query: code, game, mode, status and seat counts. */
export function labelFor<S, M>(state: TableState<S, M>): string {
  return JSON.stringify({
    code: state.code,
    gameId: state.gameId,
    mode: state.mode,
    status: tableStatus(state),
    seated: occupiedSeats(state.seats).length,
    open:
      freeSeat(state.seats) === null ? 0 : state.seats.filter((s) => s.playerId === null).length,
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

  return {
    matchInit(_ctx, logger, nk, params) {
      const core = coreFor(nk, logger);
      const state = core.create(parseParams(params), Date.now());
      logger.info("table created", {
        tableId: state.tableId,
        gameId: state.gameId,
        mode: state.mode,
      });
      return { state, tickRate: core.config.tickRate, label: labelFor(state) };
    },

    matchJoinAttempt(_ctx, logger, nk, _dispatcher, _tick, state, presence, metadata) {
      const decision = coreFor(nk, logger).joinAttempt(state, info(presence), metadata, Date.now());
      return decision.accept
        ? { state, accept: true }
        : { state, accept: false, rejectMessage: decision.code };
    },

    matchJoin(_ctx, logger, nk, dispatcher, _tick, state, presences) {
      const before = labelFor(state);
      const batch = coreFor(nk, logger).join(state, presences.map(info), Date.now());
      send(dispatcher, state, batch, presences);
      relabel(dispatcher, before, state);
      return { state };
    },

    matchLeave(_ctx, logger, nk, dispatcher, _tick, state, presences) {
      const before = labelFor(state);
      const batch = coreFor(nk, logger).leave(state, presences.map(info), Date.now());
      send(dispatcher, state, batch, presences);
      relabel(dispatcher, before, state);
      return { state };
    },

    matchLoop(_ctx, logger, nk, dispatcher, _tick, state, messages) {
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
          end = end || batch.end;
        } catch (error: unknown) {
          logger.error("message dropped", { tableId: state.tableId, error: String(error) });
        }
      }
      const batch = core.tick(state, now);
      send(dispatcher, state, batch, senders);
      relabel(dispatcher, before, state);
      return end || batch.end || state.phase === "closing" ? null : { state };
    },

    matchTerminate(_ctx, logger, nk, dispatcher, _tick, state) {
      const batch = coreFor(nk, logger).close(state, "server", Date.now());
      send(dispatcher, state, batch, []);
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
