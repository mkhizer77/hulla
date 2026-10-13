import { ErrorCodes, ruleError, type RuleError } from "@hulla/shared-ts";

import { BUNDLED_SEASONS, type PassReward, type Season } from "../content/seasons";

import { grant as grantItem, nkCatalogStore } from "./catalog";
import { credit, type LedgerPorts } from "./ledger";
import { nkProgressStore } from "./progression";

/**
 * The season pass (GDD 12.10): forty tiers driven by pass XP from rounds and missions, a free
 * and a premium track, Premium+ with ten tier skips, catch-up XP in the final two weeks. Claims
 * pay through the ledger and the inventory once per tier and track. The pass never gates play.
 */
export const PASS_COLLECTION = "season_pass";
export const TIER_COUNT = 40;
export const PLUS_SKIPS = 10;
const DAY_MS = 24 * 60 * 60 * 1000;

export type Track = "free" | "premium";
export type XpSource = "round" | "dailyMission" | "weeklyMission";

export interface PassState {
  readonly seasonId: string;
  readonly xp: number;
  readonly premium: boolean;
  readonly plus: boolean;
  /** `${tier}:${track}` to the claim time. */
  readonly claimed: Readonly<Record<string, number>>;
  readonly updatedTs: number;
}

export function emptyPass(seasonId: string): PassState {
  return { seasonId, xp: 0, premium: false, plus: false, claimed: {}, updatedTs: 0 };
}

/** The season running now, or null between seasons. */
export function currentSeason(seasons: readonly Season[], now: number): Season | null {
  return seasons.find((s) => s.startTs <= now && now < s.endTs) ?? null;
}

export function tierFor(xp: number, season: Season): number {
  return Math.min(TIER_COUNT, Math.floor(Math.max(0, xp) / season.xpPerTier));
}

/** Catch-up: every source pays more in the final days (GDD 12.10). */
export function xpFor(source: XpSource, season: Season, now: number): number {
  const base = season.xp[source];
  const catchUp = now >= season.endTs - season.xp.catchUpDays * DAY_MS;
  return catchUp ? Math.round(base * season.xp.catchUpMultiplier) : base;
}

/** Keeps the state on the season running now; a new season starts the pass afresh. */
export function forSeason(state: PassState | null, season: Season): PassState {
  return state !== null && state.seasonId === season.id ? state : emptyPass(season.id);
}

export function addPassXp(
  state: PassState,
  season: Season,
  source: XpSource,
  now: number,
): PassState {
  const gained = xpFor(source, season, now);
  if (gained === 0) return state;
  const cap = TIER_COUNT * season.xpPerTier;
  return { ...state, xp: Math.min(cap, state.xp + gained), updatedTs: now };
}

/** Premium opens the premium track for every tier already passed; Premium+ also skips ten tiers. */
export function activatePremium(
  state: PassState,
  season: Season,
  plus: boolean,
  now: number,
): PassState {
  const skip = plus && !state.plus ? PLUS_SKIPS * season.xpPerTier : 0;
  const cap = TIER_COUNT * season.xpPerTier;
  return {
    ...state,
    premium: true,
    plus: state.plus || plus,
    xp: Math.min(cap, state.xp + skip),
    updatedTs: now,
  };
}

export interface RewardPorts {
  readonly ledger: LedgerPorts;
  grantItem(userId: string, itemId: string, now: number): void;
  grantTitle(userId: string, title: string, now: number): void;
}

export type ClaimResponse =
  | {
      readonly ok: true;
      readonly applied: boolean;
      readonly tier: number;
      readonly track: Track;
      readonly reward: PassReward;
    }
  | { readonly ok: false; readonly error: RuleError };

/** Pays a reached tier once; the ledger key is the season, tier and track. */
export function claimTier(
  state: PassState,
  season: Season,
  ports: RewardPorts,
  userId: string,
  tier: number,
  track: Track,
  now: number,
): { state: PassState; response: ClaimResponse } {
  const entry = season.tiers.find((t) => t.tier === tier);
  if (entry === undefined) {
    return {
      state,
      response: {
        ok: false,
        error: ruleError(ErrorCodes.table.invalidPayload, { reason: "tier" }),
      },
    };
  }
  if (tier > tierFor(state.xp, season)) {
    return {
      state,
      response: {
        ok: false,
        error: ruleError(ErrorCodes.economy.alreadyGranted, { reason: "not_reached" }),
      },
    };
  }
  if (track === "premium" && !state.premium) {
    return {
      state,
      response: {
        ok: false,
        error: ruleError(ErrorCodes.table.notAuthorised, { reason: "premium" }),
      },
    };
  }
  const key = `${String(tier)}:${track}`;
  const reward = entry[track];
  if (key in state.claimed) {
    return { state, response: { ok: true, applied: false, tier, track, reward } };
  }
  const paid = credit(ports.ledger, {
    userId,
    sourceId: `pass:${season.id}:${key}`,
    reason: "season_pass",
    ...(reward.coins === undefined ? {} : { coins: reward.coins }),
    ...(reward.gems === undefined ? {} : { gems: reward.gems }),
    metadata: { season: season.id, tier, track },
  });
  if (!paid.ok) return { state, response: paid };
  for (const itemId of reward.items ?? []) ports.grantItem(userId, itemId, now);
  if (reward.title !== undefined) ports.grantTitle(userId, reward.title, now);
  const next: PassState = { ...state, claimed: { ...state.claimed, [key]: now }, updatedTs: now };
  return { state: next, response: { ok: true, applied: true, tier, track, reward } };
}

export interface PassView {
  readonly season: Pick<Season, "id" | "name" | "startTs" | "endTs" | "xpPerTier">;
  readonly xp: number;
  readonly tier: number;
  readonly intoTier: number;
  readonly premium: boolean;
  readonly plus: boolean;
  readonly catchUp: boolean;
  readonly tiers: readonly {
    readonly tier: number;
    readonly reached: boolean;
    readonly free: PassReward & { readonly claimed: boolean };
    readonly premium: PassReward & { readonly claimed: boolean };
  }[];
}

export function passView(state: PassState, season: Season, now: number): PassView {
  const tier = tierFor(state.xp, season);
  return {
    season: {
      id: season.id,
      name: season.name,
      startTs: season.startTs,
      endTs: season.endTs,
      xpPerTier: season.xpPerTier,
    },
    xp: state.xp,
    tier,
    intoTier: tier >= TIER_COUNT ? 0 : state.xp - tier * season.xpPerTier,
    premium: state.premium,
    plus: state.plus,
    catchUp: now >= season.endTs - season.xp.catchUpDays * DAY_MS,
    tiers: season.tiers.map((t) => ({
      tier: t.tier,
      reached: t.tier <= tier,
      free: { ...t.free, claimed: `${String(t.tier)}:free` in state.claimed },
      premium: { ...t.premium, claimed: `${String(t.tier)}:premium` in state.claimed },
    })),
  };
}

export interface PassStore {
  read(userId: string): PassState | null;
  write(userId: string, state: PassState): void;
}

function isState(value: unknown): value is PassState {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v["seasonId"] === "string" &&
    typeof v["xp"] === "number" &&
    typeof v["claimed"] === "object"
  );
}

export function nkPassStore(nk: nkruntime.Nakama): PassStore {
  return {
    read(userId) {
      const objects = nk.storageRead([{ collection: PASS_COLLECTION, key: "state", userId }]);
      const value: unknown = objects[0]?.value;
      return isState(value) ? value : null;
    },
    write(userId, state) {
      nk.storageWrite([
        {
          collection: PASS_COLLECTION,
          key: "state",
          userId,
          value: state,
          permissionRead: 1,
          permissionWrite: 0,
        },
      ]);
    },
  };
}

export function nkRewardPorts(nk: nkruntime.Nakama, ledger: LedgerPorts): RewardPorts {
  return {
    ledger,
    grantItem(userId, itemId, now) {
      const store = nkCatalogStore(nk);
      const inventory = store.inventory(userId);
      const next = grantItem(inventory, itemId, now);
      if (next !== inventory) store.writeInventory(userId, next);
    },
    grantTitle(userId, title, now) {
      const store = nkProgressStore(nk);
      const progress = store.read(userId);
      if (progress.titles.includes(title)) return;
      store.write(userId, { ...progress, titles: [...progress.titles, title], updatedTs: now });
    },
  };
}

/** Adds pass XP for one source to the player's current season, if one is running. */
export function earnPassXp(
  nk: nkruntime.Nakama,
  userId: string,
  source: XpSource,
  now: number,
): boolean {
  const season = currentSeason(BUNDLED_SEASONS, now);
  if (season === null) return false;
  const store = nkPassStore(nk);
  const state = forSeason(store.read(userId), season);
  const next = addPassXp(state, season, source, now);
  if (next !== state) store.write(userId, next);
  return next !== state;
}

export { BUNDLED_SEASONS };
