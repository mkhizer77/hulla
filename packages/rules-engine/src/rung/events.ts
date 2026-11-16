import type { Card, Suit } from "../card-kit";
import type { RungOutcomeKind, RungTeam } from "./state";
import type { SeatIndex } from "@hulla/shared-ts";

/**
 * Events are the only thing clients render; the table core logs them with seq and stateHash.
 * All of them are public: a seat learns its own cards from its view, never from an event.
 */
export type RungEvent =
  | {
      readonly type: "round_started";
      readonly commitment: string;
      readonly dealerSeat: SeatIndex;
      readonly callerSeat: SeatIndex;
      readonly handCounts: readonly number[];
    }
  | { readonly type: "redealt"; readonly commitment: string; readonly redeals: number }
  | { readonly type: "trump_called"; readonly seat: SeatIndex; readonly suit: Suit }
  | { readonly type: "hands_completed"; readonly handCounts: readonly number[] }
  | {
      readonly type: "card_played";
      readonly seat: SeatIndex;
      readonly card: Card;
      readonly trickIndex: number;
      /** A kaat: a rung on a led suit the seat was void in. */
      readonly cut: boolean;
    }
  | {
      readonly type: "trick_won";
      readonly trickIndex: number;
      readonly winner: SeatIndex;
      readonly cards: readonly Card[];
      /** Tricks lying in the centre after this one, before any take (Double Sar). */
      readonly pile: number;
      readonly counts: readonly [number, number];
    }
  | {
      readonly type: "pile_taken";
      readonly seat: SeatIndex;
      readonly team: RungTeam;
      readonly tricks: number;
      readonly counts: readonly [number, number];
    }
  | { readonly type: "court_choice_opened"; readonly seat: SeatIndex; readonly team: RungTeam }
  | { readonly type: "court_choice_made"; readonly seat: SeatIndex; readonly playOn: boolean }
  | {
      readonly type: "round_ended";
      readonly team: RungTeam;
      readonly kind: RungOutcomeKind;
      readonly counts: readonly [number, number];
    };
