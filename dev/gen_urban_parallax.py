#!/usr/bin/env python3
"""
Generate a seamlessly tileable urban night skyline parallax texture.
Output: Resources/Textures/Parallaxes/WegaUrbanBG.png  (512x512 RGBA)
Run from repo root: python3 gen_urban_parallax.py
"""
import math
import os
import random
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT  = os.path.join(ROOT, "Resources", "Textures", "Parallaxes", "WegaUrbanBG.png")
OUT_YML = OUT + ".yml"

W, H = 512, 512
HORIZON = int(H * 0.60)   # y-coord of rooftop skyline baseline
RNG = random.Random(1337)  # fixed seed — same result every run


def lerp(a, b, t):
    return a + (b - a) * t

def lerp_color(a, b, t):
    return tuple(int(lerp(a[i], b[i], t)) for i in range(len(a)))


# ── 1. Sky gradient ──────────────────────────────────────────────────────────

def make_sky() -> Image.Image:
    img = Image.new("RGBA", (W, H))
    px  = img.load()
    top = (4, 6, 16, 255)
    bot = (14, 20, 42, 255)
    for y in range(H):
        c = lerp_color(top, bot, y / (H - 1))
        for x in range(W):
            px[x, y] = c
    return img


# ── 2. Horizon light-pollution glow ─────────────────────────────────────────

def add_horizon_glow(base: Image.Image) -> Image.Image:
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(glow)
    cx = W // 2
    for i in range(10):
        t   = i / 9
        rw  = int(W  * (0.25 + t * 0.9))
        rh  = int(H  * (0.04 + t * 0.18))
        alpha = int(55 * (1 - t))
        col = lerp_color((255, 150, 30), (160, 50, 5), t) + (alpha,)
        draw.ellipse([cx - rw, HORIZON - rh, cx + rw, HORIZON + rh], fill=col)
    glow = glow.filter(ImageFilter.GaussianBlur(radius=22))
    return Image.alpha_composite(base, glow)


# ── 3. Buildings (seamless tile) ─────────────────────────────────────────────

def _gen_buildings():
    """Return list of (x, w, h) covering [0, W) with wrap-around continuity."""
    buildings = []
    x = 0
    while x < W:
        bw = RNG.randint(20, 90)
        bh = RNG.randint(28, int(H * 0.50))
        buildings.append((x % W, bw, bh))
        x += bw + RNG.randint(0, 8)
    return buildings


def add_buildings(base: Image.Image):
    buildings = _gen_buildings()

    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw  = ImageDraw.Draw(layer)

    for (bx, bw, bh) in buildings:
        d   = RNG.randint(10, 28)
        col = (d, d + 2, d + 7, 255)
        by  = HORIZON - bh

        # Draw at normal pos and wrapped so the tile is seamless
        for ox in (0, W, -W):
            rx = bx + ox
            if rx + bw < 0 or rx >= W:
                continue
            draw.rectangle([rx, by, rx + bw - 1, HORIZON], fill=col)

            # Rooftop detail (tank / antenna) on some buildings
            if RNG.random() < 0.35:
                tw = RNG.randint(4, 9)
                th = RNG.randint(6, 22)
                tx = rx + RNG.randint(2, max(3, bw - tw - 2))
                td = d + RNG.randint(4, 10)
                draw.rectangle([tx, by - th, tx + tw - 1, by], fill=(td, td + 3, td + 8, 255))

    base = Image.alpha_composite(base, layer)
    return base, buildings


# ── 4. Windows (only inside building rects) ──────────────────────────────────

def add_windows(base: Image.Image, buildings) -> Image.Image:
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw  = ImageDraw.Draw(layer)

    # Build mask of "inside a building" pixels for fast lookup
    building_mask = [[False] * H for _ in range(W)]
    for (bx, bw, bh) in buildings:
        by = HORIZON - bh
        for ox in (0, W, -W):
            rx = bx + ox
            for xx in range(max(0, rx), min(W, rx + bw)):
                for yy in range(max(0, by), HORIZON):
                    building_mask[xx][yy] = True

    for _ in range(520):
        wx = RNG.randint(0, W - 1)
        wy = RNG.randint(int(HORIZON * 0.20), HORIZON - 3)
        if not building_mask[wx][wy]:
            continue
        ww = RNG.randint(2, 4)
        wh = RNG.randint(2, 4)

        r = RNG.random()
        if r < 0.55:
            # Warm yellow — lit apartment
            wc = (RNG.randint(215, 255), RNG.randint(170, 215), RNG.randint(50, 110),
                  RNG.randint(190, 245))
        elif r < 0.78:
            # Cool blue-white — office fluorescent
            wc = (RNG.randint(170, 210), RNG.randint(200, 235), RNG.randint(225, 255),
                  RNG.randint(130, 200))
        elif r < 0.90:
            # Red neon
            wc = (RNG.randint(210, 255), RNG.randint(20, 60), RNG.randint(10, 30),
                  RNG.randint(170, 230))
        else:
            # Purple/violet — neon sign
            wc = (RNG.randint(160, 210), RNG.randint(20, 60), RNG.randint(200, 255),
                  RNG.randint(150, 220))

        draw.rectangle([wx, wy, wx + ww, wy + wh], fill=wc)

    layer = layer.filter(ImageFilter.GaussianBlur(radius=0.7))
    return Image.alpha_composite(base, layer)


# ── 5. Ground & street lights ────────────────────────────────────────────────

def add_ground(base: Image.Image) -> Image.Image:
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw  = ImageDraw.Draw(layer)

    # Dark pavement
    draw.rectangle([0, HORIZON, W - 1, H - 1], fill=(5, 6, 10, 255))

    # Street light glows at horizon line
    glow_layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    g_draw     = ImageDraw.Draw(glow_layer)
    for _ in range(14):
        sx = RNG.randint(0, W - 1)
        g_draw.ellipse([sx - 7, HORIZON - 12, sx + 7, HORIZON + 12],
                       fill=(255, 195, 70, 70))
        draw.ellipse([sx - 1, HORIZON - 1, sx + 1, HORIZON + 1],
                     fill=(255, 235, 160, 210))
    glow_layer = glow_layer.filter(ImageFilter.GaussianBlur(radius=4))

    base = Image.alpha_composite(base, layer)
    base = Image.alpha_composite(base, glow_layer)
    return base


# ── 6. Atmospheric haze bands ────────────────────────────────────────────────

def add_haze(base: Image.Image) -> Image.Image:
    haze = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(haze)
    for i in range(6):
        fy    = int(HORIZON * (0.30 + i * 0.13))
        fh    = RNG.randint(12, 35)
        alpha = RNG.randint(8, 20)
        draw.rectangle([0, fy, W - 1, fy + fh], fill=(18, 24, 45, alpha))
    haze = haze.filter(ImageFilter.GaussianBlur(radius=10))
    return Image.alpha_composite(base, haze)


# ── main ─────────────────────────────────────────────────────────────────────

def main():
    print("Generating WegaUrbanBG.png …")
    img = make_sky()
    img = add_horizon_glow(img)
    img, buildings = add_buildings(img)
    img = add_windows(img, buildings)
    img = add_ground(img)
    img = add_haze(img)

    img.save(OUT)
    print(f"  Saved: {OUT}")

    with open(OUT_YML, "w") as f:
        f.write("sample:\n  filter: true\n")
    print(f"  Saved: {OUT_YML}")


if __name__ == "__main__":
    main()
