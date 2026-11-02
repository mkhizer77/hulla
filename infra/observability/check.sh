#!/usr/bin/env bash
# Checks the observability files with the tools of the pinned images: the Prometheus config, the alert
# rules and their unit tests (alerts.test.yml), the Alertmanager config, the dashboard JSON and the
# compose file. Needs Docker. Run from anywhere: `pnpm observability:check`.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
docker="${DOCKER:-docker}"
prometheus_image="prom/prometheus:v3.5.0"
alertmanager_image="prom/alertmanager:v0.28.1"

run() { "$docker" run --rm -v "$here:/obs:ro" -w /obs "$@"; }

echo "== alert rules"
run --entrypoint promtool "$prometheus_image" check rules alerts.yml
echo "== alert rule tests"
run --entrypoint promtool "$prometheus_image" test rules alerts.test.yml
echo "== prometheus config"
# The rule file sits next to the config here and at /etc/prometheus in the container.
"$docker" run --rm -v "$here/prometheus.yml:/etc/prometheus/prometheus.yml:ro" \
  -v "$here/alerts.yml:/etc/prometheus/alerts.yml:ro" \
  --entrypoint promtool "$prometheus_image" check config /etc/prometheus/prometheus.yml
echo "== alertmanager config"
run --entrypoint amtool "$alertmanager_image" check-config alertmanager.yml
echo "== dashboards"
for f in "$here"/grafana/dashboards/*.json; do
  node -e 'const d = JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"));
    const ids = d.panels.map((p) => p.id);
    if (new Set(ids).size !== ids.length) throw new Error("duplicate panel ids in " + process.argv[1]);
    for (const p of d.panels) if (!p.targets || p.targets.length === 0) throw new Error("panel without a query: " + p.title);
    console.log(process.argv[1].split("/").pop() + ": " + d.panels.length + " panels");' "$f"
done
echo "== compose"
"$docker" compose -f "$root/server/nakama/compose.yml" -f "$here/compose.yml" config --quiet
echo "observability files OK"
