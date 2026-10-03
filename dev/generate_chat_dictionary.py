"""Собрать компактный словарь из danakt/russian-words (MIT, windows-1251)."""
import pathlib
import struct
import sys


def fingerprint(word):
    value = 14695981039346656037
    for char in word.lower().replace('ё', 'е'):
        value = ((value ^ ord(char)) * 1099511628211) & 0xffffffffffffffff
    return value


if __name__ == '__main__':
    source = pathlib.Path(sys.argv[1])
    target = pathlib.Path('Resources/_Wega/Spelling/russian.bin')
    hashes = sorted({fingerprint(word.strip()) for word in source.read_text(encoding='cp1251').splitlines()
                     if word.strip()})
    with target.open('wb') as output:
        output.write(struct.pack('<i', len(hashes)))
        for value in hashes:
            output.write(struct.pack('<Q', value))
    print(f'{len(hashes)} word forms; {target.stat().st_size} bytes')
