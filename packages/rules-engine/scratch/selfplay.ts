import { Xoshiro128StarStar, utf8Encode } from "@hulla/shared-ts";

import {
  KHILADI_BOT,
  USTAD_BOT,
  defaultRungRules,
  rungBotMove,
  rungPolicyMove,
  rungModule,
  type RungBotTuning,
  type RungMove,
  type RungRules,
  type RungState,
} from "../src/rung";
import { RUNG_PRESETS } from "../src/rung/rules";

type Player = RungBotTuning | "auto" | "policy";
const bot = rungModule.bot!;

function playDeal(
  rules: RungRules,
  dealer: number,
  seed: string,
  players: Player[],
  stats: { ms: number; n: number; max: number },
) {
  const rng = Xoshiro128StarStar.fromSeed(utf8Encode(seed));
  const made = rungModule.createRound(
    { rules, seatCount: 4, dealerSeat: dealer, seed: utf8Encode(seed) },
    rng,
  );
  if (!made.ok) throw new Error("deal");
  let state: RungState = made.value;
  let memory = bot.start(state);
  let moves = 0;
  while (!rungModule.isRoundOver(state)) {
    const seat = state.turnSeat!;
    const p = players[seat]!;
    let move: RungMove | null;
    if (p === "auto") move = rungModule.onDeadline(state, seat);
    else if (p === "policy") {
      const brng = Xoshiro128StarStar.fromSeed(utf8Encode(`b:${seed}:${moves}:${seat}`));
      move =
        state.phase === "play"
          ? rungPolicyMove(state, seat, memory as never, brng)
          : rungBotMove(state, seat, memory as never, brng, USTAD_BOT);
    } else {
      const brng = Xoshiro128StarStar.fromSeed(utf8Encode(`b:${seed}:${moves}:${seat}`));
      const t = performance.now();
      move = rungBotMove(state, seat, memory as never, brng, p);
      const dt = performance.now() - t;
      if (state.phase === "play") {
        stats.ms += dt;
        stats.n += 1;
        stats.max = Math.max(stats.max, dt);
      }
    }
    const applied = rungModule.applyMove(state, seat, move!, rng);
    if (!applied.ok) throw new Error(JSON.stringify(applied.error));
    state = applied.state;
    memory = bot.observe(memory, applied.events);
    moves += 1;
  }
  return rungModule.results(state);
}

function match(name: string, rules: RungRules, a: Player, b: Player, bazis: number) {
  const stats = { ms: 0, n: 0, max: 0 };
  let winsA = 0,
    deals = 0,
    dealsA = 0;
  for (let i = 0; i < bazis; i += 1) {
    // Team A sits at seats 0 and 2 on even bazis and 1 and 3 on odd ones.
    const aTeam = i % 2;
    const players = [0, 1, 2, 3].map((s) => (s % 2 === aTeam ? a : b));
    let series: any = {
      roundsPlayed: 0,
      points: [0, 0, 0, 0],
      loserTally: [0, 0, 0, 0],
      nextDealerSeat: i % 4,
      finished: false,
    };
    for (let d = 0; !series.finished && d < 60; d += 1) {
      const result: any = playDeal(
        rules,
        series.nextDealerSeat,
        `${name}:${i}:${d}`,
        players,
        stats,
      );
      series = rungModule.seriesUpdate(series, result);
      deals += 1;
      if (result.winnerTeam === aTeam) dealsA += 1;
    }
    if (series.winnerTeam === aTeam) winsA += 1;
  }
  console.log(
    `${name}: A wins ${winsA}/${bazis} bazis (${Math.round((100 * winsA) / bazis)}%), deals ${dealsA}/${deals} (${Math.round((100 * dealsA) / deals)}%), ${(stats.ms / Math.max(1, stats.n)).toFixed(2)} ms/decision avg, ${stats.max.toFixed(1)} max, n=${stats.n}`,
  );
}

const n = Number(process.argv[2] ?? 40);
const which = process.argv[3] ?? "all";
const double = defaultRungRules("private");
const single = { ...double, ...RUNG_PRESETS.courtPiece };
const hokm = { ...double, ...RUNG_PRESETS.hokm };
if (which === "all" || which === "auto") {
  match("ustad-vs-auto double", double, USTAD_BOT, "auto", n);
  match("ustad-vs-auto single", single, USTAD_BOT, "auto", n);
  match("ustad-vs-auto hokm", hokm, USTAD_BOT, "auto", n);
}
if (which === "all" || which === "levels") {
  match("ustad-vs-khiladi double", double, USTAD_BOT, KHILADI_BOT, n);
  match("ustad-vs-khiladi single", single, USTAD_BOT, KHILADI_BOT, n);
}

if (which === "policy") {
  match("policy-vs-auto double", double, "policy", "auto", n);
  match("ustad-vs-policy double", double, USTAD_BOT, "policy", n);
  match("ustad-vs-policy single", single, USTAD_BOT, "policy", n);
}
if (which === "deep") {
  for (const t of [3, 4, 5]) {
    match(
      `ustad(end ${t})-vs-ustad(end 3) double`,
      double,
      { ...USTAD_BOT, endgameTricks: t, endgameNodes: 4000 },
      USTAD_BOT,
      n,
    );
  }
}
if (which === "ab") {
  match(
    "A(work 9000,end 4)-vs-B double",
    double,
    { ...USTAD_BOT, work: 9000, endgameTricks: 4, endgameNodes: 3000 },
    USTAD_BOT,
    n,
  );
  match(
    "A(work 9000,end 4)-vs-B single",
    single,
    { ...USTAD_BOT, work: 9000, endgameTricks: 4, endgameNodes: 3000 },
    USTAD_BOT,
    n,
  );
  match("A(work 3000)-vs-B double", double, { ...USTAD_BOT, work: 3000 }, USTAD_BOT, n);
}
