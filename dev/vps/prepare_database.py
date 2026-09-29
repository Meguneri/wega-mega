"""Создать согласованную SQLite-копию; проверить аккаунты и выдать указанные права."""
import argparse
from contextlib import closing
import json
from pathlib import Path
import re
import sqlite3
import urllib.parse
import urllib.request
import uuid


def snapshot(source: Path, output: Path):
    if not source.is_file():
        raise ValueError(f"Database not found: {source}")
    if output.exists():
        raise ValueError(f"Refusing to overwrite: {output}")
    with closing(sqlite3.connect(source.resolve().as_uri() + "?mode=ro", uri=True)) as src:
        with closing(sqlite3.connect(output)) as dst:
            src.backup(dst)
            if dst.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
                raise ValueError("SQLite integrity check failed")


def prepare(source: Path, output: Path, admins: list[str], flags_path: Path, lookup):
    snapshot(source, output)
    flags = re.findall(r"^\s*(\w+)\s*=\s*1(?:u)?\s*<<", flags_path.read_text(encoding="utf-8"), re.M)
    if "Host" not in flags:
        raise ValueError("Host flag missing from source")
    with closing(sqlite3.connect(output)) as db, db:
        db.execute("PRAGMA foreign_keys=ON")
        owners = db.execute("SELECT p.user_id,u.last_seen_user_name,COUNT(c.profile_id) FROM preference p LEFT JOIN player u ON u.user_id=p.user_id LEFT JOIN profile c ON c.preference_id=p.preference_id GROUP BY p.preference_id").fetchall()
        accounts = {}
        for uid, name, count in owners:
            if not name:
                raise ValueError(f"Owner has no recorded nickname: {uid}")
            account = lookup(name)
            if uuid.UUID(account["userId"]) != uuid.UUID(uid):
                raise ValueError(f"{name}: offline ID differs from verified account; manual migration required")
            accounts[name.casefold()] = uid
            print(f"{name}: {count} profiles, verified account")
        for name in admins:
            key = name.casefold()
            uid = accounts.get(key)
            if uid is None:
                account = lookup(name)
                uid = str(uuid.UUID(account["userId"])).upper()
            db.execute("INSERT INTO admin(user_id,title,admin_rank_id,deadminned,suspended) VALUES(?,?,NULL,0,0) ON CONFLICT(user_id) DO UPDATE SET deadminned=0,suspended=0", (uid, "Host"))
            for flag in map(str.upper, flags):
                if db.execute("SELECT 1 FROM admin_flag WHERE admin_id=? AND flag=?", (uid, flag)).fetchone():
                    db.execute("UPDATE admin_flag SET negative=0 WHERE admin_id=? AND flag=?", (uid, flag))
                else:
                    db.execute("INSERT INTO admin_flag(flag,negative,admin_id) VALUES(?,0,?)", (flag, uid))
            print(f"{name}: all {len(flags)} admin flags")
        if db.execute("PRAGMA foreign_key_check").fetchall():
            raise ValueError("Foreign key check failed")


def lookup_account(name):
    url = "https://auth.spacestation14.com/api/query/name?" + urllib.parse.urlencode({"name": name})
    request = urllib.request.Request(url, headers={"User-Agent": "Wega-Deploy/1.0"})
    with urllib.request.urlopen(request, timeout=20) as response:
        account = json.load(response)
    if account["userName"].casefold() != name.casefold():
        raise ValueError("Authentication service returned a different name")
    return account


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--database", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--admins", nargs="*", default=[])
    args = p.parse_args()
    root = Path(__file__).resolve().parents[2]
    prepare(args.database, args.output, args.admins, root / "Content.Shared/Administration/AdminFlags.cs", lookup_account)
