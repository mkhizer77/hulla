import { Xoshiro128StarStar, shuffle, type Rng } from "@hulla/shared-ts";
import { describe, expect, it } from "vitest";

import { standardDeck, type Card } from "../../card-kit";
import { autoPlayMove } from "../autoplay";
import { REGULAR_BOT, botMove, scoreCards } from "../bot";
import { asBotMemory, botMemory, observeBotMemory, type ThullaBotMemory } from "../bot-memory";
import { createThullaRound, roundStartedEvent } from "../deal";
import { applyMove } from "../engine";
import { defaultRules } from "../rules";

import { stateFrom, type Setup } from "./helpers";

import type { ThullaState } from "../state";

const rng = (n: number): Rng => Xoshiro128StarStar.fromSeed(new Uint8Array([n, 1, 2, 3]));

/** A setup whose cards not in a hand or the pile sit in the discard, so all 52 are accounted for. */
function full(setup: Setup): ThullaState {
  const used = new Set<Card>([...setup.hands.flat(), ...(setup.plays ?? []).map((p) => p.card)]);
  return stateFrom({ ...setup, discard: standardDeck().filter((card) => !used.has(card)) });
}

function withVoids(seatCount: number, voids: Record<number, string[]>): ThullaBotMemory {
  const memory = botMemory(seatCount);
  return {
    ...memory,
    voids: memory.voids.map((_, seat) => (voids[seat] ?? []) as ThullaBotMemory["voids"][number]),
  };
}

/** The tier without its slips, so a test sees the card it thinks best. */
const STEADY = { ...REGULAR_BOT, slipChance: 0 };

function played(state: ThullaState, seat: number, memory: ThullaBotMemory, seed = 1): string {
  const move = botMove(state, seat, memory, rng(seed), STEADY);
  return move?.type === "play_card" ? move.card : String(move?.type);
}

describe("bots play like people (GDD 8.3)", () => {
  it("does not lead a suit the next player is known to lack while another suit is safe", () => {
    // Seat 1 thulla'd on hearts before; leading a heart hands the bot its own card straight back.
    const state = full({
      hands: [
        ["2H", "5H", "9H", "KH", "3C", "4D"],
        ["5C", "8C", "QC", "6D", "9D"],
        ["3H", "6H", "7C", "JD", "QD"],
        ["4H", "TH", "9C", "KD", "AD"],
      ],
      leader: 0,
    });
    const memory = withVoids(4, { 1: ["H"] });
    expect(autoPlayMove(state, 0)).toEqual({ type: "play_card", card: "2H" });
    for (let seed = 1; seed <= 5; seed++) {
      expect(played(state, 0, memory, seed)).not.toMatch(/H$/);
    }
  });

  it("follows under the top card when a later player can thulla", () => {
    const state = full({
      hands: [
        ["9H", "2C"],
        ["4H", "7H", "TH", "QH"],
        ["3H", "5D", "8D"],
        ["6C", "9C", "KC"],
      ],
      leader: 0,
      plays: [{ seat: 0, card: "9H" }],
    });
    expect(played(state, 1, withVoids(4, { 3: ["H"] }))).toBe("7H");
  });

  it("throws its highest card of the suit when it plays last and nothing can be picked up", () => {
    const state = full({
      hands: [
        ["2S", "4D", "JS"],
        ["5S", "6D", "4S"],
        ["8S", "7D", "TS"],
        ["3S", "KS", "8D"],
      ],
      leader: 0,
      plays: [
        { seat: 0, card: "2S" },
        { seat: 1, card: "5S" },
        { seat: 2, card: "8S" },
      ],
    });
    expect(played(state, 3, botMemory(4))).toBe("KS");
  });

  it("on a thulla gets rid of its worst card, a lone high card first", () => {
    const state = full({
      hands: [
        ["2S", "4D", "7C", "9H"],
        ["AC", "2D", "3D", "5D", "KH", "2H"],
        ["8S", "7D", "TC", "QH"],
      ],
      leader: 0,
      plays: [{ seat: 0, card: "2S" }],
    });
    expect(played(state, 1, botMemory(3))).toBe("AC");
  });

  it("throws its highest spade into the opening trick, which nobody picks up", () => {
    const state = stateFrom({
      hands: [
        ["AS", "2H"],
        ["3S", "JS", "4H"],
        ["5S", "6H"],
      ],
      leader: 0,
      phase: "opening",
      trickIndex: 0,
      plays: [{ seat: 0, card: "AS" }],
      openingPending: [1, 2],
    });
    expect(played(state, 1, botMemory(3))).toBe("JS");
  });

  it("plays the two-player endgame to win where the timeout policy loses", () => {
    // Leading the 2H loses (the bot ends up the Bhabhi); leading the 5C wins.
    const state = full({ hands: [["2H", "5C"], ["3H", "4C"], []], leader: 0 });
    expect(autoPlayMove(state, 0)).toEqual({ type: "play_card", card: "2H" });
    expect(played(state, 0, botMemory(3))).toBe("5C");
  });

  it("still plays a legal card when the memory cannot be right", () => {
    // Every other seat "lacks" hearts, yet hearts are unseen: the deal overrules the memory.
    const state = full({
      hands: [
        ["2S", "4D", "9H"],
        ["5C", "6D", "JH"],
        ["8S", "7D", "QH"],
        ["3S", "KS", "8D"],
      ],
      leader: 3,
    });
    const memory = withVoids(4, { 0: ["H"], 1: ["H"], 2: ["H"] });
    expect(["3S", "KS", "8D"]).toContain(played(state, 3, memory));
  });

  it("averages the draws from the discard in a two-player endgame that draws from it (R42)", () => {
    const state = full({
      hands: [["9H", "3C", "5D"], ["4H", "7C", "8D", "2D"], []],
      leader: 0,
      rules: { twoPlayerEndgame: "drawDiscard" },
    });
    const scored = scoreCards(state, 0, botMemory(3), rng(1));
    expect(scored.map((s) => s.card).sort()).toEqual(["3C", "5D", "9H"]);
    expect(scored.every((s) => Number.isFinite(s.score))).toBe(true);
  });

  it("falls back to the trick-by-trick view when the endgame is too big to search", () => {
    const state = full({ hands: [["2H", "5C"], ["3H", "4C"], []], leader: 0 });
    const capped = botMove(state, 0, botMemory(3), rng(1), { ...STEADY, endgameNodes: 1 });
    expect(capped?.type).toBe("play_card");
  });

  it("sometimes slips to the second-best card when the two are close, never when they are not", () => {
    const state = full({
      hands: [
        ["2S", "4D", "7C", "9H"],
        ["AC", "2D", "3D", "5D", "KH", "2H"],
        ["8S", "7D", "TC", "QH"],
      ],
      leader: 0,
      plays: [{ seat: 0, card: "2S" }],
    });
    const choices = new Set<string>();
    for (let seed = 1; seed <= 40; seed++) choices.add(played(state, 1, botMemory(3), seed));
    const [best, second] = scoreCards(state, 1, botMemory(3), rng(1));
    expect(best !== undefined && second !== undefined).toBe(true);
    const close = (best?.score ?? 0) - (second?.score ?? 0) < REGULAR_BOT.slipGap;
    const slipping = new Set<string>();
    for (let seed = 1; seed <= 40; seed++) {
      const move = botMove(state, 1, botMemory(3), rng(seed));
      slipping.add(move?.type === "play_card" ? move.card : "");
    }
    expect(choices).toEqual(new Set([best?.card]));
    expect(slipping).toEqual(close ? new Set([best?.card, second?.card]) : new Set([best?.card]));
  });

  it("never looks at another seat's cards: any deal with the same counts gives the same move", () => {
    for (const moves of [9, 14, 23, 31]) {
      const start = playedOut(7, moves);
      const seat = start.turnSeat ?? start.leaderSeat;
      const others = [0, 1, 2, 3].filter((s) => s !== seat && !(start.seats[s]?.escaped ?? true));
      const pool = shuffle(
        rng(moves),
        others.flatMap((s) => start.seats[s]?.hand ?? []),
      );
      let cursor = 0;
      const redealt: ThullaState = {
        ...start,
        seats: start.seats.map((seatState, index) => {
          if (!others.includes(index)) return seatState;
          const hand = pool.slice(cursor, cursor + seatState.hand.length);
          cursor += seatState.hand.length;
          return { ...seatState, hand };
        }),
      };
      expect(redealt).not.toEqual(start);
      expect(botMove(redealt, seat, null, rng(4))).toEqual(botMove(start, seat, null, rng(4)));
    }
  });

  it("only ever makes legal moves, and finishes every round it plays", () => {
    for (let round = 0; round < 6; round++) {
      const state = playRound(round, [0, 1, 2, 3]);
      expect(state.phase).toBe("ended");
    }
  });

  it("is rarely the Bhabhi against three seats on the timeout policy", () => {
    let bhabhi = 0;
    const rounds = 20;
    for (let round = 0; round < rounds; round++) {
      if (playRound(round, [round % 4]).bhabhiSeat === round % 4) bhabhi += 1;
    }
    // A fair share would be five rounds in twenty; in self-play the bot loses about 1 in 60.
    expect(bhabhi).toBeLessThanOrEqual(2);
  });
});

describe("what a bot remembers (GDD 8.2)", () => {
  it("marks a void when a seat plays off the led suit, and forgets played cards", () => {
    let memory = botMemory(3);
    memory = observeBotMemory(memory, [
      { type: "card_played", seat: 0, card: "5H", trickIndex: 2, thulla: false },
      { type: "card_played", seat: 1, card: "KC", trickIndex: 2, thulla: true },
      {
        type: "trick_resolved",
        trickIndex: 2,
        ledSuit: "H",
        cards: ["5H", "KC"],
        thullaBy: 1,
        picker: 0,
        nextLeader: 0,
        handCounts: [4, 3, 3],
      },
    ]);
    expect(memory.voids[1]).toEqual(["H"]);
    expect(memory.known[0]).toEqual(["5H", "KC"]);
    memory = observeBotMemory(memory, [
      { type: "card_played", seat: 0, card: "KC", trickIndex: 3, thulla: false },
    ]);
    expect(memory.known[0]).toEqual(["5H"]);
    expect(memory.ledSuit).toBe("C");
  });

  it("an opening response off spades marks the seat out of spades", () => {
    const memory = observeBotMemory(botMemory(3), [
      { type: "card_played", seat: 0, card: "AS", trickIndex: 0, thulla: false },
      { type: "card_played", seat: 2, card: "9D", trickIndex: 0, thulla: false },
    ]);
    expect(memory.voids[2]).toEqual(["S"]);
  });

  it("a taken hand moves what was known to the taker and keeps only voids both shared", () => {
    const memory: ThullaBotMemory = {
      ...botMemory(3),
      voids: [["H", "C"], ["C"], []],
      known: [["2D"], ["9S"], []],
    };
    const next = observeBotMemory(memory, [
      { type: "hand_taken", taker: 0, target: 1, cardCount: 4 },
    ]);
    expect(next.known).toEqual([["2D", "9S"], [], []]);
    expect(next.voids).toEqual([["C"], [], []]);
  });

  it("ignores private draws and reads back from plain data", () => {
    const memory = observeBotMemory(botMemory(3), [
      {
        type: "private_draw",
        seat: 0,
        card: "7H",
        source: "discard",
        from: null,
        visibleTo: [0],
      },
    ]);
    expect(memory).toEqual(botMemory(3));
    expect(asBotMemory(JSON.parse(JSON.stringify(memory)))).toEqual(memory);
    expect(asBotMemory({ seatCount: 3 })).toBeNull();
    expect(asBotMemory(null)).toBeNull();
  });
});

/** A seeded round played by `bots` (the rest on the timeout policy) until it ends. */
function playRound(round: number, bots: readonly number[]): ThullaState {
  return play(round, bots, Infinity);
}

/** The same round stopped after `moves` moves, for a mid-round position. */
function playedOut(round: number, moves: number): ThullaState {
  return play(round, [0, 1, 2, 3], moves);
}

function play(round: number, bots: readonly number[], moves: number): ThullaState {
  const seed = new Uint8Array(32).fill(round + 1);
  const dealRng = Xoshiro128StarStar.fromSeed(seed);
  const created = createThullaRound(
    { rules: defaultRules("public"), seatCount: 4, dealerSeat: round % 4, seed },
    dealRng,
  );
  if (!created.ok) throw new Error("deal");
  let state = created.value;
  let memory = observeBotMemory(botMemory(4), [roundStartedEvent(state)]);
  const botRng = rng(round + 50);
  for (let step = 0; step < moves && step < 1000 && state.phase !== "ended"; step++) {
    const seat = state.turnSeat ?? state.openingPending[0] ?? state.leaderSeat;
    const move = bots.includes(seat)
      ? botMove(state, seat, memory, botRng)
      : autoPlayMove(state, seat);
    if (move === null) throw new Error("no move");
    const applied = applyMove(state, seat, move, dealRng);
    if (!applied.ok) throw new Error(`illegal ${JSON.stringify(move)}`);
    memory = observeBotMemory(memory, applied.events);
    state = applied.state;
  }
  return state;
}
