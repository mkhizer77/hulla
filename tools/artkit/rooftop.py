"""Placeholder art for the Rooftop look of Home (docs/design/home-concept.md) until painted art lands.

Every piece is a separate sprite with no text in it, so labels stay in the string tables: the night
backdrop with minarets, rooftops and kites, the hanging name sign, the level shield, the coin pouch,
the truck-art game frame with peacocks, the seat medallions, the Casual/Ranked switch, the Play now
plaque, the wooden plank buttons, the reward chest, the navigation board and its four icons. Each
file keeps its name when the painted version replaces it. Deterministic; run from the repository root:

    python3 tools/artkit/rooftop.py
"""

from __future__ import annotations

import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "apps/unity/Hulla/Assets/_Project/Presentation/Theme/Art/Skins/dhaba"

K = 3  # supersampling
RGB = tuple[int, int, int]


def hexc(value: str) -> RGB:
    value = value.lstrip("#")
    return (int(value[0:2], 16), int(value[2:4], 16), int(value[4:6], 16))


def lerp(a: RGB, b: RGB, t: float) -> RGB:
    t = max(0.0, min(1.0, t))
    return (int(a[0] + (b[0] - a[0]) * t), int(a[1] + (b[1] - a[1]) * t), int(a[2] + (b[2] - a[2]) * t))


def A(c: RGB, a: int = 255) -> tuple[int, int, int, int]:
    return (c[0], c[1], c[2], a)


# The palette, read off the concept.
INK = hexc("2A140A")
WOOD_DARK = hexc("4A2812")
WOOD = hexc("7A4824")
WOOD_LIGHT = hexc("9C6436")
WOOD_HI = hexc("B97D48")
GOLD = hexc("F2B632")
GOLD_LIGHT = hexc("FFE07A")
GOLD_DARK = hexc("A8661A")
RED = hexc("C7362A")
RED_DARK = hexc("8E1E1A")
ORANGE = hexc("E8702A")
TEAL = hexc("1C8A86")
TEAL_DARK = hexc("0F4F4A")
GREEN = hexc("3E9A3A")
GREEN_DARK = hexc("1F6A2A")
PINK = hexc("F25C7A")
ROSE = hexc("E0304A")
BLUE = hexc("2A72C8")
CREAM = hexc("FFF1D6")
FELT_TOP = hexc("1F6250")
FELT_BOTTOM = hexc("124538")


class Canvas:
    """A supersampled RGBA canvas; coordinates are in final pixels."""

    def __init__(self, w: int, h: int):
        self.w, self.h = w, h
        self.img = Image.new("RGBA", (w * K, h * K), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img, "RGBA")

    def p(self, pts):
        return [(x * K, y * K) for x, y in pts]

    def box(self, b):
        return [b[0] * K, b[1] * K, b[2] * K, b[3] * K]

    def rrect(self, b, r, fill=None, outline=None, width=0):
        self.d.rounded_rectangle(self.box(b), radius=r * K, fill=fill, outline=outline, width=int(width * K))

    def ellipse(self, b, fill=None, outline=None, width=0):
        self.d.ellipse(self.box(b), fill=fill, outline=outline, width=int(width * K))

    def circle(self, cx, cy, r, fill=None, outline=None, width=0):
        self.ellipse((cx - r, cy - r, cx + r, cy + r), fill, outline, width)

    def poly(self, pts, fill=None, outline=None, width=0):
        self.d.polygon(self.p(pts), fill=fill, outline=outline, width=int(width * K) if width else 1)

    def line(self, pts, fill, width):
        self.d.line(self.p(pts), fill=fill, width=max(1, int(width * K)), joint="curve")

    def layer(self) -> "Canvas":
        return Canvas(self.w, self.h)

    def paste(self, other: "Canvas", blur: float = 0) -> None:
        im = other.img
        if blur:
            im = im.filter(ImageFilter.GaussianBlur(blur * K))
        self.img.alpha_composite(im)
        self.d = ImageDraw.Draw(self.img, "RGBA")

    def gradient_fill(self, mask_canvas: "Canvas", top: RGB, bottom: RGB, y0: float, y1: float) -> None:
        """Fills the opaque parts of mask_canvas with a vertical gradient between y0 and y1."""
        grad = Image.new("RGBA", (1, self.h * K))
        px = grad.load()
        for y in range(self.h * K):
            t = (y / K - y0) / max(1e-6, y1 - y0)
            px[0, y] = A(lerp(top, bottom, t))
        grad = grad.resize((self.w * K, self.h * K))
        grad.putalpha(mask_canvas.img.getchannel("A"))
        self.img.alpha_composite(grad)
        self.d = ImageDraw.Draw(self.img, "RGBA")

    def out(self) -> Image.Image:
        return self.img.resize((self.w, self.h), Image.LANCZOS)


def shadow_under(img: Image.Image, blur: float = 10, dy: float = 8, alpha: int = 140) -> Image.Image:
    a = img.getchannel("A").point(lambda v: v * alpha // 255)
    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sh.putalpha(a)
    sh = sh.filter(ImageFilter.GaussianBlur(blur))
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    out.alpha_composite(sh, (0, int(dy)))
    out.alpha_composite(img)
    return out


# ---------------------------------------------------------------- motifs


def petal_flower(c: Canvas, cx, cy, r, petal: RGB, centre: RGB = GOLD, petals: int = 6, rot: float = 0, ring: RGB | None = None):
    """A truck-art rose: round petals around a dotted centre, a darker inner ring."""
    for i in range(petals):
        a = rot + i * 2 * math.pi / petals
        px, py = cx + math.cos(a) * r * 0.55, cy + math.sin(a) * r * 0.55
        c.circle(px, py, r * 0.5, fill=A(lerp(petal, (0, 0, 0), 0.25)))
    for i in range(petals):
        a = rot + i * 2 * math.pi / petals
        px, py = cx + math.cos(a) * r * 0.5, cy + math.sin(a) * r * 0.5
        c.circle(px, py, r * 0.42, fill=A(petal))
        c.circle(px + math.cos(a) * r * 0.12, py + math.sin(a) * r * 0.12 - r * 0.06, r * 0.16, fill=A(lerp(petal, (255, 255, 255), 0.45), 200))
    c.circle(cx, cy, r * 0.42, fill=A(ring or lerp(petal, (0, 0, 0), 0.35)))
    c.circle(cx, cy, r * 0.3, fill=A(centre))
    for i in range(8):
        a = i * math.pi / 4
        c.circle(cx + math.cos(a) * r * 0.2, cy + math.sin(a) * r * 0.2, r * 0.05, fill=A(INK, 160))


def leaf(c: Canvas, x, y, length, angle, colour: RGB = GREEN, width: float = 0.38):
    """A pointed leaf from (x, y) along angle, with a lighter vein."""
    a = math.radians(angle)
    tip = (x + math.cos(a) * length, y + math.sin(a) * length)
    mid = (x + math.cos(a) * length * 0.5, y + math.sin(a) * length * 0.5)
    n = (-math.sin(a) * length * width / 2, math.cos(a) * length * width / 2)
    pts = []
    for i in range(13):
        t = i / 12
        bx, by = x + (tip[0] - x) * t, y + (tip[1] - y) * t
        k = math.sin(math.pi * t) ** 0.8
        pts.append((bx + n[0] * k, by + n[1] * k))
    for i in range(12, -1, -1):
        t = i / 12
        bx, by = x + (tip[0] - x) * t, y + (tip[1] - y) * t
        k = math.sin(math.pi * t) ** 0.8
        pts.append((bx - n[0] * k, by - n[1] * k))
    c.poly(pts, fill=A(colour))
    c.line([(x, y), mid, tip], fill=A(lerp(colour, (255, 255, 160), 0.45), 200), width=max(1, length * 0.04))


def sprig(c: Canvas, x, y, angle, size, rng: random.Random, flower: RGB | None = None):
    """A curling stem with leaves along it and a flower at the end."""
    pts = []
    a = math.radians(angle)
    curl = rng.choice([-1, 1]) * 0.9
    px, py = x, y
    for i in range(10):
        t = i / 9
        aa = a + curl * t * 1.2
        px += math.cos(aa) * size / 9
        py += math.sin(aa) * size / 9
        pts.append((px, py))
    c.line([(x, y)] + pts, fill=A(GREEN_DARK), width=size * 0.05)
    for i in (2, 4, 6):
        lx, ly = pts[i]
        side = 1 if i % 4 == 2 else -1
        leaf(c, lx, ly, size * 0.32, math.degrees(a + curl * i / 9 * 1.2) + 60 * side, GREEN if i != 4 else hexc("5CB040"))
    if flower is not None:
        petal_flower(c, pts[-1][0], pts[-1][1], size * 0.2, flower, petals=5, rot=rng.random())


def peacock(c: Canvas, x, y, s: float, flip: bool = False):
    """A seated peacock facing inward with its train hanging in a teardrop below; (x, y) is the body."""
    f = -1 if flip else 1

    def P(dx, dy):
        return (x + dx * s * f, y + dy * s)

    # The train: a long teardrop of green with rows of eyes, drooping down and a little outward.
    left, right = [], []
    for k in range(25):
        t = k / 24
        cx_, cy_ = P(-4 - 18 * t, 20 + 210 * t)
        half = (8 + 34 * math.sin(math.pi * min(1.0, t * 1.1)) ** 0.9) * s
        left.append((cx_ - half, cy_))
        right.append((cx_ + half, cy_))
    tail = left + right[::-1]
    c.poly([(px, py + 4) for px, py in tail], fill=A(INK, 150))
    m = c.layer()
    m.poly(tail, fill=(0, 0, 0, 255))
    c.gradient_fill(m, hexc("2E8A3A"), hexc("1A6A5A"), y + 20 * s, y + 230 * s)
    for k in range(1, 24, 2):
        t = k / 24
        cx_, cy_ = P(-4 - 18 * t, 20 + 210 * t)
        c.line([P(-4, 20), (cx_, cy_)], fill=A(hexc("7ACB5A"), 90), width=1.2 * s)
    for row, (t, n) in enumerate([(0.38, 2), (0.58, 3), (0.78, 3), (0.94, 2)]):
        cx_, cy_ = P(-4 - 18 * t, 20 + 210 * t)
        half = (8 + 34 * math.sin(math.pi * min(1.0, t * 1.1)) ** 0.9) * s
        for i in range(n):
            ex = cx_ + (i - (n - 1) / 2) * half * (0.95 if n == 3 else 0.9)
            r = 8.5 * s
            c.ellipse((ex - r, cy_ - r * 1.25, ex + r, cy_ + r * 1.25), fill=A(GOLD))
            c.ellipse((ex - r * 0.68, cy_ - r * 0.9, ex + r * 0.68, cy_ + r * 0.9), fill=A(hexc("22A4A0")))
            c.ellipse((ex - r * 0.38, cy_ - r * 0.5, ex + r * 0.38, cy_ + r * 0.5), fill=A(hexc("15287A")))
    c.poly(tail, outline=A(hexc("0E3A2A")), width=2 * s)
    # Body, wing, neck, head.
    body = (min(P(-24, 0)[0], P(26, 0)[0]), y - 28 * s, max(P(-24, 0)[0], P(26, 0)[0]), y + 34 * s)
    c.ellipse(body, fill=A(hexc("1B5FB8")), outline=A(INK), width=2 * s)
    wing = [P(-20, -4), P(10, -12), P(24, 8), P(6, 32), P(-20, 26)]
    c.poly(wing, fill=A(hexc("2F7A3A")), outline=A(hexc("123A1A")), width=1.5 * s)
    for k in range(4):
        c.line([P(-14 + k * 8, -2 + k), P(-12 + k * 8, 24)], fill=A(hexc("8FD06A"), 150), width=1.4 * s)
    neck = [P(2, -18), P(18, -24), P(22, -50), P(18, -70), P(28, -84), P(40, -80), P(34, -66), P(36, -46), P(28, -12)]
    c.poly(neck, fill=A(hexc("1B66C4")), outline=A(INK), width=1.6 * s)
    c.line([P(22, -30), P(27, -56), P(28, -72)], fill=A(hexc("6AB4F4"), 180), width=2.6 * s)
    hx, hy = P(34, -82)
    c.circle(hx, hy, 9 * s, fill=A(hexc("1B66C4")), outline=A(INK), width=1.6 * s)
    c.poly([P(41, -85), P(53, -79), P(41, -75)], fill=A(GOLD), outline=A(INK), width=1)
    ex, ey = P(36, -85)
    c.circle(ex, ey, 3 * s, fill=A((255, 255, 255)))
    c.circle(ex + 0.4 * s * f, ey, 1.5 * s, fill=A(INK))
    for dx in (-6, 0, 6):
        tx, ty = P(30 + dx, -106 + abs(dx) * 0.5)
        c.line([P(33 + dx * 0.25, -90), (tx, ty)], fill=A(hexc("1B66C4")), width=1.5 * s)
        c.circle(tx, ty, 2.8 * s, fill=A(hexc("22A4A0")))
    for dx in (-2, 12):
        c.line([P(dx, 30), P(dx + 1, 42)], fill=A(GOLD_DARK), width=2.6 * s)


def wood_grain(c: Canvas, b, rng: random.Random, colour: RGB = WOOD_DARK, alpha: int = 70, lines: int = 7):
    x0, y0, x1, y1 = b
    for i in range(lines):
        y = y0 + (y1 - y0) * (i + 0.5) / lines + rng.uniform(-3, 3)
        pts = []
        for k in range(21):
            x = x0 + (x1 - x0) * k / 20
            pts.append((x, y + math.sin(k * 0.9 + i) * rng.uniform(0.5, 2.2)))
        c.line(pts, fill=A(colour, alpha), width=1.4)
    for _ in range(2):
        kx, ky = rng.uniform(x0 + 30, x1 - 30), rng.uniform(y0 + 10, y1 - 10)
        c.ellipse((kx - 9, ky - 3.5, kx + 9, ky + 3.5), outline=A(colour, alpha + 20), width=1.3)


def plank_shape(c: Canvas, b, r, top: RGB, bottom: RGB, rng: random.Random, edge: RGB = WOOD_DARK, edge_w: float = 5):
    m = c.layer()
    m.rrect(b, r, fill=(0, 0, 0, 255))
    c.rrect((b[0], b[1] + 6, b[2], b[3] + 6), r, fill=A(hexc("2A1408")))
    c.gradient_fill(m, top, bottom, b[1], b[3])
    wood_grain(c, (b[0] + 10, b[1] + 8, b[2] - 10, b[3] - 8), rng)
    c.rrect(b, r, outline=A(edge), width=edge_w)
    c.rrect((b[0] + edge_w + 2, b[1] + edge_w + 1, b[2] - edge_w - 2, b[1] + edge_w + 4), 2, fill=A(WOOD_HI, 110))


def nail(c: Canvas, x, y, r=7):
    c.circle(x, y + 1.5, r, fill=A(INK, 160))
    c.circle(x, y, r, fill=A(GOLD_DARK))
    c.circle(x - r * 0.25, y - r * 0.25, r * 0.6, fill=A(GOLD_LIGHT))


def dotted_line(c: Canvas, pts, colour, r, step):
    total = 0.0
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        seg = math.hypot(bx - ax, by - ay)
        t = 0.0
        while t < seg:
            k = t / seg
            c.circle(ax + (bx - ax) * k, ay + (by - ay) * k, r, fill=colour)
            t += step
        total += seg


def zigzag_band(c: Canvas, x0, x1, y, h, a: RGB, b: RGB, step: float = 14):
    """A row of alternating triangles, the classic truck-art border."""
    x = x0
    flip = False
    while x < x1:
        nx = min(x1, x + step)
        if flip:
            c.poly([(x, y + h), ((x + nx) / 2, y), (nx, y + h)], fill=A(a))
        else:
            c.poly([(x, y), ((x + nx) / 2, y + h), (nx, y)], fill=A(b))
        x += step / 2
        flip = not flip


# ---------------------------------------------------------------- backdrop


def kite(c: Canvas, x, y, size, angle, body: RGB, rng: random.Random):
    a = math.radians(angle)

    def rot(dx, dy):
        return (x + dx * math.cos(a) - dy * math.sin(a), y + dx * math.sin(a) + dy * math.cos(a))

    pts = [rot(0, -size), rot(size * 0.72, -size * 0.1), rot(0, size), rot(-size * 0.72, -size * 0.1)]
    c.poly(pts, fill=A(body), outline=A(lerp(body, (255, 180, 90), 0.5), 200), width=1.2)
    c.line([rot(0, -size), rot(0, size)], fill=A(lerp(body, (255, 200, 120), 0.4), 150), width=1)
    tail = [rot(0, size)]
    tx, ty = tail[0]
    for k in range(6):
        tx += rng.uniform(-6, 6)
        ty += size * 0.45
        tail.append((tx, ty))
    c.line(tail, fill=A(lerp(body, (255, 200, 120), 0.3), 140), width=1)


def minaret(c: Canvas, x, base, height, w, col):
    c.rrect((x - w / 2, base - height, x + w / 2, base), 2, fill=A(col))
    for k in range(3):
        by = base - height * (0.35 + k * 0.22)
        c.rrect((x - w * 0.85, by - 6, x + w * 0.85, by), 2, fill=A(col))
    top = base - height
    c.rrect((x - w * 0.8, top - 26, x + w * 0.8, top), 3, fill=A(col))
    c.ellipse((x - w * 0.8, top - 46, x + w * 0.8, top - 10), fill=A(col))
    c.line([(x, top - 46), (x, top - 62)], fill=A(col), width=2)


def dome(c: Canvas, x, base, r, col):
    c.rrect((x - r * 1.05, base - r * 0.5, x + r * 1.05, base), 2, fill=A(col))
    c.ellipse((x - r, base - r * 1.6, x + r, base - r * 0.1), fill=A(col))
    c.poly([(x - r * 0.18, base - r * 1.5), (x, base - r * 1.95), (x + r * 0.18, base - r * 1.5)], fill=A(col))


def backdrop() -> Image.Image:
    """The night over the old city: stars, kites, a mosque's domes and minarets, rooftops with lit windows."""
    W, H = 1080, 1920
    rng = random.Random(42)
    sky = Image.new("RGBA", (1, H))
    px = sky.load()
    stops = [(0, hexc("15123A")), (0.25, hexc("221B4A")), (0.5, hexc("33275C")), (0.62, hexc("4A3268")), (0.72, hexc("3A2A55")), (1, hexc("1A1530"))]
    for y in range(H):
        t = y / (H - 1)
        for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
            if t0 <= t <= t1:
                px[0, y] = A(lerp(c0, c1, (t - t0) / (t1 - t0)))
                break
    img = sky.resize((W, H))
    c = Canvas(W, H)
    c.img = img.resize((W * K, H * K))
    c.d = ImageDraw.Draw(c.img, "RGBA")
    # A warm haze over the horizon.
    haze = c.layer()
    haze.ellipse((-200, 980, W + 200, 1500), fill=A(hexc("8A4A6A"), 70))
    c.paste(haze, blur=60)
    for _ in range(140):
        x, y = rng.uniform(0, W), rng.uniform(0, 1050)
        r = rng.choice([0.9, 1.1, 1.4, 1.9])
        c.circle(x, y, r, fill=(255, 240, 220, rng.randint(70, 190)))
    for x, y, sz, ang in [(110, 345, 20, -18), (965, 420, 17, 14), (895, 1045, 14, 8), (230, 1010, 13, -10), (1015, 760, 12, -6), (70, 640, 11, 12), (835, 280, 10, 20)]:
        glow = c.layer()
        glow.circle(x, y, sz * 1.6, fill=A(hexc("FF9A40"), 60))
        c.paste(glow, blur=10)
        kite(c, x, y, sz, ang, hexc("2A1E3E"), rng)
    # Far: the mosque, domes and minarets.
    far = hexc("34285A")
    far_layer = c.layer()
    minaret(far_layer, 70, 1240, 430, 34, far)
    minaret(far_layer, 1010, 1240, 450, 34, far)
    minaret(far_layer, 920, 1240, 300, 26, far)
    minaret(far_layer, 170, 1240, 280, 26, far)
    dome(far_layer, 120, 1240, 60, far)
    dome(far_layer, 960, 1240, 66, far)
    far_layer.rrect((0, 1180, W, 1300), 0, fill=A(far))
    c.paste(far_layer)
    # Middle: rooftops, water tanks, a dish, lit windows.
    mid = hexc("261C40")
    x = -10
    tops = []
    while x < W:
        bw = rng.randint(90, 180)
        top = rng.randint(1250, 1330)
        c.rrect((x, top, x + bw, H), 0, fill=A(mid))
        tops.append((x, top, bw))
        for wy in range(top + 26, top + 260, 46):
            for wx in range(int(x) + 16, int(x + bw) - 24, 40):
                if rng.random() < 0.22:
                    gl = c.layer()
                    gl.rrect((wx - 6, wy - 6, wx + 22, wy + 30), 4, fill=A(hexc("FFA040"), 90))
                    c.paste(gl, blur=6)
                    c.rrect((wx, wy, wx + 16, wy + 24), 1, fill=A(hexc("F5A848"), 230))
        x += bw + rng.randint(-6, 8)
    # A tank on one roof and a dish on another.
    tx, ttop, tw = tops[1]
    c.rrect((tx + 20, ttop - 70, tx + 90, ttop - 14), 6, fill=A(mid))
    c.line([(tx + 30, ttop - 14), (tx + 30, ttop)], fill=A(mid), width=5)
    c.line([(tx + 80, ttop - 14), (tx + 80, ttop)], fill=A(mid), width=5)
    dx, dtop, dw = tops[-2]
    c.ellipse((dx + dw - 90, dtop - 80, dx + dw - 20, dtop - 20), fill=A(mid))
    c.line([(dx + dw - 55, dtop - 40), (dx + dw - 40, dtop)], fill=A(mid), width=6)
    # Near: the parapet we stand on, darkest.
    near = hexc("17112A")
    c.rrect((0, 1430, W, H), 0, fill=A(near))
    for xx in range(0, W, 120):
        c.rrect((xx, 1418, xx + 70, 1440), 3, fill=A(near))
    out = c.out()
    grain = Image.frombytes("L", (W, H), random.Random(3).randbytes(W * H)).point(lambda v: 128 + (v - 128) // 7)
    noise = Image.merge("RGBA", (grain, grain, grain, Image.new("L", (W, H), 9)))
    out.alpha_composite(noise)
    return out.convert("RGB")


# ---------------------------------------------------------------- header


def rope(height: int = 420) -> Image.Image:
    """A twisted jute rope, stretched to whatever drop the layout needs."""
    w = 16
    c = Canvas(w, height)
    c.rrect((3, 0, w - 3, height), 4, fill=A(hexc("8A6234")))
    for y in range(-10, height + 10, 9):
        c.line([(3, y), (w - 3, y + 7)], fill=A(hexc("5A3A1A")), width=2)
        c.line([(4, y + 2), (w - 5, y + 7.5)], fill=A(hexc("C09060"), 160), width=1.2)
    return c.out()


def sign() -> Image.Image:
    """The name board: a carved plank with arrowed ends, a raised inner panel and brass nails."""
    w, h = 660, 170
    c = Canvas(w, h)
    rng = random.Random(7)
    cy = h / 2 - 4
    outline = [(10, cy), (52, 18), (w - 52, 18), (w - 10, cy), (w - 52, h - 26), (52, h - 26)]
    c.poly([(x, y + 7) for x, y in outline], fill=A(hexc("24120A")))
    m = c.layer()
    m.poly(outline, fill=(0, 0, 0, 255))
    c.gradient_fill(m, WOOD_LIGHT, WOOD_DARK, 18, h - 26)
    c.poly(outline, outline=A(INK), width=4)
    # Scroll ends.
    for side in (1, -1):
        x = 40 if side == 1 else w - 40
        c.circle(x, cy, 17, fill=A(WOOD_DARK), outline=A(INK), width=3)
        c.circle(x, cy, 9, fill=A(GOLD), outline=A(GOLD_DARK), width=2)
        c.circle(x - 2, cy - 2, 4, fill=A(GOLD_LIGHT))
    inner = (78, 30, w - 78, h - 38)
    c.rrect((inner[0], inner[1] + 3, inner[2], inner[3] + 3), 14, fill=A(hexc("2A1408"), 160))
    im = c.layer()
    im.rrect(inner, 14, fill=(0, 0, 0, 255))
    c.gradient_fill(im, hexc("6A3C1C"), hexc("4C2A12"), inner[1], inner[3])
    wood_grain(c, (inner[0] + 8, inner[1] + 6, inner[2] - 8, inner[3] - 6), rng, hexc("2E180A"), 80, 5)
    c.rrect(inner, 14, outline=A(hexc("2E1608")), width=3)
    c.rrect((inner[0] + 4, inner[1] + 4, inner[2] - 4, inner[1] + 7), 2, fill=A(hexc("A06838"), 120))
    for x in (inner[0] + 16, inner[2] - 16):
        nail(c, x, inner[1] + 14, 5)
        nail(c, x, inner[3] - 14, 5)
    # Brackets where the ropes tie on.
    for x in (150, w - 150):
        c.rrect((x - 12, 6, x + 12, 26), 5, fill=A(GOLD_DARK), outline=A(INK), width=2)
        c.circle(x, 14, 4, fill=A(INK))
    return shadow_under(c.out(), 6, 6, 120)


def shield() -> Image.Image:
    """The level badge: a gold-rimmed shield under the sign; the level is a label on top."""
    w, h = 130, 146
    c = Canvas(w, h)
    pts = [(10, 22), (w / 2, 6), (w - 10, 22), (w - 12, 74), (w / 2, h - 8), (12, 74)]
    curved = []
    for (ax, ay), (bx, by) in zip(pts, pts[1:] + pts[:1]):
        curved.append((ax, ay))
    c.poly([(x, y + 5) for x, y in curved], fill=A(INK, 200))
    c.poly(curved, fill=A(GOLD_DARK))
    inset = [(20, 28), (w / 2, 16), (w - 20, 28), (w - 22, 72), (w / 2, h - 22), (22, 72)]
    m = c.layer()
    m.poly(pts, fill=(0, 0, 0, 255))
    c.gradient_fill(m, GOLD_LIGHT, GOLD_DARK, 6, h - 8)
    c.poly(pts, outline=A(INK), width=3.5)
    mi = c.layer()
    mi.poly(inset, fill=(0, 0, 0, 255))
    c.gradient_fill(mi, hexc("5A3A2A"), hexc("2E1C14"), 16, h - 22)
    c.poly(inset, outline=A(hexc("6A3E10")), width=2.5)
    for x, y in [(w / 2, 9), (14, 24), (w - 14, 24)]:
        c.circle(x, y, 3.5, fill=A(GOLD_LIGHT))
    return c.out()


def pouch() -> Image.Image:
    """A gold coin pouch tied with a cord, a coin with a star on its front."""
    w, h = 170, 170
    c = Canvas(w, h)
    body = [(28, 140), (22, 108), (40, 74), (64, 58), (106, 58), (130, 74), (148, 108), (142, 140), (122, 158), (48, 158)]
    sm = c.layer()
    sm.ellipse((30, 150, 140, 168), fill=A(INK, 120))
    c.paste(sm, blur=4)
    m = c.layer()
    m.poly(body, fill=(0, 0, 0, 255))
    m.ellipse((22, 84, 148, 162), fill=(0, 0, 0, 255))
    c.gradient_fill(m, hexc("F6CC56"), hexc("A86A18"), 58, 162)
    c.ellipse((22, 84, 148, 162), outline=A(hexc("6A3E0C")), width=3)
    c.line([(40, 74), (64, 58), (106, 58), (130, 74)], fill=A(hexc("6A3E0C")), width=3)
    # Neck, ruffle and cord.
    neck = [(58, 62), (54, 40), (116, 40), (112, 62)]
    c.poly(neck, fill=A(hexc("E0AE3E")), outline=A(hexc("6A3E0C")), width=3)
    for i, x in enumerate(range(52, 122, 14)):
        c.ellipse((x - 2, 24 + (i % 2) * 4, x + 16, 46), fill=A(hexc("F2C850")), outline=A(hexc("6A3E0C")), width=2.5)
    c.rrect((52, 52, 118, 66), 6, fill=A(hexc("8A4A1A")), outline=A(INK), width=2)
    c.line([(110, 60), (130, 78), (124, 98)], fill=A(hexc("8A4A1A")), width=4)
    c.circle(124, 100, 5, fill=A(hexc("C0703A")))
    # Sheen and folds.
    c.line([(50, 96), (44, 128)], fill=A(hexc("FFF0B0"), 150), width=5)
    c.line([(84, 70), (80, 92)], fill=A(hexc("7A4410"), 120), width=2)
    # The coin.
    c.circle(88, 120, 28, fill=A(GOLD_DARK), outline=A(INK), width=2.5)
    c.circle(88, 118, 24, fill=A(GOLD))
    c.circle(88, 118, 18, outline=A(GOLD_DARK), width=2)
    star = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        r = 11 if i % 2 == 0 else 4.6
        star.append((88 + math.cos(a) * r, 118 + math.sin(a) * r))
    c.poly(star, fill=A(GOLD_DARK))
    return c.out()


# ---------------------------------------------------------------- the game frame


def hero_frame() -> Image.Image:
    """The Thulla card: a truck-art frame with a crest, roses, leaves and two peacocks around a felt panel."""
    w, h = 760, 860
    c = Canvas(w, h)
    rng = random.Random(11)
    top = 78
    outer = (8, top, w - 8, h - 8)
    sh = c.layer()
    sh.rrect((outer[0], outer[1] + 16, outer[2], outer[3] + 16), 34, fill=A(INK, 210))
    c.paste(sh, blur=12)
    # The crest over the top edge: an orange arch with a teal inner arch, roses and leaves.
    def arch(x0, x1, base, height, n=48):
        pts = []
        for i in range(n + 1):
            t = i / n
            pts.append((x0 + (x1 - x0) * t, base - math.sin(math.pi * t) ** 0.62 * height))
        return pts
    outer_arch = arch(140, w - 140, top + 26, 100)
    c.poly([(px, py + 8) for px, py in outer_arch], fill=A(INK, 200))
    c.poly(outer_arch, fill=A(ORANGE), outline=A(INK), width=4)
    c.line(arch(156, w - 156, top + 22, 86), fill=A(GOLD), width=5)
    inner_arch = arch(196, w - 196, top + 30, 66)
    c.poly(inner_arch, fill=A(TEAL_DARK), outline=A(GOLD), width=3)
    dotted_line(c, arch(176, w - 176, top + 24, 78), A(CREAM), 2.2, 11)
    # Outer frame: red-orange, gold rim, cream dots.
    c.rrect(outer, 36, fill=A(RED_DARK))
    m = c.layer()
    m.rrect((outer[0] + 5, outer[1] + 5, outer[2] - 5, outer[3] - 5), 32, fill=(0, 0, 0, 255))
    c.gradient_fill(m, hexc("E0502E"), hexc("C23424"), outer[1], outer[3])
    c.rrect(outer, 36, outline=A(INK), width=4)
    c.rrect((outer[0] + 9, outer[1] + 9, outer[2] - 9, outer[3] - 9), 28, outline=A(GOLD), width=4)
    for (a, b) in [((30, outer[1] + 20), (w - 30, outer[1] + 20)), ((30, outer[3] - 20), (w - 30, outer[3] - 20)),
                   ((outer[0] + 20, outer[1] + 34), (outer[0] + 20, outer[3] - 34)), ((outer[2] - 20, outer[1] + 34), (outer[2] - 20, outer[3] - 34))]:
        dotted_line(c, [a, b], A(CREAM, 230), 2.3, 12)
    # The inner border: teal with a gold zigzag, then the felt.
    panel = (outer[0] + 92, outer[1] + 86, outer[2] - 92, outer[3] - 80)
    c.rrect((panel[0] - 16, panel[1] - 16, panel[2] + 16, panel[3] + 16), 26, fill=A(TEAL_DARK), outline=A(INK), width=3)
    zz = c.layer()
    zigzag_band(zz, panel[0] - 2, panel[2] + 2, panel[1] - 13, 9, GOLD, TEAL, 15)
    zigzag_band(zz, panel[0] - 2, panel[2] + 2, panel[3] + 4, 9, GOLD, TEAL, 15)
    c.paste(zz)
    for x in (panel[0] - 8, panel[2] + 8):
        dotted_line(c, [(x, panel[1] + 6), (x, panel[3] - 6)], A(GOLD), 2.4, 11)
    pm = c.layer()
    pm.rrect(panel, 16, fill=(0, 0, 0, 255))
    c.gradient_fill(pm, FELT_TOP, FELT_BOTTOM, panel[1], panel[3])
    felt = c.layer()
    felt.ellipse((panel[0] + 30, panel[1] + 140, panel[2] - 30, panel[3] - 80), fill=A(hexc("2E8A6E"), 110))
    c.paste(felt, blur=55)
    for (cx_, cy_, a0) in [(panel[0], panel[1], 0), (panel[2], panel[1], 90), (panel[2], panel[3], 180), (panel[0], panel[3], 270)]:
        for k, (r, col) in enumerate([(54, ORANGE), (42, GOLD), (28, RED)]):
            c.d.pieslice(c.box((cx_ - r, cy_ - r, cx_ + r, cy_ + r)), a0, a0 + 90, fill=A(col))
        for i in range(5):
            a = math.radians(a0 + 9 + i * 18)
            c.circle(cx_ + math.cos(a) * 48, cy_ + math.sin(a) * 48, 2.6, fill=A(CREAM))
    c.rrect(panel, 16, outline=A(GOLD), width=4)
    c.rrect((panel[0] + 8, panel[1] + 8, panel[2] - 8, panel[3] - 8), 12, outline=A(hexc("0E3A30"), 200), width=2)
    # The border filled with sprigs, then roses over them, mirrored left and right.
    mid_x = (outer[0] + panel[0]) / 2 - 2
    for side in (0, 1):
        def mx(v):
            return v if side == 0 else w - v
        for y in range(int(outer[1]) + 50, int(outer[3]) - 30, 30):
            up = (y // 30) % 2
            leaf(c, mx(mid_x), y, 42, (250 if up else 110) if side == 0 else (290 if up else 70), GREEN if up else hexc("5CB040"))
            leaf(c, mx(mid_x + 4), y + 6, 34, (20 if up else -30) if side == 0 else (160 if up else 210), GREEN_DARK)
        for y, r, col in [(top + 62, 36, ROSE), (top + 190, 20, ORANGE), (top + 290, 28, PINK), (top + 420, 20, hexc("F5D040")),
                          (top + 530, 30, ROSE), (top + 650, 20, ORANGE), (h - 62, 36, ROSE)]:
            petal_flower(c, mx(mid_x), y, r, col, rot=rng.random(), petals=6 if r > 20 else 5, centre=GOLD if r > 20 else GOLD_LIGHT)
    for x in range(int(panel[0]) + 20, int(panel[2]) - 6, 52):
        for y in (outer[1] + 44, outer[3] - 40):
            up = (x // 58) % 2
            leaf(c, x - 10, y, 26, 180 + (20 if up else -20), GREEN)
            leaf(c, x + 10, y, 26, (20 if up else -20), hexc("5CB040"))
            petal_flower(c, x, y, 11, hexc("F5D040") if up else ORANGE, petals=5, centre=GOLD_LIGHT, rot=rng.random())
    # Crest flowers.
    for ang in (150, 205, 30, -25, 250, 290):
        leaf(c, w / 2, top - 40, 58, ang, GREEN if ang % 3 else hexc("5CB040"))
    for x, y, col in [(240, top - 6, hexc("F5D040")), (w - 240, top - 6, hexc("F5D040")), (300, top - 40, PINK), (w - 300, top - 40, PINK)]:
        leaf(c, x, y, 30, 200 if x < w / 2 else -20, GREEN)
        petal_flower(c, x, y, 15, col, petals=5, centre=GOLD_LIGHT)
    petal_flower(c, w / 2, top - 40, 40, ROSE, rot=0.3)
    # Brass knobs on the top corners.
    for x in (46, w - 46):
        c.circle(x, top + 4, 17, fill=A(GOLD_DARK), outline=A(INK), width=3)
        c.circle(x - 3, top + 1, 9, fill=A(GOLD_LIGHT))
    # The peacocks, perched on the panel's sides.
    pc = c.layer()
    peacock(pc, panel[0] + 8, 372, 0.92)
    peacock(pc, panel[2] - 8, 372, 0.92, flip=True)
    c.paste(pc)
    return c.out()


def medallion(colour: RGB) -> Image.Image:
    """A seat-count chip: a coloured disc in a gold ring; the number is a label on top."""
    w = 110
    c = Canvas(w, w)
    c.circle(w / 2, w / 2 + 4, 50, fill=A(INK, 180))
    c.circle(w / 2, w / 2, 50, fill=A(INK))
    m = c.layer()
    m.circle(w / 2, w / 2, 47, fill=(0, 0, 0, 255))
    c.gradient_fill(m, GOLD_LIGHT, GOLD_DARK, 8, w - 8)
    m2 = c.layer()
    m2.circle(w / 2, w / 2, 35, fill=(0, 0, 0, 255))
    c.gradient_fill(m2, lerp(colour, (255, 255, 255), 0.25), lerp(colour, (0, 0, 0), 0.35), 20, w - 20)
    c.circle(w / 2, w / 2, 35, outline=A(lerp(GOLD_DARK, INK, 0.5)), width=2.5)
    c.ellipse((w / 2 - 24, w / 2 - 30, w / 2 + 24, w / 2 - 8), fill=(255, 255, 255, 60))
    for i in range(12):
        a = i * math.pi / 6
        c.circle(w / 2 + math.cos(a) * 41, w / 2 + math.sin(a) * 41, 2.2, fill=A(hexc("FFF4C0"), 200))
    return c.out()


def switch_track() -> Image.Image:
    w, h = 150, 74
    c = Canvas(w, h)
    c.rrect((2, 5, w - 2, h - 1), 34, fill=A(INK, 200))
    m = c.layer()
    m.rrect((2, 2, w - 2, h - 4), 34, fill=(0, 0, 0, 255))
    c.gradient_fill(m, hexc("C89A60"), hexc("8A5A2E"), 2, h - 4)
    c.rrect((2, 2, w - 2, h - 4), 34, outline=A(INK), width=3)
    c.rrect((10, 10, w - 10, h - 12), 26, fill=A(hexc("5A3418")))
    c.rrect((12, 12, w - 12, 20), 4, fill=(0, 0, 0, 70))
    return c.out()


def switch_track_on() -> Image.Image:
    w, h = 150, 74
    c = Canvas(w, h)
    c.rrect((2, 5, w - 2, h - 1), 34, fill=A(INK, 200))
    mo = c.layer()
    mo.rrect((2, 2, w - 2, h - 4), 34, fill=(0, 0, 0, 255))
    c.gradient_fill(mo, hexc("C89A60"), hexc("8A5A2E"), 2, h - 4)
    c.rrect((2, 2, w - 2, h - 4), 34, outline=A(INK), width=3)
    m = c.layer()
    m.rrect((10, 10, w - 10, h - 12), 26, fill=(0, 0, 0, 255))
    c.gradient_fill(m, hexc("E05A3A"), RED_DARK, 10, h - 12)
    return c.out()


def switch_knob() -> Image.Image:
    w = 64
    c = Canvas(w, w)
    c.circle(w / 2, w / 2 + 3, 28, fill=A(INK, 160))
    m = c.layer()
    m.circle(w / 2, w / 2, 27, fill=(0, 0, 0, 255))
    c.gradient_fill(m, hexc("FFFDF4"), hexc("CFC2A6"), 5, w - 5)
    c.circle(w / 2, w / 2, 27, outline=A(hexc("6A5A40")), width=2.5)
    c.ellipse((w / 2 - 15, w / 2 - 19, w / 2 + 11, w / 2 - 4), fill=(255, 255, 255, 170))
    return c.out()


# ---------------------------------------------------------------- buttons


def play_plaque() -> Image.Image:
    """The Play now plaque: a truck-art mount with pointed sides around a big glossy gold button."""
    w, h = 560, 210
    c = Canvas(w, h)
    cy = h / 2
    mount = [(8, cy), (46, 26), (w - 46, 26), (w - 8, cy), (w - 46, h - 18), (46, h - 18)]
    sh = c.layer()
    sh.poly([(x, y + 12) for x, y in mount], fill=A(INK, 200))
    c.paste(sh, blur=6)
    c.poly(mount, fill=A(RED_DARK))
    inner = [(22, cy), (54, 36), (w - 54, 36), (w - 22, cy), (w - 54, h - 28), (54, h - 28)]
    c.poly(inner, fill=A(RED))
    c.poly(mount, outline=A(INK), width=4)
    zz = c.layer()
    zigzag_band(zz, 60, w - 60, 38, 9, GOLD, TEAL, 16)
    zigzag_band(zz, 60, w - 60, h - 39, 9, GOLD, TEAL, 16)
    c.paste(zz)
    for x in (34, w - 34):
        petal_flower(c, x, cy, 13, hexc("F5D040"), petals=5)
    # The gold button.
    btn = (66, 44, w - 66, h - 38)
    c.rrect((btn[0], btn[1] + 9, btn[2], btn[3] + 9), 34, fill=A(hexc("8A4A0A")))
    m = c.layer()
    m.rrect(btn, 34, fill=(0, 0, 0, 255))
    c.gradient_fill(m, hexc("FFE680"), hexc("F0A020"), btn[1], btn[3])
    c.rrect(btn, 34, outline=A(hexc("7A3E08")), width=4)
    gloss = c.layer()
    gloss.rrect((btn[0] + 14, btn[1] + 8, btn[2] - 14, btn[1] + (btn[3] - btn[1]) * 0.42), 22, fill=(255, 255, 255, 120))
    c.paste(gloss, blur=1)
    c.rrect((btn[0] + 6, btn[1] + 6, btn[2] - 6, btn[3] - 6), 28, outline=A(hexc("FFF4B0"), 140), width=2)
    return c.out()


def plank_button(trim: tuple[RGB, RGB]) -> Image.Image:
    """A wooden board with painted truck-art corners; nine-sliced, so the label decides the width."""
    w, h = 420, 124
    c = Canvas(w, h)
    rng = random.Random(trim[0][0] + trim[1][2])
    b = (6, 8, w - 6, h - 16)
    plank_shape(c, b, 18, WOOD_LIGHT, hexc("6A3C1C"), rng, INK, 4)
    inner = (b[0] + 12, b[1] + 11, b[2] - 12, b[3] - 11)
    c.rrect(inner, 12, outline=A(trim[0]), width=3)
    c.rrect((inner[0] + 5, inner[1] + 5, inner[2] - 5, inner[3] - 5), 9, outline=A(trim[1], 200), width=2)
    for (x, y, sx, sy) in [(inner[0], inner[1], 1, 1), (inner[2], inner[1], -1, 1), (inner[0], inner[3], 1, -1), (inner[2], inner[3], -1, -1)]:
        c.poly([(x, y), (x + 34 * sx, y), (x, y + 24 * sy)], fill=A(trim[0]))
        c.poly([(x + 6 * sx, y + 4 * sy), (x + 22 * sx, y + 4 * sy), (x + 6 * sx, y + 14 * sy)], fill=A(trim[1]))
    return c.out()


def chest() -> Image.Image:
    """The daily reward: a painted chest with a domed lid, gold bands and a glow, with sparkles."""
    w, h = 330, 270
    c = Canvas(w, h)
    g = c.layer()
    g.ellipse((40, 10, 290, 200), fill=A(hexc("FFD860"), 160))
    c.paste(g, blur=30)
    bx0, bx1, by0, by1 = 50, 280, 132, 240
    sh = c.layer()
    sh.ellipse((36, 228, 294, 262), fill=A(INK, 180))
    c.paste(sh, blur=6)
    # Body.
    m = c.layer()
    m.rrect((bx0, by0, bx1, by1), 12, fill=(0, 0, 0, 255))
    c.gradient_fill(m, hexc("DA3E2C"), hexc("8E1C16"), by0, by1)
    zz = c.layer()
    zigzag_band(zz, bx0 + 34, bx1 - 34, by1 - 26, 10, GOLD, TEAL, 16)
    c.paste(zz)
    petal_flower(c, bx0 + 72, by0 + 52, 16, hexc("2E9AD0"), petals=6)
    petal_flower(c, bx1 - 72, by0 + 52, 16, hexc("2E9AD0"), petals=6)
    c.rrect((bx0, by0, bx1, by1), 12, outline=A(INK), width=4)
    # Lid: a dome.
    lid_pts = [(bx0 - 4, by0 + 4)]
    for i in range(33):
        t = i / 32
        lid_pts.append((bx0 - 4 + (bx1 - bx0 + 8) * t, by0 + 4 - math.sin(math.pi * t) ** 0.55 * 92))
    lid_pts.append((bx1 + 4, by0 + 4))
    lm = c.layer()
    lm.poly(lid_pts, fill=(0, 0, 0, 255))
    c.gradient_fill(lm, hexc("F05A40"), hexc("A82820"), by0 - 92, by0)
    petal_flower(c, (bx0 + bx1) / 2, by0 - 50, 20, hexc("F5D040"), petals=8, centre=ORANGE)
    for ang in (180, 0, 200, -20):
        leaf(c, (bx0 + bx1) / 2, by0 - 50, 46, ang, GREEN)
    petal_flower(c, (bx0 + bx1) / 2, by0 - 50, 20, hexc("F5D040"), petals=8, centre=ORANGE)
    c.poly(lid_pts, outline=A(INK), width=4)
    # Gold bands over lid and body, studs.
    for x in (bx0 + 26, bx1 - 26):
        top_y = by0 + 4 - math.sin(math.pi * (x - bx0 + 4) / (bx1 - bx0 + 8)) ** 0.55 * 92
        c.rrect((x - 12, top_y + 2, x + 12, by1), 4, fill=A(GOLD), outline=A(GOLD_DARK), width=2)
        for y in range(int(top_y) + 14, by1 - 6, 22):
            c.circle(x, y, 3.2, fill=A(GOLD_LIGHT), outline=A(GOLD_DARK), width=1)
    c.rrect((bx0 - 8, by0 - 6, bx1 + 8, by0 + 10), 5, fill=A(GOLD), outline=A(INK), width=3)
    # Lock across the seam.
    lx, ly = (bx0 + bx1) / 2, by0 + 14
    c.rrect((lx - 24, ly - 26, lx + 24, ly + 28), 9, fill=A(GOLD), outline=A(INK), width=3)
    c.rrect((lx - 18, ly - 20, lx + 18, ly - 14), 2, fill=A(GOLD_LIGHT))
    c.circle(lx, ly + 2, 6.5, fill=A(INK))
    c.poly([(lx - 3.5, ly + 4), (lx + 3.5, ly + 4), (lx + 5.5, ly + 17), (lx - 5.5, ly + 17)], fill=A(INK))
    # Light leaking from the seam.
    lk = c.layer()
    lk.rrect((bx0 + 10, by0 - 4, bx1 - 10, by0 + 6), 4, fill=A(hexc("FFF0A0"), 220))
    c.paste(lk, blur=5)
    for (x, y, r) in [(24, 76, 15), (306, 44, 19), (300, 160, 11), (170, 6, 10), (36, 186, 9), (250, 18, 8)]:
        c.poly([(x, y - r), (x + r * 0.2, y - r * 0.2), (x + r, y), (x + r * 0.2, y + r * 0.2), (x, y + r), (x - r * 0.2, y + r * 0.2), (x - r, y), (x - r * 0.2, y - r * 0.2)], fill=(255, 255, 240, 255))
    return c.out()


# ---------------------------------------------------------------- navigation


def nav_board() -> Image.Image:
    """The navigation: a wooden board with a teal and red truck-art top border; nine-sliced across."""
    w, h = 540, 200
    c = Canvas(w, h)
    rng = random.Random(5)
    top = 14
    sh = c.layer()
    sh.rrect((0, top - 8, w, h), 40, fill=A(INK, 170))
    c.paste(sh, blur=8)
    c.rrect((0, top, w, h + 40), 40, fill=A(TEAL_DARK), outline=A(INK), width=4)
    c.rrect((6, top + 6, w - 6, h + 40), 34, fill=A(TEAL))
    zz = c.layer()
    zigzag_band(zz, 30, w - 30, top + 9, 9, GOLD, RED, 16)
    c.paste(zz)
    for x in range(48, w - 40, 64):
        petal_flower(c, x, top + 30, 7, ROSE if (x // 64) % 2 else hexc("F5D040"), petals=5)
    b = (18, top + 44, w - 18, h + 30)
    m = c.layer()
    m.rrect(b, 24, fill=(0, 0, 0, 255))
    c.gradient_fill(m, WOOD_LIGHT, hexc("5A3218"), b[1], h)
    wood_grain(c, (b[0] + 10, b[1] + 6, b[2] - 10, h - 6), rng, WOOD_DARK, 80, 8)
    c.rrect(b, 24, outline=A(INK), width=3)
    c.rrect((b[0] + 4, b[1] + 4, b[2] - 4, b[1] + 7), 2, fill=A(WOOD_HI, 120))
    return c.out()


def icon_home() -> Image.Image:
    w = 150
    c = Canvas(w, w)
    c.poly([(30, 70), (75, 36), (120, 70), (120, 132), (30, 132)], fill=A(INK))
    m = c.layer()
    m.rrect((38, 70, 112, 128), 6, fill=(0, 0, 0, 255))
    c.gradient_fill(m, hexc("FFD45A"), hexc("F0A030"), 70, 128)
    c.rrect((62, 92, 88, 128), 8, fill=A(hexc("7A3E14")), outline=A(INK), width=3)
    c.circle(82, 112, 2.5, fill=A(GOLD_LIGHT))
    roof = [(16, 78), (75, 22), (134, 78), (122, 90), (75, 46), (28, 90)]
    rm = c.layer()
    rm.poly(roof, fill=(0, 0, 0, 255))
    c.gradient_fill(rm, hexc("F06A4A"), hexc("C02A20"), 22, 90)
    c.poly(roof, outline=A(INK), width=4)
    c.rrect((98, 30, 114, 62), 2, fill=A(hexc("C02A20")), outline=A(INK), width=3)
    c.rrect((38, 70, 112, 128), 6, outline=A(INK), width=4)
    return c.out()


def person(c: Canvas, cx, cy, s, top: RGB, bottom: RGB):
    m = c.layer()
    m.ellipse((cx - 30 * s, cy + 8 * s, cx + 30 * s, cy + 70 * s), fill=(0, 0, 0, 255))
    c.ellipse((cx - 30 * s, cy + 8 * s, cx + 30 * s, cy + 70 * s), fill=A(INK))
    m2 = c.layer()
    m2.ellipse((cx - 27 * s, cy + 11 * s, cx + 27 * s, cy + 67 * s), fill=(0, 0, 0, 255))
    c.gradient_fill(m2, top, bottom, cy + 11 * s, cy + 50 * s)
    c.circle(cx, cy - 6 * s, 19 * s, fill=A(INK))
    m3 = c.layer()
    m3.circle(cx, cy - 6 * s, 16 * s, fill=(0, 0, 0, 255))
    c.gradient_fill(m3, lerp(top, (255, 255, 255), 0.25), bottom, cy - 22 * s, cy + 10 * s)


def icon_friends() -> Image.Image:
    w = 150
    c = Canvas(w, w)
    person(c, 40, 52, 0.85, hexc("4AB0F0"), hexc("1E6EC0"))
    person(c, 110, 52, 0.85, hexc("7AD06A"), hexc("2E8A3A"))
    person(c, 75, 50, 1.05, hexc("FF7AC0"), hexc("D02A8A"))
    mask = Image.new("L", (w * K, w * K), 0)
    ImageDraw.Draw(mask).rectangle([0, 0, w * K, 128 * K], fill=255)
    c.img.putalpha(Image.composite(c.img.getchannel("A"), Image.new("L", mask.size, 0), mask))
    c.d = ImageDraw.Draw(c.img, "RGBA")
    c.rrect((10, 124, 140, 130), 3, fill=A(INK))
    return c.out()


def icon_bag() -> Image.Image:
    w = 150
    c = Canvas(w, w)
    c.rrect((52, 22, 98, 50), 12, outline=A(INK), width=11)
    c.rrect((52, 22, 98, 50), 12, outline=A(hexc("8A4A1A")), width=5)
    m = c.layer()
    m.rrect((18, 42, 132, 132), 18, fill=(0, 0, 0, 255))
    c.gradient_fill(m, hexc("D07A3A"), hexc("8A4214"), 42, 132)
    c.rrect((18, 42, 132, 132), 18, outline=A(INK), width=4)
    c.rrect((22, 46, 128, 82), 14, fill=A(hexc("E89050")))
    c.line([(20, 82), (130, 82)], fill=A(INK), width=3)
    c.rrect((62, 72, 88, 98), 5, fill=A(GOLD), outline=A(INK), width=3)
    c.circle(75, 85, 3.5, fill=A(INK))
    for x in (32, 118):
        c.rrect((x - 5, 60, x + 5, 126), 3, fill=A(hexc("7A3410")))
    return c.out()


def icon_gear() -> Image.Image:
    w = 150
    c = Canvas(w, w)
    cx = cy = 75
    teeth = []
    for i in range(48):
        a = i * 2 * math.pi / 48
        k = (i // 3) % 2
        r = 60 if k == 0 else 48
        teeth.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    c.poly([(x, y + 3) for x, y in teeth], fill=A(INK))
    m = c.layer()
    m.poly(teeth, fill=(0, 0, 0, 255))
    c.gradient_fill(m, hexc("5AB0FF"), hexc("1A5AC8"), 15, 135)
    c.poly(teeth, outline=A(INK), width=4)
    c.circle(cx, cy, 34, outline=A(hexc("0E3A8A")), width=4)
    c.circle(cx, cy, 20, fill=A(hexc("2A1A3A")), outline=A(INK), width=4)
    c.ellipse((cx - 40, cy - 52, cx + 10, cy - 30), fill=(255, 255, 255, 80))
    return c.out()


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    pieces = {
        "menu": backdrop(),
        "rope": rope(),
        "sign": sign(),
        "shield": shield(),
        "pouch": pouch(),
        "hero-frame": hero_frame(),
        "medal-blue": medallion(hexc("2A72C8")),
        "medal-green": medallion(hexc("4A8A2A")),
        "medal-red": medallion(hexc("C8302A")),
        "medal-purple": medallion(hexc("8A3AB8")),
        "switch": switch_track(),
        "switch-on": switch_track_on(),
        "knob": switch_knob(),
        "play": play_plaque(),
        "plank-red": plank_button((hexc("D8402E"), hexc("2AA8A0"))),
        "plank-blue": plank_button((hexc("2A82D0"), hexc("F2C040"))),
        "chest": chest(),
        "nav-board": nav_board(),
        "icon-home": icon_home(),
        "icon-friends": icon_friends(),
        "icon-bag": icon_bag(),
        "icon-gear": icon_gear(),
    }
    for name, img in pieces.items():
        img.save(OUT / f"{name}.png", optimize=True)
        print(name, img.size)


if __name__ == "__main__":
    main()
