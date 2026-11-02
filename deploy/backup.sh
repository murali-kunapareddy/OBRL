#!/usr/bin/env bash
# Nightly OBRL backup: database dump + encrypted book files + keys, kept with restic.
# Configure the restic repository in /etc/obrl/backup.env, for example:
#   RESTIC_REPOSITORY=sftp:backup@backup-host:/srv/restic/obrl
#   RESTIC_PASSWORD=a-long-random-passphrase
set -euo pipefail
[ -f /etc/obrl/backup.env ] && set -a && . /etc/obrl/backup.env && set +a
STAMP="$(date +%Y%m%d-%H%M%S)"

sudo -u postgres pg_dump -Fc obrl > "/var/backups/obrl/obrl-${STAMP}.dump"
find /var/backups/obrl -name 'obrl-*.dump' -mtime +7 -delete

if [ -z "${RESTIC_REPOSITORY:-}" ]; then
    echo "RESTIC_REPOSITORY not set: kept the local database dump only. Copy /etc/obrl/master.key off this server yourself."
    exit 0
fi
restic snapshots >/dev/null 2>&1 || restic init
# The page cache and temp folders are rebuilt automatically, so they are left out.
restic backup /var/backups/obrl /var/lib/obrl /etc/obrl --exclude /var/lib/obrl/cache --exclude /var/lib/obrl/tmp
restic forget --keep-daily 7 --keep-weekly 4 --keep-monthly 6 --prune
