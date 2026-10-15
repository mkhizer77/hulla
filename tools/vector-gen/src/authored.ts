import type { Card, ThullaSetup } from "@hulla/rules-engine";
import type { Vector, VectorAssert, VectorMove } from "@hulla/test-vectors";

interface Play {
  readonly seat: number;
  readonly card: Card;
}

function play(seat: number, card: Card, extra: Partial<VectorMove> = {}): VectorMove {
  return { seat, move: { type: "play_card", card }, ...extra };
}
function take(seat: number, extra: Partial<VectorMove> = {}): VectorMove {
  return { seat, move: { type: "take_hand" }, ...extra };
}
function rejected(error: string): Pick<VectorMove, "expect"> {
  return { expect: { error } };
}
function opening(
  hands: readonly (readonly Card[])[],
  leader: number,
  extra: Partial<ThullaSetup> = {},
): ThullaSetup {
  return {
    hands,
    leader,
    trickIndex: 0,
    phase: "opening",
    openingPending: hands.map((_, i) => i).filter((i) => i !== leader),
    ...extra,
  };
}
function plays(
  hands: readonly (readonly Card[])[],
  leader: number,
  onTable: readonly Play[],
  extra: Partial<ThullaSetup> = {},
): ThullaSetup {
  return { hands, leader, plays: onTable, ...extra };
}
const noFF = { confirmedFastForward: false } as const;
const seed = (byte: number): string => byte.toString(16).padStart(2, "0").repeat(32);

/** Scenarios with assertions worked out by hand from the GDD; ids from Appendix C are kept. */
export function authored(version: string): Vector[] {
  const v = (
    id: string,
    title: string,
    rules: string[],
    body: {
      deal?: Vector["deal"];
      setup?: ThullaSetup;
      moves?: VectorMove[];
      assert: VectorAssert;
    },
  ): Vector => ({
    id,
    version,
    title,
    rules,
    ...(body.deal !== undefined ? { deal: body.deal } : {}),
    ...(body.setup !== undefined ? { setup: body.setup } : {}),
    moves: body.moves ?? [],
    assert: body.assert,
  });
  return [
    // Deals (R1, R2, R6 to R9, E20)
    v(
      "V-001-deal-four-seats",
      "four seats deal 13 each; the Ace of Spades holder opens",
      ["R1", "R2", "R6", "R7", "R8", "R9"],
      {
        deal: { seedHex: seed(7), seatCount: 4, dealerSeat: 0 },
        assert: {
          handCounts: [13, 13, 13, 13],
          leaderHoldsAceOfSpades: true,
          phase: "opening",
          trickIndex: 0,
          discardCount: 0,
        },
      },
    ),
    v(
      "V-002-deal-three-seats",
      "three seats: 18, 17, 17 from the seat left of the dealer",
      ["R3", "R7"],
      {
        deal: { seedHex: seed(7), seatCount: 3, dealerSeat: 2 },
        assert: { handCounts: [18, 17, 17], leaderHoldsAceOfSpades: true },
      },
    ),
    v("V-003-deal-five-seats", "five seats: 11, 11, 10, 10, 10", ["R7", "E20"], {
      deal: { seedHex: seed(9), seatCount: 5, dealerSeat: 4 },
      assert: { handCounts: [11, 11, 10, 10, 10] },
    }),
    v("V-004-deal-six-seats", "six seats: 9, 9, 9, 9, 8, 8", ["R7", "E20"], {
      deal: { seedHex: seed(11), seatCount: 6, dealerSeat: 5 },
      assert: { handCounts: [9, 9, 9, 9, 8, 8] },
    }),
    v(
      "V-005-deal-five-seats-dealer-zero",
      "the extra cards follow the new dealer",
      ["R5", "R7", "E20"],
      {
        deal: { seedHex: seed(9), seatCount: 5, dealerSeat: 0 },
        assert: { handCounts: [10, 11, 11, 10, 10] },
      },
    ),
    v(
      "V-006-deal-same-seed-same-round",
      "the same seed gives the same round (compare stateHash with V-001)",
      ["R6"],
      {
        deal: { seedHex: seed(7), seatCount: 4, dealerSeat: 0 },
        assert: { handCounts: [13, 13, 13, 13] },
      },
    ),
    v(
      "V-007-deal-hardcore-preset",
      "a deal under the hardcore preset still opens on the Ace",
      ["R9", "R15"],
      {
        deal: { seedHex: seed(3), seatCount: 4, dealerSeat: 1, preset: "hardcore" },
        assert: { leaderHoldsAceOfSpades: true, phase: "opening" },
      },
    ),
    v(
      "V-008-deal-public-mode",
      "a public-mode deal: hand-taking off, single round",
      ["R31", "R52"],
      {
        deal: { seedHex: seed(5), seatCount: 4, dealerSeat: 0, mode: "public" },
        assert: { handCounts: [13, 13, 13, 13] },
      },
    ),
    // Opening (R12 to R15, R30, E2)
    v(
      "V-010-opening-only-the-ace-leads",
      "the opener must lead the Ace of Spades; nobody else may act",
      ["R12"],
      {
        setup: opening(
          [
            ["AS", "2H"],
            ["3S", "4H"],
            ["5H", "6D"],
            ["7S", "8S"],
          ],
          0,
        ),
        moves: [
          play(0, "2H", rejected("rules.must_lead_ace_of_spades")),
          play(1, "3S", rejected("rules.not_your_turn")),
          play(0, "AS"),
        ],
        assert: { turnSeat: null, lastEvents: ["card_played"], handCounts: [1, 2, 2, 2] },
      },
    ),
    v(
      "V-011-opening-simultaneous-follow-spades",
      "the others respond in any order and must follow spades",
      ["R13"],
      {
        setup: opening(
          [
            ["AS", "2H"],
            ["3S", "4H"],
            ["5H", "6D"],
            ["7S", "8S"],
          ],
          0,
        ),
        moves: [
          play(0, "AS"),
          play(1, "4H", rejected("rules.must_follow_suit")),
          play(3, "8S"),
          play(3, "7S", rejected("rules.not_your_turn")),
          play(2, "6D"),
        ],
        assert: { turnSeat: null, handCounts: [1, 2, 1, 1], phase: "opening" },
      },
    ),
    v(
      "V-012-opening-no-pickup",
      "the opening trick goes to the discard and the Ace holder leads trick two",
      ["R14", "R30"],
      {
        setup: opening(
          [
            ["AS", "2H"],
            ["3S", "4H"],
            ["5H", "6D"],
            ["7S", "8S"],
          ],
          0,
        ),
        moves: [play(0, "AS"), play(2, "6D"), play(1, "3S"), play(3, "7S")],
        assert: {
          lastEvents: ["card_played", "trick_resolved"],
          discard: ["AS", "6D", "3S", "7S"],
          leaderSeat: 0,
          turnSeat: 0,
          trickIndex: 1,
          phase: "play",
          discardCount: 4,
        },
      },
    ),
    v(
      "V-013-opening-two-off-suit-plays",
      "two off-suit plays in the opening still cause no pick-up",
      ["E2", "R14"],
      {
        setup: opening(
          [
            ["AS", "2H"],
            ["4H", "5H"],
            ["6D", "7D"],
            ["7S", "8S"],
          ],
          0,
        ),
        moves: [play(0, "AS"), play(1, "4H"), play(2, "6D"), play(3, "7S")],
        assert: { discardCount: 4, handCounts: [1, 1, 1, 1], leaderSeat: 0 },
      },
    ),
    v(
      "V-014-opening-thulla-applies",
      "with the toggle on, the opening is in turn order and the opener picks up",
      ["R15"],
      {
        setup: {
          hands: [
            ["AS", "2H"],
            ["4H", "5H"],
            ["6S", "7D"],
            ["7S", "8S"],
          ],
          leader: 0,
          trickIndex: 0,
          phase: "opening",
          preset: "hardcore",
          rules: { thullaEndsTrick: true },
        },
        moves: [play(0, "AS"), play(2, "6S", rejected("rules.not_your_turn")), play(1, "4H")],
        assert: {
          lastEvents: ["card_played", "trick_resolved"],
          hands: { "0": ["AS", "2H", "4H"] },
          leaderSeat: 0,
          phase: "play",
          stats: { "0": { cardsPickedUp: 2 }, "1": { thullasGiven: 1 } },
        },
      },
    ),
    v(
      "V-015-opening-turn-order-no-thulla",
      "turn order without the thulla toggle: an off-suit play is just a play",
      ["R13", "R14"],
      {
        setup: {
          hands: [
            ["AS", "2H"],
            ["4H", "5H"],
            ["6S", "7D"],
          ],
          leader: 0,
          trickIndex: 0,
          phase: "opening",
          rules: { openingSimultaneous: false },
        },
        moves: [play(0, "AS"), play(1, "4H"), play(2, "6S")],
        assert: { discard: ["AS", "4H", "6S"], leaderSeat: 0, phase: "play" },
      },
    ),
    // Normal tricks (R16 to R24, R47)
    v(
      "V-021-thulla-ends-trick",
      "Ali 6♥, Sara K♥, Bilal Q♠ thulla: the trick ends, Noor does not play, Sara picks up",
      ["R19", "R20", "R21"],
      {
        setup: plays(
          [
            ["6H", "2C"],
            ["KH", "3C"],
            ["QS", "4C"],
            ["9H", "5C"],
          ],
          0,
          [
            { seat: 0, card: "6H" },
            { seat: 1, card: "KH" },
          ],
          { trickIndex: 5 },
        ),
        moves: [play(2, "QS")],
        assert: {
          lastEvents: ["card_played", "trick_resolved"],
          hands: { "3": ["9H", "5C"], "1": ["6H", "KH", "QS", "3C"] },
          leaderSeat: 1,
          turnSeat: 1,
          discardCount: 0,
          trickIndex: 6,
          stats: { "1": { cardsPickedUp: 3, thullasReceived: 1 }, "2": { thullasGiven: 1 } },
        },
      },
    ),
    v(
      "V-022-lead-any-and-skip-escaped",
      "the leader leads anything; responses skip escaped seats",
      ["R4", "R16", "R17"],
      {
        setup: { hands: [["2H", "9S"], [], ["4H", "5C"], ["6H"]], leader: 0 },
        moves: [
          play(0, "2H"),
          play(3, "6H", rejected("rules.not_your_turn")),
          play(1, "2H", rejected("rules.seat_not_active")),
        ],
        assert: { turnSeat: 2 },
      },
    ),
    v("V-023-must-follow-suit", "a responder holding the led suit must follow it", ["R18"], {
      setup: plays([["2H"], ["4H", "5C"], ["6H"]], 0, [{ seat: 0, card: "2H" }]),
      moves: [play(1, "5C", rejected("rules.must_follow_suit"))],
      assert: { turnSeat: 1, handCounts: [0, 2, 1] },
    }),
    v(
      "V-024-void-player-thullas",
      "a void responder may play anything and it is a thulla",
      ["R19", "R20"],
      {
        setup: plays([["2H"], ["5C", "QS"], ["6H"]], 0, [{ seat: 0, card: "2H" }]),
        moves: [play(1, "QS")],
        assert: {
          lastEvents: ["card_played", "trick_resolved"],
          hands: { "0": ["2H", "QS"] },
          leaderSeat: 0,
        },
      },
    ),
    v(
      "V-025-no-thulla-goes-to-discard",
      "without a thulla the cards go to the discard and the highest leads",
      ["R22", "R23"],
      {
        setup: plays(
          [
            ["6H", "2C"],
            ["KH", "3C"],
            ["7H", "4C"],
          ],
          0,
          [
            { seat: 0, card: "6H" },
            { seat: 1, card: "KH" },
          ],
        ),
        moves: [play(2, "7H")],
        assert: {
          discard: ["6H", "KH", "7H"],
          leaderSeat: 1,
          turnSeat: 1,
          lastEvents: ["card_played", "trick_resolved"],
        },
      },
    ),
    v(
      "V-026-thulla-ends-trick-off",
      "play continues after a thulla and the highest takes everything",
      ["R24"],
      {
        setup: plays(
          [
            ["6H", "2C"],
            ["KH", "3C"],
            ["QS", "4C"],
            ["9H", "5C"],
          ],
          0,
          [
            { seat: 0, card: "6H" },
            { seat: 1, card: "KH" },
          ],
          { preset: "hardcore" },
        ),
        moves: [play(2, "QS"), play(3, "9H")],
        assert: {
          lastEvents: ["card_played", "trick_resolved"],
          hands: { "1": ["QS", "6H", "9H", "KH", "3C"] },
          leaderSeat: 1,
        },
      },
    ),
    v(
      "V-027-illegal-plays-rejected",
      "illegal plays are rejected with a reason and change nothing",
      ["R47"],
      {
        setup: plays([["2H"], ["4H", "5C"], ["6H"]], 0, [{ seat: 0, card: "2H" }]),
        moves: [
          play(1, "AS", rejected("rules.card_not_in_hand")),
          play(9, "4H", rejected("rules.invalid_seat")),
          play(2, "6H", rejected("rules.not_your_turn")),
        ],
        assert: { hands: { "1": ["4H", "5C"] }, turnSeat: 1 },
      },
    ),
    // Escaping and the empty-hand leader (R25 to R30, E3 to E7, E26, E30, E31, R10, R11)
    v(
      "V-030-leader-with-empty-hand-draws",
      "a seat that wins with its last card is the leader, not an escape; it draws",
      ["R25", "R26"],
      {
        setup: plays(
          [["6H", "2C"], ["KH"], ["7H", "4C"]],
          0,
          [
            { seat: 0, card: "6H" },
            { seat: 1, card: "KH" },
          ],
          { discard: ["2S"] },
        ),
        moves: [play(2, "7H", { draw: 0 })],
        assert: {
          leaderSeat: 1,
          hands: { "1": ["2S"] },
          escapeOrder: [],
          pendingAutoLead: true,
          lastEvents: ["card_played", "trick_resolved", "private_draw"],
        },
      },
    ),
    v(
      "V-034-empty-hand-draw-from-discard",
      "Noor wins with her last card and draws from the discard, not this trick",
      ["R26", "E4"],
      {
        setup: plays(
          [["9C", "2D"], ["7C", "5D"], ["4C", "TD"], ["JC"]],
          2,
          [
            { seat: 2, card: "4C" },
            { seat: 3, card: "JC" },
            { seat: 0, card: "9C" },
          ],
          { discard: ["8D", "QH"] },
        ),
        moves: [play(1, "7C", { draw: 0 })],
        assert: {
          leaderSeat: 3,
          hands: { "3": ["8D"] },
          discard: ["QH", "4C", "JC", "9C", "7C"],
          pendingAutoLead: true,
          autoPlay: { "3": "8D" },
          handCounts: [1, 1, 1, 1],
        },
      },
    ),
    v(
      "V-035-drawer-escapes-when-beaten",
      "after the draw the next trick resolves normally and the drawer escapes",
      ["R27"],
      {
        setup: {
          hands: [["2D", "3H"], ["5D", "4H"], ["TD", "6H"], ["8D"]],
          leader: 3,
          pendingAutoLead: true,
        },
        moves: [play(3, "8D"), play(0, "2D"), play(1, "5D"), play(2, "TD")],
        assert: { escapeOrder: [3], leaderSeat: 2, pendingAutoLead: false },
      },
    ),
    v(
      "V-036-drawer-picks-up-on-thulla",
      "a thulla after the draw makes the drawer pick up and stay in",
      ["R27", "E3"],
      {
        setup: {
          hands: [["2D", "3H"], ["KS", "4H"], ["TD", "6H"], ["8D"]],
          leader: 3,
          pendingAutoLead: true,
        },
        moves: [play(3, "8D"), play(0, "2D"), play(1, "KS")],
        assert: { handContains: { "3": ["KS", "2D", "8D"] }, escapeOrder: [], leaderSeat: 3 },
      },
    ),
    v(
      "V-037-draw-next-player",
      "the drawNextPlayer option takes a card from the next seat, who sees it go",
      ["R28"],
      {
        setup: plays(
          [["9C"], ["7C", "5D", "QH"], ["4C", "TD"]],
          2,
          [
            { seat: 2, card: "4C" },
            { seat: 0, card: "9C" },
          ],
          { rules: { emptyHandLeader: "drawNextPlayer" } },
        ),
        moves: [play(1, "7C", { draw: 0 })],
        assert: {
          hands: { "0": ["QH"], "1": ["5D"] },
          lastEvents: ["card_played", "trick_resolved", "private_draw"],
          pendingAutoLead: true,
        },
      },
    ),
    v(
      "V-038-instant-exit",
      "the instantExit option escapes the leader and the next seat leads",
      ["R28"],
      {
        setup: plays(
          [["9C"], ["7C", "5D"], ["4C", "TD"]],
          2,
          [
            { seat: 2, card: "4C" },
            { seat: 0, card: "9C" },
          ],
          { rules: { emptyHandLeader: "instantExit", ...noFF } },
        ),
        moves: [play(1, "7C")],
        assert: {
          escapeOrder: [0],
          leaderSeat: 1,
          turnSeat: 1,
          pendingAutoLead: false,
          discard: ["4C", "9C", "7C"],
        },
      },
    ),
    v(
      "V-040-empty-discard-draws-next-player",
      "an empty discard under non-default toggles draws from the next player (OQ-001)",
      ["E31", "R26"],
      {
        setup: plays(
          [["9C"], ["7C", "5D"], ["4C", "TD"]],
          2,
          [
            { seat: 2, card: "4C" },
            { seat: 0, card: "9C" },
          ],
          { discard: [], rules: noFF },
        ),
        moves: [play(1, "7C", { draw: 0 })],
        assert: {
          hands: { "0": ["5D"] },
          escapeOrder: [1],
          eventsInclude: ["private_draw", "escape"],
        },
      },
    ),
    v(
      "V-041-leader-last-card-highest-picks-up",
      "the leader who led the last card picks up when it is highest and stays active",
      ["E3", "E7"],
      {
        setup: plays([["KH"], ["2H", "4C"], ["QS", "5C"]], 0, [
          { seat: 0, card: "KH" },
          { seat: 1, card: "2H" },
        ]),
        moves: [play(2, "QS")],
        assert: { hands: { "0": ["QS", "2H", "KH"] }, escapeOrder: [], leaderSeat: 0 },
      },
    ),
    v(
      "V-042-drawer-beaten-then-thulla",
      "the drawer's led card is beaten and a later seat thullas: the highest led card picks up",
      ["E5"],
      {
        setup: plays(
          [["8D"], ["KD", "2C"], ["QS", "5C"]],
          0,
          [
            { seat: 0, card: "8D" },
            { seat: 1, card: "KD" },
          ],
          { rules: noFF },
        ),
        moves: [play(2, "QS")],
        assert: { escapeOrder: [0], handContains: { "1": ["8D", "KD", "QS"] }, leaderSeat: 1 },
      },
    ),
    v(
      "V-043-everyone-else-escapes-same-trick",
      "when everyone else escapes in the trick the leader won, the leader is the Bhabhi without a draw",
      ["R10", "E30", "R49", "R50"],
      {
        setup: plays(
          [["9C"], ["7C"], ["4C"]],
          2,
          [
            { seat: 2, card: "4C" },
            { seat: 0, card: "9C" },
          ],
          { discard: ["2S"] },
        ),
        moves: [play(1, "7C")],
        assert: {
          lastEvents: ["card_played", "trick_resolved", "escape", "escape", "round_ended"],
          phase: "ended",
          bhabhiSeat: 0,
          escapeOrder: [1, 2],
          results: { finishOrder: [1, 2, 0], loserSeat: 0, points: [-1, 3, 2], bhabhiCards: 0 },
          views: [{ viewer: "spectator", seedHidden: false }],
        },
      },
    ),
    v(
      "V-044-escape-ranks-seat-order",
      "escape ranks follow seat order from the leader and are never tied",
      ["R11", "R27"],
      {
        setup: plays([["9C", "AH"], ["7C"], ["4C"], ["8C"]], 2, [
          { seat: 2, card: "4C" },
          { seat: 3, card: "8C" },
          { seat: 0, card: "9C" },
        ]),
        moves: [play(1, "7C")],
        assert: { escapeOrder: [1, 2, 3], bhabhiSeat: 0, phase: "ended" },
      },
    ),
    v(
      "V-045-draw-opponents-last-card",
      "drawing the opponent's last card ends the round with the leader as Bhabhi",
      ["E12", "R38"],
      {
        setup: plays([["9S"], ["3S", "7S"], []], 0, [{ seat: 0, card: "9S" }]),
        moves: [play(1, "3S", { draw: 0 })],
        assert: {
          lastEvents: [
            "card_played",
            "trick_resolved",
            "confirmed",
            "private_draw",
            "escape",
            "round_ended",
          ],
          bhabhiSeat: 0,
          hands: { "0": ["7S"] },
        },
      },
    ),
    v(
      "V-046-draw-before-endgame",
      "the trick that leaves two players is followed by the endgame draw from the opponent",
      ["E26"],
      {
        setup: plays(
          [["9C", "KH"], ["7C"], ["4C", "TD", "3H", "5D"]],
          2,
          [
            { seat: 2, card: "4C" },
            { seat: 0, card: "9C" },
          ],
          { discard: ["2S", "3S"], rules: noFF },
        ),
        moves: [play(1, "7C"), play(0, "KH"), play(2, "3H", { draw: 1 })],
        assert: {
          escapeOrder: [1],
          eventsInclude: ["endgame_started", "private_draw"],
          handCounts: [1, 0, 1],
          hands: { "0": ["TD"], "2": ["5D"] },
        },
      },
    ),
    // Hand-taking (R31 to R36, E8 to E11)
    v("V-050-hand-taking-off-in-ranked", "hand-taking is off in ranked tables", ["R31"], {
      setup: { hands: [["2H"], ["3H"], ["4H"]], leader: 0, mode: "ranked" },
      moves: [take(0, rejected("rules.hand_take_disabled"))],
      assert: { leaderSeat: 0 },
    }),
    v(
      "V-053-take-leader-only-in-public",
      "public tables allow the leader alone to take",
      ["R31", "R32"],
      {
        setup: { hands: [["2H", "9S"], ["3H"], ["4H"]], leader: 0, mode: "public" },
        moves: [take(1, rejected("rules.hand_take_not_leader"))],
        assert: { leaderSeat: 0 },
      },
    ),
    v("V-051-take-window-from-trick-two", "no take before trick two", ["R32"], {
      setup: { hands: [["2H"], ["3H"], ["4H"]], leader: 0, trickIndex: 0 },
      moves: [take(0, rejected("rules.hand_take_window_closed"))],
      assert: { trickIndex: 0 },
    }),
    v("V-052-take-leader-only", "only the leader may take in leaderOnly mode", ["R32"], {
      setup: { hands: [["2H", "9S"], ["3H"], ["4H"]], leader: 0 },
      moves: [take(1, rejected("rules.hand_take_not_leader"))],
      assert: { leaderSeat: 0 },
    }),
    v(
      "V-070-hand-take-leader-only",
      "the leader takes the next seat's three cards; the target escapes; the taker leads",
      ["R33"],
      {
        setup: {
          hands: [
            ["5H", "6H"],
            ["7H", "8H"],
            ["2C", "4D", "9S"],
            ["TH", "JH"],
          ],
          leader: 1,
          trickIndex: 3,
          rules: { handTaking: "leaderOnly" },
        },
        moves: [take(1)],
        assert: {
          lastEvents: ["hand_taken", "escape"],
          hands: { "1": ["9S", "7H", "8H", "2C", "4D"] },
          escapeOrder: [2],
          leaderSeat: 1,
          turnSeat: 1,
          handCounts: [2, 5, 0, 2],
          stats: { "1": { handsTaken: 1 } },
        },
      },
    ),
    v("V-053-lead-closes-take-window", "the leader's play closes the take window", ["E11", "R35"], {
      setup: { hands: [["2H", "9S"], ["3H"], ["4H"]], leader: 0 },
      moves: [play(0, "2H"), take(0, rejected("rules.hand_take_window_closed"))],
      assert: { turnSeat: 1 },
    }),
    v(
      "V-054-anyone-first-take-wins",
      "in anyone mode any active seat may take and the first accepted wins",
      ["R35", "E10"],
      {
        setup: {
          hands: [["2H", "9S"], ["3H"], ["4H", "5H"], ["6H"]],
          leader: 0,
          rules: { handTaking: "anyone" },
        },
        moves: [take(2), take(0, rejected("rules.hand_take_window_closed"))],
        assert: { leaderSeat: 2, escapeOrder: [3], hands: { "2": ["4H", "5H", "6H"] } },
      },
    ),
    v(
      "V-071-hand-take-rejected-two-active",
      "a take with two active players is HAND_TAKE_DISABLED_ENDGAME",
      ["R36", "R37"],
      {
        setup: { hands: [["2H", "9S"], ["3H"]], leader: 0, rules: { handTaking: "leaderOnly" } },
        moves: [take(0, rejected("rules.hand_take_disabled_endgame"))],
        assert: { leaderSeat: 0 },
      },
    ),
    v(
      "V-055-target-with-one-card",
      "a target with one card can be taken and escapes with that rank",
      ["E8", "E9"],
      {
        setup: { hands: [["2H"], ["3H"], ["4H", "5H"]], leader: 0, rules: noFF },
        moves: [take(0)],
        assert: {
          hands: { "0": ["2H", "3H"] },
          escapeOrder: [1],
          eventsInclude: ["hand_taken", "escape", "endgame_started"],
        },
      },
    ),
    v(
      "V-056-auto-lead-blocks-take",
      "a leader who just drew must lead the drawn card and cannot take",
      ["R26", "R34"],
      {
        setup: { hands: [["8D"], ["3H"], ["4H"]], leader: 0, pendingAutoLead: true },
        moves: [take(0, rejected("rules.hand_take_window_closed"))],
        assert: { autoPlay: { "0": "8D" } },
      },
    ),
    // The endgame (R37 to R42, E12 to E14, E29)
    v(
      "V-057-endgame-draw-from-opponent-confirmed",
      "GDD example 3: confirmed the instant Sara plays 3♠; fast-forwarded to Ali as Bhabhi",
      ["R38", "R39", "R41", "E14"],
      {
        setup: plays([["9S"], ["3S", "7S", "QH", "2D"]], 0, [{ seat: 0, card: "9S" }]),
        moves: [play(1, "3S", { draw: 2 })],
        assert: {
          eventsInclude: ["confirmed", "round_ended"],
          confirmedBhabhi: 0,
          bhabhiSeat: 0,
          phase: "ended",
        },
      },
    ),
    v(
      "V-058-endgame-not-confirmed",
      "not confirmed: one draw lets Sara play K♥ higher",
      ["R41", "E14"],
      {
        setup: plays([["9S"], ["3S", "7S", "QH", "KH"]], 0, [{ seat: 0, card: "9S" }]),
        moves: [play(1, "3S", { draw: 0 })],
        assert: {
          confirmedBhabhi: null,
          phase: "play",
          lastEvents: ["card_played", "trick_resolved", "private_draw"],
          pendingAutoLead: true,
        },
      },
    ),
    v(
      "V-059-confirmed-without-fast-forward",
      "with fast-forward off the confirmed event fires and play continues by hand",
      ["R41"],
      {
        setup: plays([["9S"], ["3S", "7S", "QH", "2D"]], 0, [{ seat: 0, card: "9S" }], {
          rules: noFF,
        }),
        moves: [play(1, "3S", { draw: 1 })],
        assert: { eventsInclude: ["confirmed"], phase: "play", confirmedBhabhi: 0 },
      },
    ),
    v(
      "V-061-last-card-thulla-escapes",
      "a last card played as a thulla escapes while the picker takes the pile; the endgame begins",
      ["R29", "E6", "R37"],
      {
        setup: plays(
          [["5C", "2H"], ["TC", "3H"], ["AD"]],
          0,
          [
            { seat: 0, card: "5C" },
            { seat: 1, card: "TC" },
          ],
          { rules: noFF },
        ),
        moves: [play(2, "AD")],
        assert: {
          escapeOrder: [2],
          handContains: { "1": ["5C", "TC", "AD"] },
          eventsInclude: ["endgame_started"],
          leaderSeat: 1,
        },
      },
    ),
    v(
      "V-062-endgame-opponent-higher",
      "the opponent plays higher: the leader escapes and the opponent is the Bhabhi",
      ["R39"],
      {
        setup: plays([["9S"], ["KS", "7S"], []], 0, [{ seat: 0, card: "9S" }]),
        moves: [play(1, "KS")],
        assert: { phase: "ended", bhabhiSeat: 1, escapeOrder: [2, 0] },
      },
    ),
    v(
      "V-063-endgame-both-empty",
      "the opponent beats the leader's last card with their own last card and is the Bhabhi with no cards",
      ["E29", "R39"],
      {
        setup: plays([["9S"], ["KS"], []], 0, [{ seat: 0, card: "9S" }]),
        moves: [play(1, "KS")],
        assert: { bhabhiSeat: 1, hands: { "1": [] }, escapeOrder: [2, 0] },
      },
    ),
    v(
      "V-064-endgame-thulla-pickup-continues",
      "the opponent thullas: the leader picks up and play continues",
      ["R39"],
      {
        setup: plays([["9S", "2H"], ["QH", "KD"], []], 0, [{ seat: 0, card: "9S" }], {
          rules: noFF,
        }),
        moves: [play(1, "QH")],
        assert: { hands: { "0": ["9S", "2H", "QH"] }, phase: "play", leaderSeat: 0 },
      },
    ),
    v(
      "V-065-endgame-last-card-thulla",
      "the opponent's last card is a thulla: the opponent escapes and the leader is the Bhabhi",
      ["E13", "R39"],
      {
        setup: plays([["9S", "2H"], ["QH"], []], 0, [{ seat: 0, card: "9S" }]),
        moves: [play(1, "QH")],
        assert: { bhabhiSeat: 0, escapeOrder: [2, 1], phase: "ended" },
      },
    ),
    v(
      "V-066-endgame-lower-last-card",
      "the opponent follows lower with their last card: the leader is the Bhabhi",
      ["R39"],
      {
        setup: plays([["9S"], ["3S"], []], 0, [{ seat: 0, card: "9S" }]),
        moves: [play(1, "3S")],
        assert: {
          lastEvents: ["card_played", "trick_resolved", "escape", "round_ended"],
          bhabhiSeat: 0,
        },
      },
    ),
    v(
      "V-067-endgame-draw-discard-option",
      "the drawDiscard endgame option draws from the discard when it has cards",
      ["R42"],
      {
        setup: plays([["9S"], ["3S", "7S", "QH", "KH"], []], 0, [{ seat: 0, card: "9S" }], {
          discard: ["4C", "5C"],
          rules: { twoPlayerEndgame: "drawDiscard", ...noFF },
        }),
        moves: [play(1, "3S", { draw: 1 })],
        assert: {
          hands: { "0": ["5C"] },
          eventsInclude: ["private_draw"],
          discard: ["4C", "9S", "3S"],
        },
      },
    ),
    v(
      "V-068-endgame-thulla-ends-round",
      "the thullaEndsRound option: the first endgame thulla decides the Bhabhi",
      ["R42"],
      {
        setup: plays([["9S", "2H"], ["QH", "KD", "3C"], []], 0, [{ seat: 0, card: "9S" }], {
          rules: { twoPlayerEndgame: "thullaEndsRound" },
        }),
        moves: [play(1, "QH")],
        assert: { phase: "ended", bhabhiSeat: 0, escapeOrder: [2, 1] },
      },
    ),
    v(
      "V-069-endgame-no-take",
      "the endgame begins with two active and hand-taking is off",
      ["R37", "R36"],
      {
        setup: { hands: [["9S", "2H"], ["3S", "7S"], []], leader: 0 },
        moves: [take(0, rejected("rules.hand_take_disabled_endgame"))],
        assert: { leaderSeat: 0 },
      },
    ),
    v(
      "V-072-endgame-draw-from-opponent",
      "the leader who plays the last card draws one from the opponent, who sees it leave",
      ["R38"],
      {
        setup: plays([["9S"], ["3S", "7S", "QH", "KH"], []], 0, [{ seat: 0, card: "9S" }]),
        moves: [play(1, "3S", { draw: 1 })],
        assert: {
          lastEvents: ["card_played", "trick_resolved", "private_draw"],
          handCounts: [1, 2, 0],
          pendingAutoLead: true,
          hands: { "0": ["QH"] },
        },
      },
    ),
    v("V-073-endgame-terminates", "a longer endgame still ends with one Bhabhi", ["R40"], {
      setup: plays(
        [["2S", "3H", "4D"], ["9S", "KH", "QD", "2D"], []],
        0,
        [{ seat: 0, card: "2S" }],
        { rules: noFF },
      ),
      moves: [play(1, "9S"), play(1, "KH"), play(0, "3H"), play(1, "QD"), play(0, "4D")],
      assert: { phase: "ended", bhabhiSeat: 1, escapeOrder: [2, 0] },
    }),
    // Redaction and auto-play (R6, R23, R44)
    v(
      "V-080-redaction",
      "a seat sees its own hand, counts for the others and the public pile; spectators see no hand",
      ["R23", "R6"],
      {
        setup: plays([["2H", "9S"], ["4H", "5C"], ["6H"], ["7D"]], 0, [{ seat: 0, card: "2H" }], {
          discard: ["3D", "4D"],
        }),
        assert: {
          views: [
            {
              viewer: 1,
              ownHand: ["4H", "5C"],
              handCounts: [1, 2, 1, 1],
              discardCount: 2,
              seedHidden: true,
              canTakeHand: false,
            },
            { viewer: "spectator", ownHand: null, seedHidden: true, canTakeHand: null },
          ],
        },
      },
    ),
    v("V-082-autoplay-follow-low", "auto-play follows suit with the lowest card", ["R44"], {
      setup: plays([["2H"], ["4H", "9H", "KS"], ["6H"]], 0, [{ seat: 0, card: "2H" }]),
      assert: { autoPlay: { "1": "4H" } },
    }),
    v(
      "V-083-autoplay-dump-high",
      "a forced thulla dumps the highest card, ties broken ♠ ♥ ♣ ♦",
      ["R44"],
      {
        setup: plays([["2H"], ["KS", "KD", "3C"], ["6H"]], 0, [{ seat: 0, card: "2H" }]),
        assert: { autoPlay: { "1": "KS" } },
      },
    ),
    v(
      "V-084-autoplay-lead-longest-suit",
      "auto-play leads the lowest card of the longest suit",
      ["R44"],
      {
        setup: { hands: [["9S", "2S", "QH", "3D", "4D"], ["4H"], ["6H"]], leader: 0 },
        assert: { autoPlay: { "0": "2S" } },
      },
    ),
    v("V-085-autoplay-lead-tie", "a tie between suits goes to the lowest rank overall", ["R44"], {
      setup: { hands: [["9S", "QH"], ["4H"], ["6H"]], leader: 0 },
      assert: { autoPlay: { "0": "9S" } },
    }),
    v(
      "V-086-autoplay-opening",
      "the opening auto-play is the Ace of Spades, then the lowest spade or the highest card",
      ["R44", "R12", "R13"],
      {
        setup: opening(
          [
            ["AS", "2H"],
            ["3S", "4H"],
            ["5H", "6D"],
          ],
          0,
        ),
        moves: [play(0, "AS")],
        assert: { autoPlay: { "1": "3S", "2": "6D" } },
      },
    ),
    v(
      "V-091-big-bhabhi-penalty",
      "the Bhabhi holding eight or more cards scores minus two when toggled",
      ["R50", "R49"],
      {
        setup: plays(
          [["2C"], ["3C"], ["4C"], ["5C"], ["9C", "6H", "7H", "8H", "9H", "TH", "JH", "QH", "KH"]],
          4,
          [
            { seat: 4, card: "9C" },
            { seat: 0, card: "2C" },
            { seat: 1, card: "3C" },
            { seat: 2, card: "4C" },
          ],
          { rules: { bigBhabhiPenalty: true } },
        ),
        moves: [play(3, "5C")],
        assert: {
          phase: "ended",
          bhabhiSeat: 4,
          results: { points: [3, 2, 1, 0, -2], bhabhiCards: 8, finishOrder: [0, 1, 2, 3, 4] },
        },
      },
    ),
  ];
}
