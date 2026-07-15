#!/usr/bin/env python3
"""Рамка киноэкрана (Wega TV prototype): безель 104x64 (3.25x2 тайла) под видео-слой 96x54.

Видео-слой клиент кладёт поверх спрайта с масштабом 0.6 (160x90 -> 96x54) и смещением +2px вверх,
поэтому «окно» экрана в рамке — ровно 96x54 с центром в (52, 30). Стиль — тёмный корпус в тон
dark_panel/gunmetal_panel из киберпанк-пака, тонкая циановая кромка снизу как единственный акцент.

Запуск из корня репозитория:
    python3 Tools/gen_tv_screen.py
"""
import json
import os
import random

from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(__file__), "..")
OUT = os.path.join(ROOT, "Resources", "Textures", "_Wega", "Structures", "Machines", "tv_screen.rsi")

W, H = 104, 64
SCR_W, SCR_H = 96, 54
SCR_X, SCR_Y = (W - SCR_W) // 2, 30 - SCR_H // 2  # центр экрана (52, 30)

BODY = (40, 44, 51)
BODY_LIGHT = (52, 57, 65)
BODY_DARK = (30, 33, 39)
SCREEN_OFF = (16, 18, 22)
ACCENT = (52, 190, 210)


def build():
    rng = random.Random("tv")
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Корпус со скруглёнными углами.
    d.rounded_rectangle([0, 0, W - 1, H - 1], radius=4, fill=BODY)
    d.rounded_rectangle([0, 0, W - 1, H - 1], radius=4, outline=BODY_DARK)
    d.line([(3, 1), (W - 4, 1)], fill=BODY_LIGHT)  # блик сверху

    # Панельные швы корпуса — лёгкая техно-фактура.
    for x in (SCR_X - 3, SCR_X + SCR_W + 2):
        d.line([(x, 4), (x, H - 8)], fill=BODY_DARK)

    # Гнездо экрана: тёмная выемка + чёрная «матрица» (её закрывает видео-слой).
    d.rectangle([SCR_X - 2, SCR_Y - 2, SCR_X + SCR_W + 1, SCR_Y + SCR_H + 1], fill=BODY_DARK)
    d.rectangle([SCR_X - 1, SCR_Y - 1, SCR_X + SCR_W, SCR_Y + SCR_H], fill=SCREEN_OFF)
    # Едва заметный шум выключенной матрицы.
    px = img.load()
    for y in range(SCR_Y, SCR_Y + SCR_H, 2):
        for x in range(SCR_X, SCR_X + SCR_W, 3):
            if rng.random() < 0.2:
                px[x, y] = (22, 25, 30, 255)

    # Нижняя кромка: циановая статус-полоска и вентрешётка.
    d.line([(SCR_X, H - 5), (SCR_X + 22, H - 5)], fill=ACCENT)
    for x in range(SCR_X + 30, SCR_X + SCR_W, 4):
        d.line([(x, H - 6), (x, H - 4)], fill=BODY_DARK)

    os.makedirs(OUT, exist_ok=True)
    img.save(os.path.join(OUT, "frame.png"))

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Made by Meguneri for wega-mega",
        "size": {"x": W, "y": H},
        "states": [{"name": "frame"}],
    }
    with open(os.path.join(OUT, "meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"{OUT}: frame {W}x{H}, screen window {SCR_W}x{SCR_H} at ({SCR_X},{SCR_Y})")


if __name__ == "__main__":
    build()
