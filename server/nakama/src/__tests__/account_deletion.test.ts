import { describe, expect, it } from "vitest";

import {
  cancelDeletion,
  GRACE_PERIOD_MS,
  purgeDue,
  requestDeletion,
  TOMBSTONE,
  type DeletionRequest,
  type DeletionStore,
  type PurgePorts,
} from "../services/account_deletion";

const T0 = 1_760_200_000_000;

function memoryStore(): DeletionStore & { readonly requests: Map<string, DeletionRequest> } {
  const requests = new Map<string, DeletionRequest>();
  return {
    requests,
    read: (u) => requests.get(u) ?? null,
    write: (r) => void requests.set(r.userId, r),
    remove: (u) => void requests.delete(u),
    listDue: (now) => [...requests.values()].filter((r) => r.deleteAtTs <= now),
  };
}

function ports(): PurgePorts & {
  readonly log: string[];
  readonly rounds: Map<string, (string | null)[]>;
} {
  const log: string[] = [];
  const rounds = new Map<string, (string | null)[]>([
    ["t_A:r_1", ["u1", "u2", "u3"]],
    ["t_B:r_1", ["u2", "u1", null]],
  ]);
  return {
    log,
    rounds,
    roundKeys: (u) => [...rounds.entries()].filter(([, p]) => p.includes(u)).map(([k]) => k),
    tombstone(key, u) {
      rounds.set(
        key,
        (rounds.get(key) ?? []).map((p) => (p === u ? TOMBSTONE : p)),
      );
      log.push(`tombstone ${key}`);
    },
    freeFriendCode: (u) => void log.push(`free ${u}`),
    deleteAccount(u) {
      if (u === "u9") throw new Error("boom");
      log.push(`delete ${u}`);
    },
  };
}

describe("account deletion with a grace period (GDD 10.2)", () => {
  it("schedules deletion 14 days out, is idempotent, and can be cancelled", () => {
    const store = memoryStore();
    expect(requestDeletion(store, null, T0)).toMatchObject({
      ok: false,
      error: { code: "table.not_authorised" },
    });
    expect(requestDeletion(store, "u1", T0)).toEqual({
      ok: true,
      deleteAtTs: T0 + GRACE_PERIOD_MS,
      alreadyPending: false,
    });
    expect(requestDeletion(store, "u1", T0 + 5)).toEqual({
      ok: true,
      deleteAtTs: T0 + GRACE_PERIOD_MS,
      alreadyPending: true,
    });
    expect(cancelDeletion(store, "u1")).toEqual({ ok: true, cancelled: true });
    expect(cancelDeletion(store, "u1")).toEqual({ ok: true, cancelled: false });
    expect(store.requests.size).toBe(0);
  });

  it("purges only accounts past their grace, tombstones their rounds, and keeps going after a failure", () => {
    const store = memoryStore();
    requestDeletion(store, "u1", T0);
    requestDeletion(store, "u2", T0 + 60_000);
    requestDeletion(store, "u9", T0);
    const p = ports();
    const early = purgeDue(store, p, T0 + GRACE_PERIOD_MS - 1);
    expect(early).toEqual({ purged: [], failed: [], due: 0 });
    const report = purgeDue(store, p, T0 + GRACE_PERIOD_MS);
    expect(report.purged).toEqual(["u1"]);
    expect(report.failed).toEqual([{ userId: "u9", error: "Error: boom" }]);
    expect(report.due).toBe(2);
    expect(p.rounds.get("t_A:r_1")).toEqual([TOMBSTONE, "u2", "u3"]);
    expect(p.rounds.get("t_B:r_1")).toEqual(["u2", TOMBSTONE, null]);
    expect(p.log).toEqual([
      "tombstone t_A:r_1",
      "tombstone t_B:r_1",
      "free u1",
      "delete u1",
      "free u9",
    ]);
    expect(store.read("u1")).toBeNull();
    expect(store.read("u2")).not.toBeNull();
    expect(store.read("u9")).not.toBeNull();
  });
});
