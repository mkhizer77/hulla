import { defineConfig } from "vitest/config";

/** Runs against a live Nakama (compose.yml); CI starts it in the server-smoke job. */
export default defineConfig({
  test: {
    name: "server-integration",
    testTimeout: 240_000,
    hookTimeout: 60_000,
    include: ["integration/**/*.test.ts"],
    fileParallelism: false,
  },
});
