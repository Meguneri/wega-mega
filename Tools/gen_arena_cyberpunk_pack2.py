#!/usr/bin/env python3
"""Вторая волна аренных киберпанк-тайлов и стен (Wega) — «мостик» к ванильному стилю.

Первая волна (neon_street, retro_arcade, riot_chevron...) — яркие акцентные тайлы. Рядом с ванильной
сталью SS14 они смотрятся островами. Эта волна — приглушённые базовые поверхности в ванильной гамме
(серый 0x3a–0x8a, низкая насыщенность, редкие неоновые акценты), чтобы стыковать обычную станцию с
неоновыми зонами арены.

Полы (лист 128x32 = 4 варианта 32x32, как у первой волны):
  asphalt         — тёмный асфальт с крапом и трещинами; базовая «улица», сочетается с ванильным
                    plating и neon_street.
  asphalt_marking — тот же асфальт + потёртая жёлтая разметка (перекликается с ванильными warning-полосами).
  concrete_slab   — серые бетонные плиты с желобками и пятнами; мост между сталью и трущобами.
  steel_neon_seam — ванильная стальная плита с ОДНИМ тонким циановым швом: переходный тайл сталь→неон.
  hex_deck        — тёмный ганметал-настил с рифлением и редкими тил-бликами; индустриальный низ.
  corpo_carpet    — глубокий приглушённо-синий ковролин с редкой пурпурной нитью; пара к corpo_marble.

Стены — НЕ рисуются с нуля: IconSmooth-геометрия (full + 8 состояний x 4 направления-квадранта)
берётся у готовых стен пака и перекрашивается по яркости в новый материал. Так стыковка углов
гарантированно остаётся корректной:
  steel_plate     — rust_tank, перекрашенный в нейтральную сталь (клёпаные листы, почти ваниль).
  gunmetal_panel  — chrome_panel, утемнённый в холодный ганметал ванильных стен.
  concrete_hazard — chrome_panel в тёплый бетон + жёлто-чёрная предупреждающая полоса понизу.

Запуск из корня репозитория:
    python3 Tools/gen_arena_cyberpunk_pack2.py
"""
import json
import os
import random
import shutil

from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), "..")
TILES_DIR = os.path.join(ROOT, "Resources", "Textures", "_Wega", "Tiles", "arena_cyberpunk")
WALLS_DIR = os.path.join(ROOT, "Resources", "Textures", "_Wega", "Structures", "Walls", "arena_cyberpunk")

T = 32  # размер тайла


# ---------------------------------------------------------------- utils

def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def jitter(rng, color, amp):
    return tuple(max(0, min(255, c + rng.randint(-amp, amp))) for c in color)


def put(px, x, y, color):
    if 0 <= x < T and 0 <= y < T:
        px[x, y] = (*color, 255)


# ---------------------------------------------------------------- floor painters
# Каждый painter рисует ОДИН вариант 32x32 в переданный pixel-access. rng задаёт вариативность.

def paint_asphalt(px, rng, marking=False):
    base = (62, 65, 68)
    for y in range(T):
        for x in range(T):
            c = jitter(rng, base, 4)
            if rng.random() < 0.06:  # крап мелкого гравия
                c = jitter(rng, (74, 77, 80), 4)
            put(px, x, y, c)

    # 1-2 тонкие трещины ломаной линией
    for _ in range(rng.randint(1, 2)):
        x, y = rng.randint(2, T - 3), rng.randint(2, T - 3)
        for _ in range(rng.randint(6, 12)):
            put(px, x, y, (46, 48, 51))
            x += rng.choice((-1, 0, 1))
            y += rng.choice((0, 1))
            if not (0 <= x < T and 0 <= y < T):
                break

    if marking:
        # Потёртая жёлтая полоса разметки по центру, с выщербинами.
        for y in range(14, 18):
            for x in range(T):
                if rng.random() < 0.82:
                    put(px, x, y, jitter(rng, (176, 148, 58), 10))


def paint_concrete_slab(px, rng):
    base = (108, 109, 106)
    for y in range(T):
        for x in range(T):
            put(px, x, y, jitter(rng, base, 3))

    # Желобки крестом: плита 2x2 суб-панели.
    for i in range(T):
        put(px, i, 15, (88, 89, 86))
        put(px, i, 16, (94, 95, 92))
        put(px, 15, i, (88, 89, 86))
        put(px, 16, i, (94, 95, 92))

    # Пара блёклых пятен-подтёков.
    for _ in range(rng.randint(1, 3)):
        cx, cy, r = rng.randint(4, 27), rng.randint(4, 27), rng.randint(2, 4)
        for y in range(cy - r, cy + r + 1):
            for x in range(cx - r, cx + r + 1):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r and rng.random() < 0.6:
                    if 0 <= x < T and 0 <= y < T:
                        c = px[x, y]
                        put(px, x, y, (c[0] - 9, c[1] - 9, c[2] - 8))


def paint_steel_neon_seam(px, rng):
    # База — ванильная сталь: панель с фаской по краю.
    base = (122, 127, 131)
    for y in range(T):
        for x in range(T):
            put(px, x, y, jitter(rng, base, 3))
    for i in range(T):  # фаска: свет сверху/слева, тень снизу/справа
        put(px, i, 0, (138, 143, 147))
        put(px, 0, i, (134, 139, 143))
        put(px, i, T - 1, (100, 104, 108))
        put(px, T - 1, i, (104, 108, 112))

    # Один тонкий циановый шов по правому краю панели + едва заметный полутон по бокам.
    sx = T - 5
    for y in range(2, T - 2):
        put(px, sx, y, (52, 190, 210))
        put(px, sx - 1, y, (100, 128, 134))
        put(px, sx + 1, y, (100, 128, 134))


def paint_hex_deck(px, rng):
    base = (78, 85, 92)
    for y in range(T):
        for x in range(T):
            put(px, x, y, jitter(rng, base, 3))

    # Рифление: шахматные «капли» протектора.
    for y in range(2, T, 4):
        off = 2 if (y // 4) % 2 else 0
        for x in range(2 + off, T, 4):
            put(px, x, y, (96, 104, 112))
            put(px, x + 1, y, (64, 70, 77))

    # Редкий тил-блик — намёк на неон, не более.
    if rng.random() < 0.75:
        x, y = rng.randint(3, T - 4), rng.randint(3, T - 4)
        put(px, x, y, (74, 150, 160))


def paint_corpo_carpet(px, rng):
    base = (46, 52, 64)
    for y in range(T):
        for x in range(T):
            c = jitter(rng, base, 4)
            if (x + y) % 2 == 0:  # диагональное плетение
                c = (c[0] + 5, c[1] + 5, c[2] + 6)
            put(px, x, y, c)

    # Редкая пурпурная нить — фирменный корпо-акцент, очень скупо.
    for _ in range(rng.randint(1, 2)):
        x, y = rng.randint(2, T - 6), rng.randint(2, T - 3)
        for i in range(rng.randint(3, 5)):
            put(px, x + i, y, (104, 58, 96))


FLOORS = {
    "asphalt": lambda px, rng: paint_asphalt(px, rng, marking=False),
    "asphalt_marking": lambda px, rng: paint_asphalt(px, rng, marking=True),
    "concrete_slab": paint_concrete_slab,
    "steel_neon_seam": paint_steel_neon_seam,
    "hex_deck": paint_hex_deck,
    "corpo_carpet": paint_corpo_carpet,
}


def build_floors():
    for name, painter in FLOORS.items():
        sheet = Image.new("RGBA", (T * 4, T), (0, 0, 0, 0))
        for v in range(4):
            tile = Image.new("RGBA", (T, T))
            painter(tile.load(), random.Random(f"{name}:{v}"))
            sheet.paste(tile, (v * T, 0))
        path = os.path.join(TILES_DIR, name + ".png")
        sheet.save(path)
        print(f"floor {name}: {sheet.size}")


# ---------------------------------------------------------------- wall recolor
# Перекраска по яркости: каждый непрозрачный пиксель шаблона проецируется на ramp нового материала.
# Геометрия состояний/направлений не меняется — стыковка IconSmooth сохраняется как у шаблона.

def remap_image(im, dark, light, keep_saturated=False, hazard_band=False):
    im = im.convert("RGBA")
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue

            # Сохраняем ТОЛЬКО жёлтые акценты шаблона (декаль rust_tank): условие по каналам, а не по
            # общей насыщенности — иначе весь ржаво-коричневый корпус считался бы «акцентом» и
            # перекраска не срабатывала вовсе.
            if keep_saturated and r > 150 and g > 110 and b < 90:
                px[x, y] = (int(r * 0.9), int(g * 0.9), int(b * 0.9), a)
                continue

            lum = (r * 299 + g * 587 + b * 114) / 255000  # 0..1
            nr, ng, nb = lerp(dark, light, lum)

            # Жёлто-чёрная полоса понизу стены (в координатах кадра 32x32 тайла).
            ty = y % T
            if hazard_band and 24 <= ty <= 29 and lum > 0.18:
                stripe = ((x + ty) // 3) % 2 == 0
                base = (168, 138, 44) if stripe else (52, 50, 46)
                nr, ng, nb = lerp((nr, ng, nb), base, 0.85)

            px[x, y] = (nr, ng, nb, a)
    return im


WALL_RECOLORS = [
    # (новое имя, шаблон, тёмный край ramp, светлый край ramp, keep_saturated, hazard)
    ("steel_plate", "rust_tank", (52, 55, 59), (156, 162, 167), True, False),
    ("gunmetal_panel", "chrome_panel", (40, 45, 52), (118, 128, 138), False, False),
    ("concrete_hazard", "chrome_panel", (58, 57, 53), (148, 146, 138), False, True),

    # Волна 2б: «спокойные» базовые стены без акцентов. Насыщенные детали шаблонов (граффити,
    # цветные кабели, декаль) сворачиваются в ramp по яркости и остаются лишь тональными пятнами.
    ("concrete_plain", "slum_graffiti", (60, 59, 55), (150, 147, 138), False, False),
    ("pipe_conduit", "cable_conduit", (36, 39, 44), (110, 118, 126), False, False),
    ("dark_panel", "chrome_panel", (28, 31, 36), (74, 82, 92), False, False),
    ("bronze_plate", "rust_tank", (56, 48, 42), (150, 132, 112), False, False),
]


def build_walls():
    for name, template, dark, light, keep_sat, hazard in WALL_RECOLORS:
        src = os.path.join(WALLS_DIR, template + ".rsi")
        dst = os.path.join(WALLS_DIR, name + ".rsi")
        if os.path.isdir(dst):
            shutil.rmtree(dst)
        os.makedirs(dst)

        meta = json.load(open(os.path.join(src, "meta.json"), encoding="utf-8"))
        for st in meta["states"]:
            old = st["name"]
            new = old if old == "full" else old.replace(template, name)
            st["name"] = new
            im = Image.open(os.path.join(src, old + ".png"))
            remap_image(im, dark, light, keep_sat, hazard).save(os.path.join(dst, new + ".png"))

        meta["copyright"] = "wega-mega arena cyberpunk pack"
        with open(os.path.join(dst, "meta.json"), "w", encoding="utf-8") as f:
            json.dump(meta, f, indent=2, ensure_ascii=False)
            f.write("\n")
        print(f"wall {name}: from {template} ({len(meta['states'])} states)")


if __name__ == "__main__":
    build_floors()
    build_walls()
