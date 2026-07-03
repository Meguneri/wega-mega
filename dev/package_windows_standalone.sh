#!/usr/bin/env bash
#
# Собирает «всё-в-одном» билд Wega-Mega под Windows x64 для человека,
# который просто хочет запустить игру одним кликом — без лаунчера, без .NET,
# без поиска "где клиент / где сервер".
#
# Результат: папка dist/WegaMega-Windows/ — её целиком отдаёшь игроку (zip/архив).
# Внутри он запускает "Играть.bat" -> поднимается сервер и клиент сам коннектится к localhost.
#
# Запускать ЭТОТ скрипт на своей машине (mac/linux), где стоит .NET SDK:
#     ./package_windows_standalone.sh
#
set -euo pipefail

cd "$(dirname "$0")"
ROOT="$(pwd)"

RID="win-x64"
# КРИТИЧНО при сборке на mac/linux: TargetOS по умолчанию = ОС сборки, и тогда
# в win-x64 бинарь попадают НЕ те платформенные дефайны (MACOS вместо WINDOWS),
# из-за чего клиент пытается грузить libopenal.1.dylib и падает. Форсим Windows.
TARGET_OS="Windows"
CONFIG="Release"
OUT="$ROOT/dist/WegaMega-Windows"
BIN="$OUT/bin"

echo ">>> Чистим прошлый билд: $OUT"
# macOS: Finder/Spotlight могут пересоздавать .DS_Store прямо во время удаления,
# из-за чего rm -rf падает с "Directory not empty". Делаем пару попыток.
for _ in 1 2 3; do rm -rf "$OUT" 2>/dev/null && break; sleep 1; done
rm -rf "$OUT"
mkdir -p "$BIN/Content.Client" "$BIN/Content.Server"

# --- Клиент ---------------------------------------------------------------
# ВАЖНО: FullRelease=false -> Content.Client остаётся запускаемым .exe.
# --self-contained -> игроку не нужно ставить .NET.
# БЕЗ PublishSingleFile -> иначе assembly.Location пустой и движок не найдёт ресурсы.
echo ">>> Публикуем КЛИЕНТ ($RID, self-contained)..."
dotnet publish Content.Client/Content.Client.csproj \
    -c "$CONFIG" -r "$RID" --self-contained true -p:TargetOS="$TARGET_OS" \
    -p:FullRelease=false -p:PublishSingleFile=false -p:PublishTrimmed=false \
    -p:ErrorOnDuplicatePublishOutputFiles=false \
    --nologo -o "$BIN/Content.Client"

# --- Сервер ---------------------------------------------------------------
echo ">>> Публикуем СЕРВЕР ($RID, self-contained)..."
# ErrorOnDuplicatePublishOutputFiles=false: Robust.Packaging кладёт свои
# deps.json/runtimeconfig.json и в net10.0/, и в net10.0/win-x64/, из-за чего
# publish сервера падает с NETSDK1152. Дубликат безвреден — отключаем проверку.
dotnet publish Content.Server/Content.Server.csproj \
    -c "$CONFIG" -r "$RID" --self-contained true -p:TargetOS="$TARGET_OS" \
    -p:FullRelease=false -p:PublishSingleFile=false -p:PublishTrimmed=false \
    -p:ErrorOnDuplicatePublishOutputFiles=false \
    --nologo -o "$BIN/Content.Server"

# Включаем файловый лог сервера (по умолчанию enabled=false). Без него на крашах
# не остаётся следов, а в окне их не успеть прочитать. Пишет в Content.Server/logs.
echo ">>> Включаю файл-лог сервера (log.enabled=true)..."
perl -0777 -i -pe 's/(\[log\][^\[]*?enabled = )false/${1}true/s' \
    "$BIN/Content.Server/server_config.toml" 2>/dev/null || \
    echo "    (не смог поправить server_config.toml — не критично)"

# --- Ресурсы --------------------------------------------------------------
# Движок монтирует Resources/ и RobustToolbox/Resources/ относительно .exe
# (../../ от bin/Content.*). Воспроизводим dev-раскладку.
echo ">>> Копируем игровые ресурсы..."
mkdir -p "$OUT/RobustToolbox"
# rsync без мусора сборки/исходников шейдеров не требуется — Resources уже готовы.
cp -a "$ROOT/Resources" "$OUT/Resources"
cp -a "$ROOT/RobustToolbox/Resources" "$OUT/RobustToolbox/Resources"

# --- Лаунчер для игрока ----------------------------------------------------
echo ">>> Пишем Играть.bat и README..."
# .bat для Windows: БЕЗ BOM и с CRLF-переводами строк (иначе cmd ломается на
# многострочных if-блоках и спотыкается о BOM на первой строке). Кириллица —
# через chcp 65001 (файл в UTF-8). awk добавляет \r\n в конце строк (портируемо).
{ cat <<'BAT'
@echo off
chcp 65001 >nul
title Wega-Mega
cd /d "%~dp0"

REM --- Проверка, что папка распакована (а не запуск из архива) ---
if not exist "bin\Content.Server\Content.Server.exe" (
  echo [ОШИБКА] Не найден bin\Content.Server\Content.Server.exe
  echo.
  echo Похоже, ты запустил Играть.bat прямо из ZIP-архива.
  echo Сначала РАСПАКУЙ всю папку: правый клик по архиву -^> "Извлечь все",
  echo потом открой распакованную папку и запусти Играть.bat оттуда.
  echo.
  pause
  goto :eof
)

REM --- Убиваем зависший сервер от прошлого запуска (иначе "порт занят") ---
taskkill /IM Content.Server.exe /F >nul 2>&1

echo Запускаю сервер (откроется отдельное окно "Wega-Mega Server")...
REM БЕЗ перенаправления в файл: иначе у сервера невалидная консоль и он каждый
REM кадр кидает IOException в SystemConsoleManager (грузит CPU). Логи сервер
REM пишет сам в bin\Content.Server\logs (log.enabled=true в server_config.toml).
start "Wega-Mega Server" /min "bin\Content.Server\Content.Server.exe"

echo Жду готовности сервера (до 90 секунд)...
powershell -NoProfile -Command "for($i=0;$i -lt 180;$i++){ try{ $c=New-Object Net.Sockets.TcpClient; $c.Connect('127.0.0.1',1212); $c.Close(); exit 0 }catch{ Start-Sleep -Milliseconds 500 } }; exit 1"
if errorlevel 1 (
  echo.
  echo [ОШИБКА] Сервер не поднялся за 90 секунд.
  echo Логи сервера лежат в папке: bin\Content.Server\logs
  echo Пришли разработчику самый свежий файл из этой папки.
  echo.
  pause
  goto :eof
)

echo Запускаю игру...
"bin\Content.Client\Content.Client.exe" --connect --connect-address udp://127.0.0.1:1212 > client_log.txt 2>&1
if errorlevel 1 (
  echo.
  echo [ОШИБКА] Игра завершилась с ошибкой. Последние строки client_log.txt:
  echo ----------------------------------------------------------------
  powershell -NoProfile -Command "if(Test-Path client_log.txt){Get-Content client_log.txt -Tail 30}"
  echo ----------------------------------------------------------------
  echo.
  echo Пришли файл client_log.txt разработчику (он лежит рядом с Играть.bat).
  pause
)

echo Закрываю сервер...
taskkill /IM Content.Server.exe /F >nul 2>&1
BAT
} | awk '{printf "%s\r\n", $0}' > "$OUT/Играть.bat"

# Запасной вариант на 2 шага (удобно на слабых ПК и для диагностики): отдельно
# сервер (видно загрузку до "Ready") и отдельно клиент.
{ cat <<'BAT'
@echo off
chcp 65001 >nul
title Wega-Mega SERVER
cd /d "%~dp0"

if not exist "bin\Content.Server\Content.Server.exe" (
  echo [ОШИБКА] Запускай из РАСПАКОВАННОЙ папки, а не из ZIP.
  pause
  goto :eof
)

taskkill /IM Content.Server.exe /F >nul 2>&1

echo ==========================================================
echo  ЗАПУСК СЕРВЕРА. Подожди строку:  Server Version ... -^> Ready
echo  Как увидишь "Ready" — запусти файл  Игра.bat
echo  ЭТО окно НЕ закрывай, пока играешь.
echo ==========================================================
echo.
"bin\Content.Server\Content.Server.exe"

echo.
echo === Сервер остановился. Если упал — лог в bin\Content.Server\logs
pause
BAT
} | awk '{printf "%s\r\n", $0}' > "$OUT/Сервер.bat"

{ cat <<'BAT'
@echo off
chcp 65001 >nul
title Wega-Mega
cd /d "%~dp0"

if not exist "bin\Content.Client\Content.Client.exe" (
  echo [ОШИБКА] Запускай из РАСПАКОВАННОЙ папки, а не из ZIP.
  pause
  goto :eof
)

echo Запускаю игру... (сначала должен работать Сервер.bat до строки "Ready")
"bin\Content.Client\Content.Client.exe" --connect --connect-address udp://127.0.0.1:1212 > client_log.txt 2>&1
if errorlevel 1 (
  echo.
  echo [ОШИБКА] Игра завершилась с ошибкой. Последние строки client_log.txt:
  echo ----------------------------------------------------------------
  powershell -NoProfile -Command "if(Test-Path client_log.txt){Get-Content client_log.txt -Tail 30}"
  echo ----------------------------------------------------------------
  echo.
  echo Пришли файл client_log.txt разработчику (он рядом с этим .bat).
  pause
)
BAT
} | awk '{printf "%s\r\n", $0}' > "$OUT/Игра.bat"

cat > "$OUT/КАК_ИГРАТЬ.txt" <<'TXT'
Wega-Mega — как играть
======================

1. Распакуй всю папку в любое место (например, на Рабочий стол).
2. Запусти файл "Играть.bat" (двойной клик).
3. Подожди несколько секунд — сначала поднимется сервер, потом откроется игра
   и сама подключится. Ставить .NET или что-то ещё НЕ нужно.

Чтобы выйти — просто закрой окно игры. Сервер закроется сам.

Если Windows ругается "SmartScreen / неизвестный издатель":
  нажми "Подробнее" -> "Выполнить в любом случае" (файл локальный, не из интернета).

Хочешь иконку на рабочий стол:
  правый клик по "Играть.bat" -> "Создать ярлык", перетащи ярлык на рабочий стол,
  при желании смени иконку (Свойства -> Сменить значок).
TXT

# --- Чистим за собой dev-окружение ----------------------------------------
# Сборка под -r win-x64 насыпает RID-подпапки bin/Content.*/win-x64 с дублями
# content-DLL. Из-за них обычный dev-запуск (dotnet run --project Content.Server)
# падает с "Found multiple modules with the same assembly name". Удаляем их.
echo ">>> Убираем RID-мусор из dev-папок bin/ (иначе сломается dotnet run)..."
rm -rf "$ROOT/bin/Content.Server/win-x64" "$ROOT/bin/Content.Client/win-x64"

echo ""
echo ">>> ГОТОВО. Билд тут:"
echo "    $OUT"
echo ">>> Заархивируй эту папку и отдай игроку. Запуск — Играть.bat"
