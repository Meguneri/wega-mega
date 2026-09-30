#!/bin/bash
# Только ручная установка; не вызывать из таймера.
set -euo pipefail
umask 077
exec 9>/run/lock/wega-update.lock
flock -n 9 || exit 0
source /etc/wega/deploy.env
repo=/opt/wega/repository
export DOTNET_ROOT=/opt/dotnet
export PATH=/opt/dotnet:$PATH
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
run_as_wega() { runuser -u wega -- env DOTNET_ROOT=/opt/dotnet PATH="$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 "$@"; }
run_as_wega git -C "$repo" fetch origin "$WEGA_BRANCH"
target=${1:-$(run_as_wega git -C "$repo" rev-parse "origin/$WEGA_BRANCH")}
[[ "$target" =~ ^[0-9a-f]{40}$ ]] || { echo 'Expected full commit SHA.' >&2; exit 1; }
run_as_wega git -C "$repo" cat-file -e "$target^{commit}"
release=/opt/wega/releases/$target
old=$(readlink -f /opt/wega/current || true)
if [ "$old" = "$release" ]; then
  systemctl is-active --quiet wega.service || systemctl start wega.service
  exit 0
fi
# На небольшом VPS не отнимаем ресурсы компиляцией у подключённых игроков.
if systemctl is-active --quiet wega.service; then
  if ! python3 -c 'import json,urllib.request,sys; s=json.load(urllib.request.urlopen("http://127.0.0.1:1212/status",timeout=5)); sys.exit(0 if s["players"] == 0 else 1)'; then
    echo 'Players connected; refusing to install.' >&2
    exit 1
  fi
fi
if [ ! -e "$release/.build-complete" ]; then
  if [ ! -d "$release" ]; then
    run_as_wega git -C "$repo" worktree add --detach "$release" "$target"
  fi
  run_as_wega git -C "$release" submodule update --init --recursive --depth 1
  run_as_wega /opt/dotnet/dotnet build "$release/Content.Server" -c Release -m:1 -p:UseSharedCompilation=false
  run_as_wega /opt/dotnet/dotnet build "$release/Content.Client" -c Release -m:1 -p:UseSharedCompilation=false
  touch "$release/.build-complete"
fi
# Не прерываем игру ради обновления.
if systemctl is-active --quiet wega.service; then
  if ! python3 -c 'import json,urllib.request,sys; s=json.load(urllib.request.urlopen("http://127.0.0.1:1212/status",timeout=5)); sys.exit(0 if s["players"] == 0 else 1)'; then
    echo 'Players connected during build; refusing to restart.' >&2
    exit 1
  fi
fi
systemctl stop wega.service
backup=""
if [ -f /var/lib/wega/data/preferences.db ]; then
  backup=/var/lib/wega/backups/preferences.$(date -u +%Y%m%dT%H%M%SZ).db
  python3 -c 'import sqlite3,sys; s=sqlite3.connect("/var/lib/wega/data/preferences.db"); d=sqlite3.connect(sys.argv[1]); s.backup(d); assert d.execute("pragma integrity_check").fetchone()[0]=="ok"; d.close(); s.close(); print("Database backup:",sys.argv[1])' "$backup"
fi
ln -sfn "$release" /opt/wega/current.next
mv -Tf /opt/wega/current.next /opt/wega/current
started=$(date +%s)
systemctl start wega.service
for i in $(seq 1 90); do
  if journalctl -u wega.service --since "@$started" --no-pager | grep -q 'Server Version .* -> Ready'; then
    rm -f -- /var/lib/wega/data/update-pending
    echo "Deployment ready: $target"
    exit 0
  fi
  sleep 2
done
echo 'New release did not become ready; stopping it.' >&2
systemctl stop wega.service
if [ -n "$old" ] && [ -d "$old" ]; then
  if [ -n "$backup" ]; then
    # Сохраняем неудачную миграцию отдельно и возвращаем согласованную БД старой версии.
    python3 -c 'import sqlite3,sys; c=sqlite3.connect("/var/lib/wega/data/preferences.db"); f=sqlite3.connect(sys.argv[1]+".failed-release"); c.backup(f); f.close(); b=sqlite3.connect(sys.argv[1]); b.backup(c); b.close(); c.close()' "$backup"
    chown wega:wega /var/lib/wega/data/preferences.db
  fi
  ln -sfn "$old" /opt/wega/current.next
  mv -Tf /opt/wega/current.next /opt/wega/current
  systemctl start wega.service
  echo 'Previous release and its pre-update database restored; failed database retained in backups.' >&2
fi
exit 1
