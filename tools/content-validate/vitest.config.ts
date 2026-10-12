import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    name: "content-validate",
    include: ["src/**/__tests__/**/*.test.ts"],
  },
});
