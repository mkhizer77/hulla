import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    name: "test-vectors",
    include: ["runner-ts/**/__tests__/**/*.test.ts"],
  },
});
