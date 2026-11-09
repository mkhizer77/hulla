#!/usr/bin/env bash
# Dumps the Nakama database and streams it to the box's backup bucket. Installed as /usr/local/bin/hulla-backup
# and run nightly by hulla-backup.timer; run it by hand before a risky change.
set -euo pipefail
cd /opt/hulla
# shellcheck source=/dev/null
source box.env
stamp=$(date -u +%Y%m%dT%H%MZ)
docker compose exec -T postgres pg_dump -U postgres -Fc nakama |
  gcloud storage cp - "gs://${BACKUP_BUCKET}/postgres/nakama-${stamp}.dump"
echo "backup written: gs://${BACKUP_BUCKET}/postgres/nakama-${stamp}.dump"
