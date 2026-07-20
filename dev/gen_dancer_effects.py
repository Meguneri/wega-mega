#!/usr/bin/env python3
"""Генератор эффектов арена-босса «Пепельная танцовщица» (dev/gen_dancer_effects.py).

Один RSI (_Wega/Effects/dancer.rsi):
  * warning-inner / warning-outer — разные телеграфы двух ритмов вращения, 4 кадра;
  * spin  — круговой след раскалённых клинков в момент удара, 4 кадра;
  * ember — тлеющий сектор после вращения второй жизни, 4 кадра;
  * ash   — закрученная вспышка пепла при телепорте, 4 кадра;
  * rise  — вихрь пепла и углей при начале второй жизни, 6 кадров.

Узор детерминированный (хеш координат), без рантайм-рандома.
Спрайт под правило форка: PNG руками не редактировать — править генератор и перезапускать.

Запуск из корня репозитория:
    python3 dev/gen_dancer_effects.py
"""
import json
import math
import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DST = os.path.join(ROOT, "Resources", "Textures", "_Wega", "Effects", "dancer.rsi")
PREVIEW = os.path.join(ROOT, "dev", "dancer_effects_preview.png")


def _noise(x, y, seed, modulo=101):
    return (x * 7349 + y * 9151 + seed * 331) % modulo


def warning_frame(frame, outer):
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = img.load()
    cx = cy = 15.5
    pulse = (0.0, 0.8, 1.0, 0.55)[frame]
    for y in range(32):
        for x in range(32):
            dx, dy = x - cx, y - cy
            d = math.hypot(dx, dy)
            angle = math.atan2(dy, dx)
            if outer:
                # Разомкнутые вращающиеся дуги намекают: опасно именно внешнее кольцо.
                arc = (angle + frame * 0.55) % (math.pi / 2)
                visible = 10.0 < d < 14.8 and 0.18 < arc < 1.28
                color = (255, 126, 30, int(115 + 110 * pulse))
            else:
                # Плотная багровая розетка читается как первый близкий удар.
                spoke = abs(math.sin(angle * 4 + frame * 0.35))
                visible = d < 13.2 and (d > 10.8 or spoke > 0.88)
                color = (255, 54, 28, int(105 + 130 * pulse))
            if visible:
                px[x, y] = color
    return img


def spin_frame(frame):
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = img.load()
    cx = cy = 15.5
    progress = frame / 3
    for y in range(32):
        for x in range(32):
            dx, dy = x - cx, y - cy
            d = math.hypot(dx, dy)
            angle = math.atan2(dy, dx)
            # Две противоположные серповидные кромки быстро замыкают полный круг.
            sweep = (angle - progress * math.tau) % math.pi
            edge = 11.0 + 2.4 * math.sin(angle * 2)
            if abs(d - edge) < 1.25 and sweep < 1.9:
                alpha = int(235 * (1 - frame * 0.12))
                px[x, y] = (255, 205 if d < edge else 96, 40, alpha)
    return img


def ember_frame(seed):
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = img.load()
    for y in range(32):
        for x in range(32):
            d = math.hypot(x - 15.5, y - 15.5)
            n = _noise(x, y, seed)
            if d < 15 and n < 46:
                px[x, y] = (28, 18, 16, 125)      # пепельная база
            elif d < 15 and n < 59:
                px[x, y] = (158, 48, 12, 190)     # тлеющие угли
            elif d < 14 and n < 65:
                px[x, y] = (255, 174, 46, 225)    # яркие искры
    return img


def ash_frame(frame):
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = img.load()
    cx = cy = 15.5
    radius = 6 + frame * 3.2
    for y in range(32):
        for x in range(32):
            dx, dy = x - cx, y - cy
            d = math.hypot(dx, dy)
            angle = math.atan2(dy, dx)
            n = (x * 8117 + y * 6011 + frame * 97) % 29
            spiral = abs(math.sin(angle * 3 + d * 0.48 - frame * 1.4))
            if d < radius and n < 19 and spiral > 0.36:
                a = max(0, int(210 - d * 10 - frame * 22))
                g = 105 + n * 4
                px[x, y] = (g + 8, g, g - 3, a)
    return img


def rise_frame(frame):
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = img.load()
    for y in range(32):
        for x in range(32):
            dx = x - 15.5
            # Вихрь сужается кверху и поднимается от кадра к кадру.
            lifted_y = y + frame * 2.2
            width = max(2.0, 12.5 - abs(lifted_y - 18) * 0.42)
            wave = math.sin(lifted_y * 0.55 + frame * 0.9) * 3
            n = _noise(x, y, frame, 37)
            if 4 < lifted_y < 31 and abs(dx - wave) < width and n < 14:
                hot = lifted_y > 19 and n < 5
                px[x, y] = (255, 122, 24, 220) if hot else (112, 98, 96, 170)
    return img


def save_sheet(name, frames):
    sheet = Image.new("RGBA", (32 * len(frames), 32), (0, 0, 0, 0))
    for index, frame in enumerate(frames):
        sheet.paste(frame, (32 * index, 0))
    sheet.save(os.path.join(DST, f"{name}.png"))


def save_preview(state_frames):
    scale = 4
    label_width = 132
    row_height = 32 * scale + 12
    width = label_width + 6 * 32 * scale
    preview = Image.new("RGBA", (width, row_height * len(state_frames)), (18, 15, 20, 255))
    draw = ImageDraw.Draw(preview)
    for row, (name, frames) in enumerate(state_frames):
        top = row * row_height
        draw.text((8, top + 54), name, fill=(235, 220, 210, 255))
        for index, frame in enumerate(frames):
            enlarged = frame.resize((32 * scale, 32 * scale), Image.Resampling.NEAREST)
            preview.alpha_composite(enlarged, (label_width + index * 32 * scale, top))
    preview.save(PREVIEW)


def main():
    os.makedirs(DST, exist_ok=True)

    state_frames = [
        ("warning-inner", [warning_frame(i, False) for i in range(4)]),
        ("warning-outer", [warning_frame(i, True) for i in range(4)]),
        ("spin", [spin_frame(i) for i in range(4)]),
        ("ember", [ember_frame(i * 7) for i in range(4)]),
        ("ash", [ash_frame(i) for i in range(4)]),
        ("rise", [rise_frame(i) for i in range(6)]),
    ]
    for name, frames in state_frames:
        save_sheet(name, frames)
    save_preview(state_frames)

    meta = {
        "version": 1,
        "size": {"x": 32, "y": 32},
        "license": "CC-BY-SA-3.0",
        "copyright": "Drawn for wega-mega (dev/gen_dancer_effects.py).",
        "states": [
            {"name": "warning-inner", "directions": 1, "delays": [[0.18] * 4]},
            {"name": "warning-outer", "directions": 1, "delays": [[0.18] * 4]},
            {"name": "spin", "directions": 1, "delays": [[0.08] * 4]},
            {"name": "ember", "directions": 1, "delays": [[0.18] * 4]},
            {"name": "ash", "directions": 1, "delays": [[0.12] * 4]},
            {"name": "rise", "directions": 1, "delays": [[0.12] * 6]},
        ],
    }
    with open(os.path.join(DST, "meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=4, ensure_ascii=False)
        f.write("\n")
    print("готово:", DST)
    print("превью:", PREVIEW)


if __name__ == "__main__":
    main()
