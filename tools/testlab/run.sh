#!/usr/bin/env bash
# Runs the Robo crawl of a development APK on every device in devices.txt (one Test Lab matrix)
# and writes the results link to the GitHub step summary. Usage: run.sh <apk> [results-bucket]
set -euo pipefail

apk=$1
bucket=${2:-}
here=$(cd "$(dirname "$0")" && pwd)
devices=()
while IFS= read -r line; do
  line=${line%%#*}
  line=$(echo "$line" | xargs)
  [ -n "$line" ] && devices+=(--device "$line")
done < "$here/devices.txt"

args=(firebase test android run --type robo --app "$apk" --timeout 5m
  --results-history-name "hulla-nightly" --format json "${devices[@]}")
[ -n "$bucket" ] && args+=(--results-bucket "$bucket")

set +e
gcloud "${args[@]}" > outcome.json 2> testlab.log
status=$?
set -e
link=$(grep -o 'https://console.firebase.google.com[^ ]*' testlab.log | head -1 || true)
{
  echo "## Device matrix"
  echo
  [ -n "$link" ] && echo "Results: $link" && echo
  echo '| Device | Outcome |'
  echo '|---|---|'
  python3 - <<'PY'
import json
try:
    rows = json.load(open("outcome.json"))
except Exception:
    rows = []
for r in rows:
    print(f"| {r.get('axis_value', '?')} | {r.get('outcome', '?')} |")
PY
} >> "${GITHUB_STEP_SUMMARY:-/dev/stdout}"
exit $status
