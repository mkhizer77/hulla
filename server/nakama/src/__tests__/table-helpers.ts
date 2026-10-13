import {
  tableCore,
  type NameSource,
  type PresenceInfo,
  type SocialGate,
  type TableCore,
} from "../match/table_core";

import { toyHost, type ToyMove, type ToyState } from "./toy-game";

import type { Batch, Envelope, RoundRecord, TableConfig, TableState } from "../match/types";

export const T0 = 1_760_200_000_000;

export interface Harness {
  readonly core: TableCore<ToyState, ToyMove>;
  readonly state: TableState<ToyState, ToyMove>;
  readonly records: RoundRecord[];
  now: number;
  /** Join a presence through joinAttempt and join; returns the join batch. */
  join(presence: PresenceInfo, metadata?: Record<string, unknown>): Batch;
  /** Send one client message as a presence; the envelope seq is the client's own counter. */
  send(sessionId: string, type: string, payload: object, seq?: number): Batch;
  tick(advanceMs?: number): Batch;
}

export function presence(i: number): PresenceInfo {
  return { sessionId: `s${String(i)}`, userId: `u${String(i)}`, username: `Player${String(i)}` };
}

export function harness(
  options: {
    readonly mode?: "private" | "public" | "ranked";
    readonly config?: Partial<TableConfig>;
    readonly presentationDelayMs?: number;
    readonly settings?: Record<string, unknown>;
    readonly target?: number;
    readonly social?: SocialGate;
    readonly names?: NameSource;
  } = {},
): Harness {
  const records: RoundRecord[] = [];
  let uuids = 0;
  const core = tableCore(toyHost({ presentationDelayMs: options.presentationDelayMs ?? 0 }), {
    ids: {
      uuid: () => {
        uuids += 1;
        return `00000000-0000-4000-8000-${String(uuids).padStart(12, "0")}`;
      },
    },
    persist: { roundRecord: (record) => records.push(record) },
    ...(options.config === undefined ? {} : { config: options.config }),
    ...(options.social === undefined ? {} : { social: options.social }),
    ...(options.names === undefined ? {} : { names: options.names }),
  });
  const state = core.create(
    {
      code: "ABCDEF",
      gameId: "thulla",
      mode: options.mode ?? "private",
      rules: {
        gameId: "thulla",
        ...(options.settings === undefined ? {} : { settings: options.settings }),
        ...(options.target === undefined ? {} : { overrides: { target: options.target } }),
      },
    },
    T0,
  );
  const h: Harness = {
    core,
    state,
    records,
    now: T0,
    join(p, metadata = {}) {
      const decision = core.joinAttempt(state, p, metadata, h.now);
      if (!decision.accept) throw new Error(`join rejected: ${decision.code}`);
      return core.join(state, [p], h.now);
    },
    send(sessionId, type, payload, seq = 1) {
      const envelope: Envelope = { v: 1, type, tableId: state.tableId, seq, ts: h.now, payload };
      return core.inbound(state, sessionId, JSON.stringify(envelope), h.now, type);
    },
    tick(advanceMs = 0) {
      h.now += advanceMs;
      return core.tick(state, h.now);
    },
  };
  return h;
}

/** Seat `count` players and mark them ready; the first joiner is the host. */
export function seated(h: Harness, count: number): void {
  for (let i = 0; i < count; i += 1) h.join(presence(i));
  for (let i = 0; i < count; i += 1) h.send(`s${String(i)}`, "ready", { ready: true });
}

export function started(h: Harness, count = 3): Batch {
  seated(h, count);
  return h.send("s0", "start_round", {});
}

/** Play every seat's opening number so the toy game reaches its turn-based phase. */
export function opened(h: Harness, count = 3): void {
  started(h, count);
  const roundId = h.state.round?.roundId ?? "";
  for (let i = 0; i < count; i += 1) {
    h.send(`s${String(i)}`, "play_card", { roundId, clientSeq: 1, card: 1 });
  }
}

export function types(batch: Batch): string[] {
  return batch.outbound.map((o) => o.envelope.type);
}

export function sentTo(batch: Batch, sessionId: string): Envelope[] {
  return batch.outbound
    .filter((o) => o.to.kind === "session" && o.to.sessionId === sessionId)
    .map((o) => o.envelope);
}

export function broadcasts(batch: Batch): Envelope[] {
  return batch.outbound.filter((o) => o.to.kind === "all").map((o) => o.envelope);
}

export function payloadOf<T>(envelope: Envelope | undefined): T {
  if (envelope === undefined) throw new Error("no envelope");
  return envelope.payload as T;
}
