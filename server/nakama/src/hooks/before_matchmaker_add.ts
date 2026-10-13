import { ErrorCodes } from "@hulla/shared-ts";

import { log } from "../infra/log";
import { excludeBlocked, excludedIds, nkBlockStore } from "../services/blocks";
import { isDraining } from "../services/ops";

/**
 * Registered with `registerRtBefore("MatchmakerAdd", ...)`: every ticket names its owner and
 * excludes the players blocked either way, so the pair never shares a Quick Play table (GDD 10.11).
 */
export function beforeMatchmakerAdd(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  envelope: nkruntime.EnvelopeMatchmakerAdd,
): nkruntime.EnvelopeMatchmakerAdd {
  const userId = ctx.userId ?? "";
  if (userId === "") return envelope;
  // A draining node takes no new searches; the client shows the maintenance line (GDD 20.15).
  if (isDraining(nk)) throw new Error(ErrorCodes.table.draining);
  try {
    const excluded = excludedIds(nkBlockStore(nk), userId);
    const ticket = excludeBlocked(
      {
        query: envelope.matchmakerAdd.query,
        stringProperties: envelope.matchmakerAdd.stringProperties,
      },
      userId,
      excluded,
    );
    envelope.matchmakerAdd.query = ticket.query;
    envelope.matchmakerAdd.stringProperties = ticket.stringProperties;
  } catch (error: unknown) {
    log.warn(logger, "matchmaker ticket not filtered", { error: String(error) });
  }
  return envelope;
}
