#!/usr/bin/env python3
"""Генерирует RSI фростпанк-Генератора («горн-очаг») для арены wega-mega.
Оригинальная процедурная графика (силуэт v7): приземистая индустриальная домна с
коническим колпаком-вытяжкой, кластером труб, светящейся топкой, латунью и наледью.
Кабинетная проекция SS14 с объёмом (цилиндрическая светотень + хомуты-дуги).

Пишет в Resources/Textures/_Wega/Structures/Specific/frostpunk_generator.rsi:
  on.png  — анимация (жар дышит, маховик крутится, дым идёт), 8 кадров, кадр 128x160
  off.png — холодная погасшая домна, 1 кадр
  meta.json

Запускать из корня репозитория (нужен PIL; при зависании системного python
использовать dev/.venv-sprites).
"""
import json, math, os, random
from PIL import Image, ImageDraw

FW, FH = 128, 160          # кадр RSI (4x5 тайлов)
GW, GH = 128, 134          # холст рисунка домны
YOFF = 22                  # сдвиг рисунка вниз в кадре (место дыму сверху)
FRAMES = 8

STEPS = [(130, 136, 144), (100, 106, 113), (74, 79, 85),
         (54, 58, 63), (37, 40, 45), (23, 25, 29)]
IRON, IRON_DK, IRON_HI, IRON_SH = STEPS[2], STEPS[4], STEPS[0], (15, 16, 19)
TOP, TOP_DK = STEPS[1], STEPS[3]
SOOT = (18, 16, 16); RUST = (108, 62, 36); RUST_DK = (74, 42, 24)
BRASS = (156, 120, 54); BRASS_HI = (206, 168, 90); BRASS_DK = (94, 70, 30)
FROST = (150, 175, 196); ICE = (190, 208, 222)


def cyl_band(x, cx, rx, light=-0.4):
    nx = max(-1.0, min(1.0, (x - cx) / rx))
    b = math.cos(math.asin(nx) - light)
    return STEPS[min(len(STEPS) - 1, int((1 - max(0.0, b)) * (len(STEPS) - 1) + 0.5))]


def cylinder(d, cx, top, bot, rx, ry, lid=True):
    for x in range(int(cx - rx), int(cx + rx)):
        d.line([(x, top), (x, bot)], fill=cyl_band(x, cx, rx))
    d.ellipse([cx - rx, bot - ry, cx + rx, bot + ry], fill=IRON_DK)
    d.ellipse([cx - rx + 2, bot - ry, cx + rx - 2, bot + ry - 2], fill=STEPS[3])
    if lid:
        d.ellipse([cx - rx, top - ry, cx + rx, top + ry], fill=TOP_DK)
        d.ellipse([cx - rx + 1, top - ry + 1, cx + rx - 1, top + ry - 1], fill=TOP)


def frustum(d, cx, yb, yt, rb, rt, ryt=6):
    for x in range(int(cx - rb), int(cx + rb)):
        if x < cx - rt: frac = (x - (cx - rb)) / max(1.0, ((cx - rt) - (cx - rb)))
        elif x > cx + rt: frac = ((cx + rb) - x) / max(1.0, ((cx + rb) - (cx + rt)))
        else: frac = 1.0
        d.line([(x, int(yb + frac * (yt - yb))), (x, yb)], fill=cyl_band(x, cx, rb))
    for fa in (-0.55, 0.0, 0.55):
        d.line([(cx + math.sin(fa) * rb, yb - 1), (cx + math.sin(fa) * rt, yt)],
               fill=IRON_DK if fa >= 0 else STEPS[3], width=1)
    d.ellipse([cx - rt, yt - ryt, cx + rt, yt + ryt], fill=IRON_SH)
    d.ellipse([cx - rt + 1, yt - ryt + 1, cx + rt - 1, yt + ryt - 1], fill=TOP)
    for i in range(int(rt / 2)):
        a = i / (rt / 2) * math.tau
        d.point((cx + math.cos(a) * (rt - 2), yt + math.sin(a) * (ryt - 1)), fill=IRON_SH)


def hoop(d, cx, cy, rx, ry, thick=4, rivets=True):
    d.arc([cx - rx, cy - ry, cx + rx, cy + ry], 0, 180, fill=IRON_DK, width=thick)
    d.arc([cx - rx, cy - ry - 1, cx + rx, cy + ry - 1], 6, 174, fill=IRON_HI, width=1)
    if rivets:
        for i in range(1, 14):
            a = i / 14 * math.pi
            d.point((cx - math.cos(a) * rx, cy + math.sin(a) * ry), fill=IRON_SH)


def vent(d, x, y, r, flick, cold):
    d.ellipse([x - r - 1, y - r - 1, x + r + 1, y + r + 1], fill=IRON_SH)
    d.ellipse([x - r, y - r, x + r, y + r], fill=BRASS_DK)
    if cold:
        d.ellipse([x - r + 1, y - r + 1, x + r - 1, y + r - 1], fill=(28, 24, 22))
        return
    for rr, col in [(r - 1, (150, 56, 12)), (r - 2, (226, 112, 30)), (max(1, r - 3), (255, 196, 96))]:
        col = tuple(min(255, int(c * (0.55 + 0.45 * flick))) for c in col)
        d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=col)


def stack(d, x, top, w):
    for xi in range(w):
        d.line([(x + xi, top), (x + xi, 46)], fill=cyl_band(x + xi, x + w / 2, w / 2 + 1, -0.5))
    d.ellipse([x - 2, top - 3, x + w + 2, top + 3], fill=IRON_SH)
    d.ellipse([x, top - 2, x + w, top + 1], fill=STEPS[3])


def draw_gen(phase, cold=False):
    random.seed(37)
    img = Image.new("RGBA", (GW, GH), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    flick = 0.7 + 0.3 * math.sin(phase * math.tau)

    if not cold:
        gl = Image.new("RGBA", (GW, GH), (0, 0, 0, 0))
        ImageDraw.Draw(gl).ellipse([34, 108, 96, 126], fill=(234, 120, 44, int(70 * flick)))
        img.alpha_composite(gl)

    cylinder(d, 64, 100, 118, 52, 13)
    for i in range(1, 15):
        a = i / 15 * math.pi
        px = 64 - math.cos(a) * 50; py = 87 + math.sin(a) * 12
        d.line([px, py, px, py - 5], fill=BRASS_DK); d.point((px, py - 5), fill=BRASS)
    d.arc([14, 75, 114, 99], 6, 174, fill=BRASS_DK, width=1)

    cylinder(d, 64, 58, 100, 46, 14)
    for i in range(1, 16):
        a = i / 16 * math.pi; rr = random.uniform(41, 45)
        d.point((64 - math.cos(a) * rr, 58 + math.sin(a) * 13), fill=IRON_SH)

    frustum(d, 64, 57, 38, 40, 18, ryt=6)
    stack(d, 54, 20, 9); stack(d, 63, 12, 10); stack(d, 73, 24, 8)

    d.polygon([(16, 52), (30, 56), (28, 66), (18, 62)], fill=IRON_DK)
    d.polygon([(17, 53), (25, 55), (24, 60), (19, 58)], fill=IRON)
    d.rectangle([28, 60, 44, 64], fill=IRON_SH)

    for sx in (36, 52, 78, 92):
        col = IRON_SH if sx > 64 else STEPS[3]
        d.line([sx, 64, sx, 98], fill=col, width=1)
        for yy in range(70, 98, 8):
            d.point((sx - 2, yy), fill=IRON_SH); d.point((sx - 2, yy - 1), fill=IRON_HI)
    hoop(d, 64, 72, 46, 14, thick=4); hoop(d, 64, 96, 46, 14, thick=5)
    d.arc([18, 76, 110, 102], 150, 180, fill=BRASS_DK, width=4)
    d.arc([18, 76, 110, 102], 0, 30, fill=BRASS_DK, width=4)
    d.arc([18, 76, 110, 102], 156, 178, fill=BRASS, width=1)
    vent(d, 40, 80, 5, flick, cold); vent(d, 88, 80, 5, flick, cold)

    # топка
    fx0, fy0, fx1, fy1 = 50, 76, 78, 106
    d.rectangle([fx0 - 2, fy0 - 2, fx1 + 2, fy1], fill=IRON_SH)
    d.rectangle([fx0, fy0, fx1, fy1 - 2], fill=(12, 9, 7))
    if not cold:
        cxf, cyf = (fx0 + fx1) / 2, (fy0 + fy1) / 2
        for yy in range(fy0 + 1, fy1 - 1):
            for xx in range(fx0 + 1, fx1 - 1):
                dx = (xx - cxf) / ((fx1 - fx0) / 2); dy = (yy - cyf) / ((fy1 - fy0) / 2)
                r = (1 - math.hypot(dx, dy)) * (0.55 + 0.6 * flick + random.uniform(-0.18, 0.18))
                if r <= 0.06: continue
                c = ((255, 234, 156) if r > 0.82 else (252, 178, 72) if r > 0.58 else
                     (232, 116, 30) if r > 0.36 else (172, 62, 12) if r > 0.18 else (92, 32, 8))
                d.point((xx, yy), fill=c)
    for gx in range(fx0 + 4, fx1 - 2, 5):
        d.line([gx, fy0, gx, fy1 - 2], fill=(8, 6, 5), width=1)
    d.rectangle([fx0 - 4, fy0 + 2, fx0 - 2, fy0 + 10], fill=IRON_HI)
    d.rectangle([fx0 - 4, fy1 - 12, fx0 - 2, fy1 - 4], fill=IRON_HI)

    # маховик + манометры
    wx, wy, R = 30, 84, 9
    rot = (0 if cold else phase) * math.tau * 0.5
    d.ellipse([wx - R, wy - R, wx + R, wy + R], fill=BRASS_DK)
    for k in range(6):
        a = rot + k * math.tau / 6
        d.line([wx, wy, wx + math.cos(a) * (R - 1), wy + math.sin(a) * (R - 1)], fill=BRASS, width=2)
    d.ellipse([wx - 3, wy - 3, wx + 3, wy + 3], fill=BRASS_HI)
    d.rectangle([92, 76, 106, 92], fill=IRON_DK); d.rectangle([92, 76, 106, 77], fill=IRON_HI)
    for gx, gy in [(97, 81), (101, 87)]:
        d.ellipse([gx - 3, gy - 3, gx + 3, gy + 3], fill=BRASS_DK)
        d.ellipse([gx - 2, gy - 2, gx + 2, gy + 2], fill=ICE)

    for _ in range(14):
        d.point((random.uniform(22, 106), random.uniform(70, 98)), fill=random.choice([RUST, RUST_DK]))
    for _ in range(90):
        x = random.uniform(16, 112); y = random.uniform(100, 118)
        if random.random() < (y - 96) / 24:
            d.point((x, y), fill=random.choice([FROST, ICE, (120, 150, 172)]))
    return img


# устья труб в координатах кадра (центр раструба, x; верх трубы, y) — с учётом YOFF
STACK_MOUTHS = [(58, 20 + YOFF), (68, 12 + YOFF), (77, 24 + YOFF)]


def steam_plume(sd, sx, sy, phase):
    """Бледный пар, поднимающийся из трубы; зациклен по phase (0..1)."""
    puffs, plume_h = 7, 46
    for i in range(puffs):
        frac = ((i / puffs) + phase) % 1.0          # прокрутка вверх с петлёй
        y = sy - 4 - frac * plume_h
        x = sx + math.sin(frac * 3.0 + sx) * (2 + frac * 5)
        r = 2 + frac * 5
        a = int(165 * (1 - frac) ** 1.2)
        sd.ellipse([x - r, y - r, x + r, y + r], fill=(224, 232, 238, a))


def frame(phase, cold=False):
    f = Image.new("RGBA", (FW, FH), (0, 0, 0, 0))
    f.alpha_composite(draw_gen(phase, cold), (0, YOFF))
    if not cold:
        st = Image.new("RGBA", (FW, FH), (0, 0, 0, 0))
        sd = ImageDraw.Draw(st)
        for sx, sy in STACK_MOUTHS:
            steam_plume(sd, sx, sy, phase)
        f.alpha_composite(st)
    return f


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out = os.path.join(root, "Resources/Textures/_Wega/Structures/Specific/frostpunk_generator.rsi")
    os.makedirs(out, exist_ok=True)

    # on.png — 8 кадров в один ряд
    sheet = Image.new("RGBA", (FW * FRAMES, FH), (0, 0, 0, 0))
    for i in range(FRAMES):
        sheet.alpha_composite(frame(i / FRAMES), (i * FW, 0))
    sheet.save(os.path.join(out, "on.png"))

    frame(0.0, cold=True).save(os.path.join(out, "off.png"))

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Original procedural art for wega-mega by Meguneri",
        "size": {"x": FW, "y": FH},
        "states": [
            {"name": "on", "directions": 1, "delays": [[0.12] * FRAMES]},
            {"name": "off"},
        ],
    }
    with open(os.path.join(out, "meta.json"), "w") as fp:
        json.dump(meta, fp, indent=2)
    print("wrote", out)


if __name__ == "__main__":
    main()
