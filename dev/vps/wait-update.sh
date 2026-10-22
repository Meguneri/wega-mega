#!/bin/bash
# Аварийный перезапуск использует установленную версию без сборки обновлений.
set -euo pipefail
current=$(readlink -f /opt/wega/current || true)
pending=$(readlink -f /opt/wega/pending || true)
if [ -f "$current/.build-complete" ] || [ -f "$pending/.build-complete" ]; then
  echo 'Installed build available; starting without Git fetch or compilation.'
  exit 0
fi
echo 'No installed build; run wega-install-release before starting the server.' >&2
exit 1
