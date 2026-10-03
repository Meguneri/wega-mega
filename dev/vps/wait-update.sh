#!/bin/bash
# Новый игровой процесс запускается только после успешной подготовки обновления.
set -euo pipefail
source /etc/wega/deploy.env
repo=/opt/wega/repository
target=$(runuser -u wega -- git -C "$repo" rev-parse "origin/$WEGA_BRANCH")
release=/opt/wega/releases/$target
current=$(readlink -f /opt/wega/current || true)
pending=$(readlink -f /opt/wega/pending || true)
if [ -f "$release/.build-complete" ] && { [ "$current" = "$release" ] || [ "$pending" = "$release" ]; }; then
  echo "Prepared version already available; starting immediately: $target"
  exit 0
fi
echo 'Waiting for update preparation before server start.'
# Для oneshot start ожидает завершения, в том числе уже выполняющейся сборки.
systemctl start wega-update.service
target=$(runuser -u wega -- git -C /opt/wega/repository rev-parse "origin/$WEGA_BRANCH")
release=/opt/wega/releases/$target
if [ ! -f "$release/.build-complete" ]; then
  echo "Update is not ready: $target; refusing to start an older server." >&2
  exit 1
fi
current=$(readlink -f /opt/wega/current || true)
pending=$(readlink -f /opt/wega/pending || true)
if [ "$current" != "$release" ] && [ "$pending" != "$release" ]; then
  echo "Prepared update is not selected: $target." >&2
  exit 1
fi
echo "Update ready, proceeding with server start: $target"
