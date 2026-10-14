"""Собрать компактный словарь из danakt/russian-words (MIT, windows-1251)."""
import argparse
import pathlib
import struct


def fingerprint(word):
    value = 14695981039346656037
    for char in word.lower().replace('ё', 'е'):
        if char in '\u0301\u0300\u00ad':
            continue
        if char in '\u2010\u2011':
            char = '-'
        value = ((value ^ ord(char)) * 1099511628211) & 0xffffffffffffffff
    return value


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=pathlib.Path, nargs='?')
    parser.add_argument('--extend', action='store_true', help='Дополнить готовый словарь без исходного russian.txt')
    args = parser.parse_args()
    if args.extend == (args.source is not None):
        parser.error('Укажите исходный словарь либо --extend')
    target = pathlib.Path('Resources/_Wega/Spelling/russian.bin')
    if args.extend:
        data = target.read_bytes()
        count = struct.unpack_from('<i', data)[0]
        if count < 0 or len(data) != 4 + count * 8:
            raise ValueError('Некорректный готовый словарь')
        hashes = set(struct.unpack_from(f'<{count}Q', data, 4))
    else:
        hashes = {fingerprint(word.strip()) for word in args.source.read_text(encoding='cp1251').splitlines()
                  if word.strip()}
    additional = target.with_name('additional_words.txt')
    for line in additional.read_text(encoding='utf-8').splitlines():
        word = line.split('#', 1)[0].strip()
        if word:
            hashes.add(fingerprint(word))
    hashes = sorted(hashes)
    with target.open('wb') as output:
        output.write(struct.pack('<i', len(hashes)))
        for value in hashes:
            output.write(struct.pack('<Q', value))
    print(f'{len(hashes)} word forms; {target.stat().st_size} bytes')
