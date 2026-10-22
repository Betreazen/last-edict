"""Попиксельная сверка экрана порта со снимком оригинала — числом, а не на глаз (docs/ui-windows.md §10).

    python tools/pixel-compare.py СНИМОК.png --screen DLG_OPTIONS        # порт соберёт кадры экрана сам (headless --png)
    python tools/pixel-compare.py СНИМОК.png --port порт.png             # готовая картинка порта (например, снимок боя --shot)
    python tools/pixel-compare.py --dir ПАПКА                            # все снимки папки с именами экранов: DLG_X.png

Снимок оригинала может быть больше экрана (окно с рамкой, 1024×768): порт ищет лучшее совпадение сдвигом по всему снимку
(сначала грубо по уменьшенным картинкам, затем точно). Подписи в сверку не входят: порт рисует их своим шрифтом, поэтому места
подписей, счётчиков и списков (`Text`, `Spin`, `List`, `TextList`, `Edit` из data/ui/screens.json) закрываются маской. Итог — строка

    pixel DLG_X сдвиг=dx,dy сравнено=N точек отличается=P% средняя_разница=D худшая_область=x,y,w,h маска=M -> diff.png

С `--text` сверяется обратное — сами подписи: порт собирает экран с текстом (сцена без окна — `--png --text`, рецепт `port` — снимок
окна, текст на нём и так есть), и в каждом месте подписи, где текст есть и у порта, и у снимка, считаются точки текста (яркость
отличается от средней по месту больше --ink). Строка на снимок:

    text DLG_X мест=K текста_снимка=N текста_порта=M совпало=T (T/N %) лишних=F IoU=… IoU±2=… отличается=P%

IoU — совпавшие точки текста к их объединению; IoU±2 — то же при лучшем сдвиге каждого места на ±2 точки (форма глифов без учёта
положения строки); «отличается» — доля точек мест подписей с разницей больше порога. По папке в конце — итог по суммам.

Без `--text` «отличается» — точки с разницей больше порога (--threshold, по умолчанию 24 на канал по максимуму), а diff.png — разность
(красным отличия, серым совпадения, синим подписи, фиолетовым маска снимка) в .cache/pixel/. Код выхода 0 — отличается не больше
--max (по умолчанию 2 %).

Состояние снимка — tools/pixel-states/<имя снимка>.json (LastEdict.Data.Ui.UiShotState): порт без окна рисует экран в начальном
состоянии разметки, а снимок — наполненным логикой движка; файл говорит порту те же флажки, счётчики, спрятанные кнопки и фон.
Составной снимок (карта с панелями, --port) — `screens`: [id, x, y] экранов на картинке, их подписи закрываются так же. Там же
`mask` — прямоугольники содержимого, которого порт в этой сверке не рисует (ролик, портрет из партии, курсор мыши на
снимке), каждый с объяснением `why`, и `note` — откуда состояние. Экран, которого порт не собирает (нет в разметке, нет кадров),
в --dir даёт строку «пропуск: порт не собирает» и в итог не идёт.

Рецепт картинки порта — `port` в файле состояния: `{"sg": "20260928-shots/x.sg", "clicks": "city capital", "settings": {...}}` —
экран карты с окном 800×600 открывает сохранение из корпуса (D2_CORPUS_DIR), проходит шаги проверки и снимает картинку (--shot):
так сверяются окна с содержимым из сохранения рядом со снимком (портреты, кукла, склад), составные снимки и бой. --dir обходит
папку с подпапками (прогон `tools/shot_pairs.py`: windows/ и other/); имя снимка — экран (`DLG_X.png`, повтор `DLG_X-f0-<seq>.png`)
или класс составного экрана с рецептом (`CCapitalInterf-<seq>.png`).
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parent.parent
STATES = REPO / "tools" / "pixel-states"
TEXT_KINDS = {"Text", "List", "TextList", "Edit", "Spin"}


class NotBuilt(Exception):
    """Порт не собрал экран: причина — в тексте."""


def load(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGBA"), dtype=np.int16)


def state_of(snapshot: Path) -> Path | None:
    """Файл состояния снимка: tools/pixel-states/<имя снимка>.json."""
    path = STATES / f"{snapshot.stem}.json"
    return path if path.exists() else None


def port_screen(screen: str, state: Path | None = None, text: bool = False) -> Path:
    """Картинка экрана порта: сцена UiScreen в headless складывает кадры экрана в PNG (подписи — только с text)."""
    out = REPO / ".cache" / "pixel" / f"{screen}.port.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        out.unlink()
    exe = os.environ.get("GODOT", r"C:\Tools\bin\godot.cmd")
    args = [exe, "--headless", "--path", str(REPO / "godot"), "res://scenes/ui/UiScreen.tscn", "--", "--no-mods",
            "--screen", screen, "--png", str(out)] + (["--text"] if text else [])
    if state is not None:
        args += ["--state", str(state)]
    proc = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=180)
    if proc.returncode != 0 or not out.exists():
        tail = (proc.stdout + proc.stderr).strip().splitlines()[-3:]
        raise NotBuilt(f"код {proc.returncode}: {' | '.join(tail)}")
    return out


def recipe_of(state: Path | None) -> dict | None:
    """Рецепт картинки порта из файла состояния (`port`) или None — порт соберёт экран сам."""
    return None if state is None else json.loads(state.read_text(encoding="utf-8")).get("port")


def port_map(name: str, recipe: dict) -> Path:
    """Картинка порта по рецепту: экран карты с окном 800×600 — сохранение из корпуса, шаги проверки, снимок вьюпорта."""
    corpus = Path(os.environ.get("D2_CORPUS_DIR", REPO / "corpus"))
    sg = corpus / recipe["sg"]
    if not sg.exists():
        raise NotBuilt(f"нет сохранения {sg} (D2_CORPUS_DIR)")
    out = REPO / ".cache" / "pixel" / f"{name}.port.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        out.unlink()
    exe = os.environ.get("GODOT", r"C:\Tools\bin\godot.cmd")
    args = [exe, "--resolution", "800x600", "--path", str(REPO / "godot"), "res://scenes/map/MapScreen.tscn", "--", "--no-mods",
            "--sg", str(sg), "--check", "--clicks", recipe.get("clicks", ""), "--shot", str(out)]
    if "settings" in recipe:
        settings = REPO / ".cache" / "pixel" / f"{name}.settings.json"
        settings.write_text(json.dumps(recipe["settings"]), encoding="utf-8")
        args += ["--settings", str(settings)]
    proc = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=180)
    if proc.returncode != 0 or not out.exists():
        tail = (proc.stdout + proc.stderr).strip().splitlines()[-3:]
        raise NotBuilt(f"код {proc.returncode}: {' | '.join(tail)}")
    return out


def with_videos(port: Path, state: Path | None, screen: str | None) -> Path:
    """Кадры роликов в места элементов (`video` в файле состояния: {"IMG_END_LOSE": "DEFWIN"}): сцена без окна роликов не
    играет, а на снимке в месте ролика — его последний кадр (эпилоги DEFWIN/DEFLOSE — ролики в один кадр). Кадр — ffmpeg из
    ролика порта (data/video/videos.json, D2_ASSETS_DIR/video)."""
    videos = {} if state is None else json.loads(state.read_text(encoding="utf-8")).get("video", {})
    if state is None or not videos:
        return port
    found = screen_of(screen)
    if found is None:
        raise SystemExit(f"{state.name}: video без экрана")
    catalog = {v["name"]: v for v in json.loads((REPO / "data" / "video" / "videos.json").read_text(encoding="utf-8"))}
    assets = Path(os.environ.get("D2_ASSETS_DIR", REPO / "assets"))
    with Image.open(port) as im:
        image = im.convert("RGBA")
    ox, oy = found.get("Design", {}).get("X", 0), found.get("Design", {}).get("Y", 0)
    for element, name in videos.items():
        rect = next(e["Rect"] for e in found["Elements"] if e["Id"] == element)
        frame = REPO / ".cache" / "pixel" / f"{name}.last.png"
        try:
            subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", str(assets / "video" / catalog[name]["file"]),
                            "-update", "1", str(frame)], check=True, capture_output=True, timeout=120)
        except (OSError, subprocess.SubprocessError) as e:
            raise NotBuilt(f"кадр ролика {name}: {e}") from e
        with Image.open(frame) as fr:
            image.alpha_composite(fr.convert("RGBA"), (rect["X"] - ox, rect["Y"] - oy))
    out = port.with_suffix(".video.png")
    image.save(out)
    return out


def screen_name(snapshot: Path) -> str | None:
    """Экран по имени снимка: `DLG_X.png` и повтор `DLG_X-f0-<seq>.png` — DLG_X; составной (`CCapitalInterf-<seq>`) — None."""
    m = re.fullmatch(r"(DLG_[A-Z0-9_]+?)(?:-f\d+-\d+)?", snapshot.stem)
    return m[1] if m else None


def screen_of(screen: str | None) -> dict | None:
    if not screen:
        return None
    screens = json.loads((REPO / "data" / "ui" / "screens.json").read_text(encoding="utf-8"))
    return next((s for s in screens if s["Id"] == screen), None)


def text_mask(screen: str | None, shape: tuple[int, int], at: tuple[int, int] = (0, 0),
              mask: np.ndarray | None = None) -> np.ndarray:
    """Маска точек, которые сверяются: всё, кроме мест подписей и списков экрана (экран — в точке at картинки)."""
    mask = np.ones(shape, dtype=bool) if mask is None else mask
    found = screen_of(screen)
    if found is None:
        return mask
    ox, oy = found.get("Design", {}).get("X", 0) - at[0], found.get("Design", {}).get("Y", 0) - at[1]
    for e in found.get("Elements", []):
        if e.get("Kind") in TEXT_KINDS:
            r = e["Rect"]
            x, y = r["X"] - ox, r["Y"] - oy
            mask[max(0, y):max(0, y + r["Height"]), max(0, x):max(0, x + r["Width"])] = False
    return mask


def text_rects(screen: str | None, state: Path | None) -> list[tuple[int, int, int, int]]:
    """Места подписей (x, y, w, h в точках картинки порта): экрана и каждого экрана из `screens` файла состояния."""
    placed = [(screen, 0, 0)] if screen else []
    if state is not None:
        placed += [tuple(s) for s in json.loads(state.read_text(encoding="utf-8")).get("screens", [])]
    rects = []
    for name, ax, ay in placed:
        found = screen_of(name)
        if found is None:
            continue
        ox, oy = found.get("Design", {}).get("X", 0) - ax, found.get("Design", {}).get("Y", 0) - ay
        rects += [(e["Rect"]["X"] - ox, e["Rect"]["Y"] - oy, e["Rect"]["Width"], e["Rect"]["Height"])
                  for e in found.get("Elements", []) if e.get("Kind") in TEXT_KINDS]
    return rects


def ink(part: np.ndarray, delta: int) -> np.ndarray:
    """Точки текста места: яркость отличается от средней (медианы) по месту больше delta — и тёмный текст на пергаменте, и светлый
    на тёмном."""
    lum = part[..., :3].mean(axis=2)
    return np.abs(lum - np.median(lum)) > delta


def text_compare(ref_path: Path, port_path: Path, screen: str | None, state: Path | None, threshold: int, delta: int,
                 shifts: bool = False) -> dict:
    """Сверка подписей: точки текста снимка и порта в местах подписей, где текст есть у обоих (см. --text в описании). С shifts —
    строка на место: лучший сдвиг текста порта к снимку (dx, dy в ±3 точки) — по ней видно, что строка стоит выше или ниже."""
    ref, port = load(ref_path), load(port_path)
    shots, _ = shot_mask(state, port.shape[:2])
    dx, dy = align(ref, port, composite_mask(state, port.shape[:2], text_mask(screen, port.shape[:2])) & shots)
    h, w = port.shape[:2]
    part = ref[dy:dy + h, dx:dx + w]
    total = {"rects": 0, "ref": 0, "port": 0, "tp": 0, "fp": 0, "best": 0, "best_union": 0, "cmp": 0, "bad": 0}
    for x, y, rw, rh in text_rects(screen, state):
        x0, y0, x1, y1 = max(0, x), max(0, y), min(w, x + rw), min(h, y + rh)
        if x1 - x0 < 3 or y1 - y0 < 3 or not shots[y0:y1, x0:x1].all():
            continue
        a, b = ink(part[y0:y1, x0:x1], delta), ink(port[y0:y1, x0:x1], delta)
        if not a.any() or not b.any():
            continue
        total["rects"] += 1
        total["ref"] += int(a.sum())
        total["port"] += int(b.sum())
        total["tp"] += int((a & b).sum())
        total["fp"] += int((~a & b).sum())
        best = max(int((a & np.roll(np.roll(b, sy, 0), sx, 1)).sum()) for sy in range(-2, 3) for sx in range(-2, 3))
        if shifts:
            score, sy, sx = max((int((a & np.roll(np.roll(b, sy, 0), sx, 1)).sum()), sy, sx)
                                for sy in range(-3, 4) for sx in range(-3, 4))
            rows_a, rows_b = np.flatnonzero(a.any(axis=1)), np.flatnonzero(b.any(axis=1))
            print(f"  место {x},{y},{rw}x{rh} сдвиг_к_снимку dx={sx} dy={sy} совпало={score}/{int(a.sum())} "
                  f"строки_снимка={rows_a[0]}..{rows_a[-1]} строки_порта={rows_b[0]}..{rows_b[-1]}")
        total["best"] += best
        total["best_union"] += int(a.sum()) + int(b.sum()) - best
        d = np.abs(part[y0:y1, x0:x1, :3] - port[y0:y1, x0:x1, :3]).max(axis=2)
        total["cmp"] += d.size
        total["bad"] += int((d > threshold).sum())
    print(f"text {Path(ref_path).stem} {text_line(total)}")
    return total


def text_line(t: dict) -> str:
    union = t["ref"] + t["port"] - t["tp"]
    return (f"мест={t['rects']} текста_снимка={t['ref']} текста_порта={t['port']} совпало={t['tp']} "
            f"({100.0 * t['tp'] / max(1, t['ref']):.1f}%) лишних={t['fp']} IoU={t['tp'] / max(1, union):.3f} "
            f"IoU±2={t['best'] / max(1, t['best_union']):.3f} отличается={100.0 * t['bad'] / max(1, t['cmp']):.2f}%")


def composite_mask(state: Path | None, shape: tuple[int, int], mask: np.ndarray) -> np.ndarray:
    """Составной снимок (карта с панелями): подписи каждого экрана из `screens` файла состояния — [id, x, y]."""
    if state is None:
        return mask
    for screen, x, y in json.loads(state.read_text(encoding="utf-8")).get("screens", []):
        if screen_of(screen) is None:
            raise SystemExit(f"{state.name}: нет экрана {screen}")
        text_mask(screen, shape, (x, y), mask)
    return mask


def shot_mask(state: Path | None, shape: tuple[int, int]) -> tuple[np.ndarray, list[str]]:
    """Маска снимка из файла состояния (координаты экрана порта) и объяснения по каждому прямоугольнику."""
    mask = np.ones(shape, dtype=bool)
    if state is None:
        return mask, []
    why = []
    for m in json.loads(state.read_text(encoding="utf-8")).get("mask", []):
        x, y, w, h = m["rect"]
        if not m.get("why"):
            raise SystemExit(f"{state.name}: маска {m['rect']} без объяснения why")
        mask[max(0, y):max(0, y + h), max(0, x):max(0, x + w)] = False
        why.append(m["why"])
    return mask, why


def diff_at(ref: np.ndarray, port: np.ndarray, dx: int, dy: int, mask: np.ndarray, step: int = 1) -> float:
    h, w = port.shape[:2]
    part = ref[dy:dy + h:step, dx:dx + w:step, :3]
    m = mask[::step, ::step] & (port[::step, ::step, 3] > 0)
    d = np.abs(part - port[::step, ::step, :3]).max(axis=2)
    return float(d[m].mean()) if m.any() else 1e9


def align(ref: np.ndarray, port: np.ndarray, mask: np.ndarray) -> tuple[int, int]:
    """Лучший сдвиг порта внутри снимка: грубо шагом 8 по прореженным точкам, затем точно вокруг лучшего."""
    rh, rw = ref.shape[:2]
    h, w = port.shape[:2]
    if rh < h or rw < w:
        raise SystemExit(f"снимок {rw}×{rh} меньше экрана порта {w}×{h}")
    best = min(((diff_at(ref, port, dx, dy, mask, 4), dx, dy)
                for dy in range(0, rh - h + 1, 8) for dx in range(0, rw - w + 1, 8)), key=lambda t: t[0])
    _, bx, by = best
    fine = min(((diff_at(ref, port, dx, dy, mask), dx, dy)
                for dy in range(max(0, by - 8), min(rh - h, by + 8) + 1)
                for dx in range(max(0, bx - 8), min(rw - w, bx + 8) + 1)), key=lambda t: t[0])
    return fine[1], fine[2]


def compare(ref_path: Path, port_path: Path, screen: str | None, threshold: int, limit: float,
            state: Path | None = None) -> bool:
    ref, port = load(ref_path), load(port_path)
    texts = composite_mask(state, port.shape[:2], text_mask(screen, port.shape[:2]))
    shots, why = shot_mask(state, port.shape[:2])
    mask = texts & shots
    dx, dy = align(ref, port, mask)
    h, w = port.shape[:2]
    part = ref[dy:dy + h, dx:dx + w, :3]
    compared = mask & (port[..., 3] > 0)
    delta = np.abs(part - port[..., :3]).max(axis=2)
    bad = compared & (delta > threshold)
    n = int(compared.sum())
    share = 100.0 * int(bad.sum()) / max(1, n)
    mean = float(delta[compared].mean()) if n else 0.0
    ys, xs = np.nonzero(bad)
    worst = f"{xs.min()},{ys.min()},{xs.max() - xs.min() + 1},{ys.max() - ys.min() + 1}" if len(xs) else "-"
    out = REPO / ".cache" / "pixel" / f"{Path(ref_path).stem}.diff.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    img = np.zeros((h, w, 3), dtype=np.uint8)
    grey = (part.mean(axis=2) * 0.35).astype(np.uint8)
    img[..., 0] = img[..., 1] = img[..., 2] = grey
    img[bad] = (255, 0, 0)
    img[~texts] = (0, 0, 64)
    img[~shots] = (64, 0, 64)
    Image.fromarray(img).save(out)
    ok = share <= limit
    stem = Path(ref_path).stem
    name = stem if screen is None or stem.startswith(screen) else screen
    print(f"pixel {name} сдвиг={dx},{dy} сравнено={n} точек отличается={share:.2f}% средняя_разница={mean:.1f} "
          f"худшая_область={worst} маска={len(why)} -> {out.name}{'' if ok else '  <-- больше ' + str(limit) + '%'}")
    for reason in why:
        print(f"    маска: {reason}")
    return ok


def main(argv: list[str]) -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("snapshot", nargs="?", type=Path, help="снимок оригинала (PNG)")
    p.add_argument("--screen", help="id экрана порта (DLG_…): порт соберёт его сам; им же закрываются подписи")
    p.add_argument("--port", type=Path, help="готовая картинка порта вместо --screen")
    p.add_argument("--dir", type=Path, help="папка снимков DLG_X.png — сверить все")
    p.add_argument("--threshold", type=int, default=24, help="порог разницы канала, выше — точка отличается")
    p.add_argument("--max", dest="limit", type=float, default=2.0, help="допустимая доля отличающихся точек, %%")
    p.add_argument("--text", action="store_true", help="сверять подписи, а не всё кроме них (см. описание)")
    p.add_argument("--shifts", action="store_true", help="--text: строка на место подписи — лучший сдвиг текста порта к снимку")
    p.add_argument("--ink", type=int, default=48, help="--text: отличие яркости от средней по месту, с которого точка — текст")
    a = p.parse_args(argv)
    if a.dir:
        shots = sorted((s for s in a.dir.rglob("*.png") if screen_name(s) or recipe_of(state_of(s))), key=lambda s: s.name)
        if not shots:
            print(f"в {a.dir} нет снимков DLG_*.png и снимков с рецептом порта")
            return 1
        results, skipped, texts = [], 0, []
        for s in shots:
            state, screen = state_of(s), screen_name(s)
            recipe = recipe_of(state)
            try:
                port = with_videos(port_map(s.stem, recipe) if recipe else port_screen(screen, state, a.text), state, screen)
            except NotBuilt as e:
                print(f"pixel {s.stem} пропуск: порт не собирает ({e})")
                skipped += 1
                continue
            if a.text:
                texts.append(text_compare(s, port, screen, state, a.threshold, a.ink, a.shifts))
                continue
            results.append(compare(s, port, screen, a.threshold, a.limit, state))
        if a.text:
            if texts:
                print(f"text итого снимков={len(texts)} " + text_line({k: sum(t[k] for t in texts) for k in texts[0]}))
            return 0
        print(f"сверено {len(results)}, в пределах {sum(results)}" + (f", пропущено {skipped}" if skipped else ""))
        return 0 if all(results) else 1
    state = state_of(a.snapshot) if a.snapshot else None
    recipe = recipe_of(state)
    screen = a.screen or (screen_name(a.snapshot) if a.snapshot else None)
    if a.snapshot is None or not (screen or a.port or recipe):
        p.error("нужны снимок и --screen или --port (или рецепт port в файле состояния, или --dir)")
    try:
        port = a.port or with_videos(port_map(a.snapshot.stem, recipe) if recipe else port_screen(screen, state, a.text),
                                     state, screen)
    except NotBuilt as e:
        print(f"pixel {a.snapshot.stem} пропуск: порт не собирает ({e})")
        return 1
    if a.text:
        text_compare(a.snapshot, port, screen, state, a.threshold, a.ink, a.shifts)
        return 0
    return 0 if compare(a.snapshot, port, screen, a.threshold, a.limit, state) else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
