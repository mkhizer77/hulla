"""Imports the Rooftop redesign sprites (the owner's art pack) into the Unity project.

Reads the pack's per-screen sprite folders, keeps one copy of each file, sorts them into the
folders of the integration guide under Presentation/Theme/Art/Skins/Rooftop/, and makes the few
pieces the pack does not ship ready to use:

- toggle_track_blank.png and toggle_knob.png: the Casual/Ranked track has its words and star knob
  painted in; the words are painted out (labels carry them, so they translate) and the knob is cut
  out so it can slide.
- chip_seat_3_normal/pressed.png: the pack has coins for 4, 5 and 6 seats; tables of 3 exist, so a
  violet coin is made from the 6 coin with its numeral painted out and drawn as a label.
- btn_primary_pressed.png is a plain gold bar without the floral plinth, so it is used as the
  general, stretchable primary button; the plinth button presses by moving down.
- Highlights the extraction keyed out as see-through (the gloss on the primary buttons, the card
  back's cream ground, the bulb, coins, the silver ribbon) are filled. Sprites whose openings are
  meant (the avatar frame's aperture, the timer ring, ink-only stamps, the gear, rope loops, the
  case handle) keep them.
- Sprites that grow with their content get a nine-slice version (<name>_9.png): cropped to the
  art, the corners as painted (every rivet, rope and flower inside them), and the edges and centre
  rebuilt from a clean run of the original that wraps back into itself, so the stylesheet tiles them
  (-unity-slice-type: tiled) instead of stretching the grain. NINE lists each one's insets.
- Pale halos and light drop shadows from the extraction's light background turn into a soft dark
  shadow (a warm glow on the timer ring), so nothing glows white on the night scene.

Run from the repository root with the pack's sprites folder:

    python3 tools/artkit/rooftop_import.py ../hulla/art-design/sprites
"""

from __future__ import annotations

import colorsys
import hashlib
import re
import uuid
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "apps/unity/Hulla/Assets/_Project/Presentation/Theme/Art/Skins/Rooftop"

FOLDERS = {
    "Frames": ["frame_truckart_arch", "signboard_header_blank", "nameplate_wood_blank", "banner_quickchat_blank",
               "panel_parchment_dialog_blank", "badge_level_shield_blank"],
    "Buttons": ["btn_primary_normal", "btn_primary_pressed", "btn_plank_large_normal", "btn_plank_large_pressed",
                "btn_plank_medium_normal", "btn_plank_medium_pressed", "btn_chip_pill_normal", "btn_chip_pill_pressed"],
    "Chips": ["chip_seat_4_normal", "chip_seat_4_pressed", "chip_seat_5_normal", "chip_seat_5_pressed",
              "chip_seat_6_normal", "chip_seat_6_pressed"],
    "Avatars": ["avatar_frame_brass", "avatar_timer_ring", "badge_leader_crown"],
    "Cards": ["card_back_truckart", "card_fan_deck_badge"],
    "FX": ["stamp_thulla", "stamp_bhabhi", "fx_bulb_glow"],
    "Props": ["potli_currency_bag", "chest_daily_reward_closed", "chest_daily_reward_open", "podium_3tier_marigold",
              "ribbon_1st_gold", "ribbon_2nd_silver", "ribbon_3rd_bronze"],
    "Nav": ["nav_dock_baseboard", "icon_nav_home", "icon_nav_friends", "icon_nav_inventory", "icon_nav_settings"],
}


# Sprites whose enclosed see-through areas are part of the design.
OPEN_BY_DESIGN = {"avatar_frame_brass", "avatar_timer_ring", "stamp_thulla", "stamp_bhabhi", "icon_nav_settings",
                  "signboard_header_blank", "icon_nav_inventory", "badge_leader_crown"}


# Pale edges that are paint, not extraction halo.
PALE_BY_DESIGN = {"card_back_truckart", "card_fan_deck_badge", "panel_parchment_dialog_blank", "ribbon_2nd_silver",
                  "stamp_thulla", "stamp_bhabhi"}


def defringe(img: Image.Image, glow: tuple[int, int, int] | None = None, reach: int = 11) -> Image.Image:
    """Pale, unsaturated pixels near the outline become shadow (or the given glow colour)."""
    img = img.convert("RGBA")
    edge = img.getchannel("A").point(lambda v: 255 if v < 20 else 0).filter(ImageFilter.MaxFilter(reach))
    px, ep = img.load(), edge.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a < 8 or not ep[x, y]:
                continue
            _, sat, val = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if sat < 0.12 and val > 0.55:
                if glow:
                    px[x, y] = glow + (int(a * 0.8),)
                else:
                    px[x, y] = (12, 6, 2, int(a * 0.5))
    return img


def find(pack: Path) -> dict[str, Path]:
    """One path per sprite name; every copy of a name must be the same file."""
    found: dict[str, Path] = {}
    digests: dict[str, str] = {}
    for path in sorted(pack.rglob("*.png")):
        if path.name.startswith("sheet_") or path.name == "reference_screen.png" or "avatar_pod" in path.parts:
            continue
        name = path.stem
        digest = hashlib.md5(path.read_bytes()).hexdigest()
        if name in digests and digests[name] != digest:
            raise SystemExit(f"two different files named {name}")
        digests[name] = digest
        found.setdefault(name, path)
    return found


def toggle_parts(track: Image.Image) -> tuple[Image.Image, Image.Image]:
    """The track without its words or knob, and the knob on its own canvas."""
    img = track.convert("RGBA")
    w, h = img.size
    cx, cy, r = w // 2, 76, 50
    knob = Image.new("RGBA", (2 * r + 8, 2 * r + 8), (0, 0, 0, 0))
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).ellipse((cx - r, cy - r, cx + r, cy + r), fill=255)
    cut = Image.new("RGBA", img.size, (0, 0, 0, 0))
    cut.paste(img, (0, 0), mask)
    knob.alpha_composite(cut.crop((cx - r - 4, cy - r - 4, cx + r + 4, cy + r + 4)))
    blank = img.copy()
    bp, ip = blank.load(), img.load()
    # Between the rounded ends the rim runs straight; rebuild it from one clean column left of the knob.
    for x in range(120, w - 120):
        for y in range(h):
            bp[x, y] = ip[130, y]
    # The groove: each row takes the colour of the clean groove just inside the left end, so the
    # words go and the groove keeps its shading.
    for y in range(40, 116):
        sample = sorted((ip[x, y] for x in range(44, 52)), key=lambda c: c[0] + c[1] + c[2])
        colour = sample[len(sample) // 2]
        for x in range(52, w - 44):
            if bp[x, y][3] > 0:
                bp[x, y] = colour[:3] + (bp[x, y][3],)
    return blank, knob


def coin() -> Image.Image:
    """A gold coin for the daily reward's flight to the potli, in the potli's own gold."""
    k = 4
    size = 72
    img = Image.new("RGBA", (size * k, size * k), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = size * k / 2
    d.ellipse((4 * k, 6 * k, (size - 4) * k, (size - 2) * k), fill=(140, 93, 20, 255))
    d.ellipse((4 * k, 4 * k, (size - 4) * k, (size - 4) * k), fill=(212, 155, 53, 255))
    d.ellipse((9 * k, 9 * k, (size - 9) * k, (size - 9) * k), fill=(249, 168, 37, 255))
    d.ellipse((13 * k, 13 * k, (size - 13) * k, (size - 13) * k), outline=(193, 121, 0, 255), width=3 * k)
    star = []
    for i in range(10):
        a = math.radians(-90 + i * 36)
        r = (14 if i % 2 == 0 else 6) * k
        star.append((c + math.cos(a) * r, c + math.sin(a) * r))
    d.polygon(star, fill=(255, 224, 130, 255))
    d.ellipse((14 * k, 11 * k, 30 * k, 19 * k), fill=(255, 241, 118, 170))
    return img.resize((size, size), Image.LANCZOS)


def switch_states(track: Image.Image, knob: Image.Image) -> None:
    """Settings toggles in one image each: the knob at the left (off) or the right on a warm groove (on)."""
    w, h = track.size
    k = knob.resize((h - 20, h - 20), Image.LANCZOS)
    off = track.copy()
    off.alpha_composite(k, (18, 6))
    off.save(OUT / "Buttons" / "switch_off.png", optimize=True)
    on = track.copy()
    warm = Image.new("RGBA", on.size, (249, 168, 37, 0))
    groove = Image.new("L", on.size, 0)
    ImageDraw.Draw(groove).rounded_rectangle((44, 40, w - 44, 116), radius=36, fill=110)
    warm.putalpha(groove)
    on.alpha_composite(warm)
    on.alpha_composite(k, (w - k.width - 18, 6))
    on.save(OUT / "Buttons" / "switch_on.png", optimize=True)


def blank_coin(coin: Image.Image, hue_shift: float) -> Image.Image:
    """A seat coin without its numeral, its enamel turned to another hue."""
    img = coin.convert("RGBA")
    px = img.load()
    w, h = img.size
    cx, cy = w / 2, h / 2 - 2
    inner = 50
    edge = [px[int(cx + math.cos(math.radians(a)) * (inner + 4)), int(cy + math.sin(math.radians(a)) * (inner + 4))] for a in range(0, 360, 6)]
    base = tuple(sorted(c[i] for c in edge)[len(edge) // 2] for i in range(3))
    for y in range(h):
        for x in range(w):
            d = math.hypot(x - cx, y - cy)
            if d <= inner:
                t = d / inner
                lift = 1.25 - 0.35 * t + 0.25 * max(0.0, (cy - y) / inner)
                px[x, y] = (min(255, int(base[0] * lift)), min(255, int(base[1] * lift)), min(255, int(base[2] * lift)), 255)
    gloss = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(gloss).ellipse((cx - 32, cy - 42, cx + 32, cy - 14), fill=(255, 255, 255, 60))
    img.alpha_composite(gloss.filter(ImageFilter.GaussianBlur(4)))
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            hh, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            # Only the enamel (reds) turns; the gold rim keeps its hue.
            if (hh < 0.06 or hh > 0.9) and s > 0.35:
                hh = (hh + hue_shift) % 1.0
                rr, gg, bb = colorsys.hsv_to_rgb(hh, s, v)
                px[x, y] = (int(rr * 255), int(gg * 255), int(bb * 255), a)
    return img


def fill_pinholes(img: Image.Image) -> Image.Image:
    """Fills see-through holes inside the body (keyed-out highlights) from the rows around them."""
    img = img.convert("RGBA")
    w, h = img.size
    mask = img.getchannel("A").point(lambda v: 255 if v > 128 else 0).convert("L")
    outside = mask.copy()
    ImageDraw.floodfill(outside, (0, 0), 128)
    px, op = img.load(), outside.load()
    holes = [(x, y) for y in range(h) for x in range(w) if op[x, y] == 0]
    for x, y in holes:
        up = next((px[x, yy] for yy in range(y, -1, -1) if op[x, yy] == 255), None)
        down = next((px[x, yy] for yy in range(y, h) if op[x, yy] == 255), None)
        pick = [c for c in (up, down) if c]
        if pick:
            px[x, y] = tuple(sum(c[i] for c in pick) // len(pick) for i in range(3)) + (255,)
    # Edge pixels of the hole were half see-through; make them solid.
    for x, y in holes:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                xx, yy = x + dx, y + dy
                if 0 <= xx < w and 0 <= yy < h and op[xx, yy] == 255 and px[xx, yy][3] < 255:
                    px[xx, yy] = px[xx, yy][:3] + (255,)
    return img


TEMPLATE = OUT.parent / "dhaba" / "menu.png.meta"


def import_settings(png: Path) -> None:
    """The guide's texture settings: no mipmaps, clamped, alpha as transparency; keeps an existing GUID."""
    meta = png.with_name(png.name + ".meta")
    text = meta.read_text() if meta.exists() else re.sub(r"guid: \w+", f"guid: {uuid.uuid4().hex}", TEMPLATE.read_text(), count=1)
    for key, value in (("enableMipMap", "0"), ("wrapU", "1"), ("wrapV", "1"), ("wrapW", "1"), ("alphaIsTransparency", "1"),
                       ("spriteExtrude", "1"), ("filterMode", "1")):
        text = re.sub(rf"(\n\s*{key}: )-?\w+", rf"\g<1>{value}", text)
    meta.write_text(text)


# Nine-slice versions, measured on the art cropped to its content: (L, T, R, B) are the corners and
# edges kept as painted; hx and vy are the clean column and row runs the tiles are built from.
NINE = {
    "btn_plank_large_normal": dict(folder="Buttons", hx=(145, 348), vy=(98, 128), L=140, R=140, rim=16),
    "btn_plank_large_pressed": dict(folder="Buttons", hx=(145, 348), vy=(98, 128), L=140, R=140, rim=16),
    "btn_plank_medium_normal": dict(folder="Buttons", hx=(105, 172), vy=(82, 112), L=105, R=105, rim=14),
    "btn_plank_medium_pressed": dict(folder="Buttons", hx=(105, 172), vy=(82, 112), L=105, R=105, rim=14),
    "btn_chip_pill_normal": dict(folder="Buttons", hx=(52, 116), vy=(44, 52), L=52, R=52),
    "btn_chip_pill_pressed": dict(folder="Buttons", hx=(52, 116), vy=(44, 52), L=52, R=52),
    "nameplate_wood_blank": dict(folder="Frames", hx=(50, 259), vy=(46, 56), L=50, R=50),
    "panel_parchment_dialog_blank": dict(folder="Frames", hx=(70, 316), vy=(80, 100), L=70, R=70),
    "nav_dock_baseboard": dict(folder="Nav", hx=(70, 487), vy=(55, 85), L=70, R=70),
    "btn_primary_pressed": dict(folder="Buttons", hx=(70, 382), vy=(90, 96), L=70, R=70),
    "signboard_header_blank": dict(folder="Frames", hx=(182, 290), vy=(112, 128), L=182, R=186),
}


def wrap(patch: Image.Image, axis: int) -> Image.Image:
    """
    A tile that repeats without a seam, from a clean run of the art only: the run's tail fades into its
    own head over a quarter of its length, and that head is dropped, so the last pixel of one tile
    leads into the first of the next.
    """
    n = patch.size[axis]
    k = max(2, n // 4)
    size = (n - k, patch.height) if axis == 0 else (patch.width, n - k)
    out = Image.new("RGBA", size)

    def line(src: Image.Image, i: int) -> Image.Image:
        return src.crop((i, 0, i + 1, src.height)) if axis == 0 else src.crop((0, i, src.width, i + 1))

    for j in range(n - k):
        piece = line(patch, j + k)
        if j >= n - 2 * k:
            i = j - (n - 2 * k)
            piece = Image.blend(piece, line(patch, i), (i + 1) / k)
        out.paste(piece, (j, 0) if axis == 0 else (0, j))
    return out


def nine_ready(img: Image.Image, hx: tuple[int, int], vy: tuple[int, int], L: int, R: int, rim: int = 0) -> tuple[Image.Image, tuple[int, int, int, int]]:
    """
    Corners as painted, edges and centre as seamless tiles from the clean runs (see wrap); returns the
    image and (L, T, R, B). With rim, the side edges are the outer rim that wide plus plain wood from the
    centre, so a corner flourish that runs down the side is never repeated.
    """
    art = img.crop(img.getbbox())
    w, h = art.size
    x0, x1 = hx
    y0, y1 = vy
    T, B = y0, h - y1
    top = wrap(art.crop((x0, 0, x1, T)), 0)
    bottom = wrap(art.crop((x0, h - B, x1, h)), 0)
    centre = wrap(wrap(art.crop((x0, y0, x1, y1)), 0), 1)
    tw, th = centre.size
    if rim:
        left = Image.new("RGBA", (L, th))
        right = Image.new("RGBA", (R, th))
        for strip, width, edge, at in ((left, L, art.crop((0, y0, rim, y1)), 0), (right, R, art.crop((w - rim, y0, w, y1)), R - rim)):
            x = -tw
            while x < width:
                strip.paste(centre, (x, 0))
                x += tw
            strip.paste(wrap(edge, 1), (at, 0))
    else:
        left = wrap(art.crop((0, y0, L, y1)), 1)
        right = wrap(art.crop((w - R, y0, w, y1)), 1)
    out = Image.new("RGBA", (L + tw + R, T + th + B), (0, 0, 0, 0))
    out.paste(art.crop((0, 0, L, T)), (0, 0))
    out.paste(art.crop((w - R, 0, w, T)), (L + tw, 0))
    out.paste(art.crop((0, h - B, L, h)), (0, T + th))
    out.paste(art.crop((w - R, h - B, w, h)), (L + tw, T + th))
    out.paste(top, (L, 0))
    out.paste(bottom, (L, T + th))
    out.paste(left, (0, T))
    out.paste(right, (L + tw, T))
    out.paste(centre, (L, T))
    return out, (L, T, R, B)


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    pack = Path(sys.argv[1]).resolve()
    found = find(pack)
    for folder, names in FOLDERS.items():
        (OUT / folder).mkdir(parents=True, exist_ok=True)
        for name in names:
            if name not in found:
                raise SystemExit(f"missing from the pack: {name}")
            img = Image.open(found[name]).convert("RGBA")
            if name not in OPEN_BY_DESIGN:
                img = fill_pinholes(img)
            if name not in PALE_BY_DESIGN:
                img = defringe(img, (255, 200, 90) if name == "avatar_timer_ring" else None)
            img.save(OUT / folder / f"{name}.png", optimize=True)
    track = defringe(Image.open(found["toggle_mode_track"]), reach=25)
    blank, knob = toggle_parts(track)
    blank.save(OUT / "Buttons" / "toggle_track_blank.png", optimize=True)
    knob.save(OUT / "Buttons" / "toggle_knob.png", optimize=True)
    switch_states(blank, knob)
    coin().save(OUT / "Props" / "coin_gold.png", optimize=True)
    for state in ("normal", "pressed"):
        blank_coin(defringe(Image.open(found[f"chip_seat_6_{state}"])), 0.80).save(OUT / "Chips" / f"chip_seat_3_{state}.png", optimize=True)
    for name, cfg in NINE.items():
        source = Image.open(OUT / cfg["folder"] / f"{name}.png")
        tiled, insets = nine_ready(source, cfg["hx"], cfg["vy"], cfg["L"], cfg["R"], cfg.get("rim", 0))
        tiled.save(OUT / cfg["folder"] / f"{name}_9.png", optimize=True)
        print(f"{name}_9 insets L,T,R,B = {insets}")
    for p in sorted(OUT.rglob("*.png")):
        import_settings(p)
        print(p.relative_to(OUT), Image.open(p).size)


if __name__ == "__main__":
    main()
