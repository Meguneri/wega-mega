#!/bin/bash
# Вызывается systemd только перед запуском игрового процесса.
set -euo pipefail
umask 077
exec 8>/run/lock/wega-pending.lock
flock 8
release=$(readlink -f /opt/wega/pending || true)
[ -n "$release" ] && [ -f "$release/.build-complete" ] || exit 0
[[ "$release" =~ ^/opt/wega/releases/[0-9a-f]{40}$ ]] || exit 1
[ -f "$release/bin/Content.Server/Content.Server.dll" ] || exit 1
[ "$(readlink -f /opt/wega/current || true)" != "$release" ] || exit 0
if [ -f /var/lib/wega/data/preferences.db ]; then
  backup=/var/lib/wega/backups/preferences.$(date -u +%Y%m%dT%H%M%S%N).db
  python3 -c 'import sqlite3,sys; s=sqlite3.connect("/var/lib/wega/data/preferences.db"); d=sqlite3.connect(sys.argv[1]); s.backup(d); assert d.execute("pragma integrity_check").fetchone()[0]=="ok"; d.close(); s.close()' "$backup"
fi
ln -sfn "$release" /opt/wega/current.next
mv -Tf /opt/wega/current.next /opt/wega/current
rm -f -- /opt/wega/pending /var/lib/wega/data/update-pending
echo "Starting prepared release: $release"
