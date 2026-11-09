#!/usr/bin/env bash
# Prepares the phase 0 box (Debian 12 on Google Cloud) and starts the stack. Run as root from /opt/hulla
# after copying this folder there and writing box.env (docs/runbooks/gcp-phase0.md). Safe to run again:
# secrets are made once and kept, configs are re-rendered, containers are updated in place.
set -euo pipefail
cd /opt/hulla

# shellcheck source=/dev/null
source box.env
: "${API_HOST:?}" "${VOICE_HOST:?}" "${TURN_HOST:?}" "${BACKUP_BUCKET:?}" "${NAKAMA_IMAGE:?}"

if ! command -v docker >/dev/null; then
  apt-get update -q
  apt-get install -yq ca-certificates curl gettext-base
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
    >/etc/apt/sources.list.d/docker.list
  apt-get update -q
  apt-get install -yq docker-ce docker-ce-cli containerd.io docker-compose-plugin
fi
command -v envsubst >/dev/null || apt-get install -yq gettext-base

# A small swap file so a memory spike slows the box instead of killing Postgres.
if [ ! -f /swapfile ]; then
  fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
  echo '/swapfile none swap sw 0 0' >>/etc/fstab
fi

# Larger UDP buffers for voice media (LiveKit warns below 5 MB).
cat >/etc/sysctl.d/90-hulla-voice.conf <<'SYSCTL'
net.core.rmem_max=5000000
net.core.rmem_default=5000000
SYSCTL
# Load only this file: `sysctl --system` would reapply the image's ip_forward=0 and cut Docker's published ports.
sysctl -q -p /etc/sysctl.d/90-hulla-voice.conf

# Secrets: hex only, so they need no quoting in URLs or YAML. Made once; losing HULLA_PHONE_PEPPER unlinks every phone.
if [ ! -f secrets.env ]; then
  umask 077
  {
    echo "POSTGRES_PASSWORD=$(openssl rand -hex 24)"
    echo "NAKAMA_SERVER_KEY=$(openssl rand -hex 16)"
    echo "NAKAMA_HTTP_KEY=$(openssl rand -hex 24)"
    echo "NAKAMA_CONSOLE_PASSWORD=$(openssl rand -hex 16)"
    echo "NAKAMA_SESSION_KEY=$(openssl rand -hex 32)"
    echo "NAKAMA_REFRESH_KEY=$(openssl rand -hex 32)"
    echo "LIVEKIT_API_KEY=hulla-$(openssl rand -hex 6)"
    echo "LIVEKIT_API_SECRET=$(openssl rand -hex 32)"
    echo "HULLA_PHONE_PEPPER=$(openssl rand -hex 32)"
  } >secrets.env
fi
chmod 600 secrets.env box.env

set -a
# shellcheck source=/dev/null
source secrets.env
set +a
export API_HOST VOICE_HOST TURN_HOST

umask 077
envsubst '${POSTGRES_PASSWORD} ${NAKAMA_CONSOLE_PASSWORD} ${NAKAMA_SERVER_KEY} ${NAKAMA_HTTP_KEY} ${NAKAMA_SESSION_KEY} ${NAKAMA_REFRESH_KEY} ${HULLA_PHONE_PEPPER} ${VOICE_HOST} ${LIVEKIT_API_KEY} ${LIVEKIT_API_SECRET}' \
  <nakama.yml.tmpl >nakama.yml
envsubst '${LIVEKIT_API_KEY} ${LIVEKIT_API_SECRET} ${TURN_HOST}' <livekit.yaml.tmpl >livekit.yaml
{
  echo "NAKAMA_IMAGE=${NAKAMA_IMAGE}"
  echo "POSTGRES_PASSWORD=${POSTGRES_PASSWORD}"
  echo "API_HOST=${API_HOST}"
  echo "VOICE_HOST=${VOICE_HOST}"
  echo "TURN_HOST=${TURN_HOST}"
} >.env
# Nakama runs as a non-root user and must read its config.
chmod 644 nakama.yml livekit.yaml

# Nightly database backup to Cloud Storage (the bucket deletes copies after 14 days).
install -m 0755 backup.sh /usr/local/bin/hulla-backup
cat >/etc/systemd/system/hulla-backup.service <<'UNIT'
[Unit]
Description=Back up the Hulla database to Cloud Storage
After=docker.service
[Service]
Type=oneshot
ExecStart=/usr/local/bin/hulla-backup
UNIT
cat >/etc/systemd/system/hulla-backup.timer <<'UNIT'
[Unit]
Description=Nightly Hulla database backup
[Timer]
OnCalendar=*-*-* 02:30:00 UTC
Persistent=true
[Install]
WantedBy=timers.target
UNIT
# LiveKit reads the TURN certificate only at start; a weekly restart picks up Caddy's renewals (every ~60 days).
cat >/etc/systemd/system/hulla-livekit-restart.service <<'UNIT'
[Unit]
Description=Restart LiveKit to load a renewed TURN certificate
After=docker.service
[Service]
Type=oneshot
WorkingDirectory=/opt/hulla
ExecStart=/usr/bin/docker compose restart livekit
UNIT
cat >/etc/systemd/system/hulla-livekit-restart.timer <<'UNIT'
[Unit]
Description=Weekly LiveKit restart for certificate renewals
[Timer]
OnCalendar=Mon *-*-* 04:00:00 UTC
Persistent=true
[Install]
WantedBy=timers.target
UNIT

systemctl daemon-reload
systemctl enable --now hulla-backup.timer hulla-livekit-restart.timer

docker compose pull -q
docker compose up -d --remove-orphans
docker compose ps
