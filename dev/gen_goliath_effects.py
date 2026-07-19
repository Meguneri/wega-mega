#!/usr/bin/env python3
"""Генератор эффектов арена-босса «Голиаф» (dev/gen_goliath_effects.py).

Один RSI (_Wega/Effects/goliath.rsi) с двумя стейтами:
  * warning — телеграф опасной зоны (слэм/линия чарджа): пульсирующая полупрозрачная
    красно-оранжевая плитка с рамкой, 2 кадра;
  * frost — морозный след фазы 2: голубоватая полупрозрачная наледь с «трещинками»
    (детерминированный узор, без рантайм-рандома).

Спрайт под правило форка: PNG руками не редактировать — править генератор и перезапускать.

Запуск из корня репозитория:
    python3 dev/gen_goliath_effects.py
"""
import json
import os

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DST = os.path.join(ROOT, "Resources", "Textures", "_Wega", "Effects", "goliath.rsi")


def warning_frame(alpha_body, alpha_rim):
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = img.load()
    for y in range(32):
        for x in range(32):
            rim = x < 2 or x >= 30 or y < 2 or y >= 30
            if rim:
                px[x, y] = (255, 96, 40, alpha_rim)
            else:
                # лёгкая диагональная штриховка, чтобы зона читалась и на светлом полу
                hatch = (x + y) % 8 < 2
                a = alpha_body + (28 if hatch else 0)
                px[x, y] = (232, 64, 32, min(a, 255))
    return img


def frost_frame():
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = img.load()
    # детерминированный псевдошум без random — по хешу координат
    for y in range(32):
        for x in range(32):
            n = (x * 7349 + y * 9151) % 97
            if n < 62:
                a = 70 + n % 40
                px[x, y] = (168, 214, 244, a)
            elif n < 70:
                px[x, y] = (226, 244, 255, 130)  # искристые точки
    # пара «трещин»
    for i in range(32):
        y1 = (i * 3 + 5) % 32
        px[i, y1] = (120, 170, 210, 150)
        px[(i * 2 + 9) % 32, (i + 20) % 32] = (120, 170, 210, 140)
    return img


def main():
    os.makedirs(DST, exist_ok=True)

    # warning: 2 кадра пульсации в одном листе 64x32
    sheet = Image.new("RGBA", (64, 32), (0, 0, 0, 0))
    sheet.paste(warning_frame(96, 235), (0, 0))
    sheet.paste(warning_frame(52, 160), (32, 0))
    sheet.save(os.path.join(DST, "warning.png"))

    frost_frame().save(os.path.join(DST, "frost.png"))

    meta = {
        "version": 1,
        "size": {"x": 32, "y": 32},
        "license": "CC-BY-SA-3.0",
        "copyright": "Drawn for wega-mega (dev/gen_goliath_effects.py).",
        "states": [
            {"name": "warning", "directions": 1, "delays": [[0.25, 0.25]]},
            {"name": "frost", "directions": 1},
        ],
    }
    with open(os.path.join(DST, "meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=4, ensure_ascii=False)
        f.write("\n")
    print("готово:", DST)


if __name__ == "__main__":
    main()
