#!/usr/bin/env python3
"""Генератор спрайта портативного боевого анализатора тренера арены (HandheldFightComputer).

Рисует с нуля тактический ручной компьютер: тёмный корпус, зелёный экран со «столбцами
статистики» и блок механических клавиш. Кадры:
  * icon.png            — 32x32, иконка;
  * analyzer.png        — 64x32, 2 кадра анимации (бегущие столбцы на экране, мигает индикатор);
  * analyzer-inhand-left/right.png — 64x64 (2x2 = 4 стороны): геометрия хвата берётся из
    handheldcrewmonitor.rsi (позы рук верные), а цвета перекрашиваются в тёмный тактический
    корпус с зелёным экраном.

Спрайт под правило форка: PNG руками не редактировать — править генератор и перезапускать.

Запуск из корня репозитория:
    python3 dev/gen_fight_analyzer.py
"""
import json
import os

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Resources", "Textures", "Objects", "Specific", "Medical",
                   "handheldcrewmonitor.rsi")
DST = os.path.join(ROOT, "Resources", "Textures", "_Wega", "Objects", "Specific",
                   "fight_analyzer.rsi")

# --- палитра ---
BODY_DARK = (34, 37, 43, 255)     # корпус, тень
BODY_MID = (54, 59, 68, 255)      # корпус, основной
BODY_LIGHT = (78, 85, 97, 255)    # корпус, блик/грань
SCREEN_BG = (12, 26, 16, 255)     # тёмный фон экрана
SCREEN_GREEN = (74, 224, 108, 255)  # яркие столбцы статистики
SCREEN_DIM = (40, 120, 60, 255)   # приглушённые столбцы
KEY_DARK = (24, 26, 30, 255)      # промежутки клавиатуры
KEY_CAP = (92, 99, 112, 255)      # верх клавиши
LED_ON = (240, 90, 70, 255)       # индикатор «идёт поиск»
LED_OFF = (90, 40, 36, 255)


def put(px, x, y, c):
    if 0 <= x < 32 and 0 <= y < 32:
        px[x, y] = c


def draw_body(px):
    """Скруглённый тёмный корпус 12x18 по центру."""
    x0, y0, x1, y1 = 10, 6, 21, 25
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            # срезаем уголки
            if (x, y) in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
                continue
            c = BODY_MID
            if x == x0 or y == y0:
                c = BODY_LIGHT      # верх/левый край — блик
            elif x == x1 or y == y1:
                c = BODY_DARK       # низ/правый — тень
            put(px, x, y, c)


def draw_screen(px, frame):
    """Экран со столбцами статистики (сдвигаются между кадрами)."""
    sx0, sy0, sx1, sy1 = 11, 8, 20, 15
    for y in range(sy0, sy1 + 1):
        for x in range(sx0, sx1 + 1):
            put(px, x, y, SCREEN_BG)
    # 5 столбцов разной высоты, «бегут» при смене кадра
    heights = [5, 3, 6, 2, 4] if frame == 0 else [3, 6, 2, 5, 4]
    for i, h in enumerate(heights):
        cx = sx0 + 1 + i * 2
        for dy in range(h):
            y = sy1 - dy
            c = SCREEN_GREEN if dy >= h - 2 else SCREEN_DIM
            put(px, cx, y, c)


def draw_keys(px):
    """Блок механических клавиш под экраном: сетка 5x3."""
    kx0, ky0 = 11, 17
    for row in range(3):
        for col in range(5):
            x = kx0 + col * 2
            y = ky0 + row * 2
            put(px, x, y, KEY_CAP)
            put(px, x, y + 1, KEY_DARK)


def draw_led(px, frame):
    """Индикатор поиска в правом верхнем углу — мигает."""
    put(px, 19, 7, LED_ON if frame == 0 else LED_OFF)


def base_frame(frame):
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = img.load()
    draw_body(px)
    draw_screen(px, frame)
    draw_keys(px)
    draw_led(px, frame)
    return img


def recolor_inhand(src_img):
    """Перекрашивает кадр из крю-монитора: серый корпус -> тёмный тактический, синий экран -> зелёный."""
    src = src_img.convert("RGBA")
    out = Image.new("RGBA", src.size, (0, 0, 0, 0))
    sp, op = src.load(), out.load()
    W, H = src.size
    for y in range(H):
        for x in range(W):
            r, g, b, a = sp[x, y]
            if a == 0:
                continue
            # Экран крю-монитора — синева (b заметно больше r): в зелёный.
            if b > r + 25 and b > 90:
                lum = (r + g + b) / 3 / 255
                op[x, y] = (
                    int(SCREEN_DIM[0] + (SCREEN_GREEN[0] - SCREEN_DIM[0]) * lum),
                    int(SCREEN_DIM[1] + (SCREEN_GREEN[1] - SCREEN_DIM[1]) * lum),
                    int(SCREEN_DIM[2] + (SCREEN_GREEN[2] - SCREEN_DIM[2]) * lum),
                    255,
                )
            elif r > 180 and g > 180 and b < 120:  # жёлтые вкрапления кнопок -> LED
                op[x, y] = LED_ON
            else:
                # Серый корпус: тёмный тактический градиент по яркости.
                lum = (r + g + b) / 3 / 255
                op[x, y] = (
                    int(BODY_DARK[0] + (BODY_LIGHT[0] - BODY_DARK[0]) * lum),
                    int(BODY_DARK[1] + (BODY_LIGHT[1] - BODY_DARK[1]) * lum),
                    int(BODY_DARK[2] + (BODY_LIGHT[2] - BODY_DARK[2]) * lum),
                    255,
                )
    return out


def main():
    os.makedirs(DST, exist_ok=True)

    # icon
    base_frame(0).save(os.path.join(DST, "icon.png"))

    # analyzer.png — 2 кадра 32x32 в ряд (64x32)
    anim = Image.new("RGBA", (64, 32), (0, 0, 0, 0))
    anim.paste(base_frame(0), (0, 0))
    anim.paste(base_frame(1), (32, 0))
    anim.save(os.path.join(DST, "analyzer.png"))

    # inhand — перекраска из крю-монитора
    for side in ("left", "right"):
        src = Image.open(os.path.join(SRC, f"scanner-inhand-{side}.png"))
        recolor_inhand(src).save(os.path.join(DST, f"analyzer-inhand-{side}.png"))

    meta = {
        "version": 1,
        "size": {"x": 32, "y": 32},
        "license": "CC-BY-SA-3.0",
        "copyright": "Corpus/screen drawn for wega-mega (dev/gen_fight_analyzer.py). In-hand geometry "
                     "recolored from Objects/Specific/Medical/handheldcrewmonitor.rsi "
                     "(tgstation, CC-BY-SA-3.0).",
        "states": [
            {"name": "icon", "directions": 1},
            {"name": "analyzer", "directions": 1, "delays": [[0.5, 0.5]]},
            {"name": "analyzer-inhand-left", "directions": 4},
            {"name": "analyzer-inhand-right", "directions": 4},
        ],
    }
    with open(os.path.join(DST, "meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=4, ensure_ascii=False)
        f.write("\n")

    print("готово:", DST)


if __name__ == "__main__":
    main()
