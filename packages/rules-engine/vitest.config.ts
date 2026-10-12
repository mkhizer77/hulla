import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    name: "rules-engine",
    include: ["src/**/__tests__/**/*.test.ts"],
  },
});
