import type { Card, Suit } from "../card-kit";
import type { SeatIndex } from "@hulla/shared-ts";

/**
 * Events are the only thing clients render; the table core logs them with seq and stateHash.
 * Everything is public except `private_draw`, which names the seats allowed to see the card.
 */
export type ThullaEvent =
  | {
      readonly type: "round_started";
      readonly commitment: string;
      readonly seatCount: number;
      readonly dealerSeat: SeatIndex;
      readonly leaderSeat: SeatIndex;
      readonly handCounts: readonly number[];
    }
  | {
      readonly type: "card_played";
      readonly seat: SeatIndex;
      readonly card: Card;
      readonly trickIndex: number;
      readonly thulla: boolean;
    }
  | {
      readonly type: "trick_resolved";
      readonly trickIndex: number;
      readonly ledSuit: Suit;
      readonly cards: readonly Card[];
      readonly thullaBy: SeatIndex | null;
      /** The seat that took the pile, or null when it went to the discard (R21, R22). */
      readonly picker: SeatIndex | null;
      readonly nextLeader: SeatIndex;
      readonly handCounts: readonly number[];
    }
  | { readonly type: "escape"; readonly seat: SeatIndex; readonly rank: number }
  | {
      readonly type: "private_draw";
      readonly seat: SeatIndex;
      readonly card: Card;
      readonly source: "discard" | "nextPlayer" | "opponent";
      /** The seat the card left, who sees which card went (R28, R38); null for the discard. */
      readonly from: SeatIndex | null;
      readonly visibleTo: readonly SeatIndex[];
    }
  | {
      readonly type: "hand_taken";
      readonly taker: SeatIndex;
      readonly target: SeatIndex;
      readonly cardCount: number;
    }
  | { readonly type: "endgame_started"; readonly seats: readonly SeatIndex[] }
  | {
      readonly type: "round_ended";
      readonly bhabhiSeat: SeatIndex;
      readonly escapeOrder: readonly SeatIndex[];
    };
