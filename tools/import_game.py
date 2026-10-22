#!/usr/bin/env python3
"""import_game.py — импорт данных порта из копии Disciples II (команда разработчика, docs/publishing.md).

Всё, что выведено из файлов игры (data/rules, data/game, data/map, data/scenarios, data/sound, data/video, data/gfx, данные игры в
data/ui, графика, звук, ролики и шрифты в assets/), собирается заново импортом на C# (src/Import.Legacy, GameImporter) — тем же, что
игра делает у игрока при первом запуске. Картинки кодирует движок (WebP без потерь), поэтому импорт идёт через Godot без окна:

    godot --headless --path godot -- --import --game-dir <копия> [--l10n <слой>] --out <корень> [--steps …]

Запуск (из корня репозитория):
  py tools/import_game.py                              # копия — D2_GAME_DIR или ../disciples-modding/game-pristine, выход — репозиторий
  py tools/import_game.py --game "C:/GOG Games/Disciples II" --l10n "D:/Disciples II RU" --out C:/tmp/d2
  py tools/import_game.py --steps rules,scenarios      # выборочно (шаги: rules game scenarios screens strings texts capital leaders
                                                       # fonts sound ui gfx video)

Слой русификации (--l10n): корень русской копии игры (Globals/Tglobal.dbf в кириллице) — даёт русские строки, шрифты с кириллицей и
переводы сценариев; по умолчанию — ../disciples-vanilla/l10n/russobit, если он есть. Ролики требуют ffmpeg (FFMPEG или PATH), без него
шаг video пропускается. Godot — переменная GODOT, иначе C:/Tools/bin/godot.cmd или godot из PATH.
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
PROJECTS = REPO.parent


def godot() -> str:
    """Исполняемый файл Godot. Шим .cmd разворачивается в сам .exe: через cmd.exe пути с пробелами в аргументах ломаются."""
    for candidate in (os.environ.get("GODOT"), r"C:\Tools\bin\godot.cmd", shutil.which("godot")):
        if not candidate or not Path(candidate).exists():
            continue
        if candidate.lower().endswith((".cmd", ".bat")):
            text = Path(candidate).read_text(encoding="utf-8", errors="replace")
            exe = next((os.path.expandvars(part) for part in text.split('"') if part.lower().endswith(".exe")), None)
            if exe and Path(exe).exists():
                return exe
            continue
        return candidate
    raise SystemExit("нет Godot 4.7.2 mono: укажите переменную GODOT")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--game", type=Path, default=Path(os.environ.get("D2_GAME_DIR") or PROJECTS / "disciples-modding" / "game-pristine"))
    default_l10n = PROJECTS / "disciples-vanilla" / "l10n" / "russobit"
    ap.add_argument("--l10n", type=Path, default=default_l10n if default_l10n.is_dir() else None)
    ap.add_argument("--no-l10n", action="store_true", help="без слоя русификации (только английские строки)")
    ap.add_argument("--out", type=Path, default=REPO, help="корень выхода: data/ и assets/ (по умолчанию — репозиторий)")
    ap.add_argument("--steps", default="", help="шаги через запятую (пусто — все)")
    ap.add_argument("--no-build", action="store_true", help="не собирать godot/D2.Godot.csproj перед импортом")
    args = ap.parse_args()

    if not args.no_build:
        subprocess.run(["dotnet", "build", str(REPO / "godot" / "D2.Godot.csproj"), "-v", "q", "-nologo"], check=True)
    cmd = [godot(), "--headless", "--path", str(REPO / "godot"), "--", "--import", "--game-dir", str(args.game.resolve()),
           "--out", str(args.out.resolve())]
    if args.l10n and not args.no_l10n:
        cmd += ["--l10n", str(args.l10n.resolve())]
    if args.steps:
        cmd += ["--steps", args.steps]
    if os.environ.get("FFMPEG"):
        cmd += ["--ffmpeg", os.environ["FFMPEG"]]
    print(" ".join(f'"{c}"' if " " in c else c for c in cmd), flush=True)
    return subprocess.run(cmd).returncode


if __name__ == "__main__":
    sys.exit(main())
