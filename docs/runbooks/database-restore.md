# Database backup and restore

Point-in-time recovery for Postgres and a rehearsed restore (GDD 20.13, ARCHITECTURE 15, #90).

## Backups
- Managed Postgres (phase 1): daily snapshots and continuous WAL archiving with the provider's point-in-time recovery, retention 7 days at least; the provider's console or CLI restores to a timestamp.
- The compose stack (phase 0): `pg_basebackup` nightly plus WAL archiving to object storage. In `server/nakama/compose.yml` the `postgres` service gets `-c wal_level=replica -c archive_mode=on -c archive_command='cp %p /archive/%f'` with a volume for `/archive`; a cron on the box runs `pg_basebackup -D /backups/$(date +%F) -Ft -z -X stream` and ships both folders to the bucket with `rclone`.

## Restore to a point in time
1. Note the target time (the moment before the bad deploy or the deletion).
2. Stop Nakama (drain first if players are on: `docs/runbooks/deploy-and-rollback.md`).
3. Restore the newest base backup from before the target into an empty data directory, add `recovery.signal` and `restore_command = 'cp /archive/%f %p'` with `recovery_target_time = '<target>'`, start Postgres, wait for recovery to finish, promote.
4. Run `nakama migrate up` against it, start Nakama, check `hello`, then the scripted round.
5. Players who played between the target and now lost those rounds and their rewards; the ledger's idempotent keys mean a replayed reward never pays twice, and `admin_grant` compensates by hand where a support ticket shows a loss.

## Drill record
Every drill is timed from "stop" to "scripted round passes".

| Date | Where | Backup size | Restore took | Rounds lost | Notes |
|---|---|---|---|---|---|
| pending | phase 0 box | | | | The first drill is a condition of the beta gate (GDD 28.5). |
