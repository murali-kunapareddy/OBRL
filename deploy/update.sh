#!/usr/bin/env bash
# Install or update OBRL on this server from its git repository: get the code, build it, back up the database,
# swap in the new version and restart. Needs the .NET 10 SDK on the server (install.sh installs it).
# Usage, from the repository folder:  sudo bash deploy/update.sh [branch-or-tag]   (default: main)
# To go back to the version that ran before the last update:  sudo bash deploy/update.sh --rollback
set -euo pipefail
[ "$(id -u)" -eq 0 ] || { echo "Run with sudo: sudo bash deploy/update.sh"; exit 1; }
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OWNER="$(stat -c %U "$ROOT")"            # build as the person who cloned the repository, not as root
as_owner() { sudo -u "$OWNER" -H "$@"; }
STAMP="$(date +%Y%m%d-%H%M%S)"

check_running() {
    echo "==> Checking that OBRL answers"
    local host code
    host="$(grep -E '^AllowedHosts=' /etc/obrl/obrl.env 2>/dev/null | tail -n1 | cut -d= -f2- | cut -d';' -f1)"
    for _ in $(seq 1 30); do
        code="$(curl -s -o /dev/null -w '%{http_code}' -H "Host: ${host:-localhost}" http://127.0.0.1:5000/terms || true)"
        if [ "$code" = "200" ]; then echo "   OBRL is up."; return 0; fi
        sleep 2
    done
    echo "   OBRL did not answer within a minute. Recent log lines:"
    journalctl -u obrl --no-pager -n 40
    echo "   To go back to the previous version: sudo bash deploy/update.sh --rollback"
    return 1
}

if [ "${1:-}" = "--rollback" ]; then
    [ -d /opt/obrl.previous ] || { echo "No previous version in /opt/obrl.previous."; exit 1; }
    systemctl stop obrl
    rsync -a --delete /opt/obrl.previous/ /opt/obrl/
    systemctl start obrl
    echo "Rolled back the app. Database changes from the update are not undone: if the old version fails to start,"
    echo "restore the dump taken before the update (see docs/DEPLOY.md, Restoring)."
    check_running
    exit
fi

REF="${1:-main}"
echo "==> Getting ${REF} from git"
as_owner git -C "$ROOT" fetch --tags --prune origin
as_owner git -C "$ROOT" checkout -q "$REF"
# On a branch, move to the newest commit; a tag stays where it is.
if as_owner git -C "$ROOT" symbolic-ref -q HEAD >/dev/null; then
    as_owner git -C "$ROOT" merge -q --ff-only "origin/${REF}"
fi
echo "   Now at: $(as_owner git -C "$ROOT" log -1 --format='%h %s')"

echo "==> Building"
rm -rf "$ROOT/.publish"
as_owner dotnet publish "$ROOT/src/Obrl.Web/Obrl.Web.csproj" -c Release -r linux-x64 --self-contained false \
    -o "$ROOT/.publish" -nologo -v quiet

echo "==> Backing up the database first (the new version may change it when it starts)"
install -d -m 700 /var/backups/obrl
if sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='obrl'" | grep -q 1; then
    sudo -u postgres pg_dump -Fc obrl > "/var/backups/obrl/obrl-before-update-${STAMP}.dump"
    echo "   /var/backups/obrl/obrl-before-update-${STAMP}.dump"
    ls -1t /var/backups/obrl/obrl-before-update-*.dump | tail -n +6 | xargs -r rm -f   # keep the last 5
fi

echo "==> Installing the new version"
install -m 644 "$ROOT/deploy/obrl.service" /etc/systemd/system/obrl.service
install -m 644 "$ROOT/deploy/obrl-backup.service" /etc/systemd/system/obrl-backup.service
install -m 644 "$ROOT/deploy/obrl-backup.timer" /etc/systemd/system/obrl-backup.timer
install -m 750 "$ROOT/deploy/backup.sh" /usr/local/sbin/obrl-backup
systemctl daemon-reload
systemctl stop obrl || true
if [ -f /opt/obrl/Obrl.Web.dll ]; then
    rsync -a --delete /opt/obrl/ /opt/obrl.previous/
fi
rsync -a --delete "$ROOT/.publish/" /opt/obrl/
chown -R root:root /opt/obrl
systemctl enable obrl >/dev/null 2>&1
systemctl start obrl
check_running
