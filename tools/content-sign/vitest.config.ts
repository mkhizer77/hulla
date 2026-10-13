import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    name: "content-sign",
    include: ["src/**/__tests__/**/*.test.ts"],
  },
});
