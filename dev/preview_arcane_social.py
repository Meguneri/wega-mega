"""Render the imported assets without changing the original sprites (requires Pillow)."""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
items = json.loads((ROOT / "Resources/Prototypes/_Wega/ArcaneSocial/manifest.json").read_text(encoding="utf-8"))["items"]
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 14)
canvas = Image.new("RGB", (960, 8 * 170), "#222830")
draw = ImageDraw.Draw(canvas)
for index, item in enumerate(items):
    folder = ROOT / "Resources/Textures" / item["sprite"]
    state = item.get("state", "icon")
    frame = Image.open(folder / (state + ".png")).convert("RGBA").crop((0, 0, 32, 32))
    x, y = (index % 6) * 160, (index // 6) * 170
    canvas.paste(frame.resize((112, 112), Image.Resampling.NEAREST), (x + 24, y + 4), frame.resize((112, 112), Image.Resampling.NEAREST))
    label = item["id"].removeprefix("WegaArcane").removeprefix("ClothingUniformJumpskirt").removeprefix("ClothingOuter").removeprefix("ClothingUniform")
    draw.text((x + 4, y + 120), label[:22], font=font, fill="white")
    if len(label) > 22:
        draw.text((x + 4, y + 137), label[22:], font=font, fill="white")

output = ROOT / "dev/.cache/arcane-social-preview.png"
output.parent.mkdir(parents=True, exist_ok=True)
canvas.save(output)
print(output)

# Four views of worn layers: no body is fabricated or stretched for this preview.
examples = [i for i in items if "equipped" in i][::5]
views = Image.new("RGB", (len(examples) * 128, 4 * 144), "#222830")
draw = ImageDraw.Draw(views)
for column, item in enumerate(examples):
    sheet = Image.open(ROOT / "Resources/Textures" / item["sprite"] / (item["equipped"] + ".png")).convert("RGBA")
    for direction in range(4):
        fx, fy = (direction % (sheet.width // 32)) * 32, (direction // (sheet.width // 32)) * 32
        frame = sheet.crop((fx, fy, fx + 32, fy + 32)).resize((128, 128), Image.Resampling.NEAREST)
        views.paste(frame, (column * 128, direction * 144), frame)
        draw.text((column * 128 + 4, direction * 144 + 126), ["South", "North", "East", "West"][direction], font=font, fill="white")
views.save(output.with_name("arcane-social-directions.png"))
