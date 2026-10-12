import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    name: "test-vectors",
    // Property tests replay whole rounds; the runner in CI is slower than a laptop.
    testTimeout: 60_000,
    include: ["runner-ts/**/__tests__/**/*.test.ts"],
  },
});
