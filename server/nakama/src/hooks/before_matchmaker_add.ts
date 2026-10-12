import { excludeBlocked, excludedIds, nkBlockStore } from "../services/blocks";

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
    logger.warn("matchmaker ticket not filtered", { error: String(error) });
  }
  return envelope;
}
