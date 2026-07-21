#!/usr/bin/env python3
"""Генерирует полноэкранную вуаль инея для арена-холода wega-mega.
Прозрачный центр (геймплей видно), ледяная изморозь наползает от краёв/углов,
редкие снежинки. Растягивается оверлеем на весь экран, прозрачность ∝ уровню холода.

Пишет Resources/Textures/_Wega/Effects/frost_overlay.png. Запуск из корня репо (нужен PIL).
"""
import math, os, random
from PIL import Image, ImageDraw

W, H = 1024, 576
random.seed(5)

ICE = [(206, 230, 244), (224, 240, 250), (180, 210, 232), (240, 248, 253)]


def edge_factor(x, y):
    ex = min(x, W - x) / (W / 2)
    ey = min(y, H - y) / (H / 2)
    return max(0.0, 1.0 - min(ex, ey))          # 1 у самого края, 0 в центре


def main():
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # растушёванная ледяная кайма
    border = 150
    for t in range(border):
        f = 1.0 - t / border
        a = int(150 * f * f)
        d.rectangle([t, t, W - 1 - t, H - 1 - t], outline=(200, 226, 242, a))

    # кристаллы изморози — гуще к краям/углам
    for _ in range(2600):
        x = random.uniform(0, W); y = random.uniform(0, H)
        e = edge_factor(x, y)
        if random.random() > e ** 1.4:
            continue
        a = int(200 * e)
        col = random.choice(ICE)
        arms = random.randint(2, 4)
        base = random.uniform(0, math.tau)
        for k in range(arms):
            ang = base + k * math.tau / arms + random.uniform(-0.3, 0.3)
            ln = random.uniform(2, 7) * (0.5 + e)
            x2 = x + math.cos(ang) * ln; y2 = y + math.sin(ang) * ln
            d.line([x, y, x2, y2], fill=col + (a,), width=1)

    # снежинки (шестилучевые) — тоже к краям
    for _ in range(70):
        x = random.uniform(0, W); y = random.uniform(0, H)
        e = edge_factor(x, y)
        if random.random() > (0.25 + e):
            continue
        a = int(150 * (0.4 + e)); r = random.uniform(3, 7)
        col = (232, 244, 252, a)
        for k in range(3):
            ang = k * math.pi / 3
            dx = math.cos(ang) * r; dy = math.sin(ang) * r
            d.line([x - dx, y - dy, x + dx, y + dy], fill=col, width=1)
            # веточки
            for s in (-1, 1):
                bx = x + dx * 0.55; by = y + dy * 0.55
                ba = ang + s * 0.7
                d.line([bx, by, bx + math.cos(ba) * r * 0.4, by + math.sin(ba) * r * 0.4], fill=col, width=1)

    out = "Resources/Textures/_Wega/Effects/frost_overlay.png"
    os.makedirs(os.path.dirname(out), exist_ok=True)
    img.save(out)
    print("wrote", out, img.size)


if __name__ == "__main__":
    main()
