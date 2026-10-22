"""Проверка/нормализация сохранённых вариантов тайлов под текущие определения пола."""
import base64
import collections
from pathlib import Path
import re
import struct
import yaml


def tile_variants():
    definitions = {}
    for folder in ['Tiles', 'Corvax/Tiles', '_Wega/Tiles']:
        for path in (Path('Resources/Prototypes') / folder).rglob('*.yml'):
            for proto in yaml.load(path.read_text(encoding='utf-8'), Loader=yaml.CBaseLoader) or []:
                if isinstance(proto, dict) and proto.get('type') == 'tile':
                    definitions[proto['id']] = proto
    def resolve(name, seen=()):
        if name in seen:
            raise ValueError('Tile inheritance cycle: ' + name)
        proto = definitions[name]
        if 'variants' in proto:
            return int(proto['variants'])
        parent = proto.get('parent')
        if isinstance(parent, list):
            parent = parent[0] if parent else None
        return resolve(parent, (*seen, name)) if parent else 1
    return {name: resolve(name) for name in definitions}


def normalize(text, fix=False):
    data = yaml.load(text, Loader=yaml.CBaseLoader)
    mapping = data['tilemap']
    limits = tile_variants()
    replacements = {}
    errors = collections.Counter()
    def visit(node):
        if isinstance(node, dict):
            if 'tiles' in node and 'ind' in node:
                encoded = node['tiles']
                raw = bytearray(base64.b64decode(encoded))
                version = int(node.get('version', 1))
                stride = 7 if version >= 7 else 6 if version >= 6 else 4
                assert len(raw) % stride == 0
                for offset in range(0, len(raw), stride):
                    tile_id = struct.unpack_from('<i' if version >= 6 else '<H', raw, offset)[0]
                    name = mapping[str(tile_id)]
                    limit = limits.get(name)
                    if limit is None:
                        raise ValueError('Unknown tile definition: ' + name)
                    index = offset + (5 if version >= 6 else 3)
                    if raw[index] >= limit:
                        errors[name] += 1
                        raw[index] %= limit
                replacements[encoded] = base64.b64encode(raw).decode('ascii')
            for value in node.values():
                visit(value)
        elif isinstance(node, list):
            for value in node:
                visit(value)
    visit(data)
    if fix:
        text = re.sub(r'(?m)^(\s+tiles: )([A-Za-z0-9+/=]+)$',
                      lambda m: m[1] + replacements.get(m[2], m[2]), text)
    return text, errors


if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('root', type=Path)
    parser.add_argument('--fix', action='store_true')
    args = parser.parse_args()
    count = 0
    for path in sorted(args.root.rglob('*.yml')):
        text, errors = normalize(path.read_text(encoding='utf-8'), args.fix)
        count += sum(errors.values())
        print(path.name, dict(errors), flush=True)
        if args.fix and errors:
            path.write_text(text, encoding='utf-8', newline='\n')
    print('Invalid tile variants:', count)
    raise SystemExit(bool(count) and not args.fix)
