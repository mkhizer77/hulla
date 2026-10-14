import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    name: "strings-export",
    include: ["src/**/__tests__/**/*.test.ts"],
  },
});
