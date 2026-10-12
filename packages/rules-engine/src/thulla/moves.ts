import type { Card } from "../card-kit";

/** A player's intent; the table core validates the envelope, the engine validates the rules. */
export type ThullaMove =
  { readonly type: "play_card"; readonly card: Card } | { readonly type: "take_hand" };
