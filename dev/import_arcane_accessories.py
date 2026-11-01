"""Import missing Arcane accessories; retain existing Wega equivalents."""
import argparse
import json
from pathlib import Path
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
REVISION = "81cd66873de20e597282565f27b686331e65f677"
DEST = ROOT / "Resources/Prototypes/_Wega/ArcaneAccessories"
HEADER = "# SPDX-License-Identifier: AGPL-3.0-or-later\n# Adapted from ArcaneSS14/arcane " + REVISION + ".\n\n"
EXISTING = {
    "Whip": "ERPWhip", "SoftHandcuffs": "ToyHandcuffs", "Shibari": "ERPShibariRopeWhite",
    "Collar": "ClothingNeckChokerBlack", "ClothingNeckChokerDefault": "ClothingNeckChokerBlack",
    "ClothingNeckChokerSpikes": "ClothingNeckChokerSpike", "ClothingNeckChokerRed": "ClothingNeckChokerRed",
}
NAMES = {
    "choker_red_spikes": "красный чокер с шипами",
    "choker_red-spikes_heart": "красный чокер с шипами и сердцем",
    "choker_ring": "чокер с кольцом", "choker_bell": "чокер с колокольчиком",
    "choker_heart": "чокер с сердцем", "choker_heart_spikes": "чокер с сердцем и шипами",
    "choker_lace_black": "чёрный кружевной чокер", "choker_lace_pink": "розовый кружевной чокер",
    "choker_lace_red": "красный кружевной чокер", "choker_lace_white": "белый кружевной чокер",
    "choker_pink_heart": "розовый чокер с сердцем",
    "choker_pink_heart_spikes": "розовый чокер с сердцем и шипами",
    "choker_pink_spikes": "розовый чокер с шипами", "choker_red_bell": "красный чокер с колокольчиком",
    "choker_red_heart": "красный чокер с сердцем", "long_whip": "длинная плётка",
    "paddle_stack": "пэддл", "leash": "поводок", "bdsm_leash": "кожаный поводок",
    "bdsm_gloves": "длинные чёрные перчатки", "bdsm_boots": "чёрные сапоги на каблуках",
    "bdsm_dress": "ременной костюм",
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT / "dev/.cache/arcane-source")
    source = parser.parse_args().source.resolve()
    revision = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if revision != REVISION:
        raise SystemExit("Unexpected source revision")
    DEST.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source / "LICENSE-AGPLv3.TXT", DEST / "LICENSE-AGPLv3.TXT")
    records = []
    output = HEADER
    locales = {"ru-RU": [], "en-US": []}

    def add(uid, sprite, parent, category, source_file, en):
        nonlocal output
        original_id = uid
        uid = "WegaArcane" + uid
        src = source / "Resources/Textures" / sprite
        meta = json.loads((src / "meta.json").read_text(encoding="utf-8"))
        assert meta["license"] in ("CC-BY-SA-3.0", "CC-BY-SA-4.0")
        dst = "_Wega/ArcaneAccessories/" + sprite.split("/", 1)[1]
        shutil.copytree(src, ROOT / "Resources/Textures" / dst, dirs_exist_ok=True)
        equipped = {"neck": "equipped-NECK", "gloves": "equipped-HAND",
                    "shoes": "equipped-FEET", "jumpsuit": "equipped-INNERCLOTHING"}.get(category)
        states = {s["name"] for s in meta["states"]}
        assert "icon" in states and (equipped is None or equipped in states)
        output += (f"- type: entity\n  parent: {parent}\n  id: {uid}\n  components:\n"
                   f"  - type: Sprite\n    sprite: {dst}\n    state: icon\n    scale: 1,1\n")
        if equipped:
            output += f"  - type: Clothing\n    sprite: {dst}\n"
        else:
            output += f"  - type: Item\n    sprite: {dst}\n    size: Normal\n"
        if category == "neck":
            output += "  - type: ArcaneCollar\n"
        if category == "leash":
            output += "  - type: ArcaneLeash\n    maxDistance: 3\n"
        if category == "prop":
            output += "  - type: MeleeWeapon\n    possibilityWideAtack: false\n"
        output += "\n"
        stem = Path(sprite).stem
        descriptions = {
            "neck": ("Чокер с креплением для поводка.", "A choker with a fastening for a lead."),
            "leash": ("Поводок длиной три тайла. Крепится к надетому чокеру; активация в руке отстёгивает его.",
                      "A three-tile lead. Attach it to a worn choker; activate it in hand to release it."),
            "prop": ("Не причиняет ушибов, но расходует выносливость при ударе.",
                     "Deals no blunt damage, but a hit drains stamina."),
            "gloves": ("Длинные чёрные перчатки.", "Long black gloves."),
            "shoes": ("Высокие чёрные сапоги на каблуках.", "Tall black heeled boots."),
            "jumpsuit": ("Костюм из чёрных ремней.", "An outfit made of black straps."),
        }
        ru_desc, en_desc = descriptions[category]
        locales["ru-RU"].append(f"ent-{uid} = {NAMES[stem]}\n    .desc = {ru_desc}\n")
        # Названия и описания без сексуальных инструкций и привязки к исходной ERP-системе.
        locales["en-US"].append(f"ent-{uid} = {en}\n    .desc = {en_desc}\n")
        records.append(dict(id=uid, source_id=None if original_id == "HarnessOutfit" else original_id,
                            source=source_file, sprite=dst, category=category,
                            license=meta["license"], author=meta["copyright"], equipped=equipped))

    leash_path = "Resources/Prototypes/_Arcane/Fun/leash.yml"
    for block in (source / leash_path).read_text(encoding="utf-8").split("- type: entity\n")[1:]:
        uid = re.search(r"^  id: (.+)$", block, re.M)[1].strip()
        if uid in EXISTING:
            continue
        sprite = re.search(r"^    sprite: (.+)$", block, re.M)[1].strip()
        category = "leash" if uid in ("Leash", "BdsmLeash") else "neck"
        en = Path(sprite).stem.replace("_", " ").replace("bdsm", "leather")
        add(uid, sprite, "BaseItem" if category == "leash" else "ClothingNeckBase", category, leash_path, en)

    toys = "Resources/Prototypes/_Arcane/Entities/ERP/toys.yml"
    for uid, stem, en in [("LongWhip", "long_whip", "long whip"), ("PaddleStack", "paddle_stack", "paddle")]:
        add(uid, "_Arcane/ERP/Toys/Whip/" + stem + ".rsi", "ERPWhip", "prop", toys, en)
    add("ClothingHandsBDSMgloves", "_Arcane/Clothing/Hands/bdsm_gloves.rsi", "ClothingHandsBase", "gloves",
        "Resources/Prototypes/_Arcane/Entities/Clothing/Hands/gloves.yml", "long black gloves")
    add("ClothingShoesBDSMBoots", "_Arcane/Clothing/Shoes/bdsm_boots.rsi", "ClothingShoesBase", "shoes",
        "Resources/Prototypes/_Arcane/Entities/Clothing/Shoes/shoes.yml", "black heeled boots")
    add("HarnessOutfit", "_Arcane/Clothing/Innerclothing/bdsm_dress.rsi", "ClothingUniformBase", "jumpsuit",
        "Resources/Textures/_Arcane/Clothing/Innerclothing/bdsm_dress.rsi", "strap outfit")

    crate = "WegaArcaneAccessoriesCrate"
    output += (f"- type: entity\n  parent: CrateGenericSteel\n  id: {crate}\n  components:\n"
               "  - type: EntityStorage\n    capacity: 64\n  - type: StorageFill\n    contents:\n")
    for uid in [r["id"] for r in records] + sorted(set(EXISTING.values())):
        output += "    - id: " + uid + "\n"
    locales["ru-RU"].append(f"ent-{crate} = ящик аксессуаров Arcane\n    .desc = Аксессуары и реквизит, включая уже имеющиеся в Wega аналоги.\n")
    locales["en-US"].append(f"ent-{crate} = Arcane accessories crate\n    .desc = Accessories and props, including existing Wega equivalents.\n")
    for locale, entries in locales.items():
        directory = "_Wega" if locale == "en-US" else "_wega"
        (ROOT / f"Resources/Locale/{locale}/{directory}/arcane_accessories.ftl").write_text(
            "\n".join(entries) + ("\nwega-arcane-leash-detach = Отстегнуть поводок\n" if locale == "ru-RU"
                                   else "\nwega-arcane-leash-detach = Detach lead\n"),
            encoding="utf-8", newline="\n")
    (DEST / "accessories.yml").write_text(output, encoding="utf-8", newline="\n")
    audit = dict(repository="https://github.com/ArcaneSS14/arcane", revision=REVISION,
                 items=records, reused=EXISTING,
                 deduplication="Reviewed functional equivalents; new artwork variants are distinguished from new mechanics.")
    (DEST / "manifest.json").write_text(json.dumps(audit, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Imported {len(records)} missing variants; reused {len(set(EXISTING.values()))} existing prototypes.")


if __name__ == "__main__":
    main()
