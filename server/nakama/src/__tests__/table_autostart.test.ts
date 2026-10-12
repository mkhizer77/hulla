import { describe, expect, it } from "vitest";

import { tableCore } from "../match/table_core";

import { broadcasts, harness, payloadOf, presence, sentTo, types } from "./table-helpers";
import { toyHost } from "./toy-game";

import type { ToyView } from "./toy-game";
import type { TableStateFor } from "../match/view";

const T0 = 1_760_200_000_000;

function matchmade(expected: number, parties: { id: string; members: string[] }[] = []) {
  const h = harness({ mode: "public" });
  const core = tableCore(toyHost(), {
    ids: { uuid: () => "00000000-0000-4000-8000-000000000001" },
    persist: { roundRecord: () => undefined },
  });
  const state = core.create(
    { code: "QUICK1", gameId: "thulla", mode: "public", autoStart: { expected }, parties },
    T0,
  );
  // reuse the harness plumbing against this state
  (h as { state: typeof state }).state = state;
  const join = (i: number) => {
    const p = presence(i);
    core.joinAttempt(state, p, {}, h.now);
    return core.join(state, [p], h.now);
  };
  const tick = (ms: number) => {
    h.now += ms;
    return core.tick(state, h.now);
  };
  return { core, state, h, join, tick };
}

describe("auto-start for matchmade public tables (GDD 9.5)", () => {
  it("counts down five seconds once everyone expected is seated, then deals without a ready step", () => {
    const t = matchmade(4);
    for (let i = 0; i < 3; i += 1) t.join(i);
    expect(t.state.autoStart?.startsAtTs).toBeNull();
    const last = t.join(3);
    expect(t.state.autoStart?.startsAtTs).toBe(t.h.now + 5_000);
    const view = payloadOf<TableStateFor<ToyView>>(sentTo(last, "s3")[0]);
    expect(view.autoStart).toEqual({ startsAtTs: t.h.now + 5_000 });
    expect(sentTo(last, "s0").map((e) => e.type)).toEqual(["table_state"]);
    expect(types(t.tick(4_900))).toEqual([]);
    const dealt = t.tick(100);
    expect(t.state.phase).toBe("inRound");
    expect(t.state.autoStart).toBeNull();
    expect(broadcasts(dealt).map((e) => e.type)).toContain("round_start");
    expect(t.state.round?.seatCount).toBe(4);
  });

  it("starts at the join deadline with three seated and dissolves with fewer", () => {
    const three = matchmade(4);
    for (let i = 0; i < 3; i += 1) three.join(i);
    expect(types(three.tick(19_900))).toEqual([]);
    const armed = three.tick(100);
    expect(three.state.autoStart?.startsAtTs).toBe(three.h.now + 5_000);
    expect(types(armed).filter((x) => x === "table_state")).toHaveLength(3);
    three.tick(5_000);
    expect(three.state.phase).toBe("inRound");
    expect(three.state.round?.seatCount).toBe(3);

    const two = matchmade(4);
    two.join(0);
    two.join(1);
    const closed = two.tick(20_000);
    expect(broadcasts(closed).at(-1)).toMatchObject({
      type: "table_closed",
      payload: { reason: "dissolved" },
    });
    expect(closed.end).toBe(true);
  });

  it("flags the seats of a party that queued together", () => {
    const t = matchmade(4, [{ id: "party-a", members: ["u1", "u2"] }]);
    for (let i = 0; i < 4; i += 1) t.join(i);
    expect(t.state.seats.slice(0, 4).map((s) => s.party)).toEqual([
      null,
      "party-a",
      "party-a",
      null,
    ]);
    const view = payloadOf<TableStateFor<ToyView>>(sentTo(t.join(0), "s0")[0]);
    expect(view.seats.map((s) => s.party)).toEqual([null, "party-a", "party-a", null]);
  });

  it("validates only the join fields of the metadata, so ticket properties ride along", () => {
    const t = matchmade(3);
    const decision = t.core.joinAttempt(
      t.state,
      presence(7),
      { game: "thulla", lane: "x", asSpectator: "false" },
      t.h.now,
    );
    expect(decision).toEqual({ accept: true });
  });
});
