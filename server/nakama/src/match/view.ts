import type { GameHost, PresenceRecord, SeatSlot, TableState } from "./types";
import type {
  Seat as SeatPayload,
  SeriesState as SeriesPayload,
  TableState as TableStatePayload,
} from "@hulla/protocol/generated";
import type { SeriesState, Viewer } from "@hulla/rules-engine";
import type { SeatIndex } from "@hulla/shared-ts";

export type TableStateFor<V> = Omit<TableStatePayload, "round"> & { readonly round: V | null };

export function occupiedSeats(seats: readonly SeatSlot[]): SeatIndex[] {
  return seats.flatMap((slot, index) => (slot.playerId === null ? [] : [index]));
}

/** Seats with a player who is present and has not left (what a round can be dealt to). */
export function activeSeats(seats: readonly SeatSlot[]): SeatIndex[] {
  return seats.flatMap((slot, index) =>
    slot.playerId !== null && slot.connected && !slot.left ? [index] : [],
  );
}

export function freeSeat(seats: readonly SeatSlot[]): SeatIndex | null {
  const index = seats.findIndex((slot) => slot.playerId === null);
  return index === -1 ? null : index;
}

export function spectatorCount(presences: Readonly<Record<string, PresenceRecord>>): number {
  return Object.values(presences).filter((p) => p.seat === null).length;
}

export function tableStatus<S, M>(state: TableState<S, M>): TableStatePayload["status"] {
  if (state.phase === "closing") return "closed";
  if (state.phase === "inRound") return "inRound";
  if (state.lockedByHost || freeSeat(state.seats) === null) return "locked";
  return "open";
}

export function emptySeries(seatCount: number, firstDealer: SeatIndex): SeriesState {
  return {
    roundsPlayed: 0,
    points: Array.from({ length: seatCount }, () => 0),
    loserTally: Array.from({ length: seatCount }, () => 0),
    nextDealerSeat: firstDealer,
    finished: false,
  };
}

export function seriesPayload(series: SeriesState): SeriesPayload {
  return {
    roundsPlayed: series.roundsPlayed,
    points: [...series.points],
    loserTally: [...series.loserTally],
    nextDealerSeat: series.nextDealerSeat,
    finished: series.finished,
  };
}

export function viewerFor(seat: SeatIndex | null): Viewer {
  return seat === null ? { kind: "spectator" } : { kind: "seat", seat };
}

function seatPayload(slot: SeatSlot, index: number): SeatPayload {
  return {
    seat: index,
    playerId: slot.playerId,
    displayName: slot.displayName,
    ready: slot.ready,
    connected: slot.connected,
    autoPlay: slot.autoPlay,
    party: slot.party,
    cosmetics: slot.cosmetics,
  };
}

/** The `table_state` message as one presence may see it (ARCHITECTURE 8.6). */
export function tableStateFor<S, M, V, E, R>(
  state: TableState<S, M>,
  host: GameHost<S, M, V, E, R>,
  presence: PresenceRecord,
): TableStateFor<V> {
  const seat = presence.seat;
  const slot = seat === null ? null : (state.seats[seat] ?? null);
  const round =
    state.round === null ? null : host.module.redact(state.round.state, viewerFor(seat));
  const base: TableStateFor<V> = {
    code: state.code,
    gameId: state.gameId,
    mode: state.mode,
    status: tableStatus(state),
    hostSeat: state.hostSeat,
    rules: state.rules,
    seats: state.seats.map(seatPayload),
    spectatorCount: spectatorCount(state.presences),
    spectators: Object.values(state.presences)
      .filter((p) => p.seat === null)
      .map((p) => ({
        userId: p.userId,
        displayName: displayNameOf(p.username),
        seatNext: p.seatNext === true,
      })),
    series: seriesPayload(state.series ?? emptySeries(state.seats.length, state.hostSeat ?? 0)),
    round,
    lastSeq: state.seq,
    resumeToken: slot?.resumeToken ?? null,
  };
  const starting = state.autoStart?.startsAtTs ?? null;
  const withStart = starting === null ? base : { ...base, autoStart: { startsAtTs: starting } };
  if (state.phase !== "roundEnded" || state.rematchDeadlineTs === null) return withStart;
  return {
    ...withStart,
    rematch: {
      deadlineTs: state.rematchDeadlineTs,
      votes: state.seats.flatMap((s, index) => (s.rematch === true ? [index] : [])),
    },
  };
}

/** The display name a presence shows at the table: 2 to 20 characters or nothing. */
export function displayNameOf(username: string): string | null {
  const trimmed = username.trim().slice(0, 20);
  return trimmed.length < 2 ? null : trimmed;
}
