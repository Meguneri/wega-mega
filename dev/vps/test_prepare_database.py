import sqlite3
from contextlib import closing
import tempfile
from pathlib import Path
import unittest

from prepare_database import prepare, snapshot

UID = "CE5DA150-683B-4EAE-9B2E-59DEC887F527"


class DatabaseTransferTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "source.db"
        self.output = self.root / "output.db"
        self.flags = self.root / "flags.cs"
        self.flags.write_text("Admin = 1 << 0,\nHost = 1u << 31,\n", encoding="utf-8")
        self.db = sqlite3.connect(self.source)
        self.addCleanup(self.db.close)
        self.db.executescript("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE player(user_id TEXT PRIMARY KEY,last_seen_user_name TEXT);
            CREATE TABLE preference(preference_id INTEGER PRIMARY KEY,user_id TEXT);
            CREATE TABLE profile(profile_id INTEGER PRIMARY KEY,preference_id INTEGER);
            CREATE TABLE admin(user_id TEXT PRIMARY KEY,title TEXT,admin_rank_id INTEGER,deadminned INTEGER,suspended INTEGER);
            CREATE TABLE admin_flag(admin_flag_id INTEGER PRIMARY KEY,flag TEXT,negative INTEGER,admin_id TEXT REFERENCES admin(user_id));
        """)
        self.db.execute("INSERT INTO player VALUES(?,?)", (UID, "Meguneri"))
        self.db.execute("INSERT INTO preference VALUES(1,?)", (UID,))
        self.db.execute("INSERT INTO profile VALUES(1,1)")
        self.db.commit()

    def test_wal_snapshot_and_full_rights_without_source_changes(self):
        self.db.execute("INSERT INTO profile VALUES(2,1)")
        self.db.commit()
        prepare(self.source, self.output, ["Meguneri"], self.flags,
                lambda _: {"userId": UID.lower(), "userName": "Meguneri"})
        with closing(sqlite3.connect(self.output)) as copy:
            self.assertEqual(copy.execute("SELECT COUNT(*) FROM profile").fetchone()[0], 2)
            self.assertEqual(set(copy.execute("SELECT flag FROM admin_flag")), {("ADMIN",), ("HOST",)})
        self.assertEqual(self.db.execute("SELECT COUNT(*) FROM admin").fetchone()[0], 0)

    def test_existing_output_is_not_overwritten(self):
        snapshot(self.source, self.output)
        original = self.output.read_bytes()
        with self.assertRaises(ValueError):
            snapshot(self.source, self.output)
        self.assertEqual(original, self.output.read_bytes())

    def test_wrong_account_id_aborts_before_granting_rights(self):
        with self.assertRaisesRegex(ValueError, "offline ID differs"):
            prepare(self.source, self.output, ["Meguneri"], self.flags,
                    lambda _: {"userId": "00000000-0000-0000-0000-000000000001"})
        with closing(sqlite3.connect(self.output)) as copy:
            self.assertEqual(copy.execute("SELECT COUNT(*) FROM admin").fetchone()[0], 0)


if __name__ == "__main__":
    unittest.main()
