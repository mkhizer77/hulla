// A table per VU: four seats on four sockets create, join by code, ready up, play rounds by
// following turn_start's legal cards, vote for a rematch, and one seat drops and returns with its
// resume token once per round (GDD 28.1, 28.5). Thresholds: p95 move round-trip and errors.
import { WebSocket } from "k6/experimental/websockets";
import { Counter, Trend } from "k6/metrics";
import { check, sleep } from "k6";

import { OPCODES } from "./lib/opcodes.js";
import {
  authenticate,
  dataMessage,
  decodeData,
  joinMessage,
  rpc,
  socketUrl,
} from "./lib/nakama.js";

export const options = {
  scenarios: {
    tables: {
      executor: "ramping-vus",
      startVUs: 1,
      stages: [
        { duration: __ENV.RAMP || "1m", target: Number(__ENV.TABLES || 2) },
        { duration: __ENV.HOLD || "2m", target: Number(__ENV.TABLES || 2) },
        { duration: "30s", target: 0 },
      ],
      gracefulStop: "60s",
    },
  },
  thresholds: {
    move_round_trip_ms: ["p(95)<250"],
    rounds_completed: ["count>0"],
    checks: ["rate>0.99"],
    http_req_failed: ["rate<0.01"],
  },
};

const moveRoundTrip = new Trend("move_round_trip_ms", true);
const roundsCompleted = new Counter("rounds_completed");
const reconnects = new Counter("reconnects");
const errors = new Counter("table_errors");
const SEATS = 4;
const ROUNDS = Number(__ENV.ROUNDS || 2);

function seatName(i) {
  return `k6-${__VU}-${i}`;
}

export default function () {
  const run = `${Date.now()}-${__VU}`;
  const sessions = [];
  for (let i = 0; i < SEATS; i += 1) {
    const session = authenticate(`k6-dev-${run}-${i}`, `k6_${run.slice(-8)}_${i}`);
    if (session === null) return;
    sessions.push(session);
  }
  const created = rpc(sessions[0], "table_create", { gameId: "thulla", mode: "private" });
  if (created === null || !created.ok) {
    errors.add(1);
    return;
  }
  const matchId = created.matchId;
  const tableId = created.tableId;

  const seats = sessions.map((session, i) => ({
    index: i,
    session,
    socket: null,
    seat: null,
    resumeToken: null,
    seq: 0,
    pendingMove: null,
    lastRoundId: null,
    done: false,
  }));
  let roundsDone = 0;
  let dropped = false;
  let finished = false;

  const send = (seat, type, opCode, payload) => {
    seat.seq += 1;
    seat.socket.send(
      dataMessage(matchId, opCode, { v: 1, type, tableId, seq: seat.seq, ts: Date.now(), payload }),
    );
  };

  const onEnvelope = (seat, envelope) => {
    if (envelope.type === "table_state") {
      const mine = envelope.payload.seats.find((s) => s.playerId === seat.session.user_id);
      if (mine) seat.seat = mine.seat;
      if (envelope.payload.resumeToken) seat.resumeToken = envelope.payload.resumeToken;
      if (seat.index !== 0 && envelope.payload.status === "open" && !seat.ready) {
        seat.ready = true;
        send(seat, "ready", OPCODES.ready, { ready: true });
      }
      if (seat.index === 0 && !seat.ready) {
        seat.ready = true;
        send(seat, "ready", OPCODES.ready, { ready: true });
      }
      if (seat.index === 0 && envelope.payload.status === "open") {
        const allReady = envelope.payload.seats
          .filter((s) => s.playerId !== null)
          .every((s) => s.ready);
        if (
          allReady &&
          envelope.payload.seats.filter((s) => s.playerId !== null).length === SEATS &&
          !seat.started
        ) {
          seat.started = true;
          send(seat, "start_round", OPCODES.start_round, {});
        }
      }
      return;
    }
    if (envelope.type === "turn_start" && envelope.payload.seat === seat.seat) {
      const legal = envelope.payload.legal;
      if (!legal || legal.length === 0) return;
      seat.pendingMove = { sentTs: Date.now(), clientSeq: seat.seq + 1 };
      send(seat, "play_card", OPCODES.play_card, {
        roundId: envelope.payload.roundId,
        clientSeq: seat.pendingMove.clientSeq,
        card: legal[0],
      });
      return;
    }
    if (
      envelope.type === "move_applied" &&
      envelope.payload.seat === seat.seat &&
      seat.pendingMove
    ) {
      moveRoundTrip.add(Date.now() - seat.pendingMove.sentTs);
      seat.pendingMove = null;
      return;
    }
    if (envelope.type === "round_end") {
      if (seat.index === 0) {
        roundsDone += 1;
        roundsCompleted.add(1);
        if (roundsDone < ROUNDS)
          send(seat, "vote_rematch", OPCODES.vote_rematch, { rematch: true });
        else finished = true;
      } else {
        send(seat, "vote_rematch", OPCODES.vote_rematch, { rematch: roundsDone < ROUNDS });
      }
      return;
    }
    if (envelope.type === "error") {
      errors.add(1);
    }
  };

  const connect = (seat, metadata) =>
    new Promise((resolve) => {
      const socket = new WebSocket(socketUrl(seat.session));
      seat.socket = socket;
      socket.addEventListener("open", () => {
        socket.send(joinMessage(seat.index + 1, matchId, metadata));
      });
      socket.addEventListener("message", (event) => {
        const message = JSON.parse(event.data);
        if (message.match) resolve(true);
        if (message.error) {
          errors.add(1);
          resolve(false);
        }
        if (message.match_data) onEnvelope(seat, decodeData(message.match_data.data));
      });
      socket.addEventListener("error", () => {
        errors.add(1);
        resolve(false);
      });
    });

  const main = async () => {
    for (const seat of seats) await connect(seat, { clientVersion: "0.1.0" });
    const started = Date.now();
    while (!finished && Date.now() - started < 240_000) {
      await new Promise((r) => setTimeout(r, 250));
      // Once per table: seat 3 drops mid-round and returns with its resume token (GDD 9.8).
      if (
        !dropped &&
        roundsDone === 0 &&
        seats[3].resumeToken !== null &&
        Date.now() - started > 8_000
      ) {
        dropped = true;
        seats[3].socket.close();
        await new Promise((r) => setTimeout(r, 2_000));
        await connect(seats[3], { resumeToken: seats[3].resumeToken, clientVersion: "0.1.0" });
        reconnects.add(1);
      }
    }
    check(finished, { "table finished its rounds": (f) => f });
    for (const seat of seats) seat.socket.close();
  };
  main();
  sleep(Number(__ENV.VU_SLEEP || 250));
}
