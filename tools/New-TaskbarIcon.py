"""Render HanLin's C-series single-character application badges and installer art."""

import argparse
from io import BytesIO
from pathlib import Path
import struct

from PIL import Image, ImageDraw, ImageFont

SIZES = (16, 20, 24, 25, 32, 40, 48, 64, 96, 128, 256)
TASKBAR_SIZES = (16, 20, 24, 32)
PAPER = "#F8F8F6"
FRAME = "#CF6266"
GLYPH = "#000000"


def icon_frame(size, font_path, glyph):
    scale = 8
    extent = size * scale
    small = size in TASKBAR_SIZES
    inset = 0 if small else max(2, extent // 40)
    radius = round(extent * (0.18 if small else 0.22))
    border = max(round(scale * 1.35), round(extent * 0.06)) if small else max(scale, extent // 24)
    image = Image.new("RGBA", (extent, extent))
    ImageDraw.Draw(image).rounded_rectangle(
        (inset, inset, extent - 1 - inset, extent - 1 - inset), radius,
        fill=PAPER, outline=FRAME, width=border,
    )
    font = ImageFont.truetype(str(font_path), extent)
    stroke = max(2, extent // 65) if small else max(1, extent // 110)
    mask = Image.new("L", (2 * extent, 2 * extent))
    ImageDraw.Draw(mask).text((extent // 3, extent // 3), glyph, 255, font=font, stroke_width=stroke)
    mask = mask.crop(mask.getbbox())
    max_width, max_height = (0.78, 0.78) if small else (0.64, 0.65)
    ratio = min(extent * max_width / mask.width, extent * max_height / mask.height)
    fitted = (round(mask.width * ratio), round(mask.height * ratio))
    position = tuple((extent - dimension) // 2 for dimension in fitted)
    image.paste(GLYPH, position, mask.resize(fitted, Image.Resampling.BILINEAR))
    return image.resize((size, size), Image.Resampling.BOX)


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


def wizard_art(icon_path, folder):
    folder.mkdir(parents=True, exist_ok=True)
    with Image.open(icon_path) as icon:
        badge = icon.convert("RGBA")
    badge.resize((128, 128), Image.Resampling.LANCZOS).save(folder / "HanLin-Header.png")

    # Maintain Inno Setup's 164:314 image ratio. The larger raster covers high DPI.
    width, height = 492, 942
    panel = Image.new("RGB", (width, height), "#FCEEEB")
    draw = ImageDraw.Draw(panel)
    for y in range(height):
        fraction = y / (height - 1)
        tone = tuple(round(top + (bottom - top) * fraction) for top, bottom in zip((250, 232, 230), (255, 252, 249)))
        draw.line((0, y, width - 1, y), fill=tone)
    for radius in (152, 185, 218):
        draw.ellipse((246 - radius, 428 - radius, 246 + radius, 428 + radius), outline="#F0C9C6", width=2)
    mark = badge.resize((208, 208), Image.Resampling.LANCZOS)
    panel.paste(mark, (142, 324), mark)
    panel.save(folder / "HanLin-Welcome.png", optimize=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("font", type=Path)
    parser.add_argument("ico", type=Path)
    parser.add_argument("--glyph", default="办")
    parser.add_argument("--preview", type=Path)
    parser.add_argument("--wizard-art", type=Path)
    args = parser.parse_args()
    if len(args.glyph) != 1:
        parser.error("--glyph expects one character")
    frames = [icon_frame(size, args.font, args.glyph) for size in SIZES]
    args.ico.parent.mkdir(parents=True, exist_ok=True)
    write_ico(args.ico, frames)
    if args.preview:
        args.preview.parent.mkdir(parents=True, exist_ok=True)
        frames[-1].save(args.preview)
    if args.wizard_art:
        wizard_art(args.ico, args.wizard_art)


if __name__ == "__main__":
    main()
