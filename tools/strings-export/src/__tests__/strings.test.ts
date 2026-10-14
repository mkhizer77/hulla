import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import { problemsOf, unityTable, type StringsFile } from "../export";
import { format } from "../format";
import { argumentsOf, IcuError, parse } from "../icu";
import { toSmart } from "../smart";

const english = JSON.parse(
  readFileSync(
    join(import.meta.dirname, "..", "..", "..", "..", "content", "strings", "en.json"),
    "utf8",
  ),
) as StringsFile;

const cards = "{0, plural, one {# card} other {# cards}}";

describe("the ICU subset", () => {
  it("parses text, arguments, numbers, plurals and selects", () => {
    expect(argumentsOf(parse("Join {0}'s table ({1} open)"))).toEqual(["0", "1"]);
    expect(argumentsOf(parse(`{g, select, a {A} other {B}} and ${cards}`))).toEqual(["g", "0"]);
    expect(parse("{0, number, integer} coins")[0]).toEqual({
      type: "number",
      name: "0",
      style: "integer",
    });
    expect(parse("It''s '{literal}'")).toEqual([{ type: "text", value: "It's {literal}" }]);
  });

  it("refuses what the tables do not allow", () => {
    for (const bad of [
      "{0, plural, one {x}}",
      "{0, plural, =0 {none} other {x}}",
      "{0, date}",
      "{0, number, percent}",
      "{bad name}",
      "unclosed {0",
      "stray }",
      "{0, select, a {x} a {y} other {z}}",
    ]) {
      expect(() => parse(bad), bad).toThrow(IcuError);
    }
  });

  it("formats English as the client shows it", () => {
    expect(format(parse(cards), { 0: 1 })).toBe("1 card");
    expect(format(parse(cards), { 0: 3 })).toBe("3 cards");
    expect(format(parse("{0, number, integer} coins"), { 0: 12500 })).toBe("12,500 coins");
    expect(format(parse("{g, select, a {Ay} other {Oh}}"), { g: "a" })).toBe("Ay");
  });
});

describe("Smart Strings for the Unity tables", () => {
  it("turns plurals, selects and numbers into Smart Strings", () => {
    expect(toSmart(parse(cards), "en")).toEqual({
      text: "{0:plural:{} card|{} cards}",
      smart: true,
    });
    expect(toSmart(parse("{0, number, integer} coins"), "en").text).toBe("{0:N0} coins");
    expect(toSmart(parse("{g, select, a {A|1} b {B} other {C}}"), "en").text).toBe(
      "{g:choose(a|b):A\\|1|B|C}",
    );
    expect(toSmart(parse("Plain text"), "en")).toEqual({ text: "Plain text", smart: false });
    expect(toSmart(parse("'{x}' {0}"), "en").text).toBe("\\{x\\} {0}");
  });

  it("uses each locale's plural categories in CLDR order", () => {
    const message = parse("{0, plural, one {one} few {few} other {other}}");
    expect(toSmart(message, "ur").text).toBe("{0:plural:one|other}");
    expect(toSmart(message, "ar").text).toBe("{0:plural:other|one|other|few|other|other}");
  });
});

describe("content/strings", () => {
  it("English parses, and every key is well formed", () => {
    expect(problemsOf(english, null)).toEqual([]);
    expect(unityTable(english).entries.length).toBe(Object.keys(english.strings).length);
  });

  it("a translation must keep the English arguments and keys", () => {
    const ur: StringsFile = {
      locale: "ur",
      strings: { "ui.back": "واپس", "ui.join_friend_table": "{0} کی میز", "ui.nope": "x" },
    };
    const problems = problemsOf(ur, english);
    expect(problems.some((p) => p.includes("ui.join_friend_table"))).toBe(true);
    expect(problems.some((p) => p.includes("ui.nope is not an English key"))).toBe(true);
    expect(problems.some((p) => p.includes("ui.back"))).toBe(false);
  });
});

describe("content keys", () => {
  it("every name, description and title in content has English", async () => {
    const { contentKeyProblems } = await import("../check");
    expect(
      contentKeyProblems(join(import.meta.dirname, "..", "..", "..", "..", "content"), english),
    ).toEqual([]);
    const partial: StringsFile = { locale: "en", strings: { "ui.back": "Back" } };
    expect(
      contentKeyProblems(join(import.meta.dirname, "..", "..", "..", "..", "content"), partial)
        .length,
    ).toBeGreaterThan(50);
  });
});
