#!/usr/bin/env python3
"""Генератор спрайтов комплекта арена-босса «чемпион арены» (Tools/gen_boss_arena_gear.py).

Босс не должен выглядеть как скаучат: стоковые bulletproof-жилет и security-шлем
(те самые, что у скавов) перекрашиваются в гамму «Карателя» — тёмная сталь + кримсон:
  * тёплые тона (красные/коричневые детали)  → кримсон-рампа;
  * белые/светлые полосы (фирменные шевроны) → яркий кримсон;
  * холодный/тёмный корпус (синь, серый)     → тёмная gunmetal-сталь.

Комбинезон генератором НЕ покрыт — он собран прототипом из стокового color.rsi с тинтом
(см. Resources/Prototypes/_Wega/Entities/Clothing/boss_arena_gear.yml).

Обрабатываются оба RSI целиком (icon/equipped/inhand, включая вокс/резоми-варианты),
meta.json копируется с сохранением атрибуции. Превью «было → стало»: dev/boss_gear_preview.png.

Спрайт под правило форка: PNG руками не редактировать — править генератор и перезапускать.

Запуск из корня репозитория:
    python3 Tools/gen_boss_arena_gear.py        # либо dev/.venv-sprites/bin/python
"""
import json
import os

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEX = os.path.join(ROOT, "Resources", "Textures")
PREVIEW = os.path.join(ROOT, "dev", "boss_gear_preview.png")

# (источник, назначение, осветлить гамму, кримсон-окантовка сверху)
ITEMS = [
    (os.path.join(TEX, "Clothing", "OuterClothing", "Armor", "bulletproof.rsi"),
     os.path.join(TEX, "_Wega", "Clothing", "OuterClothing", "Armor", "boss_arena.rsi"),
     0.55, 0.45),
    (os.path.join(TEX, "Clothing", "Head", "Helmets", "security.rsi"),
     os.path.join(TEX, "_Wega", "Clothing", "Head", "Helmets", "boss_arena.rsi"),
     None, None),
]

# --- палитра (как у «Карателя») ---
STEEL_DARK = (14, 16, 20)
STEEL_LIGHT = (148, 156, 166)
CRIMSON_DARK = (66, 10, 16)
CRIMSON_LIGHT = (222, 48, 56)


def ramp(lum, dark, light):
    return (
        int(dark[0] + (light[0] - dark[0]) * lum),
        int(dark[1] + (light[1] - dark[1]) * lum),
        int(dark[2] + (light[2] - dark[2]) * lum),
    )


def recolor(src_img, gamma):
    src = src_img.convert("RGBA")
    out = Image.new("RGBA", src.size, (0, 0, 0, 0))
    sp, op = src.load(), out.load()
    w, h = src.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = sp[x, y]
            if a == 0:
                continue
            lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0
            if gamma is not None:
                # Осветление тёмного исходника (гамма < 1): иначе рампа вся сидит в тени
                # и шейдинг пластин не читается.
                lum = lum ** gamma
            sat = max(r, g, b) - min(r, g, b)
            warm = r > 70 and r > b + 20 and r >= g
            whiteish = sat < 45 and lum > 0.55
            if warm or whiteish:
                op[x, y] = ramp(lum, CRIMSON_DARK, CRIMSON_LIGHT) + (255,)
            else:
                op[x, y] = ramp(lum, STEEL_DARK, STEEL_LIGHT) + (255,)
    return out


def edge_trim(img, top_frac):
    """Кримсон-кант по верхней кромке силуэта: непрозрачный пиксель рядом с прозрачным
    в верхней top_frac-доле bbox'а. Даёт жилету «боссовскую» отделку ворота/плеч."""
    img = img.copy()
    px = img.load()
    w, h = img.size
    bbox = img.getbbox()
    if bbox is None:
        return img
    y_limit = bbox[1] + (bbox[3] - bbox[1]) * top_frac
    trim = []
    for y in range(h):
        for x in range(w):
            if y > y_limit or px[x, y][3] == 0:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and px[nx, ny][3] == 0:
                    trim.append((x, y))
                    break
    for x, y in trim:
        px[x, y] = CRIMSON_LIGHT + (255,)
    return img


def first_frame(img):
    return img.crop((0, 0, 32, 32))


def build_preview(pairs):
    """Монтаж «было → стало»: сверху сток, снизу боссовский комплект, x8."""
    scale = 8
    pad = 2 * scale
    cols = len(pairs)
    w = cols * 32 * scale + (cols + 1) * pad
    h = 2 * 32 * scale + 3 * pad
    sheet = Image.new("RGBA", (w, h), (24, 24, 28, 255))
    for i, (old, new) in enumerate(pairs):
        x0 = pad + i * (32 * scale + pad)
        sheet.paste(old.resize((32 * scale, 32 * scale), Image.NEAREST), (x0, pad))
        sheet.paste(new.resize((32 * scale, 32 * scale), Image.NEAREST), (x0, 2 * pad + 32 * scale))
    sheet.save(PREVIEW)


def process(src_dir, dst_dir, gamma, trim_frac, preview_pairs):
    os.makedirs(dst_dir, exist_ok=True)
    for name in sorted(os.listdir(src_dir)):
        if not name.endswith(".png"):
            continue
        old = Image.open(os.path.join(src_dir, name))
        new = recolor(old, gamma)
        if trim_frac is not None and not name.startswith("inhand"):
            new = edge_trim(new, trim_frac)
        new.save(os.path.join(dst_dir, name))
        if name in ("icon.png", "equipped-OUTERCLOTHING.png", "equipped-HELMET.png"):
            preview_pairs.append((first_frame(old), first_frame(new)))

    with open(os.path.join(src_dir, "meta.json"), encoding="utf-8") as f:
        meta = json.load(f)
    meta["copyright"] = (
        "Recolored (boss gunmetal/crimson) by Tools/gen_boss_arena_gear.py from "
        + os.path.relpath(src_dir, TEX) + ": " + meta.get("copyright", "")
    )
    with open(os.path.join(dst_dir, "meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=4, ensure_ascii=False)
        f.write("\n")


def main():
    preview_pairs = []
    for src_dir, dst_dir, gamma, trim_frac in ITEMS:
        process(src_dir, dst_dir, gamma, trim_frac, preview_pairs)
        print("готово:", dst_dir)
    build_preview(preview_pairs)
    print("превью:", PREVIEW)


if __name__ == "__main__":
    main()
