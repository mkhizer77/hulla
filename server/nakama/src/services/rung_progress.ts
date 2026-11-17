import { seatCover, type RewardContext, type RoundReward } from "./reward_types";

import type { AchievementMetric } from "../content/achievements";
import type { MissionMetric } from "../content/missions";
import type { RoundRecord } from "../match/types";
import type { ProfileStats } from "@hulla/protocol/generated";

/*
 * What a Rung deal counts for (Rung design 10, OQ-056): rewards, the stats card, achievements and
 * missions. A deal is roughly half a Thulla round, so a deal pays about half of one; the bazi's
 * end and a court add a bonus. Bots hold no account, so they never reach this code.
 */
type Counters = Partial<Record<AchievementMetric, number>>;
type Metrics = Partial<Record<MissionMetric, number>>;

export const RUNG_XP_DEAL_WON = 12;
export const RUNG_XP_DEAL_LOST = 6;
export const RUNG_COINS_DEAL_WON = 18;
export const RUNG_COINS_DEAL_LOST = 9;
export const RUNG_XP_COURT = 10;
export const RUNG_COINS_COURT = 15;
export const RUNG_XP_BAZI_WON = 30;
export const RUNG_XP_BAZI_LOST = 10;
export const RUNG_COINS_BAZI_WON = 40;
export const RUNG_COINS_BAZI_LOST = 10;
const FRIEND_BONUS = 0.2;
const DAILY_SOFT_CAP = 1_500;
const CAPPED_RATE = 0.2;
const STREAK_STEP = 0.1;
const STREAK_MAX = 0.5;

/** The facts of one seat's deal, read from the record without trusting its shape. */
export interface RungDealFacts {
  readonly won: boolean;
  readonly court: boolean;
  readonly court52: boolean;
  /** The winners were the dealer's team: a goon court when it is a court. */
  readonly byDealer: boolean;
  readonly caller: boolean;
  readonly onCallerTeam: boolean;
  readonly trump: "S" | "H" | "C" | "D" | null;
  readonly baziOver: boolean;
  readonly baziWon: boolean;
}

/** A stored record's field, whatever game wrote it: the record is plain data from storage. */
function field(source: object | null, key: string): unknown {
  return source === null ? undefined : (source as Readonly<Record<string, unknown>>)[key];
}

export function rungDealFacts(record: RoundRecord, seat: number): RungDealFacts | null {
  const winnerTeam = field(record.result, "winnerTeam");
  if (!record.completed || typeof winnerTeam !== "number") return null;
  const team = seat % 2;
  const kind = field(record.result, "kind");
  const callerSeat = field(record.result, "callerSeat");
  const trump = field(record.result, "trump");
  const baziOver = record.series?.finished === true;
  return {
    won: winnerTeam === team,
    court: kind === "court" || kind === "court52",
    court52: kind === "court52",
    byDealer: field(record.result, "byCaller") === false,
    caller: callerSeat === seat,
    onCallerTeam: typeof callerSeat === "number" && callerSeat % 2 === team,
    trump: trump === "S" || trump === "H" || trump === "C" || trump === "D" ? trump : null,
    baziOver,
    baziWon: baziOver && field(record.series, "winnerTeam") === team,
  };
}

/** XP and coins for one seat of a completed Rung deal. */
export function rungReward(
  record: RoundRecord,
  seat: number,
  ctx: RewardContext,
): RoundReward | null {
  const facts = rungDealFacts(record, seat);
  if (facts === null) return null;
  const courtXp = facts.won && facts.court ? RUNG_XP_COURT * (facts.court52 ? 2 : 1) : 0;
  const courtCoins = facts.won && facts.court ? RUNG_COINS_COURT * (facts.court52 ? 2 : 1) : 0;
  const baziXp = !facts.baziOver ? 0 : facts.baziWon ? RUNG_XP_BAZI_WON : RUNG_XP_BAZI_LOST;
  const baziCoins = !facts.baziOver
    ? 0
    : facts.baziWon
      ? RUNG_COINS_BAZI_WON
      : RUNG_COINS_BAZI_LOST;
  const dealXp = facts.won ? RUNG_XP_DEAL_WON : RUNG_XP_DEAL_LOST;
  const subtotal = dealXp + courtXp + baziXp;
  const friendXp = ctx.withFriend ? Math.round(subtotal * FRIEND_BONUS) : 0;
  const baseCoins =
    (facts.won ? RUNG_COINS_DEAL_WON : RUNG_COINS_DEAL_LOST) + courtCoins + baziCoins;
  const streak = Math.min(STREAK_MAX, Math.max(0, ctx.loginStreak - 1) * STREAK_STEP);
  const streakCoins = Math.round(baseCoins * streak);
  const capped = ctx.coinsToday >= DAILY_SOFT_CAP;
  return {
    xp: subtotal + friendXp,
    coins: capped ? Math.round((baseCoins + streakCoins) * CAPPED_RATE) : baseCoins + streakCoins,
    breakdown: {
      // Winners are "first" and the other pair "second": the client's reward line needs no more.
      finish: facts.won ? 0 : 1,
      bhabhi: false,
      baseXp: dealXp + baziXp,
      thullaXp: courtXp,
      cleanXp: 0,
      friendXp,
      baseCoins,
      streakCoins,
      capped,
    },
  };
}

export type RungStats = NonNullable<ProfileStats["rung"]>;

export const EMPTY_RUNG_STATS: RungStats = {
  deals: 0,
  dealsWon: 0,
  bazis: 0,
  bazisWon: 0,
  courts: 0,
  courtsAgainst: 0,
  callerDeals: 0,
  callerWins: 0,
  defenderDeals: 0,
  defenderWins: 0,
  tricksWon: 0,
  cuts: 0,
  pilesTaken: 0,
  trumps: { S: 0, H: 0, C: 0, D: 0 },
};

/** The Rung half of the stats card after one deal. */
export function applyRungToStats(
  stats: ProfileStats,
  seat: number,
  record: RoundRecord,
): ProfileStats {
  const facts = rungDealFacts(record, seat);
  if (facts === null) return stats;
  const r = stats.rung ?? EMPTY_RUNG_STATS;
  // A seat the bot mostly played keeps the team's result but none of the personal numbers (6.5).
  const own = seatCover(record, seat) === "none";
  const h = own ? (record.highlights[seat] ?? {}) : {};
  const trumps = { ...r.trumps };
  if (own && facts.caller && facts.trump !== null) trumps[facts.trump] += 1;
  return {
    ...stats,
    rung: {
      deals: r.deals + 1,
      dealsWon: r.dealsWon + (facts.won ? 1 : 0),
      bazis: r.bazis + (facts.baziOver ? 1 : 0),
      bazisWon: r.bazisWon + (facts.baziWon ? 1 : 0),
      courts: r.courts + (facts.won && facts.court ? 1 : 0),
      courtsAgainst: r.courtsAgainst + (!facts.won && facts.court ? 1 : 0),
      callerDeals: r.callerDeals + (facts.onCallerTeam ? 1 : 0),
      callerWins: r.callerWins + (facts.onCallerTeam && facts.won ? 1 : 0),
      defenderDeals: r.defenderDeals + (facts.onCallerTeam ? 0 : 1),
      defenderWins: r.defenderWins + (!facts.onCallerTeam && facts.won ? 1 : 0),
      tricksWon: r.tricksWon + (h["tricksWon"] ?? 0),
      cuts: r.cuts + (h["cuts"] ?? 0),
      pilesTaken: r.pilesTaken + (h["pilesTaken"] ?? 0),
      trumps,
    },
  };
}

/** Rung's lifetime counters and streaks for the achievements (Rung design 10). */
export function countRungDeal(
  counters: Counters,
  record: RoundRecord,
  seat: number,
  friendsAtTable: number,
): Counters {
  const facts = rungDealFacts(record, seat);
  if (facts === null) return counters;
  const n = (key: keyof Counters): number => counters[key] ?? 0;
  const wonCourt = facts.won && facts.court;
  return {
    ...counters,
    rung_deals_won: n("rung_deals_won") + (facts.won ? 1 : 0),
    rung_bazis: n("rung_bazis") + (facts.baziOver ? 1 : 0),
    rung_bazis_won: n("rung_bazis_won") + (facts.baziWon ? 1 : 0),
    rung_bazis_won_friend:
      n("rung_bazis_won_friend") + (facts.baziWon && friendsAtTable > 0 ? 1 : 0),
    rung_courts: n("rung_courts") + (wonCourt ? 1 : 0),
    rung_goon_courts: n("rung_goon_courts") + (wonCourt && facts.byDealer ? 1 : 0),
    rung_court52: n("rung_court52") + (facts.won && facts.court52 ? 1 : 0),
    // Calls won in a row: only the caller's own deals move it.
    rung_call_streak:
      !facts.caller || seatCover(record, seat) !== "none"
        ? n("rung_call_streak")
        : facts.won
          ? n("rung_call_streak") + 1
          : 0,
    rounds_with_friend: n("rounds_with_friend") + (friendsAtTable > 0 ? 1 : 0),
  };
}

/** What one seat of a Rung deal counts for in the missions. */
export function rungMetrics(record: RoundRecord, seat: number, withFriend: boolean): Metrics {
  const facts = rungDealFacts(record, seat);
  if (facts === null) return {};
  const h = seatCover(record, seat) === "none" ? (record.highlights[seat] ?? {}) : {};
  return {
    rounds_played: 1,
    rounds_with_friend: withFriend ? 1 : 0,
    rung_deals_won: facts.won ? 1 : 0,
    rung_piles: h["pilesTaken"] ?? 0,
    rung_cuts: h["cuts"] ?? 0,
    rung_courts: facts.won && facts.court ? 1 : 0,
    rung_bazis_won: facts.baziWon ? 1 : 0,
  };
}
