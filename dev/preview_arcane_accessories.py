"""Render the imported accessories and compare their icon pixels with existing candidates."""
from collections import defaultdict
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
TEX = ROOT / "Resources/Textures"
manifest = json.loads((ROOT / "Resources/Prototypes/_Wega/ArcaneAccessories/manifest.json").read_text(encoding="utf-8"))
items = manifest["items"]
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 15)
width, height = 240, 170
canvas = Image.new("RGB", (4 * width, ((len(items) + 3) // 4) * height), "#252b35")
draw = ImageDraw.Draw(canvas)
labels = (ROOT / "Resources/Locale/ru-RU/_wega/arcane_accessories.ftl").read_text(encoding="utf-8")
labels = {line.split(" = ")[0][4:]: line.split(" = ")[1] for line in labels.splitlines() if line.startswith("ent-")}
hashes = defaultdict(list)


def icon_hash(path):
    with Image.open(path) as image:
        image = image.convert("RGBA").crop((0, 0, 32, 32))
        # Скрытые RGB-значения прозрачных пикселей не должны влиять на сравнение.
        bg = Image.new("RGBA", image.size)
        bg.alpha_composite(image)
        return hashlib.sha256(bg.tobytes()).hexdigest()


for index, item in enumerate(items):
    path = TEX / item["sprite"] / "icon.png"
    frame = Image.open(path).convert("RGBA").crop((0, 0, 32, 32)).resize((128, 128), Image.Resampling.NEAREST)
    x, y = (index % 4) * width, (index // 4) * height
    canvas.paste(frame, (x + 56, y), frame)
    label = labels[item["id"]]
    words = label.split()
    lines = [""]
    for word in words:
        if len(lines[-1]) + len(word) > 27:
            lines.append("")
        lines[-1] += (" " if lines[-1] else "") + word
    for row, line in enumerate(lines):
        draw.text((x + 8, y + 128 + row * 18), line, fill="white", font=font)
    hashes[icon_hash(path)].append(item["id"])

candidates = [
    "_Wega/Clothing/Neck/Misc/chokers.rsi", "_Lust/ERP/Toys/whip.rsi",
    "_Lust/ERP/Stuff/Shibari", "_Lust/ERP/Stuff/handcuff.rsi",
    "Clothing/Hands/Gloves", "_Wega/Clothing/Shoes/Misc",
]
matches = []
for directory in candidates:
    for path in (TEX / directory).rglob("*.png"):
        digest = icon_hash(path)
        if digest in hashes:
            matches.append(dict(new=hashes[digest], existing=str(path.relative_to(TEX))))

out = ROOT / "dev/.cache/arcane-accessories-preview.png"
canvas.save(out)
print(out)
print("Identical new icons:", [ids for ids in hashes.values() if len(ids) > 1])
print("Exact pixel matches against existing candidate assets:", matches)
