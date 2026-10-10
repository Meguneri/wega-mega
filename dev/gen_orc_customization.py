"""Открытые альтернативы причёсок/скафандров орков; оригиналы Aavikko имеют hosting restrictions.

Запускать из корня репозитория. Нужен Pillow. Сохраняет CC-BY-SA и авторство
исходных RSI. Перенесённые оригинальные спрайты тела этим скриптом не меняются.
"""

import copy
import json
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
TEXTURES = ROOT / "Resources/Textures"


def preserve_copyright(pairs: list[tuple]) -> dict:
    result = {}
    for key, value in pairs:
        if key == "copyright" and key in result:
            result[key] += " " + value
        else:
            result[key] = value
    return result


def read_meta(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=preserve_copyright)


def adapt(source: str, destination: str, states: dict[str, str], width: float, dy: int) -> None:
    src = TEXTURES / source
    dst = TEXTURES / destination
    dst.mkdir(parents=True, exist_ok=True)
    meta = read_meta(src / "meta.json")
    if meta["license"] not in ("CC-BY-SA-3.0", "CC0-1.0"):
        raise RuntimeError(source)
    originals = {state["name"]: state for state in meta["states"]}
    meta["states"] = []
    meta["copyright"] += f" Orc fit adapted for Wega from {source} by Codex, 2026."
    w, h = meta["size"]["x"], meta["size"]["y"]
    for target, name in states.items():
        state = copy.deepcopy(originals[name])
        state["name"] = target
        meta["states"].append(state)
        image = Image.open(src / (name + ".png")).convert("RGBA")
        fitted = Image.new("RGBA", image.size)
        for y in range(0, image.height, h):
            for x in range(0, image.width, w):
                frame = image.crop((x, y, x + w, y + h))
                scaled = frame.resize((round(w * width), h), Image.Resampling.NEAREST)
                offset = (scaled.width - w) // 2
                cell = Image.new("RGBA", (w, h))
                cell.paste(scaled.crop((offset, 0, offset + w, h)), (0, dy))
                fitted.paste(cell, (x, y))
        fitted.save(dst / (target + ".png"))
    (dst / "meta.json").write_text(json.dumps(meta, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    hair = dict(dread="dreads", father="long", meganefor="bigafro", mohawk="shavedmohawk",
                nefor="halfshaved", polnoref="pompadour", ronin="ronin", vicing="long2",
                vip="business", wisdom="long3")
    beard = dict(baron="fullbeard", hipster="hip", medium="croppedfullbeard", moustache="moustache",
                 moustachetwo="selleck", orcoldbeard="wise", reah="chinlessbeard", shield="dwarf",
                 stark="jensen", tail="gt", vicing="longbeard")
    adapt("Mobs/Customization/human_hair.rsi", "_Wega/Mobs/Customization/Orc/orc_hair.rsi", hair, 1.25, -1)
    adapt("Mobs/Customization/human_facial_hair.rsi", "_Wega/Mobs/Customization/Orc/orc_facial_hair.rsi",
          beard, 1.25, -1)
    for category in ("Head", "OuterClothing"):
        for style in ("engineering", "capspace"):
            source = f"Clothing/{category}/Hardsuits/{style}.rsi"
            meta = read_meta(TEXTURES / source / "meta.json")
            # Только общие экипированные состояния, включая световые слои шлема.
            states = {state["name"] + "-orc": state["name"] for state in meta["states"]
                      if state["name"].startswith("equipped-") and
                      not any(state["name"].endswith("-" + species) for species in
                              ("vox", "vulpkanin", "reptilian", "resomi", "dog"))}
            adapt(source, "_Wega/Clothing/Orc/" + source, states, 1.35, -1 if category == "Head" else 0)
    for kind in ("Jumpsuits", "Jumpskirts"):
        source = f"Clothing/Uniforms/{kind}/Color/color.rsi"
        meta = read_meta(TEXTURES / source / "meta.json")
        states = {state["name"] + "-orc": state["name"] for state in meta["states"]
                  if "equipped-" in state["name"] and
                  not any(state["name"].endswith("-" + species) for species in
                          ("vox", "vulpkanin", "reptilian", "resomi", "dog", "monkey", "female"))}
        adapt(source, "_Wega/Clothing/Orc/" + source, states, 1.35, 0)
    # Собственные геометрические метки, не копии ограниченных татуировок Aavikko.
    dst = TEXTURES / "_Wega/Mobs/Customization/Orc/orc_tattoos.rsi"
    dst.mkdir(parents=True, exist_ok=True)
    marks = {
        "orc_tagars_sign": [(12, 6, 14, 6), (13, 5, 13, 8)],
        "orc_sign_1": [(11, 6, 12, 7), (12, 6, 13, 7)],
        "orc_sign_2": [(19, 6, 20, 7), (19, 8, 20, 9)],
    }
    states = []
    for name, lines in marks.items():
        image = Image.new("RGBA", (32, 128))
        draw = ImageDraw.Draw(image)
        for x1, y1, x2, y2 in lines:
            draw.line((x1, y1, x2, y2), fill="white")
            draw.line((x1 - 1, y1 + 64, x2 - 1, y2 + 64), fill="white")
            draw.line((x1 + 1, y1 + 96, x2 + 1, y2 + 96), fill="white")
        image.save(dst / (name + ".png"))
        states.append(dict(name=name, directions=4))
    meta = dict(version=1, license="CC0-1.0", copyright="Original geometric Orc face marks by Codex for Wega, 2026.",
                size=dict(x=32, y=32), states=states)
    (dst / "meta.json").write_text(json.dumps(meta, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    preview()
    print("Orc customization generated.")


def preview() -> None:
    def frame(path: str, color: str | None = None) -> Image.Image:
        image = Image.open(TEXTURES / path).convert("RGBA").crop((0, 0, 32, 32))
        if color is not None:
            alpha = image.getchannel("A")
            image = ImageChops.multiply(image, Image.new("RGBA", image.size, color))
            image.putalpha(alpha)
        return image

    body = Image.new("RGBA", (32, 32))
    parts = "_Wega/Mobs/Species/Orc/parts.rsi/"
    for part in ("r_leg", "l_leg", "torso_m", "r_arm", "l_arm", "r_foot", "l_foot", "r_hand", "l_hand", "head_m"):
        body.alpha_composite(frame(parts + part + ".png", "#469d36"))
    body.alpha_composite(frame(parts + "eyes.png", "#d27d4a"))
    examples = [body.copy() for _ in range(4)]
    custom = "_Wega/Mobs/Customization/Orc/"
    examples[0].alpha_composite(frame(custom + "orc_hair.rsi/dread.png", "#402b22"))
    examples[0].alpha_composite(frame(custom + "orc_facial_hair.rsi/vicing.png", "#402b22"))
    clothing = "_Wega/Clothing/Orc/Clothing/"
    examples[1].alpha_composite(frame(
        clothing + "Uniforms/Jumpsuits/Color/color.rsi/equipped-INNERCLOTHING-orc.png", "#b3b3b3"))
    examples[1].alpha_composite(frame(custom + "orc_hair.rsi/ronin.png", "#4b3020"))
    for index, style in ((2, "engineering"), (3, "capspace")):
        examples[index].alpha_composite(frame(
            clothing + f"OuterClothing/Hardsuits/{style}.rsi/equipped-OUTERCLOTHING-orc.png"))
        examples[index].alpha_composite(frame(clothing + f"Head/Hardsuits/{style}.rsi/equipped-head-orc.png"))
    montage = Image.new("RGBA", (768, 240), "#20252d")
    draw = ImageDraw.Draw(montage)
    font_path = Path("C:/Windows/Fonts/segoeui.ttf")
    font = ImageFont.truetype(str(font_path), 18) if font_path.exists() else ImageFont.load_default()
    labels = ("Орк", "Обычная форма", "Рядовой Тагар", "Главарь Тагар")
    for i, image in enumerate(examples):
        montage.alpha_composite(image.resize((160, 160), Image.Resampling.NEAREST), (i * 192 + 16, 20))
        draw.text((i * 192 + 96, 198), labels[i], font=font, fill="#e3e8ef", anchor="mm")
    target = ROOT / "dev/.cache/orc-port/preview.png"
    target.parent.mkdir(parents=True, exist_ok=True)
    montage.save(target)


if __name__ == "__main__":
    main()
