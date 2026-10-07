#!/bin/bash
# EC2 user data for a single-instance demo of the Web Crawler (Ubuntu 24.04, t3.small).
# Paste this whole file into "Advanced details → User data" when launching the instance.
# Setup log on the instance: /var/log/webcrawler-setup.log   (see deploy/aws/README.md)

# ─────────── Change these before launching ───────────
DEMO_USER="demo"
DEMO_PASSWORD="change-me-please"
# ──────────────────────────────────────────────────────

REPO="https://github.com/qaz216/webCrawler.git"
APP_DIR="/opt/webcrawler"

set -euo pipefail
exec > >(tee -a /var/log/webcrawler-setup.log) 2>&1
echo "=== Web Crawler setup started $(date -u) ==="

# Docker Engine + Compose + Buildx from Docker's official installer.
export DEBIAN_FRONTEND=noninteractive
apt-get update -y
apt-get install -y ca-certificates curl git openssl
curl -fsSL https://get.docker.com | sh
systemctl enable --now docker

# 2 GB swap: building the .NET and React images needs more than the 2 GB of RAM on a t3.small.
if [ ! -f /swapfile ]; then
  fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
  echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi

git clone --depth 1 "$REPO" "$APP_DIR"
cd "$APP_DIR"

# Secrets: random database/broker passwords and the bcrypt hash of the demo login.
# "$" is doubled because Docker Compose interpolates variables inside .env values.
HASH="$(docker run --rm caddy:2-alpine caddy hash-password --plaintext "$DEMO_PASSWORD")"
cat > .env <<EOF
POSTGRES_PASSWORD=$(openssl rand -hex 24)
RABBITMQ_USER=crawler
RABBITMQ_PASSWORD=$(openssl rand -hex 24)
BASIC_AUTH_USER=$DEMO_USER
BASIC_AUTH_HASH=${HASH//\$/\$\$}
EOF
chmod 600 .env

# Start on every boot (the public IP — and so the sslip.io address — can change after stop/start).
chmod +x deploy/aws/start.sh
cp deploy/aws/webcrawler.service /etc/systemd/system/webcrawler.service
systemctl daemon-reload
systemctl enable webcrawler.service
systemctl start webcrawler.service

echo "=== Setup finished $(date -u). Open: $(cat "$APP_DIR/SITE_URL" 2>/dev/null || echo 'see start log') ==="
