"""The small sprites the ambient layer animates over the rooftop scene: a soft glow (the twinkling
stars), a wisp of steam and a chai glass on the table's rail. White where the layer tints them.
Deterministic; run from the repository root:

    python3 tools/artkit/ambient.py
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "apps/unity/Hulla/Assets/_Project/Presentation/Theme/Art/Skins/shared"
SS = 2  # supersampling for drawn shapes


def s(v: float) -> int:
    return int(round(v * SS))


def glow(size: int = 64, power: float = 2.2) -> Image.Image:
    """A soft white dot, brightest in the middle."""
    g = Image.radial_gradient("L").resize((size, size))
    img = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    img.putalpha(g.point(lambda v: int(255 * max(0.0, 1 - v / 255) ** power)))
    return img


def steam() -> Image.Image:
    """A soft wisp, 60 x 140, white, faded at both ends."""
    w, h = 60, 140
    img = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    d = ImageDraw.Draw(img, "RGBA")
    for k in range(40):
        t = k / 40
        x = w / 2 + math.sin(t * math.pi * 2.2) * 12
        y = h - t * h
        r = 6 + t * 10
        a = int(90 * math.sin(t * math.pi))
        d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 255, 255, a))
    return img.filter(ImageFilter.GaussianBlur(5))


def glass() -> Image.Image:
    """A cutting-chai glass seen from above at an angle, 64 x 84."""
    w, h = 64, 84
    img = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img, "RGBA")
    d.polygon([(s(6), s(10)), (s(58), s(10)), (s(50), s(80)), (s(14), s(80))], fill=(255, 255, 255, 70), outline=(255, 255, 255, 170), width=s(2))
    d.polygon([(s(9), s(32)), (s(55), s(32)), (s(50), s(78)), (s(14), s(78))], fill=(184, 101, 31, 235))
    d.ellipse([s(6), s(4), s(58), s(18)], outline=(255, 255, 255, 200), width=s(2))
    d.ellipse([s(9), s(26), s(55), s(38)], fill=(212, 138, 58, 255))
    return img.resize((w, h), Image.LANCZOS)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    glow().save(OUT / "glow.png", optimize=True)
    steam().save(OUT / "steam.png", optimize=True)
    glass().save(OUT / "glass.png", optimize=True)


if __name__ == "__main__":
    main()
