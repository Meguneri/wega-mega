#!/bin/bash
# Экспорт готовой Linux-сборки без базы, конфигурации и истории Git.
set -euo pipefail
umask 077
release=$(readlink -f /opt/wega/current)
case "$release" in /opt/wega/releases/*) ;; *) echo 'Invalid release path.' >&2; exit 1;; esac
commit=$(basename "$release")
[[ "$commit" =~ ^[0-9a-f]{40}$ ]] || exit 1
output=${1:?Pass a new output directory}
if [ -e "$output" ]; then echo 'Output directory already exists.' >&2; exit 1; fi
mkdir -m 700 -- "$output"
# Только разрешённые каталоги: data, logs и TOML из bin не попадут в архив.
nice -n 19 tar -C "$release" --exclude='*/data' --exclude='*/logs' --exclude='*.toml' \
  --exclude='*.db' --exclude='*.db-*' --exclude='*.sqlite*' \
  -czf "$output/wega-release.tar.gz" bin/Content.Server bin/Content.Client Resources
printf '%s\n' "$commit" > "$output/wega-release.commit"
(cd "$output" && sha256sum wega-release.tar.gz > wega-release.sha256)
echo "Bundle saved: $output; commit: $commit"
