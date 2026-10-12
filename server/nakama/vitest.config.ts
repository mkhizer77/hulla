import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    name: "server",
    testTimeout: 60_000,
    include: ["src/**/__tests__/**/*.test.ts"],
  },
});
