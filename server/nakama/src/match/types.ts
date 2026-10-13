import type { Check, RulesSelection } from "@hulla/protocol/generated";
import type { GameId, GameModule, RoundResult, SeriesState } from "@hulla/rules-engine";
import type { Result, RuleError, SeatIndex, Xoshiro128State } from "@hulla/shared-ts";

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
  /** The name shown for this player: their chosen display name, else the username. */
  readonly displayName?: string | null;
  /** Nakama's node for the session; a message addressed without it is dropped by the router. */
  readonly node: string;
  seat: SeatIndex | null;
  /** A spectator who takes the next free seat when the round ends (GDD 7.6, 9.10). */
  seatNext?: boolean;
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
  /** Cooldowns for quick chat and emotes (GDD 11.2). */
  quickChatTs: number;
  emoteTs: number;
  /** Players who queued together (GDD 7.2); null for everyone else. */
  party: string | null;
  /** What the seat has equipped, read from the inventory at join and at each round start. */
  cosmetics: Record<string, string> | null;
}

/** What `matchJoinAttempt` decided for a session, consumed by `matchJoin`. */
export type Admission =
  | { readonly kind: "seat"; readonly seat: SeatIndex; readonly reattach: boolean }
  | { readonly kind: "spectator"; readonly seatNext?: boolean };

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
  /** `check` is the generated validator for `type`; a game applies it before reading fields. */
  decodeMove(
    type: string,
    payload: Record<string, unknown>,
    check: Check | undefined,
  ): Result<M, RuleError>;
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
  /**
   * Whether `seat` may ask for a hand now and whose (R31 to R36); the move the core applies for
   * the asker once the target accepts. Absent for games without hand-taking.
   */
  askHand?(
    state: S,
    seat: SeatIndex,
  ): Result<
    { readonly target: SeatIndex; readonly cardCount: number; readonly move: M },
    RuleError
  >;
  stateHash(state: S): string;
  /** Per-seat numbers for the stats card, read from the round's logged messages (GDD 10.1). */
  /**
   * A selection as the table keeps it: a preset's table settings merged under the explicit ones,
   * so a selection that names a preset alone runs that preset's timer and titles (GDD 6.13).
   */
  normaliseRules?(selection: RulesSelection): RulesSelection;
  highlights?(
    messages: readonly LoggedMessage[],
    seatCount: number,
  ): readonly Record<string, number>[];
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
  /** One quick-chat phrase or emote per sender per this many ms (GDD 11.2). */
  readonly quickChatCooldownMs: number;
  readonly emoteCooldownMs: number;
  /** Public tables: the countdown before the first deal and how long matched players may take to arrive (GDD 9.5). */
  readonly autoStartCountdownMs: number;
  readonly autoStartJoinWindowMs: number;
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
  quickChatCooldownMs: 3_000,
  emoteCooldownMs: 3_000,
  autoStartCountdownMs: 5_000,
  autoStartJoinWindowMs: 20_000,
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

/** Why a hand request ended; `accepted` is the only yes. */
export type HandAnswerReason = "accepted" | "declined" | "lead" | "timeout" | "left";

/**
 * A hand request waiting for the target's answer (R33, R34). The asker's `clientSeq` is answered
 * when it resolves: by the take's own move_applied, or by a move_rejected naming the reason.
 */
export interface HandAsk {
  asker: SeatIndex;
  target: SeatIndex;
  cardCount: number;
  clientSeq: number;
  deadlineTs: number;
}

export interface RoundRuntime<S, M> {
  readonly roundId: string;
  readonly index: number;
  readonly seedHex: string;
  readonly dealerSeat: SeatIndex;
  /** The PRNG words after the deal and every draw; rebuilt into an Rng per use (ADR-0009). */
  rngState: Xoshiro128State;
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
  /** The open hand request, if any. */
  handAsk: HandAsk | null;
  /** `moves.length` when a request was declined or lapsed: no second ask until a move is applied. */
  askClosedAtMove: number | null;
}

/** Plain data only: Nakama exports it to Go and rebuilds it between callbacks. */
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
  presences: Record<string, PresenceRecord>;
  admissions: Record<string, Admission>;
  /** User ids that may not rejoin (GDD 9.9). */
  kicked: string[];
  series: SeriesState | null;
  round: RoundRuntime<S, M> | null;
  roundsStarted: number;
  /** The last server sequence number; only public messages advance it. */
  seq: number;
  log: LoggedMessage[];
  rematchDeadlineTs: number | null;
  autoStart: AutoStart | null;
  readonly createdTs: number;
  emptySinceTs: number | null;
  closeReason: TableCloseReason | null;
}

/** A matchmade public table: deals itself once everyone is seated or the join window closes. */
export interface AutoStart {
  readonly expected: number;
  readonly joinDeadlineTs: number;
  startsAtTs: number | null;
  /** userId to party id for players who queued together. */
  readonly parties: Readonly<Record<string, string>>;
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

/**
 * A copy of the record without one key. Nakama hands the match state back as plain data after
 * every callback, so the state holds records, never Maps, and nothing is deleted in place.
 */
export function without<T>(record: Readonly<Record<string, T>>, key: string): Record<string, T> {
  const out: Record<string, T> = {};
  for (const [k, v] of Object.entries(record)) {
    if (k !== key) out[k] = v;
  }
  return out;
}

export function newBatch(): Batch {
  return { outbound: [], kicks: [], end: false };
}

export interface RoundRecord {
  readonly tableId: string;
  readonly roundId: string;
  readonly gameId: GameId;
  readonly rulesVersion: string;
  /** What the table ran: enough to replay the round from its seed and moves (ARCHITECTURE 8.11). */
  readonly rules: RulesSelection;
  readonly mode: TableMode;
  readonly dealerSeat: SeatIndex;
  readonly seedHex: string;
  readonly players: readonly (string | null)[];
  readonly moves: readonly { seat: SeatIndex; move: unknown; auto: boolean }[];
  readonly result: RoundResult | null;
  readonly series: SeriesState | null;
  readonly finalStateHash: string;
  /** Per seat, from the game host; empty objects when a game has none. */
  readonly highlights: readonly Record<string, number>[];
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
  /** Set by the matchmaker: how many players are on their way. */
  readonly autoStart?: { readonly expected: number };
  readonly parties?: readonly { readonly id: string; readonly members: readonly string[] }[];
}
