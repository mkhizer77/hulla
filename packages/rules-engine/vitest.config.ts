import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    name: "rules-engine",
    // Property tests replay whole rounds; the runner in CI is slower than a laptop.
    testTimeout: 60_000,
    include: ["src/**/__tests__/**/*.test.ts"],
  },
});
