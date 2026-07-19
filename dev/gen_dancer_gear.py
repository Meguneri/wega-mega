#!/usr/bin/env python3
"""Генератор снаряжения арена-босса «Пепельная танцовщица» (dev/gen_dancer_gear.py).

Быстрый DS3-босс (Танцовщица/Фриде): струящаяся монашеская роба и капюшон перекрашиваются
в чёрный пепел, а светлая оторочка становится РАСКАЛЁННОЙ (тлеющий градиент углей) — на тёмной
арене читается как языки жара по кромке одежды. Катана — угольный клинок с раскалённой кромкой.

  * тёмные тона робы   → угольно-пепельная рампа;
  * светлые тона (оторочка/узор) → ember-рампа (тлеющие угли);
  * клинок катаны (светлая сталь) → раскалённый градиент; рукоять → уголь.

Обрабатываются RSI целиком, meta.json копируется с атрибуцией.
Превью «было → стало»: dev/dancer_gear_preview.png.

Спрайт под правило форка: PNG руками не редактировать — править генератор и перезапускать.

Запуск из корня репозитория:
    python3 dev/gen_dancer_gear.py
"""
import json
import os
import shutil

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEX = os.path.join(ROOT, "Resources", "Textures")
PREVIEW = os.path.join(ROOT, "dev", "dancer_gear_preview.png")

# --- палитра пепла и углей ---
ASH_DARK = (16, 13, 14)
ASH_LIGHT = (86, 78, 80)
EMBER_DARK = (120, 32, 8)
EMBER_LIGHT = (255, 168, 48)

ITEMS = [
    (os.path.join(TEX, "Clothing", "OuterClothing", "Misc", "nunrobe.rsi"),
     os.path.join(TEX, "_Wega", "Clothing", "OuterClothing", "Misc", "dancer_robe.rsi"),
     "robe"),
    (os.path.join(TEX, "Clothing", "Head", "Hoods", "nun.rsi"),
     os.path.join(TEX, "_Wega", "Clothing", "Head", "Hoods", "dancer_hood.rsi"),
     "robe"),
    (os.path.join(TEX, "_Wega", "Objects", "Weapons", "Melee", "katana.rsi"),
     os.path.join(TEX, "_Wega", "Objects", "Weapons", "Melee", "dancer_blade.rsi"),
     "blade"),
]


def ramp(lum, dark, light):
    return (
        int(dark[0] + (light[0] - dark[0]) * lum),
        int(dark[1] + (light[1] - dark[1]) * lum),
        int(dark[2] + (light[2] - dark[2]) * lum),
    )


def luminance(r, g, b):
    return (0.299 * r + 0.587 * g + 0.114 * b) / 255


def recolor_robe(r, g, b):
    """Тёмное → пепел; светлое (оторочка) → раскалённые угли."""
    lum = luminance(r, g, b)
    if lum > 0.62:
        # Оторочка: чем ярче исходник, тем раскалённее.
        t = (lum - 0.62) / 0.38
        return ramp(t, EMBER_DARK, EMBER_LIGHT)
    return ramp(lum / 0.62, ASH_DARK, ASH_LIGHT)


def recolor_blade(r, g, b):
    """Светлая сталь клинка → раскалённый градиент; тёмная рукоять → уголь."""
    lum = luminance(r, g, b)
    if lum > 0.45:
        t = (lum - 0.45) / 0.55
        return ramp(t, EMBER_DARK, EMBER_LIGHT)
    return ramp(lum / 0.45, ASH_DARK, ASH_LIGHT)


MODES = {"robe": recolor_robe, "blade": recolor_blade}


def process_rsi(src, dst, mode):
    os.makedirs(dst, exist_ok=True)
    fn = MODES[mode]
    for name in os.listdir(src):
        path = os.path.join(src, name)
        if name == "meta.json":
            meta = json.load(open(path))
            meta["copyright"] = (meta.get("copyright", "") +
                " | Recolored to Ash Dancer palette for wega-mega (dev/gen_dancer_gear.py).")
            with open(os.path.join(dst, name), "w", encoding="utf-8") as f:
                json.dump(meta, f, indent=4, ensure_ascii=False)
                f.write("\n")
            continue
        if not name.endswith(".png"):
            shutil.copy(path, os.path.join(dst, name))
            continue

        img = Image.open(path).convert("RGBA")
        px = img.load()
        for y in range(img.height):
            for x in range(img.width):
                r, g, b, a = px[x, y]
                if a == 0:
                    continue
                nr, ng, nb = fn(r, g, b)
                px[x, y] = (nr, ng, nb, a)
        img.save(os.path.join(dst, name))


def build_preview():
    tiles = []
    for src, dst, _ in ITEMS:
        for frame in ("icon.png", "equipped-OUTERCLOTHING.png", "equipped-HELMET.png", "equipped-HEAD.png"):
            s, d = os.path.join(src, frame), os.path.join(dst, frame)
            if os.path.exists(s) and os.path.exists(d):
                tiles.append((Image.open(s).convert("RGBA"), Image.open(d).convert("RGBA")))

    if not tiles:
        return
    scale = 6
    cell = 32 * scale
    pad = 10
    w = len(tiles) * (cell + pad) + pad
    h = cell * 2 + pad * 3
    mont = Image.new("RGBA", (w, h), (30, 30, 36, 255))
    x = pad
    for before, after in tiles:
        for row, im in enumerate((before, after)):
            frame0 = im.crop((0, 0, 32, 32)) if im.width > 32 or im.height > 32 else im
            up = frame0.resize((cell, cell), Image.NEAREST)
            mont.alpha_composite(up, (x, pad + row * (cell + pad)))
        x += cell + pad
    mont.convert("RGB").save(PREVIEW)


def main():
    for src, dst, mode in ITEMS:
        process_rsi(src, dst, mode)
        print("готово:", dst)
    build_preview()
    print("превью:", PREVIEW)


if __name__ == "__main__":
    main()
