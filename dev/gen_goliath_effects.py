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


def noise(x, y, salt=0):
    """Детерминированный «рассыпанный» шум 0..255 (линейный хеш давал муар-полосы)."""
    h = (x * 374761393 + y * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    h = (h ^ (h >> 13)) * 1274126177 & 0xFFFFFFFF
    return (h ^ (h >> 16)) & 0xFF


def dust_frame(spread, alpha):
    """Клуб пыли от удара: серо-бежевые хлопья, расходящиеся от центра."""
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = img.load()
    cx = cy = 16
    for y in range(32):
        for x in range(32):
            d = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5
            n = noise(x, y, 3)
            if d < spread and n < 115:
                a = max(0, int(alpha - d * 9))
                g = 150 + n % 24
                px[x, y] = (g, g - 12, g - 26, a)
    return img


def rubble_frame():
    """Обломки на месте сорванной плиты: тёмная выбоина, крошка и пара крупных сколов."""
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = img.load()
    cx = cy = 16
    for y in range(32):
        for x in range(32):
            d = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5
            n = noise(x, y, 7)
            # к краям тайла выбоина сходит на нет — плита выломана в середине
            edge = max(0.0, 1.0 - d / 15.0)
            if n < 90 * edge + 20:
                px[x, y] = (44, 40, 38, int(70 + 90 * edge))     # тень провала
            elif n < 150 * edge + 30:
                px[x, y] = (92, 86, 82, int(90 + 80 * edge))     # крошка
    # крупные сколы плиты — короткие светлые грани
    for i in range(11):
        sx, sy = 7 + (noise(i, 1, 11) % 18), 6 + (noise(i, 2, 13) % 20)
        for k in range(noise(i, 3, 17) % 4 + 2):
            px[min(31, sx + k), min(31, sy + (k // 2))] = (150, 142, 134, 205)
    return img


def shock_frame(inner, outer):
    """Фронт ударной волны: кольцо пыли и трещин на тайле."""
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = img.load()
    cx = cy = 16
    for y in range(32):
        for x in range(32):
            d = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5
            if inner <= d <= outer:
                n = (x * 7043 + y * 6301) % 31
                a = 210 - int(abs(d - (inner + outer) / 2) * 40)
                px[x, y] = (206, 178, 140, max(60, a)) if n < 20 else (238, 214, 176, max(50, a - 40))
    return img


def main():
    os.makedirs(DST, exist_ok=True)

    # warning: 2 кадра пульсации в одном листе 64x32
    sheet = Image.new("RGBA", (64, 32), (0, 0, 0, 0))
    sheet.paste(warning_frame(96, 235), (0, 0))
    sheet.paste(warning_frame(52, 160), (32, 0))
    sheet.save(os.path.join(DST, "warning.png"))

    frost_frame().save(os.path.join(DST, "frost.png"))
    rubble_frame().save(os.path.join(DST, "rubble.png"))

    # dust: 3 кадра — клуб растёт и тает
    sheet = Image.new("RGBA", (96, 32), (0, 0, 0, 0))
    for i, (spread, alpha) in enumerate([(7, 200), (12, 150), (16, 90)]):
        sheet.paste(dust_frame(spread, alpha), (i * 32, 0))
    sheet.save(os.path.join(DST, "dust.png"))

    # shock: 3 кадра — кольцо расходится
    sheet = Image.new("RGBA", (96, 32), (0, 0, 0, 0))
    for i, (inner, outer) in enumerate([(2, 7), (6, 12), (10, 16)]):
        sheet.paste(shock_frame(inner, outer), (i * 32, 0))
    sheet.save(os.path.join(DST, "shock.png"))

    meta = {
        "version": 1,
        "size": {"x": 32, "y": 32},
        "license": "CC-BY-SA-3.0",
        "copyright": "Drawn for wega-mega (dev/gen_goliath_effects.py).",
        "states": [
            {"name": "warning", "directions": 1, "delays": [[0.25, 0.25]]},
            {"name": "frost", "directions": 1},
            {"name": "rubble", "directions": 1},
            {"name": "dust", "directions": 1, "delays": [[0.12, 0.12, 0.16]]},
            {"name": "shock", "directions": 1, "delays": [[0.1, 0.1, 0.12]]},
        ],
    }
    with open(os.path.join(DST, "meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=4, ensure_ascii=False)
        f.write("\n")
    print("готово:", DST)


if __name__ == "__main__":
    main()
