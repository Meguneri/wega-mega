"""Проверка регистрации PNG-состояний RSI без изменения рисунков и лицензий."""
import argparse
import json
from pathlib import Path
from PIL import Image


def audit(root, fix=False):
    missing = []
    repaired = []
    for folder in sorted(root.rglob('*.rsi')):
        meta = folder / 'meta.json'
        if not meta.exists():
            missing.append(str(meta))
            continue
        data = json.loads(meta.read_text(encoding='utf-8-sig'))
        names = {s['name'] for s in data['states']}
        changed = False
        for png in sorted(folder.glob('*.png')):
            if png.stem in names:
                continue
            with Image.open(png) as image:
                width, height = image.size
            x, y = data['size']['x'], data['size']['y']
            assert width % x == 0 and height % y == 0, png
            frames = width // x * (height // y)
            if frames not in (1, 4, 8):
                raise ValueError(f'Нужна ручная проверка анимации: {png}, frames={frames}')
            state = {'name': png.stem}
            if frames != 1:
                state['directions'] = frames
            missing.append(str(png))
            if fix:
                data['states'].append(state)
                changed = True
                repaired.append(png)
        if changed:
            meta.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return missing, repaired


def preview(paths, output):
    from PIL import ImageDraw
    columns, size = 6, 128
    sheet = Image.new('RGB', (columns * size, ((len(paths) + columns - 1) // columns) * (size + 30)), '#282830')
    draw = ImageDraw.Draw(sheet)
    for i, path in enumerate(paths):
        meta = json.loads((path.parent / 'meta.json').read_text(encoding='utf-8-sig'))
        with Image.open(path) as image:
            frame = image.crop((0, 0, meta['size']['x'], meta['size']['y'])).convert('RGBA')
        frame.thumbnail((size - 8, size - 8))
        frame = frame.resize((size - 8, size - 8), Image.Resampling.NEAREST)
        x, y = i % columns * size, i // columns * (size + 30)
        sheet.paste(frame, (x + 4, y + 4), frame)
        draw.text((x + 4, y + size), path.parent.name[:18], fill='white')
    sheet.save(output)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--fix', action='store_true')
    parser.add_argument('--preview', type=Path)
    args = parser.parse_args()
    errors, repaired = audit(Path('Resources/Textures'), args.fix)
    for error in errors:
        print(error)
    print('Unregistered states/missing metadata:', len(errors), '; repaired:', len(repaired))
    if args.preview and repaired:
        preview(repaired, args.preview)
    raise SystemExit(bool(errors) and not args.fix)
