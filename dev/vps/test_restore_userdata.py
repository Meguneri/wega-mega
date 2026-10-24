import io
from pathlib import Path
import tarfile
import tempfile
import unittest
from restore_userdata import restore


class RestoreUserDataTest(unittest.TestCase):
    def archive(self, root, name, symbolic=False):
        path = Path(root) / 'state.tar.gz'
        with tarfile.open(path, 'w:gz') as archive:
            entry = tarfile.TarInfo(name)
            if symbolic:
                entry.type = tarfile.SYMTYPE
                entry.linkname = '/etc/passwd'
                archive.addfile(entry)
            else:
                entry.size = 4
                archive.addfile(entry, io.BytesIO(b'map\n'))
        return path

    def test_maps_round_trip_without_replacing_database(self):
        with tempfile.TemporaryDirectory() as root:
            target = Path(root) / 'data'
            target.mkdir()
            database = target / 'preferences.db'
            database.write_bytes(b'unchanged')
            restore(self.archive(root, 'Maps/test.yml'), target)
            self.assertEqual((target / 'Maps/test.yml').read_bytes(), b'map\n')
            self.assertEqual(database.read_bytes(), b'unchanged')
            with self.assertRaises(FileExistsError):
                restore(Path(root) / 'state.tar.gz', target)

    def test_rejects_escaping_paths_and_database_overwrite(self):
        for name in ('../outside.yml', '/etc/passwd', 'Maps/../../outside.yml', 'preferences.db'):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as root:
                with self.assertRaises(ValueError):
                    restore(self.archive(root, name), Path(root) / 'data')

    def test_rejects_symbolic_links(self):
        with tempfile.TemporaryDirectory() as root:
            with self.assertRaises(ValueError):
                restore(self.archive(root, 'Maps/link', symbolic=True), Path(root) / 'data')


if __name__ == '__main__':
    unittest.main()
