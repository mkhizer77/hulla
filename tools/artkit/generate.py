"""Placeholder art for the Unity client until the art track lands (docs/design/ux-ui-pass.md §13).

Generates the textures the theme references: the felt with grain and a vignette, the wooden rail,
the dark UI ground with a faint eight-point lattice, the card back, gradients for buttons, a
nine-slice panel with its shadow, an avatar glow and the screen vignette. Deterministic, so a rerun
changes nothing. Run from the repository root:

    python3 tools/artkit/generate.py
"""

from __future__ import annotations

import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

OUT = Path(__file__).resolve().parents[2] / "apps/unity/Hulla/Assets/_Project/Presentation/Theme/Art"

SAFFRON = (0xF4, 0xA3, 0x00)
SAFFRON_LIGHT = (0xFF, 0xC2, 0x4A)
SAFFRON_DARK = (0xC9, 0x8F, 0x10)
TEAL = (0x16, 0xA0, 0x85)
TEAL_LIGHT = (0x22, 0xBF, 0xA0)
TEAL_DARK = (0x0F, 0x7A, 0x66)
IVORY = (0xF6, 0xF1, 0xE7)
INK = (0x14, 0x15, 0x18)
PANEL = (0x1C, 0x1E, 0x22)
RAISED = (0x2A, 0x2D, 0x33)
RAISED_LIGHT = (0x36, 0x3A, 0x42)
FELT = (0x1E, 0x6B, 0x48)
FELT_DEEP = (0x0E, 0x22, 0x19)
RAIL = (0x6B, 0x44, 0x23)
THULLA = (0xE5, 0x39, 0x35)


def lerp(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    t = max(0.0, min(1.0, t))
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))  # type: ignore[return-value]


def noise_image(size: int, seed: int, blur: float) -> Image.Image:
    rng = random.Random(seed)
    img = Image.new("L", (size, size))
    img.putdata([rng.randint(0, 255) for _ in range(size * size)])
    return img.filter(ImageFilter.GaussianBlur(blur))


def felt(size: int = 1024) -> Image.Image:
    """Green felt: fine grain, a soft light in the upper middle, darker toward the edge."""
    grain = noise_image(size, 7, 0.8)
    coarse = noise_image(size, 11, 6.0).resize((size, size))
    img = Image.new("RGB", (size, size))
    px = img.load()
    g = grain.load()
    c = coarse.load()
    cx, cy = size / 2, size * 0.42
    for y in range(size):
        for x in range(size):
            d = math.hypot((x - cx) / (size * 0.62), (y - cy) / (size * 0.7))
            base = lerp(FELT, FELT_DEEP, d * d * 1.15)
            n = (g[x, y] - 128) / 128 * 0.06 + (c[x, y] - 128) / 128 * 0.05
            px[x, y] = tuple(max(0, min(255, int(v * (1 + n)))) for v in base)
    return img


def rail(size: int = 512) -> Image.Image:
    """Tileable wood: long grain along x with slow waves and a little grit."""
    grit = noise_image(size, 3, 1.2)
    img = Image.new("RGB", (size, size))
    px = img.load()
    gp = grit.load()
    for y in range(size):
        for x in range(size):
            wave = math.sin((y / size) * 2 * math.pi * 9 + math.sin(x / size * 2 * math.pi * 2) * 1.3)
            wave2 = math.sin((y / size) * 2 * math.pi * 23 + x / size * 2 * math.pi * 1)
            t = 0.5 + 0.28 * wave + 0.1 * wave2 + (gp[x, y] - 128) / 128 * 0.12
            colour = lerp((0x4A, 0x2E, 0x16), (0x86, 0x58, 0x30), t)
            px[x, y] = colour
    return img


def eight_point_star(draw: ImageDraw.ImageDraw, cx: float, cy: float, r: float, colour: tuple) -> None:
    pts = []
    for i in range(16):
        a = math.pi / 8 * i - math.pi / 2
        rr = r if i % 2 == 0 else r * 0.62
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    draw.polygon(pts, outline=colour)


def ground(size: int = 768) -> Image.Image:
    """The dark ground behind screens: ink with a faint Mughal lattice of eight-point stars."""
    img = Image.new("RGB", (size, size), INK)
    draw = ImageDraw.Draw(img)
    step = size // 3
    line = lerp(INK, SAFFRON, 0.075)
    dot = lerp(INK, SAFFRON, 0.16)
    for j in range(4):
        for i in range(4):
            cx = i * step + (step / 2 if j % 2 else 0)
            cy = j * step
            eight_point_star(draw, cx, cy, step * 0.42, line)
            draw.ellipse((cx - 3, cy - 3, cx + 3, cy + 3), fill=dot)
    return img


def card_back(w: int = 300, h: int = 420) -> Image.Image:
    """The default card back (GDD 15.4, until the twelve backs land): an ajrak-like lattice in saffron and teal."""
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img, "RGBA")
    draw.rounded_rectangle((0, 0, w - 1, h - 1), radius=22, fill=IVORY + (255,))
    inset = 14
    draw.rounded_rectangle((inset, inset, w - 1 - inset, h - 1 - inset), radius=14, fill=TEAL_DARK + (255,))
    cell = 34
    for j in range(-1, h // cell + 2):
        for i in range(-1, w // cell + 2):
            cx = inset + i * cell + (cell / 2 if j % 2 else 0)
            cy = inset + j * cell
            r = cell * 0.46
            pts = [(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)]
            draw.polygon(pts, fill=TEAL + (255,), outline=TEAL_LIGHT + (120,))
            draw.ellipse((cx - 5, cy - 5, cx + 5, cy + 5), fill=SAFFRON + (255,))
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle((inset, inset, w - 1 - inset, h - 1 - inset), radius=14, fill=255)
    back = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    back.paste(img, (0, 0), mask)
    outer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(outer, "RGBA").rounded_rectangle((0, 0, w - 1, h - 1), radius=22, fill=IVORY + (255,))
    outer.alpha_composite(back)
    d2 = ImageDraw.Draw(outer, "RGBA")
    cx, cy = w / 2, h / 2
    d2.ellipse((cx - 56, cy - 56, cx + 56, cy + 56), fill=IVORY + (255,))
    d2.ellipse((cx - 46, cy - 46, cx + 46, cy + 46), fill=SAFFRON + (255,))
    eight_point_star(d2, cx, cy, 38, TEAL_DARK + (255,))
    for k in range(3):
        eight_point_star(d2, cx, cy, 38 - k, TEAL_DARK + (255,))
    return outer


def gradient(top: tuple[int, int, int], mid: tuple[int, int, int], bottom: tuple[int, int, int], height: int = 64) -> Image.Image:
    img = Image.new("RGB", (4, height))
    px = img.load()
    for y in range(height):
        t = y / (height - 1)
        colour = lerp(top, mid, t * 2) if t < 0.5 else lerp(mid, bottom, (t - 0.5) * 2)
        for x in range(4):
            px[x, y] = colour
    return img


def panel9(size: int = 160, radius: int = 28, margin: int = 24, fill: tuple[int, int, int] = PANEL) -> Image.Image:
    """A rounded panel with a soft drop shadow; slice at the margin plus the radius."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ImageDraw.Draw(shadow, "RGBA").rounded_rectangle((margin, margin + 6, size - margin, size - margin + 6), radius=radius, fill=(0, 0, 0, 150))
    shadow = shadow.filter(ImageFilter.GaussianBlur(9))
    img.alpha_composite(shadow)
    draw = ImageDraw.Draw(img, "RGBA")
    draw.rounded_rectangle((margin, margin, size - margin, size - margin), radius=radius, fill=fill + (255,))
    draw.rounded_rectangle((margin, margin, size - margin, size - margin), radius=radius, outline=(255, 255, 255, 18), width=2)
    return img


def glow(size: int = 256, colour: tuple[int, int, int] = SAFFRON) -> Image.Image:
    img = Image.new("RGBA", (size, size), colour + (0,))
    px = img.load()
    c = size / 2
    for y in range(size):
        for x in range(size):
            d = math.hypot(x - c, y - c) / c
            a = max(0.0, 1 - d) ** 2.2
            px[x, y] = colour + (int(255 * a),)
    return img


def sphere(size: int = 128) -> Image.Image:
    """A white ball lit from the upper left; tinted per avatar by the theme."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    px = img.load()
    c = size / 2
    for y in range(size):
        for x in range(size):
            d = math.hypot((x - c * 0.72) / c, (y - c * 0.68) / c)
            v = int(255 * max(0.45, 1 - d * 0.42))
            px[x, y] = (v, v, v, 255)
    return img


def vignette(size: int = 512) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    px = img.load()
    c = size / 2
    for y in range(size):
        for x in range(size):
            d = math.hypot((x - c) / c, (y - c) / c)
            a = max(0.0, d - 0.55) / 0.6
            px[x, y] = (0, 0, 0, int(255 * min(1.0, a * a) * 0.7))
    return img


def stamp9(size: int = 160, margin: int = 16) -> Image.Image:
    """A rubber-stamp frame: a thick red border with a worn, slightly broken edge."""
    rng = random.Random(5)
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img, "RGBA")
    for k in range(4):
        draw.rounded_rectangle((margin + k, margin + k, size - margin - k, size - margin - k), radius=18 - k, outline=THULLA + (255,), width=3)
    px = img.load()
    for _ in range(900):
        x, y = rng.randrange(size), rng.randrange(size)
        r, g, b, a = px[x, y]
        if a:
            px[x, y] = (r, g, b, max(0, a - rng.randint(60, 220)))
    return img


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    felt().save(OUT / "felt.png", optimize=True)
    rail().save(OUT / "rail.png", optimize=True)
    ground().save(OUT / "ground.png", optimize=True)
    card_back().save(OUT / "card-back.png", optimize=True)
    gradient(SAFFRON_LIGHT, SAFFRON, SAFFRON_DARK).save(OUT / "grad-saffron.png")
    gradient(TEAL_LIGHT, TEAL, TEAL_DARK).save(OUT / "grad-teal.png")
    gradient(RAISED_LIGHT, RAISED, (0x22, 0x25, 0x2A)).save(OUT / "grad-raised.png")
    gradient((0x2A, 0x2A, 0x30), (0xF6, 0xF1, 0xE7), (0xE8, 0xE2, 0xD4), 8).save(OUT / "grad-card.png")
    panel9().save(OUT / "panel9.png", optimize=True)
    panel9(fill=RAISED).save(OUT / "raised9.png", optimize=True)
    glow().save(OUT / "glow-saffron.png", optimize=True)
    glow(colour=THULLA).save(OUT / "glow-thulla.png", optimize=True)
    glow(colour=TEAL).save(OUT / "glow-teal.png", optimize=True)
    vignette().save(OUT / "vignette.png", optimize=True)
    sphere().save(OUT / "sphere.png", optimize=True)
    stamp9().save(OUT / "stamp9.png", optimize=True)
    for p in sorted(OUT.glob("*.png")):
        print(p.name, p.stat().st_size)


if __name__ == "__main__":
    main()
