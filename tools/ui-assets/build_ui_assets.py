#!/usr/bin/env python3
"""build_ui_assets.py — интерфейсная графика игры в WebP без потерь + манифест для порта.

Разбирает архивы интерфейса (.ff / MQDB) через ff_extract.py и складывает:

  assets/ui/<Архив>/frames/<ИМЯ>.webp          — собранные кадры (вне git)
  assets/ui/<Архив>/anims/<АНИМ>/NNN_ИМЯ.webp
  data/ui/assets.json                           — манифест, он и коммитится

Манифест — список записей: имя кадра, архив, путь к файлу, размер, короткий sha256 от пикселей RGBA (не от байтов файла:
опись не зависит от кодека). Листы-атласы, из которых режутся кадры, на диск не пишутся — порт их не читает.

Прозрачность вырезается при записи каждого кадра (keyed_webp.py): пурпурный ключ игры (255,0,255 и всё, что в RGB565 движка
равно 0xF81F) сразу становится альфой 0, цвет обнуляется. Файла с ключом на диске не бывает; порт грузит кадры как есть.
Сборка — во временный `ui.new`, готовый каталог встаёт на место переименованием в конце: прерванная сборка оставляет
прежнюю целую графику. Порт по манифесту проверяет, что грузит именно то, что ожидал, а мод кладёт рядом свой
список и накладывает его поверх (D2.Data.Ui.UiAssets.Merge).

Игра берётся из переменной окружения D2_GAME_DIR (по умолчанию — game-pristine рядом с репозиторием моддинга),
выход — D2_ASSETS_DIR/ui (по умолчанию assets/ui репозитория).

Запуск:
  python tools/ui-assets/build_ui_assets.py
"""
from __future__ import annotations

import json
import os
import sys
import time
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from ff_extract import FFArchive  # noqa: E402  (копия рядом, см. NOTICE)
from keyed_webp import EXT, Item, fresh_dir, recover, render, replace_text, swap_dir, verify  # noqa: E402

# Архивы интерфейса. MenuAnim.ff сюда не входит: в нём 52 записи и все — видео .BIK,
# ни одного кадра интерфейса (проверено `ff_extract.py MenuAnim.ff --list`).
UI_ARCHIVES = ("Interf/Interf.ff", "Interf/IntfScen.ff", "Interf/MenuImgs.ff")

DEFAULT_GAME_DIR = Path(__file__).resolve().parents[3] / "disciples-modding" / "game-pristine"  # рядом с репозиторием
CHUNK = 32   # кадров на задачу пула: кадры интерфейса крупные (до 800×600), кусков нужно больше, чем ядер


def plan(path: Path) -> list[tuple[Item, str, str | None]]:
    """Кадры архива к записи: (кадр, имя в описи, анимация или None).

    Кадры анимаций лежат в anims/<АНИМ>/NNN_ИМЯ, и внутриигровые имена у них односимвольные ("A", "0"),
    одинаковые у разных анимаций. Поэтому кадр анимации адресуется как "<АНИМ>/<NNN>": экран ссылается на
    анимацию по имени, а порядок кадров виден из номера."""
    ff = FFArchive(path)
    stem = path.stem
    in_anims = {n for names in ff.anims.values() for n in names}
    built = {n for n, fr in ff.frames.items() if ff.buildable(fr)}
    out: list[tuple[Item, str, str | None]] = []
    for name in sorted(built - in_anims):
        out.append((("frame", name, f"{stem}/frames/{name}{EXT}"), name, None))
    for anim, names in ff.anims.items():
        for i, n in enumerate(names):
            if n in built:
                out.append((("frame", n, f"{stem}/anims/{anim}/{i:03d}_{n}{EXT}"), f"{anim}/{i:03d}", anim))
    return out


def main() -> int:
    game = Path(os.environ.get("D2_GAME_DIR") or DEFAULT_GAME_DIR)
    repo = Path(__file__).resolve().parents[2]
    assets_dir = os.environ.get("D2_ASSETS_DIR")
    assets = Path(assets_dir) / "ui" if assets_dir else repo / "assets" / "ui"
    manifest_path = repo / "data" / "ui" / "assets.json"

    missing = [a for a in UI_ARCHIVES if not (game / a).is_file()]
    if missing:
        print(f"нет архивов в {game}: {', '.join(missing)}", file=sys.stderr)
        return 1

    t0 = time.perf_counter()
    recover(assets)
    new_root = fresh_dir(assets.with_name(assets.name + ".new"))
    plans = {rel: plan(game / rel) for rel in UI_ARCHIVES}
    jobs = []
    for rel, entries in plans.items():
        items = [e[0] for e in entries]
        jobs += [(str(game / rel), str(new_root), items[i:i + CHUNK]) for i in range(0, len(items), CHUNK)]
    done: dict[str, tuple] = {}
    with ProcessPoolExecutor(max_workers=os.cpu_count() or 4) as pool:
        for part in pool.map(render, jobs):
            done.update((r[0], r) for r in part)
    verify(new_root, done)

    rows: list[dict] = []
    for rel, entries in plans.items():
        keyed = 0
        for (_, _, file), name, anim in entries:
            _, w, h, sha, k, _ = done[file]
            keyed += k
            row = {"name": name, "archive": Path(rel).name, "file": file, "w": w, "h": h, "sha256": sha}
            if anim:
                row["anim"] = anim
            rows.append(row)
        print(f"{Path(rel).name}: кадров {len(entries)}, анимаций {len({a for *_, a in entries if a})}, точек ключа {keyed}")

    # Имена кадров в разных архивах повторяются (10 пар Interf/IntfScen — разного размера),
    # поэтому запись опознаётся парой архив+имя, а не одним именем.
    dup = len(rows) - len({(r["archive"], r["name"]) for r in rows})
    if dup:
        print(f"ОШИБКА: {dup} повторов пары архив+имя", file=sys.stderr)
        return 1

    rows.sort(key=lambda r: (r["archive"], r["name"]))
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    swap_dir(new_root, assets)
    # По записи на строку: обычный JSON, но читаемый diff в git.
    body = ",\n".join(json.dumps(r, ensure_ascii=False, separators=(",", ":")) for r in rows)
    replace_text(manifest_path, f"[\n{body}\n]\n")
    print(f"{manifest_path.relative_to(repo).as_posix()}: {len(rows)} кадров, "
          f"{manifest_path.stat().st_size // 1024} КиБ, {time.perf_counter() - t0:.1f} с")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
