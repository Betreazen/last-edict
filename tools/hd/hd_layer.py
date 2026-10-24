#!/usr/bin/env python3
"""hd_layer.py — HD-графика через Topaz (Р19, Э5): выгрузка в одну плоскую папку и обратный импорт слоем мода.

  py tools/hd/hd_layer.py export <папка> [--sets ui,faces] [--limit N]
      Выбранные наборы графики (PNG) — в одну папку без подпапок, имена плоские и уникальные (путь зашит в имя:
      ui__Interf__DLG_MAIN_MENU_BG.png, gfx__Faces__G000UU0019FACEB.png, gfx__IsoUnit__G000UU0001MOVE~000_A.png),
      рядом manifest.json: плоское имя → исходный кадр, логический размер, угол обрезки.
  py tools/hd/hd_layer.py import <папка выгрузки> <папка апскейла> [--mod mods/hd] [--id hd]
      Результат Topaz (те же имена) → слой мода: файлы в assets/ui и assets/gfx мода, описи data/ui/assets.json и
      data/gfx/<Архив>.json с множителем scale (логические размеры и углы — прежние), mod.json. Сверяет: множитель целый
      и одинаковый по сторонам (до 1 точки разницы — подгоняется), прозрачность (Topaz отдаёт RGB — альфа берётся с исходника,
      увеличенная). Отчёт — import-report.txt в моде и на экран. Анимация берётся только целиком (все кадры).
      --compress — сжать слой: рядом с каждым PNG — DDS BC7 с mip-уровнями (godot/bench/hd_compress.gd, нужен Godot: GODOT или
      C:/Tools/bin/godot.cmd); игра берёт DDS в текстуру, в видеопамяти ≈ ×0,33 от RGBA. --jobs N — процессов разбора и сжатия
      (по умолчанию половина ядер; вся графика ×4 — десятки гигабайт).

Наборы — порядок апскейла из docs/own-version-roadmap.md §3.3: ui (интерфейс), faces (портреты, лорды), battle-bg (поле боя),
capital (столица, город), icons (значки, события, вор), map (карта: объекты, земля, мини-карта, курсоры, заклинания), sprites (юниты
боя и карты — самые большие). Корень графики — D2_ASSETS_DIR или assets/ рядом с репозиторием; описи — data/ui, data/gfx (импорт).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parents[2]
SETS = {
    "ui": None,   # data/ui/assets.json целиком
    "faces": ["Faces", "Lords"],
    "battle-bg": ["Battle"],
    "capital": ["Capital", "City"],
    "icons": ["IconBld", "IconItem", "IconSpel", "Icons", "Events", "Thief"],
    "map": ["IsoCmon", "IsoStill", "IsoAnim", "IsoTerrn", "GrBorder", "PalMap", "Ground", "IsoSpell", "IsoCursr"],
    "sprites": ["BatUnits", "IsoUnit", "BatItems"],
}
MANIFEST = "manifest.json"

# Земля карты собирается на процессоре в логических точках (GroundArt: узоры Ground, маски переходов GrBorder) — HD ей ничего не даёт,
# а увеличенная и уменьшенная обратно маска портит переходы (чёрная сетка между плитками, ступенчатый берег; проба 24.10).
# Эти архивы в слой не идут.
LOGICAL_ONLY = {"Ground.ff", "GrBorder.ff"}

# Плитки, что смыкаются точка в точку (туман войны, куски дорог): апскейл смягчает их жёсткий край, и стыки между соседними плитками
# просвечивают сеткой (проба 24.10). В слой не идут — по архиву и началу имени.
SEAMLESS = {("IsoTerrn.ff", "FOG"), ("IsoTerrn.ff", "ROAD")}


def kept(e: dict) -> bool:
    """Кадр идёт в HD-слой: не земля и не плитка, что смыкается с соседними."""
    return e.get("archive") not in LOGICAL_ONLY and not any(
        e.get("archive") == archive and e.get("name", "").upper().startswith(prefix) for archive, prefix in SEAMLESS)


def assets_root() -> Path:
    env = os.environ.get("D2_ASSETS_DIR")
    return Path(env) if env else REPO / "assets"


def flat_name(kind: str, archive: str, name: str) -> str:
    """Плоское уникальное имя: вид, архив без .ff и имя кадра; «/» имён (кадр анимации) — «~»."""
    stem = archive[:-3] if archive.lower().endswith(".ff") else archive
    return f"{kind}__{stem}__{name}".replace("/", "~") + ".png"


@dataclass
class Entry:
    flat: str
    kind: str          # ui | gfx-image | gfx-anim
    archive: str
    name: str          # кадр (ui, gfx-image) или анимация (gfx-anim)
    file: str          # путь файла от корня assets/ui или assets/gfx
    w: int
    h: int
    extra: dict        # прочие поля описи: anim, at, opacity, frame, frames, dir, ext


def collect(sets: list[str], data: Path) -> list[Entry]:
    entries: list[Entry] = []
    if "ui" in sets:
        for a in json.loads((data / "ui" / "assets.json").read_text(encoding="utf-8")):
            extra = {k: a[k] for k in ("anim",) if k in a}
            entries.append(Entry(flat_name("ui", a["archive"], a["name"]), "ui", a["archive"], a["name"], a["file"], a["w"], a["h"], extra))
    archives = [x for s in sets if SETS.get(s) for x in SETS[s]]
    for archive in archives:
        path = data / "gfx" / f"{archive}.json"
        if not path.exists():
            continue
        inv = json.loads(path.read_text(encoding="utf-8"))
        arch = inv["archive"]
        for i in inv.get("images", []):
            extra = {k: i[k] for k in ("at", "opacity") if k in i}
            entries.append(Entry(flat_name("gfx", arch, i["name"]), "gfx-image", arch, i["name"], i["file"], i["w"], i["h"], extra))
        for n in inv.get("anims", []):
            ext = n.get("ext", ".webp")
            at = n.get("at")
            for k, frame in enumerate(n["frames"]):
                extra = {"frame": k, "frames": n["frames"], "dir": n["dir"], "ext": ext, "opacity": n.get("opacity", 255)}
                if at and len(at) == 2 * len(n["frames"]):
                    extra["at"] = at
                entries.append(Entry(flat_name("gfx", arch, f"{n['name']}/{frame}"), "gfx-anim", arch, n["name"],
                                     f"{n['dir']}/{frame}{ext}", n["w"], n["h"], extra))
    return entries


def export(out: Path, sets: list[str], limit: int | None, data: Path, root: Path) -> int:
    out.mkdir(parents=True, exist_ok=True)
    entries = collect(sets, data)
    if limit:
        entries = entries[:limit]
    written, missing = [], []
    for e in entries:
        src = root / ("ui" if e.kind == "ui" else "gfx") / e.file
        if not src.exists():
            missing.append(str(src))
            continue
        with Image.open(src) as im:
            im = im.convert("RGBA")
            im.save(out / e.flat)
            written.append({"flat": e.flat, "kind": e.kind, "archive": e.archive, "name": e.name, "file": e.file, "w": e.w, "h": e.h,
                            "fileW": im.width, "fileH": im.height, **e.extra})
    (out / MANIFEST).write_text(json.dumps({"version": 1, "sets": sets, "entries": written}, ensure_ascii=False, indent=1),
                                encoding="utf-8")
    print(f"выгружено {len(written)} кадров в {out} (наборы: {', '.join(sets)}); нет файла: {len(missing)}")
    for m in missing[:20]:
        print(f"  нет файла: {m}")
    return 0


def fit(src: Image.Image, up: Image.Image) -> tuple[int | None, str]:
    """Множитель апскейла: целый, одинаковый по сторонам, ≥ 2; разница до 1 точки на сторону подгоняется. None — не годится."""
    sx, sy = up.width / src.width, up.height / src.height
    scale = round(sx)
    if scale < 2 or round(sy) != scale:
        return None, f"не тот размер {up.width}x{up.height} при исходнике {src.width}x{src.height} (×{sx:.2f}, ×{sy:.2f})"
    if abs(up.width - scale * src.width) > 1 or abs(up.height - scale * src.height) > 1:
        return None, f"пропорции {up.width}x{up.height} не ×{scale} от {src.width}x{src.height}"
    return scale, ""


def with_alpha(src: Image.Image, up: Image.Image) -> tuple[Image.Image, bool]:
    """Прозрачность исходника на апскейле: Topaz отдаёт RGB — альфа исходника, увеличенная, ставится поверх. True — восстановлена."""
    up = up.convert("RGBA")
    alpha = src.getchannel("A")
    if alpha.getextrema() == (255, 255):
        return up, False
    if up.getchannel("A").getextrema() != (255, 255):
        return up, False   # своя прозрачность у апскейла есть — её и берём
    up.putalpha(alpha.resize(up.size, Image.Resampling.LANCZOS))
    return up, True


def short_hash(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()[:16]


def load_json(path: Path, default):
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else default


def import_one(e: dict, export_dir: Path, upscaled: Path, mod: Path) -> tuple[dict | None, list[str], bool]:
    """Один кадр: проверка множителя, прозрачность, запись в слой. (запись описи или None, замечания, восстановлена ли альфа)."""
    if not kept(e):
        return None, [], False
    up_path = upscaled / e["flat"]
    if not up_path.exists():
        return None, [f"нет апскейла: {e['flat']}"], False
    with Image.open(export_dir / e["flat"]) as s, Image.open(up_path) as u:
        src, up = s.convert("RGBA"), u.copy()
    scale, why = fit(src, up)
    if scale is None:
        return None, [f"{e['flat']}: {why}"], False
    exact = (scale * src.width, scale * src.height)
    if up.size != exact:
        up = up.resize(exact, Image.Resampling.LANCZOS)
    up, fixed = with_alpha(src, up)
    kind_root = "ui" if e["kind"] == "ui" else "gfx"
    rel = str(Path(e["file"]).with_suffix(".png")).replace("\\", "/")
    target = mod / "assets" / kind_root / rel
    target.parent.mkdir(parents=True, exist_ok=True)
    up.save(target, compress_level=1)   # игра берёт DDS (--compress); PNG — запасной и источник сжатия: быстрее записи, чем мельче файла
    notes = []
    if e["kind"] != "ui" and "at" not in e and (e["fileW"], e["fileH"]) == (e["w"], e["h"]):
        notes.append(f"{e['flat']}: в описи нет угла обрезки — в память идёт весь холст ×{scale}")
    return {**e, "modFile": rel, "scale": scale, "sha256": short_hash(target)}, notes, fixed


def _import_chunk(args: tuple[list[dict], Path, Path, Path]) -> list[tuple[dict | None, list[str], bool]]:
    entries, export_dir, upscaled, mod = args
    return [import_one(e, export_dir, upscaled, mod) for e in entries]


def import_layer(export_dir: Path, upscaled: Path, mod: Path, mod_id: str, jobs: int = 1) -> int:
    manifest = json.loads((export_dir / MANIFEST).read_text(encoding="utf-8"))
    done: list[dict] = []
    report: list[str] = []
    restored = 0
    entries = manifest["entries"]
    if jobs > 1:
        # Кадры — пачками по процессам (PNG ×4 — сотни мегабайт на кадр анимации разом); опись пишет главный процесс.
        from concurrent.futures import ProcessPoolExecutor
        size = max(1, len(entries) // (jobs * 8))
        chunks = [(entries[i:i + size], export_dir, upscaled, mod) for i in range(0, len(entries), size)]
        with ProcessPoolExecutor(max_workers=jobs) as pool:
            results = [r for part in pool.map(_import_chunk, chunks) for r in part]
    else:
        results = [import_one(e, export_dir, upscaled, mod) for e in entries]
    for entry, notes, fixed in results:
        report.extend(notes)
        restored += fixed
        if entry is not None:
            done.append(entry)
    write_inventories(mod, mod_id, done, report)
    lines = [f"в слой {mod}: {len(done)} кадров, прозрачность восстановлена у {restored}; замечаний {len(report)}", *report]
    (mod / "import-report.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines[:60]))
    return 0


def write_inventories(mod: Path, mod_id: str, done: list[dict], report: list[str]) -> None:
    mod.mkdir(parents=True, exist_ok=True)
    if not (mod / "mod.json").exists():
        (mod / "mod.json").write_text(json.dumps({"id": mod_id, "name": "HD-графика (Topaz)",
                                                  "description": "Апскейл графики по описи: tools/hd/hd_layer.py (Р19)."},
                                                 ensure_ascii=False, indent=2), encoding="utf-8")
    # Интерфейс: опись мода — кадры с множителем, логические размеры прежние; повторный импорт заменяет кадр.
    ui_path = mod / "data" / "ui" / "assets.json"
    ui = {f"{a['archive']}/{a['name']}": a for a in load_json(ui_path, [])}
    for e in (d for d in done if d["kind"] == "ui"):
        entry = {"name": e["name"], "archive": e["archive"], "file": e["modFile"], "w": e["w"], "h": e["h"], "sha256": e["sha256"],
                 "scale": e["scale"]}
        if "anim" in e:
            entry["anim"] = e["anim"]
        ui[f"{e['archive']}/{e['name']}"] = entry
    if ui:
        ui_path.parent.mkdir(parents=True, exist_ok=True)
        ui_path.write_text(json.dumps(list(ui.values()), ensure_ascii=False, indent=1), encoding="utf-8")
    # Графика мира: по архивам; анимация — только целиком и одним множителем.
    by_archive: dict[str, list[dict]] = {}
    for e in done:
        if e["kind"] != "ui":
            by_archive.setdefault(e["archive"], []).append(e)
    for archive, items in by_archive.items():
        path = mod / "data" / "gfx" / f"{Path(archive).stem}.json"
        inv = load_json(path, {"archive": archive, "images": [], "anims": []})
        images = {i["name"]: i for i in inv["images"]}
        anims = {n["name"]: n for n in inv["anims"]}
        for e in (x for x in items if x["kind"] == "gfx-image"):
            image = {"name": e["name"], "file": e["modFile"], "w": e["w"], "h": e["h"], "sha256": e["sha256"], "scale": e["scale"]}
            if "at" in e:
                image["at"] = e["at"]
            if e.get("opacity", 255) != 255:
                image["opacity"] = e["opacity"]
            images[e["name"]] = image
        frames: dict[str, list[dict]] = {}
        for e in (x for x in items if x["kind"] == "gfx-anim"):
            frames.setdefault(e["name"], []).append(e)
        for name, got in frames.items():
            first = got[0]
            scales = {g["scale"] for g in got}
            if len(got) != len(first["frames"]) or len(scales) != 1:
                # Частичную анимацию не оставлять: записанные кадры — прочь, прежняя запись описи (другой множитель) — тоже.
                for g in got:
                    (mod / "assets" / "gfx" / g["modFile"]).unlink(missing_ok=True)
                anims.pop(name, None)
                report.append(f"анимация {archive}/{name}: кадров {len(got)} из {len(first['frames'])}, множители {sorted(scales)} — пропущена")
                continue
            anim = {"name": name, "dir": first["dir"], "ext": ".png", "w": first["w"], "h": first["h"], "frames": first["frames"],
                    "sha256": hashlib.sha256("".join(g["sha256"] for g in sorted(got, key=lambda g: g["frame"])).encode()).hexdigest()[:16],
                    "scale": scales.pop()}
            if "at" in first:
                anim["at"] = first["at"]
            if first.get("opacity", 255) != 255:
                anim["opacity"] = first["opacity"]
            anims[name] = anim
        inv["images"], inv["anims"] = list(images.values()), list(anims.values())
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(inv, ensure_ascii=False, indent=1), encoding="utf-8")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    ex = sub.add_parser("export")
    ex.add_argument("out", type=Path)
    ex.add_argument("--sets", default="ui,faces")
    ex.add_argument("--limit", type=int)
    ex.add_argument("--data", type=Path, default=REPO / "data")
    im = sub.add_parser("import")
    im.add_argument("export_dir", type=Path)
    im.add_argument("upscaled", type=Path)
    im.add_argument("--mod", type=Path, default=REPO / "mods" / "hd")
    im.add_argument("--id", default="hd")
    im.add_argument("--compress", action="store_true")
    im.add_argument("--jobs", type=int, default=max(1, (os.cpu_count() or 2) // 2),
                    help="процессов разбора и сжатия (по умолчанию — половина ядер)")
    a = parser.parse_args(argv)
    if a.command == "export":
        sets = [s.strip() for s in a.sets.split(",") if s.strip()]
        unknown = [s for s in sets if s not in SETS]
        if unknown:
            parser.error(f"нет наборов {unknown}; есть: {', '.join(SETS)}")
        return export(a.out, sets, a.limit, a.data, assets_root())
    code = import_layer(a.export_dir, a.upscaled, a.mod, a.id, a.jobs)
    return compress(a.mod, a.jobs) if code == 0 and a.compress else code


def compress(mod: Path, jobs: int = 1) -> int:
    """DDS BC7 с mip-уровнями рядом с каждым PNG слоя — Godot headless (godot/bench/hd_compress.gd), jobs процессов, у каждого своя
    часть файлов (--part k/n); уже сжатые и не старые — пропуск."""
    godot = os.environ.get("GODOT", r"C:\Tools\bin\godot.cmd")
    procs = [subprocess.Popen([godot, "--headless", "--path", str(REPO / "godot"), "--script", "res://bench/hd_compress.gd", "--",
                               str(mod.resolve()), "--part", f"{k}/{jobs}"], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                              encoding="utf-8", errors="replace") for k in range(max(1, jobs))]
    code = 0
    for proc in procs:
        out, _ = proc.communicate()
        print("\n".join(line for line in (out or "").splitlines() if "hd_compress" in line))
        code = max(code, proc.returncode)
    return code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
