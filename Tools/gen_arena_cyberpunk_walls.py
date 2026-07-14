#!/usr/bin/env python3
"""Пересобирает анимацию неоновых стен арены (Wega): плавнее и живее, чем исходные 4 кадра по ~0.5с.

Проблема: у анимированных стен (neon_tube, holo_barrier, circuit_panel, data_glass, bio_neon) каждый
IconSmooth-вариант — 4 кадра с большим шагом (0.45–0.55с/кадр, цикл ~2с). Пульс неона медленный и
ступенчатый.

ВАЖНО: эти состояния 4-НАПРАВЛЕННЫЕ (directions: 4). В листе кадры лежат построчно: строка = направление,
столбец = кадр (плоский индекс = dir*framesPerDir + frame). Нельзя трогать кадры, игнорируя направления,
иначе число delays-массивов разойдётся с числом направлений в Icons и движок упадёт при рендере
(Icons[dir] вне границ). Скрипт сохраняет число направлений и раскладку.

Что делает (арт НЕ рисуется заново, берутся ИСХОДНЫЕ кадры художника):
  1. По каждому направлению достаёт его исходные кадры.
  2. Интерполирует их в FRAMES плавных (кроссфейд по кольцу) — движение непрерывное, без рывков.
  3. Усиливает АМПЛИТУДУ мерцания относительно временно́го среднего КАЖДОГО направления (статичное тело
     стены не трогается, «дышит» только неон).
  4. Кладёт новый лист: строка = направление, столбец = кадр (та же схема, что у оригинала).
  5. Ставит быстрый общий тайминг (DELAY) и delays на каждое направление.

«full» (одиночный кадр-иконка) не трогаем. Запуск из корня репозитория:
    python3 Tools/gen_arena_cyberpunk_walls.py
"""
import json
import math
import os

import numpy as np
from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), "..")
WALLS_DIR = os.path.join(ROOT, "Resources", "Textures", "_Wega", "Structures", "Walls", "arena_cyberpunk")

WALLS = ["neon_tube", "holo_barrier", "circuit_panel", "data_glass", "bio_neon"]

FRAMES = 12       # плавных кадров в новом цикле (на каждое направление)
DELAY = 0.09      # секунд на кадр → цикл ~1.08с (живее прежних ~2с)
GAIN = 1.7        # усиление амплитуды мерцания относительно временно́го среднего


def load_dir_frames(png_path, size, dirs, frames_per_dir):
    """Достаёт кадры как [dir][frame] (RGBA size×size). Плоский индекс = dir*frames_per_dir + frame."""
    sheet = np.asarray(Image.open(png_path).convert("RGBA")).astype(np.float32)
    cols = sheet.shape[1] // size
    out = []
    for d in range(dirs):
        frames = []
        for f in range(frames_per_dir):
            idx = d * frames_per_dir + f
            r = (idx // cols) * size
            c = (idx % cols) * size
            frames.append(sheet[r:r + size, c:c + size].copy())
        out.append(frames)
    return out


def resample(keyframes, frames):
    """Кроссфейд ключевых кадров в `frames` плавных (по кольцу)."""
    n = len(keyframes)
    out = []
    for k in range(frames):
        p = (k / frames) * n
        i0 = int(math.floor(p)) % n
        i1 = (i0 + 1) % n
        t = p - math.floor(p)
        out.append(keyframes[i0] * (1.0 - t) + keyframes[i1] * t)
    return out


def amplify(frames, gain):
    """Отталкивает каждый кадр от временно́го среднего — усиливает мерцание, не трогая статичные пиксели."""
    stack = np.stack(frames, axis=0)
    mean = stack.mean(axis=0, keepdims=True)
    boosted = np.clip(mean + (stack - mean) * gain, 0.0, 255.0)
    return [boosted[i] for i in range(boosted.shape[0])]


def pack(dir_frames, size):
    """Лист: строка = направление, столбец = кадр (dimX = FRAMES)."""
    dirs = len(dir_frames)
    sheet = np.zeros((dirs * size, FRAMES * size, 4), dtype=np.uint8)
    for d in range(dirs):
        for f in range(FRAMES):
            sheet[d * size:(d + 1) * size, f * size:(f + 1) * size] = dir_frames[d][f].round().astype(np.uint8)
    return Image.fromarray(sheet, "RGBA")


def main():
    for wall in WALLS:
        rsi = os.path.join(WALLS_DIR, wall + ".rsi")
        meta_path = os.path.join(rsi, "meta.json")
        meta = json.load(open(meta_path, encoding="utf-8"))
        size = meta["size"]["x"]

        changed = 0
        for st in meta["states"]:
            delays = st.get("delays")
            if not delays or len(delays[0]) <= 1:
                continue  # статичный кадр (напр. full) — не трогаем

            dirs = st.get("directions", 1)
            frames_per_dir = len(delays[0])
            png = os.path.join(rsi, st["name"] + ".png")

            src = load_dir_frames(png, size, dirs, frames_per_dir)
            new = [amplify(resample(src[d], FRAMES), GAIN) for d in range(dirs)]
            pack(new, size).save(png)

            st["delays"] = [[DELAY] * FRAMES for _ in range(dirs)]
            changed += 1

        with open(meta_path, "w", encoding="utf-8") as f:
            json.dump(meta, f, indent=2, ensure_ascii=False)
            f.write("\n")
        print(f"{wall}: {changed} states → {FRAMES} frames/dir @ {DELAY}s (cycle {round(FRAMES * DELAY, 2)}s)")


if __name__ == "__main__":
    main()
