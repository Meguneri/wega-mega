import os
from PIL import Image

DIR = "/private/tmp/claude-501/-Users-meguneri-Programming-wega-mega/81e85a2a-b0cc-459d-b346-21f4db814bda/scratchpad/cand_beacon"
os.makedirs(DIR, exist_ok=True)

W = H = 32
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
px = img.load()

# ---------------- palette (muted, desaturated grungy sci-fi) ----------------
OUT   = (24, 26, 30, 255)     # silhouette outline
D0    = (44, 48, 55, 255)     # darkest body / recess
D1    = (58, 63, 72, 255)     # body shadow edge
D2    = (74, 80, 90, 255)     # body base
D3    = (96, 104, 116, 255)   # body highlight
HI    = (124, 134, 148, 255)  # bright metal edge
STEEL = (150, 160, 172, 255)  # antenna / dial rim metal
STEEL_D = (96, 104, 116, 255) # antenna shadow side

AMB    = (236, 156, 46, 255)  # amber accent
AMB_HI = (255, 198, 96, 255)  # amber bright / lit LED
AMB_D  = (158, 92, 22, 255)   # amber shadow
LED_OFF = (104, 66, 26, 255)  # dim/unlit amber

HAZ_Y = (198, 150, 54, 255)   # hazard yellow (muted)
HAZ_K = (40, 40, 46, 255)     # hazard black


def put(x, y, c):
    if 0 <= x < W and 0 <= y < H:
        px[x, y] = c


def disc(cx, cy, r, c, fudge=1):
    for y in range(cy - r, cy + r + 1):
        for x in range(cx - r, cx + r + 1):
            if (x - cx) ** 2 + (y - cy) ** 2 <= r * r + fudge:
                put(x, y, c)


# ---------------- build the SOLID silhouette mask ----------------
solid = set()

def add_rect(x0, y0, x1, y1):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            solid.add((x, y))

# Box body: WIDER-THAN-TALL. solid interior; outline will wrap it by 1px.
BX0, BY0, BX1, BY1 = 5, 13, 26, 26        # 22 wide x 14 tall  -> silhouette 24x16
add_rect(BX0, BY0, BX1, BY1)

# Chamfer the two top corners 1px for a rugged (non-rounded) bevel
for c in [(BX0, BY0), (BX1, BY0)]:
    solid.discard(c)

# Antenna: stubby diagonal rod rising from top-right, 1px stepped core.
rod = [(23, 12), (24, 11), (25, 10), (26, 9), (27, 8), (28, 7)]
for c in rod:
    solid.add(c)
# small mount foot on the box top
for c in [(22, 12), (23, 12)]:
    solid.add(c)
# amber signal tip (part of silhouette)
TIP = (28, 6)
solid.add(TIP)

# ---------------- paint body base ----------------
for (x, y) in solid:
    put(x, y, D2)

# ---------------- generate 1px outline around whole silhouette ----------------
outline = set()
for (x, y) in solid:
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            if dx == 0 and dy == 0:
                continue
            n = (x + dx, y + dy)
            if n not in solid:
                outline.add(n)
for (x, y) in outline:
    put(x, y, OUT)

# ---------------- body shading (top-left highlight / bottom-right shadow) ----------------
# inner top edge highlight
for x in range(BX0 + 1, BX1):
    put(x, BY0, D3)
put(BX0 + 1, BY0, HI)          # bright top-left nub
# inner left edge highlight
for y in range(BY0, BY1):
    put(BX0, y, D3)
# inner bottom edge shadow
for x in range(BX0, BX1 + 1):
    put(x, BY1, D1)
# inner right edge shadow
for y in range(BY0 + 1, BY1 + 1):
    put(BX1, y, D1)
put(BX1, BY1, D0)              # dark bottom-right corner

# a faint horizontal seam under the header for panel definition
for x in range(BX0 + 1, BX1):
    if (BX0 + 1) <= x <= (BX1 - 1):
        put(x, BY0 + 2, D1)
put(BX0, BY0 + 2, D3)

# ---------------- rotary tier-dial (left) ----------------
DCX, DCY = 10, 19
disc(DCX, DCY, 3, OUT, fudge=1)          # dark bezel ring
disc(DCX, DCY, 3, STEEL_D, fudge=-1)     # rim base
# clean rim: highlight top-left, shade bottom-right
put(DCX - 1, DCY - 2, STEEL); put(DCX - 2, DCY - 1, STEEL)
put(DCX,     DCY - 3, HI);     put(DCX - 3, DCY,     HI)
put(DCX + 1, DCY + 2, D0);     put(DCX + 2, DCY + 1, D0)
put(DCX,     DCY + 3, D0);     put(DCX + 3, DCY,     D0)
disc(DCX, DCY, 2, D0, fudge=-1)          # dark recessed face
disc(DCX, DCY, 1, D1, fudge=1)           # slightly lit knob center
# amber pointer wedge pointing up = current tier
put(DCX, DCY - 2, AMB_HI)
put(DCX, DCY - 1, AMB)
put(DCX, DCY,     AMB_D)                  # hub

# a slim vertical divider between dial zone and LED zone
for y in range(BY0 + 3, BY1):
    put(16, y, D1)
put(16, BY0 + 3, D3)

# ---------------- 3 stacked tier LEDs (right) ----------------
LX0, LX1 = 19, 22                        # LED width 4px
led_rows = [15, 18, 21]                  # top, mid, bottom (2px tall each)
led_state = [AMB_HI, AMB, AMB]           # all lit (tier readout)
for ly, col in zip(led_rows, led_state):
    # dark recessed bezel
    for yy in range(ly - 1, ly + 3):
        for xx in range(LX0 - 1, LX1 + 2):
            put(xx, yy, D0)
    # LED body
    for yy in range(ly, ly + 2):
        for xx in range(LX0, LX1 + 1):
            put(xx, yy, col)
    put(LX0, ly, AMB_HI)                  # top-left specular
    put(LX1, ly + 1, AMB_D)              # bottom-right falloff

# ---------------- hazard stripe accent (bottom band) ----------------
hy0, hy1 = 24, 25
for y in range(hy0, hy1 + 1):
    for x in range(BX0, BX1 + 1):
        if ((x - y) % 4) < 2:
            put(x, y, HAZ_Y)
        else:
            put(x, y, HAZ_K)
# re-assert dark bottom outline-edge for grounding
for x in range(BX0, BX1 + 1):
    put(x, BY1, HAZ_K if px[x, BY1] == HAZ_K else AMB_D)

# ---------------- antenna metal shading + amber tip ----------------
for (x, y) in rod:
    put(x, y, STEEL)
# shade lower-left side of rod
put(23, 12, STEEL_D)
put(24, 11, STEEL_D)
# amber tip + glow
put(TIP[0], TIP[1], AMB_HI)
put(28, 7, AMB)                          # transition pixel below tip
# faint signal glow pixel (semi-transparent) just off the tip
put(27, 5, (255, 210, 120, 90))

# ---------------- save ----------------
out_path = os.path.join(DIR, "icon.png")
img.save(out_path)

big = img.resize((256, 256), Image.NEAREST)
big.save(os.path.join(DIR, "icon_8x.png"))
print("saved", out_path)
