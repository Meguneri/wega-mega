#!/bin/bash
# Снимок данных для переезда; работающий игровой процесс не останавливается.
set -euo pipefail
umask 077
output=${1:?Pass a new output directory}
[ ! -e "$output" ] || { echo 'Output already exists.' >&2; exit 1; }
mkdir -m 700 -- "$output"
python3 - "$output" <<'PY'
import datetime
from contextlib import closing
import hashlib
import json
from pathlib import Path
import sqlite3
import shutil
import sys
import tarfile

output = Path(sys.argv[1])
data = Path('/var/lib/wega/data')
with closing(sqlite3.connect(f'file:{data}/preferences.db?mode=ro', uri=True)) as source:
    with closing(sqlite3.connect(output / 'preferences.live.db')) as target:
        source.backup(target)
        assert target.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
        assert not target.execute('PRAGMA foreign_key_check').fetchall()
for name in ('server_config.toml', 'deploy.env'):
    shutil.copyfile(Path('/etc/wega') / name, output / name)
with tarfile.open(output / 'userdata.tar.gz', 'w:gz') as archive:
    for path in sorted(data.rglob('*')):
        relative = path.relative_to(data)
        if relative.parts[0] == 'media_player' or relative.parts[0].startswith('preferences.'):
            continue
        if relative.parts[0] in ('update-pending', 'update-available'):
            continue
        if path.is_symlink():
            raise ValueError(f'Unexpected symbolic link: {relative}')
        if path.is_file():
            before = path.stat()
            archive.add(path, arcname=str(relative), recursive=False)
            after = path.stat()
            if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
                raise RuntimeError(f'File changed during export, retry snapshot: {relative}')
manifest = {
    'saved_on': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'attributes_verified': True,
    'files': {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
              for path in sorted(output.iterdir()) if path.is_file()},
}
(output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
print('Snapshot complete; database integrity and file stability verified.')
PY
