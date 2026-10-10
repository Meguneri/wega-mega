"""Генератор карт смещения одежды по геометрии человеческого и орочьего тела.

Запускать из корня репозитория с Pillow. Карта содержит обратное отображение:
R = 128 + смещение X, G = 128 + смещение Y; displacementSize в движке равен 127.
Используется и для шейдера, и для подгонки открытых специальных скафандров.
"""

import json
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
TEXTURES = ROOT / "Resources/Textures"
DESTINATION = "_Wega/Mobs/Species/Orc/displacement.rsi"
PARTS = ("r_arm", "l_arm", "r_hand", "l_hand", "r_leg", "l_leg", "r_foot", "l_foot", "torso_m", "head_m")


def frame(image: Image.Image, direction: int) -> Image.Image:
    columns = image.width // 32
    x, y = direction % columns * 32, direction // columns * 32
    return image.crop((x, y, x + 32, y + 32))


def rows(image: Image.Image) -> dict[int, tuple[int, int]]:
    result = {}
    for y in range(32):
        xs = [x for x in range(32) if image.getpixel((x, y))[3]]
        if xs:
            result[y] = (min(xs), max(xs))
    return result


def build_maps() -> list[Image.Image]:
    result = []
    for direction in range(4):
        bodies = [Image.new("RGBA", (32, 32)) for _ in range(2)]
        torsos = []
        for part in PARTS:
            source_path = TEXTURES / f"Mobs/Species/Human/parts.rsi/{part}.png"
            target_path = TEXTURES / f"_Wega/Mobs/Species/Orc/parts.rsi/{part}.png"
            source = frame(Image.open(source_path).convert("RGBA"), direction)
            target = frame(Image.open(target_path).convert("RGBA"), direction)
            bodies[0].alpha_composite(source)
            bodies[1].alpha_composite(target)
            if part == "torso_m":
                torsos = [rows(source), rows(target)]
        source_rows, target_rows = [rows(body) for body in bodies]
        displacement = Image.new("RGBA", (32, 32), (128, 128, 0, 255))
        for y in range(32):
            source_y = round(interpolate(y, [(0, 2), (8, 10), (9, 11), (17, 17), (23, 23), (31, 31)]))
            target_left, target_right = nearest_row(target_rows, y)
            source_left, source_right = nearest_row(source_rows, source_y)
            anchors = [(target_left, source_left), (target_right, source_right)]
            if 9 <= y <= 23:
                source_core = nearest_row(torsos[0], source_y)
                target_core = nearest_row(torsos[1], y)
                anchors += [(tx, sx) for tx, sx in zip(target_core, source_core)
                            if target_left < tx < target_right]
            anchors.sort()
            for x in range(32):
                source_x = round(interpolate(x, anchors))
                displacement.putpixel((x, y), (128 + source_x - x, 128 + source_y - y, 0,
                                              255 if 0 <= source_x < 32 and 0 <= source_y < 32 else 0))
        result.append(displacement)
    return result


def nearest_row(bounds: dict[int, tuple[int, int]], y: int) -> tuple[int, int]:
    return bounds[min(bounds, key=lambda row: abs(row - y))]


def interpolate(value: float, anchors: list[tuple[int, int]]) -> float:
    # Снаружи силуэта сохраняем масштаб 1:1, чтобы края плащей не схлопывались в полосы.
    if value <= anchors[0][0]:
        return anchors[0][1] + value - anchors[0][0]
    for (left, source_left), (right, source_right) in zip(anchors, anchors[1:]):
        if value <= right:
            return source_left + (value - left) * (source_right - source_left) / (right - left)
    return anchors[-1][1] + value - anchors[-1][0]


def warp(image: Image.Image, displacement: Image.Image) -> Image.Image:
    result = Image.new("RGBA", (32, 32))
    for y in range(32):
        for x in range(32):
            red, green, _, alpha = displacement.getpixel((x, y))
            source_x, source_y = x + red - 128, y + green - 128
            if alpha and 0 <= source_x < 32 and 0 <= source_y < 32:
                result.putpixel((x, y), image.getpixel((source_x, source_y)))
    return result


def main() -> None:
    target = TEXTURES / DESTINATION
    target.mkdir(parents=True, exist_ok=True)
    image = Image.new("RGBA", (32, 128))
    maps = build_maps()
    for direction, displacement in enumerate(maps):
        image.paste(displacement, (0, direction * 32))
    image.save(target / "body.png")
    human_meta = json.loads((TEXTURES / "Mobs/Species/Human/parts.rsi/meta.json").read_text(encoding="utf-8-sig"))
    meta = dict(version=1, license="CC-BY-SA-3.0",
                copyright="Orc geometry by QWERTY especially for Aavikko Avaruus. Human geometry: "
                          + human_meta["copyright"] + " Inverse clothing displacement derived for Wega by Codex, 2026.",
                size=dict(x=32, y=32), states=[dict(name="body", directions=4)])
    (target / "meta.json").write_text(json.dumps(meta, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    preview(maps)
    print("Orc displacement maps generated.")


def preview(maps: list[Image.Image]) -> None:
    # Диагностический монтаж: тот же выбор пикселя, что у displacement.swsl, во всех направлениях.
    def load(path: str, direction: int, color: str | None = None) -> Image.Image:
        result = frame(Image.open(TEXTURES / path).convert("RGBA"), direction)
        if color:
            alpha = result.getchannel("A")
            result = ImageChops.multiply(result, Image.new("RGBA", result.size, color))
            result.putalpha(alpha)
        return result

    clothing = "Clothing/"
    styles = [
        ("Без одежды", []),
        ("Серая форма", [(clothing + "Uniforms/Jumpsuits/Color/color.rsi/equipped-INNERCLOTHING.png", "#b3b3b3"),
                         (clothing + "Uniforms/Jumpsuits/Color/color.rsi/trinkets-equipped-INNERCLOTHING.png", None)]),
        ("Форма капитана", [(clothing + "Uniforms/Jumpsuits/Command/captain.rsi/equipped-INNERCLOTHING.png", None)]),
    ]
    for name, style in (("Рядовой Тагар", "engineering"), ("Главарь Тагар", "capspace")):
        styles.append((name, [(clothing + f"OuterClothing/Hardsuits/{style}.rsi/equipped-OUTERCLOTHING.png", None),
                             (clothing + f"Head/Hardsuits/{style}.rsi/equipped-head.png", None)]))
    montage = Image.new("RGBA", (1050, 802), "#20252d")
    draw = ImageDraw.Draw(montage)
    font_path = Path("C:/Windows/Fonts/segoeui.ttf")
    font = ImageFont.truetype(str(font_path), 15) if font_path.exists() else ImageFont.load_default()
    for column, (label, layers) in enumerate(styles):
        draw.text((column * 210 + 105, 20), label, font=font, fill="white", anchor="mm")
        for direction in range(4):
            body = Image.new("RGBA", (32, 32))
            for part in PARTS:
                body.alpha_composite(load(f"_Wega/Mobs/Species/Orc/parts.rsi/{part}.png", direction, "#469d36"))
            body.alpha_composite(load("_Wega/Mobs/Species/Orc/parts.rsi/eyes.png", direction, "#d27d4a"))
            for path, color in layers:
                body.alpha_composite(warp(load(path, direction, color), maps[direction]))
            montage.alpha_composite(body.resize((160, 160), Image.Resampling.NEAREST),
                                    (column * 210 + 25, direction * 190 + 42))
    output = ROOT / "dev/.cache/orc-port/displacement-preview.png"
    output.parent.mkdir(parents=True, exist_ok=True)
    montage.save(output)


if __name__ == "__main__":
    main()
