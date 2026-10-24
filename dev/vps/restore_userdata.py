"""Восстановить приватные карты/автосохранения; база переносится отдельно."""
from pathlib import Path, PurePosixPath
import tarfile
import sys


def restore(archive_path, destination):
    destination = Path(destination)
    with tarfile.open(archive_path, 'r:gz') as archive:
        members = archive.getmembers()
        for member in members:
            path = PurePosixPath(member.name)
            if path.is_absolute() or '..' in path.parts or not path.parts:
                raise ValueError('Invalid archive path')
            if not (member.isfile() or member.isdir()):
                raise ValueError('Only regular files and directories are supported')
            if path.parts[0].startswith('preferences.') or path.parts[0] in (
                    'update-pending', 'update-available', 'media_player'):
                raise ValueError('Database and runtime cache must not be restored through this archive')
            target = destination.joinpath(*path.parts)
            if target.exists() or target.is_symlink():
                raise FileExistsError(target)
            if not target.resolve().is_relative_to(destination.resolve()):
                raise ValueError('Archive escapes destination')
        destination.mkdir(parents=True, exist_ok=True)
        archive.extractall(destination, members=members, filter='data')


if __name__ == '__main__':
    restore(*sys.argv[1:])
