"""Brings the Basant art into the Unity project (docs/design/basant-home.md).

The art is delivered as transparent PNGs, one per element: blank plates and their pressed states,
art without text, and words as separate text sprites per language. This script scales each one to
twice the size it is shown at on the 1080-wide design canvas, writes it under
Presentation/Theme/Art/Basant and gives it the import settings UI Toolkit needs (no mipmaps,
clamped, alpha as transparency, a stable GUID). Run from the repository root:

    python3 tools/artkit/basant_import.py <folder of delivered PNGs>
"""

from __future__ import annotations

import hashlib
import re
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / "apps/unity/Hulla/Assets/_Project/Presentation/Theme/Art/Basant"
TEMPLATE = ROOT / "apps/unity/Hulla/Assets/_Project/Presentation/Theme/Art/Skins/basant/btn-primary.png.meta"

# Display width on the 1080 canvas for each element (the file is written at twice this).
WIDTH = {
    "btn_settings": 122, "plate_profile": 560, "frame_avatar_gold": 136, "badge_level_star": 84,
    "plate_coins": 280, "icon_coin": 86, "panel_friends": 726, "frame_friend": 116, "dot_online": 32,
    "btn_invite": 118, "banner_table": 784, "btn_join_small": 138, "card_hero": 726, "kite_hero": 292,
    "cards_fan": 342, "panel_seat_size": 582, "medal_seat": 112, "ring_seat_selected": 122,
    "switch": 102, "btn_play": 476, "btn_create_room": 352, "btn_join_code": 356, "card_daily": 352,
    "icon_gift": 106, "card_soon": 356, "tab_tile": 254, "icon_nav": 112, "badge_count": 60,
    "title_thulla": 458, "title_rung_soon": 200,
}

# The shared kit (art-design/extraction/02_lobby_basant.md): blank plates, buttons and icons every
# screen after Home draws from, under Kit/; a screen's own piece goes under its folder.
KIT_WIDTH = {
    "kite_faint": 70, "btn_round": 124, "btn_square": 92, "btn_pill": 194, "btn_teal": 247, "icon": 64,
    "plate_name": 222, "tag": 120, "plate_label": 760, "panel_cream": 1012, "seat_empty": 165,
    "crown_host": 90, "tick_ready": 52, "bar_track": 700, "bar_fill": 700, "ring_turn_glow": 190,
    "segment_track": 1000, "segment_on": 480, "card_row": 1000, "card_spotlight": 1000, "medal_rank": 80,
    "medal_gold": 150, "medal_grey": 150, "tier_cell": 160, "icon_lock": 48, "knob_slider": 56, "bar_fill_green": 700,
    "btn_danger": 300, "btn_circle": 260, "logo_hulla": 700, "icon_kite_break": 160, "icon_kite_update": 160,
    "icon_coins_stack": 160,
}
SCREEN_WIDTH = {
    "Lobby": {"rug_table": 760},
    "Table": {"plate_turn": 540, "card_back": 110, "podium": 480, "stamp_bhabhi": 260},
}

# Display height on the 1080 canvas for each text sprite (written at twice this).
TEXT_HEIGHT = {
    "play_now": 52, "create_room": 40, "join_by_code": 40, "friends_online": 34, "invite": 26,
    "join": 30, "casual": 34, "ranked": 34, "seat_size": 30, "tagline_thulla": 42, "daily_reward": 30,
    "coming_soon": 26, "nav_home": 36, "nav_friends": 36, "nav_trophies": 36, "nav_shop": 36,
    "digit_3": 58, "digit_4": 58, "digit_5": 58, "digit_6": 58,
}


def width_for(stem: str, table: dict[str, int]) -> int | None:
    for key in sorted(table, key=len, reverse=True):
        if stem.startswith(key):
            return table[key]
    return None


def place(stem: str) -> tuple[str, int] | None:
    """The folder and display width for an element: a screen's own piece, else the kit, else Home."""
    for folder, table in SCREEN_WIDTH.items():
        if (width := width_for(stem, table)) is not None:
            return folder, width
    if (width := width_for(stem, KIT_WIDTH)) is not None:
        return "Kit", width
    if (width := width_for(stem, WIDTH)) is not None:
        return "Home", width
    return None


def guid(path: Path) -> str:
    return hashlib.md5(str(path.relative_to(ROOT)).encode()).hexdigest()


def import_settings(png: Path, max_size: int = 2048) -> None:
    """No mipmaps, clamped, alpha as transparency; keeps an existing GUID, else one from the path."""
    meta = png.with_name(png.name + ".meta")
    text = meta.read_text() if meta.exists() else re.sub(r"guid: \w+", f"guid: {guid(png)}", TEMPLATE.read_text(), count=1)
    for key, value in (("enableMipMap", "0"), ("wrapU", "1"), ("wrapV", "1"), ("wrapW", "1"),
                       ("alphaIsTransparency", "1"), ("filterMode", "1"), ("maxTextureSize", str(max_size)), ("nPOTScale", "0")):
        text = re.sub(rf"(\n\s*{key}: )-?\w+", rf"\g<1>{value}", text)
    meta.write_text(text)


def scaled(img: Image.Image, width: int | None = None, height: int | None = None) -> Image.Image:
    if width is not None:
        size = (width, round(img.height * width / img.width))
    else:
        size = (round(img.width * height / img.height), height)
    return img if size[0] >= img.width else img.resize(size, Image.LANCZOS)


def main(source: Path) -> None:
    for png in sorted(p for p in source.glob("*.png") if not p.stem.startswith("_")):
        stem = png.stem
        img = Image.open(png).convert("RGBA")
        text = re.match(r"txt_(.+?)(?:_([a-z]{2}))?$", stem)
        if text:
            # Digits carry no language suffix; they are the Latin set until a script needs its own.
            key, lang = text.group(1), text.group(2) or "en"
            if key not in TEXT_HEIGHT:
                print("skipped, no size:", stem)
                continue
            out = ART / "Text" / lang / f"{key.replace('_', '-')}.png"
            img = scaled(img, height=TEXT_HEIGHT[key] * 2)
        elif stem.startswith("bg_"):
            out = ART / "Backdrops" / f"{stem[3:].replace('_', '-')}.png"
        else:
            placed = place(stem)
            if placed is None:
                print("skipped, no size:", stem)
                continue
            folder, width = placed
            out = ART / folder / f"{stem.replace('_', '-')}.png"
            img = scaled(img, width=width * 2)
        out.parent.mkdir(parents=True, exist_ok=True)
        img.save(out, optimize=True)
        import_settings(out, 4096 if stem.startswith("bg_") else 2048)
        print(out.relative_to(ROOT), img.size)


if __name__ == "__main__":
    main(Path(sys.argv[1]))
