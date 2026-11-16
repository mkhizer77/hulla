import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";

import { createChecks } from "@hulla/protocol/generated";
import {
  applyRungMove,
  rungModule,
  rungStateFromSetup,
  type RungEvent,
  type RungMove,
  type RungSetup,
  type RungState,
} from "@hulla/rules-engine";
import { Xoshiro128StarStar, stateFromSeed } from "@hulla/shared-ts";
import { describe, expect, it } from "vitest";

import { presetsFor } from "../content/presets";
import {
  RUNG_CALL_MS,
  RUNG_PILE_MS,
  RUNG_TRICK_MS,
  decodeRungMove,
  encodeRungEvents,
  rungHighlights,
  rungHost,
} from "../games/rung/host";
import { hostFor, isGameId } from "../match/registry";

import type { EncodeContext, GameMessage } from "../match/types";

const checks = createChecks();
const VECTORS = join(
  import.meta.dirname,
  "..",
  "..",
  "..",
  "..",
  "packages",
  "test-vectors",
  "rung",
);

interface Vector {
  readonly id: string;
  readonly setup?: RungSetup;
  readonly moves?: readonly { readonly seat: number; readonly move: RungMove }[];
}

function vectors(): Vector[] {
  return readdirSync(VECTORS)
    .filter((name) => name.startsWith("V-R") && name.endsWith(".json"))
    .map((name) => JSON.parse(readFileSync(join(VECTORS, name), "utf8")) as Vector);
}

function ctxFor(
  state: RungState,
  move: EncodeContext<RungState>["move"],
  ended: boolean,
): EncodeContext<RungState> {
  const result = ended ? rungModule.results(state) : null;
  return {
    roundId: "r_1",
    seedHex: state.seedHex,
    state,
    result,
    series:
      result === null
        ? null
        : rungModule.seriesUpdate(
            {
              roundsPlayed: 0,
              points: [0, 0, 0, 0],
              loserTally: [0, 0, 0, 0],
              nextDealerSeat: 0,
              finished: false,
            },
            result,
          ),
    move,
  };
}

function setupState(setup: RungSetup): RungState {
  const made = rungStateFromSetup(setup);
  if (!made.ok) throw new Error(JSON.stringify(made.error));
  // A set-up position has no deal behind it; give it the commitment and seed a real one carries.
  return { ...made.value, commitment: "ab".repeat(32), seedHex: "07".repeat(32) };
}

/** Applies a move as a player's own (clientSeq 7) and returns the new state and wire messages. */
function play(
  state: RungState,
  seat: number,
  move: RungMove,
): { state: RungState; events: readonly RungEvent[]; messages: GameMessage[] } {
  const rng = Xoshiro128StarStar.fromState(stateFromSeed(new Uint8Array(32).fill(7)));
  const applied = applyRungMove(state, seat, move, rng);
  if (!applied.ok) throw new Error(JSON.stringify(applied.error));
  const ctx = ctxFor(
    applied.state,
    { seat, clientSeq: 7, auto: false },
    rungModule.isRoundOver(applied.state),
  );
  return {
    state: applied.state,
    events: applied.events,
    messages: encodeRungEvents(ctx, applied.events),
  };
}

function typesOf(messages: readonly GameMessage[]): string[] {
  return messages.filter((m) => m.seat === null).map((m) => m.type);
}

describe("Rung host: engine events to wire messages", () => {
  it("every move of every shared vector encodes to messages that match their schemas", () => {
    const seen = new Set<string>();
    let moves = 0;
    for (const vector of vectors()) {
      if (vector.setup === undefined) continue;
      let state = setupState(vector.setup);
      for (const step of vector.moves ?? []) {
        const rng = Xoshiro128StarStar.fromState(stateFromSeed(new Uint8Array(32).fill(3)));
        const applied = applyRungMove(state, step.seat, step.move, rng);
        if (!applied.ok) continue;
        state = applied.state;
        moves += 1;
        const over = rungModule.isRoundOver(state);
        const ctx = ctxFor(state, { seat: step.seat, clientSeq: 1, auto: false }, over);
        const messages = encodeRungEvents(ctx, applied.events);
        // One public message per engine event, in order; hands go to seats on top.
        expect(
          messages.filter((m) => m.seat === null),
          vector.id,
        ).toHaveLength(applied.events.length);
        for (const m of messages) {
          seen.add(m.type);
          expect(checks.payload[m.type]?.(m.payload), `${vector.id} ${JSON.stringify(m)}`).toBe(
            true,
          );
        }
        if (state.turnSeat !== null) {
          const turn = rungHost.turnStart({ roundId: "r_1", state }, state.turnSeat, 1_000, false);
          expect(checks.payload["turn_start"]?.(turn), `${vector.id} turn`).toBe(true);
        }
        expect(
          checks.rungView(rungModule.redact(state, { kind: "seat", seat: 0 })),
          vector.id,
        ).toBe(true);
        expect(checks.rungView(rungModule.redact(state, { kind: "spectator" })), vector.id).toBe(
          true,
        );
      }
    }
    expect(moves).toBeGreaterThan(100);
    // Every message a move can make after the call; the call itself runs in rung_table.test.ts.
    expect([...seen].sort()).toEqual([
      "rung_card_played",
      "rung_court_choice_made",
      "rung_court_choice_opened",
      "rung_hand",
      "rung_pile_taken",
      "rung_redealt",
      "rung_round_end",
      "rung_trick_won",
    ]);
  });

  it("RG7 a redeal sends the new commitment to all and five new cards to each seat alone", () => {
    const before = setupState({
      hands: [
        ["2S", "3S", "4S", "5S", "6S"],
        ["2H", "3H", "4H", "5H", "6H"],
        ["2C", "3C", "4C", "5C", "6C"],
        ["2D", "3D", "4D", "5D", "6D"],
      ],
      trump: null,
      dealer: 3,
      stock: [],
    });
    const { state, messages } = play(before, 0, { type: "redeal" });
    expect(typesOf(messages)).toEqual(["rung_redealt"]);
    const redealt = messages[0]?.payload as Record<string, unknown>;
    expect(redealt).toMatchObject({ seat: 0, redeals: 1, clientSeq: 7, auto: false });
    expect(redealt["commitment"]).toBe(state.commitment);
    expect(redealt["commitment"]).not.toBe(before.commitment);
    const hands = messages.filter((m) => m.type === "rung_hand");
    expect(hands.map((m) => m.seat)).toEqual([0, 1, 2, 3]);
    hands.forEach((m) => {
      expect((m.payload as { cards: string[] }).cards).toEqual(state.hands[m.seat ?? 0]);
    });
  });

  it("RG26 the court choice is offered to one seat and its answer carries the player's clientSeq", () => {
    const vector = vectors().find((v) => v.id === "V-R053-court-choice-opens");
    if (vector?.setup === undefined) throw new Error("vector missing");
    let state = setupState(vector.setup);
    let last: GameMessage[] = [];
    for (const step of (vector.moves ?? []).slice(0, 4)) {
      const played = play(state, step.seat, step.move);
      state = played.state;
      last = played.messages;
    }
    expect(typesOf(last)).toEqual([
      "rung_card_played",
      "rung_trick_won",
      "rung_court_choice_opened",
    ]);
    const turn = rungHost.turnStart({ roundId: "r_1", state }, 0, 5, false) as Record<
      string,
      unknown
    >;
    expect(turn).toMatchObject({ phase: "courtChoice", legal: [], canTakeHand: false });
    expect(turn["suits"]).toBeUndefined();
    const stop = play(state, 0, { type: "court_choice", playOn: false });
    expect(typesOf(stop.messages)).toEqual(["rung_court_choice_made", "rung_round_end"]);
    expect(stop.messages[0]?.payload).toMatchObject({ seat: 0, playOn: false, clientSeq: 7 });
    const end = stop.messages[1]?.payload as Record<string, unknown>;
    expect(end).toMatchObject({ winnerTeam: 0, kind: "court" });
    expect(end["series"]).toMatchObject({ teams: { dealsWon: [1, 0] } });
  });

  it("a timeout's or a bot's move is marked auto and carries no clientSeq", () => {
    const vector = vectors().find((v) => v.id === "V-R053-court-choice-opens");
    if (vector?.setup === undefined) throw new Error("vector missing");
    const state = setupState(vector.setup);
    const rng = Xoshiro128StarStar.fromState(stateFromSeed(new Uint8Array(32)));
    const applied = applyRungMove(state, 0, { type: "play_card", card: "AH" }, rng);
    if (!applied.ok) throw new Error("move refused");
    const messages = encodeRungEvents(
      ctxFor(applied.state, { seat: 0, clientSeq: null, auto: true }, false),
      applied.events,
    );
    expect(messages[0]?.payload).toMatchObject({ auto: true });
    expect(messages[0]?.payload).not.toHaveProperty("clientSeq");
  });

  it("holds the next turn while the table shows the call, a trick and a taken pile", () => {
    const delay = (types: RungEvent["type"][]): number =>
      rungHost.presentationDelayMs(types.map((type) => ({ type }) as RungEvent));
    expect(delay(["card_played"])).toBe(0);
    expect(delay(["card_played", "trick_won"])).toBe(RUNG_TRICK_MS);
    expect(delay(["card_played", "trick_won", "pile_taken"])).toBe(RUNG_PILE_MS);
    expect(delay(["trump_called", "hands_completed"])).toBe(RUNG_CALL_MS);
  });
});

describe("Rung host: moves, rules and registration", () => {
  it("decodes the four move types and refuses the rest", () => {
    const p = { roundId: "r_1", clientSeq: 1 };
    const decode = (type: string, payload: Record<string, unknown>): unknown => {
      const decoded = decodeRungMove(type, payload, checks.payload[type]);
      return decoded.ok ? decoded.value : decoded.error.detail?.["reason"];
    };
    expect(decode("play_card", { ...p, card: "AS" })).toEqual({ type: "play_card", card: "AS" });
    expect(decode("call_trump", { ...p, suit: "H" })).toEqual({ type: "call_trump", suit: "H" });
    expect(decode("redeal", p)).toEqual({ type: "redeal" });
    expect(decode("court_choice", { ...p, playOn: true })).toEqual({
      type: "court_choice",
      playOn: true,
    });
    expect(decode("call_trump", { ...p, suit: "X" })).toBe("schema");
    expect(decode("court_choice", p)).toBe("schema");
    expect(decode("take_hand", p)).toBe("unsupported_type");
    expect(rungHost.isMoveType("take_hand")).toBe(false);
    expect(rungHost.isMoveType("redeal")).toBe(true);
  });

  it("resolves the three rule sets and refuses another game's preset", () => {
    const rules = (preset?: string, overrides?: Record<string, unknown>): unknown => {
      const resolved = rungHost.resolveRules(
        { gameId: "rung", ...(preset === undefined ? {} : { preset }), overrides } as never,
        "private",
      );
      return resolved.ok ? resolved.value : resolved.error.detail?.["reason"];
    };
    expect(rules()).toMatchObject({ collect: "double" });
    expect(rules("courtPiece")).toMatchObject({ collect: "single" });
    expect(rules("hokm")).toMatchObject({ pointsTable: "hokm", chase: false });
    expect(rules("rung", { target: 5 })).toMatchObject({ target: 5 });
    expect(rules("standard")).toBe("unknown_preset");
    const thulla = hostFor("thulla")?.resolveRules({ gameId: "thulla", preset: "hokm" }, "private");
    expect(thulla?.ok).toBe(false);
  });

  it("serves three Rung presets, anticlockwise, beside Thulla's", () => {
    expect(presetsFor("rung").map((p) => p.id)).toEqual(["rung", "courtPiece", "hokm"]);
    expect(rungHost.normaliseRules?.({ gameId: "rung" })).toEqual({
      gameId: "rung",
      settings: { direction: "ccw" },
    });
    expect(presetsFor("thulla")).toHaveLength(5);
    expect(isGameId("rung")).toBe(true);
  });

  it("reads the stats card from the deal's messages", () => {
    const rows = rungHighlights(
      [
        { seq: 1, ts: 0, type: "rung_redealt", payload: { seat: 1 }, seat: null, stateHash: null },
        {
          seq: 2,
          ts: 0,
          type: "rung_trump_called",
          payload: { seat: 1 },
          seat: null,
          stateHash: null,
        },
        {
          seq: 3,
          ts: 0,
          type: "rung_round_end",
          payload: {
            stats: [
              { tricksWon: 4, cuts: 1, pilesTaken: 2 },
              { tricksWon: 3, cuts: 0, pilesTaken: 1 },
              { tricksWon: 0, cuts: 0, pilesTaken: 0 },
              { tricksWon: 0, cuts: 2, pilesTaken: 0 },
            ],
          },
          seat: null,
          stateHash: null,
        },
      ],
      4,
    );
    expect(rows[0]).toEqual({ tricksWon: 4, cuts: 1, pilesTaken: 2, calls: 0, redeals: 0 });
    expect(rows[1]).toEqual({ tricksWon: 3, cuts: 0, pilesTaken: 1, calls: 1, redeals: 1 });
    expect(rows[3]?.["cuts"]).toBe(2);
  });
});
