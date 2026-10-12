# Context (read this first)

**Hulla** is a mobile platform for the games South Asian friends play in person, rebuilt for playing together from anywhere. Game 1: Thulla (Bhabhi). Game 2: Rung. Then Ludo, Tambola, Antakshari and a catalogue (GDD §1.7). Unity 6 mobile first; a TypeScript web client later; one authoritative Nakama server.

## Decisions in one line each
- Rules: Punjab standard with the owner's two-player endgame (GDD §6, D1).
- Money: cosmetics, season pass, Remove Ads, rewarded ads. No wagering, no currency transfers, no loot boxes (D2).
- Markets: all; English UI first, fully localisation-ready; Urdu, Hindi, Bengali after launch (D3).
- Brand: Hulla; Thulla is the first game's name (D4). Store title "Hulla: Desi Games with Friends".
- Platform: multi-game shell with game modules on server and clients (D5, ARCHITECTURE §7).
- Priorities: friends first, random players optional, bots and offline play last and optional (D7).
- Unity: 6000.4.9f1 (stay on the installed version), UI Toolkit + Advanced Text Generator + runtime data binding, hand-wired composition root, `Awaitable`, plain C# events. No DI container (ADR-0007, 0008, 0016).
- Server: self-hosted Nakama, TypeScript runtime modules, Postgres, Docker Compose (ADR-0002).
- Engines: TypeScript authoritative; the C# mirror is deferred past v1.0 (ADR-0019); shared vectors; every rule ID tested (ADR-0003); no clock in engines (ADR-0020); one deck and 3–6 seats until v1.2.
- Protocol: JSON Schema draft-07 source, generated TS and C# (ADR-0005, ADR-0021). Match log is event-sourced (ADR-0006). Deterministic engines with a fair-deal commitment (ADR-0009).
- Voice: Agora on Unity and web behind an interface (ADR-0012).
- Chat: Nakama channels for table and club chat; quick chat and emotes as match data; our own filters (ADR-0018).
- Hosting: free EU tier for development, paid small boxes with real users, South Asia region for players (ADR-0013, TECH_STACK §6).
- Repo: public GitHub, PolyForm Noncommercial 1.0.0, tests gate merges, tag-triggered Android builds, iOS later (ADR-0014, 0015).

## Standing rules
Short, plainly worded issues and pull requests. No tool or vendor attribution in commits or docs. Decisions live in files, not chats: update `docs/STATUS.md` at the end of every work session and add gaps to `docs/OPEN_QUESTIONS.md`.

## Read next
The GDD is private and never committed; it lives at `docs/GDD_Hulla.md` in the owner's checkout, git-ignored. Public documents cite its section numbers and rule IDs.

`docs/STATUS.md` → `CONTRIBUTING.md` → `CODING_RULES.md` → `docs/ARCHITECTURE.md` → `docs/TECH_STACK.md` → `docs/PROJECT_STRUCTURE.md` → the GDD section for the task.
