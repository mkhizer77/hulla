import { ErrorCodes } from "@hulla/shared-ts";

import { BUNDLED_RANKED, type RankedConfig } from "../content/ranked";
import { log } from "../infra/log";
import { excludeBlocked, excludedIds, nkBlockStore } from "../services/blocks";
import { isDraining } from "../services/ops";
import { currentState } from "../services/ranked";

export interface MatchmakerTicketShape {
  query: string;
  minCount: number;
  maxCount: number;
  stringProperties: Record<string, string>;
  numericProperties: Record<string, number>;
}

/**
 * A ranked ticket (GDD 7.5, 9.6) is rebuilt on the server: the stored rating, not the client's,
 * goes on the ticket, the band the client asks for is held between the content's floor and cap,
 * and the table size is the ranked size. Solo queue only; parties are refused in their own hook.
 */
export function rankedTicket<T extends MatchmakerTicketShape>(
  ticket: T,
  rating: number,
  config: RankedConfig = BUNDLED_RANKED,
): T {
  const asked = ticket.numericProperties["band"];
  const band = Math.min(
    config.matchmaking.maxBand,
    Math.max(config.matchmaking.band, typeof asked === "number" ? Math.floor(asked) : 0),
  );
  const lo = Math.max(0, rating - band);
  const hi = rating + band;
  return {
    ...ticket,
    query: `+properties.game:${config.gameId} +properties.mode:ranked +properties.rating:>=${String(lo)} +properties.rating:<=${String(hi)}`,
    minCount: config.seats,
    maxCount: config.seats,
    stringProperties: { game: config.gameId, mode: "ranked" },
    numericProperties: { rating, band },
  };
}

export function isRankedTicket(stringProperties: Record<string, string> | undefined): boolean {
  return stringProperties?.["mode"] === "ranked";
}

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
  if (isRankedTicket(envelope.matchmakerAdd.stringProperties)) {
    const rating = currentState(nk, logger, userId, Date.now()).rating;
    envelope.matchmakerAdd = rankedTicket(envelope.matchmakerAdd, rating);
  }
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

/** Registered with `registerRtBefore("PartyMatchmakerAdd", ...)`: Ranked is solo queue only (GDD 7.5). */
export function beforePartyMatchmakerAdd(
  _ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  envelope: nkruntime.EnvelopePartyMatchmakerAdd,
): nkruntime.EnvelopePartyMatchmakerAdd {
  if (isDraining(nk)) throw new Error(ErrorCodes.table.draining);
  if (isRankedTicket(envelope.partyMatchmakerAdd.stringProperties)) {
    throw new Error(ErrorCodes.table.rankedSoloOnly);
  }
  return envelope;
}
