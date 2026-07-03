import math, os, random
from PIL import Image, ImageDraw, ImageFilter
OUT = os.path.dirname(os.path.abspath(__file__))
SS = 6; CELL = 32; S = CELL * SS
random.seed(7)
def cell(c=(0, 0, 0, 0)): return Image.new("RGBA", (S, S), c)
def down(im): return im.resize((CELL, CELL), Image.LANCZOS)
def u(v): return int(v * SS)
def noisy(base, sigma, amt):
    b = Image.new("RGB", (S, S), base)
    n = Image.effect_noise((S, S), sigma).convert("L")
    n = Image.merge("RGB", (n, n, n))
    return Image.blend(b, n, amt).convert("RGBA")

# --- original 8 ---
def metal():
    im = cell((96, 100, 108, 255)); d = ImageDraw.Draw(im)
    for x in (0, S // 2):
        for y in (0, S // 2):
            w = S // 2
            d.rectangle([x + u(1), y + u(1), x + w - u(1), y + w - u(1)], fill=(110, 114, 122, 255))
            d.line([x + u(1), y + u(1), x + w - u(1), y + u(1)], fill=(142, 146, 154, 255), width=SS)
            d.line([x + u(1), y + u(1), x + u(1), y + w - u(1)], fill=(142, 146, 154, 255), width=SS)
            d.line([x + w - u(1), y + u(1), x + w - u(1), y + w - u(1)], fill=(68, 72, 80, 255), width=SS)
            d.line([x + u(1), y + w - u(1), x + w - u(1), y + w - u(1)], fill=(68, 72, 80, 255), width=SS)
            for rx in (x + u(3), x + w - u(3)):
                for ry in (y + u(3), y + w - u(3)):
                    d.ellipse([rx - u(1), ry - u(1), rx + u(1), ry + u(1)], fill=(155, 159, 167, 255))
    return down(im.filter(ImageFilter.GaussianBlur(SS * 0.2)))
def hazard():
    im = cell((28, 28, 30, 255)); d = ImageDraw.Draw(im); p = u(8)
    for i in range(-S, 2 * S, p * 2):
        d.polygon([(i, 0), (i + p, 0), (i + p - S, S), (i - S, S)], fill=(232, 192, 42, 255))
    return down(im)
def grating():
    im = cell((40, 42, 46, 255)); d = ImageDraw.Draw(im); bar = u(2); gap = u(8)
    for o in range(0, S, gap):
        d.rectangle([o, 0, o + bar, S], fill=(122, 126, 132, 255)); d.rectangle([0, o, S, o + bar], fill=(122, 126, 132, 255))
        d.line([o, 0, o, S], fill=(152, 156, 162, 255), width=SS // 2); d.line([0, o, S, o], fill=(152, 156, 162, 255), width=SS // 2)
    return down(im.filter(ImageFilter.GaussianBlur(SS * 0.15)))
def tech():
    im = cell((22, 26, 34, 255)); d = ImageDraw.Draw(im)
    d.rectangle([u(2), u(2), S - u(2), S - u(2)], outline=(40, 90, 110, 255), width=SS); g = (60, 210, 235, 255)
    d.line([S // 2, 0, S // 2, S], fill=g, width=SS); d.line([0, S // 2, S, S // 2], fill=g, width=SS)
    d.ellipse([S // 2 - u(2), S // 2 - u(2), S // 2 + u(2), S // 2 + u(2)], outline=g, width=SS)
    return down(im.filter(ImageFilter.GaussianBlur(SS * 0.12)))
def smooth():
    im = cell((54, 56, 62, 255)); d = ImageDraw.Draw(im)
    d.rectangle([u(0.5), u(0.5), S - u(0.5), S - u(0.5)], outline=(40, 42, 48, 255), width=SS)
    d.line([u(1), u(1), S - u(2), u(1)], fill=(66, 68, 74, 255), width=SS // 2)
    return down(im)
def carpet():
    im = cell((120, 32, 36, 255)); d = ImageDraw.Draw(im)
    for y in range(0, S, u(2)):
        for x in range(0, S, u(2)):
            c = (138, 40, 44, 255) if (x // u(2) + y // u(2)) % 2 else (108, 28, 32, 255)
            d.rectangle([x, y, x + u(2), y + u(2)], fill=c)
    return down(im.filter(ImageFilter.GaussianBlur(SS * 0.25)))
def diamond():
    im = cell((92, 96, 104, 255)); d = ImageDraw.Draw(im)
    for cy in range(0, S + u(8), u(8)):
        for cx in range(0, S + u(8), u(8)):
            ox = u(4) if (cy // u(8)) % 2 else 0; x = cx + ox
            d.polygon([(x, cy - u(2)), (x + u(2), cy), (x, cy + u(2)), (x - u(2), cy)], fill=(120, 124, 132, 255))
            d.line([(x - u(2), cy), (x, cy - u(2))], fill=(150, 154, 160, 255), width=SS // 2)
    return down(im.filter(ImageFilter.GaussianBlur(SS * 0.18)))
def wood():
    im = cell((120, 86, 52, 255)); d = ImageDraw.Draw(im)
    for i, y in enumerate(range(0, S, u(10.66))):
        d.rectangle([0, y, S, y + u(10.66)], fill=(126, 90, 54, 255) if i % 2 else (112, 80, 48, 255)); d.line([0, y, S, y], fill=(86, 60, 36, 255), width=SS)
        for gx in range(0, S, u(3)):
            d.line([gx, y + u(1), gx, y + u(9)], fill=(104, 74, 44, 255), width=SS // 3)
    return down(im.filter(ImageFilter.GaussianBlur(SS * 0.2)))

# --- new 10 ---
def concrete(): return down(noisy((92, 94, 98), 26, 0.22).filter(ImageFilter.GaussianBlur(SS * 0.15)))
def sand():     return down(noisy((196, 176, 128), 24, 0.18).filter(ImageFilter.GaussianBlur(SS * 0.2)))
def dirt():     return down(noisy((96, 72, 48), 30, 0.25).filter(ImageFilter.GaussianBlur(SS * 0.18)))
def brick():
    im = cell((64, 60, 58, 255)); d = ImageDraw.Draw(im); bw = u(16); bh = u(8)
    for ri, y in enumerate(range(0, S, bh)):
        off = bw // 2 if ri % 2 else 0
        for x in range(-bw, S + bw, bw):
            xx = x + off
            d.rectangle([xx + u(0.6), y + u(0.6), xx + bw - u(0.6), y + bh - u(0.6)], fill=(150, 72, 58, 255))
            d.line([xx + u(0.6), y + u(0.6), xx + bw - u(0.6), y + u(0.6)], fill=(174, 92, 76, 255), width=SS // 2)
    return down(im.filter(ImageFilter.GaussianBlur(SS * 0.12)))
def ice():
    im = cell((168, 200, 224, 255)); d = ImageDraw.Draw(im)
    for _ in range(5):
        x0 = random.randint(0, S); x1 = x0 + random.randint(-u(6), u(6))
        d.line([x0, 0, x1, S], fill=(150, 186, 214, 255), width=SS // 2)
    d.ellipse([u(6), u(8), u(16), u(15)], fill=(190, 216, 236, 180))
    return down(im.filter(ImageFilter.GaussianBlur(SS * 0.4)))
def lava():
    im = cell((44, 26, 20, 255))
    g = Image.new("RGBA", (S, S), (0, 0, 0, 0)); gd = ImageDraw.Draw(g)
    pts = [(u(0), u(10)), (u(8), u(6)), (u(16), u(14)), (u(24), u(8)), (u(32), u(12))]
    gd.line(pts, fill=(255, 140, 30, 255), width=SS * 2)
    gd.line([(u(10), u(0)), (u(14), u(12)), (u(8), u(22)), (u(16), u(32))], fill=(255, 120, 20, 255), width=SS * 2)
    g = g.filter(ImageFilter.GaussianBlur(SS * 0.7)); im.alpha_composite(g)
    d = ImageDraw.Draw(im); d.line(pts, fill=(255, 224, 120, 255), width=SS // 2)
    return down(im)
def pcb():
    im = cell((18, 70, 40, 255)); d = ImageDraw.Draw(im); t = (46, 150, 80, 255)
    for o in (u(6), u(16), u(26)):
        d.line([o, 0, o, S], fill=t, width=SS // 2); d.line([0, o, S, o], fill=t, width=SS // 2)
    for x in (u(6), u(16), u(26)):
        for y in (u(6), u(16), u(26)):
            d.ellipse([x - u(1.3), y - u(1.3), x + u(1.3), y + u(1.3)], fill=(214, 180, 70, 255))
    return down(im.filter(ImageFilter.GaussianBlur(SS * 0.12)))
def checker():
    im = cell((222, 222, 228, 255)); d = ImageDraw.Draw(im); q = u(8)
    for yi, y in enumerate(range(0, S, q)):
        for xi, x in enumerate(range(0, S, q)):
            if (xi + yi) % 2:
                d.rectangle([x, y, x + q, y + q], fill=(40, 42, 48, 255))
    return down(im)
def mesh():
    im = cell((58, 60, 66, 255)); d = ImageDraw.Draw(im); g = u(8)
    for y in range(g // 2, S, g):
        for x in range(g // 2, S, g):
            d.ellipse([x - u(2), y - u(2), x + u(2), y + u(2)], fill=(26, 28, 32, 255))
    return down(im.filter(ImageFilter.GaussianBlur(SS * 0.12)))
def water():
    im = cell((30, 86, 140, 255)); d = ImageDraw.Draw(im)
    for y in range(0, S, u(4)):
        pts = [(x, y + int(math.sin(x / S * 2 * math.pi) * u(0.8))) for x in range(0, S + 1, SS)]
        d.line(pts, fill=(70, 140, 196, 200), width=SS // 2)
    return down(im.filter(ImageFilter.GaussianBlur(SS * 0.3)))

tiles = [("metal", metal()), ("hazard", hazard()), ("grating", grating()), ("tech", tech()),
         ("smooth", smooth()), ("carpet", carpet()), ("diamond", diamond()), ("wood", wood()),
         ("concrete", concrete()), ("sand", sand()), ("dirt", dirt()), ("brick", brick()),
         ("ice", ice()), ("lava", lava()), ("pcb", pcb()), ("checker", checker()),
         ("mesh", mesh()), ("water", water())]

for key, t in tiles:
    t.save(os.path.join(OUT, f"{key}.png"))
    t.resize((CELL * 8, CELL * 8), Image.NEAREST).save(os.path.join(OUT, f"{key}_x8.png"))
    blk = Image.new("RGBA", (CELL * 2, CELL * 2), (0, 0, 0, 0))
    for bx in (0, CELL):
        for by in (0, CELL):
            blk.alpha_composite(t, (bx, by))
    blk.resize((CELL * 2 * 6, CELL * 2 * 6), Image.NEAREST).save(os.path.join(OUT, f"{key}_tiled.png"))

scale = 5; pad = 8; lab = 16; cols = 6; bw = CELL * 2 * scale; rows = math.ceil(len(tiles) / cols)
W = cols * (bw + pad) + pad; H = rows * (bw + lab + pad) + pad
cv = Image.new("RGBA", (W, H), (18, 18, 22, 255)); dd = ImageDraw.Draw(cv)
for i, (key, t) in enumerate(tiles):
    r, c = divmod(i, cols)
    blk = Image.new("RGBA", (CELL * 2, CELL * 2), (0, 0, 0, 0))
    for bx in (0, CELL):
        for by in (0, CELL):
            blk.alpha_composite(t, (bx, by))
    x = pad + c * (bw + pad); y = pad + r * (bw + lab + pad)
    cv.alpha_composite(blk.resize((bw, bw), Image.NEAREST), (x, y))
    dd.text((x + 2, y + bw + 3), f"{i + 1}.{key}", fill=(220, 220, 225, 255))
cv.save(os.path.join(OUT, "_overview.png"))
print("styles:", len(tiles), "| files:", len(os.listdir(OUT)))
