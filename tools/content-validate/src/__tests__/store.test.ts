import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * The store listings in `store/` (#86, GDD 1.5, 25.2, 25.3): each field fits its store's limit and
 * no text uses gambling words, which would bring the simulated-gambling rating descriptor.
 */
const storeDir = join(import.meta.dirname, "..", "..", "..", "..", "store");

/** Character limits from Play Console and App Store Connect. */
const LIMITS: Record<string, Record<string, number>> = {
  android: { "title.txt": 30, "short_description.txt": 80, "full_description.txt": 4000 },
  ios: {
    "name.txt": 30,
    "subtitle.txt": 30,
    "keywords.txt": 100,
    "promotional_text.txt": 170,
    "description.txt": 4000,
  },
};

const GAMBLING =
  /\b(bet|bets|betting|wager\w*|stake|stakes|jackpot\w*|casino\w*|cash ?out|real[- ]money prizes?|win money|poker|chips|gambl\w*|odds|payout\w*|teen ?patti|rummy)\b/i;

function text(platform: string, locale: string, file: string): string {
  return readFileSync(join(storeDir, platform, locale, file), "utf8").replace(/\n$/, "");
}

describe("store listings", () => {
  for (const [platform, limits] of Object.entries(LIMITS)) {
    for (const locale of readdirSync(join(storeDir, platform))) {
      it(`${platform} ${locale}: every field is there and within its limit`, () => {
        for (const [file, limit] of Object.entries(limits)) {
          const value = text(platform, locale, file);
          expect(value.length, file).toBeGreaterThan(0);
          const characters = Array.from(new Intl.Segmenter().segment(value)).length;
          expect(characters, file).toBeLessThanOrEqual(limit);
        }
      });

      it(`${platform} ${locale}: no gambling words`, () => {
        for (const file of Object.keys(limits)) {
          // "No betting" is the one allowed use: it says what the game is not.
          const value = text(platform, locale, file).replace(/No betting/g, "");
          expect(GAMBLING.exec(value)?.[0], file).toBeUndefined();
        }
      });
    }
  }

  it("the title and the subtitle follow the naming in GDD 1.5", () => {
    expect(text("android", "en-US", "title.txt")).toBe("Hulla: Desi Games with Friends");
    expect(text("ios", "en-US", "name.txt")).toBe("Hulla: Desi Games with Friends");
  });
});
