import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    projects: ["packages/*", "server/*", "tools/*"],
    coverage: {
      provider: "v8",
      reporter: ["text", "lcov"],
      include: ["packages/*/src/**", "server/*/src/**"],
      exclude: ["**/__tests__/**", "**/generated/**"],
      thresholds: {
        "packages/shared-ts/src/**": { lines: 95, branches: 90 },
        "packages/rules-engine/src/**": { lines: 95, branches: 90 },
        "packages/rules-engine/src/**": { lines: 95, branches: 90 },
        "server/**/src/**": { lines: 80 },
      },
    },
  },
});
