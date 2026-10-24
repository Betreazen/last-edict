#!/usr/bin/env python3
"""hd_layer.py — HD-графика через Topaz (Р19, Э5): выгрузка в одну плоскую папку и обратный импорт слоем мода.

  py tools/hd/hd_layer.py export <папка> [--sets ui,faces]
      Выбранные наборы графики (PNG) — в одну папку без подпапок, имена плоские и уникальные (путь зашит в имя:
      ui__Interf__DLG_MAIN_MENU_BG.png, gfx__Faces__G000UU0019FACEB.png, gfx__IsoUnit__G000UU0001MOVE~000_A.png),
      рядом manifest.json: плоское имя → исходный кадр, логический размер, угол обрезки. Кадры, что в слой не идут (земля, туман,
      дороги — kept), не выгружаются.
  py tools/hd/hd_layer.py import <папка выгрузки> <папка апскейла> [--mod mods/hd] [--compress] [--jobs N]
      Результат Topaz (те же имена) → слой мода: файлы в assets/ui и assets/gfx мода, описи data/ui/assets.json и
      data/gfx/<Архив>.json с множителем scale (логические размеры и углы — прежние), mod.json. Сверяет: множитель целый
      и одинаковый по сторонам (до 1 точки разницы — подгоняется), прозрачность (Topaz отдаёт RGB — альфа берётся с исходника,
      увеличенная). Отчёт — import-report.txt в моде и на экран. Анимация берётся только целиком (все кадры).
      Повторный импорт: кадр, записанный в слой позже своего апскейла, не пересчитывается (прерванный прогон продолжается); кадр,
      который не читается, — замечание, а не остановка; записи и файлы того, что в слой больше не идёт (kept), — вычищаются.
      --compress — сжать слой: рядом с каждым PNG — DDS BC7 с mip-уровнями (godot/bench/hd_compress.gd, нужен Godot: GODOT или
      C:/Tools/bin/godot.cmd); игра берёт DDS в текстуру, в видеопамяти ≈ ×0,33 от RGBA. --jobs N — процессов разбора и сжатия
      (по умолчанию половина ядер; вся графика ×4 — десятки гигабайт).

Наборы — порядок апскейла из docs/own-version-roadmap.md §3.3: ui (интерфейс), faces (портреты, лорды), battle-bg (поле боя),
capital (столица, город), icons (значки, события, вор), map (карта: объекты, мини-карта, курсоры, заклинания), sprites (юниты
боя и карты — самые большие). Корень графики — D2_ASSETS_DIR или assets/ рядом с репозиторием; описи — data/ui, data/gfx импорта
(D2_DATA_DIR или корень репозитория). Выгрузка, листы и слой — файлы игры (Р3): внутри репозитория — только в местах, которые git
не берёт (guard_output).
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import subprocess
import sys
from concurrent.futures import ProcessPoolExecutor
from concurrent.futures.process import BrokenProcessPool
from dataclasses import dataclass
from functools import partial
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


def data_root() -> Path:
    """Описи импорта: D2_DATA_DIR (корень импорта, как у игры) или корень репозитория — их каталог data/."""
    env = os.environ.get("D2_DATA_DIR")
    return (Path(env) if env else REPO) / "data"


def guard_output(path: Path) -> None:
    """Р3: файлы графики игры (выгрузка, листы, слой) внутри репозитория — только там, где их не возьмёт git (`git check-ignore`)."""
    target = path.resolve()
    try:
        target.relative_to(REPO)
    except ValueError:
        return
    # Файл внутри: правило каталога (mods/hd*/) git применяет к пути, только если знает, что это каталог, — а его ещё может не быть.
    probe = target / "probe.png"
    ignored = subprocess.run(["git", "-C", str(REPO), "check-ignore", "-q", str(probe)], capture_output=True).returncode == 0
    if not ignored:
        raise SystemExit(f"{path}: внутри репозитория и не в .gitignore — графика игры попала бы в git (Р3); "
                         "выберите папку вне репозитория или под mods/hd*/")


def inside(base: Path, rel: str) -> Path:
    """Путь из описи — только внутри base (опись — данные пользователя: «..» и абсолютный путь вывели бы запись наружу)."""
    path = (base / rel).resolve()
    path.relative_to(base.resolve())
    return path


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
    # В слой не идут — не выгружать: апскейл их всё равно отбросил бы (kept).
    return [e for e in entries if kept({"archive": e.archive, "name": e.name})]


def export(out: Path, sets: list[str], data: Path, root: Path) -> int:
    guard_output(out)
    out.mkdir(parents=True, exist_ok=True)
    entries = collect(sets, data)
    # Плоские имена — одна папка: совпадение (в том числе регистром — на NTFS это один файл) затёрло бы кадр молча.
    seen: dict[str, Entry] = {}
    for e in entries:
        if (other := seen.setdefault(e.flat.lower(), e)) is not e:
            raise SystemExit(f"плоские имена совпадают: {other.flat} и {e.flat}")
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


def short_hash(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()[:16]


def load_json(path: Path, default):
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else default


def mod_file(e: dict) -> str:
    """Файл кадра в слое (от assets/ui или assets/gfx мода): PNG вместо исходного расширения."""
    return str(Path(e["file"]).with_suffix(".png")).replace("\\", "/")


def import_one(e: dict, export_dir: Path, upscaled: Path, mod: Path) -> tuple[dict | None, list[str], bool]:
    """Один кадр: проверка множителя, прозрачность, запись в слой. (запись описи или None, замечания, восстановлена ли альфа).
    Кадр, что не читается (оборванный файл апскейлера), — замечание: прогон на сотню тысяч кадров из-за него не встаёт."""
    try:
        return _import_one(e, export_dir, upscaled, mod)
    except (OSError, ValueError) as error:
        return None, [f"{e['flat']}: не принят — {type(error).__name__}: {error}"], False


def _import_one(e: dict, export_dir: Path, upscaled: Path, mod: Path) -> tuple[dict | None, list[str], bool]:
    up_path = inside(upscaled, e["flat"])
    if not up_path.exists():
        return None, [f"нет апскейла: {e['flat']}"], False
    kind_root = "ui" if e["kind"] == "ui" else "gfx"
    rel = mod_file(e)
    target = inside(mod / "assets" / kind_root, rel)
    if target.exists() and target.stat().st_mtime >= up_path.stat().st_mtime:
        # Уже в слое и не старше апскейла (прерванный прогон): множитель — по размерам файлов, без пересчёта точек.
        with Image.open(inside(export_dir, e["flat"])) as s, Image.open(target) as t:
            scale = t.width // s.width
        return {**e, "modFile": rel, "scale": scale, "sha256": short_hash(target.read_bytes())}, [], False
    with Image.open(inside(export_dir, e["flat"])) as s, Image.open(up_path) as u:
        src, up = s.convert("RGBA"), u.copy()
    scale, why = fit(src, up)
    if scale is None:
        return None, [f"{e['flat']}: {why}"], False
    exact = (scale * src.width, scale * src.height)
    if up.size != exact:
        up = up.resize(exact, Image.Resampling.LANCZOS)
    up, fixed = with_alpha(src, up)
    target.parent.mkdir(parents=True, exist_ok=True)
    # Игра берёт DDS (--compress); PNG — запасной и источник сжатия: быстрее записи, чем мельче файла. Хэш — по тем же байтам, без
    # перечитывания файла; запись — через временный файл: оборванная не выглядит готовым кадром.
    buffer = io.BytesIO()
    up.save(buffer, format="PNG", compress_level=1)
    tmp = target.with_name(target.name + ".part")
    tmp.write_bytes(buffer.getvalue())
    tmp.replace(target)
    target.with_suffix(".dds").unlink(missing_ok=True)   # сжатая копия прежнего кадра больше не годится
    notes = []
    if e["kind"] != "ui" and "at" not in e and (e["fileW"], e["fileH"]) == (e["w"], e["h"]):
        notes.append(f"{e['flat']}: в описи нет угла обрезки — в память идёт весь холст ×{scale}")
    return {**e, "modFile": rel, "scale": scale, "sha256": short_hash(buffer.getvalue())}, notes, fixed


def import_layer(export_dir: Path, upscaled: Path, mod: Path, mod_id: str, jobs: int = 1) -> int:
    guard_output(mod)
    manifest = json.loads((export_dir / MANIFEST).read_text(encoding="utf-8"))
    report: list[str] = []
    # Кадр, что в слой не идёт, и повтор одного файла (анимация, что повторяет кадр) — не считать: два процесса писали бы один файл.
    entries, files = [], set()
    for e in manifest["entries"]:
        key = ("ui" if e["kind"] == "ui" else "gfx", mod_file(e))
        if kept(e) and key not in files:
            files.add(key)
            entries.append(e)
    results: list[tuple[dict | None, list[str], bool]] = []
    one = partial(import_one, export_dir=export_dir, upscaled=upscaled, mod=mod)
    if jobs > 1:
        # Кадры — пачками по процессам (PNG ×4 — сотни мегабайт на кадр анимации разом); опись пишет главный процесс.
        try:
            with ProcessPoolExecutor(max_workers=jobs) as pool:
                results.extend(pool.map(one, entries, chunksize=max(1, len(entries) // (jobs * 8))))
        except BrokenProcessPool as error:   # процесс убит (память): что успели — в опись, повторный прогон продолжит
            report.append(f"прогон оборван: {error} — повторите import, готовые кадры не пересчитываются")
    else:
        results = [one(e) for e in entries]
    done: list[dict] = []
    restored = 0
    for entry, notes, fixed in results:
        report.extend(notes)
        restored += fixed
        if entry is not None:
            done.append(entry)
    pruned = prune(mod)
    write_inventories(mod, mod_id, done, report)
    lines = [f"в слой {mod}: {len(done)} кадров, прозрачность восстановлена у {restored}; вычищено не идущих в слой {pruned}; "
             f"замечаний {len(report)}", *report]
    (mod / "import-report.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines[:60]))
    if not done:
        print(f"ни одного кадра не принято — проверьте папку апскейла {upscaled}")
        return 1
    return 1 if any(r.startswith("прогон оборван") for r in report) else 0


def remove(path: Path) -> None:
    """Файл слоя и его сжатая копия."""
    path.unlink(missing_ok=True)
    path.with_suffix(".dds").unlink(missing_ok=True)


def prune(mod: Path) -> int:
    """Записи и файлы слоя, что в него больше не идут (kept: земля, туман, дороги — исключены 24.10): слой, импортированный раньше,
    иначе сохранил бы их, и чёрная сетка вернулась бы. Число вычищенных кадров."""
    count = 0
    for path in sorted((mod / "data" / "gfx").glob("*.json")) if (mod / "data" / "gfx").is_dir() else []:
        inv = load_json(path, {})
        archive = inv.get("archive", "")
        images = [i for i in inv.get("images", []) if kept({"archive": archive, "name": i["name"]})]
        anims = [n for n in inv.get("anims", []) if kept({"archive": archive, "name": n["name"]})]
        gone_images = [i for i in inv.get("images", []) if i not in images]
        gone_anims = [n for n in inv.get("anims", []) if n not in anims]
        for i in gone_images:
            remove(inside(mod / "assets" / "gfx", i["file"]))
        for n in gone_anims:
            for frame in n["frames"]:
                remove(inside(mod / "assets" / "gfx", f"{n['dir']}/{frame}{n.get('ext', '.png')}"))
        count += len(gone_images) + sum(len(n["frames"]) for n in gone_anims)
        if not images and not anims:
            path.unlink()
        elif gone_images or gone_anims:
            inv["images"], inv["anims"] = images, anims
            path.write_text(json.dumps(inv, ensure_ascii=False, indent=1), encoding="utf-8")
    return count


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
            if len({g["modFile"] for g in got}) != len(set(first["frames"])) or len(scales) != 1:   # повтор кадра — один файл
                # Частичную анимацию не оставлять: записанные кадры — прочь (с их DDS), прежняя запись описи (другой множитель) — тоже.
                for g in got:
                    remove(inside(mod / "assets" / "gfx", g["modFile"]))
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
    im = sub.add_parser("import")
    im.add_argument("export_dir", type=Path)
    im.add_argument("upscaled", type=Path)
    im.add_argument("--mod", type=Path, default=REPO / "mods" / "hd")
    im.add_argument("--compress", action="store_true")
    im.add_argument("--jobs", type=int, default=max(1, (os.cpu_count() or 2) // 2),
                    help="процессов разбора и сжатия (по умолчанию — половина ядер)")
    a = parser.parse_args(argv)
    if a.command == "export":
        sets = [s.strip() for s in a.sets.split(",") if s.strip()]
        unknown = [s for s in sets if s not in SETS]
        if unknown:
            parser.error(f"нет наборов {unknown}; есть: {', '.join(SETS)}")
        return export(a.out, sets, data_root(), assets_root())
    code = import_layer(a.export_dir, a.upscaled, a.mod, "hd", a.jobs)
    return compress(a.mod, a.jobs) if code == 0 and a.compress else code


def kill_tree(proc: subprocess.Popen) -> None:
    """Снять процесс с потомками: godot.cmd — оболочка cmd.exe, kill() снял бы только её, а Godot остался бы."""
    if sys.platform == "win32":
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)], capture_output=True, check=False)
    else:
        proc.kill()


def compress(mod: Path, jobs: int = 1) -> int:
    """DDS BC7 с mip-уровнями рядом с каждым PNG слоя — Godot headless (godot/bench/hd_compress.gd), jobs процессов, у каждого своя
    часть файлов (--part k/n); уже сжатые и не старые — пропуск. Прерывание (Ctrl+C) снимает процессы Godot; при ошибке — хвост вывода."""
    godot = os.environ.get("GODOT", r"C:\Tools\bin\godot.cmd")
    n = max(1, jobs)
    procs: list[subprocess.Popen] = []
    try:
        for k in range(n):
            procs.append(subprocess.Popen([godot, "--headless", "--path", str(REPO / "godot"), "--script", "res://bench/hd_compress.gd",
                                           "--", str(mod.resolve()), "--part", f"{k}/{n}"], stdout=subprocess.PIPE,
                                          stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace"))
        code = 0
        for proc in procs:
            out, _ = proc.communicate()
            lines = (out or "").splitlines()
            print("\n".join(line for line in lines if "hd_compress" in line))
            if proc.returncode != 0:
                print("\n".join(lines[-20:]))
                code = 1
        return code
    except FileNotFoundError:
        print(f"нет Godot: {godot} (задайте GODOT)")
        return 1
    finally:
        for proc in procs:
            if proc.poll() is None:
                kill_tree(proc)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
