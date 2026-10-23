#!/usr/bin/env python3
"""hd_atlas.py — листы для апскейла (Р19): кадры выгрузки hd_layer.py — в несколько больших листов и обратно.

Апскейлер (Topaz) на сотне тысяч мелких файлов тратит время на открытие и запись каждого, а не на сам апскейл. Лист — только перевозка:
  py tools/hd/hd_atlas.py pack <листы> --src <выгрузка> [--src <ещё папка>] [--match подстрока ...] [--skip <папка апскейла>] [--size 2048] [--pad 8]
      Кадры выгрузки (плоские имена hd_layer.py) — в листы <листы>/sheet_0001.png… не больше size×size, между кадрами — прозрачный
      зазор pad точек (апскейлер смотрит на соседние точки: без зазора край одного кадра подмешался бы к другому). Рядом с каждым листом —
      sheet_0001.json: плоское имя → место в листе. Кадры одной анимации идут подряд (имена по порядку), лист заполняется полками.
      --skip — кадры, что уже есть в этой папке (апскейл по одному), в листы не идут.
  py tools/hd/hd_atlas.py unpack <листы> <увеличенные листы> <папка апскейла>
      Увеличенный лист (то же имя, PNG) режется обратно на кадры под их плоскими именами — в папку апскейла, откуда их берёт
      hd_layer.py import. Множитель — по размеру листа (целый, одинаковый по сторонам); лист, которого ещё нет, пропускается.
"""
from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass
from pathlib import Path

from PIL import Image

SHEET = "sheet_{:04d}"


@dataclass(frozen=True)
class Placed:
    flat: str
    x: int
    y: int
    w: int
    h: int


def shelves(sizes: list[tuple[str, int, int]], size: int, pad: int) -> list[list[Placed]]:
    """Полки слева направо, сверху вниз; кадр не влезает в лист — новый лист. Кадр больше листа — свой лист его размера."""
    sheets: list[list[Placed]] = []
    current: list[Placed] = []
    x = y = shelf = 0
    for flat, w, h in sizes:
        if w + (2 * pad) > size or h + (2 * pad) > size:
            sheets.append([Placed(flat, 0, 0, w, h)])
            continue
        # Зазор — и перед кадром, и после последнего: лист не шире size.
        if x + w + (2 * pad) > size:
            x, y, shelf = 0, y + shelf, 0
        if y + h + (2 * pad) > size:
            sheets.append(current)
            current, x, y, shelf = [], 0, 0, 0
        current.append(Placed(flat, x + pad, y + pad, w, h))
        x += w + pad
        shelf = max(shelf, h + pad)
    if current:
        sheets.append(current)
    return sheets


def find(flat: str, sources: list[Path]) -> Path | None:
    return next((s / flat for s in sources if (s / flat).is_file()), None)


def pack(out: Path, sources: list[Path], match: list[str], size: int, pad: int, skip: list[Path] | None = None) -> int:
    names = sorted({p.name for s in sources for p in s.glob("*.png")})
    ready = {p.name for d in skip or [] for p in d.glob("*.png")}
    names = [n for n in names if n not in ready]
    if match:
        names = [n for n in names if any(m in n for m in match)]
    sizes = []
    for name in names:
        with Image.open(find(name, sources)) as im:
            sizes.append((name, im.width, im.height))
    out.mkdir(parents=True, exist_ok=True)
    sheets = shelves(sizes, size, pad)
    for i, placed in enumerate(sheets, 1):
        w = max(p.x + p.w for p in placed) + pad
        h = max(p.y + p.h for p in placed) + pad
        sheet = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        for p in placed:
            with Image.open(find(p.flat, sources)) as im:
                sheet.paste(im.convert("RGBA"), (p.x, p.y))
        stem = SHEET.format(i)
        sheet.save(out / f"{stem}.png", optimize=False, compress_level=1)
        (out / f"{stem}.json").write_text(json.dumps({"w": w, "h": h, "pad": pad, "frames": [p.__dict__ for p in placed]},
                                                     ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"кадров {len(sizes)} → листов {len(sheets)} в {out} (не больше {size}×{size}, зазор {pad}; уже готовых пропущено {len(ready)})")
    return 0


def unpack(sheets: Path, upscaled: Path, out: Path) -> int:
    out.mkdir(parents=True, exist_ok=True)
    done = skipped = frames = 0
    problems = []
    for meta_path in sorted(sheets.glob("sheet_*.json")):
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        up_path = upscaled / f"{meta_path.stem}.png"
        if not up_path.is_file():
            skipped += 1
            continue
        with Image.open(up_path) as up:
            sx, sy = up.width / meta["w"], up.height / meta["h"]
            scale = round(sx)
            if scale < 1 or abs(sx - scale) > 0.01 or abs(sy - scale) > 0.01:
                problems.append(f"{up_path.name}: {up.width}×{up.height} — не целый множитель от {meta['w']}×{meta['h']}")
                continue
            up = up.convert("RGBA")
            for f in meta["frames"]:
                box = (f["x"] * scale, f["y"] * scale, (f["x"] + f["w"]) * scale, (f["y"] + f["h"]) * scale)
                up.crop(box).save(out / f["flat"], optimize=False, compress_level=1)
                frames += 1
        done += 1
    print(f"листов разрезано {done}, ещё нет {skipped}; кадров {frames} → {out}")
    for p in problems:
        print("  ", p)
    return 1 if problems else 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0] if __doc__ else None)
    sub = ap.add_subparsers(dest="cmd", required=True)
    pk = sub.add_parser("pack")
    pk.add_argument("out", type=Path)
    pk.add_argument("--src", type=Path, action="append", required=True)
    pk.add_argument("--match", action="append", default=[])
    pk.add_argument("--skip", type=Path, action="append", default=[])
    pk.add_argument("--size", type=int, default=2048)
    pk.add_argument("--pad", type=int, default=8)
    up = sub.add_parser("unpack")
    up.add_argument("sheets", type=Path)
    up.add_argument("upscaled", type=Path)
    up.add_argument("out", type=Path)
    a = ap.parse_args(argv)
    if a.cmd == "pack":
        return pack(a.out, a.src, a.match, a.size, a.pad, a.skip)
    return unpack(a.sheets, a.upscaled, a.out)


if __name__ == "__main__":
    sys.exit(main())
