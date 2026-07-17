#!/usr/bin/env python3
"""Генератор анимаций демонтажа стен арест-ботом.

Пишет Resources/Textures/_Wega/Effects/wall_demolish.rsi с двумя стейтами:
  hit      — удар по стене: вспышка, разлетающиеся обломки, клубы пыли (8 кадров);
  collapse — обрушение: падающая крошка, поднимающееся облако пыли (14 кадров).

Запускать из корня репозитория: python3 dev/gen_wall_demolish_fx.py
Детеминированный (фиксированный seed) — перезапуск даёт те же PNG.
"""

import json
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw

SIZE = 32
OUT = Path("Resources/Textures/_Wega/Effects/wall_demolish.rsi")

# Палитра: серая каменная крошка + тёплые искры + пыль.
DEBRIS_COLORS = [(138, 138, 138), (111, 111, 111), (85, 85, 85), (160, 152, 140)]
SPARK_COLORS = [(255, 200, 90), (255, 150, 40), (255, 235, 170)]
DUST_COLOR = (154, 146, 132)
FLASH_COLOR = (255, 245, 210)


def clamp(v, lo, hi):
    return max(lo, min(hi, v))


def blend_px(img, x, y, color, alpha):
    """Нарисовать пиксель с альфа-смешиванием поверх текущего содержимого."""
    x, y = int(x), int(y)
    if not (0 <= x < SIZE and 0 <= y < SIZE):
        return
    r0, g0, b0, a0 = img.getpixel((x, y))
    a = clamp(alpha, 0, 255)
    if a0 == 0:
        img.putpixel((x, y), (*color, a))
        return
    na = a + a0 * (255 - a) // 255
    nr = (color[0] * a + r0 * a0 * (255 - a) // 255) // max(na, 1)
    ng = (color[1] * a + g0 * a0 * (255 - a) // 255) // max(na, 1)
    nb = (color[2] * a + b0 * a0 * (255 - a) // 255) // max(na, 1)
    img.putpixel((x, y), (int(nr), int(ng), int(nb), int(na)))


def soft_circle(img, cx, cy, radius, color, alpha):
    """Мягкий круг: альфа спадает от центра к краю."""
    r_int = int(math.ceil(radius))
    for y in range(int(cy) - r_int, int(cy) + r_int + 1):
        for x in range(int(cx) - r_int, int(cx) + r_int + 1):
            d = math.hypot(x - cx, y - cy)
            if d > radius:
                continue
            fall = 1.0 - (d / max(radius, 0.001)) ** 2
            blend_px(img, x, y, color, int(alpha * fall))


class Particle:
    def __init__(self, rng, x, y, speed, angle, colors, life, gravity=0.5, size=1):
        self.x, self.y = x, y
        self.vx = math.cos(angle) * speed
        self.vy = math.sin(angle) * speed
        self.color = rng.choice(colors)
        self.life = life
        self.age = 0
        self.gravity = gravity
        self.size = size

    def step(self):
        self.x += self.vx
        self.y += self.vy
        self.vy += self.gravity
        self.vx *= 0.92
        self.age += 1
        # Отскок от «пола» тайла.
        if self.y > SIZE - 4 and self.vy > 0:
            self.y = SIZE - 4
            self.vy *= -0.35

    def draw(self, img):
        if self.age >= self.life:
            return
        alpha = int(255 * (1.0 - self.age / self.life))
        for dy in range(self.size):
            for dx in range(self.size):
                blend_px(img, self.x + dx, self.y + dy, self.color, alpha)


def ring(img, cx, cy, radius, color, alpha, thickness=1.4):
    """Кольцо (ударная волна): альфа сосредоточена на окружности."""
    r_int = int(math.ceil(radius + thickness))
    for y in range(int(cy) - r_int, int(cy) + r_int + 1):
        for x in range(int(cx) - r_int, int(cx) + r_int + 1):
            d = abs(math.hypot(x - cx, y - cy) - radius)
            if d > thickness:
                continue
            blend_px(img, x, y, color, int(alpha * (1.0 - d / thickness)))


def gen_hit_frames():
    """Удар: яркая вспышка, ударная волна кольцом, сноп искр и обломков.

    Две фазы: панч (8 кадров x 0.06с) + оседание пыли и обломков
    (10 кадров x 0.102с). Итого ровно 1.5с — пауза между ударами бота
    (ArrestBotComponent.BreachCooldown), чтобы эффект жил весь цикл удара.
    """
    rng = random.Random(1337)
    frames_n = 8
    settle_n = 10
    cx, cy = SIZE / 2, SIZE / 2

    debris = [
        Particle(rng, cx, cy,
                 speed=rng.uniform(2.0, 4.0), angle=rng.uniform(0, math.tau),
                 colors=DEBRIS_COLORS, life=rng.randint(5, frames_n),
                 gravity=0.55, size=rng.choice([1, 1, 2]))
        for _ in range(20)
    ]
    sparks = [
        Particle(rng, cx, cy,
                 speed=rng.uniform(3.0, 5.5), angle=rng.uniform(0, math.tau),
                 colors=SPARK_COLORS, life=rng.randint(4, 6), gravity=0.2)
        for _ in range(18)
    ]
    puffs = [  # (угол, скорость расширения, фаза)
        (rng.uniform(0, math.tau), rng.uniform(0.7, 1.2), rng.uniform(0, 1.0))
        for _ in range(4)
    ]

    frames = []
    for f in range(frames_n):
        img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))

        # Пыль — лёгкая дымка, быстро тает, не превращаясь в кашу.
        t = f + 1
        for ang, spd, phase in puffs:
            r = 1.5 + (t + phase) * spd
            d = (t + phase) * spd * 0.9
            alpha = clamp(int(95 - t * 18), 0, 255)
            if alpha > 0:
                soft_circle(img, cx + math.cos(ang) * d, cy + math.sin(ang) * d,
                            r, DUST_COLOR, alpha)

        # Ударная волна: расширяющееся кольцо на первых кадрах.
        if f < 4:
            ring(img, cx, cy, 3.0 + f * 3.2, (255, 220, 150), 200 - f * 50)

        for p in debris + sparks:
            p.draw(img)
            p.step()

        # Вспышка удара — первые два кадра.
        if f == 0:
            soft_circle(img, cx, cy, 6.0, FLASH_COLOR, 255)
            soft_circle(img, cx, cy, 3.0, (255, 170, 60), 255)
            draw = ImageDraw.Draw(img)
            for ang in (0, math.tau / 4, math.tau / 8, 3 * math.tau / 8):
                ex, ey = math.cos(ang) * 9, math.sin(ang) * 9
                draw.line([cx - ex, cy - ey, cx + ex, cy + ey],
                          fill=(*FLASH_COLOR, 220))
        elif f == 1:
            soft_circle(img, cx, cy, 4.0, FLASH_COLOR, 160)
            soft_circle(img, cx, cy, 2.0, (255, 170, 60), 170)

        frames.append(img)

    # Фаза оседания: осевшие обломки тают, лёгкая дымка, пара тлеющих угольков.
    settled = [(p.x, p.y, p.color, p.size) for p in debris if p.y > SIZE - 7]
    embers = [(p.x, p.y) for p in rng.sample(sparks, 3)]
    for s in range(settle_n):
        img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        fade = 1.0 - s / settle_n

        # Дымка на месте удара, тает.
        haze = int(45 * fade)
        if haze > 0:
            soft_circle(img, cx, cy - 1 - s * 0.4, 6.0 + s * 0.5, DUST_COLOR, haze)

        for x, y, color, size in settled:
            alpha = int(190 * fade)
            for dy in range(size):
                for dx in range(size):
                    blend_px(img, x + dx, y + dy, color, alpha)

        # Угольки перемигиваются и гаснут к концу.
        for i, (ex, ey) in enumerate(embers):
            if s > settle_n - 3:
                continue
            blink = 1.0 if (s + i) % 2 == 0 else 0.45
            blend_px(img, ex, ey, (255, 150, 40), int(200 * fade * blink))

        frames.append(img)

    return frames, [0.06] * frames_n + [0.102] * settle_n


def gen_collapse_frames():
    """Обрушение: стена рассыпается — крошка валится вниз, пыль поднимается столбом."""
    rng = random.Random(4242)
    frames_n = 14
    cx = SIZE / 2

    # Крошка сыплется по всей ширине тайла с разных высот;
    # среди неё — крупные глыбы (2x2, 3x3), падают тяжело.
    rubble = []
    for _ in range(26):
        p = Particle(rng, rng.uniform(4, SIZE - 4), rng.uniform(3, 20),
                     speed=rng.uniform(0.2, 1.2),
                     angle=rng.uniform(math.tau * 0.15, math.tau * 0.35),  # вниз ±
                     colors=DEBRIS_COLORS, life=rng.randint(8, frames_n),
                     gravity=0.5, size=rng.choice([1, 2, 2]))
        rubble.append(p)
    for _ in range(6):
        p = Particle(rng, rng.uniform(6, SIZE - 6), rng.uniform(2, 12),
                     speed=rng.uniform(0.1, 0.6),
                     angle=rng.uniform(math.tau * 0.2, math.tau * 0.3),
                     colors=DEBRIS_COLORS[:3], life=frames_n,
                     gravity=0.65, size=3)
        rubble.append(p)

    # Клубы пыли: снизу вверх, расширяются и тают.
    puffs = [
        # (x, y0, скорость подъёма, радиус0, рост, фаза)
        (rng.uniform(6, SIZE - 6), rng.uniform(18, 26), rng.uniform(0.5, 1.1),
         rng.uniform(2.0, 3.5), rng.uniform(0.5, 0.9), rng.uniform(0, 3))
        for _ in range(9)
    ]

    frames = []
    for f in range(frames_n):
        img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))

        for x0, y0, rise, r0, grow, phase in puffs:
            t = f + phase
            r = r0 + t * grow
            y = y0 - t * rise
            alpha = clamp(int(185 - f * 14 - phase * 8), 0, 255)
            if alpha > 0:
                soft_circle(img, x0 + math.sin(t * 0.9) * 1.2, y, r, DUST_COLOR, alpha)

        # Горка щебня внизу — нарастает по мере обрушения.
        pile_w = clamp(f * 2.4, 0, SIZE - 8)
        if pile_w > 2:
            pile_alpha = clamp(int(60 + f * 12), 0, 200)
            soft_circle(img, cx, SIZE - 3, pile_w / 2, (100, 100, 100), pile_alpha)

        for p in rubble:
            p.draw(img)
            p.step()

        frames.append(img)
    return frames, [0.09] * frames_n


def write_state(name, frames, delays):
    sheet = Image.new("RGBA", (SIZE * len(frames), SIZE), (0, 0, 0, 0))
    for i, fr in enumerate(frames):
        sheet.paste(fr, (i * SIZE, 0))
    sheet.save(OUT / f"{name}.png")
    return {"name": name, "directions": 1, "delays": [delays]}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    states = [
        write_state("hit", *gen_hit_frames()),
        write_state("collapse", *gen_collapse_frames()),
    ]
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Generated for wega-mega by dev/gen_wall_demolish_fx.py",
        "size": {"x": SIZE, "y": SIZE},
        "states": states,
    }
    (OUT / "meta.json").write_text(json.dumps(meta, indent=2) + "\n")
    print(f"OK: {OUT} ({', '.join(s['name'] for s in states)})")


if __name__ == "__main__":
    main()
