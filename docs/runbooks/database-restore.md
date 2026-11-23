# Database backup and restore

Backups for Postgres and a rehearsed restore (GDD 20.13, ARCHITECTURE 15, #90).

## Backups
- Managed Postgres (phase 1): daily snapshots and continuous WAL archiving with the provider's point-in-time recovery, retention 7 days at least; the provider's console or CLI restores to a timestamp.
- The phase 0 box: `hulla-backup` (`infra/phase0/backup.sh`) runs `pg_dump -Fc` nightly at 02:30 UTC and streams the dump to `gs://hulla-phase0-backups/postgres/`, kept 14 days. There is no WAL archiving on the box, so there is no point-in-time recovery: a restore goes back to the newest dump. Run `sudo hulla-backup` before a risky change so the marker is fresh. The daily disk snapshot (`hulla-daily`) is the second copy.

## Restore to a point in time (managed Postgres, phase 1)
1. Note the target time (the moment before the bad deploy or the deletion).
2. Stop Nakama (drain first if players are on: `docs/runbooks/deploy-and-rollback.md`).
3. Restore to the target with the provider's point-in-time recovery into a new instance and point Nakama at it.
4. Run `nakama migrate up` against it, start Nakama, check `hello`, then the scripted round.
5. Players who played between the target and now lost those rounds and their rewards; the ledger's idempotent keys mean a replayed reward never pays twice, and `admin_grant` compensates by hand where a support ticket shows a loss.

## Restore a dump on the phase 0 box
Not yet run on the box itself; the same commands were rehearsed off the box (below).
1. Pick the dump: `gcloud storage ls -l gs://hulla-phase0-backups/postgres/`. Everything after its timestamp is lost.
2. Drain, then stop Nakama: `cd /opt/hulla && sudo docker compose stop nakama`.
3. Empty the database: `sudo docker compose exec -T postgres psql -U postgres -c 'DROP DATABASE nakama WITH (FORCE)' -c 'CREATE DATABASE nakama'`.
4. Restore: `gcloud storage cat gs://hulla-phase0-backups/postgres/<dump> | sudo docker compose exec -T postgres pg_restore -U postgres -d nakama --no-owner --exit-on-error`.
5. `sudo docker compose start nakama`; its entrypoint runs `migrate up`. Check `/healthcheck` and `hello`, then the scripted round.
6. Step 5 of the section above applies to the rounds played since the dump.

## Rehearse a restore off the box
Needs only read access to the bucket and Docker; nothing on the box changes. The dump holds real accounts: keep it in a private folder and delete it afterwards.
1. `gcloud storage cp gs://hulla-phase0-backups/postgres/<dump> <folder>/`.
2. `docker network create drill`, then `docker run -d --name drill-pg --network drill --network-alias postgres -e POSTGRES_DB=nakama -e POSTGRES_PASSWORD=drill -v <folder>:/dump:ro postgres:16-alpine`. No published ports, so it cannot clash with the development stack.
3. `time docker exec drill-pg pg_restore -U postgres -d nakama --no-owner --exit-on-error /dump/<dump>`.
4. Check: 20 tables in `public`, row counts of `users`, `storage`, `wallet_ledger`, `user_device`, the newest `update_time` in `storage` against the dump's time, 20 rows in `migration_info`.
5. Boot the box's image against it (`NAKAMA_IMAGE` in `/opt/hulla/box.env`; it is built for amd64 only, so `--platform linux/amd64` on an Apple silicon Mac): `docker run -d --platform linux/amd64 --name drill-nakama --network drill --entrypoint /bin/sh <image> -ec '/nakama/nakama migrate up --database.address postgres:drill@postgres:5432/nakama && exec /nakama/nakama --config /nakama/data/config/local.yml --database.address postgres:drill@postgres:5432/nakama'`. `migrate up` must apply nothing. From the Postgres container: `wget -qO- http://drill-nakama:7350/healthcheck`, and `hello` with the http key of `local.yml`.
6. `docker rm -f -v drill-nakama drill-pg && docker network rm drill`, delete the dump.

## Drill record
A drill on the box is timed from "stop" to "scripted round passes"; a rehearsal off the box times the restore alone and says so.

| Date | Where | Backup size | Restore took | Rounds lost | Notes |
|---|---|---|---|---|---|
| 23 November 2026 | Off the box: throwaway `postgres:16-alpine` on the dev Mac, dump `nakama-20261123T1235Z.dump` | 89,771 bytes (88 KB); 9.4 MB restored | 0.2 s for `pg_restore`; about 10 s from an empty container to `hello` answering | None lost in the rehearsal. On the box the loss would be everything since the dump, up to a day | No errors. 20 tables, 58 indexes; 37 users, 399 storage rows, 106 ledger rows, 36 devices; newest storage write 20 November 14:27 UTC, which fits the dumps being the same size since 21 November (nobody played). The box's image (`f5ac7cc`) started on it with `migrate up` applying nothing, `/healthcheck` and `hello` answered, no error lines in its log. Not covered: the scripted round, and the restore on the box itself. The runbook described `pg_basebackup` with WAL archiving for phase 0, which was never set up; corrected above. |
| pending | phase 0 box | | | | A restore on the box, timed from "stop" to the scripted round, is still a condition of the beta gate (GDD 28.5). |
