/**
 * Game metrics on Nakama's Prometheus endpoint (GDD 20.13, ARCHITECTURE 8.12): counters and
 * timers per game, named `hulla_*` here and exposed as `nakama_custom_hulla_*`. A metrics call
 * that fails never breaks a callback.
 */
export const NS_PER_MS = 1_000_000;
export const METRICS = {
  rounds: "hulla_rounds_started",
  roundsEnded: "hulla_rounds_ended",
  /** A timer: Nakama takes nanoseconds and exposes seconds as a summary. */
  roundDuration: "hulla_round_duration",
  moves: "hulla_moves",
  timeouts: "hulla_timeouts",
  thullas: "hulla_thullas",
  disconnects: "hulla_disconnects",
  reconnects: "hulla_reconnects",
  messagesDropped: "hulla_messages_dropped",
  tablesCreated: "hulla_tables_created",
  rpcErrors: "hulla_rpc_errors",
  pushSent: "hulla_push_sent",
  pushFailed: "hulla_push_failed",
  invites: "hulla_invites",
  purchases: "hulla_purchases",
  purchasesRejected: "hulla_purchases_rejected",
} as const;

export interface MetricsSink {
  metricsCounterAdd(name: string, tags: Record<string, string>, delta: number): void;
  metricsTimerRecord(name: string, tags: Record<string, string>, value: number): void;
  metricsGaugeSet(name: string, tags: Record<string, string>, value: number): void;
}

export const metrics = {
  count(sink: Partial<MetricsSink>, name: string, tags: Record<string, string>, delta = 1): void {
    try {
      sink.metricsCounterAdd?.(name, tags, delta);
    } catch {
      // metrics never break the game
    }
  },
  /** `ms` is milliseconds; Nakama's timer takes a duration in nanoseconds. */
  time(sink: Partial<MetricsSink>, name: string, tags: Record<string, string>, ms: number): void {
    try {
      sink.metricsTimerRecord?.(name, tags, Math.round(ms * NS_PER_MS));
    } catch {
      // metrics never break the game
    }
  },
  gauge(
    sink: Partial<MetricsSink>,
    name: string,
    tags: Record<string, string>,
    value: number,
  ): void {
    try {
      sink.metricsGaugeSet?.(name, tags, value);
    } catch {
      // metrics never break the game
    }
  },
};
