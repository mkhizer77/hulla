import { asBotMemory, legalMoves } from "@hulla/rules-engine";
import { describe, expect, it } from "vitest";

import { thullaHost } from "../games/thulla/host";
import { botThinkMs } from "../match/bots";
import { tableCore } from "../match/table_core";
import { DEFAULT_TABLE_CONFIG } from "../match/types";

import type { Batch, Envelope, TableState } from "../match/types";
import type { ThullaMove, ThullaState } from "@hulla/rules-engine";

const T0 = 1_760_200_000_000;
/** The bots take the first three seats; the player sits next (match/bots.ts). */
const PLAYER = 3;

interface BotTable {
  readonly state: TableState<ThullaState, ThullaMove>;
  readonly sent: Batch[];
  now: number;
  send(type: string, payload: object): Batch;
  tick(advanceMs: number): Batch;
}

function botTable(): BotTable {
  let uuids = 0;
  const core = tableCore(thullaHost, {
    ids: {
      uuid: () => {
        uuids += 1;
        return `0000000${String(uuids % 10)}-0000-4000-8000-${String(uuids).padStart(12, "0")}`;
      },
    },
    persist: { roundRecord: () => undefined },
  });
  const state = core.create(
    {
      code: "ABCDEF",
      gameId: "thulla",
      mode: "public",
      bots: 3,
      autoStart: { expected: 4 },
      parties: [],
      rules: { gameId: "thulla" },
    },
    T0,
  );
  const sent: Batch[] = [];
  const t: BotTable = {
    state,
    sent,
    now: T0,
    send(type, payload) {
      const envelope: Envelope = { v: 1, type, tableId: state.tableId, seq: 1, ts: t.now, payload };
      const batch = core.inbound(state, "s0", JSON.stringify(envelope), t.now, type);
      sent.push(batch);
      return batch;
    },
    tick(advanceMs) {
      t.now += advanceMs;
      const batch = core.tick(state, t.now);
      sent.push(batch);
      return batch;
    },
  };
  const player = { sessionId: "s0", userId: "u0", username: "Player" };
  core.joinAttempt(state, player, {}, t.now);
  sent.push(core.join(state, [player], t.now));
  t.tick(DEFAULT_TABLE_CONFIG.autoStartCountdownMs);
  return t;
}

function types(batches: readonly Batch[]): string[] {
  return batches.flatMap((batch) => batch.outbound.map((o) => o.envelope.type));
}

/** The player plays their first legal card whenever it is their turn; time passes otherwise. */
function step(t: BotTable, clientSeq: number): number {
  const round = t.state.round;
  if (round === null) return clientSeq;
  const card = legalMoves(round.state, PLAYER).find(
    (m): m is Extract<ThullaMove, { type: "play_card" }> => m.type === "play_card",
  );
  if (card === undefined) {
    t.tick(250);
    return clientSeq;
  }
  t.send("play_card", { roundId: round.roundId, clientSeq: clientSeq + 1, card: card.card });
  return clientSeq + 1;
}

describe("Thulla bots at the table (GDD 8)", () => {
  it("play a whole round with the bot policy and keep the table's memory as plain data", () => {
    const t = botTable();
    expect(t.state.phase).toBe("inRound");
    expect(asBotMemory(t.state.round?.botMemory)).not.toBeNull();
    let clientSeq = 0;
    let botMoves = 0;
    for (let i = 0; i < 2_000 && t.state.phase === "inRound"; i += 1) {
      clientSeq = step(t, clientSeq);
      const round = t.state.round;
      botMoves = round?.moves.filter((m) => m.seat !== PLAYER && m.auto).length ?? botMoves;
      if (round !== null && round.moves.length === 20) {
        const memory = asBotMemory(JSON.parse(JSON.stringify(round.botMemory)));
        expect(memory?.seatCount).toBe(4);
        expect(memory?.trickIndex).toBeGreaterThan(0);
      }
    }
    expect(t.state.phase).not.toBe("inRound");
    expect(botMoves).toBeGreaterThan(10);
  });

  it("a bot asked for its hand says yes after a pause, and the player takes it (R33)", () => {
    const t = botTable();
    let clientSeq = 0;
    for (let i = 0; i < 2_000 && t.state.phase === "inRound"; i += 1) {
      const round = t.state.round;
      if (round !== null && legalMoves(round.state, PLAYER).some((m) => m.type === "take_hand")) {
        break;
      }
      clientSeq = step(t, clientSeq);
    }
    const round = t.state.round;
    if (round === null) throw new Error("no round");
    const asked = t.send("take_hand", { roundId: round.roundId, clientSeq: clientSeq + 1 });
    expect(types([asked])).toContain("hand_asked");
    const target = round.handAsk?.target;
    expect(t.state.seats[target ?? -1]?.bot).toBe(true);
    const quick = t.tick(200);
    expect(types([quick])).not.toContain("hand_answered");
    const later = [t.tick(DEFAULT_TABLE_CONFIG.botPlayDelayMs * 2)];
    expect(types(later)).toEqual(expect.arrayContaining(["hand_answered", "hand_taken"]));
    expect(t.state.round?.state.seats[target ?? -1]?.escaped).toBe(true);
  });

  it("think for a varied, human-looking time, and quickly when only one card is legal", () => {
    const base = DEFAULT_TABLE_CONFIG.botPlayDelayMs;
    const pauses = Array.from({ length: 40 }, (_, i) => botThinkMs(base, `r_1:${String(i)}:0`, 5));
    for (const ms of pauses) {
      expect(ms).toBeGreaterThanOrEqual(base * 0.6);
      expect(ms).toBeLessThanOrEqual(base * 1.2 + 320);
    }
    expect(new Set(pauses).size).toBeGreaterThan(20);
    expect(botThinkMs(base, "r_1:3:0", 5)).toBe(botThinkMs(base, "r_1:3:0", 5));
    expect(botThinkMs(base, "r_1:3:0", 1)).toBe(base / 2);
  });
});
