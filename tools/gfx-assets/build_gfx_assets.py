#!/usr/bin/env python3
"""build_gfx_assets.py — графика карты и боя (Imgs/*.ff) в WebP без потерь с прозрачностью + опись для порта.

Как tools/ui-assets для интерфейса, но для остальных архивов игры: земля, объекты и отряды на карте, фоны и
модели боя, заклинания, лица, значки. Порт в рантайме формата .ff (MQDB) не знает.

  assets/gfx/<Архив>/frames/<ИМЯ>.webp         — отдельные кадры (RGBA, ключ уже вырезан)
  assets/gfx/<Архив>/anims/<АНИМ>/NNN_Х.webp   — кадры анимаций по порядку (RGBA)
  assets/gfx/<Архив>/plain/<ИМЯ>.webp          — самостоятельные картинки (земля, лица, значки; RGBA)
  assets/gfx/<Архив>/manifest.json             — разбор архива (кадры, куски, opacity) для режима --opacity
  data/gfx/<Архив>.json                         — опись архива, она и коммитится

Отличия от интерфейсного конвейера:

* **Прозрачность вырезается при записи каждого кадра** (tools/ui-assets/keyed_webp.py): маджента — ключ прозрачности
  игры — сразу становится альфой 0 (и цвет обнуляется). Файла с ключом на диске не бывает; порт ключа не знает.
* **WebP без потерь**, сборка во временный `assets/gfx.new` и подмена готового каталога переименованием в конце:
  прерванная сборка (или запуск игры посреди неё) оставляет прежнюю целую графику.
* **Листы-атласы** (из них движок режет кадры) на диск не пишутся: порт берёт готовые кадры. Самостоятельные
  картинки (у Ground, Faces, GrBorder, IconBld, PalMap кадров нет вовсе) — пишутся и входят в опись.
* **Анимация — одна запись описи**, со списком имён кадров по порядку и расширением файлов (`ext`): в BatUnits 4912
  анимаций и 65 тысяч кадров, опись по кадру раздула бы файл в git до мегабайтов.
* **Хеш — от пикселей**, а не от байтов файла: опись не зависит от кодека и его настроек.
* **Кадр — только занятая часть холста** (Г19, `docs/perf-log.md`): кадры карты и боя — холсты 320×320…800×600 вокруг якоря, занято в
  среднем ~12 % точек. Если занятый прямоугольник (альфа не 0) не больше половины холста, пишется только он, а в опись — угол `at`
  (картинка — `[x, y]`, анимация — `[x0, y0, x1, y1, …]` по кадрам); размер холста — прежние `w`, `h`, хеш — от холста. Правило то же,
  что у рантайма (`GfxTextures.Trim`, Г7): текстура на экране та же, только без разбора холста и поиска занятой части. У анимации с кадрами
  разного холста (одна в IsoSpell) обрезки нет и поля `at` нет — её кадры, как и картинки модов без `at`, обрезает рантайм.
* **Смешение кадра — поле `opacity`** (из `-IMAGES.OPT`, пишется только когда не 255): 1…254 — ровно половина (у движка
  DirectDraw — `⌊d/2⌋+⌊s/2⌋`, значение не важно), 300 — сложение с насыщением, 400 — вычитание, 0 — не рисуется
  (`docs/ai-screen.md` §13). У анимации все кадры с одной `opacity` — поле одно на запись.

Игра — из D2_GAME_DIR (по умолчанию game-pristine рядом с репозиторием моддинга), выход — D2_ASSETS_DIR/gfx (по умолчанию
assets/gfx репозитория). Архивы разбираются в главном процессе (только оглавление), кадры собираются и кодируются пулом
процессов на все ядра кусками по CHUNK; архив процессы читают через отображение файла (страницы общие).

Запуск:
  python tools/gfx-assets/build_gfx_assets.py                  # все архивы Imgs
  python tools/gfx-assets/build_gfx_assets.py Battle IsoTerrn  # выборочно (опись и каталоги остальных не трогаются)
  python tools/gfx-assets/build_gfx_assets.py --opacity        # только дописать opacity в опись по манифестам разбора
                                                                # (D2_ASSETS_DIR/gfx или assets/gfx), картинки не трогаются
"""
from __future__ import annotations

import hashlib
import json
import os
import sys
import time
from concurrent.futures import ProcessPoolExecutor
from dataclasses import asdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "ui-assets"))
from ff_extract import FFArchive  # noqa: E402  (копия в tools/ui-assets, см. NOTICE там)
from keyed_webp import EXT, SHA_LEN, Done, Item, fresh_dir, recover, render, replace_text, swap_dir, verify  # noqa: E402

DEFAULT_GAME_DIR = Path(__file__).resolve().parents[3] / "disciples-modding" / "game-pristine"  # рядом с репозиторием
CHUNK = 256          # кадров на задачу пула: мельче — больше накладных, крупнее — пул простаивает в конце
OPAQUE = 255


def plan(path: Path) -> tuple[list[Item], dict, frozenset[str]]:
    """Что записать из архива (без разбора картинок), манифест разбора и пути кадров к обрезке по занятой части: самостоятельные картинки
    (не листы-атласы), кадры вне анимаций, кадры анимаций по порядку. Кадры без листа или с нулевым размером пропускаются, как у ff_extract.
    Не обрезаются кадры анимаций с разным холстом: холст у записи анимации один (`w`, `h`)."""
    ff = FFArchive(path)
    stem = path.stem
    sheets = ff.base_sheets()
    in_anims = {n for names in ff.anims.values() for n in names}
    items: list[Item] = []
    for e in ff.entries:
        if e.name.upper().endswith(".PNG") and e.name not in sheets:
            items.append(("plain", e.name, f"{stem}/plain/{Path(e.name).stem}{EXT}"))
    built = {n: fr for n, fr in ff.frames.items() if ff.buildable(fr)}
    for name in built:
        if name not in in_anims:
            items.append(("frame", name, f"{stem}/frames/{name}{EXT}"))
    keep: set[str] = set()
    for anim, names in ff.anims.items():
        canvases = {(built[n].width, built[n].height) for n in names if n in built}
        for i, n in enumerate(names):
            if n in built:
                rel = f"{stem}/anims/{anim}/{i:03d}_{n}{EXT}"
                items.append(("frame", n, rel))
                if len(canvases) > 1:
                    keep.add(rel)
    meta = {
        "archive": path.name,
        "entries": [asdict(e) for e in ff.entries],
        "frames": {n: {"base": fr.base_file_name, "w": fr.width, "h": fr.height,
                       "transparent_index": fr.transparent_index, "opacity": fr.opacity,
                       "pieces": [asdict(p) for p in fr.pieces]} for n, fr in built.items()},
        "anims": ff.anims,
        "base_sheets": sorted(sheets),
    }
    return items, meta, frozenset(rel for _, _, rel in items if rel not in keep)


def with_opacity(doc: dict, meta: dict) -> dict:
    """Опись с полем opacity (не 255) у кадров и анимаций по манифесту разбора (frames: имя → {opacity}, anims: имя → кадры)."""
    frames = meta.get("frames") or {}
    anims = meta.get("anims") or {}

    def mark(record: dict, opacity: int | None) -> dict:
        record = {k: v for k, v in record.items() if k != "opacity"}
        if opacity is not None and opacity != OPAQUE:
            record["opacity"] = opacity
        return record

    images = [mark(r, (frames.get(r["name"]) or {}).get("opacity")) for r in doc["images"]]
    out_anims = []
    for r in doc["anims"]:
        names = anims.get(r["name"]) or []
        values = {(frames.get(n) or {}).get("opacity") for n in names}
        if len(values) > 1:   # поле одно на анимацию — разные кадры сломали бы смешение молча
            print(f"ВНИМАНИЕ: {doc['archive']} {r['name']}: у кадров разная opacity {sorted(v for v in values if v is not None)}",
                  file=sys.stderr)
        out_anims.append(mark(r, (frames.get(names[0]) or {}).get("opacity") if names else None))
    return {**doc, "images": images, "anims": out_anims}


def annotate_opacity(data: Path, out_root: Path, wanted: set[str]) -> int:
    """Режим --opacity: дописать поле в существующие описи, не распаковывая архивы заново."""
    marked = 0
    for path in sorted(data.glob("*.json")):
        stem = path.stem
        meta_path = out_root / stem / "manifest.json"
        if (wanted and stem.lower() not in wanted) or not meta_path.exists():
            continue
        doc = json.loads(path.read_text(encoding="utf-8"))
        doc = with_opacity(doc, json.loads(meta_path.read_text(encoding="utf-8")))
        replace_text(path, manifest_text(doc))
        n = sum("opacity" in r for r in doc["images"] + doc["anims"])
        marked += n
        print(f"{stem}: с opacity {n}")
    print(f"всего с opacity: {marked}")
    return 0


def describe(stem: str, meta: dict, items: list[Item], done: dict[str, Done]) -> tuple[dict, int]:
    """Опись архива по записанным кадрам: картинки по имени файла, анимации по каталогу, кадры по номеру."""
    keyed = sum(done[rel][4] for _, _, rel in items)
    images = []
    by_anim: dict[str, list[str]] = {}
    for _, _, rel in items:
        parts = rel.split("/")
        if parts[1] == "anims":
            by_anim.setdefault(parts[2], []).append(rel)
        else:
            images.append(rel)
    records = []
    # Порядок — как у прежней описи: картинки по имени файла, при равном имени кадр (frames) раньше самостоятельной картинки
    # (plain; в словаре порта побеждает последняя); анимации и кадры — без учёта регистра, как сортировались пути Windows.
    for rel in sorted(images, key=lambda r: (r.rsplit("/", 1)[1], r.split("/")[1] == "plain")):
        _, w, h, sha, _, at = done[rel]
        record = {"name": Path(rel).stem, "file": rel, "w": w, "h": h, "sha256": sha}
        if at is not None:
            record["at"] = list(at)
        records.append(record)
    anims = []
    for anim in sorted(by_anim, key=str.lower):
        frames = sorted(by_anim[anim], key=str.lower)
        digest = hashlib.sha256()
        for f in frames:
            digest.update(done[f][3].encode())
        first = done[frames[0]]
        record = {"name": anim, "dir": f"{stem}/anims/{anim}", "ext": EXT, "w": first[1], "h": first[2],
                  "frames": [Path(f).stem for f in frames], "sha256": digest.hexdigest()[:SHA_LEN]}
        if all(done[f][5] is not None for f in frames):
            record["at"] = [c for f in frames for c in done[f][5]]
        anims.append(record)
    doc = {"archive": meta["archive"], "images": records, "anims": anims}
    return with_opacity(doc, meta), keyed


def manifest_text(doc: dict) -> str:
    """По записи на строку: обычный JSON, но читаемый diff в git."""
    lines = [f'{{"archive":{json.dumps(doc["archive"])},', '"images":[']
    lines.append(",\n".join(json.dumps(r, ensure_ascii=False, separators=(",", ":")) for r in doc["images"]))
    lines.append('],\n"anims":[')
    lines.append(",\n".join(json.dumps(r, ensure_ascii=False, separators=(",", ":")) for r in doc["anims"]))
    lines.append("]}\n")
    return "\n".join(lines)


def main(argv: list[str]) -> int:
    game = Path(os.environ.get("D2_GAME_DIR") or DEFAULT_GAME_DIR)
    repo = Path(__file__).resolve().parents[2]
    assets = os.environ.get("D2_ASSETS_DIR")
    out_root = Path(assets) / "gfx" if assets else repo / "assets" / "gfx"
    data = repo / "data" / "gfx"
    if "--opacity" in argv:
        return annotate_opacity(data, out_root, {a.lower() for a in argv[1:] if not a.startswith("--")})
    wanted = {a.lower() for a in argv[1:]}
    archives = sorted(p for p in (game / "Imgs").glob("*.ff") if not wanted or p.stem.lower() in wanted)
    if not archives:
        print(f"нет архивов в {game / 'Imgs'}", file=sys.stderr)
        return 1

    recover(out_root)
    for p in archives:
        recover(out_root / p.stem)   # выборочная сборка подменяет каталоги архивов по одному
    new_root = fresh_dir(out_root.with_name(out_root.name + ".new"))
    data.mkdir(parents=True, exist_ok=True)
    t0 = time.perf_counter()
    plans = {p.stem: (p, *plan(p)) for p in archives}
    jobs = []
    # Самые большие архивы — первыми: пул не простаивает в конце на одном BatUnits.
    for _, (path, items, _, trim) in sorted(plans.items(), key=lambda kv: -len(kv[1][1])):
        for i in range(0, len(items), CHUNK):
            chunk = items[i:i + CHUNK]
            jobs.append((str(path), str(new_root), chunk, frozenset(rel for _, _, rel in chunk if rel in trim)))
    total = sum(len(j[2]) for j in jobs)
    print(f"разобрано оглавлений: {len(plans)}, кадров к записи {total}, задач {len(jobs)}, {time.perf_counter() - t0:.0f} с",
          flush=True)

    done: dict[str, Done] = {}
    with ProcessPoolExecutor(max_workers=os.cpu_count() or 4) as pool:
        for n, part in enumerate(pool.map(render, jobs), 1):
            done.update((r[0], r) for r in part)
            if n % 100 == 0:
                print(f"  {len(done)}/{total}, {time.perf_counter() - t0:.0f} с", flush=True)
    print(f"записано: {len(done)} картинок, {time.perf_counter() - t0:.0f} с", flush=True)
    verify(new_root, done)

    texts = {}
    total_images = total_frames = 0
    for stem, (_, items, meta, _) in sorted(plans.items()):
        doc, keyed = describe(stem, meta, items, done)
        (new_root / stem).mkdir(exist_ok=True)
        (new_root / stem / "manifest.json").write_text(json.dumps(meta, indent=1), encoding="utf-8")
        texts[stem] = manifest_text(doc)
        frames = sum(len(a["frames"]) for a in doc["anims"])
        total_images += len(doc["images"])
        total_frames += frames
        print(f"{doc['archive']}: картинок {len(doc['images'])}, анимаций {len(doc['anims'])} ({frames} кадров), "
              f"точек ключа {keyed}")

    # Подмена: весь каталог, если собраны все архивы игры; иначе — каталоги собранных архивов по одному.
    if not wanted:
        swap_dir(new_root, out_root)
    else:
        out_root.mkdir(parents=True, exist_ok=True)
        for stem in plans:
            swap_dir(new_root / stem, out_root / stem)
        new_root.rmdir()
    for stem, text in texts.items():
        replace_text(data / f"{stem}.json", text)
    print(f"всего: картинок {total_images}, кадров анимаций {total_frames}, {time.perf_counter() - t0:.0f} с")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
