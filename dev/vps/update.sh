#!/bin/bash
# Готовим новую версию, не останавливая работающий сервер.
set -euo pipefail
exec /usr/local/sbin/wega-install-release --prepare
