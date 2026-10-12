import type { RulesSelection } from "@hulla/protocol/generated";
import type { GameId, GameModule, RoundResult, SeriesState } from "@hulla/rules-engine";
import type { Result, Rng, RuleError, SeatIndex } from "@hulla/shared-ts";

export type TableMode = "private" | "public" | "ranked";

/** Starting is instantaneous (createRound is synchronous), so it is never a stored phase. */
export type TablePhase = "lobby" | "inRound" | "roundEnded" | "closing";

export type TableCloseReason = "host_closed" | "empty" | "dissolved" | "server";

export type PresenceStatus =
  "joined" | "left" | "disconnected" | "reconnected" | "auto_play" | "kicked" | "spectating";

/** A connected socket at the table; `seat` is null for spectators. */
export interface PresenceRecord {
  readonly sessionId: string;
  readonly userId: string;
  readonly username: string;
  seat: SeatIndex | null;
}

export interface SeatSlot {
  playerId: string | null;
  displayName: string | null;
  sessionId: string | null;
  ready: boolean;
  connected: boolean;
  /** The R44 policy holds the seat (disconnected, absent after three timeouts, or left mid-round). */
  autoPlay: boolean;
  /** Left or was kicked during a round; the seat is freed when the round ends (GDD 9.9). */
  left: boolean;
  resumeToken: string | null;
  joinedTs: number;
  disconnectedTs: number | null;
  /** Consecutive timeouts for R45. */
  timeouts: number;
  rematch: boolean | null;
}

/** What `matchJoinAttempt` decided for a session, consumed by `matchJoin`. */
export type Admission =
  | { readonly kind: "seat"; readonly seat: SeatIndex; readonly reattach: boolean }
  | { readonly kind: "spectator" };

/** A wire message produced from engine events; `seat` set means that seat only. */
export interface GameMessage {
  readonly type: string;
  readonly payload: object;
  readonly seat: SeatIndex | null;
}

export interface EncodeContext<S> {
  readonly roundId: string;
  readonly seedHex: string;
  readonly state: S;
  /** The series after this round's result was applied; null while the round is running. */
  readonly series: SeriesState | null;
  readonly result: RoundResult | null;
  /** The move that produced the events; null for round start. */
  readonly move: {
    readonly seat: SeatIndex;
    readonly clientSeq: number | null;
    readonly auto: boolean;
  } | null;
}

/**
 * What the table core needs from a game beyond the engine contract: how its moves arrive on the
 * wire and how its events leave. The core knows no game; every game registers one of these.
 */
export interface GameHost<S, M, V, E, R> {
  readonly module: GameModule<S, M, V, E, R>;
  resolveRules(selection: RulesSelection, mode: TableMode): Result<R, RuleError>;
  isMoveType(type: string): boolean;
  decodeMove(type: string, payload: Record<string, unknown>): Result<M, RuleError>;
  roundStartEvents(state: S): readonly E[];
  encodeEvents(ctx: EncodeContext<S>, events: readonly E[]): readonly GameMessage[];
  turnStart(
    ctx: { readonly roundId: string; readonly state: S },
    seat: SeatIndex,
    deadlineTs: number,
    simultaneous: boolean,
  ): object;
  /** How long clients animate the events before the next deadline may start (ARCHITECTURE 8.4). */
  presentationDelayMs(events: readonly E[]): number;
  stateHash(state: S): string;
}

export interface TableConfig {
  readonly tickRate: number;
  readonly openingSeconds: number;
  /** A seat under auto-play acts this long after its turn starts (R46, OQ-003). */
  readonly autoPlayDelayMs: number;
  readonly reconnectWindowMs: number;
  readonly rematchWindowMs: number;
  readonly emptyTtlMs: Readonly<Record<TableMode, number>>;
  readonly defaultTurnSeconds: Readonly<Record<TableMode, number>>;
  readonly snapshotEvery: number;
  readonly maxSpectators: number;
}

/** GDD R43, R46, R52, 9.1 and 9.10. */
export const DEFAULT_TABLE_CONFIG: TableConfig = {
  tickRate: 5,
  openingSeconds: 10,
  autoPlayDelayMs: 3_000,
  reconnectWindowMs: 30_000,
  rematchWindowMs: 20_000,
  emptyTtlMs: { private: 30 * 60_000, public: 60_000, ranked: 60_000 },
  defaultTurnSeconds: { private: 20, public: 15, ranked: 12 },
  snapshotEvery: 25,
  maxSpectators: 10,
};

export interface AppliedMove<M> {
  readonly seat: SeatIndex;
  readonly move: M;
  readonly auto: boolean;
  readonly clientSeq: number | null;
  readonly seq: number;
}

/** One message in the table's log: public (`seat` null) or for one seat. */
export interface LoggedMessage {
  readonly seq: number;
  readonly ts: number;
  readonly type: string;
  readonly payload: object;
  readonly seat: SeatIndex | null;
  readonly stateHash: string | null;
}

export interface RoundSnapshot<S> {
  readonly seq: number;
  readonly stateHash: string;
  readonly state: S;
}

export interface Deadline {
  readonly kind: "perSeatTimer" | "simultaneousWindow";
  readonly seats: readonly SeatIndex[];
  /** Nothing fires before the presentation delay has passed. */
  readonly notBeforeTs: number;
  /** When connected seats time out; null when the timer is off. */
  readonly humanTs: number | null;
  /** When seats under auto-play act. */
  readonly autoTs: number;
}

export interface Ack {
  readonly type: string;
  readonly payload: object;
}

export interface RoundRuntime<S, M> {
  readonly roundId: string;
  readonly index: number;
  readonly seedHex: string;
  readonly rng: Rng;
  readonly seatCount: number;
  state: S;
  phase: string;
  moves: AppliedMove<M>[];
  /** Keyed `seat:clientSeq`; the original answer is re-sent on a duplicate (ARCHITECTURE 8.4). */
  acks: Record<string, Ack>;
  snapshots: RoundSnapshot<S>[];
  sinceSnapshot: number;
  deadline: Deadline | null;
  readonly startedTs: number;
  result: RoundResult | null;
}

export interface TableState<S, M> {
  readonly tableId: string;
  readonly code: string;
  readonly gameId: GameId;
  readonly mode: TableMode;
  phase: TablePhase;
  lockedByHost: boolean;
  hostSeat: SeatIndex | null;
  rules: RulesSelection;
  seats: SeatSlot[];
  presences: Map<string, PresenceRecord>;
  admissions: Map<string, Admission>;
  /** User ids that may not rejoin (GDD 9.9). */
  kicked: string[];
  series: SeriesState | null;
  round: RoundRuntime<S, M> | null;
  roundsStarted: number;
  /** The last server sequence number; only public messages advance it. */
  seq: number;
  log: LoggedMessage[];
  rematchDeadlineTs: number | null;
  readonly createdTs: number;
  emptySinceTs: number | null;
  closeReason: TableCloseReason | null;
}

export interface Envelope {
  readonly v: 1;
  readonly type: string;
  readonly tableId: string;
  readonly seq: number;
  readonly ts: number;
  readonly payload: object;
}

export type Target =
  { readonly kind: "all" } | { readonly kind: "session"; readonly sessionId: string };

export interface Outbound {
  readonly envelope: Envelope;
  readonly to: Target;
}

/** What one callback produced; the Nakama binding sends it. The state is mutated in place. */
export interface Batch {
  readonly outbound: Outbound[];
  readonly kicks: string[];
  end: boolean;
}

export function newBatch(): Batch {
  return { outbound: [], kicks: [], end: false };
}

export interface RoundRecord {
  readonly tableId: string;
  readonly roundId: string;
  readonly gameId: GameId;
  readonly rulesVersion: string;
  readonly seedHex: string;
  readonly players: readonly (string | null)[];
  readonly moves: readonly { seat: SeatIndex; move: unknown; auto: boolean }[];
  readonly result: RoundResult | null;
  readonly series: SeriesState | null;
  readonly finalStateHash: string;
  readonly startedTs: number;
  readonly endedTs: number;
  readonly completed: boolean;
}

export interface Persistence {
  roundRecord(record: RoundRecord): void;
}

export interface TableIds {
  uuid(): string;
}

export interface CreateTableParams {
  readonly code: string;
  readonly gameId: GameId;
  readonly mode: TableMode;
  readonly rules?: RulesSelection;
}
