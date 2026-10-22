"""keyed_webp.py — общая запись кадров графики для сборщиков tools/ui-assets и tools/gfx-assets.

* **Ключ вырезается при записи кадра**, в одном проходе с распаковкой: пурпур игры (всё, что в 16-битном цвете движка RGB565
  равно 0xF81F, — не только ровно 255,0,255) становится прозрачной чёрной точкой (цвет обнулён — иначе при фильтрации по краям
  просачивалась бы розовая кайма). Файла с ключом на диске не бывает ни в какой момент сборки.
* **Формат — WebP без потерь** (`lossless`, `exact`: RGB под альфой 0 хранится как есть, то есть нулём). Пиксели после
  разбора те же, что до записи, бит в бит; хеш описи — от пикселей RGBA, а не от байтов файла.
* **Кадр графики мира — только занятая часть** (`trim`, Г19 `docs/perf-log.md`): по альфе, если занятый прямоугольник не больше половины
  холста — то же правило, что у рантайма `GfxTextures.Trim` (Г7), поэтому текстура на экране та же самая. Угол части в холсте уходит в
  опись; хеш — от холста, восстановленного из части (у прежнего целого файла хеш тот же). Интерфейс не обрезается: его картинки рантайм
  берёт целыми.
* **Сборка — во временный каталог**, готовый каталог встаёт на место переименованием (`swap_dir`): прерванная сборка оставляет
  прежнюю целую графику, а не смесь старых и новых файлов.
"""
from __future__ import annotations

import hashlib
import os
import shutil
import time
from pathlib import Path

import numpy as np
from PIL import Image

from ff_extract import FFArchive

KEY = (255, 0, 255)
SHA_LEN = 16
EXT = ".webp"
# method 4 / quality 50 — замер 11.10 (docs/perf-log.md, Г6) на 360 кадрах gfx/ui: 0,38 от байтов PNG за 2,2 с; method 5 даёт 0,35
# за 54 с, method 6 — 0,34 за 380 с, method 0 — 1,28 (больше PNG). quality без потерь — усилие сжатия, на пиксели не влияет.
WEBP_OPTIONS = {"lossless": True, "exact": True, "method": 4, "quality": 50}
SHEET_CACHE = 16     # разобранных листов-атласов на процесс (кадры одного листа идут подряд)
VERIFY_EVERY = 211   # после сборки каждый N-й файл разбирается заново и сверяется с хешем описи
OLD_SUFFIX = ".old"

# Кадр к записи: (вид, имя в архиве, путь от корня выхода); вид — "plain" (картинка архива как есть) или "frame" (собранный кадр).
Item = tuple[str, str, str]
# Записанный кадр: (путь от корня, ширина и высота холста, хеш пикселей холста, точек ключа, угол записанной части в холсте или None —
# записан весь холст без обрезки).
Done = tuple[str, int, int, str, int, tuple[int, int] | None]
RENAME_TRIES = 120   # × RENAME_PAUSE_S: свежие файлы может держать антивирус или индексатор, а каталог — открытый проводник
RENAME_PAUSE_S = 0.5


def key_mask(rgba: np.ndarray) -> np.ndarray:
    """Точки ключа: цвет, который в RGB565 движка совпадает с пурпуром 0xF81F. В кадрах встречается и (252,2,252): у
    DLG_BATTLE_A_LUNITGROUP/RUNITGROUP 138 точек по краям; игра их не рисует (пункт 15 прогона 11.10)."""
    r, g, b = rgba[..., 0], rgba[..., 1], rgba[..., 2]
    return ((r >> 3) == KEY[0] >> 3) & ((g >> 2) == KEY[1] >> 2) & ((b >> 3) == KEY[2] >> 3)


def pixel_sha(rgba: np.ndarray) -> str:
    """Хеш описи: первые 16 знаков sha256 от байтов RGBA (строка за строкой) — не зависит от кодека и его настроек."""
    return hashlib.sha256(np.ascontiguousarray(rgba).tobytes()).hexdigest()[:SHA_LEN]


def used_rect(rgba: np.ndarray) -> tuple[int, int, int, int]:
    """Занятый прямоугольник (x, y, ширина, высота) — точки с альфой не 0, как `Image.get_used_rect` Godot; пустой кадр — одна точка в углу,
    как у `GfxTextures.Trim`."""
    ys, xs = np.nonzero(rgba[..., 3])
    if len(xs) == 0:
        return 0, 0, 1, 1
    return int(xs.min()), int(ys.min()), int(xs.max() - xs.min() + 1), int(ys.max() - ys.min() + 1)


def trim_box(rgba: np.ndarray) -> tuple[int, int, int, int] | None:
    """Что записать от холста: занятый прямоугольник, если он не больше половины площади (правило `GfxTextures.Trim`, Г7); иначе None —
    холст целиком (выигрыш не больше вдвое, а текстура рантайма тогда — весь холст)."""
    x, y, w, h = used_rect(rgba)
    return None if w * h * 2 > rgba.shape[0] * rgba.shape[1] else (x, y, w, h)


def write_frame(image: Image.Image, path: Path, trim: bool = False) -> tuple[int, int, str, int, tuple[int, int] | None]:
    """Вырезать ключ и записать кадр WebP без потерь. Возвращает ширину и высоту холста, хеш пикселей холста, сколько точек ключа было и
    угол записанной части: `trim` — только занятая часть (`trim_box`; не обрезается — угол (0, 0) и весь холст), иначе None и весь холст.
    Вне занятой части альфа 0 по определению; цвет там рантайм не видит (поля текстуры прозрачные), и хеш — от холста с нулями вне части,
    то есть того, что восстановится из файла. Повторная запись того же кадра ничего не меняет."""
    rgba = np.array(image.convert("RGBA"))
    key = key_mask(rgba)
    rgba[key] = 0
    at = None
    part = rgba
    if trim:
        box = trim_box(rgba)
        at = (0, 0) if box is None else box[:2]
        if box is not None:
            x, y, w, h = box
            part = np.ascontiguousarray(rgba[y:y + h, x:x + w])
            rgba = restore(part, at, rgba.shape[1], rgba.shape[0])
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(part, "RGBA").save(path, "WEBP", **WEBP_OPTIONS)
    return rgba.shape[1], rgba.shape[0], pixel_sha(rgba), int(key.sum()), at


def restore(part: np.ndarray, at: tuple[int, int], w: int, h: int) -> np.ndarray:
    """Холст w×h из записанной части: часть в углу `at`, вокруг — прозрачные нулевые точки."""
    canvas = np.zeros((h, w, 4), dtype=np.uint8)
    x, y = at
    canvas[y:y + part.shape[0], x:x + part.shape[1]] = part
    return canvas


def read_sha(path: Path, at: tuple[int, int] | None = None, size: tuple[int, int] | None = None) -> tuple[int, int, str]:
    """Размер и хеш пикселей холста файла (для выборочной сверки после сборки и для модов); `at` и `size` — угол части и холст из описи."""
    with Image.open(path) as im:
        rgba = np.array(im.convert("RGBA"))
    if at is not None and size is not None:
        rgba = restore(rgba, at, *size)
    return rgba.shape[1], rgba.shape[0], pixel_sha(rgba)


_archives: dict[str, FFArchive] = {}


def render(job: tuple) -> list[Done]:
    """Задача пула процессов `(архив, корень выхода, кадры[, пути кадров к обрезке])`: собрать кадры куска архива, вырезать ключ, записать
    WebP (из `trim` — только занятую часть). Возвращает по кадру `Done`. Архив открывается один раз на процесс."""
    src, out_root, items = job[:3]
    trim: frozenset[str] = job[3] if len(job) > 3 else frozenset()
    ff = _archives.get(src)
    if ff is None:
        ff = _archives[src] = FFArchive(Path(src))
    cache: dict = {}
    out = []
    for kind, name, rel in items:
        if len(cache) > SHEET_CACHE:
            cache.clear()
        image = ff.plain_image(name) if kind == "plain" else ff.build_frame(ff.frames[name], cache)
        out.append((rel, *write_frame(image, Path(out_root) / rel, rel in trim)))
    return out


def verify(root: Path, done: dict[str, Done]) -> None:
    """Выборочная сверка: файл, разобранный заново (обрезанный — с холстом по углу), даёт те же размер и хеш пикселей, что ушли в опись
    (без потерь)."""
    sample = sorted(done)[::VERIFY_EVERY]
    bad = [rel for rel in sample if read_sha(root / rel, done[rel][5], done[rel][1:3]) != tuple(done[rel][1:4])]
    if bad:
        raise SystemExit(f"ОШИБКА: пиксели после записи не совпали у {len(bad)} из {len(sample)}: {bad[:5]}")
    print(f"сверено заново: {len(sample)} файлов, расхождений 0", flush=True)


def fresh_dir(path: Path) -> Path:
    """Пустой временный каталог сборки: остаток прерванной сборки сносится."""
    if path.exists():
        shutil.rmtree(path)
    path.mkdir(parents=True)
    return path


def recover(final: Path) -> None:
    """Сборку прервали между двумя переименованиями `swap_dir` (прежний каталог уже отложен, новый ещё не встал): вернуть прежний."""
    old = final.with_name(final.name + OLD_SUFFIX)
    if not final.exists() and old.exists():
        _rename(old, final)
        print(f"восстановлен прежний {final} (прошлая сборка прервалась при подмене)")


def swap_dir(new: Path, final: Path) -> None:
    """Поставить готовый каталог на место прежнего. В Windows занятый каталог одним вызовом не заменить, поэтому два
    переименования подряд (миллисекунды); прервались между ними — прежний вернёт `recover` при следующем запуске сборщика.
    Файл в каталоге открыт другим процессом (игра, антивирус, проводник) — переименование повторяется до ~60 с; не вышло — готовая
    сборка остаётся в `*.new`, прежний каталог — на месте или в `*.old` (его вернёт `recover`)."""
    old = final.with_name(final.name + OLD_SUFFIX)
    if old.exists():
        shutil.rmtree(old)
    if final.exists():
        _rename(final, old)
    _rename(new, final)
    if old.exists():
        shutil.rmtree(old)


def replace_text(path: Path, text: str) -> None:
    """Записать файл целиком через временный рядом и os.replace: недописанной описи не бывает."""
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_text(text, encoding="utf-8")
    os.replace(tmp, path)


def _rename(src: Path, dst: Path) -> None:
    for attempt in range(RENAME_TRIES):
        try:
            os.rename(src, dst)
            return
        except PermissionError as e:
            if attempt == RENAME_TRIES - 1:
                raise SystemExit(f"ОШИБКА: не переименовать {src} -> {dst} ({e}): закройте программы, держащие файлы "
                                 f"графики (игру, проводник), и запустите сборку снова") from e
            time.sleep(RENAME_PAUSE_S)
