"""Проверить YAML-контент на повторные ключи (требуется PyYAML)."""
import argparse
from pathlib import Path

import yaml


def signature(node):
    if isinstance(node, yaml.ScalarNode):
        return node.tag, node.value
    if isinstance(node, yaml.SequenceNode):
        return node.tag, tuple(signature(v) for v in node.value)
    return node.tag, tuple((signature(k), signature(v)) for k, v in node.value)


def duplicates(node, visited):
    if node is None or id(node) in visited:
        return
    visited.add(id(node))
    if isinstance(node, yaml.MappingNode):
        keys = {}
        for key, value in node.value:
            ident = signature(key)
            if ident in keys:
                yield key, value, signature(keys[ident]) == signature(value)
            else:
                keys[ident] = value
            yield from duplicates(value, visited)
    elif isinstance(node, yaml.SequenceNode):
        for value in node.value:
            yield from duplicates(value, visited)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', nargs='?', type=Path, default=Path('Resources'))
    parser.add_argument('--fix-identical', action='store_true')
    args = parser.parse_args()
    files = count = errors = 0
    for path in sorted(args.root.rglob('*')):
        if not path.is_file() or path.suffix.lower() not in ('.yml', '.yaml'):
            continue
        files += 1
        raw = path.read_bytes()
        try:
            nodes = yaml.compose_all(raw.decode('utf-8-sig'), Loader=yaml.CSafeLoader)
            found = [item for node in nodes for item in duplicates(node, set())]
        except yaml.YAMLError as exc:
            print(f'{path}: {exc}', flush=True)
            errors += 1
            continue
        remove = set()
        text_lines = raw.decode('utf-8-sig').splitlines()
        for key, value, same in found:
            count += 1
            print(f'{path}:{key.start_mark.line + 1}: {key.value} '
                  f'({"identical" if same else "CONFLICT"})', flush=True)
            # Автоматически удаляем только отдельную строку со скалярным значением.
            # Inline-словари и многострочные значения требуют ручного исправления.
            can_fix = (same and isinstance(value, yaml.ScalarNode)
                       and value.end_mark.line == key.start_mark.line
                       and not text_lines[key.start_mark.line][:key.start_mark.column].strip())
            if args.fix_identical and can_fix:
                end = value.end_mark.line + bool(value.end_mark.column)
                remove.update(range(key.start_mark.line, end))
            else:
                errors += 1
        if remove:
            lines = raw.splitlines(keepends=True)
            path.write_bytes(b''.join(line for i, line in enumerate(lines) if i not in remove))
    print(f'Files: {files}; duplicate keys: {count}; unresolved errors: {errors}', flush=True)
    return bool(errors)


if __name__ == '__main__':
    raise SystemExit(main())
