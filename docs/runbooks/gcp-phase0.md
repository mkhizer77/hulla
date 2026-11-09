# Phase 0 box on Google Cloud

The development and test server (ADR-0013, update of 9 November 2026): one VM in Frankfurt running `infra/phase0/compose.yml` (Postgres, Nakama, LiveKit, Caddy), paid from the free-trial credit of the owner's billing account.

## What exists
| Thing | Name | Notes |
|---|---|---|
| Project | `hulla-phase0` | Only the server lives here; deleting the project stops every cost it has |
| VM | `hulla-1`, `europe-west3-a`, e2-medium (2 vCPU, 4 GB), Debian 12, 30 GB disk | Restarts itself after a host fault; IAP-only SSH |
| Address | `hulla-ip`, 35.198.164.91 | Premium network tier |
| Hostnames | `api.hulla.khizerkhalid.de` (Nakama, https 443), `voice.hulla.khizerkhalid.de` (LiveKit signalling), `turn.hulla.khizerkhalid.de` (TURN over TLS, 5349) | A records at IONOS on the owner's spare domain; `hulla.khizerkhalid.de` is a CNAME to the Pages site for the legal pages. Let's Encrypt certificates through Caddy. Move to the Hulla domain when it exists |
| Firewall | `hulla-web` tcp 80, 443; `hulla-voice` tcp 7881, udp 7882, udp 3478, tcp 5349; `hulla-ssh-iap` tcp 22 from IAP only | The console (7351) and metrics (9100) stay on loopback |
| Backups | `gs://hulla-phase0-backups/postgres/`, nightly 02:30 UTC, kept 14 days | `hulla-backup.timer`; run `sudo hulla-backup` before risky changes |
| Snapshots | `hulla-daily`, 03:00, kept 7 days | Whole disk, including `/opt/hulla/secrets.env` |
| Uptime | `hulla-api-de` checks `/healthcheck` every 5 minutes from three regions | Alert "Hulla server not answering" emails the owner |
| Budget | "Hulla credit guard", $280 across the whole billing account, before credits | Emails at 25, 50, 75, 90 and 100% and at a 90% forecast |
| Stop switch | Function `budget-guard` on topic `budget-alerts` | At 100% it stops every VM in the project; disks and backups stay |

## Money
- About $40 a month: the VM about $32, disk and address about $7, snapshots, storage and traffic cents.
- The billing account is a free trial. While it is not upgraded, Google cannot charge the card; at the end of the credit or the 90 days the VM stops. Image and video generation spend the same credit, which is why the budget covers the whole billing account.
- Once the box is stopped by the switch, the disk, address and snapshots still cost about $10 a month until deleted.
- Before the trial ends (late January 2027): upgrade the account and keep paying, or move to the Phase 1 box. Copy the latest dump to the Mac first: `gcloud storage cp gs://hulla-phase0-backups/postgres/<latest> .`.

## Daily use
```bash
gcloud compute ssh hulla-1 --zone europe-west3-a --project hulla-phase0 --tunnel-through-iap
cd /opt/hulla && sudo docker compose ps && sudo docker compose logs --tail 50 nakama
```
- Keys: `sudo cat /opt/hulla/secrets.env`. Keep a copy of `HULLA_PHONE_PEPPER` in the password manager; changing it unlinks every phone. The server key goes into client builds; the http key, console password and LiveKit secret never leave the box.
- Console: `gcloud compute start-iap-tunnel hulla-1 7351 --local-host-port=localhost:7351 --zone europe-west3-a --project hulla-phase0`, then http://localhost:7351.
- Deploy a new server build: set `NAKAMA_IMAGE=ghcr.io/mkhizer77/hulla/nakama:<sha>` in `/opt/hulla/box.env`, then `sudo bash /opt/hulla/setup.sh`. Rollback is the previous SHA. The `server-image` workflow's SSH deploy does not reach an IAP-only box yet.
- Change the stack: edit `infra/phase0/`, copy the changed files to `/opt/hulla` with `gcloud compute scp --tunnel-through-iap`, run `setup.sh` again. It keeps the secrets and re-renders the configs.

## Clients on the box
- Development builds: put `{"server": "https://api.hulla.khizerkhalid.de", "serverKey": "<NAKAMA_SERVER_KEY>"}` in the git-ignored `apps/unity/Hulla/HullaServer.local.json` (or set `HULLA_DEV_SERVER` and `HULLA_DEV_SERVER_KEY`), then build as usual; the APK and the iOS export start at the box. Without the file they start at the building Mac as before.
- Mac windows: `open -n Builds/macOS/Hulla.app --args -hulla-server https://api.hulla.khizerkhalid.de -hulla-server-key <key> -hulla-device <name>`.
- The Settings server field takes `host[:port]` (local, http) or `https://host` (the box); a build remembers the key for its own server.
- Integration tests against the box: `HULLA_NAKAMA_HOST=api.hulla.khizerkhalid.de HULLA_NAKAMA_PORT=443 HULLA_NAKAMA_SCHEME=https HULLA_NAKAMA_SERVER_KEY=<key> HULLA_NAKAMA_HTTP_KEY=<http key>`, play mode, filter `Hulla.Integration`. On 9 November 2026 nine of ten passed; phone sign-in needs the WhatsApp or SMS accounts first.

## Building it again
1. `gcloud projects create`, link the billing account, enable Compute, Storage, Billing Budgets, Pub/Sub, Cloud Functions, Cloud Run, Cloud Build, Artifact Registry, Eventarc, Monitoring and IAP.
2. Topic `budget-alerts`; the budget with `--credit-types-treatment=exclude-all-credits` and that topic; service account `budget-guard` with Compute Instance Admin and Run Invoker on its own service; deploy `infra/phase0/budget-guard` (gen2, Python 3.12, 512 MiB, internal ingress). Test it on an empty VM by publishing `{"costAmount":281,"budgetAmount":280}` to the topic: the VM must stop within a minute.
3. Static address, the three firewall rules (delete the default SSH and RDP rules), the backup bucket with a 14-day delete rule and Object Admin for the box's service account, the snapshot schedule.
4. The VM with the address, tag `hulla`, the box service account and the snapshot schedule on its disk.
5. Copy `infra/phase0/*` to `/opt/hulla`, write `box.env` (`API_HOST`, `VOICE_HOST`, `TURN_HOST`, `BACKUP_BUCKET`, `NAKAMA_IMAGE`), run `sudo bash setup.sh`. LiveKit restarts until Caddy has the TURN certificate, then starts on its own; restart Nakama after a host change so it hands out the new voice URL.
6. Uptime check on `/healthcheck` and the alert policy with an email channel.

## Traps
- `sysctl --system` on the GCE image resets `net.ipv4.ip_forward` to 0 and cuts Docker's published ports while loopback still works. `setup.sh` loads only its own file; `systemctl restart docker` repairs it.
- The box cannot reach its own public address; test from outside or with `curl --resolve <host>:443:127.0.0.1`.
- LiveKit loads the TURN certificate only at start: `hulla-livekit-restart.timer` restarts it every Monday 04:00 UTC to pick up Caddy's renewals.
- A gen2 function needs Run Invoker for the trigger's service account, or every budget message is refused.

## Checks
- `curl https://api.hulla.khizerkhalid.de/healthcheck` answers `{}`; `openssl s_client -connect turn.hulla.khizerkhalid.de:5349` shows its certificate.
- A device sign-in with the server key, then `hello` and `table_create`, answer `ok` (done 9 November 2026).
- A hard reset of the VM: the API answers again within a minute (done 9 November 2026).
