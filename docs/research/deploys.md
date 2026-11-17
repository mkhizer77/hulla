# Deploys

One row per event, newest first (`docs/runbooks`).

| Date | Version | Drain took | Rollback | Notes |
|---|---|---|---|---|
| 17 Nov 2026 | `7fc18f3` (from `3aa540b`) | None needed: `admin_drain` reported 0 live tables | Not exercised; the previous `box.env` is kept as `/opt/hulla/box.env.prev` | Phase 0 box. Backup run first (`hulla-backup.service`). `NAKAMA_IMAGE` set in `box.env`, then `setup.sh`; Nakama healthy in about 13 s, no errors in its log. Checked from outside with one new guest: the daily reward pays once and a second claim changes nothing, the wallet shows the ledger line, `table_create` answers. Brings the one-transaction ledger write and the bot-table rewards (PR 334). |
| 16 Nov 2026 | `b18f8d7` (from `f94ccca`) | None needed: `admin_drain` reported 0 live tables | Not exercised; the previous `box.env` is kept as `/opt/hulla/box.env.prev` | Phase 0 box. Backup run first (`hulla-backup.service`). `NAKAMA_IMAGE` set in `box.env`, then `setup.sh`; Nakama healthy in about 15 s. Checked from outside with two test clients: `hello`, `table_create`, both join over the socket, and `change_game` answers from the new code. Brings the one-table match module, `change_game`, and the voice changes of 9 Nov. |
