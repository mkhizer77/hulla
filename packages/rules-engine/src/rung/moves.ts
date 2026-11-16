import type { Card, Suit } from "../card-kit";

/** A player's intent; the table core validates the envelope, the engine validates the rules. */
export type RungMove =
  | { readonly type: "call_trump"; readonly suit: Suit }
  | { readonly type: "redeal" }
  | { readonly type: "play_card"; readonly card: Card }
  | { readonly type: "court_choice"; readonly playOn: boolean };
