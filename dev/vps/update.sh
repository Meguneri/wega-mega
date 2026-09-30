#!/bin/bash
# Проверяем Git и сообщаем игре о новой версии. Никакой сборки или перезапуска.
set -euo pipefail
umask 077
exec 9>/run/lock/wega-update.lock
flock -n 9 || exit 0
if [ -f /etc/wega/deploy.env ]; then
  source /etc/wega/deploy.env
else
  WEGA_BRANCH=codex/upstream-integration
fi

repo=/opt/wega/repository
notice=/var/lib/wega/data/update-pending
runuser -u wega -- git -C "$repo" fetch origin "$WEGA_BRANCH"
target=$(runuser -u wega -- git -C "$repo" rev-parse "origin/$WEGA_BRANCH")
installed=$(basename -- "$(readlink -f /opt/wega/current)")

if [ "$target" = "$installed" ]; then
  rm -f -- "$notice"
  exit 0
fi

if [ -f "$notice" ] && [ "$(tr -d '\r\n' < "$notice")" = "$target" ]; then
  exit 0
fi

tmp=$(mktemp /var/lib/wega/data/update-pending.XXXXXX)
printf '%s\n' "$target" > "$tmp"
chown wega:wega "$tmp"
chmod 600 "$tmp"
mv -f -- "$tmp" "$notice"
echo "Update available: $target; no build or restart was performed."
