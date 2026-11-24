"""Render a multi-resolution Windows icon using a locally installed YueHei font."""

import argparse
from io import BytesIO
from pathlib import Path
import struct

from PIL import Image, ImageDraw, ImageFont


SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
CORAL = "#CC575C"
PAPER = "#FFFCFA"
INK = "#292424"


def icon_frame(size, font_path):
    scale = 8
    extent = size * scale
    image = Image.new("RGBA", (extent, extent))
    draw = ImageDraw.Draw(image)
    inset = max(1, round(extent * 0.019))
    stroke = max(9, round(extent * 0.088))
    draw.rounded_rectangle(
        (inset, inset, extent - 1 - inset, extent - 1 - inset),
        radius=round(extent * 0.115),
        fill=PAPER,
        outline=CORAL,
        width=stroke,
    )

    glyph = "办"
    font = ImageFont.truetype(str(font_path), extent)
    left, top, right, bottom = font.getbbox(glyph)
    glyph_fraction = 0.71 if size <= 24 else 0.65
    font_size = round(extent * min(extent * glyph_fraction / (right - left), extent * glyph_fraction / (bottom - top)))
    font = ImageFont.truetype(str(font_path), font_size)
    left, top, right, bottom = font.getbbox(glyph)
    # Strengthen the original contours at tray sizes, where several strokes fall between pixels.
    stroke_hint = 2 if size <= 20 else 1 if size <= 24 else 0
    draw.text(
        ((extent - right - left) / 2, (extent - bottom - top) / 2),
        glyph,
        font=font,
        fill=INK,
        stroke_width=stroke_hint,
        stroke_fill=INK,
    )
    return image.resize((size, size), Image.Resampling.LANCZOS)


def write_ico(path, frames):
    offset = 6 + 16 * len(frames)
    directory = bytearray(struct.pack("<HHH", 0, 1, len(frames)))
    payloads = []
    for frame in frames:
        with BytesIO() as output:
            frame.save(output, "PNG", optimize=True)
            payload = output.getvalue()
        width, height = frame.size
        directory.extend(struct.pack("<BBBBHHII", width % 256, height % 256, 0, 0, 1, 32, len(payload), offset))
        payloads.append(payload)
        offset += len(payload)
    path.write_bytes(directory + b"".join(payloads))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("font", type=Path)
    parser.add_argument("ico", type=Path)
    parser.add_argument("--preview", type=Path)
    args = parser.parse_args()
    frames = [icon_frame(size, args.font) for size in SIZES]
    args.ico.parent.mkdir(parents=True, exist_ok=True)
    write_ico(args.ico, frames)
    if args.preview:
        args.preview.parent.mkdir(parents=True, exist_ok=True)
        frames[-1].save(args.preview)


if __name__ == "__main__":
    main()
