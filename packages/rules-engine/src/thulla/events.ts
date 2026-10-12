import type { SeatIndex } from "@hulla/shared-ts";

/** Events are the only thing clients render; the table core logs them with seq and stateHash. */
export interface ThullaEvent {
  readonly type: "round_started";
  readonly commitment: string;
  readonly seatCount: number;
  readonly dealerSeat: SeatIndex;
  readonly leaderSeat: SeatIndex;
  readonly handCounts: readonly number[];
}
