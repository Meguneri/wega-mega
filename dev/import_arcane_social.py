"""Import the licensed Arcane social wardrobe and sofa; run from the repository root."""
import argparse
import json
from pathlib import Path
import re
import shutil
import subprocess

REVISION = "81cd66873de20e597282565f27b686331e65f677"
ROOT = Path(__file__).resolve().parents[1]
TARGET = ROOT / "Resources/Prototypes/_Wega/ArcaneSocial"
NAMES = {
    "dresseswithflowers": "чёрное платье с цветами", "maid_blue": "голубое платье с фартуком",
    "maid_long": "длинное платье с фартуком", "maid_pink": "розовое платье с фартуком",
    "tango_dress": "платье для танго", "xeno_dress": "платье в инопланетном стиле",
    "beer_wedding": "праздничное свадебное платье", "flower_princess": "платье цветочной принцессы",
    "fly_agaric": "платье «Мухомор»", "gentle_yellow": "нежно-жёлтое платье",
    "leopard": "леопардовое платье", "purple_evening": "фиолетовое вечернее платье",
    "spider": "платье с паутинкой", "wedding": "свадебное платье",
    "wedding_sleeves": "свадебное платье с рукавами",
    "kimono_blgr": "чёрно-зелёное кимоно", "kimono_blu": "синее кимоно",
    "kimono_flowbl": "чёрное кимоно с цветами", "kimono_flowblu": "синее кимоно с цветами",
    "kimono_flowred": "красное кимоно с цветами", "kimono_flowwh": "белое кимоно с цветами",
    "kimono_red": "красное кимоно", "kimono_redbl": "красно-чёрное кимоно",
    "kimono_stripes": "полосатое кимоно", "kimono_wh": "белое кимоно",
    "cornflower": "васильковый венок", "geranium": "венок из герани",
    "lily_of_the_valley": "ландышевый венок", "white_lily": "венок из белых лилий",
    "pink_lily": "венок из розовых лилий", "peony": "пионовый венок",
    "sunflower": "венок из подсолнухов", "white_rose": "венок из белых роз",
    "red_rose": "венок из красных роз", "chamomile": "ромашковый венок",
    "pink_tulip": "венок из розовых тюльпанов", "yellow_tulip": "венок из жёлтых тюльпанов",
}
HEADER = (
    "# SPDX-License-Identifier: AGPL-3.0-or-later\n"
    "# Adapted from ArcaneSS14/arcane, " + REVISION + ". See README.md.\n\n"
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT / "dev/.cache/arcane-source")
    args = parser.parse_args()
    source = args.source.resolve()
    revision = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if revision != REVISION:
        raise SystemExit("Source revision differs from the audited commit")
    TARGET.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source / "LICENSE-AGPLv3.TXT", TARGET / "LICENSE-AGPLv3.TXT")
    groups = [
        ("Clothing/Uniforms/dresses.yml", "dresses", "equipped-INNERCLOTHING"),
        ("Clothing/OuterClothing/kimonos.yml", "kimonos", "equipped-OUTERCLOTHING"),
        ("Clothing/Head/flower_wreaths.yml", "wreaths", "equipped-HELMET"),
    ]
    locales = {"ru-RU": [], "en-US": []}
    manifest = []

    def copy_sprite(sprite):
        src = source / "Resources/Textures" / sprite
        meta = json.loads((src / "meta.json").read_text(encoding="utf-8"))
        if meta["license"] not in ("CC0-1.0", "CC-BY-SA-3.0", "CC-BY-SA-4.0"):
            raise ValueError("Unapproved license: " + sprite)
        dst = "_Wega/ArcaneSocial/" + sprite.split("/", 1)[1]
        shutil.copytree(src, ROOT / "Resources/Textures" / dst, dirs_exist_ok=True)
        return dst, meta

    def loc(uid, ru, en, description_ru, description_en):
        locales["ru-RU"].append(f"ent-{uid} = {ru}\n    .desc = {description_ru}\n")
        locales["en-US"].append(f"ent-{uid} = {en}\n    .desc = {description_en}\n")

    for file, group, equipped in groups:
        relative = "Resources/Prototypes/_Arcane/Entities/" + file
        text = (source / relative).read_text(encoding="utf-8")
        output = HEADER
        for block in text.split("- type: entity\n")[1:]:
            sprites = re.findall(r"^    sprite: (.+)$", block, re.M)
            if not sprites:
                continue
            sprite = sprites[0].strip()
            stem = Path(sprite).stem
            if stem not in NAMES:
                continue
            original = re.search(r"^  id: (.+)$", block, re.M)[1].strip()
            # These source prototypes refer to a missing 'bouquet' sprite state.
            if original.startswith("Bouquet"):
                continue
            uid = "WegaArcane" + original
            parent = {"dresses": "ClothingUniformSkirtBase", "kimonos": "ClothingOuterBase",
                      "wreaths": "ClothingHeadBase"}[group]
            dst, meta = copy_sprite(sprite)
            states = {state["name"] for state in meta["states"]}
            if "icon" not in states or equipped not in states:
                raise ValueError("Missing required sprite state: " + sprite)
            output += (f"- type: entity\n  parent: {parent}\n  id: {uid}\n  components:\n"
                       f"  - type: Sprite\n    sprite: {dst}\n")
            output += f"  - type: Clothing\n    sprite: {dst}\n"
            if group == "kimonos":
                # Arcane's FoldedHandsClothing changes the body rendering; do not import that system.
                output += ("  - type: Foldable\n    canFoldInsideContainer: true\n"
                           "  - type: FoldableClothing\n    foldedEquippedPrefix: folded\n")
            output += "\n"
            en = re.search(r"^  name: (.+)$", block, re.M)[1].strip()
            loc(uid, NAMES[stem], en, "Одежда или аксессуар для встреч и праздников.",
                "Clothing or an accessory for social occasions and celebrations.")
            manifest.append(dict(id=uid, source_id=original, source=relative, sprite=dst,
                                 license=meta["license"], author=meta["copyright"],
                                 equipped=equipped))
        (TARGET / (group + ".yml")).write_text(output.rstrip() + "\n", encoding="utf-8", newline="\n")

    sprite, meta = copy_sprite("_ADT/Structures/Furniture/Sofas/sofa_brown.rsi")
    output = HEADER
    for state, ru in [
        ("brownsofa_left", "левый край"), ("brownsofa_left_02", "левый край, вариант 2"),
        ("brownsofa_right", "правый край"), ("brownsofa_right_02", "правый край, вариант 2"),
        ("brownsofa_middle", "середина"), ("brownsofa_leftcorner", "левый угол"),
        ("brownsofa_rightcorner", "правый угол"),
    ]:
        uid = "WegaArcaneSofa" + state.removeprefix("brownsofa_").title().replace("_", "")
        output += (f"- type: entity\n  parent: ChairBase\n  id: {uid}\n  components:\n"
                   f"  - type: Sprite\n    sprite: {sprite}\n    state: {state}\n    noRot: false\n\n")
        loc(uid, "коричневый диван: " + ru, "brown sofa: " + state.removeprefix("brownsofa_"),
            "Мягкая секция дивана для спокойного отдыха.", "A comfortable modular sofa section.")
        manifest.append(dict(id=uid, source_id="ADTSofaBrown*", source="Resources/Prototypes/_Arcane/Entities/Structures/Furniture/sofas.yml",
                             sprite=sprite, state=state, license=meta["license"], author=meta["copyright"]))
    (TARGET / "sofas.yml").write_text(output.rstrip() + "\n", encoding="utf-8", newline="\n")

    crate = HEADER + "- type: entity\n  parent: CrateGenericSteel\n  id: WegaArcaneSocialWardrobe\n  components:\n  - type: Storage\n    grid:\n    - 0,0,15,15\n  - type: StorageFill\n    contents:\n"
    for item in manifest:
        if "equipped" in item:
            crate += "    - id: " + item["id"] + "\n"
    (TARGET / "wardrobe.yml").write_text(crate, encoding="utf-8", newline="\n")
    loc("WegaArcaneSocialWardrobe", "ящик одежды Arcane для встреч и праздников", "Arcane social wardrobe crate",
        "Платья, кимоно и цветочные венки. Выдаётся администратором.",
        "Dresses, kimonos and flower wreaths. Supplied by an administrator.")
    for locale, entries in locales.items():
        path = ROOT / f"Resources/Locale/{locale}/_wega/arcane_social.ftl"
        path.write_text("\n".join(entries), encoding="utf-8", newline="\n")
    (TARGET / "manifest.json").write_text(json.dumps(dict(repository="https://github.com/ArcaneSS14/arcane",
        revision=REVISION, items=manifest), ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Imported {len(manifest)} items and one wardrobe crate")


if __name__ == "__main__":
    main()
