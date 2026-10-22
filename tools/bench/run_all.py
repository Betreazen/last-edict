#!/usr/bin/env python3
"""run_all.py — бенч сцен Last Edict одной командой (Э5, задача 0): без корпуса эталона, на своих данных.

Сохранение середины партии для замеров карты — снимок партии стенда (tools/CheckSaves: новая партия меню «Power to Corrupt» за Кланы,
ИИ за всех, день 30, постоянное зерно); бой — бой-образец сценария импорта (godot/bench/BenchScene.cs). Нужен импорт копии игры
(data/ в корне или D2_DATA_DIR) и графика (D2_ASSETS_DIR).

Пишет bench/<имя>.json — формат bench.gd скилла gdscript-knowledge (заголовок, замеры по имени; время — мкс, mem_* — байты).
Сравнение двух файлов:
  godot --headless --path godot --script res://bench/bench_compare.gd -- ../bench/before.json ../bench/after.json [порог-%]

Базовая и повторная линии — на той же машине, без другой тяжёлой работы, одной и той же сборкой.

  py tools/bench/run_all.py before
  py tools/bench/run_all.py after --filter=map_open,battle_open
"""
from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SCENARIO, PLAYER, DAY = "Power to Corrupt", "S073PL000a", 30


def run(cmd: list[str], env: dict[str, str] | None = None) -> None:
    print("$", " ".join(cmd), flush=True)
    proc = subprocess.run(cmd, cwd=REPO, env={**os.environ, **(env or {})}, timeout=3600)
    if proc.returncode != 0:
        sys.exit(f"код {proc.returncode}: {' '.join(cmd)}")


def main() -> int:
    name = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith("--") else "before"
    extra = [a for a in sys.argv[1:] if a.startswith("--filter=")]
    out = REPO / "bench"
    out.mkdir(exist_ok=True)
    godot = os.environ.get("GODOT", r"C:\Tools\bin\godot.cmd")
    saves = REPO / ".cache" / "bench" / "saves"
    run(["dotnet", "build", "tools/CheckSaves", "-c", "Release", "-v", "q", "-nologo"])
    run(["dotnet", "build", "godot/LastEdict.Godot.csproj", "-v", "q", "-nologo"])
    run(["dotnet", str(REPO / "tools/CheckSaves/bin/Release/net10.0/CheckSaves.dll"), "--scenario", SCENARIO, "--player", PLAYER,
         "--days", str(DAY), "--out", str(saves)])
    state = saves / f"{SCENARIO}-d{DAY:02}.d2save"
    run([godot, "--headless", "--path", "godot", "res://bench/BenchScene.tscn", "--", "--no-mods", f"--save={out / f'{name}.json'}",
         *extra], {"D2_BENCH_STATE": str(state)})
    return 0


if __name__ == "__main__":
    sys.exit(main())
