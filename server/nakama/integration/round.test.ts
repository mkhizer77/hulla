import { Client } from "@heroiclabs/nakama-js";
import { OPCODES, type TableState, type TurnStart } from "@hulla/protocol/generated";
import { createThullaRound, fairDealCommitment, resolveRules } from "@hulla/rules-engine";
import { hexToBytes, stateFromSeed, Xoshiro128StarStar } from "@hulla/shared-ts";
import { afterAll, describe, expect, it } from "vitest";

import {
  NAKAMA_HOST,
  NAKAMA_PORT,
  SERVER_KEY,
  rpc,
  scriptedClient,
  setTableId,
  type ScriptedClient,
} from "./nakama-client";

interface Created {
  readonly ok: boolean;
  readonly matchId: string;
  readonly code: string;
  readonly tableId: string;
}

const SEATS = 4;

describe.skipIf(NAKAMA_HOST === undefined)(
  "a scripted four-seat Thulla round against Nakama",
  () => {
    const clients: ScriptedClient[] = [];
    let live: ScriptedClient[] = [];
    const tail = (c: ScriptedClient, n = 14): string =>
      c.received
        .slice(-n)
        .map((e) => {
          const p = e.payload as {
            seat?: number;
            clientSeq?: number;
            legal?: string[];
            auto?: boolean;
            status?: string;
          };
          const bits = [
            p.seat === undefined ? "" : `s${String(p.seat)}`,
            p.clientSeq === undefined ? "" : `c${String(p.clientSeq)}`,
            p.legal === undefined ? "" : `L${String(p.legal.length)}`,
            p.auto === true ? "auto" : "",
            p.status ?? "",
          ].filter((b) => b !== "");
          return `${String(e.seq)}:${e.type}${bits.length > 0 ? `(${bits.join(",")})` : ""}`;
        })
        .join(" ");
    const dump = (): string =>
      live.map((c) => `${c.name}[${String(c.received.length)}]: ${tail(c)}`).join("\n");
    afterAll(() => {
      for (const c of clients) c.close();
    });

    it("plays to round_end with the legal mask, an absent seat under auto-play and no hand leaking", async () => {
      const suffix = String(Date.now());
      for (let i = 0; i < SEATS; i += 1)
        clients.push(await scriptedClient(`p${String(i)}`, suffix));
      const [host] = clients;
      if (host === undefined) throw new Error("no host");

      const created = await rpc<Created>(host, "table_create", {
        gameId: "thulla",
        mode: "private",
      });
      expect(created).toMatchObject({ ok: true });
      setTableId(created.tableId);
      const matchId = created.matchId;

      // The table RPCs: the code resolves to the match, a wrong code does not, presets come from
      // content, and voice waits until the caller is at the table.
      const byCode = await rpc<{ ok: boolean; matchId: string }>(
        clients[1] ?? host,
        "table_join_by_code",
        {
          code: created.code.toLowerCase(),
        },
      );
      expect(byCode).toMatchObject({ ok: true, matchId });
      expect(
        await rpc<{ ok: boolean }>(host, "table_join_by_code", { code: "ZZZZZZ" }),
      ).toMatchObject({
        ok: false,
        error: { code: "table.not_found" },
      });
      const presets = await rpc<{ ok: boolean; presets: { id: string }[] }>(
        host,
        "presets_get",
        {},
      );
      expect(presets.presets.map((p) => p.id)).toEqual([
        "standard",
        "family",
        "hardcore",
        "classicPagat",
      ]);
      expect(await rpc<{ ok: boolean }>(host, "voice_token", { matchId })).toMatchObject({
        ok: false,
        error: { code: "voice.not_at_table" },
      });

      for (const c of clients) await c.socket.joinMatch(matchId);
      const seatOf = new Map<string, number>();
      for (const c of clients) {
        const state = (await c.next((e) => e.type === "table_state")).payload as TableState;
        expect(state.code).toBe(created.code);
        const own = state.seats.find((s) => s.playerId === c.session.user_id);
        if (own === undefined) throw new Error(`${c.name} has no seat`);
        seatOf.set(c.name, own.seat);
      }
      // At the table each seat gets a room token the compose LiveKit accepts (ADR-0024).
      const voice = await rpc<{ ok: boolean; url: string; channel: string; token: string }>(
        host,
        "voice_token",
        { matchId },
      );
      expect(voice).toMatchObject({ ok: true, provider: "livekit", account: host.session.user_id });
      const validate = await fetch(
        `${voice.url.replace(/^ws/, "http")}/rtc/validate?access_token=${voice.token}`,
      );
      expect(validate.status).toBe(200);
      for (const c of clients)
        await c.send(matchId, "ready", OPCODES.ready.opcode, { ready: true });
      await host.next(
        (e) =>
          e.type === "table_state" &&
          (e.payload as TableState).seats.filter((s) => s.playerId !== null).every((s) => s.ready),
      );
      await host.send(matchId, "start_round", OPCODES.start_round.opcode, {});

      const starts = await Promise.all(clients.map((c) => c.next((e) => e.type === "round_start")));
      const roundId = (starts[0]?.payload as { roundId: string }).roundId;
      const commitment = (starts[0]?.payload as { commitment: string }).commitment;

      // A friend of the host arrives mid-round to watch: a spectator's view never carries a hand.
      const watcher = await scriptedClient("spec", suffix);
      clients.push(watcher);
      const hostProfile = await rpc<{ profile?: { friendCode: string } }>(host, "profile_get", {});
      await rpc<object>(watcher, "friend_add_by_code", {
        friendCode: hostProfile.profile?.friendCode ?? "",
      });
      await new Client(SERVER_KEY, NAKAMA_HOST ?? "", NAKAMA_PORT, false).addFriends(host.session, [
        watcher.session.user_id ?? "",
      ]);
      await watcher.socket.joinMatch(matchId, undefined, { asSpectator: "true" });
      const watching = (await watcher.next((e) => e.type === "table_state")).payload as TableState;
      expect(watching.spectators.map((s) => s.userId)).toEqual([watcher.session.user_id]);
      expect(watching.round).not.toBeNull();

      // Seat 3 drops off the socket right after the deal; the policy plays it until it returns.
      const absent = clients[3];
      if (absent === undefined) throw new Error("no fourth client");
      absent.socket.disconnect(false);
      const autoPlayed = await host.next(
        (e) =>
          e.type === "move_applied" &&
          (e.payload as { seat: number; auto?: boolean }).seat === seatOf.get("p3") &&
          (e.payload as { auto?: boolean }).auto === true,
        40_000,
      );
      expect(autoPlayed.type).toBe("move_applied");
      const returned = await scriptedClient("p3", suffix);
      clients.push(returned);
      await returned.socket.joinMatch(matchId);
      await returned.next((e) => e.type === "table_state");
      live = [clients[0], clients[1], clients[2], returned].filter(
        (c): c is ScriptedClient => c !== undefined,
      );
      const players = live;

      const play = async (c: ScriptedClient): Promise<void> => {
        let handled = 0;
        for (let guard = 0; guard < 200; guard += 1) {
          const next = await c
            .next(
              (e) =>
                e.type === "round_end" ||
                (e.type === "turn_start" &&
                  (e.payload as TurnStart).roundId === roundId &&
                  !(e as { handled?: boolean }).handled),
              60_000,
            )
            .catch(() => null);
          if (next === null) throw new Error(`${c.name} stalled waiting for a turn\n${dump()}`);
          if (next.type === "round_end") return;
          (next as { handled?: boolean }).handled = true;
          const legal = (next.payload as TurnStart).legal;
          if (legal.length === 0) continue;
          handled += 1;
          await c.send(matchId, "play_card", OPCODES.play_card.opcode, {
            roundId,
            clientSeq: 100 + handled,
            card: legal[0],
          });
        }
      };
      const ends = Promise.all(players.map((c) => c.next((e) => e.type === "round_end", 200_000)));
      await Promise.all(players.map(play));
      const endPayloads = (await ends).map(
        (e) =>
          e.payload as {
            fairness: { commitment: string; seed: string; deckOrder: string[] };
            bhabhiSeat: number;
          },
      );
      const end = endPayloads[0];
      if (end === undefined) throw new Error("no round_end");
      expect(end.fairness.commitment).toBe(commitment);
      expect(
        fairDealCommitment(hexToBytes(end.fairness.seed), end.fairness.deckOrder as never),
      ).toBe(commitment);

      // The deal replays from the revealed seed: the deck order is what the engine shuffles for it.
      const rules = resolveRules("private");
      if (!rules.ok) throw new Error(rules.error.code);
      const seed = hexToBytes(end.fairness.seed);
      const dealerSeat = (starts[0]?.payload as { dealerSeat: number }).dealerSeat;
      const replay = createThullaRound(
        { rules: rules.value, seatCount: SEATS, dealerSeat, seed },
        Xoshiro128StarStar.fromState(stateFromSeed(seed)),
      );
      if (!replay.ok) throw new Error(replay.error.code);
      expect(replay.value.deckOrder).toEqual(end.fairness.deckOrder);
      expect(replay.value.commitment).toBe(commitment);

      // The stats card moved for every player.
      for (const c of players) {
        const own = await rpc<{ ok: boolean; profile?: { stats: { rounds: number } } }>(
          c,
          "profile_get",
          {},
        );
        expect(own.profile?.stats.rounds).toBe(1);
        // The first live round pays the one-time welcome reward with it (GDD 17.2, OQ-042).
        const progress = await rpc<{ lastReward?: { welcomeCoins?: number } }>(
          c,
          "progress_get",
          {},
        );
        expect(progress.lastReward?.welcomeCoins).toBe(250);
      }

      // Recent Players: everyone remembers the other three (GDD 10.6).
      for (const c of players) {
        const overview = await rpc<{ ok: boolean; recent: { userId: string }[] }>(
          c,
          "social_overview",
          {},
        );
        expect(overview.recent.map((r) => r.userId).sort()).toEqual(
          players
            .filter((o) => o !== c)
            .map((o) => o.session.user_id)
            .sort(),
        );
      }

      // The spectator saw the whole round and never a hand (GDD 7.7).
      expect(watcher.received.some((e) => e.type === "round_end")).toBe(true);
      for (const e of watcher.received) {
        const payload = e.payload as {
          round?: { seats?: { hand: unknown }[] } | null;
          seats?: { hand: unknown }[];
        };
        const seatsWithHands = [...(payload.round?.seats ?? []), ...(payload.seats ?? [])];
        for (const seat of seatsWithHands) expect(seat.hand ?? null).toBeNull();
        expect(e.type).not.toBe("turn_start");
        expect(e.type).not.toBe("private_draw");
      }

      // Rewards landed: XP and coins per seat from the GDD tables, through the ledger (GDD 12.2, 12.3).
      for (const c of players) {
        const progress = await rpc<{
          ok: boolean;
          xp: number;
          lastReward: { roundId: string; coins: number; xp: number; welcomeCoins?: number } | null;
        }>(c, "progress_get", { tzOffsetMinutes: 300 });
        expect(progress.lastReward).toMatchObject({ roundId });
        expect(progress.xp).toBeGreaterThanOrEqual(10);
        const wallet = await rpc<{
          ok: boolean;
          balances: { coins: number };
          ledger: { sourceId: string }[];
        }>(c, "wallet_get", {});
        // The round's coins plus the one-time welcome reward of a first round.
        expect(wallet.balances.coins).toBe(
          (progress.lastReward?.coins ?? 0) + (progress.lastReward?.welcomeCoins ?? 0),
        );
        expect(wallet.ledger.map((l) => l.sourceId)).toContain(
          `${created.tableId}:${roundId}:round`,
        );
      }

      // Missions moved with the round (GDD 12.5): the weekly round count shows one.
      const missions = await rpc<{ ok: boolean; weekly: { id: string; progress: number }[] }>(
        host,
        "missions_get",
        { tzOffsetMinutes: 300 },
      );
      expect(missions.weekly.find((w) => w.id === "w_rounds_15")?.progress).toBe(1);

      // Baithak points reached the weekly board (GDD 12.8): the first escape holds three or more.
      const board = await rpc<{
        ok: boolean;
        rows: { userId: string; score: number }[];
        me: { score: number } | null;
      }>(host, "leaderboard_get", { board: "weekly", scope: "friends" });
      expect(board.ok).toBe(true);
      const roundEnd = host.received.find((e) => e.type === "round_end")?.payload as
        { escapeOrder: number[] } | undefined;
      const firstSeat = roundEnd?.escapeOrder[0];
      const firstId = players.find((c) => seatOf.get(c.name) === firstSeat)?.session.user_id;
      const firstBoard = await rpc<{ me: { score: number } | null }>(
        players.find((c) => c.session.user_id === firstId) ?? host,
        "leaderboard_get",
        // The friends view lists the caller whatever the global rank; the local board holds many runs.
        { board: "weekly", scope: "friends" },
      );
      expect(firstBoard.me?.score ?? 0).toBeGreaterThanOrEqual(3);

      // The round replays from its log with the same final hash (ARCHITECTURE 8.11).
      const replayUrl = `http://${NAKAMA_HOST ?? ""}:${NAKAMA_PORT}/v2/rpc/admin_round_replay?http_key=local-only-http-key&unwrap`;
      const replayed = (await (
        await fetch(replayUrl, {
          method: "POST",
          body: JSON.stringify({ tableId: created.tableId, roundId }),
        })
      ).json()) as { ok: boolean; matches: boolean; moves: number };
      expect(replayed).toMatchObject({ ok: true, matches: true });
      expect(replayed.moves).toBeGreaterThan(0);

      // Redaction at the socket: no client ever saw another seat's hand or legal mask.
      for (const c of players) {
        const mine = seatOf.get(c.name);
        for (const e of c.received) {
          if (e.type === "table_state") {
            const round = (e.payload as TableState).round;
            if (round === null) continue;
            round.seats.forEach((s, index) => {
              if (index !== mine) expect(s.hand).toBeNull();
            });
          }
          if (e.type === "turn_start") expect((e.payload as TurnStart).seat).toBe(mine);
        }
        const seqs = c.received
          .filter((e) => OPCODES[e.type as keyof typeof OPCODES].visibility === "public")
          .map((e) => e.seq);
        for (let i = 1; i < seqs.length; i += 1) expect(seqs[i]).toBe((seqs[i - 1] ?? 0) + 1);
      }
    });
  },
);
