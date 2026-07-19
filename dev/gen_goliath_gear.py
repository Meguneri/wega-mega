#!/usr/bin/env python3
"""Генератор снаряжения арена-босса «Голиаф» (dev/gen_goliath_gear.py).

Тяжёлый рыцарь в духе DS3 (Вордт/Гундир): за основу берётся сапёрный бомб-костюм
(шлем+костюм) и кувалда, перекрашиваемые в гамму «мёрзлой стали»:
  * основной корпус (оливковый/серый)  → тёмная сталь (gunmetal);
  * насыщенные детали (ремни, визор)   → ледяная синева;
  * рукоять кувалды (дерево)           → чернёное железо.

Обрабатываются RSI целиком (icon/equipped/inhand, все расовые варианты), meta.json
копируется с атрибуцией. Превью «было → стало»: dev/goliath_gear_preview.png.

Спрайт под правило форка: PNG руками не редактировать — править генератор и перезапускать.

Запуск из корня репозитория:
    python3 dev/gen_goliath_gear.py
"""
import json
import os
import shutil

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEX = os.path.join(ROOT, "Resources", "Textures")
PREVIEW = os.path.join(ROOT, "dev", "goliath_gear_preview.png")

# --- палитра «мёрзлой стали» ---
STEEL_DARK = (16, 18, 24)
STEEL_LIGHT = (128, 138, 152)
ICE_DARK = (30, 62, 92)
ICE_LIGHT = (140, 200, 240)
IRON_DARK = (10, 10, 12)
IRON_LIGHT = (74, 78, 86)

ITEMS = [
    # (источник, назначение, режим)
    (os.path.join(TEX, "Clothing", "OuterClothing", "Suits", "bombsuit.rsi"),
     os.path.join(TEX, "_Wega", "Clothing", "OuterClothing", "Suits", "goliath.rsi"),
     "armor"),
    (os.path.join(TEX, "Clothing", "Head", "Helmets", "bombsuit.rsi"),
     os.path.join(TEX, "_Wega", "Clothing", "Head", "Helmets", "goliath.rsi"),
     "armor"),
    (os.path.join(TEX, "Objects", "Weapons", "Melee", "sledgehammer.rsi"),
     os.path.join(TEX, "_Wega", "Objects", "Weapons", "Melee", "goliath_hammer.rsi"),
     "hammer"),
]


def ramp(lum, dark, light):
    return (
        int(dark[0] + (light[0] - dark[0]) * lum),
        int(dark[1] + (light[1] - dark[1]) * lum),
        int(dark[2] + (light[2] - dark[2]) * lum),
    )


def luminance(r, g, b):
    return (0.299 * r + 0.587 * g + 0.114 * b) / 255


def saturation(r, g, b):
    mx, mn = max(r, g, b), min(r, g, b)
    return 0 if mx == 0 else (mx - mn) / mx


def recolor_armor(r, g, b):
    """Бомб-костюм: корпус → сталь; насыщенные детали (ремни/визор) → ледяная синева."""
    lum = luminance(r, g, b)
    if saturation(r, g, b) > 0.35:
        return ramp(lum, ICE_DARK, ICE_LIGHT)
    return ramp(lum, STEEL_DARK, STEEL_LIGHT)


def recolor_hammer(r, g, b):
    """Кувалда: тёплая рукоять → чернёное железо; серая голова → светлая мёрзлая сталь."""
    lum = luminance(r, g, b)
    warm = r > b + 15  # дерево/тёплые тона
    if warm:
        return ramp(lum, IRON_DARK, IRON_LIGHT)
    return ramp(min(1.0, lum * 1.15 + 0.08), ICE_DARK, ICE_LIGHT)


MODES = {"armor": recolor_armor, "hammer": recolor_hammer}


def process_rsi(src, dst, mode):
    os.makedirs(dst, exist_ok=True)
    fn = MODES[mode]
    for name in os.listdir(src):
        path = os.path.join(src, name)
        if name == "meta.json":
            meta = json.load(open(path))
            meta["copyright"] = (meta.get("copyright", "") +
                " | Recolored to Goliath frozen-steel palette for wega-mega (dev/gen_goliath_gear.py).")
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
    """Монтаж «было → стало» по иконкам и equipped-кадрам."""
    tiles = []
    for src, dst, _ in ITEMS:
        for frame in ("icon.png", "equipped-OUTERCLOTHING.png", "equipped-HELMET.png"):
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
    mont = Image.new("RGBA", (w, h), (40, 40, 46, 255))
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
