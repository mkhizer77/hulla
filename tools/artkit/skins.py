"""Placeholder art for the five table themes (docs/design/table-themes.md) until the art track lands.

For each theme: the backdrop behind every screen, the whole table seen from above, the avatar
frame, the card back, nine-slice buttons, panels and name plates, and the small sprites the
ambient layer animates (bulbs, lamps, garlands, petals, steam). Deterministic, so a rerun changes
nothing. Run from the repository root:

    python3 tools/artkit/skins.py
"""

from __future__ import annotations

import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "apps/unity/Hulla/Assets/_Project/Presentation/Theme/Art/Skins"
FONTS = ROOT / "apps/unity/Hulla/Assets/_Project/Presentation/Theme/Fonts"

W, H = 1080, 1920
TABLE = (940, 1060)
SS = 2  # supersampling for drawn shapes

RGB = tuple[int, int, int]


def hexc(value: str) -> RGB:
    value = value.lstrip("#")
    return (int(value[0:2], 16), int(value[2:4], 16), int(value[4:6], 16))


def lerp(a: RGB, b: RGB, t: float) -> RGB:
    t = max(0.0, min(1.0, t))
    return (int(a[0] + (b[0] - a[0]) * t), int(a[1] + (b[1] - a[1]) * t), int(a[2] + (b[2] - a[2]) * t))


def rgba(c: RGB, a: int = 255) -> tuple[int, int, int, int]:
    return (c[0], c[1], c[2], a)


def vertical(size: tuple[int, int], stops: list[tuple[float, RGB]]) -> Image.Image:
    """A vertical gradient through the given (position, colour) stops."""
    w, h = size
    strip = Image.new("RGB", (1, h))
    px = strip.load()
    for y in range(h):
        t = y / max(1, h - 1)
        for i in range(len(stops) - 1):
            (t0, c0), (t1, c1) = stops[i], stops[i + 1]
            if t0 <= t <= t1:
                px[0, y] = lerp(c0, c1, (t - t0) / max(1e-6, t1 - t0))
                break
        else:
            px[0, y] = stops[-1][1]
    return strip.resize((w, h)).convert("RGBA")


def radial_mask(size: tuple[int, int], centre: tuple[float, float], radius: tuple[float, float]) -> Image.Image:
    """L mask: 0 at the centre rising to 255 at the radius and beyond."""
    w, h = size
    g = Image.radial_gradient("L").resize((max(1, int(radius[0] * 2)), max(1, int(radius[1] * 2))))
    mask = Image.new("L", size, 255)
    mask.paste(g, (int(centre[0] - radius[0]), int(centre[1] - radius[1])))
    return mask


def radial(size: tuple[int, int], centre: tuple[float, float], radius: tuple[float, float], inner: RGB, outer: RGB) -> Image.Image:
    return Image.composite(Image.new("RGBA", size, rgba(outer)), Image.new("RGBA", size, rgba(inner)), radial_mask(size, centre, radius))


def glow_layer(size: tuple[int, int], centre: tuple[float, float], radius: float, colour: RGB, strength: int) -> Image.Image:
    mask = radial_mask(size, centre, (radius, radius)).point(lambda v: int(max(0, 255 - v) ** 1.6 / 255 ** 0.6 * strength / 255))
    layer = Image.new("RGBA", size, rgba(colour, 0))
    layer.putalpha(mask)
    return layer


def vignette(img: Image.Image, strength: int = 170, start: float = 0.55) -> Image.Image:
    w, h = img.size
    mask = radial_mask((w, h), (w / 2, h / 2), (w * 0.75, h * 0.62)).point(lambda v: int(min(255, max(0, (v / 255 - start) / (1 - start)) ** 1.5 * strength)))
    shade = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    shade.putalpha(mask)
    out = img.copy()
    out.alpha_composite(shade)
    return out


def grain(img: Image.Image, amount: int = 10, seed: int = 1) -> Image.Image:
    """Fine grain from a seeded noise field, mixed in at a low alpha."""
    rng = random.Random(seed)
    w, h = img.size
    small = Image.frombytes("L", (w // 2, h // 2), rng.randbytes((w // 2) * (h // 2))).resize((w, h))
    layer = Image.merge("RGBA", (small, small, small, Image.new("L", (w, h), amount)))
    out = img.copy()
    alpha = out.getchannel("A")
    out.alpha_composite(layer)
    out.putalpha(alpha)
    return out


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONTS / name), size)


def text_centre(draw: ImageDraw.ImageDraw, xy: tuple[float, float], text: str, f: ImageFont.FreeTypeFont, fill, spacing: int = 0) -> None:
    if spacing:
        widths = [draw.textlength(ch, font=f) for ch in text]
        total = sum(widths) + spacing * (len(text) - 1)
        x = xy[0] - total / 2
        for ch, cw in zip(text, widths):
            draw.text((x, xy[1]), ch, font=f, fill=fill, anchor="lm")
            x += cw + spacing
        return
    draw.text(xy, text, font=f, fill=fill, anchor="mm")


def big(size: tuple[int, int]) -> tuple[Image.Image, ImageDraw.ImageDraw]:
    img = Image.new("RGBA", (size[0] * SS, size[1] * SS), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img, "RGBA")


def down(img: Image.Image) -> Image.Image:
    return img.resize((img.width // SS, img.height // SS), Image.LANCZOS)


def s(v: float) -> int:
    return int(round(v * SS))


def mask_rounded(size: tuple[int, int], box, radius: int) -> Image.Image:
    m = Image.new("L", (size[0] * SS, size[1] * SS), 0)
    ImageDraw.Draw(m).rounded_rectangle([s(v) for v in box], radius=s(radius), fill=255)
    return m.resize(size, Image.LANCZOS)


def shadowed(img: Image.Image, blur: int = 18, offset: int = 14, alpha: int = 150) -> Image.Image:
    """Adds a soft drop shadow under the opaque parts of a sprite (canvas must have room)."""
    a = img.getchannel("A").point(lambda v: v * alpha // 255)
    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sh.putalpha(a)
    sh = sh.filter(ImageFilter.GaussianBlur(blur))
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    out.alpha_composite(sh, (0, offset))
    out.alpha_composite(img)
    return out


def star8(draw: ImageDraw.ImageDraw, cx: float, cy: float, r: float, outline=None, fill=None, width: int = 1) -> None:
    pts = []
    for i in range(16):
        a = math.pi / 8 * i - math.pi / 2
        rr = r if i % 2 == 0 else r * 0.62
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    draw.polygon(pts, outline=outline, fill=fill, width=width)


def suit(draw: ImageDraw.ImageDraw, cx: float, cy: float, size: float, kind: str, fill) -> None:
    """A suit symbol drawn as shapes (the bundled fonts have no suit glyphs)."""
    r = size / 2
    if kind in "HS":
        sign = 1 if kind == "H" else -1
        lobe = r * 0.52
        ly = cy - sign * r * 0.28
        draw.ellipse([cx - r * 0.98, ly - lobe, cx - r * 0.98 + lobe * 2, ly + lobe], fill=fill)
        draw.ellipse([cx + r * 0.98 - lobe * 2, ly - lobe, cx + r * 0.98, ly + lobe], fill=fill)
        draw.polygon([(cx - r * 0.95, ly + sign * lobe * 0.25), (cx + r * 0.95, ly + sign * lobe * 0.25), (cx, cy + sign * r)], fill=fill)
        if kind == "S":
            draw.polygon([(cx, cy + r * 0.1), (cx + r * 0.35, cy + r), (cx - r * 0.35, cy + r)], fill=fill)
    elif kind == "D":
        draw.polygon([(cx, cy - r), (cx + r * 0.75, cy), (cx, cy + r), (cx - r * 0.75, cy)], fill=fill)
    else:
        lobe = r * 0.45
        for (dx, dy) in [(0, -0.5), (-0.5, 0.15), (0.5, 0.15)]:
            draw.ellipse([cx + dx * r - lobe, cy + dy * r - lobe, cx + dx * r + lobe, cy + dy * r + lobe], fill=fill)
        draw.polygon([(cx - r * 0.1, cy), (cx + r * 0.1, cy), (cx + r * 0.35, cy + r), (cx - r * 0.35, cy + r)], fill=fill)


# ---------------------------------------------------------------- backdrops


def backdrop_haveli() -> Image.Image:
    img = vertical((W, H), [(0, hexc("3A1A10")), (0.55, hexc("2A120A")), (1, hexc("160803"))])
    img.alpha_composite(glow_layer((W, H), (W / 2, 260), 760, hexc("FFAA50"), 90))
    big_img, d = big((W, H))
    # Arches along the top: dark recesses with a brass edge and a cusped crown.
    arch_w, arch_h, gap = 230, 640, 34
    total = 4 * arch_w + 3 * gap
    x0 = (W - total) / 2
    for i in range(4):
        x = x0 + i * (arch_w + gap)
        box = (x, -40, x + arch_w, arch_h)
        d.rounded_rectangle([s(v) for v in box], radius=s(arch_w / 2), fill=(0, 0, 0, 70), outline=rgba(hexc("C9973F"), 70), width=s(5))
        d.rounded_rectangle([s(v) for v in (x + 22, -20, x + arch_w - 22, arch_h - 26)], radius=s(arch_w / 2 - 22), outline=rgba(hexc("C9973F"), 30), width=s(3))
        for k in range(5):
            cy = arch_h - 70 - k * 100
            star8(d, s(x + arch_w / 2), s(cy), s(20), outline=rgba(hexc("C9973F"), 28), width=s(2))
    # A frieze under the arches.
    d.rectangle([s(0), s(arch_h), s(W), s(arch_h + 22)], fill=rgba(hexc("4A2412"), 230))
    for i in range(0, W, 36):
        d.ellipse([s(i + 10), s(arch_h + 5), s(i + 22), s(arch_h + 17)], fill=rgba(hexc("C9973F"), 110))
    img.alpha_composite(down(big_img))
    # A floor of tiles in perspective.
    floor_top = 1240
    floor = Image.new("RGBA", (W, H - floor_top), (0, 0, 0, 0))
    fd = ImageDraw.Draw(floor, "RGBA")
    rows = 9
    for r in range(rows):
        y0 = (r / rows) ** 1.6 * (H - floor_top)
        y1 = ((r + 1) / rows) ** 1.6 * (H - floor_top)
        cols = 14
        for c in range(-cols, cols + 1):
            def xp(col: float, yy: float) -> float:
                spread = 0.35 + 0.65 * (yy / (H - floor_top))
                return W / 2 + col * 120 * spread * 1.6
            colour = hexc("3C1F12") if (r + c) % 2 == 0 else hexc("2B150B")
            fd.polygon([(xp(c, y0), y0), (xp(c + 1, y0), y0), (xp(c + 1, y1), y1), (xp(c, y1), y1)], fill=rgba(colour, 235))
    fade = vertical((W, H - floor_top), [(0, (0, 0, 0)), (0.25, (255, 255, 255)), (1, (255, 255, 255))]).convert("L")
    floor.putalpha(Image.composite(floor.getchannel("A"), Image.new("L", floor.size, 0), fade))
    img.alpha_composite(floor, (0, floor_top))
    return vignette(img, 190, 0.5).convert("RGB")


def backdrop_dhaba() -> Image.Image:
    img = vertical((W, H), [(0, hexc("10163F")), (0.32, hexc("2C2160")), (0.5, hexc("4A2A5A")), (0.62, hexc("5A2E4E")), (1, hexc("3A2228"))])
    rng = random.Random(4)
    d = ImageDraw.Draw(img, "RGBA")
    for _ in range(160):
        x, y = rng.randrange(W), rng.randrange(0, 760)
        r = rng.choice([1, 1, 1.5, 2])
        d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 255, 255, rng.randint(80, 200)))
    # The city: three rows of buildings, the nearest darkest, with a few lit windows.
    for row, (base, col, hmin, hmax) in enumerate([(820, hexc("2A1F5C"), 80, 220), (900, hexc("221A4C"), 120, 300), (980, hexc("1A1440"), 60, 200)]):
        x = -20
        while x < W:
            bw = rng.randint(60, 150)
            bh = rng.randint(hmin, hmax)
            d.rectangle([x, base - bh, x + bw, base + 400], fill=rgba(col))
            if rng.random() < 0.3:
                d.rectangle([x + bw // 2 - 3, base - bh - 40, x + bw // 2 + 3, base - bh], fill=rgba(col))
            for wy in range(base - bh + 14, base + 60, 26):
                for wx in range(x + 10, x + bw - 12, 22):
                    if rng.random() < 0.18:
                        d.rectangle([wx, wy, wx + 8, wy + 11], fill=(255, 207, 90, rng.randint(90, 180)))
            x += bw + rng.randint(0, 12)
    # A water tank on a stand, the rooftop parapet and the floor of the roof.
    d.rectangle([840, 760, 1000, 900], fill=rgba(hexc("1E1846")))
    d.rectangle([860, 900, 870, 1000], fill=rgba(hexc("1E1846")))
    d.rectangle([970, 900, 980, 1000], fill=rgba(hexc("1E1846")))
    roof_top = 1040
    d.rectangle([0, roof_top, W, roof_top + 46], fill=rgba(hexc("6E4436")))
    d.rectangle([0, roof_top, W, roof_top + 8], fill=rgba(hexc("8A5A48")))
    roof = vertical((W, H - roof_top - 46), [(0, hexc("5A3A30")), (1, hexc("3A241E"))])
    rd = ImageDraw.Draw(roof, "RGBA")
    for x in range(0, W, 90):
        rd.line([(x, 0), (x, roof.height)], fill=(0, 0, 0, 50), width=3)
    for y in range(0, roof.height, 140):
        rd.line([(0, y), (W, y)], fill=(0, 0, 0, 35), width=2)
    img.alpha_composite(roof, (0, roof_top + 46))
    img = grain(img, 8, 3)
    return vignette(img, 150, 0.55).convert("RGB")


def backdrop_club() -> Image.Image:
    img = radial((W, H), (W / 2, H * 0.42), (W * 0.95, H * 0.62), hexc("2A3A78"), hexc("0A0F26"))
    big_img, d = big((W, H))
    # A damask wall: medallions on a diamond lattice, barely there.
    step = 120
    for j in range(-1, H // step + 2):
        for i in range(-1, W // step + 2):
            cx = i * step + (step / 2 if j % 2 else 0)
            cy = j * step
            d.ellipse([s(cx - 22), s(cy - 30), s(cx + 22), s(cy + 30)], outline=(255, 255, 255, 14), width=s(3))
            d.ellipse([s(cx - 6), s(cy - 6), s(cx + 6), s(cy + 6)], fill=(255, 255, 255, 16))
            d.line([s(cx), s(cy + 30), s(cx + step / 2), s(cy + step / 2)], fill=(255, 255, 255, 8), width=s(2))
            d.line([s(cx), s(cy + 30), s(cx - step / 2), s(cy + step / 2)], fill=(255, 255, 255, 8), width=s(2))
    img.alpha_composite(down(big_img))
    # A wainscot rail low on the wall.
    d2 = ImageDraw.Draw(img, "RGBA")
    d2.rectangle([0, 1500, W, H], fill=(6, 8, 22, 150))
    d2.line([(0, 1500), (W, 1500)], fill=rgba(hexc("D4A642"), 90), width=4)
    d2.line([(0, 1512), (W, 1512)], fill=rgba(hexc("D4A642"), 40), width=2)
    return vignette(img, 200, 0.45).convert("RGB")


def backdrop_mehndi() -> Image.Image:
    img = radial((W, H), (W / 2, H * 0.46), (W * 0.9, H * 0.6), hexc("7A1650"), hexc("2E041E"))
    big_img, d = big((W, H))
    step = 72
    for j in range(H // step + 2):
        for i in range(W // step + 2):
            cx = i * step + (step / 2 if j % 2 else 0)
            cy = j * step
            d.ellipse([s(cx - 4), s(cy - 4), s(cx + 4), s(cy + 4)], fill=(255, 194, 51, 30))
            if (i + j) % 3 == 0:
                for k in range(6):
                    a = k * math.pi / 3
                    px, py = cx + math.cos(a) * 12, cy + math.sin(a) * 12
                    d.ellipse([s(px - 5), s(py - 5), s(px + 5), s(py + 5)], fill=(255, 194, 51, 16))
    img.alpha_composite(down(big_img))
    return vignette(img, 170, 0.5).convert("RGB")


def backdrop_adda() -> Image.Image:
    img = Image.new("RGBA", (W, H))
    rng = random.Random(8)
    plank = 180
    for x in range(0, W, plank):
        base = lerp(hexc("6E421E"), hexc("85532A"), rng.random())
        strip = Image.new("RGBA", (plank, H), rgba(base))
        sd = ImageDraw.Draw(strip, "RGBA")
        streaks = Image.frombytes("L", (plank // 6, 24), rng.randbytes(plank // 6 * 24)).resize((plank, H), Image.BICUBIC)
        strip.alpha_composite(Image.merge("RGBA", (Image.new("L", (plank, H), 0),) * 3 + (streaks.point(lambda v: v // 9),)))
        for _ in range(3):
            ky = rng.randrange(H)
            kx = rng.randrange(20, plank - 20)
            for k in range(6):
                sd.ellipse([kx - 6 - k * 5, ky - 3 - k * 8, kx + 6 + k * 5, ky + 3 + k * 8], outline=(40, 20, 6, 14))
        sd.line([(0, 0), (0, H)], fill=(25, 12, 4, 160), width=4)
        img.paste(strip, (x, 0))
    img = grain(img, 5, 9)
    img = img.filter(ImageFilter.GaussianBlur(1.2))
    shade = vertical((W, H), [(0, (0, 0, 0)), (0.5, (0, 0, 0)), (1, (0, 0, 0))])
    shade.putalpha(vertical((W, H), [(0, (40, 40, 40)), (0.45, (60, 60, 60)), (1, (150, 150, 150))]).convert("L"))
    img.alpha_composite(shade)
    img.alpha_composite(glow_layer((W, H), (W / 2, H * 0.4), 900, hexc("FFC878"), 60))
    return vignette(img, 210, 0.45).convert("RGB")


# ---------------------------------------------------------------- tables


def table_haveli() -> Image.Image:
    w, h = TABLE
    img, d = big(TABLE)
    r = 300
    d.rounded_rectangle([s(10), s(10), s(w - 10), s(h - 10)], radius=s(r), fill=rgba(hexc("4A2812")))
    d.rounded_rectangle([s(10), s(10), s(w - 10), s(h - 10)], radius=s(r), outline=rgba(hexc("7A4A24")), width=s(6))
    d.rounded_rectangle([s(34), s(34), s(w - 34), s(h - 34)], radius=s(r - 24), outline=rgba(hexc("2E170A")), width=s(4))
    felt_box = (52, 52, w - 52, h - 52)
    out = down(img)
    felt = radial(TABLE, (w / 2, h * 0.45), (w * 0.62, h * 0.6), hexc("982A3E"), hexc("520C1A"))
    fd = ImageDraw.Draw(felt, "RGBA")
    step = 46
    for j in range(h // step + 2):
        for i in range(w // step + 2):
            cx = i * step + (step / 2 if j % 2 else 0)
            cy = j * step
            fd.ellipse([cx - 2.5, cy - 2.5, cx + 2.5, cy + 2.5], fill=(255, 210, 150, 34))
    for k in range(48):
        a = k / 48 * 2 * math.pi
        x, y = w / 2 + math.cos(a) * 230, h / 2 + math.sin(a) * 150
        fd.ellipse([x - 4, y - 4, x + 4, y + 4], fill=(255, 210, 150, 40))
    for (cx, cy) in [(w / 2, h / 2 - 210), (w / 2, h / 2 + 210)]:
        star8(fd, cx, cy, 44, outline=(255, 210, 150, 34), width=3)
    text_centre(fd, (w / 2, h / 2), "HULLA", font("RozhaOne.ttf", 96), (255, 215, 150, 20))
    felt.putalpha(mask_rounded(TABLE, felt_box, r - 42))
    out.alpha_composite(felt)
    od = ImageDraw.Draw(out, "RGBA")
    od.rounded_rectangle(felt_box, radius=r - 42, outline=rgba(hexc("C9973F")), width=3)
    od.rounded_rectangle((felt_box[0] + 8, felt_box[1] + 8, felt_box[2] - 8, felt_box[3] - 8), radius=r - 50, outline=rgba(hexc("C9973F"), 80), width=1)
    # Brass studs along the straight runs of the rail.
    for x in range(r - 40, w - r + 41, 70):
        for y in (31, h - 31):
            od.ellipse([x - 5, y - 5, x + 5, y + 5], fill=rgba(hexc("E2B45C")), outline=rgba(hexc("6A3A1C")))
    for y in range(r - 40, h - r + 41, 70):
        for x in (31, w - 31):
            od.ellipse([x - 5, y - 5, x + 5, y + 5], fill=rgba(hexc("E2B45C")), outline=rgba(hexc("6A3A1C")))
    shine = vertical(TABLE, [(0, (255, 230, 190)), (0.06, (255, 230, 190)), (0.12, (0, 0, 0))]).convert("L").point(lambda v: v // 10)
    hl = Image.new("RGBA", TABLE, (255, 230, 190, 0))
    hl.putalpha(Image.composite(shine, Image.new("L", TABLE, 0), mask_rounded(TABLE, (10, 10, w - 10, h - 10), r)))
    out.alpha_composite(hl)
    return out


def table_dhaba() -> Image.Image:
    w, h = TABLE
    img, d = big(TABLE)
    r = 110
    d.rounded_rectangle([s(6), s(6), s(w - 6), s(h - 6)], radius=s(r), fill=(17, 17, 17, 255))
    out = down(img)
    rim = Image.new("RGBA", TABLE, (0, 0, 0, 0))
    rd = ImageDraw.Draw(rim, "RGBA")
    colours = [hexc("E8202A"), hexc("FFD23F"), hexc("19C37D"), hexc("1E5BD8")]
    band = 34
    for i in range(-h, w + h, band):
        rd.polygon([(i, 0), (i + band, 0), (i + band - h, h), (i - h, h)], fill=rgba(colours[(i // band) % 4]))
    for i in range(0, w + h, band):
        rd.line([(i, 0), (i - h, h)], fill=(17, 17, 17, 255), width=3)
    rim.putalpha(mask_rounded(TABLE, (14, 14, w - 14, h - 14), r - 8))
    out.alpha_composite(rim)
    od = ImageDraw.Draw(out, "RGBA")
    rng = random.Random(2)
    for _ in range(80):
        a = rng.random() * 2 * math.pi
        x = w / 2 + math.cos(a) * (w / 2 - 30)
        y = h / 2 + math.sin(a) * (h / 2 - 30)
        x, y = min(max(x, 28), w - 28), min(max(y, 28), h - 28)
        od.ellipse([x - 3, y - 3, x + 3, y + 3], fill=(255, 255, 255, 200))
    top_box = (48, 48, w - 48, h - 48)
    od.rounded_rectangle(top_box, radius=r - 40, fill=(17, 17, 17, 255))
    inner = (58, 58, w - 58, h - 58)
    tin = radial(TABLE, (w / 2, h * 0.45), (w * 0.7, h * 0.66), hexc("1AA39A"), hexc("0A5E58"))
    tdraw = ImageDraw.Draw(tin, "RGBA")
    for j in range(0, h, 64):
        for i in range(0, w, 64):
            cx, cy = i + (32 if (j // 64) % 2 else 0), j
            for k in range(5):
                a = k * 2 * math.pi / 5
                tdraw.ellipse([cx + math.cos(a) * 9 - 5, cy + math.sin(a) * 9 - 5, cx + math.cos(a) * 9 + 5, cy + math.sin(a) * 9 + 5], fill=(255, 255, 255, 18))
            tdraw.ellipse([cx - 3, cy - 3, cx + 3, cy + 3], fill=(255, 210, 63, 30))
    text_centre(tdraw, (w / 2, h / 2 - 20), "HULLA", font("LilitaOne.ttf", 104), (255, 255, 255, 26))
    text_centre(tdraw, (w / 2, h / 2 + 62), "DEKH MAGAR PYAR SE", font("Nunito.ttf", 28), (255, 255, 255, 34), spacing=6)
    # Painted flowers in the corners.
    for (cx, cy) in [(120, 120), (w - 120, 120), (120, h - 120), (w - 120, h - 120)]:
        for k in range(6):
            a = k * math.pi / 3
            tdraw.ellipse([cx + math.cos(a) * 20 - 13, cy + math.sin(a) * 20 - 13, cx + math.cos(a) * 20 + 13, cy + math.sin(a) * 20 + 13], fill=rgba(hexc("FF4F9A"), 120), outline=(17, 17, 17, 140), width=2)
        tdraw.ellipse([cx - 10, cy - 10, cx + 10, cy + 10], fill=rgba(hexc("FFD23F"), 200), outline=(17, 17, 17, 160), width=2)
    tin.putalpha(mask_rounded(TABLE, inner, r - 48))
    out.alpha_composite(tin)
    return out


def table_club() -> Image.Image:
    w, h = TABLE
    img, d = big(TABLE)
    d.ellipse([s(6), s(6), s(w - 6), s(h - 6)], fill=(20, 20, 23, 255))
    out = down(img)
    leather = vertical(TABLE, [(0, hexc("3A3A40")), (0.5, hexc("1C1C20")), (1, hexc("0C0C0E"))])
    lm = Image.new("L", (w * SS, h * SS), 0)
    ImageDraw.Draw(lm).ellipse([s(6), s(6), s(w - 6), s(h - 6)], fill=255)
    leather.putalpha(lm.resize(TABLE, Image.LANCZOS))
    leather = grain(leather, 14, 5)
    out.alpha_composite(leather)
    od = ImageDraw.Draw(out, "RGBA")
    # Stitching along the rail.
    n = 220
    for k in range(n):
        if k % 2:
            continue
        a0, a1 = k / n * 2 * math.pi, (k + 1) / n * 2 * math.pi
        rx, ry = w / 2 - 22, h / 2 - 22
        od.line([(w / 2 + math.cos(a0) * rx, h / 2 + math.sin(a0) * ry), (w / 2 + math.cos(a1) * rx, h / 2 + math.sin(a1) * ry)], fill=(255, 255, 255, 70), width=2)
    felt = radial(TABLE, (w / 2, h * 0.44), (w * 0.6, h * 0.58), hexc("27B062"), hexc("0A4E28"))
    felt = grain(felt, 18, 6)
    fd = ImageDraw.Draw(felt, "RGBA")
    text_centre(fd, (w / 2, h / 2 - 30), "HULLA", font("TitanOne.ttf", 98), (255, 255, 255, 20))
    for i, k in enumerate("SHCD"):
        suit(fd, w / 2 + (i - 1.5) * 70, h / 2 + 62, 40, k, (255, 255, 255, 22))
    fm = Image.new("L", (w * SS, h * SS), 0)
    ImageDraw.Draw(fm).ellipse([s(64), s(64), s(w - 64), s(h - 64)], fill=255)
    felt.putalpha(fm.resize(TABLE, Image.LANCZOS))
    out.alpha_composite(felt)
    big2, d2 = big(TABLE)
    d2.ellipse([s(64), s(64), s(w - 64), s(h - 64)], outline=rgba(hexc("D4A642")), width=s(5))
    d2.ellipse([s(76), s(76), s(w - 76), s(h - 76)], outline=(0, 0, 0, 70), width=s(3))
    out.alpha_composite(down(big2))
    hl = glow_layer(TABLE, (w / 2, 60), 300, (255, 255, 255), 50)
    hl.putalpha(Image.composite(hl.getchannel("A"), Image.new("L", TABLE, 0), lm.resize(TABLE)))
    out.alpha_composite(hl)
    return out


def table_mehndi() -> Image.Image:
    w, h = TABLE
    out = Image.new("RGBA", TABLE, (0, 0, 0, 0))
    od = ImageDraw.Draw(out, "RGBA")
    # Tassels top and bottom.
    for x in range(40, w - 30, 22):
        for y0, y1 in ((0, 26), (h - 26, h)):
            od.line([(x, y0), (x, y1)], fill=rgba(hexc("FFC233")), width=4)
    od.rounded_rectangle((10, 22, w - 10, h - 22), radius=18, fill=rgba(hexc("F7A600")))
    band = Image.new("RGBA", TABLE, (0, 0, 0, 0))
    bd = ImageDraw.Draw(band, "RGBA")
    cols = [hexc("2E7D32"), hexc("E91E63"), hexc("2E7D32"), hexc("FFC233")]
    for i in range(0, w + h, 14):
        bd.line([(i, 0), (i - h, h)], fill=rgba(cols[(i // 14) % 4]), width=14)
    band.putalpha(mask_rounded(TABLE, (24, 36, w - 24, h - 36), 12))
    out.alpha_composite(band)
    field_box = (50, 62, w - 50, h - 62)
    field = radial(TABLE, (w / 2, h / 2), (w * 0.6, h * 0.55), hexc("C2185B"), hexc("7C0C3E"))
    fd = ImageDraw.Draw(field, "RGBA")
    for j in range(0, h, 44):
        for i in range(0, w, 44):
            cx, cy = i + (22 if (j // 44) % 2 else 0), j
            fd.ellipse([cx - 3.5, cy - 3.5, cx + 3.5, cy + 3.5], fill=(255, 194, 51, 56))
    cx, cy = w / 2, h / 2
    sq = 190
    fd.polygon([(cx, cy - sq), (cx + sq, cy), (cx, cy + sq), (cx - sq, cy)], outline=(255, 194, 51, 100), width=5)
    fd.polygon([(cx, cy - sq + 26), (cx + sq - 26, cy), (cx, cy + sq - 26), (cx - sq + 26, cy)], outline=(255, 194, 51, 50), width=2)
    for k in range(36):
        a = k / 36 * 2 * math.pi
        fd.ellipse([cx + math.cos(a) * 92 - 4, cy + math.sin(a) * 92 - 4, cx + math.cos(a) * 92 + 4, cy + math.sin(a) * 92 + 4], fill=(255, 194, 51, 110))
    for k in range(8):
        a = k / 8 * 2 * math.pi
        fd.ellipse([cx + math.cos(a) * 42 - 16, cy + math.sin(a) * 42 - 16, cx + math.cos(a) * 42 + 16, cy + math.sin(a) * 42 + 16], fill=(255, 194, 51, 70))
    for (px, py) in [(110, 120), (w - 110, 120), (110, h - 120), (w - 110, h - 120)]:
        fd.polygon([(px, py - 34), (px + 34, py), (px, py + 34), (px - 34, py)], fill=(255, 194, 51, 60), outline=(255, 194, 51, 140), width=3)
    field.putalpha(mask_rounded(TABLE, field_box, 6))
    out.alpha_composite(field)
    od = ImageDraw.Draw(out, "RGBA")
    od.rounded_rectangle(field_box, radius=6, outline=rgba(hexc("FFC233")), width=4)
    od.rounded_rectangle((field_box[0] + 9, field_box[1] + 9, field_box[2] - 9, field_box[3] - 9), radius=4, outline=rgba(hexc("8A0E45")), width=4)
    od.rounded_rectangle((field_box[0] + 15, field_box[1] + 15, field_box[2] - 15, field_box[3] - 15), radius=4, outline=rgba(hexc("FFC233"), 200), width=3)
    return out


def table_adda() -> Image.Image:
    w, h = TABLE
    out = Image.new("RGBA", TABLE, (0, 0, 0, 0))
    od = ImageDraw.Draw(out, "RGBA")
    od.rounded_rectangle((6, 6, w - 6, h - 6), radius=40, fill=rgba(hexc("4A2A12")))
    wood = Image.new("RGBA", TABLE, rgba(hexc("5C3418")))
    wd = ImageDraw.Draw(wood, "RGBA")
    rng = random.Random(12)
    for _ in range(140):
        y = rng.randrange(h)
        wd.line([(0, y), (w, y + rng.randint(-6, 6))], fill=(0, 0, 0, rng.randint(14, 34)), width=rng.choice([1, 2]))
    wood.putalpha(mask_rounded(TABLE, (6, 6, w - 6, h - 6), 40))
    out.alpha_composite(wood)
    cloth_box = (60, 60, w - 60, h - 60)
    cloth = radial(TABLE, (w / 2, h * 0.44), (w * 0.62, h * 0.58), hexc("23887F"), hexc("0D4744"))
    cloth = grain(cloth, 14, 13)
    cd = ImageDraw.Draw(cloth, "RGBA")
    for j in range(0, h, 30):
        for i in range(0, w, 30):
            cd.ellipse([i - 1.5, j - 1.5, i + 1.5, j + 1.5], fill=(255, 255, 255, 22))
    text_centre(cd, (w / 2, h / 2), "HULLA", font("Baloo2.ttf", 100), (255, 255, 255, 20))
    # Tea rings: someone has had a few cups here.
    for (cx, cy, rr, a) in [(w - 210, 210, 46, 60), (230, h - 250, 44, 50), (w - 260, h - 300, 40, 34)]:
        cd.ellipse([cx - rr, cy - rr * 0.92, cx + rr, cy + rr * 0.92], outline=(90, 40, 10, a), width=5)
        cd.arc([cx - rr - 4, cy - rr, cx + rr - 2, cy + rr], 20, 140, fill=(90, 40, 10, a // 2), width=3)
    cloth.putalpha(mask_rounded(TABLE, cloth_box, 26))
    out.alpha_composite(cloth)
    od = ImageDraw.Draw(out, "RGBA")
    od.rounded_rectangle((cloth_box[0] + 14, cloth_box[1] + 14, cloth_box[2] - 14, cloth_box[3] - 14), radius=18, outline=(255, 230, 180, 90), width=3)
    # Brass corner plates.
    brass = hexc("D7A847")
    for (x, y, sx, sy) in [(6, 6, 1, 1), (w - 6, 6, -1, 1), (6, h - 6, 1, -1), (w - 6, h - 6, -1, -1)]:
        pts = [(x, y), (x + sx * 110, y), (x + sx * 110, y + sy * 22), (x + sx * 22, y + sy * 22), (x + sx * 22, y + sy * 110), (x, y + sy * 110)]
        od.polygon(pts, fill=rgba(brass), outline=rgba(hexc("8A5E1C")))
        for (dx, dy) in [(60, 11), (11, 60), (11, 11)]:
            od.ellipse([x + sx * dx - 4, y + sy * dy - 4, x + sx * dx + 4, y + sy * dy + 4], fill=rgba(hexc("8A5E1C")))
    return out


# ---------------------------------------------------------------- frames, backs, nine-slices


def frame(kind: str) -> Image.Image:
    """An avatar frame, 200 px, with the face showing through a 156 px hole."""
    size = 200
    img, d = big((size, size))
    c = s(size / 2)
    hole = s(78)

    def ring(r_out: float, colour, width: float) -> None:
        d.ellipse([c - s(r_out), c - s(r_out), c + s(r_out), c + s(r_out)], outline=colour, width=s(width))

    if kind == "haveli":
        ring(96, rgba(hexc("2E170A")), 4)
        ring(93, rgba(hexc("F6DC96")), 6)
        ring(87, rgba(hexc("B8862F")), 9)
        ring(80, rgba(hexc("6A3A1C")), 3)
        for k in range(24):
            a = k / 24 * 2 * math.pi
            x, y = c + math.cos(a) * s(87), c + math.sin(a) * s(87)
            d.ellipse([x - s(3), y - s(3), x + s(3), y + s(3)], fill=rgba(hexc("FFF1C0")))
    elif kind == "dhaba":
        d.rounded_rectangle([s(8), s(8), s(size - 8), s(size - 8)], radius=s(46), outline=(17, 17, 17, 255), width=s(10))
        d.rounded_rectangle([s(18), s(18), s(size - 18), s(size - 18)], radius=s(38), outline=(255, 255, 255, 90), width=s(3))
    elif kind == "club":
        for k in range(72):
            a0, a1 = k / 72 * 360, (k + 1.4) / 72 * 360
            t = 0.5 + 0.5 * math.cos(math.radians(a0 * 2 - 60))
            col = lerp(hexc("A87418"), hexc("FFE79A"), t)
            d.arc([c - s(95), c - s(95), c + s(95), c + s(95)], a0, a1, fill=rgba(col), width=s(15))
        ring(96, (0, 0, 0, 120), 2)
        ring(81, (0, 0, 0, 140), 2)
    elif kind == "mehndi":
        for k in range(36):
            a0, a1 = k / 36 * 360, (k + 1) / 36 * 360
            d.arc([c - s(94), c - s(94), c + s(94), c + s(94)], a0, a1, fill=rgba(hexc("FFB300") if k % 2 else hexc("E65100")), width=s(16))
        for k in range(12):
            a = k / 12 * 2 * math.pi
            x, y = c + math.cos(a) * s(94), c + math.sin(a) * s(94)
            d.ellipse([x - s(7), y - s(7), x + s(7), y + s(7)], fill=rgba(hexc("E91E63")), outline=rgba(hexc("8A0E45")), width=s(2))
        ring(80, rgba(hexc("8A0E45")), 4)
    else:  # adda
        ring(95, rgba(hexc("3A1E0A")), 4)
        ring(91, rgba(hexc("FFE0A0")), 5)
        ring(85, rgba(hexc("C98A2E")), 8)
        ring(80, rgba(hexc("3A1E0A")), 3)
    out = down(img)
    if kind == "dhaba":
        return out
    hole_mask = Image.new("L", (size * SS, size * SS), 255)
    ImageDraw.Draw(hole_mask).ellipse([c - hole, c - hole, c + hole, c + hole], fill=0)
    out.putalpha(Image.composite(out.getchannel("A"), Image.new("L", (size, size), 0), hole_mask.resize((size, size), Image.LANCZOS)))
    return out


def card_back(kind: str) -> Image.Image:
    w, h = 300, 420
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img, "RGBA")
    border = {"haveli": hexc("F3E2BE"), "dhaba": hexc("111111"), "club": hexc("FFFFFF"), "mehndi": hexc("FFF3E0"), "adda": hexc("FFF4E2")}[kind]
    d.rounded_rectangle((0, 0, w - 1, h - 1), radius=24, fill=rgba(border))
    inner = (16, 16, w - 17, h - 17)
    field = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    fd = ImageDraw.Draw(field, "RGBA")
    if kind == "haveli":
        fd.rectangle((0, 0, w, h), fill=rgba(hexc("6C1626")))
        for i in range(-h, w + h, 22):
            fd.line([(i, 0), (i + h, h)], fill=rgba(hexc("8A1F30")), width=8)
            fd.line([(i, h), (i + h, 0)], fill=(201, 151, 63, 50), width=2)
        star8(fd, w / 2, h / 2, 70, fill=rgba(hexc("C9973F")), outline=rgba(hexc("F6DC96")), width=3)
        star8(fd, w / 2, h / 2, 40, fill=rgba(hexc("6C1626")))
    elif kind == "dhaba":
        cols = [hexc("E8202A"), hexc("FFD23F"), hexc("19C37D"), hexc("1E5BD8")]
        for i in range(0, w, 26):
            fd.rectangle((i, 0, i + 26, h), fill=rgba(cols[(i // 26) % 4]))
            fd.line([(i, 0), (i, h)], fill=(17, 17, 17, 255), width=3)
        fd.ellipse((w / 2 - 70, h / 2 - 70, w / 2 + 70, h / 2 + 70), fill=(255, 255, 255, 255), outline=(17, 17, 17, 255), width=6)
        text_centre(fd, (w / 2, h / 2 + 4), "H", font("LilitaOne.ttf", 96), (232, 32, 42, 255))
    elif kind == "club":
        fd.rectangle((0, 0, w, h), fill=rgba(hexc("1B3A8C")))
        for j in range(0, h, 14):
            for i in range(0, w, 14):
                fd.ellipse((i + 4, j + 4, i + 10, j + 10), fill=rgba(hexc("2C59C9")))
        fd.rounded_rectangle((30, 30, w - 31, h - 31), radius=12, outline=(255, 255, 255, 200), width=3)
        suit(fd, w / 2, h / 2, 110, "S", (255, 255, 255, 230))
    elif kind == "mehndi":
        fd.rectangle((0, 0, w, h), fill=rgba(hexc("8A0E45")))
        for j in range(0, h, 30):
            for i in range(0, w, 30):
                cx, cy = i + (15 if (j // 30) % 2 else 0), j
                fd.ellipse((cx - 4, cy - 4, cx + 4, cy + 4), fill=rgba(hexc("FFC233")))
        for k in range(12):
            a = k / 12 * 2 * math.pi
            fd.ellipse((w / 2 + math.cos(a) * 50 - 18, h / 2 + math.sin(a) * 50 - 18, w / 2 + math.cos(a) * 50 + 18, h / 2 + math.sin(a) * 50 + 18), fill=rgba(hexc("FFB300")), outline=rgba(hexc("E65100")), width=3)
        fd.ellipse((w / 2 - 26, h / 2 - 26, w / 2 + 26, h / 2 + 26), fill=rgba(hexc("E91E63")))
    else:
        fd.rectangle((0, 0, w, h), fill=rgba(hexc("0F4F4A")))
        for i in range(-h, w + h, 16):
            fd.line([(i, 0), (i + h, h)], fill=rgba(hexc("1F7A72")), width=6)
        fd.ellipse((w / 2 - 64, h / 2 - 64, w / 2 + 64, h / 2 + 64), fill=rgba(hexc("D7A847")), outline=rgba(hexc("FFF4E2")), width=5)
        text_centre(fd, (w / 2, h / 2 + 4), "H", font("Baloo2.ttf", 90), rgba(hexc("3A1E0A")))
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).rounded_rectangle(inner, radius=14, fill=255)
    field.putalpha(Image.composite(field.getchannel("A"), Image.new("L", (w, h), 0), m))
    img.alpha_composite(field)
    return img


def nine(fill_top: RGB, fill_bottom: RGB, *, outline: RGB | None = None, outline_w: int = 0, lip: RGB | None = None,
         lip_h: int = 0, radius: int = 34, gloss: int = 0, size: tuple[int, int] = (200, 140), inner_line: tuple | None = None,
         dotted: RGB | None = None, shadow: int = 0) -> Image.Image:
    """A nine-slice button or panel: fill gradient, optional outline, a darker lip under it and a gloss."""
    w, h = size
    pad = 14 if shadow else 0
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    body = (pad + 2, pad + 2, w - pad - 3, h - pad - 3 - lip_h)
    if shadow:
        sh = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        ImageDraw.Draw(sh, "RGBA").rounded_rectangle((body[0], body[1] + 8, body[2], body[3] + lip_h + 8), radius=radius, fill=(0, 0, 0, shadow))
        img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(8)))
    big_img, d = big((w, h))
    if lip is not None and lip_h:
        d.rounded_rectangle([s(body[0]), s(body[1] + lip_h), s(body[2]), s(body[3] + lip_h)], radius=s(radius), fill=rgba(lip))
    if outline is not None and outline_w:
        d.rounded_rectangle([s(v) for v in body], radius=s(radius), fill=rgba(outline))
        if lip is not None and lip_h:
            d.rounded_rectangle([s(body[0]), s(body[1] + lip_h), s(body[2]), s(body[3] + lip_h)], radius=s(radius), outline=rgba(outline), width=s(outline_w))
    img.alpha_composite(down(big_img))
    inset = outline_w
    fill_box = (body[0] + inset, body[1] + inset, body[2] - inset, body[3] - inset)
    grad = vertical((w, h), [(0, fill_top), (fill_box[1] / h, fill_top), (fill_box[3] / h, fill_bottom), (1, fill_bottom)])
    grad.putalpha(mask_rounded((w, h), fill_box, max(4, radius - inset)))
    img.alpha_composite(grad)
    if gloss:
        g = Image.new("RGBA", (w, h), (255, 255, 255, 0))
        gm = mask_rounded((w, h), (fill_box[0] + 6, fill_box[1] + 4, fill_box[2] - 6, fill_box[1] + (fill_box[3] - fill_box[1]) * 0.45), max(4, radius - inset - 6))
        g.putalpha(gm.point(lambda v: v * gloss // 255))
        img.alpha_composite(g)
    dd = ImageDraw.Draw(img, "RGBA")
    if inner_line is not None:
        dd.rounded_rectangle((fill_box[0] + 5, fill_box[1] + 5, fill_box[2] - 5, fill_box[3] - 5), radius=max(4, radius - inset - 5), outline=inner_line, width=2)
    if dotted is not None:
        x0, y0, x1, y1 = fill_box[0] + 7, fill_box[1] + 7, fill_box[2] - 7, fill_box[3] - 7
        for x in range(int(x0) + 10, int(x1) - 8, 9):
            dd.ellipse((x - 1.5, y0 - 1.5, x + 1.5, y0 + 1.5), fill=rgba(dotted))
            dd.ellipse((x - 1.5, y1 - 1.5, x + 1.5, y1 + 1.5), fill=rgba(dotted))
        for y in range(int(y0) + 10, int(y1) - 8, 9):
            dd.ellipse((x0 - 1.5, y - 1.5, x0 + 1.5, y + 1.5), fill=rgba(dotted))
            dd.ellipse((x1 - 1.5, y - 1.5, x1 + 1.5, y + 1.5), fill=rgba(dotted))
    return img


def skin_nines(kind: str) -> dict[str, Image.Image]:
    if kind == "haveli":
        return {
            "btn-primary": nine(hexc("F6DC96"), hexc("A9742A"), outline=hexc("2E170A"), outline_w=3, lip=hexc("5A3512"), lip_h=7, radius=28, gloss=40),
            "btn-secondary": nine(hexc("5A3218"), hexc("2E170A"), outline=hexc("C9973F"), outline_w=3, lip=hexc("170A04"), lip_h=6, radius=28),
            "panel": nine(hexc("45260F"), hexc("24130A"), outline=hexc("C9973F"), outline_w=3, radius=34, inner_line=(201, 151, 63, 60), shadow=150, size=(220, 220)),
            "plate": nine(hexc("45260F"), hexc("24130A"), outline=hexc("C9973F"), outline_w=2, radius=14, size=(120, 64)),
            "pill": nine(hexc("1C0C06"), hexc("1C0C06"), outline=hexc("8A6428"), outline_w=2, radius=30, size=(120, 64)),
        }
    if kind == "dhaba":
        return {
            "btn-primary": nine(hexc("FFD23F"), hexc("FFC21A"), outline=hexc("111111"), outline_w=6, lip=hexc("111111"), lip_h=9, radius=26),
            "btn-secondary": nine(hexc("FFFFFF"), hexc("F2F2F2"), outline=hexc("111111"), outline_w=6, lip=hexc("111111"), lip_h=9, radius=26),
            "panel": nine(hexc("1E2A6E"), hexc("161E52"), outline=hexc("111111"), outline_w=7, lip=hexc("111111"), lip_h=10, radius=36, size=(220, 220)),
            "plate": nine(hexc("FFD23F"), hexc("FFD23F"), outline=hexc("111111"), outline_w=4, lip=hexc("111111"), lip_h=5, radius=14, size=(120, 64)),
            "pill": nine(hexc("111111"), hexc("111111"), radius=30, size=(120, 64)),
        }
    if kind == "club":
        return {
            "btn-primary": nine(hexc("7BEA6B"), hexc("219A3C"), lip=hexc("156B28"), lip_h=8, radius=28, gloss=70),
            "btn-secondary": nine(hexc("6BA8FF"), hexc("1F50B8"), lip=hexc("12347A"), lip_h=8, radius=28, gloss=60),
            "panel": nine(hexc("1A2350"), hexc("0E1430"), outline=hexc("3A4C9A"), outline_w=2, radius=34, shadow=170, size=(220, 220)),
            "plate": nine(hexc("26304F"), hexc("0E1430"), outline=hexc("4C6BD8"), outline_w=2, radius=30, size=(120, 64)),
            "pill": nine(hexc("05081A"), hexc("05081A"), outline=hexc("2A3466"), outline_w=2, radius=30, size=(120, 64)),
        }
    if kind == "mehndi":
        return {
            "btn-primary": nine(hexc("FFD54F"), hexc("F59000"), outline=hexc("8A0E45"), outline_w=3, lip=hexc("8A4A00"), lip_h=7, radius=40, gloss=50),
            "btn-secondary": nine(hexc("FF6FA4"), hexc("C2185B"), outline=hexc("4A0A30"), outline_w=3, lip=hexc("6A0A30"), lip_h=7, radius=40, gloss=40),
            "panel": nine(hexc("3C0628"), hexc("2A031C"), outline=hexc("FFC233"), outline_w=2, radius=36, dotted=hexc("FFB300"), shadow=150, size=(220, 220)),
            "plate": nine(hexc("2E7D32"), hexc("1B5E20"), outline=hexc("FFC233"), outline_w=3, radius=30, size=(120, 64)),
            "pill": nine(hexc("300520"), hexc("300520"), outline=hexc("B8862F"), outline_w=2, radius=30, size=(120, 64)),
        }
    return {
        "btn-primary": nine(hexc("FFD27A"), hexc("E8962A"), lip=hexc("8A4E10"), lip_h=8, radius=30, gloss=60),
        "btn-secondary": nine(hexc("3FB8AD"), hexc("17655E"), lip=hexc("0B3F3A"), lip_h=8, radius=30, gloss=45),
        "panel": nine(hexc("2A1608"), hexc("1E0E04"), outline=hexc("8A6428"), outline_w=2, radius=34, shadow=150, size=(220, 220)),
        "plate": nine(hexc("FFF4E2"), hexc("EADBC2"), lip=hexc("3A1E0A"), lip_h=4, radius=14, size=(120, 64)),
        "pill": nine(hexc("1E0E04"), hexc("1E0E04"), outline=hexc("8A6428"), outline_w=2, radius=30, size=(120, 64)),
    }


# ---------------------------------------------------------------- ambient sprites


def soft(size: int, colour: RGB = (255, 255, 255), power: float = 2.2) -> Image.Image:
    img = Image.new("RGBA", (size, size), rgba(colour, 0))
    mask = radial_mask((size, size), (size / 2, size / 2), (size / 2, size / 2)).point(lambda v: int(255 * max(0.0, 1 - v / 255) ** power))
    img.putalpha(mask)
    return img


def bulb() -> Image.Image:
    """A small round bulb, white so the layer tints it; a bright core and a cap."""
    size = 48
    img, d = big((size, size))
    d.rectangle([s(19), s(0), s(29), s(10)], fill=(60, 60, 60, 255))
    d.ellipse([s(10), s(8), s(38), s(40)], fill=(255, 255, 255, 235))
    d.ellipse([s(15), s(13), s(25), s(25)], fill=(255, 255, 255, 255))
    return down(img)


def lamp() -> Image.Image:
    """A hanging brass lantern with pierced work, 120 x 220."""
    w, h = 120, 220
    img, d = big((w, h))
    brass, dark = rgba(hexc("C9973F")), rgba(hexc("4A2812"))
    d.line([s(60), s(0), s(60), s(40)], fill=brass, width=s(3))
    d.polygon([(s(40), s(40)), (s(80), s(40)), (s(96), s(70)), (s(24), s(70))], fill=brass, outline=dark)
    d.rounded_rectangle([s(28), s(70), s(92), s(170)], radius=s(10), fill=(255, 214, 140, 235), outline=brass, width=s(5))
    for k in range(3):
        for j in range(5):
            d.ellipse([s(40 + k * 20 - 4), s(82 + j * 18 - 4), s(40 + k * 20 + 4), s(82 + j * 18 + 4)], fill=rgba(hexc("FFF4D0")))
    d.polygon([(s(24), s(170)), (s(96), s(170)), (s(76), s(196)), (s(44), s(196))], fill=brass, outline=dark)
    d.ellipse([s(54), s(196), s(66), s(214)], fill=brass)
    return down(img)


def marigold_strand() -> Image.Image:
    """A hanging string of marigolds, 36 x 420, ending in a pink tassel."""
    w, h = 36, 420
    img, d = big((w, h))
    d.line([s(18), s(0), s(18), s(h - 30)], fill=(120, 60, 0, 255), width=s(2))
    y = 12
    i = 0
    while y < h - 40:
        col = hexc("FFB300") if i % 3 else hexc("E65100")
        for k in range(8):
            a = k / 8 * 2 * math.pi
            x2, y2 = 18 + math.cos(a) * 8, y + math.sin(a) * 8
            d.ellipse([s(x2 - 6), s(y2 - 6), s(x2 + 6), s(y2 + 6)], fill=rgba(col), outline=rgba(lerp(col, (120, 40, 0), 0.4)), width=s(1))
        d.ellipse([s(14), s(y - 4), s(22), s(y + 4)], fill=rgba(hexc("A85A00")))
        y += 20
        i += 1
    d.ellipse([s(8), s(h - 40), s(28), s(h - 4)], fill=rgba(hexc("E91E63")))
    return down(img)


def petal() -> Image.Image:
    size = 28
    img, d = big((size, size))
    d.ellipse([s(4), s(9), s(24), s(19)], fill=(255, 255, 255, 255))
    return down(img).rotate(30, resample=Image.BICUBIC)


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


def cone() -> Image.Image:
    """A spotlight cone from the top centre, 600 x 900, white."""
    w, h = 600, 900
    img = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    m = Image.new("L", (w, h), 0)
    md = ImageDraw.Draw(m)
    md.polygon([(w * 0.42, 0), (w * 0.58, 0), (w, h), (0, h)], fill=255)
    m = m.filter(ImageFilter.GaussianBlur(40))
    fade = vertical((w, h), [(0, (255, 255, 255)), (1, (60, 60, 60))]).convert("L")
    img.putalpha(Image.composite(m, Image.new("L", (w, h), 0), fade).point(lambda v: v * 200 // 255))
    return img


def glass() -> Image.Image:
    """A cutting-chai glass seen from above at an angle, 64 x 84."""
    w, h = 64, 84
    img, d = big((w, h))
    d.polygon([(s(6), s(10)), (s(58), s(10)), (s(50), s(80)), (s(14), s(80))], fill=(255, 255, 255, 70), outline=(255, 255, 255, 170), width=s(2))
    d.polygon([(s(9), s(32)), (s(55), s(32)), (s(50), s(78)), (s(14), s(78))], fill=rgba(hexc("B8651F"), 235))
    d.ellipse([s(6), s(4), s(58), s(18)], outline=(255, 255, 255, 200), width=s(2))
    d.ellipse([s(9), s(26), s(55), s(38)], fill=rgba(hexc("D48A3A")))
    return down(img)


def stamp_burst() -> Image.Image:
    """The comic burst behind THULLA! on the rooftop, yellow with a red heart, 900 x 460."""
    w, h = 900, 460
    img, d = big((w, h))
    def burst(scale: float, colour) -> None:
        pts = []
        n = 22
        for i in range(n * 2):
            a = i / (n * 2) * 2 * math.pi
            r = (1.0 if i % 2 == 0 else 0.72) * scale
            pts.append((s(w / 2 + math.cos(a) * w / 2 * r), s(h / 2 + math.sin(a) * h / 2 * r)))
        d.polygon(pts, fill=colour)
    burst(0.98, (17, 17, 17, 255))
    burst(0.93, rgba(hexc("FFD23F")))
    burst(0.80, (17, 17, 17, 255))
    burst(0.76, rgba(hexc("E8202A")))
    return down(img)


def stamp_ribbon() -> Image.Image:
    """The club's red banner with notched ends, 900 x 170."""
    w, h = 900, 170
    img, d = big((w, h))
    d.polygon([(s(0), s(26)), (s(w), s(26)), (s(w - 50), s(h / 2 + 13)), (s(w), s(h)), (s(0), s(h)), (s(50), s(h / 2 + 13))], fill=rgba(hexc("7A0E14")))
    d.polygon([(s(0), s(0)), (s(w), s(0)), (s(w - 50), s(h / 2 - 13)), (s(w), s(h - 26)), (s(0), s(h - 26)), (s(50), s(h / 2 - 13))], fill=rgba(hexc("D6202E")))
    d.rectangle([s(60), s(8), s(w - 60), s(40)], fill=(255, 255, 255, 40))
    return down(img)


def stamp_oval() -> Image.Image:
    """The mehndi stamp: a magenta oval in a marigold ring, 760 x 300."""
    w, h = 760, 300
    img, d = big((w, h))
    d.ellipse([s(4), s(4), s(w - 4), s(h - 4)], fill=rgba(hexc("E65100")))
    d.ellipse([s(16), s(16), s(w - 16), s(h - 16)], fill=rgba(hexc("FFC233")))
    d.ellipse([s(28), s(28), s(w - 28), s(h - 28)], fill=rgba(hexc("C2185B")))
    for k in range(40):
        a = k / 40 * 2 * math.pi
        x, y = w / 2 + math.cos(a) * (w / 2 - 10), h / 2 + math.sin(a) * (h / 2 - 10)
        d.ellipse([s(x - 5), s(y - 5), s(x + 5), s(y + 5)], fill=rgba(hexc("FFB300")))
    return down(img)


def preview(backdrop: Image.Image, table: Image.Image) -> Image.Image:
    """The settings tile: the scene with its table, 240 x 320."""
    w, h = 480, 640
    img = backdrop.convert("RGBA").resize((W // 2, H // 2)).crop((0, 140, w, 140 + h))
    t = table.resize((360, 406))
    canvas = Image.new("RGBA", (420, 470), (0, 0, 0, 0))
    canvas.alpha_composite(t, (30, 20))
    img.alpha_composite(shadowed(canvas, 14, 12, 160), (30, 120))
    return img.resize((240, 320), Image.LANCZOS)


SKINS = {
    "haveli": (backdrop_haveli, table_haveli),
    "dhaba": (backdrop_dhaba, table_dhaba),
    "club": (backdrop_club, table_club),
    "mehndi": (backdrop_mehndi, table_mehndi),
    "adda": (backdrop_adda, table_adda),
}


def main() -> None:
    shared = OUT / "shared"
    shared.mkdir(parents=True, exist_ok=True)
    soft(64).save(shared / "glow.png", optimize=True)
    soft(16, power=1.4).save(shared / "mote.png", optimize=True)
    bulb().save(shared / "bulb.png", optimize=True)
    lamp().save(shared / "lamp.png", optimize=True)
    marigold_strand().save(shared / "marigolds.png", optimize=True)
    petal().save(shared / "petal.png", optimize=True)
    steam().save(shared / "steam.png", optimize=True)
    cone().save(shared / "cone.png", optimize=True)
    glass().save(shared / "glass.png", optimize=True)
    stamp_burst().save(shared / "stamp-burst.png", optimize=True)
    stamp_ribbon().save(shared / "stamp-ribbon.png", optimize=True)
    stamp_oval().save(shared / "stamp-oval.png", optimize=True)
    for kind, (make_backdrop, make_table) in SKINS.items():
        folder = OUT / kind
        folder.mkdir(parents=True, exist_ok=True)
        backdrop = make_backdrop()
        backdrop.save(folder / "backdrop.png", optimize=True)
        table = make_table()
        shadowed_table = Image.new("RGBA", (TABLE[0] + 60, TABLE[1] + 80), (0, 0, 0, 0))
        shadowed_table.alpha_composite(table, (30, 20))
        shadowed(shadowed_table, 22, 22, 170).save(folder / "table.png", optimize=True)
        frame(kind).save(folder / "frame.png", optimize=True)
        card_back(kind).save(folder / "back.png", optimize=True)
        for name, img in skin_nines(kind).items():
            img.save(folder / f"{name}.png", optimize=True)
        preview(backdrop, table).save(folder / "preview.png", optimize=True)
    for p in sorted(OUT.rglob("*.png")):
        print(p.relative_to(OUT), p.stat().st_size)


if __name__ == "__main__":
    main()
