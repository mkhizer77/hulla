import * as fc from "fast-check";
import { describe, expect, it } from "vitest";

import { all, andThen, err, isErr, isOk, map, mapErr, ok, unwrapOr } from "../result";

describe("Result", () => {
  it("ok and err are distinguishable", () => {
    expect(isOk(ok(1))).toBe(true);
    expect(isErr(ok(1))).toBe(false);
    expect(isErr(err("boom"))).toBe(true);
    expect(isOk(err("boom"))).toBe(false);
  });

  it("map and andThen only touch the ok branch", () => {
    expect(map(ok(2), (n) => n * 3)).toEqual(ok(6));
    expect(map(err("e"), (n: number) => n * 3)).toEqual(err("e"));
    expect(andThen(ok(2), (n) => ok(n + 1))).toEqual(ok(3));
    expect(andThen(ok(2), () => err("later"))).toEqual(err("later"));
    expect(andThen(err("first"), () => ok(1))).toEqual(err("first"));
  });

  it("mapErr only touches the error branch", () => {
    expect(mapErr(err("e"), (e) => e.toUpperCase())).toEqual(err("E"));
    expect(mapErr(ok(1), (e: string) => e.toUpperCase())).toEqual(ok(1));
  });

  it("unwrapOr falls back on error", () => {
    expect(unwrapOr(ok(5), 0)).toBe(5);
    expect(unwrapOr(err("e"), 0)).toBe(0);
  });

  it("all collects values and stops at the first error", () => {
    expect(all([ok(1), ok(2)])).toEqual(ok([1, 2]));
    expect(all([ok(1), err("a"), err("b")])).toEqual(err("a"));
    expect(all([])).toEqual(ok([]));
  });

  it("map obeys the functor laws", () => {
    fc.assert(
      fc.property(fc.integer(), fc.integer(), (n, k) => {
        const value = ok(n);
        expect(map(value, (x) => x)).toEqual(value);
        const f = (x: number): number => x + k;
        const g = (x: number): number => x * 2;
        expect(map(map(value, f), g)).toEqual(map(value, (x) => g(f(x))));
      }),
    );
  });
});
