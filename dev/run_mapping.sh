#!/usr/bin/env bash
# Запуск wega-mega для МАППИНГА (macOS / Linux).
# Требуется только установленный .NET SDK 10 (https://dotnet.microsoft.com/download).
#
# Что делает: собирает проект, поднимает локальный сервер и открывает клиент.
# В клиенте подключайся к localhost — на локальном сервере ты автоматически админ.
#
# Дальше в игре открой консоль (~) и набери:
#   mapping 50                                  — создать НОВУЮ карту (id 50, любой свободный)
#   mapping 50 Maps/_Wega/Arena/DMarena2urban.yml   — открыть существующую для правки
# Сохранение: команда  savemap 50 Maps/_Wega/Arena/моя_карта.yml
set -euo pipefail
cd "$(dirname "$0")"

echo "[1/3] Сборка (первый раз — несколько минут)..."
dotnet build Content.Server --configuration Debug -v quiet
dotnet build Content.Client --configuration Debug -v quiet

echo "[2/3] Запуск локального сервера..."
dotnet run --project Content.Server --configuration Debug &
SERVER_PID=$!
trap 'kill $SERVER_PID 2>/dev/null || true' EXIT
sleep 5

echo "[3/3] Запуск клиента. Подключайся к: localhost"
dotnet run --project Content.Client --configuration Debug

echo "Клиент закрыт, останавливаю сервер."
