import { Xoshiro128StarStar, utf8Encode, type Rng } from "@hulla/shared-ts";
import { describe, expect, it } from "vitest";

import { standardDeck, type Card, type Suit } from "../../card-kit";
import { rungAutoPlayMove } from "../autoplay";
import {
  KHILADI_BOT,
  USTAD_BOT,
  botCallSuit,
  callScore,
  rungBotMove,
  rungPolicyMove,
  scoreRungCards,
} from "../bot";
import {
  asRungBotMemory,
  observeRungBotMemory,
  rungBotMemory,
  type RungBotMemory,
} from "../bot-memory";
import { cardId, cardOf, simFrom, simOutcomeKind, simPlay } from "../bot-sim";
import { createRungRound } from "../deal";
import { applyRungMove } from "../engine";
import { rungBot, rungModule } from "../module";
import { RUNG_PRESETS, defaultRungRules, type RungRules } from "../rules";
import { rungStateFromSetup, type RungSetup } from "../setup";

import type { SeriesState } from "../../contract";
import type { RungMove } from "../moves";
import type { RungRoundResult, RungSeriesState } from "../results";
import type { RungState } from "../state";

function rngOf(seed: string): Rng {
  return Xoshiro128StarStar.fromSeed(utf8Encode(seed));
}

function dealt(
  seed: string,
  rules: RungRules = defaultRungRules("private"),
  dealer = 0,
): RungState {
  const made = createRungRound(
    { rules, seatCount: 4, dealerSeat: dealer, seed: utf8Encode(seed) },
    rngOf(seed),
  );
  if (!made.ok) throw new Error("deal failed");
  return made.value;
}

function apply(state: RungState, move: RungMove, rng: Rng, memory: RungBotMemory) {
  const seat = state.turnSeat ?? 0;
  const applied = applyRungMove(state, seat, move, rng);
  if (!applied.ok) throw new Error(JSON.stringify(applied.error));
  return { state: applied.state, memory: observeRungBotMemory(memory, applied.events) };
}

/** A real deal played by the timeout policy for `plays` moves, with the table's memory of it. */
function midDeal(seed: string, plays: number, rules?: RungRules) {
  const rng = rngOf(`${seed}:moves`);
  let state = dealt(seed, rules);
  let memory = rungBotMemory();
  for (let i = 0; i < plays && state.phase !== "ended"; i += 1) {
    const move = rungAutoPlayMove(state, state.turnSeat ?? 0);
    if (move === null) break;
    ({ state, memory } = apply(state, move, rng, memory));
  }
  return { state, memory };
}

/** A set-up position and the memory of a table that has seen every card not in it. */
function position(setup: RungSetup): { state: RungState; memory: RungBotMemory } {
  const made = rungStateFromSetup(setup);
  if (!made.ok) throw new Error(JSON.stringify(made.error));
  const inPlay = new Set<Card>([
    ...setup.hands.flat(),
    ...(setup.plays ?? []).map((play) => play.card),
  ]);
  return {
    state: made.value,
    memory: {
      ...rungBotMemory(),
      trump: setup.trump,
      played: [
        ...standardDeck().filter((card) => !inPlay.has(card)),
        ...(setup.plays ?? []).map((play) => play.card),
      ],
      trick: setup.plays ?? [],
    },
  };
}

function choice(setup: RungSetup, seat: number): Card | null {
  const { state, memory } = position(setup);
  const move = rungBotMove(state, seat, memory, rngOf("choice"));
  return move?.type === "play_card" ? move.card : null;
}

type Player = "ustad" | "khiladi" | "policy" | "auto";

function playerMove(player: Player, state: RungState, memory: RungBotMemory, key: string) {
  const seat = state.turnSeat ?? 0;
  if (player === "auto") return rungAutoPlayMove(state, seat);
  if (player === "policy" && state.phase === "play") {
    return rungPolicyMove(state, seat, memory, rngOf(key));
  }
  return rungBotMove(
    state,
    seat,
    memory,
    rngOf(key),
    player === "khiladi" ? KHILADI_BOT : USTAD_BOT,
  );
}

/** Bazis won by team A out of `bazis`; A sits at seats 0 and 2 on even bazis, 1 and 3 on odd. */
function bazisWon(a: Player, b: Player, bazis: number, rules: RungRules, tag: string): number {
  let won = 0;
  for (let bazi = 0; bazi < bazis; bazi += 1) {
    const aTeam = bazi % 2;
    let series: SeriesState = {
      roundsPlayed: 0,
      points: [0, 0, 0, 0],
      loserTally: [0, 0, 0, 0],
      nextDealerSeat: bazi % 4,
      finished: false,
    };
    for (let d = 0; !series.finished && d < 60; d += 1) {
      const seed = `${tag}:${String(bazi)}:${String(d)}`;
      const rng = rngOf(`${seed}:moves`);
      let state = dealt(seed, rules, series.nextDealerSeat);
      let memory = rungBotMemory();
      for (let m = 0; state.phase !== "ended" && m < 80; m += 1) {
        const seat = state.turnSeat ?? 0;
        const move = playerMove(seat % 2 === aTeam ? a : b, state, memory, `${seed}:${String(m)}`);
        if (move === null) throw new Error("no move");
        ({ state, memory } = apply(state, move, rng, memory));
      }
      series = rungModule.seriesUpdate(series, rungModule.results(state));
    }
    if ((series as RungSeriesState).winnerTeam === aTeam) won += 1;
  }
  return won;
}

describe("Rung bot: the call and the court choice (design 6.3)", () => {
  it("scores two per card plus A 4, K 3, Q 2, J 1 and calls the best suit", () => {
    const hand: Card[] = ["AS", "KS", "2H", "3H", "4H"];
    expect(callScore(hand, "S")).toBe(11);
    expect(callScore(hand, "H")).toBe(6);
    expect(botCallSuit(hand)).toBe("S");
    // Length wins when the honours do not make up for it.
    expect(botCallSuit(["JS", "2H", "3H", "4H", "5H"])).toBe("H");
  });

  it("RG7 asks for a redeal on a weak five and calls a long suit instead", () => {
    const hands = (caller: Card[]): Card[][] => [
      ["2D", "3D", "4D", "5D", "6D"],
      caller,
      ["7D", "8D", "9D", "TD", "2C"],
      ["3C", "4C", "5C", "6C", "7C"],
    ];
    const move = (caller: Card[]): RungMove | null => {
      const made = rungStateFromSetup({ hands: hands(caller), trump: null, dealer: 0, stock: [] });
      if (!made.ok) throw new Error("setup");
      return rungBotMove(made.value, 1, rungBotMemory(), rngOf("call"));
    };
    expect(move(["2S", "3S", "2H", "3H", "8C"])).toEqual({ type: "redeal" });
    expect(move(["2S", "3S", "4S", "5S", "8C"])).toEqual({ type: "call_trump", suit: "S" });
    // An honour means no redeal is on offer (RG7), so it calls.
    expect(move(["KS", "3S", "2H", "3H", "8C"])).toEqual({ type: "call_trump", suit: "S" });
  });

  it("RG26 RG27 plays on for the bigger prize, since a failed chase costs nothing", () => {
    const made = rungStateFromSetup({
      hands: [
        ["AH", "2C", "2D"],
        ["2H", "AC", "AD"],
        ["3H", "3C", "3D"],
        ["4H", "4C", "4D"],
      ],
      trump: "S",
      dealer: 3,
      rules: { collect: "single" },
      trickIndex: 6,
      counts: [6, 0],
    });
    if (!made.ok) throw new Error("setup");
    let state = made.value;
    const rng = rngOf("court");
    for (const card of ["AH", "2H", "3H", "4H"] as Card[]) {
      const applied = applyRungMove(state, state.turnSeat ?? 0, { type: "play_card", card }, rng);
      if (!applied.ok) throw new Error("play");
      state = applied.state;
    }
    expect(state.phase).toBe("courtChoice");
    expect(rungBotMove(state, 0, null, rng)).toEqual({ type: "court_choice", playOn: true });
    expect(rungBotMove(state, 1, null, rng)).toBeNull();
  });
});

describe("Rung bot: positions with one right card (design 6.6)", () => {
  const single = { collect: "single" } as const;

  it("does not beat partner's winning ace", () => {
    const card = choice(
      {
        hands: [
          ["7C", "8C"],
          ["9C", "TC"],
          ["KH", "3H", "2C"],
          ["4H", "5C", "6C"],
        ],
        trump: "S",
        dealer: 3,
        rules: single,
        trickIndex: 10,
        counts: [5, 5],
        plays: [
          { seat: 0, card: "AH" },
          { seat: 1, card: "2H" },
        ],
      },
      2,
    );
    expect(card).toBe("3H");
  });

  it("wins as cheaply as it can when playing last, keeping the boss card", () => {
    const card = choice(
      {
        hands: [
          ["TH", "AH", "3C"],
          ["KH", "4C"],
          ["6C", "7C"],
          ["QH", "8C"],
        ],
        trump: "S",
        dealer: 0,
        rules: single,
        trickIndex: 10,
        counts: [5, 5],
        plays: [
          { seat: 1, card: "9H" },
          { seat: 2, card: "2H" },
          { seat: 3, card: "5H" },
        ],
      },
      0,
    );
    expect(card).toBe("TH");
  });

  it("cuts an opponent's winner with its lowest rung", () => {
    const card = choice(
      {
        hands: [
          ["2S", "9S", "5C"],
          ["4H", "6C"],
          ["7C", "8C"],
          ["9C", "TC"],
        ],
        trump: "S",
        dealer: 0,
        rules: single,
        trickIndex: 10,
        counts: [5, 5],
        plays: [
          { seat: 1, card: "AH" },
          { seat: 2, card: "2H" },
          { seat: 3, card: "3H" },
        ],
      },
      0,
    );
    expect(card).toBe("2S");
  });

  it("RG17 leads its surest winner when the next trick takes the pile", () => {
    const card = choice(
      {
        hands: [
          ["AS", "2H", "3C"],
          ["KS", "KH", "4C"],
          ["5H", "6H", "5C"],
          ["QS", "AH", "6C"],
        ],
        trump: "S",
        dealer: 3,
        trickIndex: 10,
        counts: [3, 3],
        pile: 4,
        lastWinner: 0,
        run: 1,
        leader: 0,
      },
      0,
    );
    expect(card).toBe("AS");
  });

  it("RG18 spends its ace to break an opponent's run on a big pile", () => {
    // Seat 1 won the last trick and leads the king; the ace of the suit stops the pile going.
    const card = choice(
      {
        hands: [
          ["9C", "8C"],
          ["2D", "3D"],
          ["AH", "3H", "4C"],
          ["4H", "5C", "6C"],
        ],
        trump: "S",
        dealer: 0,
        trickIndex: 10,
        counts: [3, 3],
        pile: 4,
        lastWinner: 1,
        run: 1,
        leader: 1,
        plays: [{ seat: 1, card: "KH" }],
      },
      2,
    );
    expect(card).toBe("AH");
  });
});

describe("Rung bot: honest, repeatable and never illegal (design 6.1)", () => {
  it("plays the same card whatever the hidden hands are", () => {
    for (const seed of ["h1", "h2", "h3", "h4"]) {
      const { state, memory } = midDeal(seed, 21);
      const seat = state.turnSeat ?? 0;
      const base = scoreRungCards(state, seat, memory, rngOf("same"));
      // Hand the other three seats each other's cards: same counts, different owners.
      const others = [0, 1, 2, 3].filter((s) => s !== seat);
      const pool = others.flatMap((s) => state.hands[s] ?? []).reverse();
      let at = 0;
      const hands = state.hands.map((hand, s) => {
        if (s === seat) return hand;
        const next = pool.slice(at, at + hand.length);
        at += hand.length;
        return next;
      });
      const swapped: RungState = { ...state, hands, stock: [], deckOrder: [], seedHex: "" };
      expect(scoreRungCards(swapped, seat, memory, rngOf("same")), seed).toEqual(base);
    }
  });

  it("gives the same answer for the same seed and a legal card on every turn of a deal", () => {
    for (const rules of [
      defaultRungRules("private"),
      { ...defaultRungRules("private"), ...RUNG_PRESETS.courtPiece },
      { ...defaultRungRules("private"), firstTwo: true, aceRule: true, lastTwo: true },
    ]) {
      const rng = rngOf("legal:moves");
      let state = dealt("legal", rules);
      let memory = rungBotMemory();
      for (let m = 0; state.phase !== "ended" && m < 80; m += 1) {
        const seat = state.turnSeat ?? 0;
        const move = rungBotMove(state, seat, memory, rngOf(`legal:${String(m)}`));
        expect(move).toEqual(rungBotMove(state, seat, memory, rngOf(`legal:${String(m)}`)));
        expect(rungModule.legalMoves(state, seat)).toContainEqual(move);
        if (move === null) break;
        ({ state, memory } = apply(state, move, rng, memory));
      }
      expect(state.phase).toBe("ended");
    }
  });

  it("still plays a legal card with no memory, or with one that is not this deal's", () => {
    const { state } = midDeal("nomem", 30);
    const seat = state.turnSeat ?? 0;
    const legal = rungModule.legalMoves(state, seat);
    expect(legal).toContainEqual(rungBotMove(state, seat, null, rngOf("x")));
    const wrong: RungBotMemory = { ...rungBotMemory(), played: standardDeck() };
    expect(legal).toContainEqual(rungBotMove(state, seat, wrong, rngOf("x")));
    expect(legal).toContainEqual(rungBot.choose(state, seat, { junk: true }, rngOf("x")));
  });
});

describe("Rung bot: memory of the table (design 6.2)", () => {
  it("records voids, played cards and a seat that would not cut, and resets on a redeal", () => {
    let memory = observeRungBotMemory(rungBotMemory(), [
      { type: "trump_called", seat: 1, suit: "S" },
      { type: "card_played", seat: 1, card: "AH", trickIndex: 0, cut: false },
      { type: "card_played", seat: 2, card: "2C", trickIndex: 0, cut: false },
      { type: "card_played", seat: 3, card: "3H", trickIndex: 0, cut: false },
      { type: "card_played", seat: 0, card: "4S", trickIndex: 0, cut: true },
    ]);
    expect(memory.played).toEqual(["AH", "2C", "3H", "4S"]);
    // Seat 2 was void and losing to an opponent's ace, and threw a club: probably no rungs.
    expect(memory.voids[2]).toEqual(["H" satisfies Suit]);
    expect(memory.noRungs).toEqual([false, false, true, false]);
    // Seat 0 was void too but cut: a void, no more.
    expect(memory.voids[0]).toEqual(["H"]);
    expect(asRungBotMemory(JSON.parse(JSON.stringify(memory)))).toEqual(memory);
    memory = observeRungBotMemory(memory, [
      { type: "trick_won", trickIndex: 0, winner: 0, cards: [], pile: 1, counts: [0, 0] },
    ]);
    expect(memory.trick).toEqual([]);
    memory = observeRungBotMemory(memory, [{ type: "redealt", commitment: "", redeals: 1 }]);
    expect(memory).toEqual(rungBotMemory());
    expect(asRungBotMemory({ played: "x" })).toBeNull();
    expect(asRungBotMemory(null)).toBeNull();
  });

  it("a seat that discards while its partner is winning is not marked as out of rungs", () => {
    const memory = observeRungBotMemory(rungBotMemory(), [
      { type: "trump_called", seat: 1, suit: "S" },
      { type: "card_played", seat: 1, card: "AH", trickIndex: 0, cut: false },
      { type: "card_played", seat: 2, card: "2H", trickIndex: 0, cut: false },
      { type: "card_played", seat: 3, card: "2C", trickIndex: 0, cut: false },
    ]);
    expect(memory.voids[3]).toEqual(["H"]);
    expect(memory.noRungs[3]).toBe(false);
  });
});

describe("Rung bot: the simulator plays by the engine's rules", () => {
  it("RG10 to RG27: random deals end the same in the simulator and the engine", () => {
    const variants: RungRules[] = [
      defaultRungRules("private"),
      { ...defaultRungRules("private"), firstTwo: true, aceRule: true, lastTwo: true },
      { ...defaultRungRules("private"), chase: false },
      { ...defaultRungRules("private"), ...RUNG_PRESETS.courtPiece },
      { ...defaultRungRules("private"), ...RUNG_PRESETS.hokm },
    ];
    let courts = 0;
    for (let i = 0; i < 300; i += 1) {
      const rules = variants[i % variants.length] ?? defaultRungRules("private");
      const seed = `mirror:${String(i)}`;
      const rng = rngOf(`${seed}:moves`);
      const pick = rngOf(`${seed}:pick`);
      let state = dealt(seed, rules, i % 4);
      const call = rungAutoPlayMove(state, state.turnSeat ?? 0);
      if (call === null) throw new Error("no call");
      ({ state } = apply(state, call, rng, rungBotMemory()));
      const sim = simFrom(
        state,
        state.hands.map((hand) => hand.map(cardId)),
      );
      // A lopsided deal now and then, so courts and chases are reached: one team plays its best card.
      const lopsided = i % 3 === 0;
      for (let m = 0; state.phase !== "ended" && m < 80; m += 1) {
        const seat = state.turnSeat ?? 0;
        if (state.phase === "courtChoice") {
          // The simulator always plays on.
          ({ state } = apply(state, { type: "court_choice", playOn: true }, rng, rungBotMemory()));
          continue;
        }
        const legal = rungModule.legalMoves(state, seat);
        const sorted = [...legal].sort((a, b) =>
          a.type === "play_card" && b.type === "play_card"
            ? (cardId(a.card) % 13) - (cardId(b.card) % 13)
            : 0,
        );
        const move =
          lopsided && seat % 2 === 0
            ? sorted[sorted.length - 1]
            : lopsided
              ? sorted[0]
              : legal[pick.nextInt(legal.length)];
        if (move?.type !== "play_card") throw new Error("expected a card");
        ({ state } = apply(state, move, rng, rungBotMemory()));
        simPlay(sim, cardId(move.card));
        const where = `${seed} move ${String(m)}`;
        expect([sim.count0, sim.count1], where).toEqual(state.counts);
        expect(sim.pile, where).toBe(state.pile);
        expect(sim.ended, where).toBe(state.phase === "ended");
        if (!sim.ended) {
          expect(sim.turn, where).toBe(state.turnSeat);
          expect(sim.trickNo, where).toBe(state.trickIndex);
        }
      }
      expect(sim.ended, seed).toBe(true);
      expect({ team: sim.outcomeTeam, kind: simOutcomeKind(sim) }, seed).toEqual(state.outcome);
      if (state.outcome?.kind !== "win") courts += 1;
    }
    // The lopsided deals did reach courts, so the chase rules were compared too.
    expect(courts).toBeGreaterThan(5);
  });

  it("card numbers round-trip", () => {
    for (const card of standardDeck()) expect(cardOf(cardId(card))).toBe(card);
  });
});

describe("Rung bot: strength in self-play (design 6.6)", () => {
  const double = defaultRungRules("private");
  const single = { ...double, ...RUNG_PRESETS.courtPiece };

  it("an Ustad pair beats a pair on the RG42 timeout policy in nearly every bazi", () => {
    expect(bazisWon("ustad", "auto", 10, double, "sa")).toBeGreaterThanOrEqual(9);
    expect(bazisWon("ustad", "auto", 10, single, "sb")).toBeGreaterThanOrEqual(9);
  });

  it("the search beats its own rule policy, and the policy beats the timeout policy", () => {
    expect(bazisWon("ustad", "policy", 16, double, "sc")).toBeGreaterThanOrEqual(11);
    expect(bazisWon("policy", "auto", 16, double, "sd")).toBeGreaterThanOrEqual(10);
  });

  it("Khiladi plays whole bazis and is no pushover for the timeout policy", () => {
    expect(bazisWon("khiladi", "auto", 8, double, "se")).toBeGreaterThanOrEqual(7);
  });

  it("a result carries the team that won", () => {
    const { state } = midDeal("result", 200);
    const result = rungModule.results(state) as RungRoundResult;
    expect([0, 1]).toContain(result.winnerTeam);
  });
});
