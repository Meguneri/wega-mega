#!/bin/bash
# Сборка для следующего запуска либо ручная установка.
set -euo pipefail
umask 077
exec 9>/run/lock/wega-update.lock
flock -n 9 || exit 0
prepare=false
if [ "${1:-}" = "--prepare" ]; then prepare=true; shift; fi
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
  if ! $prepare; then systemctl is-active --quiet wega.service || systemctl start wega.service; fi
  exit 0
fi
# На небольшом VPS не отнимаем ресурсы компиляцией у подключённых игроков.
if ! $prepare && systemctl is-active --quiet wega.service; then
  if ! python3 -c 'import json,urllib.request,sys; s=json.load(urllib.request.urlopen("http://127.0.0.1:1212/status",timeout=5)); sys.exit(0 if s["players"] == 0 else 1)'; then
    echo 'Players connected; refusing to install.' >&2
    exit 1
  fi
fi
if [ ! -e "$release/.build-complete" ]; then
  reusable=""
  # Карты, текстуры и скрипты деплоя не меняют игровые сборки и движок.
  for marker in /opt/wega/releases/*/.build-complete; do
    [ -f "$marker" ] || continue
    candidate=${marker%/.build-complete}
    base=${candidate##*/}
    [[ "$base" =~ ^[0-9a-f]{40}$ ]] || continue
    run_as_wega git -C "$repo" merge-base --is-ancestor "$base" "$target" || continue
    compatible=true
    changed=$(run_as_wega git -C "$repo" diff --name-only "$base" "$target")
    while IFS= read -r path; do
      [ -n "$path" ] || continue
      case "$path" in
        Resources/*|dev/*|*.md) ;;
        *) compatible=false; break ;;
      esac
    done <<< "$changed"
    if $compatible && [ -f "$candidate/bin/Content.Server/Content.Server.dll" ] &&
       [ -f "$candidate/bin/Content.Client/Content.Client.dll" ]; then
      reusable=$candidate
      break
    fi
  done
  if [ ! -d "$release" ]; then
    run_as_wega git -C "$repo" worktree add --detach "$release" "$target"
  fi
  if [ -n "$reusable" ]; then
    echo "Reusing verified binaries from $reusable; only resources/deployment changed."
    # Ресурсы основного репозитория уже взяты из нового коммита через worktree.
    # Движок неизменен; его ресурсы и бинарники копируем без ссылок на старый Git worktree.
    run_as_wega tar -C "$reusable" --exclude=.git -cf - bin RobustToolbox |
      run_as_wega tar -C "$release" -xf -
  else
    run_as_wega git -C "$release" submodule update --init --recursive --depth 1
    run_as_wega /opt/dotnet/dotnet build "$release/Content.Server" -c Release -m:1 -p:UseSharedCompilation=false
    run_as_wega /opt/dotnet/dotnet build "$release/Content.Client" -c Release -m:1 -p:UseSharedCompilation=false
  fi
  touch "$release/.build-complete"
fi
if $prepare; then
  exec 8>/run/lock/wega-pending.lock
  flock 8
  ln -sfn "$release" /opt/wega/pending.next
  mv -Tf /opt/wega/pending.next /opt/wega/pending
  printf '%s\n' "$target" > /var/lib/wega/data/update-pending
  chown wega:wega /var/lib/wega/data/update-pending
  echo "Build ready for next server start: $target; running server unchanged."
  exit 0
fi
# Не прерываем игру ради обновления.
if systemctl is-active --quiet wega.service; then
  if ! python3 -c 'import json,urllib.request,sys; s=json.load(urllib.request.urlopen("http://127.0.0.1:1212/status",timeout=5)); sys.exit(0 if s["players"] == 0 else 1)'; then
    echo 'Players connected during build; refusing to restart.' >&2
    exit 1
  fi
fi
systemctl stop wega.service
rm -f -- /opt/wega/pending
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
