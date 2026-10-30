import { ErrorCodes, ruleError } from "@hulla/shared-ts";

import { activeSeats, occupiedSeats } from "./view";

import type { GameHost, PresenceRecord, PresenceStatus, SeatSlot, TableState } from "./types";
import type { RulesSelection } from "@hulla/protocol/generated";
import type { Result, RuleError, SeatIndex } from "@hulla/shared-ts";

export function emptySlot(): SeatSlot {
  return {
    playerId: null,
    displayName: null,
    sessionId: null,
    ready: false,
    connected: false,
    autoPlay: false,
    left: false,
    resumeToken: null,
    joinedTs: 0,
    disconnectedTs: null,
    absentMs: 0,
    awaySinceTs: null,
    timeouts: 0,
    rematch: null,
    quickChatTs: 0,
    emoteTs: 0,
    mic: false,
    party: null,
    cosmetics: null,
    emotePacks: null,
    bot: false,
    avatar: null,
  };
}

export function presencePayload(
  presence: PresenceRecord,
  displayName: string | null,
  status: PresenceStatus,
  deadlineTs?: number,
): object {
  const base = { seat: presence.seat, playerId: presence.userId, status };
  const named = displayName === null ? base : { ...base, displayName };
  return deadlineTs === undefined ? named : { ...named, deadlineTs };
}

export function isHost<S, M>(state: TableState<S, M>, seat: SeatIndex | null): boolean {
  return seat !== null && state.hostSeat === seat;
}

/** The longest-seated connected human takes over (GDD 9.9, E17). */
export function migrateHost<S, M>(state: TableState<S, M>): SeatIndex | null {
  const candidates = activeSeats(state.seats).sort((a, b) => {
    const left = state.seats[a]?.joinedTs ?? 0;
    const right = state.seats[b]?.joinedTs ?? 0;
    return left - right || a - b;
  });
  const next = candidates[0] ?? null;
  state.hostSeat = next;
  return next;
}

export function freeSlot<S, M>(state: TableState<S, M>, seat: SeatIndex): void {
  state.seats[seat] = emptySlot();
}

/** Whether the host may deal now (GDD 9.5): enough active seats and every one of them ready. */
export function canStart<S, M, V, E, R>(
  state: TableState<S, M>,
  host: GameHost<S, M, V, E, R>,
): Result<true, RuleError> {
  const active = activeSeats(state.seats);
  if (active.length < host.module.seats.min) {
    return {
      ok: false,
      error: ruleError(ErrorCodes.rules.invalidSeatCount, {
        seats: active.length,
        min: host.module.seats.min,
      }),
    };
  }
  const unready = active.filter((seat) => state.seats[seat]?.ready !== true);
  if (unready.length > 0 && state.phase === "lobby") {
    return {
      ok: false,
      error: ruleError(ErrorCodes.table.notAuthorised, {
        reason: "seats_not_ready",
        seats: unready.length,
      }),
    };
  }
  return { ok: true, value: true };
}

/** Top-level keys whose value changed, for the diff banner (E19). */
export function changedRuleKeys(before: RulesSelection, after: RulesSelection): string[] {
  const keys = new Set<string>();
  if (before.preset !== after.preset) keys.add("preset");
  const sections = ["overrides", "settings"] as const;
  for (const section of sections) {
    const a = (before[section] ?? {}) as Record<string, unknown>;
    const b = (after[section] ?? {}) as Record<string, unknown>;
    const names = new Set([...Object.keys(a), ...Object.keys(b)]);
    for (const name of names) {
      if (JSON.stringify(a[name]) !== JSON.stringify(b[name])) keys.add(`${section}.${name}`);
    }
  }
  return [...keys].sort();
}

/** Count of seated players who are present: public tables dissolve under two (R46). */
export function humansPresent<S, M>(state: TableState<S, M>): number {
  return activeSeats(state.seats).filter((seat) => state.seats[seat]?.bot !== true).length;
}

/** After a round: free the seats that left, and in public tables the ones that never returned. */
export function settleSeatsAfterRound<S, M>(state: TableState<S, M>): SeatIndex[] {
  const freed: SeatIndex[] = [];
  for (const seat of occupiedSeats(state.seats)) {
    const slot = state.seats[seat];
    if (slot === undefined) continue;
    if (slot.left || (!slot.connected && state.mode !== "private")) {
      freeSlot(state, seat);
      freed.push(seat);
      continue;
    }
    if (slot.bot) {
      // A bot stays on auto-play and votes for the next round at once.
      slot.timeouts = 0;
      slot.ready = true;
      slot.rematch = true;
      continue;
    }
    slot.autoPlay = false;
    slot.timeouts = 0;
    slot.ready = false;
    slot.rematch = null;
  }
  if (state.hostSeat !== null && state.seats[state.hostSeat]?.playerId === null) migrateHost(state);
  return freed;
}
