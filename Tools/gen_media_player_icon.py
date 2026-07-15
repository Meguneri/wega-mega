#!/usr/bin/env python3
"""Рисует иконку медиаплеера (Wega): пара восьмых нот с лучиком-бимом.

Зачем: окно плеера и индикатор ссылались на `actions_borg.rsi/musical-module.png` — PNG ВНУТРИ .rsi.
`xe:Tex` резолвит путь через GetResource<TextureResource>, а .rsi регистрируется как RSIResource
целиком, поэтому отдельного TextureResource на файл внутри неё нет → движок подставлял ERROR-заглушку.
Лечится только отдельно лежащим PNG — его тут и генерируем.

Рисуем в 4x и уменьшаем — так края получаются сглаженными без возни с SVG-тулчейном.
Палитра под тёмную панель плеера (#2B323D): холодный циан + мягкое свечение.

Запуск из корня репозитория:
    python3 Tools/gen_media_player_icon.py
"""
import os

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.join(os.path.dirname(__file__), "..")
OUT_DIR = os.path.join(ROOT, "Resources", "Textures", "_Wega", "Interface", "MediaPlayer")

SIZE = 64        # итоговый размер иконки
SS = 4           # супersampling: рисуем в SIZE*SS и ужимаем
S = SIZE * SS

NOTE = (150, 222, 240, 255)      # основной циан
NOTE_DIM = (96, 166, 190, 255)   # тень/объём снизу
GLOW = (110, 200, 230, 90)       # ореол


def head(draw_img, cx, cy, w, h, angle, color):
    """Нотная головка — наклонённый эллипс (PIL не умеет ellipse под углом, поэтому слой + rotate)."""
    pad = int(max(w, h))
    layer = Image.new("RGBA", (pad * 2, pad * 2), (0, 0, 0, 0))
    ImageDraw.Draw(layer).ellipse(
        [pad - w // 2, pad - h // 2, pad + w // 2, pad + h // 2], fill=color
    )
    layer = layer.rotate(angle, resample=Image.BICUBIC)
    draw_img.alpha_composite(layer, (int(cx) - pad, int(cy) - pad))


def build():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    k = SS  # чтобы координаты ниже читались как «в пикселях итоговой иконки»

    stem_w = 5 * k
    lx, ly = 20 * k, 47 * k          # левая головка
    rx, ry = 44 * k, 41 * k          # правая головка (выше — бим наклонён)
    lsx, rsx = lx + 7 * k, rx + 7 * k  # стойки идут по правому краю головок
    top_l, top_r = 17 * k, 11 * k    # верх стоек

    # стойки
    d.rectangle([lsx - stem_w // 2, top_l, lsx + stem_w // 2, ly], fill=NOTE)
    d.rectangle([rsx - stem_w // 2, top_r, rsx + stem_w // 2, ry], fill=NOTE)

    # бим — наклонная перекладина между стойками
    beam_h = 8 * k
    d.polygon(
        [
            (lsx - stem_w // 2, top_l),
            (rsx + stem_w // 2, top_r),
            (rsx + stem_w // 2, top_r + beam_h),
            (lsx - stem_w // 2, top_l + beam_h),
        ],
        fill=NOTE,
    )

    # головки: тёмная снизу (объём) + основная поверх
    head(img, lx, ly + 1.5 * k, 20 * k, 14 * k, -20, NOTE_DIM)
    head(img, rx, ry + 1.5 * k, 20 * k, 14 * k, -20, NOTE_DIM)
    head(img, lx, ly, 19 * k, 13 * k, -20, NOTE)
    head(img, rx, ry, 19 * k, 13 * k, -20, NOTE)

    # мягкий ореол, чтобы иконка не «тонула» в тёмной панели
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    glow.paste(Image.new("RGBA", (S, S), GLOW), (0, 0), img)
    glow = glow.filter(ImageFilter.GaussianBlur(3 * k))

    out = Image.alpha_composite(glow, img).resize((SIZE, SIZE), Image.LANCZOS)

    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, "note.png")
    out.save(path)
    print(f"{path}: {SIZE}x{SIZE}")


if __name__ == "__main__":
    build()
