import type { Card, RungMove, RungOutcome, RungSetup } from "@hulla/rules-engine";
import type {
  RungVector,
  RungVectorAssert,
  RungVectorBazi,
  RungVectorMove,
} from "@hulla/test-vectors";

type Hands = readonly (readonly Card[])[];

const seed = (byte: number): string => byte.toString(16).padStart(2, "0").repeat(32);

/** Dealer 3 makes seat 0 the caller and first leader. */
function pos(hands: Hands, extra: Partial<RungSetup> = {}): RungSetup {
  return { hands, trump: "S", dealer: 3, ...extra };
}
/** `"0:AH 1:2H"` as moves. */
function seq(plays: string): RungVectorMove[] {
  return plays.split(" ").map((token) => {
    const [seat, card] = token.split(":");
    return { seat: Number(seat), move: { type: "play_card", card: card as Card } };
  });
}
function refused(seat: number, move: RungMove, error: string): RungVectorMove {
  return { seat, move, expect: { error } };
}
const call = (suit: "S" | "H" | "C" | "D"): RungMove => ({ type: "call_trump", suit });
const CALLS: RungMove[] = [call("S"), call("H"), call("C"), call("D")];
const STOP: RungMove = { type: "court_choice", playOn: false };
const PLAY_ON: RungMove = { type: "court_choice", playOn: true };
const REDEAL: RungMove = { type: "redeal" };

const SINGLE = { collect: "single" } as const;
const WEAK: Card[] = ["2S", "5S", "7H", "9C", "TD"];
const FIVES: Hands = [
  WEAK,
  ["AS", "KS", "QS", "JS", "TS"],
  ["AH", "KH", "QH", "JH", "TH"],
  ["AC", "KC", "QC", "JC", "TC"],
];
function calling(first: readonly Card[], extra: Partial<RungSetup> = {}): RungSetup {
  return pos([first, FIVES[1] ?? [], FIVES[2] ?? [], FIVES[3] ?? []], { trump: null, ...extra });
}

/** Seat 0 holds the winners; everyone follows hearts, then clubs, then diamonds. */
function three(top: string, extra: Partial<RungSetup> = {}): RungSetup {
  return pos(
    [
      [`${top[0]}H`, `${top[1]}C`, `${top[2]}D`] as Card[],
      ["2H", "2C", "2D"],
      ["3H", "3C", "3D"],
      ["4H", "4C", "4D"],
    ],
    extra,
  );
}
const t1 = (top: string): string => `0:${top}H 1:2H 2:3H 3:4H`;
const t2 = (top: string): string => `0:${top}C 1:2C 2:3C 3:4C`;
const t3 = (top: string): string => `0:${top}D 1:2D 2:3D 3:4D`;

/** Seat 0 wins a heart trick; seat 1 can then win a club and a diamond trick. */
function ending(extra: Partial<RungSetup> = {}): RungSetup {
  return pos(
    [
      ["AH", "2C", "2D"],
      ["2H", "AC", "AD"],
      ["3H", "3C", "3D"],
      ["4H", "4C", "4D"],
    ],
    extra,
  );
}
const HEARTS = "0:AH 1:2H 2:3H 3:4H";
const CLUBS_TO_1 = "0:2C 1:AC 2:3C 3:4C";
const last = (extra: Partial<RungSetup>): RungSetup =>
  pos([["AH"], ["2H"], ["3H"], ["4H"]], { trickIndex: 12, ...extra });

const A_WIN: RungOutcome = { team: 0, kind: "win" };
const B_WIN: RungOutcome = { team: 1, kind: "win" };
const A_COURT: RungOutcome = { team: 0, kind: "court" };
const B_COURT: RungOutcome = { team: 1, kind: "court" };
const COURTS = { scoring: "courts" } as const;

/** Scenarios with assertions worked out by hand from the Rung design. */
export function rungAuthored(version: string): RungVector[] {
  const v = (
    id: string,
    title: string,
    rules: string[],
    body: {
      deal?: RungVector["deal"];
      setup?: RungSetup;
      bazi?: RungVectorBazi;
      moves?: RungVectorMove[];
      assert: RungVectorAssert;
    },
  ): RungVector => ({
    id,
    version,
    title,
    rules,
    ...(body.deal !== undefined ? { deal: body.deal } : {}),
    ...(body.setup !== undefined ? { setup: body.setup } : {}),
    ...(body.bazi !== undefined ? { bazi: body.bazi } : {}),
    moves: body.moves ?? [],
    assert: body.assert,
  });
  const bazi = (deals: RungOutcome[], extra: Partial<RungVectorBazi> = {}): RungVectorBazi => ({
    firstDealer: 3,
    deals,
    ...extra,
  });
  return [
    // The deal and the call
    v(
      "V-R001-deal-five-each",
      "five cards each from the caller, the rest in the stock",
      ["RG1", "RG2", "RG4", "RG5"],
      {
        deal: { seedHex: seed(7), dealerSeat: 3 },
        assert: {
          phase: "call",
          dealerSeat: 3,
          callerSeat: 0,
          turnSeat: 0,
          trump: null,
          handCounts: [5, 5, 5, 5],
          stockCount: 32,
          fullDeck: true,
          firstFiveFromDeck: true,
          views: [{ viewer: 0, stockCount: 32, seedHidden: true, hidesOthers: true }],
        },
      },
    ),
    v("V-R002-deal-caller-follows-dealer", "the caller is the seat after the dealer", ["RG4"], {
      deal: { seedHex: seed(9), dealerSeat: 1 },
      assert: { phase: "call", dealerSeat: 1, callerSeat: 2, turnSeat: 2 },
    }),
    v(
      "V-R003-call-completes-hands",
      "the call deals the stock and the caller leads",
      ["RG6", "RG8", "RG10"],
      {
        deal: { seedHex: seed(7), dealerSeat: 3, preset: "hokm" },
        moves: [{ seat: 0, move: call("D") }],
        assert: {
          phase: "play",
          trump: "D",
          turnSeat: 0,
          trickIndex: 0,
          handCounts: [13, 13, 13, 13],
          stockCount: 0,
          fullDeck: true,
          firstFiveFromDeck: true,
          lastEvents: ["trump_called", "hands_completed"],
        },
      },
    ),
    v("V-R004-call-only-the-caller", "only the caller may act, and only with a call", ["RG6"], {
      deal: { seedHex: seed(7), dealerSeat: 3, preset: "hokm" },
      moves: [
        refused(1, call("H"), "rules.not_your_turn"),
        refused(0, { type: "play_card", card: "AS" }, "rules.wrong_phase"),
      ],
      assert: { phase: "call", legal: { "0": CALLS, "1": [], "2": [], "3": [] } },
    }),
    v("V-R005-redeal-offered", "five cards without an honour may be dealt again", ["RG7"], {
      setup: calling(WEAK),
      assert: {
        legal: { "0": [...CALLS, REDEAL] },
        views: [
          { viewer: 0, canRedeal: true },
          { viewer: 1, canRedeal: false },
        ],
      },
    }),
    v("V-R006-redeal-refused-with-an-honour", "a jack among the five rules a redeal out", ["RG7"], {
      setup: calling(["2S", "5S", "7H", "9C", "JD"]),
      moves: [refused(0, REDEAL, "rules.illegal_move")],
      assert: { redeals: 0, legal: { "0": CALLS } },
    }),
    v("V-R007-redeal-deals-again", "a redeal gives everyone five new cards, same dealer", ["RG7"], {
      setup: calling(WEAK),
      moves: [{ seat: 0, move: REDEAL }],
      assert: {
        phase: "call",
        redeals: 1,
        dealerSeat: 3,
        callerSeat: 0,
        turnSeat: 0,
        handCounts: [5, 5, 5, 5],
        stockCount: 32,
        fullDeck: true,
        lastEvents: ["redealt"],
      },
    }),
    v(
      "V-R008-third-redeal-refused",
      "after two redeals in a row the hand must be called",
      ["RGE2", "RG7"],
      {
        setup: calling(WEAK, { redeals: 2 }),
        moves: [refused(0, REDEAL, "rules.illegal_move")],
        assert: { redeals: 2, legal: { "0": CALLS } },
      },
    ),
    v("V-R009-redeal-off", "with the option off a weak hand must be called", ["RG7"], {
      setup: calling(WEAK, { rules: { redeal: "off" } }),
      moves: [refused(0, REDEAL, "rules.illegal_move")],
      assert: { legal: { "0": CALLS } },
    }),
    v(
      "V-R010-auto-call-longest-then-ranks",
      "a silent caller gets the stronger of two long suits",
      ["RG9", "RGE1"],
      {
        setup: calling(["2H", "3H", "8C", "9C", "9D"]),
        assert: { autoPlay: { "0": call("C"), "1": null } },
      },
    ),
    v("V-R011-auto-call-suit-order", "equal suits fall back to spades first", ["RG9"], {
      setup: calling(["2H", "3H", "2S", "3S", "9D"]),
      assert: { autoPlay: { "0": call("S") } },
    }),

    // Tricks
    v("V-R020-must-follow-suit", "a player holding the led suit must follow it", ["RG11", "RG14"], {
      setup: pos([[], ["4H", "5C", "AS"], ["6H"], ["7H"]], {
        plays: [{ seat: 0, card: "2H" }],
      }),
      moves: [
        refused(1, { type: "play_card", card: "5C" }, "rules.must_follow_suit"),
        refused(1, { type: "play_card", card: "AS" }, "rules.must_follow_suit"),
      ],
      assert: { turnSeat: 1, legal: { "1": [{ type: "play_card", card: "4H" }] } },
    }),
    v("V-R021-void-plays-anything", "a void player may play any card, a rung included", ["RG11"], {
      setup: pos([[], ["5C", "AS", "3D"], ["6H"], ["7H"]], {
        plays: [{ seat: 0, card: "2H" }],
      }),
      assert: {
        legal: {
          "1": [
            { type: "play_card", card: "AS" },
            { type: "play_card", card: "5C" },
            { type: "play_card", card: "3D" },
          ],
        },
      },
    }),
    v(
      "V-R022-highest-of-led-suit",
      "without a rung the highest card of the led suit wins and leads",
      ["RG12", "RG13", "RG15"],
      {
        setup: pos(
          [
            ["9H", "2C"],
            ["KH", "3C"],
            ["AC", "4D"],
            ["TH", "5C"],
          ],
          { rules: SINGLE },
        ),
        moves: seq("0:9H 1:KH 2:AC 3:TH"),
        assert: {
          counts: [0, 1],
          pile: 0,
          turnSeat: 1,
          trickIndex: 1,
          lastEvents: ["card_played", "trick_won"],
        },
      },
    ),
    v("V-R023-rung-beats-ace", "the highest rung wins over the ace of the led suit", ["RG12"], {
      setup: pos(
        [
          ["AH", "2C"],
          ["2S", "3C"],
          ["KH", "4C"],
          ["5S", "5C"],
        ],
        { rules: SINGLE },
      ),
      moves: seq("0:AH 1:2S 2:KH 3:5S"),
      assert: {
        counts: [0, 1],
        turnSeat: 3,
        stats: { "1": { cuts: 1, tricksWon: 0 }, "3": { cuts: 1, tricksWon: 1 } },
      },
    }),
    v("V-R024-turn-order", "play passes to the next seat from the leader", ["RG3"], {
      setup: pos([["2H"], ["3H"], ["4H"], ["5H"]], { leader: 2 }),
      moves: seq("2:4H 3:5H"),
      assert: { turnSeat: 0 },
    }),

    // Double Sar
    v("V-R030-pile", "a won trick goes onto the pile and nobody owns it", ["RG16"], {
      setup: three("AKQ"),
      moves: seq(t1("A")),
      assert: { pile: 1, counts: [0, 0], lastWinner: 0, run: 1, turnSeat: 0 },
    }),
    v(
      "V-R031-two-in-a-row-take-the-pile",
      "two tricks in a row take everything in the centre",
      ["RG17"],
      {
        setup: three("AKQ", { trickIndex: 6, counts: [1, 2], pile: 3 }),
        moves: seq(`${t1("A")} ${t2("K")}`),
        assert: {
          counts: [6, 2],
          pile: 0,
          lastWinner: null,
          run: 0,
          turnSeat: 0,
          lastEvents: ["card_played", "trick_won", "pile_taken"],
          stats: { "0": { pilesTaken: 1, tricksWon: 2 } },
        },
      },
    ),
    v(
      "V-R032-partners-do-not-take",
      "one trick each by the two partners leaves the pile",
      ["RG18"],
      {
        setup: pos([
          ["AH", "2C", "6D"],
          ["2H", "3C", "5D"],
          ["3H", "AC", "KD"],
          ["4H", "4C", "7D"],
        ]),
        moves: seq("0:AH 1:2H 2:3H 3:4H 0:2C 1:3C 2:AC 3:4C"),
        assert: { counts: [0, 0], pile: 2, lastWinner: 2, run: 1 },
      },
    ),
    v(
      "V-R033-second-partner-needs-one-more",
      "the second partner wins again and takes all three",
      ["RG18", "RG17"],
      {
        setup: pos([
          ["AH", "2C", "6D"],
          ["2H", "3C", "5D"],
          ["3H", "AC", "KD"],
          ["4H", "4C", "7D"],
        ]),
        moves: seq("0:AH 1:2H 2:3H 3:4H 0:2C 1:3C 2:AC 3:4C 2:KD 3:7D 0:6D 1:5D"),
        assert: { counts: [3, 0], pile: 0 },
      },
    ),
    v("V-R034-run-starts-again", "after a take the next trick starts a new pile", ["RG19"], {
      setup: three("AKQ"),
      moves: seq(`${t1("A")} ${t2("K")} ${t3("Q")}`),
      assert: { counts: [2, 0], pile: 1, lastWinner: 0, run: 1 },
    }),
    v("V-R035-thirteenth-trick-takes-the-pile", "trick thirteen takes what is left", ["RG20"], {
      setup: last({ counts: [5, 4], pile: 3, lastWinner: 1, run: 1 }),
      moves: seq(HEARTS),
      assert: { counts: [9, 4], phase: "ended", outcome: { team: 0, kind: "win" } },
    }),
    v(
      "V-R036-twelve-and-thirteen-one-take",
      "winning tricks twelve and thirteen is one take",
      ["RGE3"],
      {
        setup: last({ counts: [5, 5], pile: 2, lastWinner: 0, run: 1 }),
        moves: seq(HEARTS),
        assert: {
          counts: [8, 5],
          outcome: { team: 0, kind: "win" },
          lastEvents: ["card_played", "trick_won", "pile_taken", "round_ended"],
        },
      },
    ),
    v(
      "V-R037-first-two-wait",
      "the winner of tricks one and two does not take the pile",
      ["RG21"],
      {
        setup: three("AKQ", { rules: { firstTwo: true } }),
        moves: seq(`${t1("A")} ${t2("K")}`),
        assert: { counts: [0, 0], pile: 2, run: 2 },
      },
    ),
    v("V-R038-first-two-then-three", "winning trick three as well takes all three", ["RG21"], {
      setup: three("AKQ", { rules: { firstTwo: true } }),
      moves: seq(`${t1("A")} ${t2("K")} ${t3("Q")}`),
      assert: { counts: [3, 0], pile: 0 },
    }),
    v("V-R039-two-aces-do-not-take", "two aces in a row leave the pile", ["RG22"], {
      setup: three("AAK", { rules: { aceRule: true } }),
      moves: seq(`${t1("A")} ${t2("A")}`),
      assert: { counts: [0, 0], pile: 2, run: 2 },
    }),
    v("V-R040-two-aces-then-a-king", "the next win after two aces takes the pile", ["RG22"], {
      setup: three("AAK", { rules: { aceRule: true } }),
      moves: seq(`${t1("A")} ${t2("A")} ${t3("K")}`),
      assert: { counts: [3, 0], pile: 0 },
    }),
    v("V-R041-ace-and-king-take", "an ace and another card take the pile as usual", ["RG22"], {
      setup: three("AKQ", { rules: { aceRule: true } }),
      moves: seq(`${t1("A")} ${t2("K")}`),
      assert: { counts: [2, 0], pile: 0 },
    }),
    v(
      "V-R042-aces-on-four-and-five",
      "aces on four and five, a king on six takes the pile",
      ["RGE6"],
      {
        setup: three("AAK", { rules: { aceRule: true }, trickIndex: 3, counts: [2, 1] }),
        moves: seq(`${t1("A")} ${t2("A")} ${t3("K")}`),
        assert: { counts: [5, 1], trickIndex: 6 },
      },
    ),
    v(
      "V-R043-first-two-with-aces",
      "first two and the ace rule together take on trick three",
      ["RGE7"],
      {
        setup: three("AAA", { rules: { aceRule: true, firstTwo: true } }),
        moves: seq(`${t1("A")} ${t2("A")} ${t3("A")}`),
        assert: { counts: [3, 0], pile: 0 },
      },
    ),
    v("V-R044-eleven-and-twelve-wait", "tricks eleven and twelve do not take the pile", ["RG23"], {
      setup: pos(
        [
          ["AH", "2C"],
          ["2H", "AC"],
          ["3H", "3C"],
          ["4H", "4C"],
        ],
        {
          rules: { lastTwo: true },
          trickIndex: 11,
          counts: [5, 4],
          pile: 2,
          lastWinner: 0,
          run: 1,
        },
      ),
      moves: seq(HEARTS),
      assert: { counts: [5, 4], pile: 3, phase: "play" },
    }),
    v(
      "V-R045-thirteen-decides",
      "after the wait trick thirteen decides the pile",
      ["RG23", "RG20"],
      {
        setup: pos(
          [
            ["AH", "2C"],
            ["2H", "AC"],
            ["3H", "3C"],
            ["4H", "4C"],
          ],
          {
            rules: { lastTwo: true },
            trickIndex: 11,
            counts: [5, 4],
            pile: 2,
            lastWinner: 0,
            run: 1,
          },
        ),
        moves: seq(`${HEARTS} ${CLUBS_TO_1}`),
        assert: { counts: [5, 8], outcome: { team: 1, kind: "win" } },
      },
    ),

    // How a deal ends
    v(
      "V-R050-single-sar-seven",
      "seven tricks against three win the deal at once",
      ["RG24", "RG25", "RG30", "RG32", "RG36"],
      {
        setup: ending({ rules: SINGLE, trickIndex: 9, counts: [6, 3] }),
        moves: [...seq(HEARTS), refused(0, { type: "play_card", card: "2C" }, "rules.round_over")],
        assert: {
          phase: "ended",
          turnSeat: null,
          counts: [7, 3],
          outcome: { team: 0, kind: "win" },
          results: {
            winnerTeam: 0,
            kind: "win",
            byCaller: true,
            dealPoints: 1,
            points: [1, 0, 1, 0],
          },
          nextDealer: 3,
        },
      },
    ),
    v("V-R051-double-sar-seven", "seven taken against one end the deal", ["RG25"], {
      setup: ending({ trickIndex: 7, counts: [5, 1], pile: 1, lastWinner: 0, run: 1 }),
      moves: seq(HEARTS),
      assert: { counts: [7, 1], outcome: { team: 0, kind: "win" } },
    }),
    v(
      "V-R052-single-sar-court",
      "the first seven tricks are a court when nobody may chase",
      ["RG31", "RG30", "RG32", "RG37"],
      {
        setup: ending({ rules: { ...SINGLE, chase: false }, trickIndex: 6, counts: [6, 0] }),
        moves: seq(HEARTS),
        assert: {
          outcome: { team: 0, kind: "court" },
          results: { kind: "court", byCaller: true, dealPoints: 3, points: [3, 0, 3, 0] },
          nextDealer: 1,
        },
      },
    ),
    v("V-R053-court-choice-opens", "at seven against zero the trick winner chooses", ["RG26"], {
      setup: ending({ rules: SINGLE, trickIndex: 6, counts: [6, 0] }),
      moves: [...seq(HEARTS), refused(2, PLAY_ON, "rules.not_your_turn")],
      assert: {
        phase: "courtChoice",
        turnSeat: 0,
        counts: [7, 0],
        legal: { "0": [STOP, PLAY_ON], "2": [] },
        autoPlay: { "0": STOP },
        eventsInclude: ["court_choice_opened"],
      },
    }),
    v(
      "V-R054-court-choice-stop",
      "stopping scores what is won; a later answer is refused",
      ["RG26", "RGE8"],
      {
        setup: ending({ rules: SINGLE, trickIndex: 6, counts: [6, 0] }),
        moves: [...seq(HEARTS), { seat: 0, move: STOP }, refused(0, PLAY_ON, "rules.round_over")],
        assert: {
          outcome: { team: 0, kind: "court" },
          lastEvents: ["court_choice_made", "round_ended"],
        },
      },
    ),
    v("V-R055-single-sar-failed-chase", "a failed chase keeps the court", ["RG27"], {
      setup: ending({ rules: SINGLE, trickIndex: 6, counts: [6, 0] }),
      moves: [...seq(HEARTS), { seat: 0, move: PLAY_ON }, ...seq(CLUBS_TO_1)],
      assert: { counts: [7, 1], chasing: 0, outcome: { team: 0, kind: "court" } },
    }),
    v("V-R056-single-sar-52-court", "all thirteen by playing on are a 52-court", ["RG31"], {
      setup: last({ rules: SINGLE, counts: [12, 0], chasing: 0 }),
      moves: seq(HEARTS),
      assert: { counts: [13, 0], outcome: { team: 0, kind: "court52" } },
    }),
    v(
      "V-R057-double-sar-choice-after-a-take",
      "seven by a take against zero opens the choice",
      ["RGE4", "RG26"],
      {
        setup: ending({ trickIndex: 6, counts: [5, 0], pile: 1, lastWinner: 0, run: 1 }),
        moves: [...seq(HEARTS), { seat: 0, move: STOP }],
        assert: {
          counts: [7, 0],
          pile: 0,
          outcome: { team: 0, kind: "win" },
          eventsInclude: ["pile_taken", "court_choice_opened"],
        },
      },
    ),
    v(
      "V-R058-double-sar-no-chase",
      "without the chase the deal ends at seven against zero",
      ["RGE5"],
      {
        setup: ending({
          rules: { chase: false },
          trickIndex: 6,
          counts: [5, 0],
          pile: 1,
          lastWinner: 0,
          run: 1,
        }),
        moves: seq(HEARTS),
        assert: { phase: "ended", outcome: { team: 0, kind: "win" } },
      },
    ),
    v(
      "V-R059-double-sar-failed-chase",
      "the chase ends when the other team takes a pile",
      ["RG27"],
      {
        setup: ending({ trickIndex: 7, counts: [7, 0], chasing: 0 }),
        moves: seq(`${CLUBS_TO_1} 1:AD 2:3D 3:4D 0:2D`),
        assert: { counts: [7, 2], outcome: { team: 0, kind: "win" } },
      },
    ),
    v("V-R060-double-sar-court", "all thirteen by playing on are a court", ["RG31", "RG32"], {
      setup: last({ counts: [11, 0], pile: 1, lastWinner: 0, run: 1, chasing: 0 }),
      moves: seq(HEARTS),
      assert: {
        counts: [13, 0],
        outcome: { team: 0, kind: "court" },
        results: { kind: "court", byCaller: true, dealPoints: 3 },
      },
    }),
    v(
      "V-R061-dealers-team-wins",
      "the dealer's team scores two and the old caller deals",
      ["RG30", "RG32", "RG35"],
      {
        setup: ending({ rules: SINGLE, trickIndex: 9, counts: [3, 6] }),
        moves: seq(CLUBS_TO_1),
        assert: {
          counts: [3, 7],
          outcome: { team: 1, kind: "win" },
          results: {
            winnerTeam: 1,
            byCaller: false,
            dealPoints: 2,
            points: [0, 2, 0, 2],
            finishOrder: [1, 3, 0, 2],
            loserSeat: null,
          },
          nextDealer: 0,
        },
      },
    ),

    // The bazi
    v(
      "V-R070-bazi-to-five",
      "points add up to the target and the dealer follows the results",
      ["RG32", "RG34", "RG35", "RG36", "RG37"],
      {
        bazi: bazi([A_WIN, A_COURT, B_WIN, A_WIN], { overrides: { target: 5 } }),
        assert: {
          series: {
            roundsPlayed: 4,
            teamPoints: [6, 2],
            points: [6, 2, 6, 2],
            dealsWon: [3, 1],
            finished: true,
            winnerTeam: 0,
            nextDealerSeat: 3,
          },
        },
      },
    ),
    v(
      "V-R071-bazi-not-yet",
      "four points of five do not end the bazi; the call passes after a court",
      ["RG34", "RG37"],
      {
        bazi: bazi([A_WIN, A_COURT], { overrides: { target: 5 } }),
        assert: {
          series: { teamPoints: [4, 0], finished: false, winnerTeam: null, nextDealerSeat: 1 },
        },
      },
    ),
    v("V-R072-bazi-52-court", "a 52-court wins the bazi outright", ["RG32"], {
      bazi: bazi([{ team: 0, kind: "court52" }], {
        preset: "courtPiece",
        overrides: { target: 10 },
      }),
      assert: { series: { teamPoints: [3, 0], finished: true, winnerTeam: 0 } },
    }),
    v(
      "V-R073-bazi-hokm-points",
      "Hokm scores one, two and three and the caller keeps the call",
      ["RG32", "RG35", "RG37"],
      {
        bazi: bazi([A_WIN, A_COURT, B_COURT], { preset: "hokm" }),
        assert: { series: { teamPoints: [3, 3], finished: false, nextDealerSeat: 0 } },
      },
    ),
    v("V-R074-courts-tally", "a court counts one and a goon court three", ["RG33"], {
      bazi: bazi([A_COURT, B_COURT, A_WIN], { overrides: COURTS }),
      assert: {
        series: {
          courts: [1, 3],
          points: [1, 3, 1, 3],
          teamPoints: [0, 0],
          finished: false,
          streakTeam: 0,
          streakDeals: 1,
        },
      },
    }),
    v("V-R075-goon-court-ten", "a goon court can be set to count ten", ["RG33"], {
      bazi: bazi([B_COURT], { overrides: { ...COURTS, goonCourt: 10 } }),
      assert: { series: { courts: [0, 10] } },
    }),
    v("V-R076-seven-in-a-row", "seven deals won in a row are a court", ["RG33"], {
      bazi: bazi([A_WIN, A_WIN, A_WIN, A_WIN, A_WIN, A_WIN, A_WIN], { overrides: COURTS }),
      assert: { series: { courts: [1, 0], streakDeals: 0, streakTeam: null } },
    }),
    v("V-R077-run-broken-by-the-other-team", "the other team's win starts its own run", ["RGE14"], {
      bazi: bazi([A_WIN, A_WIN, B_WIN], { overrides: COURTS }),
      assert: { series: { courts: [0, 0], streakTeam: 1, streakDeals: 1 } },
    }),
    v("V-R078-run-broken-by-a-court", "a court starts the run of seven again", ["RGE14"], {
      bazi: bazi([A_WIN, A_WIN, A_COURT, A_WIN], { overrides: COURTS }),
      assert: { series: { courts: [1, 0], streakTeam: 0, streakDeals: 1 } },
    }),

    // Timeouts
    v("V-R080-auto-follow-lowest", "a timed-out seat follows with its lowest card", ["RG42"], {
      setup: pos([[], ["KH", "4H", "2C"], ["6H"], ["7H"]], {
        plays: [{ seat: 0, card: "9H" }],
      }),
      assert: { autoPlay: { "1": { type: "play_card", card: "4H" } } },
    }),
    v(
      "V-R081-auto-void",
      "a void seat throws the lowest card of its longest plain suit",
      ["RG42"],
      {
        setup: pos([[], ["2S", "KC", "5C", "3D"], ["6H"], ["7H"]], {
          plays: [{ seat: 0, card: "9H" }],
        }),
        assert: { autoPlay: { "1": { type: "play_card", card: "5C" } } },
      },
    ),
    v("V-R082-auto-lead", "a timed-out leader keeps the rung and leads low from length", ["RG42"], {
      setup: pos([["AS", "KD", "3D", "2H"], ["3H"], ["4H"], ["5H"]]),
      assert: { autoPlay: { "0": { type: "play_card", card: "3D" }, "1": null } },
    }),

    // Views
    v(
      "V-R090-view-before-the-call",
      "a seat sees its five cards and nothing of the stock",
      ["RGE12", "RG5"],
      {
        setup: calling(WEAK, { stock: ["2D", "3D", "4D", "5D"] }),
        assert: {
          views: [
            {
              viewer: 0,
              hand: ["2S", "5S", "7H", "9C", "TD"],
              handCounts: [5, 5, 5, 5],
              stockCount: 4,
              seedHidden: true,
              hidesOthers: true,
            },
            { viewer: 2, hand: ["TH", "JH", "QH", "KH", "AH"], hidesOthers: true },
          ],
        },
      },
    ),
    v("V-R091-view-spectator", "a spectator sees no hand", ["RGE13"], {
      setup: ending(),
      assert: {
        views: [{ viewer: "spectator", hand: null, canRedeal: null, hidesOthers: true }],
      },
    }),
  ];
}
