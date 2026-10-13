import type { Batch, TableState } from "./types";
import type { AnalyticsEventName, AnalyticsEvents } from "@hulla/protocol/generated";

/**
 * Turns what a table callback sent into analytics events (GDD 23.3): the public game messages
 * carry everything the taxonomy asks for, so this stays a pure mapping with no second source of
 * truth. Names, chat text and hands never appear; cards become counts.
 */
export interface TableEvent<E extends AnalyticsEventName = AnalyticsEventName> {
  readonly name: E;
  readonly params: AnalyticsEvents[E];
  readonly seat: number | null;
}

type Payload = Record<string, unknown>;

const num = (value: unknown): number | undefined => (typeof value === "number" ? value : undefined);
const str = (value: unknown): string => (typeof value === "string" ? value : "");

/** The engine's draw sources in the taxonomy's spelling. */
function drawSource(value: unknown): "discard" | "next_player" | "opponent" {
  if (value === "opponent") return "opponent";
  if (value === "nextPlayer" || value === "next_player") return "next_player";
  return "discard";
}
const count = (value: unknown): number => (Array.isArray(value) ? value.length : 0);

/** How long the seat took since its last turn_start, from the table log. */
function decisionMs<S, M>(state: TableState<S, M>, seat: number, now: number): number {
  for (let i = state.log.length - 1; i >= 0 && i >= state.log.length - 200; i -= 1) {
    const entry = state.log[i];
    if (entry === undefined) continue;
    if (entry.type === "turn_start" && entry.seat === seat) return Math.max(0, now - entry.ts);
  }
  return 0;
}

function legalCount<S, M>(state: TableState<S, M>, seat: number): number {
  for (let i = state.log.length - 1; i >= 0 && i >= state.log.length - 200; i -= 1) {
    const entry = state.log[i];
    if (entry === undefined) continue;
    if (entry.type === "turn_start" && entry.seat === seat) {
      const legal = (entry.payload as Payload)["legal"];
      return count(legal) + ((entry.payload as Payload)["canTakeHand"] === true ? 1 : 0);
    }
  }
  return 0;
}

export function analyticsFromBatch<S, M>(
  state: TableState<S, M>,
  batch: Batch,
  now: number,
): TableEvent[] {
  const out: TableEvent[] = [];
  const push = <E extends AnalyticsEventName>(
    name: E,
    params: AnalyticsEvents[E],
    seat: number | null,
  ): void => {
    out.push({ name, params, seat });
  };
  for (const o of batch.outbound) {
    const type = o.envelope.type;
    const p = o.envelope.payload as Payload;
    switch (type) {
      case "round_start": {
        if (o.to.kind !== "all") break;
        const seats = num(p["seatCount"]) ?? 0;
        push(
          "round_start",
          {
            mode: state.mode,
            seats,
            humans: seats,
            bots: 0,
            preset_hash: state.rules.preset ?? "standard",
          },
          null,
        );
        break;
      }
      case "move_applied": {
        const seat = num(p["seat"]);
        if (seat === undefined) break;
        const auto = p["auto"] === true;
        push(
          "move_played",
          {
            legal_count: legalCount(state, seat),
            decision_ms: auto ? 0 : decisionMs(state, seat, now),
            timeout: auto,
            auto,
          },
          seat,
        );
        break;
      }
      case "trick_resolved": {
        const giver = num(p["thullaBy"]);
        const picker = num(p["picker"]);
        if (giver === undefined || picker === undefined) break;
        push(
          "thulla",
          { pile_size: count(p["cards"]), giver, picker, trick_index: num(p["trickIndex"]) ?? 0 },
          giver,
        );
        break;
      }
      case "pickup":
        push("pickup", { cards: count(p["cards"]) }, num(p["seat"]) ?? null);
        break;
      case "hand_taken":
        push(
          "hand_taken",
          {
            cards: num(p["cardCount"]) ?? 0,
            players_active: state.seats.filter((s) => s.playerId !== null && !s.left).length,
          },
          num(p["taker"]) ?? null,
        );
        break;
      case "private_draw": {
        const source = drawSource(p["source"]);
        if (source === "discard") push("empty_hand_draw", { source }, num(p["seat"]) ?? null);
        else push("endgame_draw", { source }, num(p["seat"]) ?? null);
        break;
      }
      case "confirmed":
        push(
          "confirmed",
          {
            cards_remaining: num(p["cardsRemaining"]) ?? 0,
            fast_forwarded: p["fastForward"] === true,
          },
          num(p["bhabhiSeat"]) ?? null,
        );
        break;
      case "escape":
        push("escape", { rank: num(p["rank"]) ?? 0 }, num(p["seat"]) ?? null);
        break;
      case "round_end": {
        const started = state.round?.startedTs ?? now;
        push(
          "round_end",
          { duration: Math.max(0, now - started) / 1000, bhabhi_cards: num(p["bhabhiCards"]) ?? 0 },
          null,
        );
        break;
      }
      case "quick_chat_sent":
        push(
          "quick_chat",
          {
            phrase_id: str(p["phraseId"]),
            pack: typeof p["pack"] === "string" ? p["pack"] : "v1",
            context: state.phase === "inRound" ? "round" : "lobby",
          },
          num(p["seat"]) ?? null,
        );
        break;
      case "emote_sent":
        push(
          "emote",
          {
            phrase_id: str(p["emoteId"]),
            context: state.phase === "inRound" ? "round" : "lobby",
            target: typeof p["targetSeat"] === "number",
          },
          num(p["seat"]) ?? null,
        );
        break;
      case "presence": {
        const status = p["status"];
        const seat = num(p["seat"]);
        if (status === "disconnected") {
          push("disconnect", { outcome: "dropped" }, seat ?? null);
        } else if (status === "left" && seat !== undefined) {
          push("disconnect", { outcome: "left" }, seat);
        } else if (status === "reconnected") {
          const slot = seat === undefined ? undefined : state.seats[seat];
          const since = slot?.joinedTs ?? now;
          push(
            "reconnect",
            { outcome: "rejoined", duration: Math.max(0, now - since) / 1000 },
            seat ?? null,
          );
        }
        break;
      }
      default:
        break;
    }
  }
  return out;
}

// Keeps the generic names of the taxonomy visible to readers of this file.
export type { AnalyticsEventName };
