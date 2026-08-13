#!/usr/bin/env bash
# Перенос боевого сервера на другой VPS без потери данных.
#
# Что переносится:
#   /opt/wega/data/*.db              — БД: персонажи, преференции, баны, вайтлист, админы,
#                                      наигранное время, рейд-стэш и валюта
#   /opt/wega/data/llm_npc/          — файловая память NPC (Ева, Макс, Вивьен) + учёт расхода
#   /opt/wega/server/server_config.toml — конфиг с API-ключом (git-untracked!)
#
# Что НЕ переносится (намеренно):
#   /opt/wega/data/media_player/     — скачанные yt-dlp/ffmpeg и кэш роликов, гигабайты,
#                                      сервер сам всё перекачает при первом запуске
#   /opt/wega/server (код)           — клонируется заново из git на новом VPS
#
# Использование:
#   на СТАРОМ VPS:   ./migrate_server.sh backup
#   скопировать архив: scp root@старый:/root/wega_backup_*.tar.gz .
#                      scp wega_backup_*.tar.gz root@новый:/root/
#   на НОВОМ VPS:    ./migrate_server.sh restore /root/wega_backup_ГГГГММДД-ЧЧММСС.tar.gz
#
# ВАЖНО: архив содержит API-ключ в открытом виде. Не клади его в репозиторий и удали
# с промежуточной машины после переноса.
set -euo pipefail

DATA_DIR=/opt/wega/data
SERVER_DIR=/opt/wega/server
CONFIG="$SERVER_DIR/server_config.toml"
SERVICE=wega

die() { printf '\033[31mОШИБКА: %s\033[0m\n' "$1" >&2; exit 1; }
info() { printf '\033[1m▶ %s\033[0m\n' "$1"; }
ok() { printf '\033[32m  ✓ %s\033[0m\n' "$1"; }

find_db() {
    ls "$DATA_DIR"/*.db "$DATA_DIR"/*.sqlite 2>/dev/null | head -1
}

backup() {
    [[ $EUID -eq 0 ]] || die "запускать от root"
    [[ -d "$DATA_DIR" ]] || die "нет каталога данных $DATA_DIR — это точно боевой сервер?"

    local stamp archive db was_running=0
    stamp=$(date +%Y%m%d-%H%M%S)
    archive="/root/wega_backup_${stamp}.tar.gz"

    # Сервер обязательно останавливаем: копировать живую SQLite нельзя — часть свежих
    # записей лежит в -wal и в снимок не попадёт, а в худшем случае копия окажется битой.
    if systemctl is-active --quiet "$SERVICE"; then
        was_running=1
        info "Останавливаю $SERVICE (иначе снимок БД будет неконсистентным)"
        systemctl stop "$SERVICE"
        sleep 2
        ok "остановлен"
    fi

    db=$(find_db)
    if [[ -n "$db" ]]; then
        info "Проверяю целостность БД: $(basename "$db")"
        local check
        check=$(sqlite3 "$db" "PRAGMA integrity_check;" 2>&1 | head -1)
        [[ "$check" == "ok" ]] || die "БД повреждена ($check) — переносить нельзя, сначала чинить"
        ok "целостность в порядке"

        # WAL сливаем в основной файл, чтобы архив был самодостаточным.
        sqlite3 "$db" "PRAGMA wal_checkpoint(TRUNCATE);" >/dev/null 2>&1 || true

        printf '  персонажей: %s | игроков: %s | банов: %s\n' \
            "$(sqlite3 "$db" 'SELECT COUNT(*) FROM profile;' 2>/dev/null || echo '?')" \
            "$(sqlite3 "$db" 'SELECT COUNT(*) FROM player;' 2>/dev/null || echo '?')" \
            "$(sqlite3 "$db" 'SELECT COUNT(*) FROM server_ban;' 2>/dev/null || echo '?')"
    else
        printf '\033[33m  ВНИМАНИЕ: файл БД не найден в %s\033[0m\n' "$DATA_DIR"
    fi

    [[ -f "$CONFIG" ]] || printf '\033[33m  ВНИМАНИЕ: нет %s — API-ключ и настройки не попадут в архив\033[0m\n' "$CONFIG"

    info "Собираю архив"
    # Конфиг кладём рядом с данными в массиве аргументов, а не строкой:
    # подстановка строки внутрь tar ломается на пробелах в путях.
    local tar_args=(--exclude='media_player' -C "$DATA_DIR" .)
    [[ -f "$CONFIG" ]] && tar_args+=(-C "$SERVER_DIR" server_config.toml)
    tar czf "$archive" "${tar_args[@]}"
    ok "$archive ($(du -h "$archive" | cut -f1))"

    sha256sum "$archive" | tee "${archive}.sha256"

    if [[ $was_running -eq 1 ]]; then
        printf '\n\033[33mСервер НЕ запущен обратно намеренно: всё, что игроки сделают после снимка,\n'
        printf 'на новый VPS не попадёт. Запусти обратно только если передумал переезжать:\n'
        printf '  systemctl start %s\033[0m\n' "$SERVICE"
    fi

    printf '\nДальше:\n  scp root@$(hostname -I | awk "{print \$1}"):%s .\n' "$archive"
}

restore() {
    [[ $EUID -eq 0 ]] || die "запускать от root"
    local archive="${1:-}"
    [[ -n "$archive" && -f "$archive" ]] || die "укажи путь к архиву: ./migrate_server.sh restore /root/wega_backup_*.tar.gz"
    [[ -d "$SERVER_DIR" ]] || die "нет $SERVER_DIR — сначала разверни код по DEPLOY.md (§3), потом restore"

    if [[ -f "${archive}.sha256" ]]; then
        info "Сверяю контрольную сумму"
        (cd "$(dirname "$archive")" && sha256sum -c "$(basename "$archive").sha256") || die "архив побился при передаче"
        ok "сумма совпала"
    fi

    systemctl is-active --quiet "$SERVICE" && { info "Останавливаю $SERVICE"; systemctl stop "$SERVICE"; }

    # Если на новом VPS уже успели наиграть — не затираем молча.
    local existing
    existing=$(find_db)
    if [[ -n "$existing" ]]; then
        local aside="${existing}.before-restore-$(date +%s)"
        mv "$existing" "$aside"
        printf '\033[33m  Существующая БД отодвинута в %s\033[0m\n' "$aside"
    fi

    info "Распаковываю"
    mkdir -p "$DATA_DIR"
    tar xzf "$archive" -C "$DATA_DIR"

    # Конфиг лежит в архиве рядом с данными — перекладываем на своё место.
    if [[ -f "$DATA_DIR/server_config.toml" ]]; then
        mv "$DATA_DIR/server_config.toml" "$CONFIG"
        chmod 600 "$CONFIG"
        ok "конфиг восстановлен (chmod 600 — в нём API-ключ)"
    fi

    local db
    db=$(find_db)
    if [[ -n "$db" ]]; then
        local check
        check=$(sqlite3 "$db" "PRAGMA integrity_check;" 2>&1 | head -1)
        [[ "$check" == "ok" ]] || die "восстановленная БД повреждена ($check)"
        printf '  персонажей: %s | игроков: %s | банов: %s\n' \
            "$(sqlite3 "$db" 'SELECT COUNT(*) FROM profile;' 2>/dev/null || echo '?')" \
            "$(sqlite3 "$db" 'SELECT COUNT(*) FROM player;' 2>/dev/null || echo '?')" \
            "$(sqlite3 "$db" 'SELECT COUNT(*) FROM server_ban;' 2>/dev/null || echo '?')"
        ok "БД на месте и целая"
    fi

    printf '\n\033[32mГотово.\033[0m Проверь числа выше — они должны совпасть с теми, что показал backup.\n'
    printf 'Запуск:  systemctl start %s  &&  systemctl status %s\n' "$SERVICE" "$SERVICE"
}

case "${1:-}" in
    backup)  backup ;;
    restore) restore "${2:-}" ;;
    *) printf 'Использование:\n  %s backup                 # на СТАРОМ VPS\n  %s restore <архив>        # на НОВОМ VPS\n' "$0" "$0"; exit 1 ;;
esac
