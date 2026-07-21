#!/usr/bin/env python3
"""Фростпанк-пак тайлов и стен арены (Wega): индустриальная эстетика вечной стужи.

Клёпаное железо, угольная решётка, утоптанный снег и холодный бетон — в тон ванильной стали, но
холоднее и синее. Полы намеренно ТЕМНЕЕ чистого FloorSnow: белый снегопад на белом снегу сливается,
а на этих поверхностях частицы погоды читаются (см. заметку про weather-видимость).

Полы (лист 128x32 = 4 варианта 32x32, как у arena_cyberpunk):
  frost_iron_deck  — клёпаное железо, фаска, иней в швах; базовый пол зоны.
  frost_grate      — тёмная индустриальная решётка с тёплым отливом (пол у домны, где теплее).
  packed_snow_path — утоптанный снег со следами и ледяными латками; темнее чистого снега.
  frost_concrete   — холодный бетон, изморозь ползёт от краёв, трещины.

Стены — НЕ рисуются с нуля: IconSmooth-геометрия (full + 8 состояний) берётся у готовой стены и
перекрашивается по яркости в новый материал, поэтому стыковка углов остаётся корректной:
  frost_iron     — rust_tank -> холодное клёпаное железо (ржавчина уходит в сталь-синь).
  ice_block      — concrete_plain -> яркий спрессованный лёд/снег.
  frost_concrete — chrome_panel -> холодный бетон с изморозью.

Запуск из корня репозитория (лучше через venv, если системный python виснет на PIL):
    dev/.venv-sprites/bin/python Tools/gen_frostpunk_pack.py
"""
import json
import os
import random
import shutil

from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), "..")
TILES_DIR = os.path.join(ROOT, "Resources", "Textures", "_Wega", "Tiles", "frostpunk")
WALLS_DIR = os.path.join(ROOT, "Resources", "Textures", "_Wega", "Structures", "Walls", "frostpunk")
# Доноры берём у ВАНИЛЬНЫХ стен: у них IconSmooth-геометрия уже с запечённой тёмной обводкой по
# открытым граням и корректной стыковкой. Перекрас по яркости сохраняет и текстуру, и обводку.
DONOR_DIR = os.path.join(ROOT, "Resources", "Textures", "Structures", "Walls")

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

def paint_frost_iron_deck(px, rng):
    base = (88, 98, 110)
    for y in range(T):
        for x in range(T):
            put(px, x, y, jitter(rng, base, 3))

    # Фаска: свет сверху/слева, тень снизу/справа.
    for i in range(T):
        put(px, i, 0, (108, 118, 130))
        put(px, 0, i, (104, 114, 126))
        put(px, i, T - 1, (66, 74, 84))
        put(px, T - 1, i, (70, 78, 88))

    # Клёпки по углам панели.
    for (cx, cy) in ((4, 4), (T - 5, 4), (4, T - 5), (T - 5, T - 5)):
        put(px, cx, cy, (140, 150, 162))
        put(px, cx, cy + 1, (58, 65, 74))
        put(px, cx + 1, cy, (118, 128, 140))

    # Иней в швах — редкие светлые крапинки холодного оттенка.
    for _ in range(rng.randint(10, 18)):
        x, y = rng.randint(0, T - 1), rng.randint(0, T - 1)
        if x in (0, T - 1) or y in (0, T - 1) or rng.random() < 0.25:
            put(px, x, y, jitter(rng, (176, 196, 214), 8))


def paint_frost_grate(px, rng):
    base = (48, 54, 62)
    for y in range(T):
        for x in range(T):
            put(px, x, y, jitter(rng, base, 3))

    # Решётка: горизонтальные планки с тёмными провалами между ними.
    for y in range(1, T, 4):
        for x in range(T):
            put(px, x, y, jitter(rng, (74, 82, 90), 4))       # блик планки
            put(px, x, y + 1, jitter(rng, (60, 67, 75), 3))
            put(px, x, y + 2, jitter(rng, (28, 32, 38), 3))   # провал
            put(px, x, y + 3, jitter(rng, (34, 39, 45), 3))

    # Тёплый отлив в углу — намёк на близость домны (очень скупо).
    for _ in range(rng.randint(8, 14)):
        x, y = rng.randint(T - 12, T - 1), rng.randint(T - 12, T - 1)
        c = px[x, y]
        put(px, x, y, (min(255, c[0] + 26), min(255, c[1] + 8), c[2]))


def paint_packed_snow_path(px, rng):
    # Намеренно СЕРЕЕ чистого снега, чтобы снегопад читался поверх.
    base = (196, 202, 210)
    for y in range(T):
        for x in range(T):
            put(px, x, y, jitter(rng, base, 4))

    # Синевато-серые потёртости от ходьбы.
    for _ in range(rng.randint(4, 7)):
        cx, cy, r = rng.randint(3, 28), rng.randint(3, 28), rng.randint(2, 5)
        for y in range(cy - r, cy + r + 1):
            for x in range(cx - r, cx + r + 1):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r and rng.random() < 0.5:
                    put(px, x, y, jitter(rng, (168, 176, 188), 6))

    # Пара тёмных смазанных следов + мелкие ледяные латки посветлее.
    for _ in range(rng.randint(1, 2)):
        x, y = rng.randint(4, T - 6), rng.randint(4, T - 4)
        for i in range(rng.randint(3, 5)):
            put(px, x + i, y, (150, 158, 170))
    for _ in range(rng.randint(2, 4)):
        x, y = rng.randint(2, T - 3), rng.randint(2, T - 3)
        put(px, x, y, (214, 226, 238))


def paint_frost_concrete(px, rng):
    base = (118, 122, 130)
    for y in range(T):
        for x in range(T):
            put(px, x, y, jitter(rng, base, 3))

    # Желобки крестом: плита 2x2.
    for i in range(T):
        put(px, i, 15, (96, 100, 108))
        put(px, i, 16, (104, 108, 116))
        put(px, 15, i, (96, 100, 108))
        put(px, 16, i, (104, 108, 116))

    # Изморозь ползёт от краёв к центру, затухая.
    for i in range(T):
        for edge, (x, y) in ((0, (i, 0)), (1, (i, T - 1)), (2, (0, i)), (3, (T - 1, i))):
            depth = rng.randint(0, 4)
            for d in range(depth):
                if edge == 0:
                    tx, ty = i, d
                elif edge == 1:
                    tx, ty = i, T - 1 - d
                elif edge == 2:
                    tx, ty = d, i
                else:
                    tx, ty = T - 1 - d, i
                if rng.random() < 0.5 - d * 0.1:
                    put(px, tx, ty, jitter(rng, (182, 198, 212), 8))

    # 1-2 тонкие трещины.
    for _ in range(rng.randint(1, 2)):
        x, y = rng.randint(3, T - 4), rng.randint(3, T - 4)
        for _ in range(rng.randint(5, 10)):
            put(px, x, y, (92, 96, 104))
            x += rng.choice((-1, 0, 1))
            y += rng.choice((0, 1))
            if not (0 <= x < T and 0 <= y < T):
                break


FLOORS = {
    "frost_iron_deck": paint_frost_iron_deck,
    "frost_grate": paint_frost_grate,
    "packed_snow_path": paint_packed_snow_path,
    "frost_concrete": paint_frost_concrete,
}


def build_floors():
    os.makedirs(TILES_DIR, exist_ok=True)
    for name, painter in FLOORS.items():
        sheet = Image.new("RGBA", (T * 4, T), (0, 0, 0, 0))
        for v in range(4):
            tile = Image.new("RGBA", (T, T))
            painter(tile.load(), random.Random(f"frostpunk:{name}:{v}"))
            sheet.paste(tile, (v * T, 0))
        sheet.save(os.path.join(TILES_DIR, name + ".png"))
        print(f"floor {name}: {sheet.size}")


# ---------------------------------------------------------------- wall recolor
# Перекраска по яркости: непрозрачный пиксель шаблона проецируется на ramp нового материала.
# Геометрия состояний/направлений не меняется — стыковка IconSmooth сохраняется как у шаблона.

# Ниже этого порога яркости пиксель считается контуром стены и красится в крепкий холодный
# near-black — так «нормальная обводка» остаётся чёткой при любом материале, а не растворяется в ramp.
OUTLINE_LUM = 0.12
OUTLINE_COLOR = (12, 14, 18)


def remap_image(im, dark, light, frost=False):
    im = im.convert("RGBA")
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            lum = (r * 299 + g * 587 + b * 114) / 255000  # 0..1

            # Тёмный контур донора сохраняем как контур, а не размываем в ramp.
            if lum < OUTLINE_LUM:
                px[x, y] = (*OUTLINE_COLOR, a)
                continue

            nr, ng, nb = lerp(dark, light, lum)
            # Иней на светлых участках: чуть подмешиваем холодный белый.
            if frost and lum > 0.62:
                nr, ng, nb = lerp((nr, ng, nb), (206, 222, 236), 0.35)
            px[x, y] = (nr, ng, nb, a)
    return im


# Только визуальные стейты стены: full + <prefix>0..7. Прочие стейты донора (construct/reinf/girder)
# не тащим — они не про наш материал.
def wall_states(prefix):
    return {"full"} | {f"{prefix}{d}" for d in range(8)}


WALL_RECOLORS = [
    # (новое имя, донор.rsi, префикс стейтов донора, тёмный ramp, светлый ramp, иней)
    ("frost_iron", "solid.rsi", "solid", (34, 40, 50), (150, 162, 176), True),
    ("ice_block", "ice.rsi", "ice", (46, 82, 108), (228, 242, 252), False),
    ("frost_stone", "cobblebrick_snow.rsi", "cobblebrick", (54, 58, 66), (202, 210, 220), True),
]


def build_walls():
    os.makedirs(WALLS_DIR, exist_ok=True)
    for name, donor, prefix, dark, light, frost in WALL_RECOLORS:
        src = os.path.join(DONOR_DIR, donor)
        dst = os.path.join(WALLS_DIR, name + ".rsi")
        if os.path.isdir(dst):
            shutil.rmtree(dst)
        os.makedirs(dst)

        keep = wall_states(prefix)
        meta = json.load(open(os.path.join(src, "meta.json"), encoding="utf-8"))
        meta["states"] = [s for s in meta["states"] if s["name"] in keep]
        for st in meta["states"]:
            old = st["name"]
            new = old if old == "full" else f"{name}{old[len(prefix):]}"
            st["name"] = new
            im = Image.open(os.path.join(src, old + ".png"))
            remap_image(im, dark, light, frost).save(os.path.join(dst, new + ".png"))

        # Ванильная геометрия под CC-BY-SA-3.0 — сохраняем атрибуцию (см. EXTERNAL_CONTENT.md).
        meta["license"] = "CC-BY-SA-3.0"
        meta["copyright"] = f"Wega frostpunk recolor of vanilla {donor} (CC-BY-SA-3.0)"
        with open(os.path.join(dst, "meta.json"), "w", encoding="utf-8") as f:
            json.dump(meta, f, indent=2, ensure_ascii=False)
            f.write("\n")
        print(f"wall {name}: from {donor} ({len(meta['states'])} states)")


if __name__ == "__main__":
    build_floors()
    build_walls()
