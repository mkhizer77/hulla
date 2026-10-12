/* Dependency rules from docs/ARCHITECTURE.md section 5. Run with `pnpm depcruise`. */
module.exports = {
  forbidden: [
    {
      name: "rules-engine-only-shared-ts",
      comment: "The rules engine may depend on shared-ts and nothing else in the repository.",
      severity: "error",
      from: { path: "^packages/rules-engine/src" },
      to: { path: "^(packages/(?!shared-ts/|rules-engine/)|server/|apps/|tools/)" },
    },
    {
      name: "shared-ts-depends-on-nothing",
      comment: "shared-ts is the bottom of the graph.",
      severity: "error",
      from: { path: "^packages/shared-ts/src" },
      to: { path: "^(packages/(?!shared-ts/)|server/|apps/|tools/)" },
    },
    {
      name: "engines-no-node-builtins",
      comment:
        "Engines and shared-ts run in the browser and in Nakama's JS runtime: no Node built-ins.",
      severity: "error",
      from: { path: "^packages/(rules-engine|shared-ts)/src", pathNot: "__tests__" },
      to: { dependencyTypes: ["core"] },
    },
    {
      name: "engines-no-runtime-packages",
      comment:
        "Engines and shared-ts ship inside the server bundle and the web client: no npm runtime dependencies.",
      severity: "error",
      from: { path: "^packages/(rules-engine|shared-ts)/src", pathNot: "__tests__" },
      to: {
        dependencyTypes: ["npm", "npm-dev", "npm-optional", "npm-peer"],
        pathNot: "^packages/",
      },
    },
    {
      name: "server-not-apps",
      severity: "error",
      from: { path: "^server/" },
      to: { path: "^apps/" },
    },
    {
      name: "web-not-server",
      comment: "The web client talks to the server over the protocol, never through its code.",
      severity: "error",
      from: { path: "^apps/web/" },
      to: { path: "^server/" },
    },
    {
      name: "no-circular",
      severity: "error",
      from: {},
      to: { circular: true },
    },
  ],
  options: {
    tsPreCompilationDeps: true,
    tsConfig: { fileName: "tsconfig.base.json" },
    doNotFollow: { path: "node_modules" },
    exclude: { path: "node_modules|/dist/|/coverage/" },
    enhancedResolveOptions: {
      exportsFields: ["exports"],
      conditionNames: ["import", "require", "node", "default", "types"],
      extensions: [".ts", ".js", ".mjs", ".cjs", ".json"],
    },
    reporterOptions: { text: { highlightFocused: true } },
  },
};
