#!/usr/bin/env python3
"""Procedural generator for the adaptive (Mahoraga) armor visuals.

Outputs three things, all supersampled (drawn at 8x then LANCZOS-downscaled for
crisp baked anti-aliasing):

  * mahoraga_wheel.rsi  - multi-layer Dharmachakra: glow / ring / spokes / seg0..8
  * adaptive_shockwave.rsi - one-shot expanding ring played on absorb
  * adaptive_armor.rsi equipped-OUTERCLOTHING-glow[-vox] - emissive rim derived
    from the existing equipped sprites so it always lines up on the body.

Run from the repo root: `python gen_adaptive_sprites.py`
"""
import json
import math
import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.abspath(__file__))
TEX = os.path.join(ROOT, "Resources", "Textures", "_Wega")
WHEEL_DIR = os.path.join(TEX, "Effects", "mahoraga_wheel.rsi")
SHOCK_DIR = os.path.join(TEX, "Effects", "adaptive_shockwave.rsi")
ARMOR_DIR = os.path.join(TEX, "Clothing", "OuterClothing", "Armor", "adaptive_armor.rsi")

CELL = 32          # native px per frame
SS = 8             # supersample factor
S = CELL * SS      # supersampled canvas
C = S / 2          # centre

# -- palette (RGBA) ----------------------------------------------------------
GOLD = (232, 178, 60, 255)
GOLD_HI = (255, 226, 140, 255)
GOLD_LO = (150, 103, 30, 255)
OUTLINE = (60, 40, 14, 255)
LIT = (255, 240, 200, 255)
DIM = (120, 92, 40, 150)


def u(v):
    """32-space units -> supersampled px."""
    return v * SS


def cell():
    return Image.new("RGBA", (S, S), (0, 0, 0, 0))


def down(img):
    return img.resize((CELL, CELL), Image.LANCZOS)


def scale_alpha(img, factor):
    r, g, b, a = img.split()
    return Image.merge("RGBA", (r, g, b, a.point(lambda v: int(v * factor))))


def circle(draw, cx, cy, r, fill):
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)


# -- glow --------------------------------------------------------------------
def draw_glow(pulse):
    img = cell()
    d = ImageDraw.Draw(img)
    rmax = u(15.5)
    steps = 140
    for i in range(steps):
        r = rmax * (1 - i / (steps - 1))     # outer -> inner
        rr = r / SS                          # back to 32-space
        # Bright coloured halo peaked just outside the wheel (so it isn't hidden behind the gold), plus a
        # soft inner fill so the colour also shows between the spokes. Pure white -> the type tint reads true.
        halo = math.exp(-((rr - 12.5) / 4.0) ** 2)
        fill = max(0.0, 1.0 - rr / 13.0)
        a = int(min(255.0, (235.0 * halo + 70.0 * fill)) * pulse)
        circle(d, C, C, r, (255, 255, 255, max(0, a)))
    return down(img.filter(ImageFilter.GaussianBlur(SS * 0.6)))


def draw_glow_sector(start_deg, end_deg, pulse):
    """Same glow as draw_glow but masked to a pie-slice sector [start_deg, end_deg].
    Angles follow PIL convention: 0° = 3 o'clock, increasing clockwise."""
    img = cell()
    d = ImageDraw.Draw(img)
    rmax = u(15.5)
    steps = 140
    for i in range(steps):
        r = rmax * (1 - i / (steps - 1))
        rr = r / SS
        halo = math.exp(-((rr - 12.5) / 4.0) ** 2)
        fill_v = max(0.0, 1.0 - rr / 13.0)
        a = int(min(255.0, (235.0 * halo + 70.0 * fill_v)) * pulse)
        d.ellipse([C - r, C - r, C + r, C + r], fill=(255, 255, 255, max(0, a)))
    img = img.filter(ImageFilter.GaussianBlur(SS * 0.6))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).pieslice([0, 0, S - 1, S - 1], start=start_deg, end=end_deg, fill=255)
    r_ch, g_ch, b_ch, a_ch = img.split()
    return down(Image.merge("RGBA", (r_ch, g_ch, b_ch, ImageChops.multiply(a_ch, mask))))


HUB_R = u(2.3)
RIM_R = u(8.2)
RIM_BAND = u(1.0)
SPOKE_TIP = u(9.7)         # spokes poke past the rim (the rotating "направляющие")
SPOKE_HW = u(1.3)          # spoke half-width


# -- only the spokes (these are the part that spins) ------------------------
# Bold and high-contrast (dark outline + gold + bright centreline) so the guides read clearly while
# they turn inside the static rim.
def draw_spokes():
    img = cell()
    d = ImageDraw.Draw(img)
    for k in range(8):
        ang = math.radians(k * 45 - 90)
        ca, sa = math.cos(ang), math.sin(ang)
        pa = ang + math.pi / 2
        cp, sp = math.cos(pa), math.sin(pa)

        def spoke(width, r0, r1, fill):
            d.polygon([
                (C + ca * r0 + cp * width, C + sa * r0 + sp * width),
                (C + ca * r1 + cp * width, C + sa * r1 + sp * width),
                (C + ca * r1 - cp * width, C + sa * r1 - sp * width),
                (C + ca * r0 - cp * width, C + sa * r0 - sp * width),
            ], fill=fill)

        spoke(SPOKE_HW + u(0.7), u(1.4), SPOKE_TIP, OUTLINE)   # thick dark outline so they pop on the glow
        spoke(SPOKE_HW, u(1.4), SPOKE_TIP, GOLD)               # gold body
        d.line([(C + ca * u(1.8), C + sa * u(1.8)),
                (C + ca * (SPOKE_TIP - u(0.6)), C + sa * (SPOKE_TIP - u(0.6)))],
               fill=GOLD_HI, width=int(u(0.8)))                # bright centreline

    # the eight rings ride the spoke tips — always all lit, and turn with the spokes
    halo_r = u(11.7)
    ring_r = u(1.6)
    thick = u(0.9)
    for k in range(8):
        ang = math.radians(k * 45 - 90)
        cx, cy = C + math.cos(ang) * halo_r, C + math.sin(ang) * halo_r
        circle(d, cx, cy, ring_r + thick * 0.5, (255, 240, 200, 80))  # soft core glow
        d.ellipse([cx - ring_r - thick, cy - ring_r - thick,
                   cx + ring_r + thick, cy + ring_r + thick],
                  outline=OUTLINE, width=int(thick) + SS)
        d.ellipse([cx - ring_r, cy - ring_r, cx + ring_r, cy + ring_r],
                  outline=LIT, width=int(thick))
    return down(img.filter(ImageFilter.GaussianBlur(SS * 0.15)))


# -- the static frame: rim + hub (these stay put while the spokes turn) ------
def draw_frame():
    img = cell()
    d = ImageDraw.Draw(img)
    d.ellipse([C - RIM_R - RIM_BAND, C - RIM_R - RIM_BAND, C + RIM_R + RIM_BAND, C + RIM_R + RIM_BAND],
              outline=OUTLINE, width=int(RIM_BAND) + SS)
    d.ellipse([C - RIM_R - RIM_BAND, C - RIM_R - RIM_BAND, C + RIM_R + RIM_BAND, C + RIM_R + RIM_BAND],
              outline=GOLD, width=int(RIM_BAND))
    circle(d, C, C, HUB_R + u(0.45), OUTLINE)
    circle(d, C, C, HUB_R, GOLD)
    circle(d, C, C, u(1.1), GOLD_HI)
    return down(img)


# -- shockwave (expanding ring) ---------------------------------------------
def draw_shock(t):
    img = cell()
    d = ImageDraw.Draw(img)
    r = u(2.5 + t * 12.5)
    width = int(u(2.4 * (1 - t * 0.5)))
    a = int(255 * (1 - t) ** 1.3)
    d.ellipse([C - r, C - r, C + r, C + r], outline=(255, 245, 220, a), width=max(1, width))
    inner = int(a * 0.5)
    d.ellipse([C - r, C - r, C + r, C + r], outline=(255, 255, 255, inner),
              width=max(1, width // 2))
    img = img.filter(ImageFilter.GaussianBlur(SS * 0.4))
    return down(img)


# -- sheet packing -----------------------------------------------------------
def pack(frames, cols):
    rows = math.ceil(len(frames) / cols)
    sheet = Image.new("RGBA", (cols * CELL, rows * CELL), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        sheet.paste(f, ((i % cols) * CELL, (i // cols) * CELL))
    return sheet


def save_png(img, path):
    img.save(path)
    print("wrote", os.path.relpath(path, ROOT))


# -- armor rim glow derived from equipped sprite ----------------------------
def derive_glow(src_path, dst_path):
    """Build the emissive accent from the armour's *internal* detail rather than its outline.

    The base sprite carries a built-in "lattice of micro-cells" — warm amber/brown pixels (the
    adaptive-cell recolour) running down the chest — plus bright metal highlights on the plates.
    We light those specific parts up, so adapting makes the embedded cells and plate facets glow
    individually instead of flooding a single colour around the whole silhouette. White base so the
    type tint (applied via ToggleableVisuals.Color, unshaded) reads true."""
    src = Image.open(src_path).convert("RGBA")
    w, h = src.size
    cols, rows = w // CELL, h // CELL
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    for cy in range(rows):
        for cx in range(cols):
            box = (cx * CELL, cy * CELL, cx * CELL + CELL, cy * CELL + CELL)
            tile = src.crop(box)
            px = tile.load()
            # Per-pixel emission strength: the warm cells are the heart of the effect (full), the bright
            # plate highlights catch a little of the energy too (half) so the glow reads as discrete lit
            # nodes scattered over the armour, not a continuous edge.
            core = Image.new("L", tile.size, 0)
            cpx = core.load()
            for y in range(CELL):
                for x in range(CELL):
                    r, g, b, a = px[x, y]
                    if a == 0:
                        continue
                    if r > g >= b and r - b > 25:        # amber/brown adaptive cell
                        cpx[x, y] = 255
                    elif r + g + b > 600:                # bright metal highlight on a plate facet
                        cpx[x, y] = 120
            # Soft bloom so each lit cell blooms into a small node rather than a hard pixel, then a faint
            # halo of the same map at low strength to bind neighbouring cells without washing the vest out.
            node = core.filter(ImageFilter.GaussianBlur(0.6))
            halo = core.filter(ImageFilter.GaussianBlur(1.6)).point(lambda v: int(v * 0.35))
            glow = ImageChops.lighter(node, halo)
            white = Image.new("RGBA", tile.size, (255, 255, 255, 255))
            white.putalpha(glow)
            out.paste(white, box)
    out.save(dst_path)
    print("wrote", os.path.relpath(dst_path, ROOT))


def write_meta(path, states, copyright_):
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": copyright_,
        "size": {"x": CELL, "y": CELL},
        "states": states,
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)
    print("wrote", os.path.relpath(path, ROOT))


def main():
    os.makedirs(SHOCK_DIR, exist_ok=True)

    # --- wheel -------------------------------------------------------------
    # Single-frame layers: the wheel stands still and only spins (once, in code) on adaptation, like
    # the canonical Mahoraga wheel. The ring/spokes are rotated by AdaptiveWheelVisualsSystem.
    # The layers are unshaded (self-lit), so over a dark background they look bright even at reduced alpha.
    # Push the alpha well down so the translucency actually reads: wheel ~60% opacity, glow ~40%.
    WHEEL_OPACITY = 0.35   # wheel ~65% transparent
    GLOW_OPACITY = 0.45    # damage-colour sectors — bumped so adjacent hues (e.g. Slash/Heat) read distinctly
    save_png(scale_alpha(draw_frame(), WHEEL_OPACITY), os.path.join(WHEEL_DIR, "frame.png"))
    save_png(scale_alpha(draw_spokes(), WHEEL_OPACITY), os.path.join(WHEEL_DIR, "spokes.png"))

    # 8 sector glow slices (45° each, sector 0 starts at 3 o'clock going clockwise).
    # Together they tile the full circle; the client colours each sector independently
    # based on which damage types the armour is currently adapted to.
    for n in range(8):
        save_png(draw_glow_sector(n * 45, (n + 1) * 45, GLOW_OPACITY),
                 os.path.join(WHEEL_DIR, f"glow_sec{n}.png"))

    stale = ["glow.png", "wheel.png", "ring.png"] + [f"seg{n}.png" for n in range(9)]
    for name in stale:
        path = os.path.join(WHEEL_DIR, name)
        if os.path.exists(path):
            os.remove(path)
            print("removed", name)

    states = [{"name": "frame"}, {"name": "spokes"}] + [{"name": f"glow_sec{n}"} for n in range(8)]
    write_meta(os.path.join(WHEEL_DIR, "meta.json"), states,
               "Procedurally drawn for the Wega fork - Mahoraga adaptation wheel "
               "(Dharmachakra) for the adaptive arena armor.")

    # --- shockwave ---------------------------------------------------------
    shock_frames = [draw_shock(i / 5) for i in range(6)]
    save_png(pack(shock_frames, 3), os.path.join(SHOCK_DIR, "pulse.png"))
    write_meta(os.path.join(SHOCK_DIR, "meta.json"),
               [{"name": "pulse", "delays": [[0.09] * 6]}],
               "Procedurally drawn for the Wega fork - adaptive armor absorb shockwave.")

    # --- armor rim glow ----------------------------------------------------
    derive_glow(os.path.join(ARMOR_DIR, "equipped-OUTERCLOTHING.png"),
                os.path.join(ARMOR_DIR, "equipped-OUTERCLOTHING-glow.png"))
    derive_glow(os.path.join(ARMOR_DIR, "equipped-OUTERCLOTHING-vox.png"),
                os.path.join(ARMOR_DIR, "equipped-OUTERCLOTHING-glow-vox.png"))


if __name__ == "__main__":
    main()
