#!/usr/bin/env python3
r"""Снимки F12 из журнала зонда -> сверка карты со снимком (godot/scenes/dev/ShotCompare.tscn).

Зонд (схема 7.1, `screenshot`) пишет на каждый F12 клетку в центре вида карты и время нажатия. Под обёрткой
DisciplesGL снимок пишет она (`ScreenShots\ScreenShotNNN.png`, на следующей отрисовке), зонд имени файла не знает
(`via = key`, `file = null`) — берётся первый файл `ScreenShot???.*`, записанный не раньше нажатия; снимок самого
движка (`via = engine`) несёт имя сразу. Состояние карты берётся из сохранения той же партии (между строками `game_phase` new/load): первое после снимка,
иначе последнее до него — камера состояния не меняет, лишь бы между снимком и сохранением никто не ходил (памятка
docs/tasks/probe-shots-memo.md: F12 и сразу сохранение). Журналы прогона (`passive-*.jsonl`) — все, по порядку.

Сохранение ищется так:
  1. `<игра>\SaveGame\<имя>.sg` — если имя своё (не QuickSave и не автосохранение): надёжно, файл не затирается;
  2. иначе — копия сторожа сохранений в каталоге прогона по времени: часы журнала — `time_ms − t` строки `screenshot`,
     копия — первая записанная в течение 20 с после сохранения. Повтор того же содержимого сторож не копирует — тогда
     берётся последняя копия до сохранения (то же содержимое), строка помечается «повтор».

Пары копируются в `.cache\shots\<прогон>\` (shotNN.png|bmp, shotNN.sg), по каждой печатается команда сверки; с
`--run` сверка запускается (Godot — переменная GODOT, по умолчанию C:\Tools\bin\godot.cmd).

Снимок карты — верхний экран `DLG_ISO` класса `CStratInterf`. Остальные снимки — окна: первый снимок каждого окна
кладётся PNG в `.cache\shots\<прогон>\windows\<экран>.png` (сверка — `python tools/pixel-compare.py --dir` по этой папке),
бой (`DLG_BATTLE_*`) и повторы — в `...\other\<экран>-f<зеркало>-<seq>.png` (сверяются вручную, `--port`).

    python tools/shot_pairs.py corpus/20260928-shots
    python tools/shot_pairs.py corpus/20260928-shots --run
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
DEFAULT_GAME = REPO.parent / "disciples-modding" / "game-dev"
SHARED_SAVES = {"quicksave", "автосохранение"}
TIMEOUT_S = 300
MAP_SCREEN, MAP_CLASS = "DLG_ISO", "CStratInterf"


def events(journal: Path, names: set[str]):
    wanted = re.compile(r'"ev":\s*"(' + "|".join(map(re.escape, names)) + r')"')
    with journal.open(encoding="utf-8") as f:
        for line in f:
            if wanted.search(line):
                yield json.loads(line)


COPY_AFTER_MS, COPY_BEFORE_MS = 20000, 1000


def journals_of(run: Path) -> list[Path]:
    found = sorted(run.glob("*.jsonl"))
    if not found:
        raise SystemExit(f"нет журнала *.jsonl в {run}")
    return found


# События, после которых карта уже не та, что на снимке (27.09: четыре снимка в столице и сохранение после выхода отряда —
# отряд в чужой клетке вместо щита в воротах, «равно» 0,970 против 0,981 с сохранением до снимков).
MAP_CHANGES = {"stack_move", "unit_transfer", "build", "bank", "scout", "battle_start", "battle_end", "capture"}


def pairs(journal: Path) -> list[tuple[dict, dict | None, int | None]]:
    """(screenshot карты, сохранение той же партии или None, часы сохранения в мс от 1970 или None). Сохранение — первое после
    снимка, если между ними карта не менялась (события MAP_CHANGES), иначе последнее до снимка."""
    rows = list(events(journal, {"screenshot", "save", "game_phase"} | MAP_CHANGES))
    offset = next((r["time_ms"] - r["t"] for r in rows if r["ev"] == "screenshot" and r.get("time_ms")), None)
    game, games = 0, []
    for r in rows:
        if r["ev"] == "game_phase" and r["what"] in ("new", "load", "enter"):
            game += 1
        games.append(game)
    result = []
    for i, r in enumerate(rows):
        if r["ev"] != "screenshot" or not is_map(r):
            continue
        same = [(j, s) for j, s in enumerate(rows) if s["ev"] == "save" and s.get("ok") and games[j] == games[i]]
        after = [(j, s) for j, s in same if j > i]
        before = [s for j, s in same if j < i]
        clean = after and not any(rows[k]["ev"] in MAP_CHANGES for k in range(i + 1, after[0][0]))
        save = after[0][1] if clean else (before[-1] if before else after[0][1] if after else None)
        wall = save["t"] + offset if save and offset is not None else None
        result.append((r, save, wall))
    return result


SHOT_SLACK_MS = 2000   # часы файловой системы и GetSystemTimeAsFileTime могут расходиться на доли секунды


class ShotFiles:
    """Файлы ScreenShots: по имени из журнала или первый по времени записи после нажатия, каждый — один раз."""

    def __init__(self, game: Path) -> None:
        folder = game / "ScreenShots"
        files = sorted(folder.glob("ScreenShot???.*")) if folder.is_dir() else []
        self.by_time = sorted((f.stat().st_mtime * 1000, f) for f in files)
        self.folder, self.used, self.cache = folder, set(), {}

    def of(self, shot: dict) -> Path | None:
        key = (shot["seq"], shot.get("time_ms"))
        if key in self.cache:
            return self.cache[key]
        self.cache[key] = self._find(shot)
        return self.cache[key]

    def _find(self, shot: dict) -> Path | None:
        if shot.get("file"):
            path = self.folder / shot["file"]
        else:
            since = (shot.get("time_ms") or 0) - SHOT_SLACK_MS
            path = next((f for t, f in self.by_time if t >= since and f not in self.used), None)
        if path is None or not path.is_file():
            return None
        self.used.add(path)
        return path


def is_map(shot: dict) -> bool:
    return bool(shot.get("camera")) and shot.get("top") == MAP_SCREEN and shot.get("top_class") == MAP_CLASS


def export_windows(journal: Path, files: ShotFiles, out_dir: Path) -> int:
    """Снимки окон -> PNG по имени экрана. Возвращает число разложенных."""
    from PIL import Image  # noqa: PLC0415  нужен только здесь (как у pixel-compare.py)

    taken: set[str] = set()
    count = 0
    for shot in events(journal, {"screenshot"}):
        top = shot.get("top") or ""
        if is_map(shot) or not top.startswith("DLG_"):
            continue
        bmp = files.of(shot)
        if bmp is None:
            print(f"окно {top} seq {shot['seq']}: файла снимка нет — пропуск")
            continue
        if top == MAP_SCREEN:              # столица, дипломатия — экраны внутри DLG_ISO: по классу
            dest = out_dir / "other" / f"{shot.get('top_class')}-{shot['seq']}.png"
        elif top.startswith("DLG_BATTLE") or top in taken:
            flipped = int(bool((shot.get("display") or {}).get("flipped_battle")))
            dest = out_dir / "other" / f"{top}-f{flipped}-{shot['seq']}.png"
        else:
            dest = out_dir / "windows" / f"{top}.png"
            taken.add(top)
        dest.parent.mkdir(parents=True, exist_ok=True)
        with Image.open(bmp) as im:
            im.convert("RGB").save(dest)
        print(f"окно {top} seq {shot['seq']} {bmp.name} -> {dest.relative_to(out_dir.parent)}")
        count += 1
    return count


def save_file(save: dict, wall: int | None, game: Path, copies: list[tuple[float, Path]]) -> tuple[Path | None, str]:
    name = save["name"]
    if name.lower() not in SHARED_SAVES:
        own = game / "SaveGame" / f"{name}.sg"
        if own.exists():
            return own, "своё имя"
    if wall is None:
        return None, "нет часов журнала (строк screenshot с time_ms)"
    fresh = [p for t, p in copies if wall - COPY_BEFORE_MS <= t <= wall + COPY_AFTER_MS]
    if fresh:
        return fresh[0], f"копия сторожа {fresh[0].name}"
    older = [p for t, p in copies if t < wall - COPY_BEFORE_MS]
    return (older[-1], f"повтор, копия {older[-1].name}") if older else (None, "копии сторожа нет")


def godot(extra: list[str]) -> tuple[int, str]:
    exe = os.environ.get("GODOT", r"C:\Tools\bin\godot.cmd")
    try:
        out = subprocess.run([exe, "--headless", "--path", str(REPO / "godot"), "res://scenes/dev/ShotCompare.tscn",
                              "--", *extra], capture_output=True, text=True, encoding="utf-8", errors="replace",
                             timeout=TIMEOUT_S)
    except subprocess.TimeoutExpired:
        return -1, "таймаут"
    lines = [x for x in (out.stdout + out.stderr).splitlines() if "shotcompare" in x or "ShotCompare:" in x]
    return out.returncode, "\n".join(lines)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("run", type=Path, help="каталог прогона (журнал и manifest.json)")
    ap.add_argument("--game", type=Path, default=Path(os.environ.get("D2_GAME_DIR", DEFAULT_GAME)),
                    help="копия игры с ScreenShots и SaveGame (по умолчанию game-dev)")
    ap.add_argument("--run", dest="execute", action="store_true", help="запустить сверку по каждой паре")
    args = ap.parse_args()
    args.run, args.game = args.run.resolve(), args.game.resolve()   # Godot работает из каталога godot/

    journals = journals_of(args.run)
    copies = sorted((p.stat().st_mtime * 1000, p) for p in args.run.glob("*.sg"))
    # Не в корпусе: харнессы обходят его *.sg рекурсивно, копии сохранений посчитались бы вторично.
    out_dir = REPO / ".cache" / "shots" / args.run.name
    out_dir.mkdir(parents=True, exist_ok=True)
    files = ShotFiles(args.game)
    for j in journals:                   # файлы — строго по порядку нажатий, до раскладки окон и карты
        for shot in events(j, {"screenshot"}):
            files.of(shot)
    windows = sum(export_windows(j, files, out_dir) for j in journals)
    found = [p for j in journals for p in pairs(j)]
    if not found:
        print(f"нет снимков карты с клеткой камеры (окон разложено: {windows})")
        return 0 if windows else 1

    bad = 0
    for n, (shot, save, wall) in enumerate(found, 1):
        cam = shot["camera"]
        bmp = files.of(shot)
        head = (f"#{n:02d} seq {shot['seq']} центр {cam['x']},{cam['y']} (dx {cam['dx']}, dy {cam['dy']}) "
                f"{bmp.name if bmp else 'файла нет'}")
        if not save:
            print(f"{head}: в этой партии нет сохранения — пропуск")
            bad += 1
            continue
        sg, how = save_file(save, wall, args.game, copies)
        if sg is None or bmp is None:
            print(f"{head}: {how if sg is None else 'нет файла снимка'} — пропуск")
            bad += 1
            continue
        shot_png, shot_sg = out_dir / f"shot{n:02d}{bmp.suffix}", out_dir / f"shot{n:02d}.sg"
        shutil.copyfile(bmp, shot_png)
        shutil.copyfile(sg, shot_sg)
        # dx/dy строки screenshot — сдвиг камеры в точках (совпал с найденным перебором 8/8, 27.09): сверка по нему, без поиска.
        extra = ["--sg", str(shot_sg), "--player", "human", "--center", f"{cam['x']},{cam['y']}",
                 "--shift", f"{cam['dx']},{cam['dy']}",
                 "--shot", str(shot_png), "--diff", str(out_dir / f"shot{n:02d}-diff.png")]
        print(f"{head}: сохранение «{save['name']}» ход {save['turn']} ({how})")
        if args.execute:
            code, text = godot(extra)
            print(f"    код {code}: {text or '(нет строки shotcompare)'}")
            bad += code != 0
        else:
            print("    godot --headless --path godot res://scenes/dev/ShotCompare.tscn -- " + " ".join(
                f'"{x}"' if " " in x else x for x in extra))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
