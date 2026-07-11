#!/usr/bin/env python3
"""Crate-shaped selector fob: a mini supply crate (amber body, cross straps,
clasp with amber status lamp) mounted on a short angled gunmetal grip with a
trigger. Silhouette = little crate-on-a-grip. 32x32 RGBA, transparent bg."""

import os
from PIL import Image

DIR = "/private/tmp/claude-501/-Users-meguneri-Programming-wega-mega/81e85a2a-b0cc-459d-b346-21f4db814bda/scratchpad/cand_cratefob"
os.makedirs(DIR, exist_ok=True)

W = H = 32
px = {}  # (x,y) -> (r,g,b)

# ---- palette (muted, desaturated grungy sci-fi) ----
AMBER      = (196, 128, 52)
AMBER_HI   = (226, 166, 88)
AMBER_SH   = (150, 92, 34)

STRAP      = (70, 52, 34)
STRAP_HI   = (96, 74, 50)
STRAP_SH   = (46, 33, 20)

METAL      = (150, 148, 140)
METAL_HI   = (196, 194, 186)
METAL_SH   = (98, 96, 90)
LAMP       = (244, 184, 74)
LAMP_HI    = (255, 224, 150)

GRIP       = (76, 80, 88)
GRIP_HI    = (106, 110, 120)
GRIP_SH    = (50, 52, 60)
TRIG       = (58, 60, 68)

OUTLINE    = (34, 27, 20)


def put(x, y, c):
    if 0 <= x < W and 0 <= y < H:
        px[(x, y)] = c


def rect(x0, y0, x1, y1, c):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            put(x, y, c)


# =========================================================
# CRATE (front face)  x:8..23  y:4..18
# =========================================================
CX0, CX1, CY0, CY1 = 8, 23, 4, 18
rect(CX0, CY0, CX1, CY1, AMBER)

# top-left highlight edges
for x in range(CX0, CX1 + 1):
    put(x, CY0, AMBER_HI)          # top row
for y in range(CY0, CY1 + 1):
    put(CX0, y, AMBER_HI)          # left col
# bottom-right shadow edges
for x in range(CX0, CX1 + 1):
    put(x, CY1, AMBER_SH)          # bottom row
for y in range(CY0, CY1 + 1):
    put(CX1, y, AMBER_SH)          # right col

# corner rivets
for (rx, ry) in [(CX0 + 1, CY0 + 1), (CX1 - 1, CY0 + 1),
                 (CX0 + 1, CY1 - 1), (CX1 - 1, CY1 - 1)]:
    put(rx, ry, STRAP_SH)

# ---- vertical strap  x:14..17 ----
for y in range(CY0, CY1 + 1):
    for x in range(14, 18):
        c = STRAP
        if x == 14:
            c = STRAP_HI
        elif x == 17:
            c = STRAP_SH
        put(x, y, c)

# ---- horizontal strap  y:9..11 ----
for x in range(CX0, CX1 + 1):
    for y in range(9, 12):
        c = STRAP
        if y == 9:
            c = STRAP_HI
        elif y == 11:
            c = STRAP_SH
        put(x, y, c)

# ---- clasp (metal latch) at strap crossing  x:12..19 y:8..13 ----
LX0, LX1, LY0, LY1 = 12, 19, 8, 13
rect(LX0, LY0, LX1, LY1, METAL)
for x in range(LX0, LX1 + 1):
    put(x, LY0, METAL_HI)
for y in range(LY0, LY1 + 1):
    put(LX0, y, METAL_HI)
for x in range(LX0, LX1 + 1):
    put(x, LY1, METAL_SH)
for y in range(LY0, LY1 + 1):
    put(LX1, y, METAL_SH)
# amber status lamp in the clasp centre
put(15, 10, LAMP_HI)
put(16, 10, LAMP)
put(15, 11, LAMP)
put(16, 11, LAMP_SH := (198, 138, 52))

# =========================================================
# GRIP (angled, gunmetal) below the crate
# =========================================================
grip_rows = {
    19: (12, 18),
    20: (12, 18),
    21: (13, 19),
    22: (13, 19),
    23: (14, 20),
    24: (14, 20),
    25: (15, 20),
    26: (15, 20),
    27: (15, 19),
    28: (16, 19),
}
for y, (gx0, gx1) in grip_rows.items():
    for x in range(gx0, gx1 + 1):
        c = GRIP
        if x == gx0:
            c = GRIP_HI
        elif x == gx1:
            c = GRIP_SH
        put(x, y, c)

# =========================================================
# TRIGGER: small nub jutting to the lower-left of the grip
# =========================================================
trigger = [
    (10, 21), (11, 21), (12, 21),
    (9, 22), (10, 22), (11, 22),
    (10, 23), (11, 23),
]
for (x, y) in trigger:
    put(x, y, TRIG)
put(11, 21, GRIP_HI)  # tiny highlight on trigger top

# =========================================================
# OUTLINE: 4-neighbour dark border around the whole silhouette
# =========================================================
filled = set(px.keys())
outline_cells = set()
for (x, y) in filled:
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        n = (x + dx, y + dy)
        if 0 <= n[0] < W and 0 <= n[1] < H and n not in filled:
            outline_cells.add(n)
for (x, y) in outline_cells:
    put(x, y, OUTLINE)

# =========================================================
# render
# =========================================================
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
for (x, y), c in px.items():
    img.putpixel((x, y), (c[0], c[1], c[2], 255))

img.save(os.path.join(DIR, "icon.png"))
img.resize((256, 256), Image.NEAREST).save(os.path.join(DIR, "icon_8x.png"))
print("saved icon.png and icon_8x.png; filled px:", len(filled))
