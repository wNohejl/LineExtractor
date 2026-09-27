#!/bin/sh
# Runs inside the backup container (compose.yml): a dump now, then one every 24 hours, keeping
# KEEP_DAYS days of them in /backups. Custom format, so scripts/restore-data.ps1's approach
# (reset the schema, pg_restore) and deploy/restore.sh both read them.
#
# A failed dump is loud in `docker compose logs backup` and leaves the previous files alone;
# the retention sweep runs only after a dump succeeds, so a week of failures never deletes the
# last good one.
set -u

while true; do
    stamp=$(date -u +%Y-%m-%dT%H%MZ)
    target="/backups/lineops-$stamp.dump"

    if pg_dump -Fc -Z 6 -f "$target.partial" && mv "$target.partial" "$target"; then
        echo "backup: wrote $target ($(du -h "$target" | cut -f1))"
        find /backups -name 'lineops-*.dump' -mtime +"${KEEP_DAYS:-14}" -print -delete \
            | sed 's/^/backup: expired /'
    else
        echo "backup: FAILED at $stamp; earlier dumps kept" >&2
        rm -f "$target.partial"
    fi

    sleep 86400
done
