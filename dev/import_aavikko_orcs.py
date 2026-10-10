"""Импорт оригинальных RSI орков Aavikko; файлы источника кешируются в dev/.cache.

Лицензии и авторство сохраняются в meta.json. Варианты обычной одежды хранятся
отдельно от апстримных RSI и подключаются OrcClothingVisualsSystem.
"""

import concurrent.futures
import copy
import json
from pathlib import Path
import struct
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
REVISION = "ffa8f45cce560f8dfda71384de4fb0638a4eaaff"
REPOSITORY = "KoTiKo43/Aavikko-2.0"
CACHE = ROOT / "dev/.cache/orc-port"


def preserve_copyright(pairs: list[tuple]) -> dict:
    result = {}
    for key, value in pairs:
        if key == "copyright" and key in result:
            result[key] += " " + value
        else:
            result[key] = value
    return result


def destination_rsi(source: str) -> str | None:
    path = source.replace("/Jumpsuit/", "/Jumpsuits/").replace("/Jumpskirt/", "/Jumpskirts/")
    if "/Uniforms/" in path and "/Color/" in path:
        if not path.endswith("/white.rsi"):
            return None
        path = path.removesuffix("/white.rsi") + "/color.rsi"
    return path


def download(path: str) -> bytes:
    target = CACHE / "source" / path
    if target.exists():
        return target.read_bytes()
    url = f"https://raw.githubusercontent.com/{REPOSITORY}/{REVISION}/{path}"
    for attempt in range(4):
        try:
            request = urllib.request.Request(url, headers={"User-Agent": "Wega-Orc-Port"})
            with urllib.request.urlopen(request, timeout=40) as response:
                data = response.read()
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            return data
        except Exception:
            if attempt == 3:
                raise
            time.sleep(1 + attempt)
    raise RuntimeError(path)


def main() -> None:
    tree_path = CACHE / "tree.json"
    if not tree_path.exists():
        url = f"https://api.github.com/repos/{REPOSITORY}/git/trees/{REVISION}?recursive=1"
        with urllib.request.urlopen(url, timeout=60) as response:
            tree_path.parent.mkdir(parents=True, exist_ok=True)
            tree_path.write_bytes(response.read())
    tree = json.loads(tree_path.read_text(encoding="utf-8-sig"))
    if tree["sha"] != REVISION:
        raise RuntimeError("Кеш дерева относится к другому коммиту")
    paths = [entry["path"] for entry in tree["tree"] if entry["type"] == "blob"]
    originals = {
        "Aavikko/Mobs/Species/Orc": "_Wega/Mobs/Species/Orc",
    }
    copies = {}
    for path in paths:
        for source, dest in originals.items():
            prefix = "Resources/Textures/" + source + "/"
            if path.startswith(prefix):
                copies[path] = "Resources/Textures/" + dest + "/" + path[len(prefix):]
    clothing = {}
    for path in paths:
        if not path.endswith("-orc.png"):
            continue
        relative = path.removeprefix("Resources/Textures/")
        if not relative.startswith(("Clothing/", "Corvax/Clothing/")):
            continue
        rsi = Path(relative).parent.as_posix()
        dest_rsi = destination_rsi(rsi)
        if dest_rsi is None or not (ROOT / "Resources/Textures" / dest_rsi / "meta.json").exists():
            continue
        clothing.setdefault(rsi, []).append(path)
        copies[path] = "Resources/Textures/_Wega/Clothing/Orc/" + dest_rsi + "/" + Path(relative).name
    metadata = ["Resources/Textures/" + rsi + "/meta.json" for rsi in clothing]
    source_files = [
        "LICENSE.TXT",
        "Resources/Prototypes/Aavikko/Species/orc.yml",
        "Resources/Prototypes/Aavikko/Body/Species/orc.yml",
        "Resources/Prototypes/Aavikko/Datasets/Names/orc.yml",
        "Resources/Prototypes/Aavikko/Entities/Clothing/OuterClothing/hardsuits_orc.yml",
        "Resources/Prototypes/Aavikko/Entities/Clothing/Head/hardsuit-helmets.yml",
        "Resources/Prototypes/Aavikko/Voice/speech_emote_sounds.yml",
        "Resources/Prototypes/Aavikko/Voice/speech_sounds.yml",
        "Resources/Prototypes/Aavikko/Accents/word_replacements.yml",
    ]
    source_files += [p for p in paths if
        (p.startswith("Resources/Locale/") and "/aavikko/" in p and "/orc" in p)
        or (p.startswith("Resources/Prototypes/Aavikko/Entities/Mobs/Customization/Markings/orc_"))
        or (p.startswith("Content.Server/Aavikko/Speech/") and "/Orc" in p)
        or p == "Content.Shared/Aavikko/Species/OrcClothingRestrictionSystem.cs"]
    # Лицензия корня может называться LICENSE: используем имя из дерева.
    source_files[0] = next(p for p in paths if p.lower() in ("license.txt", "license"))
    tasks = list(dict.fromkeys([*copies, *metadata, *source_files]))
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        for count, _ in enumerate(pool.map(download, tasks), 1):
            if count % 100 == 0:
                print(f"Скачано {count}/{len(tasks)}", flush=True)
    for rsi, sprites in clothing.items():
        meta = json.loads(download("Resources/Textures/" + rsi + "/meta.json"),
                          object_pairs_hook=preserve_copyright)
        if meta.get("license") not in ("CC-BY-SA-3.0", "CC0-1.0"):
            for path in sprites:
                copies.pop(path)
            continue
        states = {Path(p).stem for p in sprites}
        source_states = {state["name"]: state for state in meta["states"]}
        selected = []
        for name in sorted(states):
            state = source_states.get(name)
            if state is None:
                # В источнике часть PNG не зарегистрирована. Направления/анимацию
                # берём из исходного обычного состояния, а не угадываем.
                state = copy.deepcopy(source_states.get(name.removesuffix("-orc")))
                if state is None:
                    raise RuntimeError(f"Нет исходного состояния: {rsi}/{name}")
                state["name"] = name
            # У двух исходных поясных предметов забыто directions: 4,
            # хотя PNG содержит четыре направления без анимации.
            data = download("Resources/Textures/" + rsi + "/" + name + ".png")
            width, height = struct.unpack(">II", data[16:24])
            frames = width // meta["size"]["x"] * (height // meta["size"]["y"])
            if frames == 4 and state.get("directions", 1) == 1 and not state.get("delays"):
                state["directions"] = 4
            selected.append(state)
        meta["states"] = selected
        if not meta.get("license") or not meta.get("copyright"):
            raise RuntimeError(f"Нет лицензии или авторства: {rsi}")
        meta["copyright"] += f" Orc variants ported from {REPOSITORY} at {REVISION} for Wega."
        target = ROOT / "Resources/Textures/_Wega/Clothing/Orc" / destination_rsi(rsi) / "meta.json"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(meta, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for source, dest in copies.items():
        target = ROOT / dest
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(download(source))
    print(f"Готово: {len(copies)} файлов, {len(clothing)} RSI одежды.", flush=True)


if __name__ == "__main__":
    main()
