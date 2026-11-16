import { canonicalJson, sha256Hex, utf8Encode } from "@hulla/shared-ts";

import type { Card } from "./card";

/** SHA-256 over the raw seed bytes followed by the UTF-8 canonical JSON of the deck order (GDD R6). */
export function fairDealCommitment(seed: Uint8Array, deckOrder: readonly Card[]): string {
  const order = utf8Encode(canonicalJson(deckOrder));
  const message = new Uint8Array(seed.length + order.length);
  message.set(seed);
  message.set(order, seed.length);
  return sha256Hex(message);
}
