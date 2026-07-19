#!/usr/bin/env python3
"""Генератор эффектов арена-босса «Пепельная танцовщица» (dev/gen_dancer_effects.py).

Один RSI (_Wega/Effects/dancer.rsi):
  * ember — тлеющий сектор после вращения (фаза 2): пепельная база, угли, искры; 2 кадра мерцания;
  * ash   — вспышка пепла при телепорте: расходящееся серое облачко, 2 кадра.

Узор детерминированный (хеш координат), без рантайм-рандома.
Спрайт под правило форка: PNG руками не редактировать — править генератор и перезапускать.

Запуск из корня репозитория:
    python3 dev/gen_dancer_effects.py
"""
import json
import os

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DST = os.path.join(ROOT, "Resources", "Textures", "_Wega", "Effects", "dancer.rsi")


def ember_frame(seed):
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = img.load()
    for y in range(32):
        for x in range(32):
            n = (x * 7349 + y * 9151 + seed * 331) % 101
            if n < 40:
                px[x, y] = (24, 16, 14, 150)      # пепельная база
            elif n < 52:
                px[x, y] = (140, 44, 10, 180)     # тлеющие угли
            elif n < 58:
                px[x, y] = (255, 150, 40, 210)    # яркие искры
    return img


def ash_frame(scale):
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = img.load()
    cx = cy = 16
    for y in range(32):
        for x in range(32):
            d = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5
            n = (x * 8117 + y * 6011) % 23
            if d < scale and n < 15:
                a = max(0, int(190 - d * 14))
                g = 120 + n * 4
                px[x, y] = (g, g - 8, g - 4, a)
    return img


def main():
    os.makedirs(DST, exist_ok=True)

    sheet = Image.new("RGBA", (64, 32), (0, 0, 0, 0))
    sheet.paste(ember_frame(0), (0, 0))
    sheet.paste(ember_frame(7), (32, 0))
    sheet.save(os.path.join(DST, "ember.png"))

    sheet = Image.new("RGBA", (64, 32), (0, 0, 0, 0))
    sheet.paste(ash_frame(9), (0, 0))
    sheet.paste(ash_frame(14), (32, 0))
    sheet.save(os.path.join(DST, "ash.png"))

    meta = {
        "version": 1,
        "size": {"x": 32, "y": 32},
        "license": "CC-BY-SA-3.0",
        "copyright": "Drawn for wega-mega (dev/gen_dancer_effects.py).",
        "states": [
            {"name": "ember", "directions": 1, "delays": [[0.3, 0.3]]},
            {"name": "ash", "directions": 1, "delays": [[0.15, 0.15]]},
        ],
    }
    with open(os.path.join(DST, "meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=4, ensure_ascii=False)
        f.write("\n")
    print("готово:", DST)


if __name__ == "__main__":
    main()
