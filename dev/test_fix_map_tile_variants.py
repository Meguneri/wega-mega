"""Проверки миграции вариантов без изменения геометрии и флагов тайлов."""
import base64
import struct
import unittest
from unittest.mock import patch

from fix_map_tile_variants import normalize


class TileVariantTests(unittest.TestCase):
    def test_only_invalid_variant_byte_changes(self):
        raw = struct.pack('<iBBB', 7, 3, 9, 5) + struct.pack('<iBBB', 7, 8, 1, 2)
        encoded = base64.b64encode(raw).decode()
        text = f'tilemap:\n  7: TestFloor\nchunks:\n- ind: 0,0\n  version: 7\n  tiles: {encoded}\n'
        with patch('fix_map_tile_variants.tile_variants', return_value={'TestFloor': 2}):
            fixed, errors = normalize(text, fix=True)
            self.assertEqual(errors, {'TestFloor': 1})
            result = base64.b64decode(fixed.split('tiles: ')[1].strip())
            expected = bytearray(raw)
            expected[5] = 1
            self.assertEqual(result, bytes(expected))
            self.assertFalse(normalize(fixed)[1])

    def test_check_mode_does_not_modify_text(self):
        encoded = base64.b64encode(struct.pack('<HBB', 7, 4, 3)).decode()
        text = f'tilemap:\n  7: TestFloor\nchunks:\n- ind: 0,0\n  tiles: {encoded}\n'
        with patch('fix_map_tile_variants.tile_variants', return_value={'TestFloor': 1}):
            result, errors = normalize(text)
        self.assertEqual(result, text)
        self.assertEqual(errors, {'TestFloor': 1})


if __name__ == '__main__':
    unittest.main()
