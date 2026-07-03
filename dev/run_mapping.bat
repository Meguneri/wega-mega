@echo off
REM Запуск wega-mega для МАППИНГА (Windows).
REM Требуется только .NET SDK 10: https://dotnet.microsoft.com/download
REM
REM Собирает проект, поднимает локальный сервер и открывает клиент.
REM В клиенте подключись к  localhost  — там ты автоматически админ.
REM
REM В игре открой консоль (~) и набери:
REM   mapping 50                                          - создать НОВУЮ карту
REM   mapping 50 Maps/_Wega/Arena/DMarena2urban.yml       - открыть существующую
REM Сохранение:  savemap 50 Maps/_Wega/Arena/моя_карта.yml
setlocal
cd /d "%~dp0"

echo [1/3] Сборка (первый раз — несколько минут)...
dotnet build Content.Server --configuration Debug -v quiet || goto :err
dotnet build Content.Client --configuration Debug -v quiet || goto :err

echo [2/3] Запуск локального сервера...
start "wega-server" dotnet run --project Content.Server --configuration Debug
timeout /t 5 /nobreak >nul

echo [3/3] Запуск клиента. Подключайся к: localhost
dotnet run --project Content.Client --configuration Debug

echo Готово. Окно сервера можно закрыть вручную.
goto :eof

:err
echo Ошибка сборки. Проверь, что установлен .NET SDK 10.
pause
