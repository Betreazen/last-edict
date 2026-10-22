#!/usr/bin/env python3
"""import_fonts.py — растровые шрифты игры (Interf/*.mft) в BMFont: assets/fonts/<имя>.fnt + <имя>.png.

Шрифты игры — семь однобитных растров фиксированного кегля (small, normal, medbold, medium, menu, large, vlarge). Порт в рантайме
формат MFT не читает (docs/principles.md): импорт один раз переводит их в BMFont — текстовое описание глифов и PNG-атлас, обычный
формат растровых шрифтов, который Godot грузит сам (FontFile.LoadBitmapFont), а мод может нарисовать своим инструментом
(BMFont, Hiero, Glyph Designer) или заменить на TrueType в data/ui/fonts.json.

Формат MFT (разбор и замеры — docs/ui-text.md §2): "MFT\\0", u32 версия (2), u32 длина имени, имя, u32 высота ячейки, u32 межстрочный
интервал, u32 ?, байт 0, затем 255 записей по кодам 1…255: u8 есть ли глиф, u32 ширина, u32 высота, u32 продвижение (= ширине), u32 ?,
и если глиф есть — растр 1 бит на точку, старший бит слева, строка выровнена на байт. Глиф стоит от верха ячейки (базовая линия —
высота ячейки, выносные вниз выходят за неё), перо после глифа сдвигается ровно на его ширину: строка «Вы проиграли эту миссию.»,
нарисованная так, совпала со снимком оригинала 28.09 во всех 376 точках (DLG_MESSAGE_BOX).

Коды — однобайтовые: 0x20…0xBF у английской и русской игры одинаковы (cp1252), 0xC0…0xFF у английской — латиница-1 (cp1252), у
русификации Руссобит-М — кириллица (cp1251, без Ё/ё). Импорт сводит оба слоя в один шрифт по Юникоду: латиница-1 из игры
(D2_GAME_DIR), кириллица из слоя русификации (disciples-vanilla/l10n/russobit, параметр --l10n); Ё и ё рисуются глифами Е и е
(в шрифтах игры их нет, а имя героя, набранное игроком, их содержать может). Слоя русификации нет — шрифт без кириллицы и
предупреждение.

Выход — D2_ASSETS_DIR/fonts (по умолчанию assets/fonts репозитория), вне git, как и остальная графика игры.

Запуск:
  python tools/ui-assets/import_fonts.py
  python tools/ui-assets/import_fonts.py --l10n "C:/…/l10n/russobit" --out "C:/…/assets/fonts"
"""
from __future__ import annotations

import argparse
import os
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parents[2]
PROJECTS = REPO.parent
DEFAULT_GAME_DIR = PROJECTS / "disciples-modding" / "game-pristine"
DEFAULT_L10N_DIR = PROJECTS / "disciples-vanilla" / "l10n" / "russobit"
NAMES = ("small", "normal", "medbold", "medium", "menu", "large", "vlarge")
ATLAS_WIDTH = 256
PAD = 1   # пустая точка между глифами атласа: фильтр текстуры не захватит соседа
ALIASES = {"Ё": "Е", "ё": "е"}


@dataclass(frozen=True)
class Glyph:
    width: int
    height: int
    rows: tuple[tuple[bool, ...], ...]


@dataclass(frozen=True)
class Mft:
    name: str
    height: int
    line_spacing: int
    glyphs: dict[int, Glyph]


def parse_mft(path: Path) -> Mft:
    data = path.read_bytes()
    if data[:4] != b"MFT\0":
        raise ValueError(f"{path}: не MFT (подпись {data[:4]!r})")
    version, name_len = struct.unpack_from("<II", data, 4)
    if version != 2:
        raise ValueError(f"{path}: версия {version}, разобрана только 2")
    name = data[12:12 + name_len].decode("ascii")
    at = 12 + name_len
    height, line_spacing, _ = struct.unpack_from("<III", data, at)
    at += 13
    glyphs: dict[int, Glyph] = {}
    for code in range(1, 256):
        present = data[at]
        width, glyph_height, advance, _ = struct.unpack_from("<IIII", data, at + 1)
        at += 17
        if not present:
            continue
        if advance != width:
            raise ValueError(f"{path}: у кода {code:#x} продвижение {advance} ≠ ширине {width}")
        stride = (width + 7) // 8
        bits = data[at:at + stride * glyph_height]
        at += stride * glyph_height
        rows = tuple(tuple(bool(bits[y * stride + x // 8] & (0x80 >> (x % 8))) for x in range(width)) for y in range(glyph_height))
        glyphs[code] = Glyph(width, glyph_height, rows)
    if at != len(data):
        raise ValueError(f"{path}: после 255 глифов осталось {len(data) - at} байт")
    return Mft(name, height, line_spacing, glyphs)


def unicode_of(code: int, encoding: str) -> str | None:
    """Символ однобайтового кода; управляющие коды, DEL и пустые места кодовой страницы — None."""
    if code < 0x20 or code == 0x7F:
        return None
    try:
        return bytes([code]).decode(encoding)
    except UnicodeDecodeError:
        return None


def merge(base: Mft, l10n: Mft | None) -> dict[str, Glyph]:
    """Глифы по символам: 0x20…0xFF игры в cp1252, поверх — кириллица 0xC0…0xFF слоя русификации в cp1251, затем Ё/ё."""
    chars: dict[str, Glyph] = {}
    for code, glyph in base.glyphs.items():
        if (ch := unicode_of(code, "cp1252")) is not None:
            chars[ch] = glyph
    if l10n is not None:
        if (l10n.height, l10n.line_spacing) != (base.height, base.line_spacing):
            raise ValueError(f"{base.name}: ячейка русификации {l10n.height}+{l10n.line_spacing} ≠ {base.height}+{base.line_spacing}")
        for code in range(0xC0, 0x100):
            if code in l10n.glyphs:
                chars[bytes([code]).decode("cp1251")] = l10n.glyphs[code]
        for alias, source in ALIASES.items():
            if source in chars:
                chars[alias] = chars[source]
    return chars


def write_bmfont(face: Mft, chars: dict[str, Glyph], out: Path) -> tuple[Path, Path]:
    """BMFont (текстовый вариант) + PNG-атлас: глиф белый, покрытие в альфе (0 или 255 — полутонов у MFT нет)."""
    order = sorted(chars, key=ord)
    places: dict[str, tuple[int, int]] = {}
    x = y = PAD
    row_height = 0
    for ch in order:
        g = chars[ch]
        if x + g.width + PAD > ATLAS_WIDTH:
            x, y, row_height = PAD, y + row_height + PAD, 0
        places[ch] = (x, y)
        x += g.width + PAD
        row_height = max(row_height, g.height)
    atlas_height = 1
    while atlas_height < y + row_height + PAD:
        atlas_height *= 2
    image = Image.new("RGBA", (ATLAS_WIDTH, atlas_height), (255, 255, 255, 0))
    pixels = image.load()
    for ch in order:
        g, (gx, gy) = chars[ch], places[ch]
        for row, bits in enumerate(g.rows):
            for col, ink in enumerate(bits):
                if ink:
                    pixels[gx + col, gy + row] = (255, 255, 255, 255)
    png = out / f"{face.name.lower()}.png"
    fnt = out / f"{face.name.lower()}.fnt"
    image.save(png, optimize=True)
    lines = [
        f'info face="{face.name}" size={face.height} bold=0 italic=0 charset="" unicode=1 stretchH=100 smooth=0 aa=1 '
        f"padding=0,0,0,0 spacing={PAD},{PAD} outline=0",
        # Ячейка как у MFT: строка высотой в ячейку, базовая линия — её низ (глифы стоят от верха ячейки). Межстрочный интервал MFT в
        # шрифт не входит — он в data/ui/fonts.json (Line), как и у TrueType.
        f"common lineHeight={face.height} base={face.height} scaleW={ATLAS_WIDTH} scaleH={atlas_height} pages=1 packed=0 "
        "alphaChnl=0 redChnl=4 greenChnl=4 blueChnl=4",
        f'page id=0 file="{png.name}"',
        f"chars count={len(order)}",
    ]
    for ch in order:
        g, (gx, gy) = chars[ch], places[ch]
        lines.append(f"char id={ord(ch)} x={gx} y={gy} width={g.width} height={g.height} xoffset=0 yoffset=0 "
                     f"xadvance={g.width} page=0 chnl=15")
    lines.append("kernings count=0")
    fnt.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    return fnt, png


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--game", type=Path, default=Path(os.environ.get("D2_GAME_DIR") or DEFAULT_GAME_DIR))
    parser.add_argument("--l10n", type=Path, default=DEFAULT_L10N_DIR, help="слой русификации (кириллица 0xC0…0xFF)")
    parser.add_argument("--out", type=Path, default=None, help="каталог шрифтов (по умолчанию D2_ASSETS_DIR/fonts или assets/fonts)")
    args = parser.parse_args()
    assets = os.environ.get("D2_ASSETS_DIR")
    out: Path = args.out or (Path(assets) / "fonts" if assets else REPO / "assets" / "fonts")

    missing = [n for n in NAMES if not (args.game / "Interf" / f"{n}.mft").is_file()]
    if missing:
        print(f"нет шрифтов в {args.game / 'Interf'}: {', '.join(missing)}", file=sys.stderr)
        return 1
    l10n_dir = args.l10n / "Interf"
    if not l10n_dir.is_dir():
        print(f"предупреждение: нет слоя русификации {l10n_dir} — шрифты без кириллицы", file=sys.stderr)
    out.mkdir(parents=True, exist_ok=True)
    for name in NAMES:
        base = parse_mft(args.game / "Interf" / f"{name}.mft")
        l10n_path = l10n_dir / f"{name}.mft"
        l10n = parse_mft(l10n_path) if l10n_path.is_file() else None
        chars = merge(base, l10n)
        fnt, _ = write_bmfont(base, chars, out)
        cyrillic = sum(1 for c in chars if "\u0400" <= c <= "\u04ff")
        print(f"{name:8} ячейка {base.height}+{base.line_spacing}  глифов {len(chars)} (кириллица {cyrillic}) -> {fnt}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
