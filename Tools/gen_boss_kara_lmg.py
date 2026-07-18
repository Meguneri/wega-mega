#!/usr/bin/env python3
"""Генератор уникального спрайта пулемёта арена-босса «Каратель» (WeaponBossArenaKara).

Берёт ВСЮ геометрию стокового L6 (Objects/Weapons/Guns/LMGs/l6.rsi — силуэт читается как LMG)
и перекрашивает в боссовскую гамму:
  * металл — тёмная холодная сталь (gunmetal), чтобы отличался от «коричневого» стокового L6;
  * дерево/тёплые части (приклад, цевьё, рукоять) — глубокий кримсон, фирменный цвет босса.

Обрабатываются все кадры RSI (icon/base/bolt-open/mag-*/inhand-*/wielded-*/equipped-*),
meta.json копируется с сохранением атрибуции оригинала. Заодно собирается превью-монтаж
«было → стало» (dev/boss_kara_preview.png) — показать пользователю после генерации.

Спрайт под правило форка: PNG руками не редактировать — править генератор и перезапускать.

Запуск из корня репозитория:
    python3 Tools/gen_boss_kara_lmg.py
"""
import json
import os

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Resources", "Textures", "Objects", "Weapons", "Guns", "LMGs", "l6.rsi")
DST = os.path.join(ROOT, "Resources", "Textures", "_Wega", "Objects", "Weapons", "Guns", "boss_kara.rsi")
PREVIEW = os.path.join(ROOT, "dev", "boss_kara_preview.png")

# --- палитра ---
STEEL_DARK = (14, 16, 20)      # металл, глубокая тень
STEEL_LIGHT = (148, 156, 166)  # металл, блик
CRIMSON_DARK = (66, 10, 16)    # кримсон, тень
CRIMSON_LIGHT = (218, 44, 52)  # кримсон, блик


def ramp(lum, dark, light):
    """Линейная рампа dark→light по яркости исходного пикселя (0..1)."""
    return (
        int(dark[0] + (light[0] - dark[0]) * lum),
        int(dark[1] + (light[1] - dark[1]) * lum),
        int(dark[2] + (light[2] - dark[2]) * lum),
    )


def recolor(src_img):
    """Перекрашивает кадр L6: тёплое дерево → кримсон, холодный/серый металл → тёмная сталь."""
    src = src_img.convert("RGBA")
    out = Image.new("RGBA", src.size, (0, 0, 0, 0))
    sp, op = src.load(), out.load()
    w, h = src.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = sp[x, y]
            if a == 0:
                continue
            # Перцептивная яркость исходника — водит рампу, форма подсветки сохраняется.
            lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0
            # Дерево/тёплые части L6 (приклад, цевьё): красный канал заметно впереди синего.
            warm = r > 70 and r > b + 20 and r >= g
            if warm:
                op[x, y] = ramp(lum, CRIMSON_DARK, CRIMSON_LIGHT) + (255,)
            else:
                op[x, y] = ramp(lum, STEEL_DARK, STEEL_LIGHT) + (255,)
    return out


def first_frame(img):
    """Первый кадр 4-направленного листа (для превью)."""
    return img.crop((0, 0, 32, 32))


def build_preview(pairs):
    """Монтаж «было → стало»: сверху стоковый L6, снизу «Каратель», увеличение x8."""
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


def main():
    os.makedirs(DST, exist_ok=True)

    names = [n for n in os.listdir(SRC) if n.endswith(".png")]
    preview_pairs = []
    for name in sorted(names):
        old = Image.open(os.path.join(SRC, name))
        new = recolor(old)
        new.save(os.path.join(DST, name))
        if name in ("icon.png", "base.png", "bolt-open.png", "mag-3.png",
                    "inhand-left.png", "wielded-inhand-left.png", "equipped-BACKPACK.png"):
            preview_pairs.append((first_frame(old), first_frame(new)))

    # meta.json: стейты те же, атрибуция — оригинал + перекраска генератором.
    with open(os.path.join(SRC, "meta.json"), encoding="utf-8") as f:
        meta = json.load(f)
    meta["copyright"] = (
        "Recolored (boss gunmetal/crimson) by Tools/gen_boss_kara_lmg.py from "
        "Objects/Weapons/Guns/LMGs/l6.rsi: " + meta.get("copyright", "")
    )
    with open(os.path.join(DST, "meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=4, ensure_ascii=False)
        f.write("\n")

    build_preview(preview_pairs)
    print("готово:", DST)
    print("превью:", PREVIEW)


if __name__ == "__main__":
    main()
