import eslint from "@eslint/js";
import { defineConfig } from "eslint/config";
import prettier from "eslint-config-prettier";
import { flatConfigs as importX } from "eslint-plugin-import-x";
import * as tseslint from "typescript-eslint";

const nodeBuiltins = [
  "assert",
  "buffer",
  "child_process",
  "crypto",
  "events",
  "fs",
  "http",
  "https",
  "net",
  "os",
  "path",
  "process",
  "stream",
  "timers",
  "url",
  "util",
  "worker_threads",
  "zlib",
];

export default defineConfig([
  {
    ignores: [
      "**/node_modules/**",
      "**/dist/**",
      "**/coverage/**",
      "**/generated/**",
      "**/build/**",
      "apps/unity/**",
      "**/*.cjs",
    ],
  },
  eslint.configs.recommended,
  tseslint.configs.strictTypeChecked,
  tseslint.configs.stylisticTypeChecked,
  importX.recommended,
  importX.typescript,
  {
    languageOptions: {
      parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname },
    },
    settings: { "import-x/resolver": { typescript: true } },
    rules: {
      "no-console": "error",
      "import-x/no-default-export": "error",
      "import-x/order": [
        "error",
        {
          groups: ["builtin", "external", "internal", "parent", "sibling", "index", "type"],
          "newlines-between": "always",
          alphabetize: { order: "asc", caseInsensitive: true },
        },
      ],
      "@typescript-eslint/consistent-type-imports": ["error", { prefer: "type-imports" }],
      "@typescript-eslint/explicit-module-boundary-types": "error",
      "@typescript-eslint/restrict-template-expressions": ["error", { allowNumber: true }],
    },
  },
  {
    // Engines and shared code run in the browser and inside Nakama's JS runtime (CODING_RULES 3.3).
    files: ["packages/rules-engine/src/**", "packages/shared-ts/src/**"],
    ignores: ["**/__tests__/**"],
    rules: {
      "no-restricted-globals": [
        "error",
        { name: "Date", message: "Engines have no clock (ADR-0020)." },
        { name: "setTimeout", message: "No timers in engines." },
        { name: "setInterval", message: "No timers in engines." },
        { name: "console", message: "No console in engines." },
        { name: "process", message: "Node globals are not available in Nakama or browsers." },
        { name: "Buffer", message: "Node globals are not available in Nakama or browsers." },
        {
          name: "TextEncoder",
          message: "Not available in Nakama's runtime; use utf8Encode from shared-ts.",
        },
        { name: "TextDecoder", message: "Not available in Nakama's runtime." },
        {
          name: "crypto",
          message: "No crypto API in Nakama's runtime; use sha256 from shared-ts.",
        },
        { name: "fetch", message: "No IO in engines." },
      ],
      "no-restricted-properties": [
        "error",
        {
          object: "Math",
          property: "random",
          message: "Randomness comes from the injected Rng (ADR-0009).",
        },
      ],
      "no-restricted-imports": [
        "error",
        {
          paths: nodeBuiltins.map((name) => ({
            name,
            message: "No Node built-ins in engines or shared-ts.",
          })),
          patterns: [{ group: ["node:*"], message: "No Node built-ins in engines or shared-ts." }],
        },
      ],
    },
  },
  {
    // Generators and checks that run from the shell may print.
    files: ["tools/**", "**/scripts/**"],
    rules: { "no-console": "off" },
  },
  {
    files: ["**/__tests__/**"],
    rules: {
      "@typescript-eslint/no-non-null-assertion": "off",
      "@typescript-eslint/no-unsafe-assignment": "off",
      "@typescript-eslint/no-unnecessary-type-parameters": "off",
    },
  },
  {
    files: ["**/*.config.{js,ts}", "eslint.config.js", "**/*.mjs"],
    extends: [tseslint.configs.disableTypeChecked],
    rules: { "import-x/no-default-export": "off" },
  },
  prettier,
]);
