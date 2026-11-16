import { ErrorCodes, Xoshiro128StarStar, ruleError, utf8Encode } from "@hulla/shared-ts";

import { log } from "../infra/log";
import { botRng } from "../match/bots";
import { hostFor } from "../match/registry";
import { liveTables, readDrain, writeDrain } from "../services/ops";
import { audit } from "../services/sanctions";

import { isAdmin } from "./admin";

function parse(payload: string): Record<string, unknown> {
  try {
    const value: unknown = payload === "" ? {} : JSON.parse(payload);
    return typeof value === "object" && value !== null && !Array.isArray(value)
      ? (value as Record<string, unknown>)
      : {};
  } catch {
    return {};
  }
}

/**
 * `{ on, reason? }`: turns draining on or off and reports the live tables, so a deploy can wait
 * for zero before retiring the node (docs/runbooks/deploy-and-rollback.md). Server key or owner.
 */
export function rpcAdminDrain(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string,
): string {
  if (!isAdmin(ctx)) {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  const input = parse(payload);
  const now = Date.now();
  const current = readDrain(nk);
  if (typeof input["on"] === "boolean" && input["on"] !== current.draining) {
    const reason = typeof input["reason"] === "string" ? input["reason"].slice(0, 64) : "";
    writeDrain(
      nk,
      input["on"]
        ? { draining: true, since: now, reason }
        : { draining: false, since: 0, reason: "" },
    );
    audit(nk, {
      actorId: ctx.userId === undefined || ctx.userId === "" ? "server_key" : ctx.userId,
      action: input["on"] ? "drain_on" : "drain_off",
      targetId: null,
      detail: { reason },
      ts: now,
    });
    log.info(logger, input["on"] ? "draining: no new tables" : "draining ended", { reason });
  }
  const state = readDrain(nk);
  return JSON.stringify({
    ok: true,
    draining: state.draining,
    since: state.since,
    liveTables: liveTables(nk),
  });
}

/** Deals `admin_bot_bench` plays at most: each is some fifty decisions in the slow interpreter. */
const MAX_BENCH_DEALS = 5;

/**
 * `{ gameId, deals? }`: plays whole rounds with the game's bot in every seat and reports how long
 * a decision takes inside Nakama, which is many times slower than Node (Rung design 6.6). Nothing
 * is stored. Server key or owner.
 */
export function rpcAdminBotBench(
  ctx: nkruntime.Context,
  _logger: nkruntime.Logger,
  _nk: nkruntime.Nakama,
  payload: string,
): string {
  if (!isAdmin(ctx)) {
    return JSON.stringify({ ok: false, error: ruleError(ErrorCodes.table.notAuthorised) });
  }
  const input = parse(payload);
  const gameId = typeof input["gameId"] === "string" ? input["gameId"] : "rung";
  const deals = Math.max(1, Math.min(MAX_BENCH_DEALS, Number(input["deals"] ?? 1) || 1));
  return JSON.stringify(botBench(gameId, deals, () => Date.now()));
}

export interface BotBenchResult {
  readonly ok: boolean;
  readonly gameId: string;
  readonly deals: number;
  readonly decisions: number;
  readonly avgMs: number;
  readonly maxMs: number;
  /** Decisions that took longer than 150 ms, the worst case the design allows. */
  readonly over150: number;
  /** The slow decisions: which move of its round, how many choices the seat had, how long. */
  readonly slow: readonly {
    readonly move: number;
    readonly choices: number;
    readonly ms: number;
  }[];
}

/** Bots in every seat play `deals` rounds; `clock` is read around each decision. */
export function botBench(gameId: string, deals: number, clock: () => number): BotBenchResult {
  const host = hostFor(gameId);
  const bot = host?.module.bot;
  const empty = { gameId, deals: 0, decisions: 0, avgMs: 0, maxMs: 0, over150: 0, slow: [] };
  if (host === null || bot === undefined) return { ok: false, ...empty };
  const rules = host.resolveRules({ gameId: host.module.id }, "private");
  if (!rules.ok) return { ok: false, ...empty };
  const seatCount = host.module.seats.max;
  let decisions = 0;
  let total = 0;
  let maxMs = 0;
  let over150 = 0;
  let played = 0;
  const slow: { move: number; choices: number; ms: number }[] = [];
  for (let d = 0; d < deals; d += 1) {
    const seed = utf8Encode(`bench:${gameId}:${String(d)}`);
    const rng = Xoshiro128StarStar.fromSeed(seed);
    const created = host.module.createRound(
      { rules: rules.value, seatCount, dealerSeat: d % seatCount, seed },
      rng,
    );
    if (!created.ok) break;
    let state = created.value;
    let memory = bot.start(state);
    for (let moves = 0; moves < 400 && !host.module.isRoundOver(state); moves += 1) {
      let acted = false;
      for (let seat = 0; seat < seatCount && !acted; seat += 1) {
        const choices = host.module.legalMoves(state, seat).length;
        if (choices === 0) continue;
        const started = clock();
        const move =
          bot.choose(state, seat, memory, botRng(`bench${String(d)}`, moves, seat)) ??
          host.module.onDeadline(state, seat);
        const took = clock() - started;
        if (move === null) continue;
        const applied = host.module.applyMove(state, seat, move, rng);
        if (!applied.ok) continue;
        decisions += 1;
        total += took;
        maxMs = Math.max(maxMs, took);
        if (took > 150) {
          over150 += 1;
          if (slow.length < 20) slow.push({ move: moves, choices, ms: took });
        }
        state = applied.state;
        memory = bot.observe(memory, applied.events);
        acted = true;
      }
      if (!acted) break;
    }
    played += 1;
  }
  return {
    ok: true,
    gameId,
    deals: played,
    decisions,
    avgMs: decisions === 0 ? 0 : Math.round(total / decisions),
    maxMs,
    over150,
    slow,
  };
}
