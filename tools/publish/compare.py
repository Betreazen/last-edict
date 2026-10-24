"""Пара «было / стало» для README (docs/media.md): два снимка рядом, над каждым — подпись (EB Garamond из data/fonts).

py tools/publish/compare.py <было.png> <стало.png> --left "Disciples II" --right "Last Edict" --out <каталог>/compare-<имя>.png

Пишет полный PNG и *-small.png ≤ 200 КБ (палитра 256 цветов, ширина по убыванию, пока не влезет). Снимки разного размера приводятся к
высоте меньшего. Результат — только во временный каталог, не в git (на снимках графика оригинала, Р3).
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

from screenshots import SMALL_MAX_BYTES, small_copy

REPO = Path(__file__).resolve().parents[2]
FONT = REPO / "data" / "fonts" / "EBGaramond-SemiBold.ttf"
GAP = 16
BAND = 56                       # полоса подписи над снимком
FONT_SIZE = 34
BACKGROUND = (24, 22, 20)
INK = (232, 220, 196)
SMALL_WIDTHS = (1280, 1120, 960, 800, 640)   # пара снимков шире одного — и копия шире


def fit(image: Image.Image, height: int) -> Image.Image:
    if image.height == height:
        return image
    return image.resize((image.width * height // image.height, height), Image.Resampling.LANCZOS)


def glue(left: Image.Image, right: Image.Image, left_text: str, right_text: str) -> Image.Image:
    height = min(left.height, right.height)
    left, right = fit(left, height), fit(right, height)
    sheet = Image.new("RGB", (left.width + GAP + right.width, BAND + height), BACKGROUND)
    sheet.paste(left, (0, BAND))
    sheet.paste(right, (left.width + GAP, BAND))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.truetype(str(FONT), FONT_SIZE)
    for text, x, width in ((left_text, 0, left.width), (right_text, left.width + GAP, right.width)):
        box = draw.textbbox((0, 0), text, font=font)
        draw.text((x + (width - (box[2] - box[0])) // 2, (BAND - (box[3] - box[1])) // 2 - box[1]), text, font=font, fill=INK)
    return sheet


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    parser.add_argument("--left", required=True, help="подпись левого снимка")
    parser.add_argument("--right", required=True, help="подпись правого снимка")
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    with Image.open(args.before) as before, Image.open(args.after) as after:
        sheet = glue(before.convert("RGB"), after.convert("RGB"), args.left, args.right)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(args.out, optimize=True)
    small = args.out.with_name(args.out.stem + "-small.png")
    size = small_copy(sheet, small, SMALL_WIDTHS)
    if size is None:
        print(f"{small.name}: и в {SMALL_WIDTHS[-1]} точек больше {SMALL_MAX_BYTES} байт")
        return 1
    print(f"{args.out.name} {sheet.width}x{sheet.height} {args.out.stat().st_size // 1024} КБ; "
          f"{small.name} {size[0]}x{size[1]} {small.stat().st_size // 1024} КБ")
    return 0


if __name__ == "__main__":
    sys.exit(main())
