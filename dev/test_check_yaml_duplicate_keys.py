"""Проверка безопасного удаления повторных YAML-ключей."""
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


class DuplicateKeyTests(unittest.TestCase):
    def check(self, text, expected, exit_code):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'map.yml'
            path.write_bytes(text.encode())
            result = subprocess.run(
                [sys.executable, str(Path(__file__).with_name('check_yaml_duplicate_keys.py')),
                 directory, '--fix-identical'], capture_output=True, text=True)
            self.assertEqual(result.returncode, exit_code, result.stdout + result.stderr)
            self.assertEqual(path.read_bytes(), expected.encode())

    def test_identical_parent_preserves_other_fields_and_line_endings(self):
        self.check('- parent: 2\r\n  pos: 1,2\r\n  parent: 2\r\n  rot: 0\r\n',
                   '- parent: 2\r\n  pos: 1,2\r\n  rot: 0\r\n', 0)

    def test_conflicting_coordinates_are_not_removed(self):
        text = 'pos: 1,2\npos: 3,4\n'
        self.check(text, text, 1)

    def test_inline_dictionary_requires_manual_fix(self):
        text = 'transform: {parent: 2, parent: 2}\n'
        self.check(text, text, 1)

    def test_multiline_duplicate_does_not_remove_next_field(self):
        text = 'items:\n  - one\nitems:\n  - one\nnext: keep\n'
        self.check(text, text, 1)


if __name__ == '__main__':
    unittest.main()
