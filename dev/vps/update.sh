#!/bin/bash
# Таймер только проверяет Git и уведомляет игру; сборка запускается отдельно.
set -euo pipefail
umask 077
exec 9>/run/lock/wega-update.lock
flock -n 9 || exit 0
source /etc/wega/deploy.env
repo=/opt/wega/repository
runuser -u wega -- git -C "$repo" fetch origin "$WEGA_BRANCH"
target=$(runuser -u wega -- git -C "$repo" rev-parse "origin/$WEGA_BRANCH")
[[ "$target" =~ ^[0-9a-f]{40}$ ]] || exit 1
current=$(readlink -f /opt/wega/current || true)
if [ "${current##*/}" = "$target" ]; then exit 0; fi
notice=/var/lib/wega/data/update-pending
printf '%s\n' "$target" > "$notice.next"
chown wega:wega "$notice.next"
mv -f "$notice.next" "$notice"
echo "Git update available: $target; notification only."
