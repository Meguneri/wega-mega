"""Проверка/нормализация сохранённых вариантов тайлов под текущие определения пола."""
import base64
import collections
from pathlib import Path
import re
import struct
import yaml
from functools import lru_cache


@lru_cache(maxsize=1)
def tile_variants():
    definitions = {}
    for path in Path('Resources/Prototypes').rglob('*.yml'):
        text = path.read_text(encoding='utf-8')
        if re.search(r'^- type: tile\s*$', text, re.M):
            for proto in yaml.load(text, Loader=yaml.CBaseLoader) or []:
                if isinstance(proto, dict) and proto.get('type') == 'tile':
                    definitions[proto['id']] = proto
    def resolve(name, seen=()):
        if name in seen:
            raise ValueError('Tile inheritance cycle: ' + name)
        proto = definitions[name]
        if 'variants' in proto:
            return int(proto['variants'])
        parents = proto.get('parent', [])
        if isinstance(parents, str):
            parents = [parents]
        for parent in parents:
            inherited = resolve(parent, (*seen, name))
            if inherited is not None:
                return inherited
        return None
    limits = {name: resolve(name) or 1 for name in definitions}
    aliases = yaml.load(Path('Resources/Prototypes/Tiles/tile_migrations.yml').read_text(encoding='utf-8'),
                        Loader=yaml.CBaseLoader)
    for alias in aliases:
        if alias.get('type') != 'tileAlias':
            continue
        target = alias['target']
        chain = {a['id']: a['target'] for a in aliases if a.get('type') == 'tileAlias'}
        seen = {alias['id']}
        while target in chain:
            if target in seen:
                raise ValueError('Tile alias cycle: ' + target)
            seen.add(target)
            target = chain[target]
        if target in limits:
            limits[alias['id']] = limits[target]
    return limits


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
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    count = 0
    report = {}
    failures = []
    for path in sorted(args.root.rglob('*.yml')):
        try:
            text, errors = normalize(path.read_text(encoding='utf-8'), args.fix)
        except (ValueError, KeyError, AssertionError) as exc:
            failures.append(str(path))
            print(path, 'ERROR:', exc, flush=True)
            continue
        count += sum(errors.values())
        if errors:
            report[path.as_posix()] = dict(errors)
            print(path, sum(errors.values()), dict(errors), flush=True)
        if args.fix and errors:
            path.write_text(text, encoding='utf-8', newline='\n')
    if args.report:
        import json
        args.report.write_text(json.dumps({'maps': report, 'failures': failures}, ensure_ascii=False, indent=2),
                               encoding='utf-8')
    print('Invalid tile variants:', count, '; affected maps:', len(report), '; failures:', len(failures))
    raise SystemExit(bool(failures) or (bool(count) and not args.fix))
