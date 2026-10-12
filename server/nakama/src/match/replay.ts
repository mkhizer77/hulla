import {
  ErrorCodes,
  hexToBytes,
  ruleError,
  stateFromSeed,
  Xoshiro128StarStar,
} from "@hulla/shared-ts";

import type { GameHost, RoundRecord } from "./types";
import type { Result, RuleError } from "@hulla/shared-ts";

export interface Replay {
  readonly roundId: string;
  readonly moves: number;
  readonly recordedHash: string;
  readonly replayedHash: string;
  readonly matches: boolean;
  readonly result: unknown;
}

/**
 * Reproduces a round from its log: the same rules, seed and dealer give the same deal (ADR-0009),
 * and the same moves give the same final state, so the hashes agree when nothing was tampered
 * with (ARCHITECTURE 8.11). A move the engine refuses stops the replay with its error.
 */
export function replayRound<S, M, V, E, R>(
  host: GameHost<S, M, V, E, R>,
  record: RoundRecord,
): Result<Replay, RuleError> {
  const rules = host.resolveRules(record.rules, record.mode);
  if (!rules.ok) return rules;
  const seed = hexToBytes(record.seedHex);
  const rng = Xoshiro128StarStar.fromState(stateFromSeed(seed));
  const created = host.module.createRound(
    { rules: rules.value, seatCount: record.players.length, dealerSeat: record.dealerSeat, seed },
    rng,
  );
  if (!created.ok) return created;
  let state = created.value;
  for (const entry of record.moves) {
    const applied = host.module.applyMove(state, entry.seat, entry.move as M, rng);
    if (!applied.ok) {
      return {
        ok: false,
        error: ruleError(ErrorCodes.table.invalidPayload, {
          reason: "move",
          seat: entry.seat,
          detail: applied.error.code,
        }),
      };
    }
    state = applied.state;
  }
  const replayedHash = host.stateHash(state);
  return {
    ok: true,
    value: {
      roundId: record.roundId,
      moves: record.moves.length,
      recordedHash: record.finalStateHash,
      replayedHash,
      matches: replayedHash === record.finalStateHash,
      result: host.module.isRoundOver(state) ? host.module.results(state) : null,
    },
  };
}
