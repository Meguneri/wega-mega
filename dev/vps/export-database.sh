#!/bin/bash
# Создаёт согласованную копию активной SQLite-БД; не останавливает сервер.
set -euo pipefail
umask 077
source_db=/var/lib/wega/data/preferences.db
output=${1:?Pass a new output file}
if [ ! -f "$source_db" ] || [ -e "$output" ]; then
  echo 'Source missing or output exists; refusing to overwrite.' >&2
  exit 1
fi
python3 - "$source_db" "$output" <<'PY'
import sqlite3
import sys
import hashlib
from contextlib import closing

source, target = sys.argv[1:]
with closing(sqlite3.connect(f"file:{source}?mode=ro", uri=True)) as src:
    with closing(sqlite3.connect(target)) as dst:
        src.execute("BEGIN")
        src.backup(dst)
        assert dst.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
        assert not dst.execute("PRAGMA foreign_key_check").fetchall()
        # Сравниваем полное содержимое, включая все атрибуты и связанные таблицы.
        def digest(db):
            result = hashlib.sha256()
            for statement in db.iterdump():
                result.update(statement.encode("utf-8"))
                result.update(b"\n")
            return result.digest()
        assert digest(src) == digest(dst), "Snapshot changed database contents"
        print("Profiles:", dst.execute("SELECT COUNT(*) FROM profile").fetchone()[0])
        print("Admins:", dst.execute("SELECT COUNT(*) FROM admin").fetchone()[0])
PY
chmod 600 "$output"
