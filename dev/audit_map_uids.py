"""Проверить уникальность UID сущностей карт без перезаписи YAML."""
import collections
import pathlib
import re
import subprocess
import argparse

UID = re.compile(r"^ *- uid: (\d+)\s*$", re.M)
PROTO = re.compile(r"^- proto: *(.*)$", re.M)


def entries(text):
    protos = list(PROTO.finditer(text))
    proto_index = 0
    for match in UID.finditer(text):
        while proto_index + 1 < len(protos) and protos[proto_index + 1].start() < match.start():
            proto_index += 1
        proto = protos[proto_index].group(1) if protos and protos[proto_index].start() < match.start() else ""
        yield int(match.group(1)), proto, match


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument('--history', action='store_true')
    args = parser.parse_args()
    total = checked = 0
    for path in sorted(p for p in pathlib.Path("Resources/Maps").rglob('*')
                       if p.suffix.lower() in ('.yml', '.yaml')):
        checked += 1
        text = path.read_text(encoding="utf-8")
        groups = collections.defaultdict(list)
        for uid, proto, _ in entries(text):
            groups[uid].append(proto)
        duplicates = {uid: protos for uid, protos in groups.items() if len(protos) > 1}
        if duplicates:
            total += 1
            print(path, len(duplicates), dict(list(duplicates.items())[:3]), flush=True)
            if args.history:
                for ref in ['7ebe6eec86^1', '7ebe6eec86^2']:
                    result = subprocess.run(['git', 'show', f'{ref}:{path.as_posix()}'], capture_output=True)
                    if result.returncode:
                        continue
                    old = list(entries(result.stdout.decode('utf-8')))
                    counts = collections.Counter(uid for uid, _, _ in old)
                    print(ref, 'entries', len(old), 'duplicates', sum(n > 1 for n in counts.values()), flush=True)
    print("Maps checked:", checked, "; maps with duplicate entity IDs:", total)
    raise SystemExit(bool(total))
