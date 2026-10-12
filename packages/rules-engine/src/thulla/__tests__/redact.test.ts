import { Xoshiro128StarStar, canonicalJson, type SeatIndex } from "@hulla/shared-ts";
import * as fc from "fast-check";
import { describe, expect, it } from "vitest";

import { isCard, type Card } from "../../card-kit";
import { createThullaRound } from "../deal";
import { applyMove, legalMoves } from "../engine";
import { resolveRules } from "../rules";
import { redact, redactEvent } from "../view";

import { stateFrom } from "./helpers";

import type { ThullaEvent } from "../events";
import type { ThullaState } from "../state";

/** Every card code that appears anywhere in a JSON document. */
function cardsMentioned(value: unknown): Set<Card> {
  const found = new Set<Card>();
  const text = canonicalJson(value as never);
  for (const match of text.matchAll(/"([2-9TJQKA][SHCD])"/g)) {
    const code = match[1];
    if (code !== undefined && isCard(code)) {
      found.add(code);
    }
  }
  return found;
}

function hiddenFrom(state: ThullaState, seat: SeatIndex | null): Set<Card> {
  const hidden = new Set<Card>(state.discard);
  state.seats.forEach((other, index) => {
    if (index !== seat) {
      for (const card of other.hand) {
        hidden.add(card);
      }
    }
  });
  return hidden;
}

function freshRound(
  seedByte: number,
  seatCount: number,
): { state: ThullaState; rng: Xoshiro128StarStar } {
  const seed = new Uint8Array(32).fill(seedByte);
  const rng = Xoshiro128StarStar.fromSeed(seed);
  const rules = resolveRules("private");
  if (!rules.ok) throw new Error(rules.error.code);
  const round = createThullaRound({ rules: rules.value, seatCount, dealerSeat: 0, seed }, rng);
  if (!round.ok) throw new Error(round.error.code);
  return { state: round.value, rng };
}

describe("redaction", () => {
  it("R6_the_seed_and_deck_order_are_hidden_until_the_round_ends_then_revealed", () => {
    const { state } = freshRound(3, 4);
    const view = redact(state, { kind: "spectator" });
    expect(view.seedHex).toBeNull();
    expect(view.deckOrder).toBeNull();
    expect(view.commitment).toBe(state.commitment);
    const ended = stateFrom({ hands: [["2H"], [], []], leader: 0, phase: "ended" });
    const revealed = redact(
      { ...ended, bhabhiSeat: 0, seedHex: "ab", deckOrder: ["2H"] },
      { kind: "spectator" },
    );
    expect(revealed.seedHex).toBe("ab");
    expect(revealed.deckOrder).toEqual(["2H"]);
  });

  it("R23_a_seat_sees_its_own_hand_counts_for_the_others_and_the_public_pile", () => {
    const state = stateFrom({
      hands: [["2H", "9S"], ["4H", "5C"], ["6H"]],
      leader: 0,
      plays: [{ seat: 0, card: "2H" }],
      discard: ["3D", "4D"],
    });
    const view = redact(state, { kind: "seat", seat: 1 });
    expect(view.seats.map((seat) => seat.hand)).toEqual([null, ["4H", "5C"], null]);
    expect(view.seats.map((seat) => seat.handCount)).toEqual([1, 2, 1]);
    expect(view.trick.plays).toEqual([{ seat: 0, card: "2H" }]);
    expect(view.discardCount).toBe(2);
    expect(view.canTakeHand).toBe(false);
    const spectator = redact(state, { kind: "spectator" });
    expect(spectator.seats.every((seat) => seat.hand === null)).toBe(true);
    expect(spectator.canTakeHand).toBeNull();
  });

  it("no_hidden_card_appears_in_any_non_owner_view_at_any_point_of_random_rounds", () => {
    fc.assert(
      fc.property(
        fc.integer({ min: 0, max: 255 }),
        fc.integer({ min: 3, max: 6 }),
        (seedByte, seatCount) => {
          const { rng } = freshRound(seedByte, seatCount);
          let { state } = freshRound(seedByte, seatCount);
          const picker = Xoshiro128StarStar.fromState([seedByte + 9, 1, 2, 3]);
          for (let step = 0; step < 400 && state.phase !== "ended"; step++) {
            const viewers: (SeatIndex | null)[] = [null];
            for (let seat = 0; seat < seatCount; seat++) viewers.push(seat);
            for (const viewer of viewers) {
              const view = redact(
                state,
                viewer === null ? { kind: "spectator" } : { kind: "seat", seat: viewer },
              );
              const mentioned = cardsMentioned(view);
              for (const card of hiddenFrom(state, viewer)) {
                expect(mentioned.has(card)).toBe(false);
              }
              expect(view.deckOrder).toBeNull();
              expect(view.seedHex).toBeNull();
            }
            const options: { seat: SeatIndex; card: Card }[] = [];
            for (let seat = 0; seat < seatCount; seat++) {
              for (const move of legalMoves(state, seat)) {
                if (move.type === "play_card") options.push({ seat, card: move.card });
              }
            }
            const choice = options[picker.nextInt(options.length)];
            if (choice === undefined) throw new Error("no move");
            const result = applyMove(
              state,
              choice.seat,
              { type: "play_card", card: choice.card },
              rng,
            );
            if (!result.ok) throw new Error(result.error.code);
            state = result.state;
          }
        },
      ),
      { numRuns: 40 },
    );
  });

  it("a_private_draw_reaches_only_the_seats_it_names", () => {
    const draw: ThullaEvent = {
      type: "private_draw",
      seat: 0,
      card: "8D",
      source: "opponent",
      from: 2,
      visibleTo: [0, 2],
    };
    expect(redactEvent(draw, { kind: "seat", seat: 0 })).toBe(draw);
    expect(redactEvent(draw, { kind: "seat", seat: 2 })).toBe(draw);
    expect(redactEvent(draw, { kind: "seat", seat: 1 })).toBeNull();
    expect(redactEvent(draw, { kind: "spectator" })).toBeNull();
    const pub: ThullaEvent = { type: "escape", seat: 1, rank: 1 };
    expect(redactEvent(pub, { kind: "spectator" })).toBe(pub);
  });
});
