# tools/sprites_export.py — воспроизводимый экспорт анимации юнита из .ff в PNG.
# Зовёт ff_extract.py из disciples-modding (../disciples-modding относительно этого репо).
# Запуск по умолчанию (юнит G000UU0001 из BatUnits.ff):
#   python tools/sprites_export.py
# Свой архив/фильтр:
#   python tools/sprites_export.py --archive game-pristine/Imgs/Icons.ff --filter "^BORDER"
import argparse
import os
import subprocess
import sys
from pathlib import Path

MODDING_REPO = Path(__file__).resolve().parents[2] / "disciples-modding"
FF_EXTRACT = MODDING_REPO / "hd-mod" / "tools" / "ff_extract.py"
GAME_DIR = Path(os.environ.get("D2_GAME_DIR", str(MODDING_REPO / "game-pristine")))

if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--archive", default=str(GAME_DIR / "Imgs" / "BatUnits.ff"))
    p.add_argument("--filter", default="^G000UU0001")
    p.add_argument("--out", default=str(Path(__file__).resolve().parents[1] / "assets"))
    args = p.parse_args()
    # cwd у процесса — соседний репозиторий: --archive по шапке задаётся от него, а --out — от текущего каталога,
    # иначе относительный --out ушёл бы в чужой репозиторий
    out = Path(args.out).resolve()
    cmd = [sys.executable, str(FF_EXTRACT), args.archive, "-o", str(out), "--anims", "--filter", args.filter]
    sys.exit(subprocess.run(cmd, cwd=MODDING_REPO).returncode)
