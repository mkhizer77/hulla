"""Stand-in table art for the Rooftop redesign until the art pack ships a felt and a rail.

The pack's table and lobby screens show a turquoise felt inside a carved wood rail with painted
flowers, but the pack has no sprite for either. This draws both in the pack's palette (handover §3):
the in-round table (portrait, rounded) and the round lobby table. Deterministic; run from the
repository root:

    python3 tools/artkit/rooftop_table.py
"""

from __future__ import annotations

import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "apps/unity/Hulla/Assets/_Project/Presentation/Theme/Art/Skins/Rooftop/Frames"
K = 2

WOOD_TOP = (120, 74, 41)
WOOD = (74, 46, 27)
WOOD_SHADOW = (42, 23, 12)
BRASS = (212, 155, 53)
BRASS_LIGHT = (255, 224, 130)
PEACOCK = (0, 137, 123)
FELT_DARK = (0, 77, 64)
MAGENTA = (194, 24, 91)
CRIMSON = (211, 47, 47)
LEAF = (46, 125, 50)


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def shape_mask(size, box, kind, inset=0):
    m = Image.new("L", (size[0] * K, size[1] * K), 0)
    d = ImageDraw.Draw(m)
    x0, y0, x1, y1 = [v * K for v in box]
    i = inset * K
    if kind == "round":
        d.ellipse((x0 + i, y0 + i, x1 - i, y1 - i), fill=255)
    else:
        r = (x1 - x0) * 0.42 - i
        d.rounded_rectangle((x0 + i, y0 + i, x1 - i, y1 - i), radius=max(10, int(r)), fill=255)
    return m


def radial(size, centre, radius, inner, outer):
    w, h = size[0] * K, size[1] * K
    g = Image.radial_gradient("L").resize((int(radius[0] * 2 * K), int(radius[1] * 2 * K)))
    m = Image.new("L", (w, h), 255)
    m.paste(g, (int((centre[0] - radius[0]) * K), int((centre[1] - radius[1]) * K)))
    return Image.composite(Image.new("RGBA", (w, h), outer + (255,)), Image.new("RGBA", (w, h), inner + (255,)), m)


def flower(d, cx, cy, r, colour):
    for k in range(5):
        a = k * 2 * math.pi / 5 - math.pi / 2
        px, py = cx + math.cos(a) * r * 0.55, cy + math.sin(a) * r * 0.55
        d.ellipse(((px - r * 0.45) * K, (py - r * 0.45) * K, (px + r * 0.45) * K, (py + r * 0.45) * K), fill=colour + (255,))
    d.ellipse(((cx - r * 0.28) * K, (cy - r * 0.28) * K, (cx + r * 0.28) * K, (cy + r * 0.28) * K), fill=BRASS_LIGHT + (255,))


def leaf(d, cx, cy, length, angle):
    a = math.radians(angle)
    tip = (cx + math.cos(a) * length, cy + math.sin(a) * length)
    n = (-math.sin(a) * length * 0.2, math.cos(a) * length * 0.2)
    mid = ((cx + tip[0]) / 2, (cy + tip[1]) / 2)
    pts = [(cx, cy), (mid[0] + n[0], mid[1] + n[1]), tip, (mid[0] - n[0], mid[1] - n[1])]
    d.polygon([(x * K, y * K) for x, y in pts], fill=LEAF + (255,))


def table(size, box, kind, rail_w, rng):
    w, h = size
    img = Image.new("RGBA", (w * K, h * K), (0, 0, 0, 0))
    # Shadow under the table.
    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sh.putalpha(shape_mask(size, (box[0], box[1] + 18, box[2], box[3] + 18), kind).point(lambda v: v * 170 // 255))
    img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(18 * K)))
    # The rail: carved wood, lighter on top.
    cx, cy = (box[0] + box[2]) / 2, (box[1] + box[3]) / 2
    rail = radial(size, (cx, cy - 40), ((box[2] - box[0]) * 0.75, (box[3] - box[1]) * 0.75), WOOD_TOP, WOOD)
    rail.putalpha(shape_mask(size, box, kind))
    img.alpha_composite(rail)
    d = ImageDraw.Draw(img, "RGBA")
    # Wood grain rings and a dark outer edge.
    for k in range(6):
        ring = shape_mask(size, box, kind, inset=4 + k * (rail_w - 16) / 6)
        edge = ring.filter(ImageFilter.FIND_EDGES)
        layer = Image.new("RGBA", img.size, WOOD_SHADOW + (0,))
        layer.putalpha(edge.point(lambda v, k=k: min(255, v) * (60 if k else 220) // 255))
        img.alpha_composite(layer)
    # Brass pinstripe inside the rail.
    pin = shape_mask(size, box, kind, inset=rail_w - 10).filter(ImageFilter.FIND_EDGES).filter(ImageFilter.MaxFilter(5))
    layer = Image.new("RGBA", img.size, BRASS + (0,))
    layer.putalpha(pin)
    img.alpha_composite(layer)
    # The felt: turquoise, darker towards the rail, a faint medallion in the middle.
    felt = radial(size, (cx, cy), ((box[2] - box[0]) * 0.55, (box[3] - box[1]) * 0.55), PEACOCK, FELT_DARK)
    fm = shape_mask(size, box, kind, inset=rail_w)
    felt.putalpha(fm)
    img.alpha_composite(felt)
    mark = Image.new("RGBA", img.size, (0, 0, 0, 0))
    md = ImageDraw.Draw(mark)
    mr = min(box[2] - box[0], box[3] - box[1]) * 0.18
    for k in range(12):
        a = k * math.pi / 6
        px, py = cx + math.cos(a) * mr * 0.6, cy + math.sin(a) * mr * 0.6
        md.ellipse(((px - mr * 0.4) * K, (py - mr * 0.4) * K, (px + mr * 0.4) * K, (py + mr * 0.4) * K), outline=(255, 255, 255, 26), width=3 * K)
    md.ellipse(((cx - mr) * K, (cy - mr) * K, (cx + mr) * K, (cy + mr) * K), outline=(255, 255, 255, 30), width=3 * K)
    img.alpha_composite(mark)
    # Felt edge shadow under the rail lip.
    lip = shape_mask(size, box, kind, inset=rail_w).filter(ImageFilter.FIND_EDGES).filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(6 * K))
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    layer.putalpha(lip.point(lambda v: v * 120 // 255))
    img.alpha_composite(layer)
    # Painted flowers on the rail.
    d = ImageDraw.Draw(img, "RGBA")
    spots = 8 if kind == "round" else 6
    for k in range(spots):
        if kind == "round":
            a = k * 2 * math.pi / spots + math.pi / spots
            rx, ry = (box[2] - box[0]) / 2 - rail_w / 2, (box[3] - box[1]) / 2 - rail_w / 2
            fx, fy = cx + math.cos(a) * rx, cy + math.sin(a) * ry
        else:
            fx = box[0] + rail_w / 2 if k % 2 == 0 else box[2] - rail_w / 2
            fy = box[1] + (box[3] - box[1]) * (0.36 + 0.14 * (k // 2))
        leaf(d, fx, fy, rail_w * 0.45, rng.uniform(0, 360))
        leaf(d, fx, fy, rail_w * 0.45, rng.uniform(0, 360))
        flower(d, fx, fy, rail_w * 0.34, MAGENTA if k % 2 else CRIMSON)
    return img.resize(size, Image.LANCZOS)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    rng = random.Random(7)
    table((1000, 1140), (30, 20, 970, 1100), "rounded", 64, rng).save(OUT / "table_felt_rail.png", optimize=True)
    table((880, 880), (30, 20, 850, 840), "round", 58, rng).save(OUT / "lobby_table_round.png", optimize=True)
    for name in ("table_felt_rail.png", "lobby_table_round.png"):
        print(name)


if __name__ == "__main__":
    main()
