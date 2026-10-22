#!/usr/bin/env python3
"""
ff_extract.py - extractor for Disciples 2 .ff archives (MQDB container).

Port of the format logic from Karnah/Disciples.Net (ImagesExtractor.cs), MIT-licensed.

What it extracts:
  * plain PNG entries (as-is, byte copy; magenta 255/0/255 = game transparency, preserved);
  * composite frames described by -INDEX.OPT / -IMAGES.OPT: sub-images (buttons, panels,
    icons, cursors...) cut out of shared base PNG sheets and rebuilt at their real size;
  * animations from -ANIMS.OPT (frame sequences) - optional, off by default.

Output layout:  <out>/<ArchiveName>/plain/*.PNG        (raw entries)
                <out>/<ArchiveName>/frames/*.PNG       (rebuilt composite frames)
                <out>/<ArchiveName>/anims/<NAME>/NNN.PNG (if --anims)
                <out>/<ArchiveName>/manifest.json       (everything needed to repack later)

Usage:
  python ff_extract.py <file.ff> [<file2.ff> ...] -o <out_dir> [--anims] [--list]
  python ff_extract.py Imgs/Icons.ff -o source --filter "^BORDER"
"""
from __future__ import annotations

import argparse
import io
import json
import mmap
import re
import struct
import sys
from dataclasses import dataclass, field, asdict
from pathlib import Path

from PIL import Image

MAGENTA = (255, 0, 255)
RESOURCE_HEADER = b"MQDB"
RECORD_HEADER = b"MQRC"
FILES_RECORD_ID = 2
ANIMATION_INDEX_ID = -1
PALETTE_BYTES = 1024
RECORDS_START = 28
NAME_FIELD_LEN = 256


@dataclass
class Record:
    id: int
    size: int
    offset: int


@dataclass
class Entry:
    id: int
    name: str
    size: int
    offset: int


@dataclass
class Piece:
    frame_x: int   # position inside the rebuilt frame  (C#: SourceX/SourceY)
    frame_y: int
    base_x: int    # position inside the base sheet       (C#: DestX/DestY)
    base_y: int
    w: int
    h: int


@dataclass
class Frame:
    name: str
    base_file_id: int
    base_file_name: str
    width: int
    height: int
    transparent_index: int
    opacity: int
    pieces: list[Piece] = field(default_factory=list)


class FFArchive:
    def __init__(self, path: Path):
        self.path = path
        # Отображение файла, а не копия в памяти: архив до 346 МБ (BatUnits), и его читают сразу несколько процессов сборки —
        # страницы файла у них общие.
        with open(path, "rb") as f:
            self.data = mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ)
        if self.data[:4] != RESOURCE_HEADER:
            raise ValueError(f"{path}: not an MQDB archive")
        self.records = self._load_records()
        self.entries = self._load_entries()
        self.by_name = {e.name: e for e in self.entries}
        self.by_id = {e.id: e for e in self.entries}
        self.frames: dict[str, Frame] = {}
        self.anims: dict[str, list[str]] = {}
        self._load_opt()

    # ---- container -----------------------------------------------------
    def _u32(self, pos: int) -> int:
        return struct.unpack_from("<i", self.data, pos)[0]

    def _load_records(self) -> dict[int, Record]:
        records: dict[int, Record] = {}
        pos = RECORDS_START
        n = len(self.data)
        while pos + 28 <= n and self.data[pos:pos + 4] == RECORD_HEADER:
            rid = self._u32(pos + 8)
            size = self._u32(pos + 12)
            real = self._u32(pos + 16)
            offset = pos + 28
            records[rid] = Record(rid, size, offset)
            pos = offset + real
        return records

    def _load_entries(self) -> list[Entry]:
        rec = self.records.get(FILES_RECORD_ID)
        if rec is None:
            raise ValueError(f"{self.path}: no file list record (id {FILES_RECORD_ID})")
        pos = rec.offset
        count = self._u32(pos)
        pos += 4
        entries = []
        for _ in range(count):
            raw = self.data[pos:pos + NAME_FIELD_LEN]
            name = raw.split(b"\0", 1)[0].decode("ascii", "replace")
            fid = self._u32(pos + NAME_FIELD_LEN)
            pos += NAME_FIELD_LEN + 4
            r = self.records.get(fid)
            if r is None:
                continue
            entries.append(Entry(fid, name, r.size, r.offset))
        return entries

    def raw(self, entry: Entry) -> bytes:
        return self.data[entry.offset:entry.offset + entry.size]

    def _cstr(self, pos: int) -> tuple[str, int]:
        end = self.data.find(b"\0", pos)
        if end < 0:
            raise ValueError(f"{self.path}: unterminated string at {pos}")
        return self.data[pos:end].decode("ascii", "replace"), end + 1

    # ---- -INDEX.OPT / -IMAGES.OPT / -ANIMS.OPT --------------------------
    def _load_opt(self) -> None:
        idx = self.by_name.get("-INDEX.OPT")
        img = self.by_name.get("-IMAGES.OPT")
        if idx is None or img is None:
            return
        # index: frame name -> base file id (or -1 for animation names)
        index: dict[str, int] = {}
        anim_names: list[str] = []
        pos = idx.offset
        count = self._u32(pos)
        pos += 4
        for _ in range(count):
            fid = self._u32(pos)
            name, pos = self._cstr(pos + 4)
            pos += 8  # related offset, size
            index.setdefault(name, fid)
            if fid == ANIMATION_INDEX_ID:
                anim_names.append(name)

        pos = img.offset
        end = img.offset + img.size - 1
        while pos < end:
            transparent = self.data[pos]
            opacity = struct.unpack_from("<h", self.data, pos + 1)[0]
            pos += 3 + 8  # sizeX, sizeY unused
            pos += PALETTE_BYTES
            frames_count = self._u32(pos)
            pos += 4
            for _ in range(frames_count):
                name, pos = self._cstr(pos)
                pieces_count, w, h = struct.unpack_from("<iii", self.data, pos)
                pos += 12
                pieces = []
                for _ in range(pieces_count):
                    fx, fy, bx, by, pw, ph = struct.unpack_from("<iiiiii", self.data, pos)
                    pos += 24
                    pieces.append(Piece(fx, fy, bx, by, pw, ph))
                base_id = index.get(name, -1)
                base = self.by_id.get(base_id)
                fr = Frame(name, base_id, base.name if base else "", w, h, transparent, opacity, pieces)
                self.frames.setdefault(name, fr)

        an = self.by_name.get("-ANIMS.OPT")
        if an is None:
            return
        pos = an.offset
        end = an.offset + an.size - 1
        i = 0
        while pos < end and i < len(anim_names):
            n = self._u32(pos)
            pos += 4
            names = []
            for _ in range(n):
                nm, pos = self._cstr(pos)
                names.append(nm)
            self.anims[anim_names[i]] = names
            i += 1

    # ---- rebuilding -----------------------------------------------------
    def base_sheets(self) -> set[str]:
        """Entry names of the shared sheets that composite frames are cut from."""
        return {self.by_id[fr.base_file_id].name for fr in self.frames.values() if fr.base_file_id in self.by_id}

    def buildable(self, fr: Frame) -> bool:
        """Exactly when build_frame returns an image (its base sheet exists and the size is positive)."""
        return fr.base_file_id in self.by_id and fr.width > 0 and fr.height > 0

    def plain_image(self, name: str) -> Image.Image:
        """A plain PNG entry decoded as-is (magenta still in it)."""
        with Image.open(io.BytesIO(self.raw(self.by_name[name]))) as im:
            im.load()
            return im.copy()

    def base_image(self, file_id: int, cache: dict, transparent: int | None = None) -> Image.Image | None:
        """Лист-атлас в RGB. ``transparent`` — индекс палитры ключа из ``-IMAGES.OPT``: движок блитит его как ключ прозрачности
        **независимо от цвета в палитре** (``docs/hd-mod-sprite-id.md`` п. 2, ``docs/level2-rasterizer-spec.md`` §10 в
        disciples-modding: у 85 групп ``palette[0]`` — чёрный, у остальных — маджента). Точки этого индекса становятся мадженты,
        дальше их вырезает общий ключ (``keyed_webp.key_mask``). Без этого атака гоблина (``G000UU5017HMOVA1*``) рисовалась в
        чёрных прямоугольниках — пункт 2 прогона 2 (11.10)."""
        key = (file_id, transparent)
        if key in cache:
            return cache[key]
        e = self.by_id.get(file_id)
        if e is None:
            return None
        im = Image.open(io.BytesIO(self.raw(e)))
        if transparent is not None and im.mode == "P" and 0 <= transparent < 256:
            palette = im.getpalette() or []
            if len(palette) >= 3 * (transparent + 1):
                palette[3 * transparent:3 * transparent + 3] = list(MAGENTA)
                im.putpalette(palette)
        im = im.convert("RGB")
        cache[key] = im
        return im

    def build_frame(self, fr: Frame, cache: dict[int, Image.Image]) -> Image.Image | None:
        base = self.base_image(fr.base_file_id, cache, fr.transparent_index)
        if base is None or fr.width <= 0 or fr.height <= 0:
            return None
        out = Image.new("RGB", (fr.width, fr.height), MAGENTA)
        bw, bh = base.size
        for p in fr.pieces:
            if p.base_x >= bw or p.base_y >= bh:
                continue
            w = min(p.w, bw - p.base_x, fr.width - p.frame_x)
            h = min(p.h, bh - p.base_y, fr.height - p.frame_y)
            if w <= 0 or h <= 0:
                continue
            out.paste(base.crop((p.base_x, p.base_y, p.base_x + w, p.base_y + h)), (p.frame_x, p.frame_y))
        return out


def extract(archive: Path, out_root: Path, want_anims: bool, name_filter: re.Pattern | None) -> dict:
    ff = FFArchive(archive)
    out = out_root / archive.stem
    plain_dir = out / "plain"
    frames_dir = out / "frames"
    anim_frame_names = {n for names in ff.anims.values() for n in names}
    stats = {"archive": archive.name, "plain": 0, "frames": 0, "anims": 0, "skipped": 0}
    manifest = {
        "archive": archive.name,
        "entries": [asdict(e) for e in ff.entries],
        "frames": {},
        "anims": ff.anims,
    }

    base_ids = {fr.base_file_id for fr in ff.frames.values()}
    for e in ff.entries:
        if not e.name.upper().endswith(".PNG"):
            continue
        if name_filter and not name_filter.search(e.name):
            continue
        plain_dir.mkdir(parents=True, exist_ok=True)
        (plain_dir / e.name).write_bytes(ff.raw(e))
        stats["plain"] += 1

    cache: dict[int, Image.Image] = {}
    for name, fr in ff.frames.items():
        if name in anim_frame_names and not want_anims:
            continue
        if name_filter and not name_filter.search(name):
            continue
        im = ff.build_frame(fr, cache)
        if im is None:
            stats["skipped"] += 1
            continue
        manifest["frames"][name] = {
            "base": fr.base_file_name, "w": fr.width, "h": fr.height,
            "transparent_index": fr.transparent_index, "opacity": fr.opacity,
            "pieces": [asdict(p) for p in fr.pieces],
        }
        if name in anim_frame_names:
            continue  # written below per animation
        frames_dir.mkdir(parents=True, exist_ok=True)
        im.save(frames_dir / f"{name}.PNG")
        stats["frames"] += 1

    if want_anims:
        for anim, names in ff.anims.items():
            if name_filter and not name_filter.search(anim):
                continue
            d = out / "anims" / anim
            for i, n in enumerate(names):
                fr = ff.frames.get(n)
                im = ff.build_frame(fr, cache) if fr else None
                if im is None:
                    continue
                d.mkdir(parents=True, exist_ok=True)
                im.save(d / f"{i:03d}_{n}.PNG")
            stats["anims"] += 1

    manifest["base_sheets"] = sorted(ff.by_id[i].name for i in base_ids if i in ff.by_id)
    out.mkdir(parents=True, exist_ok=True)
    (out / "manifest.json").write_text(json.dumps(manifest, indent=1), encoding="utf-8")
    return stats


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("archives", nargs="+", type=Path)
    ap.add_argument("-o", "--out", type=Path, required=True)
    ap.add_argument("--anims", action="store_true", help="also rebuild animation frames from -ANIMS.OPT")
    ap.add_argument("--filter", help="regex on entry/frame names")
    ap.add_argument("--list", action="store_true", help="only list entries, frames and animations")
    args = ap.parse_args()
    flt = re.compile(args.filter, re.I) if args.filter else None

    for archive in args.archives:
        if args.list:
            ff = FFArchive(archive)
            print(f"== {archive.name}: {len(ff.entries)} entries, {len(ff.frames)} frames, {len(ff.anims)} anims")
            for e in ff.entries:
                print(f"  E {e.id:6d} {e.size:9d} {e.name}")
            for n, fr in ff.frames.items():
                print(f"  F {fr.width:4d}x{fr.height:<4d} {n}  <- {fr.base_file_name}")
            for a, names in ff.anims.items():
                print(f"  A {len(names):3d} frames {a}")
            continue
        try:
            print(extract(archive, args.out, args.anims, flt))
        except Exception as exc:  # report and continue with next archive
            print(f"ERROR {archive}: {exc}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
