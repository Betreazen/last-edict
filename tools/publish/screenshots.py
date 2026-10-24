#!/usr/bin/env python3
r"""screenshots.py — снимки порта для описания проекта и README: запускает порт С ОКНОМ (снимка без окна у Godot нет,
docs/godot-notes.md), сам проходит по сценарию и кладёт PNG в docs/screenshots/.

    py tools/publish/screenshots.py                 # все снимки
    py tools/publish/screenshots.py --only battle   # один (ключ повторяется)
    py tools/publish/screenshots.py --hold-ms battle=14600   # подбор мгновения

Как снимается. Экран порта рисуется в буфер показа (ScreenStage) в родном размере игры 800×600, режим «целый» (--scale integer),
окно 800×600 — точка буфера = точка окна, без растяжения. Карта, бой и окна — сцена карты в стенде показа (dev/StageHost) по
сохранению корпуса: шаги --clicks (те же, что у tools/ui-render-check.py), выдержка --hold-ms, снимок --shot. Главное меню —
UiFlow с --start DLG_MAIN_MENU и --stage-shot. Полноразмерный снимок — 1600×1200, ×2 ближайшим соседом (точка в точку, как
окно 1600×1200 в режиме «целый»); уменьшенный *-small.png — не больше 200 КБ (палитра 256 цветов, ширина 640 и меньше, пока не влезет).

Широкий экран (Э5): снимок с size (1280×720) снимается в окне этого размера в режиме показа по умолчанию («чёткий», как у игрока),
без увеличения ×2. --repo <рабочая копия> — снимать сборкой другой рабочей копии (кадр «было» для пары: сборка начала этапа).

Детерминированно: сохранения корпуса фиксированы, --check отключает пересев генератора боя (бой повторяется ход в ход), моды
выключены (--no-mods), зонд порта выключен (D2_PORT_PROBE=0 — ни журнала, ни надписи «Пометка … записана»), звук — Dummy.
Мгновение боя задаёт выдержка в мс: ±кадр от прогона к прогону.

Данные: графика — D2_ASSETS_DIR, сохранения — D2_CORPUS_DIR (в рабочей копии ../dg-wt-* — основной каталог, как у check.py).
Корпус и графика только читаются; каталоги меню (герои, кампании, сохранения) — во временном каталоге, не в user://.
Перед запуском — dotnet build godot/LastEdict.Godot.csproj (скрипт собирает сам, --no-build — нет).
Инструмент разработчика: в публичном дереве ему нужны импортированные данные игры (py tools/import_game.py, docs/publishing.md) и
сохранения корпуса журналов зонда, которых в репозитории нет.

Каждый снимок проверяется числами: размер, разброс яркости (не пустой), доля тёмного (не чёрный), точки мадженты (ключ
прозрачности 255,0,255 не выбит), строка сцены (тот экран открыт). Итог — таблица с размерами файлов.
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass, replace
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))
from check import data_env  # noqa: E402  (корпус и графика рабочей копии — из основного каталога)

OUT = REPO / "docs" / "screenshots"
GODOT = os.environ.get("GODOT", r"C:\Tools\bin\godot.cmd")
WIDTH, HEIGHT = 800, 600          # родной размер экранов игры
WIDE = (1280, 720)                # широкий экран (Э5)
FULL_SCALE = 2                    # полноразмерный снимок — 1600×1200
SMALL_MAX_BYTES = 200_000
SMALL_WIDTHS = (640, 560, 480, 400)
RUN_TIMEOUT = 180

# «Растленная власть» (штатный сценарий S073), партия человека за гномов (корпус 20260919-human): день 71 — карта открыта,
# столица отстроена, книга с заклинаниями; день 30 — герой S073KC0001 (5 воинов) у города S073FT0009 с охраной из 5.
SAVE_LATE = "20260919-human/rastlennaya-vlast-dwarf-t71-122.sg"
SAVE_MID = "20260919-human/rastlennaya-vlast-dwarf-t30-055.sg"
HUMAN = "S073PL000a"


@dataclass(frozen=True)
class Shot:
    name: str
    save: str | None          # None — главное меню
    expect: str               # подстрока вывода сцены: открыт нужный экран
    clicks: str = ""
    at: str | None = None     # клетка в центре вида
    hold_ms: int = 800
    extra: tuple[str, ...] = ()   # свои ключи сцены (--queue-strip — полоса очереди Last Edict, под --check она выключена)
    size: tuple[int, int] = (WIDTH, HEIGHT)   # окно; не 800×600 — показ «чёткий» и без увеличения (широкий экран, Э5)
    min_luma: int = 25   # порог средней яркости: ниже — «чёрный экран»; тёмные окна (настройки в стиле «B») — ниже

    @property
    def wide(self) -> bool:
        return self.size != (WIDTH, HEIGHT)


SHOTS = (
    Shot("main-menu", None, "stage shot=", hold_ms=2500),
    Shot("world-map", SAVE_LATE, "ground_ready=True", at="60,55", hold_ms=1500),
    # Бой героя с охраной города (10 юнитов), автобой; выдержка — середина взрыва удара по площади (виден с 13,7 до 14,3 с).
    Shot("battle", SAVE_MID, "fight 70,44 55,50 бой=1", clicks="fight 70,44 55,50;battle ui click TOG_AUTOBATTLE", hold_ms=14000),
    # Тот же бой, ход человека: полоса очереди ходов над полем (своя функция Last Edict, docs/own-version-roadmap.md §3.3).
    Shot("battle-queue", SAVE_MID, "fight 70,44 55,50 бой=1", clicks="fight 70,44 55,50", hold_ms=4000, extra=("--queue-strip",)),
    Shot("city", SAVE_LATE, "панель=DLG_CAPITAL", clicks="panel click BTN_CAPITAL"),
    Shot("spellbook", SAVE_LATE, "окна=DLG_CAST_SPELL", clicks="panel click BTN_CAST_SPELL"),
    Shot("hero", SAVE_LATE, "окна=DLG_MANAGE_STACK", clicks="select S073KC0001;panel release IMG_LEADER"),
    Shot("diplomacy", SAVE_LATE, "панель=DLG_DIPLOMACY", clicks="panel click BTN_DIPLOMACY;diplo 3"),
    # Э5: широкий экран, окно настроек, вкладка «Управление», подсказки геймпада.
    Shot("wide-map", SAVE_LATE, "ground_ready=True", at="60,55", hold_ms=1500, size=WIDE),
    Shot("settings", SAVE_LATE, "окна=LE_SETTINGS", clicks="ui click BTN_OPTIONS", size=WIDE, min_luma=15),
    Shot("options-old", SAVE_LATE, "окна=DLG_OPTIONS", clicks="ui click BTN_OPTIONS", size=WIDE),   # «было»: окно оригинала (--repo)
    Shot("controls", SAVE_LATE, "окна=LE_SETTINGS", clicks="ui click BTN_OPTIONS;ui tab Controls", size=WIDE, min_luma=15),
    Shot("gamepad", SAVE_LATE, "ground_ready=True", clicks="select S073KC0001;ui pad DpadDown", at="60,55", hold_ms=1500, size=WIDE,
         extra=("--shot-window",)),   # полоса подсказок — верхний слой окна, не буфер стенда
)


def window_position(size: tuple[int, int] = (WIDTH, HEIGHT)) -> list[str]:
    """Окно — в угол основного экрана подальше от мыши: курсор над окном дал бы подсветку клетки или кнопки на снимке. Мышь на
    другом мониторе — угол любой, окно её и так не накроет. У Godot начало координат — левый верхний угол всех мониторов (у Windows —
    угол основного): монитор левее или выше основного сдвигает его, иначе окно открылось бы на другом мониторе."""
    if sys.platform != "win32":
        return []
    try:
        import ctypes
        from ctypes import wintypes
        ctypes.windll.shcore.SetProcessDpiAwareness(2)   # точки экрана, как у Godot
        point = wintypes.POINT()
        ctypes.windll.user32.GetCursorPos(ctypes.byref(point))
        screen_w, screen_h = ctypes.windll.user32.GetSystemMetrics(0), ctypes.windll.user32.GetSystemMetrics(1)
        # SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN: угол всех мониторов в точках Windows (≤ 0).
        left, top = ctypes.windll.user32.GetSystemMetrics(76), ctypes.windll.user32.GetSystemMetrics(77)
    except (AttributeError, OSError):
        return []
    margin = 60
    x = margin if point.x > screen_w // 2 else max(margin, screen_w - size[0] - margin)
    y = margin if point.y > screen_h // 2 else max(margin, screen_h - size[1] - margin * 2)
    return ["--position", f"{x - left},{y - top}"]


def godot_args(shot: Shot, raw: Path, menu_dirs: Path, corpus: Path, repo: Path = REPO) -> list[str]:
    w, h = shot.size
    head = [GODOT, "--path", str(repo / "godot"), "--resolution", f"{w}x{h}", *window_position(shot.size), "--audio-driver", "Dummy"]
    scale = "sharp" if shot.wide else "integer"
    if shot.save is None:
        empty = menu_dirs / "empty"
        empty.mkdir(parents=True, exist_ok=True)
        return [*head, "res://scenes/ui/UiFlow.tscn", "--", "--no-mods", "--start", "DLG_MAIN_MENU", "--scale", "integer",
                "--stage-shot", str(raw), "--stage-shot-ms", str(shot.hold_ms),
                "--ldr", str(empty), "--csg", str(empty), "--leaders", str(menu_dirs / "leaders"),
                "--campaigns", str(menu_dirs / "campaigns"), "--saves", str(menu_dirs / "saves")]
    args = [*head, "res://scenes/dev/StageHost.tscn", "--", "--no-mods", "--scene", "res://scenes/map/MapScreen.tscn",
            "--scale", scale, "--sg", str(corpus / shot.save), "--player", HUMAN, "--check",
            "--hold-ms", str(shot.hold_ms), "--shot", str(raw)]
    if shot.at:
        args += ["--at", shot.at]
    if shot.clicks:
        args += ["--clicks", shot.clicks]
    return [*args, *shot.extra]


def kill_tree(proc: subprocess.Popen) -> None:
    """Снять прогон с потомками: godot.cmd — оболочка cmd.exe, и kill() снял бы только её, а окно Godot осталось бы."""
    if sys.platform == "win32":
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)], capture_output=True, check=False)
    else:
        proc.kill()
    proc.communicate()


def capture(shot: Shot, raw: Path, menu_dirs: Path, corpus: Path, env: dict[str, str], repo: Path = REPO) -> str | None:
    """Прогон сцены с окном; None — снимок есть и сцена дошла до нужного экрана, иначе причина."""
    if shot.save is not None and not (corpus / shot.save).is_file():
        return f"нет сохранения {corpus / shot.save} (корпус: D2_CORPUS_DIR)"
    proc = subprocess.Popen(godot_args(shot, raw, menu_dirs, corpus, repo), cwd=repo, env=env, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    try:
        out, _ = proc.communicate(timeout=RUN_TIMEOUT)
    except subprocess.TimeoutExpired:
        kill_tree(proc)
        return f"таймаут {RUN_TIMEOUT} с"
    out = out or ""
    (raw.parent / f"{shot.name}.log").write_text(out, encoding="utf-8")
    if proc.returncode != 0:
        return f"код {proc.returncode}: {out.strip()[-600:]}"
    if shot.expect not in out:
        return f"в выводе нет «{shot.expect}» (лог {raw.parent / (shot.name + '.log')})"
    if not raw.is_file():
        return "сцена вышла без снимка"
    return None


def verify(image: Image.Image, size: tuple[int, int] = (WIDTH, HEIGHT), min_luma: int = 25) -> tuple[str | None, str]:
    """Числа снимка: (причина отказа или None, строка чисел)."""
    rgb = np.asarray(image.convert("RGB")).astype(np.int32)
    luma = rgb @ np.array([299, 587, 114]) // 1000
    dark = float((rgb.sum(axis=2) < 30).mean())
    magenta = int(((rgb[..., 0] >= 250) & (rgb[..., 1] <= 5) & (rgb[..., 2] >= 250)).sum())
    line = f"{image.width}x{image.height} яркость={luma.mean():.0f} разброс={luma.std():.0f} тёмного={dark:.0%} маджента={magenta}"
    if image.size != size:
        return f"размер {image.size}, ожидался {size[0]}x{size[1]}", line
    if luma.std() < 20:
        return "почти однотонный — пустой экран?", line
    if luma.mean() < min_luma or dark > 0.5:
        return "слишком тёмный — чёрный экран или туман", line
    if magenta > 0:
        return "точки мадженты — ключ прозрачности не выбит", line
    return None, line


def small_copy(image: Image.Image, path: Path) -> tuple[int, int] | None:
    """Уменьшенная копия ≤ SMALL_MAX_BYTES: палитра 256 цветов, ширина по убыванию, пока не влезет; не влезла и самая узкая — None."""
    for width in SMALL_WIDTHS:
        height = width * image.height // image.width
        scaled = image.resize((width, height), Image.Resampling.LANCZOS)
        # Октодерево без размытия ошибки: при той же палитре файл на треть меньше медианного сечения (640×480 влезает).
        scaled.quantize(colors=256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE).save(path, optimize=True)
        if path.stat().st_size <= SMALL_MAX_BYTES:
            return width, height
    return None


def build(env: dict[str, str], repo: Path = REPO) -> bool:
    proc = subprocess.run(["dotnet", "build", str(repo / "godot" / "LastEdict.Godot.csproj"), "-nologo", "-v", "q"], cwd=repo, env=env,
                          capture_output=True, text=True, encoding="utf-8", errors="replace")
    if proc.returncode != 0:
        print((proc.stdout or "") + (proc.stderr or ""))
    return proc.returncode == 0


def parse_holds(items: list[str]) -> dict[str, int]:
    holds = {}
    for item in items:
        name, _, ms = item.partition("=")
        if not ms.isdigit():
            raise SystemExit(f"--hold-ms {item}: нужно ИМЯ=МС")
        holds[name] = int(ms)
    return holds


def main(argv: list[str]) -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0], formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--only", action="append", default=[], help="только этот снимок (ключ повторяется)")
    parser.add_argument("--hold-ms", action="append", default=[], help="выдержка снимка: ИМЯ=МС")
    parser.add_argument("--out", type=Path, default=OUT, help=f"каталог снимков (по умолчанию {OUT.relative_to(REPO)})")
    parser.add_argument("--no-build", action="store_true", help="не собирать godot/LastEdict.Godot.csproj")
    parser.add_argument("--repo", type=Path, default=REPO, help="рабочая копия, чьей сборкой снимать (кадр «было»)")
    args = parser.parse_args(argv)
    known = {s.name for s in SHOTS}
    if unknown := set(args.only) - known:
        parser.error(f"нет снимков {sorted(unknown)}; есть {sorted(known)}")
    holds = parse_holds(args.hold_ms)
    shots = [replace(s, hold_ms=holds.get(s.name, s.hold_ms)) for s in SHOTS if not args.only or s.name in args.only]

    if shutil.which(GODOT) is None:
        print(f"нет Godot {GODOT}: задайте переменную GODOT (Godot 4.7.2 mono)")
        return 1
    env = dict(data_env(), D2_PORT_PROBE="0")
    corpus = Path(env.get("D2_CORPUS_DIR") or REPO / "corpus")
    if not args.no_build and not build(env, args.repo):
        print("сборка godot/LastEdict.Godot.csproj не прошла")
        return 1
    args.out.mkdir(parents=True, exist_ok=True)
    failed, rows = 0, []
    with tempfile.TemporaryDirectory(prefix="d2-shots-") as tmp:
        work = Path(tmp)
        for shot in shots:
            raw = work / f"{shot.name}.png"
            reason = capture(shot, raw, work / "menu", corpus, env, args.repo)
            numbers = "-"
            if reason is None:
                with Image.open(raw) as opened:
                    image = opened.convert("RGB")
                # Широкий кадр снимается из буфера показа (экраны ×2, «чёткий»): окно показывает его уменьшенным — так же и здесь.
                if shot.wide and image.size != shot.size and abs(image.width / image.height - shot.size[0] / shot.size[1]) < 0.01:
                    image = image.resize(shot.size, Image.Resampling.LANCZOS)
                reason, numbers = verify(image, shot.size, shot.min_luma)
            small_size = None
            if reason is None:
                full_image = image if shot.wide else image.resize((WIDTH * FULL_SCALE, HEIGHT * FULL_SCALE), Image.Resampling.NEAREST)
                full_image.save(work / "full.png", optimize=True)
                if (small_size := small_copy(image, work / "small.png")) is None:
                    reason = f"уменьшенная копия и в {SMALL_WIDTHS[-1]} точек больше {SMALL_MAX_BYTES} байт"
            if reason is not None:
                failed += 1
                rows.append((shot.name, "ОТКАЗ", numbers, reason))
                if (log := work / f"{shot.name}.log").is_file():
                    shutil.copy(log, args.out / f"{shot.name}.failed.log")
                continue
            # В каталог снимков — только прошедшие проверку: неудача оставляет прежние файлы как были.
            full = Path(shutil.copy(work / "full.png", args.out / f"{shot.name}.png"))
            small = Path(shutil.copy(work / "small.png", args.out / f"{shot.name}-small.png"))
            sw, sh = small_size
            rows.append((shot.name, "ok", numbers, f"{full.name} {full.stat().st_size // 1024} КБ; "
                         f"{small.name} {sw}x{sh} {small.stat().st_size // 1024} КБ"))
    for name, status, numbers, note in rows:
        print(f"{name:<10} {status:<5} {numbers}  {note}")
    print(f"снимков {len(rows) - failed} из {len(rows)} → {args.out}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
