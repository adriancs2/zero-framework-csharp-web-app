#!/usr/bin/env bash
# Installs / upgrades cshttp Hearth CMS on Debian/Ubuntu behind nginx.
#
#   sudo ./install.sh <package-dir-or-tar.gz>
#
# <package> is publish/linux-x64 (or hearthcms-linux-x64.tar.gz) produced by
# tools/publish.ps1. Nothing else is needed: the app is self-contained (no
# .NET runtime install). Re-running upgrades the binaries and keeps the
# existing App_Data (database, config.txt, logs) and uploaded media.
set -euo pipefail

PKG="${1:?usage: install.sh <publish dir or .tar.gz>}"
APP=/opt/hearthcms
HERE="$(cd "$(dirname "$0")" && pwd)"

[ "$(id -u)" -eq 0 ] || { echo "run as root (sudo)"; exit 1; }

# 1. nginx + rsync
if ! command -v nginx >/dev/null || ! command -v rsync >/dev/null; then
    apt-get update -y
    DEBIAN_FRONTEND=noninteractive apt-get install -y nginx rsync
fi

# 2. service account
id hearthcms >/dev/null 2>&1 || useradd --system --home-dir "$APP" --shell /usr/sbin/nologin hearthcms

# 3. unpack package
SRC="$PKG"
if [ -f "$PKG" ]; then
    SRC="$(mktemp -d)"
    tar -xzf "$PKG" -C "$SRC"
fi
[ -x "$SRC/HearthCms" ] || chmod +x "$SRC/HearthCms" 2>/dev/null || true
[ -f "$SRC/HearthCms" ] || { echo "HearthCms executable not found in $SRC"; exit 1; }

# 4. stop, copy (keep data on upgrade), fix ownership
systemctl stop hearthcms 2>/dev/null || true
mkdir -p "$APP"
if [ -d "$APP/App_Data" ]; then
    echo "upgrade: keeping existing App_Data, media and favicon.ico"
    rsync -a --delete --exclude 'App_Data/' --exclude 'media/' --exclude 'favicon.ico' "$SRC/" "$APP/"
else
    echo "first install"
    rsync -a "$SRC/" "$APP/"
fi
mkdir -p "$APP/App_Data/logs"
chmod +x "$APP/HearthCms"
chown -R hearthcms:hearthcms "$APP"
# nginx (www-data) reads the static folders directly.
chmod o+rx "$APP"
find "$APP/css" "$APP/js" "$APP/assets" "$APP/fonts" "$APP/media" -type d -exec chmod o+rx {} + 2>/dev/null || true
find "$APP/css" "$APP/js" "$APP/assets" "$APP/fonts" "$APP/media" -type f -exec chmod o+r {} + 2>/dev/null || true
[ -f "$APP/favicon.ico" ] && chmod o+r "$APP/favicon.ico"
chmod -R o-rwx "$APP/App_Data"
# new uploads must stay readable by nginx
setfacl -R -d -m o::rX "$APP/media" "$APP/assets" "$APP/css" 2>/dev/null || true

# 5. systemd unit
install -m 0644 "$HERE/hearthcms.service" /etc/systemd/system/hearthcms.service
systemctl daemon-reload
systemctl enable hearthcms >/dev/null
systemctl start hearthcms

# 6. nginx site
install -m 0644 "$HERE/nginx-hearthcms.conf" /etc/nginx/sites-available/hearthcms
ln -sf /etc/nginx/sites-available/hearthcms /etc/nginx/sites-enabled/hearthcms
rm -f /etc/nginx/sites-enabled/default
nginx -t
systemctl enable nginx >/dev/null
systemctl reload nginx || systemctl restart nginx

# 7. wait for the app
for i in $(seq 1 30); do
    if curl -fsS -o /dev/null http://127.0.0.1:5080/robots.txt; then
        echo "Hearth CMS is up: http://$(hostname -I | awk '{print $1}')/"
        exit 0
    fi
    sleep 1
done
echo "app did not answer on 127.0.0.1:5080 - check: journalctl -u hearthcms -n 100"
exit 1
