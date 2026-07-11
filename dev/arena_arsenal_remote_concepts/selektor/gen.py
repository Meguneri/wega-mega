#!/usr/bin/env python3
"""
Tiered rotary selector module - 32x32 SS14 pixel-art icon.

Silhouette: a chunky OCTAGONAL industrial dial housing with ONE big central
rotary knob + pointer, ringed by 4 tier LEDs (3 amber tiers + 1 blue melee).
Deliberately NOT a tall rounded rectangle with a button grid (that's the
remote we must not resemble).
"""

import os
from PIL import Image

DIR = "/private/tmp/claude-501/-Users-meguneri-Programming-wega-mega/81e85a2a-b0cc-459d-b346-21f4db814bda/scratchpad/cand_selector"
os.makedirs(DIR, exist_ok=True)

W = H = 32
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
px = img.load()

# ---- palette (muted, desaturated station sci-fi) ----
OUTLINE   = (18, 21, 25, 255)    # near-black blue-gray, silhouette outline
HOUSE_HI  = (86, 96, 105, 255)   # top-left bevel highlight
HOUSE_MID = (60, 68, 76, 255)    # main housing face
HOUSE_LO  = (40, 46, 52, 255)    # bottom-right shadow face
HOUSE_DK  = (30, 35, 40, 255)    # inner recess

RING_DK   = (24, 28, 32, 255)    # dark recessed ring under knob
RING_HI   = (72, 81, 90, 255)

KNOB_HI   = (110, 120, 130, 255) # knob top-left
KNOB_MID  = (74, 83, 92, 255)    # knob face
KNOB_LO   = (48, 55, 62, 255)    # knob bottom-right
KNOB_DK   = (28, 33, 38, 255)    # knob outline

PTR_HI    = (255, 214, 140, 255) # pointer amber bright
PTR_MID   = (232, 161, 58, 255)  # pointer amber

AMB_CORE  = (255, 208, 120, 255)
AMB_MID   = (231, 150, 44, 255)
AMB_LO    = (150, 92, 26, 255)
AMB_DIM   = (120, 78, 32, 255)    # unlit-ish amber (dimmer tiers)
AMB_DIMC  = (186, 126, 52, 255)

BLU_CORE  = (168, 224, 255, 255)
BLU_MID   = (78, 160, 210, 255)
BLU_LO    = (40, 96, 140, 255)

SCREW_HI  = (96, 106, 116, 255)
SCREW_DK  = (34, 39, 44, 255)


def s(x, y, c):
    if 0 <= x < W and 0 <= y < H:
        px[x, y] = c


# ---------------------------------------------------------------------------
# 1) Octagonal housing mask  (bounds 4..27, corner bevel = 5)
# ---------------------------------------------------------------------------
LO, HIb, BEV = 4, 27, 5
house = set()
for y in range(LO, HIb + 1):
    for x in range(LO, HIb + 1):
        if (x - LO) + (y - LO) < BEV:            # TL corner
            continue
        if (HIb - x) + (y - LO) < BEV:           # TR corner
            continue
        if (x - LO) + (HIb - y) < BEV:           # BL corner
            continue
        if (HIb - x) + (HIb - y) < BEV:          # BR corner
            continue
        house.add((x, y))


def is_edge(mask, x, y):
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        if (x + dx, y + dy) not in mask:
            return True
    return False


# Fill housing with directional shading, edge -> outline color.
cx = cy = 15.5
for (x, y) in house:
    if is_edge(house, x, y):
        s(x, y, OUTLINE)
        continue
    # diagonal light: up-left brighter, down-right darker
    d = (x - cx) + (y - cy)
    if d < -6:
        c = HOUSE_HI
    elif d > 8:
        c = HOUSE_LO
    else:
        c = HOUSE_MID
    s(x, y, c)

# corner screws (inside the octagon flats) for industrial feel
for (sx, sy) in ((8, 8), (23, 8), (8, 23), (23, 23)):
    if (sx, sy) in house:
        s(sx, sy, SCREW_DK)
        s(sx, sy - 0, SCREW_DK)
        s(sx, sy, SCREW_DK)
        s(sx, sy, SCREW_DK)
        s(sx, sy, SCREW_HI)  # will be overwritten below with hi dot
# give each screw a tiny highlight dot
for (sx, sy) in ((8, 8), (23, 8), (8, 23), (23, 23)):
    if (sx, sy) in house:
        s(sx, sy, SCREW_DK)
        s(sx, sy, SCREW_DK)
        s(sx, sy, SCREW_HI)


# ---------------------------------------------------------------------------
# 2) Recessed ring + central rotary knob
# ---------------------------------------------------------------------------
def disc(cxf, cyf, r):
    out = set()
    for y in range(H):
        for x in range(W):
            if (x + 0.5 - cxf) ** 2 + (y + 0.5 - cyf) ** 2 <= r * r:
                out.add((x, y))
    return out


ring = disc(cx, cy, 7.4)
knob = disc(cx, cy, 5.6)

# dark recessed ring (between knob and housing)
for (x, y) in ring:
    if (x, y) in knob:
        continue
    s(x, y, RING_DK)
# a thin top highlight on the recess rim
for (x, y) in ring:
    if (x, y) in knob:
        continue
    if is_edge(ring, x, y) and (y - cy) < -3:
        s(x, y, RING_HI)

# knob body
for (x, y) in knob:
    if is_edge(knob, x, y):
        s(x, y, KNOB_DK)
        continue
    d = (x - cx) + (y - cy)
    if d < -4:
        c = KNOB_HI
    elif d > 5:
        c = KNOB_LO
    else:
        c = KNOB_MID
    s(x, y, c)


# ---------------------------------------------------------------------------
# 3) Pointer: a clear amber NEEDLE/arrow on the knob aiming UP
#    (toward the currently-selected top LED).  Kept in the upper half of the
#    knob + a small raised hub, so it reads as a hand, not a plus sign.
# ---------------------------------------------------------------------------
# small raised metal hub in the knob center
s(15, 15, KNOB_HI)
s(16, 15, KNOB_MID)
s(15, 16, KNOB_MID)
s(16, 16, KNOB_LO)
# arrowhead (pointing up)
s(16, 11, PTR_HI)          # tip
s(15, 12, PTR_MID)
s(16, 12, PTR_HI)
s(17, 12, PTR_MID)
s(14, 13, PTR_MID)         # wide barbs of the arrowhead
s(18, 13, PTR_MID)
# needle stem from arrowhead down to the hub
s(16, 13, PTR_HI)
s(16, 14, PTR_MID)


# ---------------------------------------------------------------------------
# 4) Four tier LEDs at N / E / S / W (N = blue melee, selected & lit)
# ---------------------------------------------------------------------------
def led(cxp, cyp, core, mid, lo, lit=True):
    # 2x2 core with a rim -> reads as a small round lamp
    if lit:
        s(cxp, cyp, core)
        s(cxp + 1, cyp, mid)
        s(cxp, cyp + 1, mid)
        s(cxp + 1, cyp + 1, lo)
    else:
        s(cxp, cyp, mid)
        s(cxp + 1, cyp, lo)
        s(cxp, cyp + 1, lo)
        s(cxp + 1, cyp + 1, lo)
    # dark socket rim (top + left) for seated look
    s(cxp - 1, cyp, OUTLINE)
    s(cxp, cyp - 1, OUTLINE)
    s(cxp + 2, cyp + 1, OUTLINE)
    s(cxp + 1, cyp + 2, OUTLINE)


# N (top) - blue melee tier, currently selected (bright)
led(15, 6, BLU_CORE, BLU_MID, BLU_LO, lit=True)
# E (right) - amber tier
led(23, 15, AMB_CORE, AMB_MID, AMB_LO, lit=True)
# S (bottom) - amber tier (dimmer / unselected)
led(15, 24, AMB_DIMC, AMB_DIM, AMB_DIM, lit=False)
# W (left) - amber tier (dimmer / unselected)
led(7, 15, AMB_DIMC, AMB_DIM, AMB_DIM, lit=False)


# ---------------------------------------------------------------------------
# save
# ---------------------------------------------------------------------------
out1 = os.path.join(DIR, "icon.png")
img.save(out1)

big = img.resize((256, 256), Image.NEAREST)
big.save(os.path.join(DIR, "icon_8x.png"))

print("saved", out1)
