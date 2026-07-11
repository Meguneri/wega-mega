#!/usr/bin/env python3
"""Supply datapad / slate icon for SS14 (32x32 pixel art).

A flat, thin, landscape-oriented handheld datapad (rounded dark slate).
The face is mostly a screen showing an amber supply-CRATE glyph and a
small segmented TC tier readout bar. One slim physical button sits on the
right bezel. Silhouette = flat slate, clearly a screen device -- NOT a
tall button-remote.
"""

import os
from PIL import Image

DIR = "/private/tmp/claude-501/-Users-meguneri-Programming-wega-mega/81e85a2a-b0cc-459d-b346-21f4db814bda/scratchpad/cand_datapad"
os.makedirs(DIR, exist_ok=True)

W = H = 32
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
px = img.load()

# ---- palette (muted, slightly desaturated sci-fi) ----
OUT       = (26, 30, 36, 255)    # silhouette outline
BODY      = (74, 84, 96, 255)    # slate base
HI        = (106, 118, 130, 255) # top-left highlight
SH        = (50, 58, 68, 255)    # bottom-right shadow

SCR_BORD  = (40, 48, 60, 255)    # screen bezel/inner border
SCR_BG    = (20, 26, 36, 255)    # screen background (dark)
SCR_BG_HI = (28, 36, 48, 255)    # slight top glow of screen

AMBER     = (222, 150, 58, 255)  # supply accent (slightly muted)
AMBER_HI  = (244, 186, 98, 255)
AMBER_DK  = (162, 104, 38, 255)
AMBER_DIM = (96, 74, 46, 255)    # unlit tier segment (a bit more visible)

BTN       = (94, 104, 116, 255)  # physical button base
BTN_HI    = (126, 138, 150, 255)
BTN_SH    = (56, 64, 74, 255)


def set_px(x, y, c):
    if 0 <= x < W and 0 <= y < H:
        px[x, y] = c


def in_bounds(x, y):
    return 0 <= x < W and 0 <= y < H


# ---- body: rounded-rect slate, landscape (wider than tall) ----
# interior rounded rectangle including its outline ring
X0, Y0, X1, Y1 = 3, 8, 28, 23   # inclusive; 26 wide x 16 tall (landscape)
R = 3                            # corner radius


def in_body(x, y):
    if not (X0 <= x <= X1 and Y0 <= y <= Y1):
        return False
    # rounded corners: check distance to each corner centre
    cx = None
    cy = None
    if x < X0 + R and y < Y0 + R:
        cx, cy = X0 + R, Y0 + R
    elif x > X1 - R and y < Y0 + R:
        cx, cy = X1 - R, Y0 + R
    elif x < X0 + R and y > Y1 - R:
        cx, cy = X0 + R, Y1 - R
    elif x > X1 - R and y > Y1 - R:
        cx, cy = X1 - R, Y1 - R
    if cx is not None:
        # distance test (use radius+0.5 for a fuller round look)
        if (x - cx) ** 2 + (y - cy) ** 2 > (R + 0.4) ** 2:
            return False
    return True


body = set()
for y in range(H):
    for x in range(W):
        if in_body(x, y):
            body.add((x, y))

# fill base
for (x, y) in body:
    set_px(x, y, BODY)

# outline: body pixels touching a non-body 4-neighbour
outline = set()
for (x, y) in body:
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        if (x + dx, y + dy) not in body:
            outline.add((x, y))
            break
for (x, y) in outline:
    set_px(x, y, OUT)

interior = body - outline

# highlight (top / left interior edge) and shadow (bottom / right interior edge)
for (x, y) in interior:
    up_out = (x, y - 1) in outline
    left_out = (x - 1, y) in outline
    down_out = (x, y + 1) in outline
    right_out = (x + 1, y) in outline
    if up_out or left_out:
        set_px(x, y, HI)
    elif down_out or right_out:
        set_px(x, y, SH)


# ---- screen (inset, occupies most of the face) ----
SX0, SY0, SX1, SY1 = 6, 10, 22, 21   # inclusive; 17 wide x 12 tall

# screen bezel (1px darker frame just outside screen)
for x in range(SX0 - 1, SX1 + 2):
    for y in range(SY0 - 1, SY1 + 2):
        if x in (SX0 - 1, SX1 + 1) or y in (SY0 - 1, SY1 + 1):
            if (x, y) in interior:
                set_px(x, y, SCR_BORD)

# screen background with a subtle top glow band
for x in range(SX0, SX1 + 1):
    for y in range(SY0, SY1 + 1):
        if y <= SY0 + 1:
            set_px(x, y, SCR_BG_HI)
        else:
            set_px(x, y, SCR_BG)


# ---- amber supply CRATE glyph ----
# a box with a border frame and corner cross-braces (an X), reads as cargo.
CX0, CY0, CX1, CY1 = 8, 11, 15, 17   # crate box, 8 wide x 7 tall

# crate border frame
for x in range(CX0, CX1 + 1):
    set_px(x, CY0, AMBER)
    set_px(x, CY1, AMBER)
for y in range(CY0, CY1 + 1):
    set_px(CX0, y, AMBER)
    set_px(CX1, y, AMBER)

# fill interior of crate faintly (dim amber) to read as a solid box
for x in range(CX0 + 1, CX1):
    for y in range(CY0 + 1, CY1):
        set_px(x, y, AMBER_DK)

# diagonal cross-braces (X) in bright amber
w = CX1 - CX0
h = CY1 - CY0
for i in range(0, w + 1):
    # main diagonal top-left -> bottom-right
    x = CX0 + i
    y = CY0 + round(i * h / w)
    if CX0 < x < CX1 and CY0 < y < CY1:
        set_px(x, y, AMBER)
    # anti diagonal top-right -> bottom-left
    x2 = CX1 - i
    y2 = CY0 + round(i * h / w)
    if CX0 < x2 < CX1 and CY0 < y2 < CY1:
        set_px(x2, y2, AMBER)

# top-left corner highlight on crate for a bit of pop
set_px(CX0, CY0, AMBER_HI)
set_px(CX0 + 1, CY0, AMBER_HI)
set_px(CX0, CY0 + 1, AMBER_HI)


# ---- TC tier readout bar (segmented) ----
# a short horizontal row of segments to the right of the crate glyph,
# stacked so it clearly reads as a level/tier meter. 3 lit + 1 unlit.
bar_x = 17
seg_top = 12
seg_h = 1
lit = [True, True, True, False]   # bottom-up tiers; last (top) unlit
# draw as 4 stacked segments, each 4px wide, growing = tier meter feel
seg_w = 4
gap = 1
for idx, on in enumerate(lit):
    y = SY1 - 1 - idx * (seg_h + gap)   # stack upward from screen bottom
    c = AMBER if on else AMBER_DIM
    hi = AMBER_HI if on else AMBER_DIM
    for k in range(seg_w):
        set_px(bar_x + k, y, c)
    set_px(bar_x, y, hi)  # left cap brighter when lit


# ---- slim physical button on the right bezel ----
# vertical pill in the right margin, outside the screen.
BX = 25
for y in range(12, 20):
    set_px(BX, y, BTN)
    set_px(BX + 1, y, BTN_SH)
# top cap highlight, bottom cap shadow, lit inner edge
set_px(BX, 12, BTN_HI)
set_px(BX + 1, 12, BTN)
set_px(BX, 19, BTN_SH)
set_px(BX + 1, 19, BTN_SH)
for y in range(13, 19):
    set_px(BX, y, BTN_HI)


# ---- save ----
img.save(os.path.join(DIR, "icon.png"))
img.resize((W * 8, H * 8), Image.NEAREST).save(os.path.join(DIR, "icon_8x.png"))
print("saved icon.png and icon_8x.png to", DIR)
