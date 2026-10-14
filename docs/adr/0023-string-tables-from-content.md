# ADR-0023: String tables come from content and ship as a text asset

Date: 14 October 2026. Status: accepted.

## Context
GDD §26.1 asks for every user-facing string in tables, Smart Strings for plurals and gender, a pseudo-localisation check, locale-aware numbers and an export the web client can read. TECH_STACK named the Unity Localization package's string tables. Those tables are Addressables assets: adding them puts an Addressables content build into every player build and the release workflow, and the tables would become a second, binary-ish copy of the text that only the Unity editor can write. The project already keeps content in `content/` as the single source and copies it into the client (ContentSync), and the web client needs the same text.

## Decision
- `content/strings/<locale>.json` is the only source: stable keys, ICU MessageFormat limited to simple arguments, `{n, number}`, `plural` and `select` (CODING_RULES §9).
- `tools/strings-export` (`pnpm codegen`) checks every locale (syntax, CLDR plural categories, the English arguments kept) and writes two copies: `apps/unity/Hulla/Assets/_Project/Localization/Resources/Strings/strings.<locale>.json` with each message as a Unity Smart String, and `packages/strings/generated/<locale>.json` in ICU for the web. CI fails when either copy drifts.
- The client's `Strings` class loads the locale's table from Resources and formats with the Localization package's Smart Strings engine in the locale's culture. English fills any key a locale lacks. No Addressables string tables.
- `pnpm content:validate` also checks that every `name`, `description` and `title` key a content file points at has English.

## Consequences
- Plurals, selects and grouped numbers format the same way in Unity (Smart Strings) and on the web (ICU), from one file.
- Player builds need no Addressables step for text; a table is a small text asset in the build (about 60 KB for English).
- Translators work on JSON with notes per key instead of Unity assets; a translation platform can sit on the same files later.
- The Localization package stays for Smart Strings and its plural rules; its string table assets, locale selectors and UI Toolkit bindings are not used. Views set text from the text classes, which now read keys.
- Supersedes the string-table line in TECH_STACK §2 and the "Unity Localization tables" wording in ARCHITECTURE §12.3 and PROJECT_STRUCTURE.
