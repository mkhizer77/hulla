import { AVATAR_IDS, DEFAULT_AVATAR } from "../content/avatars";

import type { SeatSlot, TableState } from "./types";

/**
 * The avatar a seat shows (GDD 15.6): the player's own unless another seat already shows it, else
 * the first of the set nobody at the table shows. Fixed when the player sits, so it never changes
 * under them during a round.
 */
export function pickSeatAvatar(
  seats: readonly SeatSlot[],
  seat: number,
  own: string | null,
): string {
  const taken = new Set<string>();
  seats.forEach((s, i) => {
    if (i !== seat && s.playerId !== null && s.avatar !== null) taken.add(s.avatar);
  });
  if (own !== null && !taken.has(own)) return own;
  return AVATAR_IDS.find((id) => !taken.has(id)) ?? own ?? DEFAULT_AVATAR;
}

/** Gives every seated player without an avatar one, in seat order; `own` reads the profile's choice. */
export function fillSeatAvatars<S, M>(
  state: TableState<S, M>,
  own: (userId: string) => string | null,
): boolean {
  let changed = false;
  state.seats.forEach((slot, seat) => {
    if (slot.playerId === null || slot.avatar !== null) return;
    slot.avatar = pickSeatAvatar(state.seats, seat, own(slot.playerId));
    changed = true;
  });
  return changed;
}

export function needsSeatAvatars<S, M>(state: TableState<S, M>): boolean {
  return state.seats.some((s) => s.playerId !== null && s.avatar === null);
}
