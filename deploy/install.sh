#!/usr/bin/env bash
# One-time server setup for OBRL on Ubuntu 26.04 LTS (also works on 24.04).
# Run as root:  sudo DOMAIN=books.example.com ADMIN_EMAIL=you@example.com bash deploy/install.sh
# Safe to run again: existing keys, passwords and data are kept.
set -euo pipefail

DOMAIN="${DOMAIN:?Set DOMAIN, e.g. DOMAIN=books.example.com}"
ADMIN_EMAIL="${ADMIN_EMAIL:?Set ADMIN_EMAIL, the first admin account}"
WITH_CALIBRE="${WITH_CALIBRE:-yes}"     # needed only for MOBI/AZW3/FB2 uploads
LE_EMAIL="${LE_EMAIL:-$ADMIN_EMAIL}"    # Let's Encrypt expiry notices
WITH_SDK="${WITH_SDK:-yes}"             # the .NET SDK lets deploy/update.sh build new versions on this server
HERE="$(cd "$(dirname "$0")" && pwd)"

DOTNET_PKG=aspnetcore-runtime-10.0
[ "$WITH_SDK" = "yes" ] && DOTNET_PKG=dotnet-sdk-10.0   # the SDK includes the ASP.NET Core runtime

echo "==> Installing packages"
apt-get update
DEBIAN_FRONTEND=noninteractive apt-get install -y software-properties-common
add-apt-repository -y universe
apt-get update
if ! apt-cache show "$DOTNET_PKG" >/dev/null 2>&1; then
    echo "   $DOTNET_PKG is not in this Ubuntu release's archive: adding Canonical's .NET backports PPA"
    add-apt-repository -y ppa:dotnet/backports
    apt-get update
fi
DEBIAN_FRONTEND=noninteractive apt-get install -y \
    "$DOTNET_PKG" postgresql nginx certbot python3-certbot-nginx \
    libreoffice-writer-nogui fonts-dejavu-core fonts-liberation \
    ufw fail2ban python3-systemd unattended-upgrades restic openssl rsync curl git
if [ "$WITH_CALIBRE" = "yes" ]; then
    DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends calibre
fi

echo "==> Creating the obrl user and folders"
id obrl >/dev/null 2>&1 || useradd --system --home /var/lib/obrl --shell /usr/sbin/nologin obrl
install -d -o obrl -g obrl -m 750 /var/lib/obrl /var/lib/obrl/keys
install -d -o root -g obrl -m 750 /etc/obrl
install -d -o root -g root -m 755 /opt/obrl
install -d -o root -g root -m 700 /var/backups/obrl

echo "==> Master key for book encryption"
if [ ! -f /etc/obrl/master.key ]; then
    openssl rand -base64 32 > /etc/obrl/master.key
    echo "   Created /etc/obrl/master.key. BACK IT UP somewhere safe: without it no book can be read."
fi
chown root:obrl /etc/obrl/master.key && chmod 640 /etc/obrl/master.key

echo "==> PostgreSQL database"
if [ ! -f /etc/obrl/db.password ]; then
    openssl rand -hex 24 > /etc/obrl/db.password
    chmod 600 /etc/obrl/db.password
fi
DB_PASS="$(cat /etc/obrl/db.password)"
sudo -u postgres psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='obrl'" | grep -q 1 \
    || sudo -u postgres psql -c "CREATE ROLE obrl LOGIN PASSWORD '${DB_PASS}';"
sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='obrl'" | grep -q 1 \
    || sudo -u postgres createdb -O obrl obrl

echo "==> App settings (/etc/obrl/obrl.env)"
if [ ! -f /etc/obrl/obrl.env ]; then
    ADMIN_PASS="$(openssl rand -base64 18 | tr -d '/+=' | cut -c1-20)-Obrl"
    cat > /etc/obrl/obrl.env <<ENV
ConnectionStrings__Default=Host=localhost;Database=obrl;Username=obrl;Password=${DB_PASS}
Obrl__StorageRoot=/var/lib/obrl
Obrl__KeyFile=/etc/obrl/master.key
Obrl__PasskeyServerDomain=${DOMAIN}
AllowedHosts=${DOMAIN}
Obrl__Seed__AdminEmail=${ADMIN_EMAIL}
Obrl__Seed__AdminPassword=${ADMIN_PASS}
ENV
    echo "   First admin: ${ADMIN_EMAIL} / ${ADMIN_PASS}"
    echo "   Sign in, set up two-factor sign-in, then remove the two Seed lines from /etc/obrl/obrl.env."
fi
chown root:obrl /etc/obrl/obrl.env && chmod 640 /etc/obrl/obrl.env

echo "==> systemd service"
install -m 644 "$HERE/obrl.service" /etc/systemd/system/obrl.service
install -m 644 "$HERE/obrl-backup.service" /etc/systemd/system/obrl-backup.service
install -m 644 "$HERE/obrl-backup.timer" /etc/systemd/system/obrl-backup.timer
install -m 750 "$HERE/backup.sh" /usr/local/sbin/obrl-backup
systemctl daemon-reload
systemctl enable obrl.service obrl-backup.timer

echo "==> nginx"
install -m 644 "$HERE/nginx/obrl-proxy.conf" /etc/nginx/snippets/obrl-proxy.conf
sed "s/obrl.example.com/${DOMAIN}/g" "$HERE/nginx/obrl.conf" > /etc/nginx/sites-available/obrl
ln -sf /etc/nginx/sites-available/obrl /etc/nginx/sites-enabled/obrl
rm -f /etc/nginx/sites-enabled/default
nginx -t && systemctl reload nginx

echo "==> Firewall"
ufw allow OpenSSH
ufw allow 'Nginx Full'
ufw --force enable
# Ban IPs that keep failing SSH sign-in. Reading the systemd journal works whether or not /var/log/auth.log exists.
cat > /etc/fail2ban/jail.d/obrl-sshd.local <<'JAIL'
[sshd]
enabled = true
backend = systemd
JAIL
systemctl enable fail2ban && systemctl restart fail2ban

echo "==> HTTPS certificate"
certbot --nginx --non-interactive --agree-tos --redirect -m "$LE_EMAIL" -d "$DOMAIN" || \
    echo "   certbot failed. Check that ${DOMAIN} points at this server, then run: certbot --nginx -d ${DOMAIN}"

echo
echo "Done. Next: build and start the app with: sudo bash deploy/update.sh"
echo "(or, from a machine with the .NET 10 SDK: deploy/publish.sh user@${DOMAIN})"
