#!/usr/bin/env python3
"""hd_atlas.py — листы для апскейла (Р19): кадры выгрузки hd_layer.py — в несколько больших листов и обратно.

Апскейлер (Topaz) на сотне тысяч мелких файлов тратит время на открытие и запись каждого, а не на сам апскейл. Лист — только перевозка:
  py tools/hd/hd_atlas.py pack <листы> --src <выгрузка> [--src <ещё папка>] [--match подстрока ...] [--skip <папка апскейла>] [--size 2048] [--pad 8]
      Кадры выгрузки (плоские имена hd_layer.py) — в листы <листы>/sheet_0001.png… не больше size×size, между кадрами — прозрачный
      зазор pad точек (апскейлер смотрит на соседние точки: без зазора край одного кадра подмешался бы к другому). Рядом с каждым листом —
      sheet_0001.json: плоское имя → место в листе. Кадры одной анимации идут подряд (имена по порядку), лист заполняется полками.
      --skip — кадры, что уже есть в этой папке (апскейл по одному), в листы не идут. Папка листов — пустая: номера листов зависят от
      набора кадров, и прежний увеличенный sheet_0001.png резался бы по новой раскладке (кадры перемешались бы молча).
  py tools/hd/hd_atlas.py unpack <листы> <увеличенные листы> <папка апскейла> [--new]
      Увеличенный лист (то же имя, PNG) режется обратно на кадры под их плоскими именами — в папку апскейла, откуда их берёт
      hd_layer.py import. Множитель — по размеру листа (целый, ≥ 2, одинаковый по сторонам); лист, которого ещё нет, пропускается;
      --new — и лист, все кадры которого уже нарезаны (дорезать листы, что пришли позже). Лист, который не читается (апскейлер
      ещё пишет его), — замечание: прочие режутся, повторный --new дорежет его.
"""
from __future__ import annotations

import argparse
import json
import sys
from concurrent.futures import ProcessPoolExecutor
from dataclasses import dataclass
from itertools import repeat
from pathlib import Path

from PIL import Image

from hd_layer import guard_output, inside

SHEET = "sheet_{:04d}"


@dataclass(frozen=True)
class Placed:
    flat: str
    x: int
    y: int
    w: int
    h: int


def shelves(sizes: list[tuple[str, int, int]], size: int, pad: int) -> list[list[Placed]]:
    """Полки слева направо, сверху вниз; кадр не влезает в лист — новый лист. Кадр больше листа — свой лист его размера (с тем же
    зазором вокруг)."""
    sheets: list[list[Placed]] = []
    current: list[Placed] = []
    x = y = shelf = 0
    for flat, w, h in sizes:
        if w + (2 * pad) > size or h + (2 * pad) > size:
            sheets.append([Placed(flat, pad, pad, w, h)])
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
    guard_output(out)
    if out.is_dir() and any(out.glob("sheet_*")):
        print(f"{out}: уже есть листы — раскладка новых не совпала бы с ними (номера зависят от набора кадров); нужна пустая папка")
        return 1
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


def cut(meta_path: Path, upscaled: Path, out: Path, new_only: bool) -> tuple[str, int, str | None]:
    """Один лист: («нарезан» | «пропущен», кадров, замечание). Кадр пишется во временный файл и переименовывается — оборванная запись
    не выглядит готовым кадром (--new). Лист, который не читается (апскейлер ещё пишет его), — замечание, а не остановка прогона."""
    meta = json.loads(meta_path.read_text(encoding="utf-8"))
    up_path = upscaled / f"{meta_path.stem}.png"
    if not up_path.is_file() or (new_only and all(inside(out, f["flat"]).is_file() for f in meta["frames"])):
        return "пропущен", 0, None
    try:
        with Image.open(up_path) as up:
            sx, sy = up.width / meta["w"], up.height / meta["h"]
            scale = round(sx)
            if scale < 2 or abs(sx - scale) > 0.01 or abs(sy - scale) > 0.01:
                return "пропущен", 0, f"{up_path.name}: {up.width}×{up.height} — не целый множитель ≥ 2 от {meta['w']}×{meta['h']}"
            up = up.convert("RGBA")
            for f in meta["frames"]:
                box = (f["x"] * scale, f["y"] * scale, (f["x"] + f["w"]) * scale, (f["y"] + f["h"]) * scale)
                target = inside(out, f["flat"])
                tmp = target.with_name(target.name + ".part")
                up.crop(box).save(tmp, format="PNG", optimize=False, compress_level=1)
                tmp.replace(target)
    except (OSError, ValueError) as error:
        return "пропущен", 0, f"{up_path.name}: не читается — {type(error).__name__}: {error}"
    return "нарезан", len(meta["frames"]), None


def unpack(sheets: Path, upscaled: Path, out: Path, new_only: bool = False, jobs: int = 1) -> int:
    guard_output(out)
    out.mkdir(parents=True, exist_ok=True)
    metas = sorted(sheets.glob("sheet_*.json"))
    args = (metas, repeat(upscaled), repeat(out), repeat(new_only))
    if jobs > 1:
        with ProcessPoolExecutor(max_workers=jobs) as pool:
            results = list(pool.map(cut, *args))
    else:
        results = list(map(cut, *args))
    done = sum(1 for r in results if r[0] == "нарезан")
    frames = sum(r[1] for r in results)
    problems = [r[2] for r in results if r[2]]
    print(f"листов разрезано {done}, пропущено {len(results) - done} (нет листа{' или уже нарезан' if new_only else ''}); "
          f"кадров {frames} → {out}")
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
    up.add_argument("--new", action="store_true")
    up.add_argument("--jobs", type=int, default=1, help="процессов: каждый режет свой лист (8192² RGBA — ~270 МБ памяти на процесс)")
    a = ap.parse_args(argv)
    if a.cmd == "pack":
        return pack(a.out, a.src, a.match, a.size, a.pad, a.skip)
    return unpack(a.sheets, a.upscaled, a.out, a.new, a.jobs)


if __name__ == "__main__":
    sys.exit(main())
