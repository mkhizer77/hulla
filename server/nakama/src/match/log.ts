import { newBatch, type Batch, type Envelope, type LoggedMessage, type TableState } from "./types";

import type { SeatIndex } from "@hulla/shared-ts";

/**
 * Sequence numbers and the message log (ARCHITECTURE 8.4 step 6). Only public messages advance
 * `seq`, so every recipient sees a gap-free stream; a seat-only message carries the current seq
 * and is logged against its seat for replay. Session-only answers (errors, pong, snapshots,
 * table_state) are never logged.
 */
export interface Emitter {
  readonly batch: Batch;
  public(type: string, payload: object): Envelope;
  seat(seat: SeatIndex, type: string, payload: object): Envelope | null;
  session(sessionId: string, type: string, payload: object): Envelope;
}

export function emitter<S, M>(
  state: TableState<S, M>,
  now: number,
  stateHash: () => string | null,
  batch: Batch = newBatch(),
): Emitter {
  const envelope = (type: string, payload: object, seq: number): Envelope => ({
    v: 1,
    type,
    tableId: state.tableId,
    seq,
    ts: now,
    payload,
  });
  const log = (env: Envelope, seat: SeatIndex | null): void => {
    const entry: LoggedMessage = {
      seq: env.seq,
      ts: env.ts,
      type: env.type,
      payload: env.payload,
      seat,
      stateHash: stateHash(),
    };
    state.log.push(entry);
    if (state.round !== null) state.round.sinceSnapshot += 1;
  };
  return {
    batch,
    public(type, payload) {
      state.seq += 1;
      const env = envelope(type, payload, state.seq);
      log(env, null);
      batch.outbound.push({ envelope: env, to: { kind: "all" } });
      return env;
    },
    seat(seat, type, payload) {
      const env = envelope(type, payload, state.seq);
      log(env, seat);
      const sessionId = state.seats[seat]?.sessionId ?? null;
      if (sessionId === null) return null;
      batch.outbound.push({ envelope: env, to: { kind: "session", sessionId } });
      return env;
    },
    session(sessionId, type, payload) {
      const env = envelope(type, payload, state.seq);
      batch.outbound.push({ envelope: env, to: { kind: "session", sessionId } });
      return env;
    },
  };
}

/** Logged messages a viewer may replay after `sinceSeq`: public ones and its own seat's. */
export function replayFor<S, M>(
  state: TableState<S, M>,
  viewerSeat: SeatIndex | null,
  sinceSeq: number,
): Envelope[] {
  return state.log
    .filter((m) => m.seq > sinceSeq && (m.seat === null || m.seat === viewerSeat))
    .map((m) => ({
      v: 1 as const,
      type: m.type,
      tableId: state.tableId,
      seq: m.seq,
      ts: m.ts,
      payload: m.payload,
    }));
}

/** Snapshot the engine state against the log when enough messages piled up or a phase changed. */
export function maybeSnapshot<S, M>(
  state: TableState<S, M>,
  hash: (s: S) => string,
  every: number,
  force: boolean,
): void {
  const round = state.round;
  if (round === null) return;
  if (!force && round.sinceSnapshot < every) return;
  round.snapshots.push({ seq: state.seq, stateHash: hash(round.state), state: round.state });
  round.sinceSnapshot = 0;
}
