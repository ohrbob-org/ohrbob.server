#!/bin/sh
# Runs as root only long enough to repair ownership of the mounted
# DataProtection-Keys volume, then drops privileges to the non-root app user.
#
# A named volume keeps the ownership it was created with. Volumes created by
# deployments that still ran the container as root therefore stay root-owned,
# and the non-root app user cannot read/write the encryption keys stored there
# (UnauthorizedAccessException on /app/DataProtection-Keys/key-*.xml).
set -eu

APP_UID="${APP_UID:-1654}"

if [ "$(id -u)" = "0" ]; then
    if [ -d /app/DataProtection-Keys ]; then
        if ! chown -R "$APP_UID:$APP_UID" /app/DataProtection-Keys; then
            echo "warning: could not take ownership of /app/DataProtection-Keys" >&2
        fi
    fi

    exec setpriv --reuid="$APP_UID" --regid="$APP_UID" --init-groups "$@"
fi

exec "$@"
