import { NO_SEAT, bitCount, highBit, ledSuit, lowBit, type CardId, type Sim } from "./bot-sim";

/*
 * The rule policy (Rung design 6.3): what a sound player does without thinking far ahead. It is
 * the play-out policy for all four seats inside the bot's search, the tie-break between cards the
 * search values alike, and the fallback when the budget runs out. It reads the acting seat's own
 * hand, the open trick, the counts and which cards are still out; the cards still out are public
 * (everything not played and not held), so it never uses where the search placed them.
 */

function held(sim: Sim, seat: number, suit: number): number {
  return sim.hands[seat * 4 + suit] ?? 0;
}

/** The cards of `suit` the other three seats hold between them: public as a set. */
function out(sim: Sim, seat: number, suit: number): number {
  let mask = 0;
  for (let s = 0; s < 4; s += 1) {
    if (s !== seat) mask |= held(sim, s, suit);
  }
  return mask;
}

/** Whether the seat's top card of the suit beats every card of it still out. */
function holdsBoss(mine: number, others: number): boolean {
  return mine !== 0 && (others === 0 || highBit(mine) > highBit(others));
}

/** The bits of `mask` above `rank`. */
function above(mask: number, rank: number): number {
  return mask & ~((2 << rank) - 1);
}

function card(suit: number, rank: number): CardId {
  return suit * 13 + rank;
}

/** The lowest card outside the rung, from the longest suit on a tie; the lowest rung if that is all. */
function discard(sim: Sim, seat: number): CardId {
  let best = -1;
  let bestRank = 99;
  let bestLength = -1;
  for (let suit = 0; suit < 4; suit += 1) {
    if (suit === sim.trump) continue;
    const mask = held(sim, seat, suit);
    if (mask === 0) continue;
    const rank = lowBit(mask);
    const length = bitCount(mask);
    if (rank < bestRank || (rank === bestRank && length > bestLength)) {
      best = card(suit, rank);
      bestRank = rank;
      bestLength = length;
    }
  }
  return best >= 0 ? best : card(sim.trump, lowBit(held(sim, seat, sim.trump)));
}

function lead(sim: Sim, seat: number): CardId {
  const trump = sim.trump;
  const rungs = held(sim, seat, trump);
  const rungsOut = out(sim, seat, trump);
  const bossRung = holdsBoss(rungs, rungsOut);
  // A boss card outside the rung, from the suit with the most cards out (least likely to be cut).
  let bossSide = -1;
  let bossSideOut = -1;
  for (let suit = 0; suit < 4; suit += 1) {
    if (suit === trump) continue;
    const mine = held(sim, seat, suit);
    const others = out(sim, seat, suit);
    if (!holdsBoss(mine, others)) continue;
    const outCount = bitCount(others);
    if (outCount > bossSideOut) {
      bossSide = card(suit, highBit(mine));
      bossSideOut = outCount;
    }
  }
  const runAlive = !sim.single && sim.lastWinner === seat;
  if (runAlive) {
    // RG17: this trick is worth the pile, so lead the surest winner held.
    if (bossRung) return card(trump, highBit(rungs));
    if (bossSide >= 0 && (rungsOut === 0 || bossSideOut >= 3)) return bossSide;
  }
  // Draw rungs from strength: the boss rung while opponents may still hold some.
  if (bossRung && rungsOut !== 0 && bitCount(rungs) >= 3) return card(trump, highBit(rungs));
  if (bossSide >= 0) return bossSide;
  // Otherwise the lowest card of the longest suit outside the rung.
  let longest = -1;
  let length = 0;
  for (let suit = 0; suit < 4; suit += 1) {
    if (suit === trump) continue;
    const n = bitCount(held(sim, seat, suit));
    if (n > length) {
      longest = suit;
      length = n;
    }
  }
  if (longest >= 0) return card(longest, lowBit(held(sim, seat, longest)));
  return card(trump, bossRung ? highBit(rungs) : lowBit(rungs));
}

function follow(sim: Sim, seat: number, led: number, mine: number): CardId {
  const position = sim.trickLen;
  const partnerWinning = sim.winSeat % 2 === seat % 2;
  const winSuit = Math.floor(sim.winCard / 13);
  const winRank = sim.winCard % 13;
  const low = card(led, lowBit(mine));
  // A card of the led suit beats the winner only when the winner is of that suit too.
  const beating = winSuit === led ? above(mine, winRank) : 0;
  const others = out(sim, seat, led);
  const boss = holdsBoss(mine, others);
  if (partnerWinning) {
    if (position === 3) return low;
    // Partner's card already tops what is out: leave it, and their run, alone (RG18).
    const partnerSafe = winSuit === led && (others === 0 || winRank > highBit(others));
    if (partnerSafe || winSuit !== led) return low;
    return boss && beating !== 0 ? card(led, highBit(mine)) : low;
  }
  if (beating === 0) return low;
  if (position === 3) return card(led, lowBit(beating));
  if (boss) return card(led, highBit(mine));
  if (position === 2) return card(led, highBit(beating));
  // Second to play: low, unless an opponent's run is alive and the pile is theirs to take.
  const theirRun = !sim.single && sim.lastWinner !== NO_SEAT && sim.lastWinner % 2 !== seat % 2;
  return theirRun ? card(led, lowBit(beating)) : low;
}

function offSuit(sim: Sim, seat: number, led: number): CardId {
  const trump = sim.trump;
  const rungs = held(sim, seat, trump);
  const partnerWinning = sim.winSeat % 2 === seat % 2;
  if (partnerWinning || rungs === 0) return discard(sim, seat);
  const winSuit = Math.floor(sim.winCard / 13);
  if (winSuit === trump && led !== trump) {
    // An opponent cut: go over them if possible, otherwise keep the rungs.
    const over = above(rungs, sim.winCard % 13);
    return over === 0 ? discard(sim, seat) : card(trump, lowBit(over));
  }
  return card(trump, lowBit(rungs));
}

/** The policy's card for the seat to act. The seat must hold a card. */
export function policyCard(sim: Sim): CardId {
  const seat = sim.turn;
  const led = ledSuit(sim);
  if (led < 0) return lead(sim, seat);
  const mine = held(sim, seat, led);
  return mine !== 0 ? follow(sim, seat, led, mine) : offSuit(sim, seat, led);
}
