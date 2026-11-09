"""Frames app renders as store screenshots (#86): a caption band over the screen, in the Basant colours.

    python3 tools/store-shots/frame.py <renders dir> <out dir>

The renders come from the Unity AuditSnapshots test (HULLA_SNAPSHOT_DIR). Play takes 1080 x 1920;
the App Store's 6.7-inch size is 1290 x 2796. Captions are short and free of gambling words.
"""

import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
FONT = os.path.join(HERE, "..", "..", "apps", "unity", "Hulla", "Assets", "_Project", "Presentation", "Theme", "Fonts", "LilitaOne.ttf")

SHOTS = [
    ("audit-home.png", "home", "Desi card nights with your friends"),
    ("theme-basant-mine.png", "table", "Can't follow suit? Throw a thulla!"),
    ("audit-lobby.png", "room", "Make a room, share the code on WhatsApp"),
    ("audit-results.png", "results", "Last one holding cards is the Bhabhi"),
    ("audit-rulebook.png", "rules", "Your house rules, one tap away"),
]

SIZES = {"android": (1080, 1920), "ios-6.7": (1290, 2796)}
SKY_TOP = (126, 200, 245)
SKY_BOTTOM = (255, 214, 140)
INK = (122, 40, 10)


def gradient(size):
    w, h = size
    band = Image.new("RGB", size)
    draw = ImageDraw.Draw(band)
    for y in range(h):
        t = y / max(1, h - 1)
        draw.line([(0, y), (w, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(SKY_TOP, SKY_BOTTOM)))
    return band


def wrap(draw, text, font, width):
    """Two balanced lines when one is too wide, so no word sits alone on the second line."""
    words = text.split()
    if draw.textlength(text, font=font) <= width:
        return [text]
    best = None
    for cut in range(1, len(words)):
        lines = [" ".join(words[:cut]), " ".join(words[cut:])]
        widest = max(draw.textlength(line, font=font) for line in lines)
        if widest <= width and (best is None or widest < best[0]):
            best = (widest, lines)
    if best is not None:
        return best[1]
    lines, line = [], ""
    for word in words:
        trial = (line + " " + word).strip()
        if draw.textlength(trial, font=font) <= width:
            line = trial
        else:
            lines.append(line)
            line = word
    lines.append(line)
    return lines


def frame(render, caption, size):
    w, h = size
    canvas = gradient(size)
    caption_h = int(h * 0.2)
    shot_h = h - caption_h - int(w * 0.04)
    shot = render.convert("RGB")
    scale = min((w * 0.9) / shot.width, shot_h / shot.height)
    shot = shot.resize((int(shot.width * scale), int(shot.height * scale)), Image.LANCZOS)
    mask = Image.new("L", shot.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, shot.width - 1, shot.height - 1], radius=int(w * 0.05), fill=255)
    canvas.paste(shot, ((w - shot.width) // 2, caption_h), mask)
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.truetype(FONT, int(w * 0.075))
    lines = wrap(draw, caption, font, w * 0.86)
    line_h = int(font.size * 1.15)
    y = (caption_h - line_h * len(lines)) // 2
    for line in lines:
        draw.text(((w - draw.textlength(line, font=font)) / 2, y), line, font=font, fill=INK)
        y += line_h
    return canvas


def main(renders, out):
    for name, size in SIZES.items():
        os.makedirs(os.path.join(out, name), exist_ok=True)
        for i, (file, stem, caption) in enumerate(SHOTS, 1):
            image = frame(Image.open(os.path.join(renders, file)), caption, size)
            image.save(os.path.join(out, name, f"{i:02d}-{stem}.jpg"), quality=90, optimize=True)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
