#!/usr/bin/env bash
# Nightly OBRL backup: database dump + encrypted book files + keys, kept with restic.
# Configure the restic repository in /etc/obrl/backup.env, for example:
#   RESTIC_REPOSITORY=sftp:backup@backup-host:/srv/restic/obrl
#   RESTIC_PASSWORD=a-long-random-passphrase
set -euo pipefail
[ -f /etc/obrl/backup.env ] && set -a && . /etc/obrl/backup.env && set +a
STAMP="$(date +%Y%m%d-%H%M%S)"

# Where the books are: Obrl__StorageRoot in /etc/obrl/obrl.env (default /var/lib/obrl). When the library lives on
# another server that keeps its own snapshots, set BACKUP_BOOKS=no in /etc/obrl/backup.env to back up only the
# database and keys here.
env_value() { grep -E "^$1=" /etc/obrl/obrl.env 2>/dev/null | tail -n1 | cut -d= -f2- || true; }
STORAGE="$(env_value Obrl__StorageRoot)"; STORAGE="${STORAGE:-/var/lib/obrl}"
CACHE="$(env_value Obrl__CacheRoot)"; CACHE="${CACHE:-$STORAGE}"

sudo -u postgres pg_dump -Fc obrl > "/var/backups/obrl/obrl-${STAMP}.dump"
find /var/backups/obrl -name 'obrl-*.dump' -mtime +7 -delete

if [ -z "${RESTIC_REPOSITORY:-}" ]; then
    echo "RESTIC_REPOSITORY not set: kept the local database dump only. Copy /etc/obrl/master.key off this server yourself."
    exit 0
fi
restic snapshots >/dev/null 2>&1 || restic init
# The page cache and temp folders are rebuilt automatically, so they are left out.
PATHS=(/var/backups/obrl /etc/obrl)
[ "${BACKUP_BOOKS:-yes}" = "no" ] || PATHS+=("$STORAGE")
restic backup "${PATHS[@]}" --exclude "$CACHE/cache" --exclude "$CACHE/tmp"
restic forget --keep-daily 7 --keep-weekly 4 --keep-monthly 6 --prune
