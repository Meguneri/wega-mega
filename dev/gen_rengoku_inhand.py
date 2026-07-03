#!/usr/bin/env python3
"""Генератор inhand/equipped кадров катаны Рэнгоку.

Берёт кадры ОБЫЧНОЙ katana.rsi (правильная геометрия хвата и поз для 4 сторон)
и перекрашивает их палитрой, снятой с твоего rengoku_katana.rsi/icon.png:
  * клинок (blade-*)  -> огненный градиент (насыщенные цвета icon)
  * рукоять (handle-*) -> серый градиент (несатурированные цвета icon)
Затем склеивает blade+handle в один кадр и кладёт в rengoku_katana.rsi.

Так геометрия остаётся «как у нормального меча», а цвета — твои.

Запуск из корня репозитория:
    python3 gen_rengoku_inhand.py
"""
import colorsys
import os

from PIL import Image

ROOT = os.path.dirname(os.path.abspath(__file__))
MELEE = os.path.join(ROOT, "Resources", "Textures", "_Wega", "Objects", "Weapons", "Melee")
BASE = os.path.join(MELEE, "katana.rsi")          # источник геометрии
RSI = os.path.join(MELEE, "rengoku_katana.rsi")   # назначение
ICON = os.path.join(RSI, "icon.png")

# Какие кадры собрать: имя_результата -> (blade_файл, handle_файл).
FRAMES = {
    "inhand-left":       ("blade-inhand-left.png", "handle-inhand-left.png"),
    "inhand-right":      ("blade-inhand-right.png", "handle-inhand-right.png"),
    "equipped-BELT":     ("blade-equipped-BELT.png", "handle-equipped-BELT.png"),
    "equipped-BACKPACK": ("blade-equipped-BACKPACK.png", "handle-equipped-BACKPACK.png"),
}

SAT_THRESHOLD = 0.25  # выше — считаем пиксель «пламенем», ниже — «рукоятью»


def luminance(r, g, b):
    return 0.299 * r + 0.587 * g + 0.114 * b


def build_gradient(pixels):
    """Список (lum, (r,g,b)), отсортированный по яркости — палитра-градиент."""
    pixels = sorted(pixels, key=lambda p: p[0])
    return pixels


def sample_gradient(grad, t):
    """Цвет градиента в позиции t in [0,1] (по перцентилю)."""
    if not grad:
        v = int(t * 255)
        return (v, v, v)
    idx = min(len(grad) - 1, max(0, round(t * (len(grad) - 1))))
    return grad[idx][1]


def extract_palettes(icon):
    px = icon.load()
    W, H = icon.size
    flame, handle = [], []
    for y in range(H):
        for x in range(W):
            r, g, b, a = px[x, y]
            if a < 30:
                continue
            _, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            entry = (luminance(r, g, b), (r, g, b))
            (flame if s > SAT_THRESHOLD else handle).append(entry)
    return build_gradient(flame), build_gradient(handle)


def recolor(src, grad):
    """Перекрашивает непрозрачные пиксели src по их яркости через градиент."""
    src = src.convert("RGBA")
    px = src.load()
    W, H = src.size
    lums = [luminance(*px[x, y][:3]) for y in range(H) for x in range(W) if px[x, y][3] > 20]
    if not lums:
        return src
    lo, hi = min(lums), max(lums)
    span = max(1.0, hi - lo)
    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    op = out.load()
    for y in range(H):
        for x in range(W):
            r, g, b, a = px[x, y]
            if a <= 20:
                continue
            t = (luminance(r, g, b) - lo) / span
            nr, ng, nb = sample_gradient(grad, t)
            op[x, y] = (nr, ng, nb, a)
    return out


def main():
    if not os.path.exists(ICON):
        raise SystemExit(f"Нет {ICON} — сначала положи туда нарисованный icon.png.")

    icon = Image.open(ICON).convert("RGBA")
    flame, handle = extract_palettes(icon)
    print(f"палитра icon: пламя {len(flame)} цв., рукоять {len(handle)} цв.")

    for name, (blade_f, handle_f) in FRAMES.items():
        blade = recolor(Image.open(os.path.join(BASE, blade_f)), flame)
        grip = recolor(Image.open(os.path.join(BASE, handle_f)), handle)
        combined = Image.new("RGBA", blade.size, (0, 0, 0, 0))
        combined.alpha_composite(grip)   # рукоять под клинком
        combined.alpha_composite(blade)
        combined.save(os.path.join(RSI, f"{name}.png"))
        print("written", name + ".png", combined.size)

    print("\nГотово. Геометрия от обычной катаны, цвета — с твоего icon.")
    print("meta.json уже содержит нужные состояния (directions: 4) — не трогаю.")


if __name__ == "__main__":
    main()
