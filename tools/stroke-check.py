#!/usr/bin/env python3
"""stroke-check.py — толщина линий растра после масштаба экрана в окно, числом (прогон 3 пользователя 11.10, пункт 1).

Экран 800×600 вписывается в окно 1280×720 масштабом 1,2. Прежний показ (дробный масштаб узлом, ближайший сосед) делает линию в одну
точку разметки то одной, то двумя точками окна; «чёткий» (godot/scenes/ui/ScreenStage.cs: целое увеличение в буфере + сжатие по
площади) — каждой линии один и тот же вес 1,2 точки окна, размыты только края.

Два способа:

* модель (по умолчанию, без окна): раскладка показа берётся у сцены (`UiFlow.tscn --scale … --window-size WxH` в headless печатает
  `stage … буфер=… масштаб=…`), а сжатие считается здесь той же формулой, что шейдер показа (усреднение по площади точки окна);
* `--window` — по-настоящему: сцена с окном рисует проверочную картинку (`--stage-pattern`: чёрные линии в точку разметки на белом)
  и снимает окно (`--stage-shot`), а здесь меряется вес каждой линии в снимке. Окно открывается на ~3 с на режим.

Вес линии — сумма затемнения (1 − яркость/255) поперёк неё; в точках разметки — делённый на масштаб окна k. Выход 0, если у «чёткого»
разброс весов не больше --tolerance (по умолчанию 0,05 точки разметки), а у «как прежде» он есть (иначе проверка ничего не различает).

  py -3 tools/stroke-check.py                    # модель, окно 1280×720
  py -3 tools/stroke-check.py --window           # снимки окна
  py -3 tools/stroke-check.py --size 1920x1080
"""
from __future__ import annotations

import argparse
import math
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np

REPO = Path(__file__).resolve().parents[1]
MODES = ("nearest", "sharp", "integer")
STAGE = re.compile(r"stage (?:shot=\S+ )?показ=(?P<mode>\S+) буфер=(?P<w>\d+)x(?P<h>\d+) масштаб=(?P<scale>[\d.]+) "
                   r"угол=(?P<x>-?\d+),(?P<y>-?\d+) фильтр=(?P<filter>\S+)")
DESIGN = (800, 600)


def godot(args: list[str], window: bool) -> str:
    exe = os.environ.get("GODOT", r"C:\Tools\bin\godot.cmd")
    cmd = [exe, *([] if window else ["--headless"]), *args]
    proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120)
    return (proc.stdout or "") + (proc.stderr or "")


def pattern() -> np.ndarray:
    """Та же проверочная картинка, что у сцены (ScreenStage.AddPattern): 1 — чёрная точка."""
    img = np.zeros((DESIGN[1], DESIGN[0]))
    for x in range(10, DESIGN[0] - 10, 7):
        img[10:290, x] = 1
    for y in range(310, DESIGN[1] - 10, 7):
        img[y, 10:DESIGN[0] - 10] = 1
    return img


def model(stage_w: int, stage_h: int, scale: float, fx: int, fy: int, size: tuple[int, int], filtered: bool) -> np.ndarray:
    """Окно по модели показа: экран вписан в буфер ближайшим соседом, буфер ложится в окно со сжатием по площади (шейдер) или точка
    в точку. Считается только строка окна посередине верхней половины — вертикальные линии — этого достаточно."""
    k = min(stage_w / DESIGN[0], stage_h / DESIGN[1])
    ax, ay = (stage_w - DESIGN[0] * k) / 2, (stage_h - DESIGN[1] * k) / 2
    src = pattern()
    row_design = 150
    # Строка буфера: точка буфера → точка разметки ближайшим соседом (Sprite2D со Scale k и фильтром Nearest).
    stage_row = np.zeros(stage_w)
    for x in range(stage_w):
        d = math.floor((x + 0.5 - ax) / k)
        if 0 <= d < DESIGN[0]:
            stage_row[x] = src[row_design, d]
    out = np.zeros(size[0])
    for X in range(size[0]):
        if not filtered:
            sx = math.floor((X + 0.5 - fx) / scale)
            out[X] = stage_row[sx] if 0 <= sx < stage_w else 0
            continue
        t0, t1 = (X - fx) / scale, (X + 1 - fx) / scale
        acc = 0.0
        for i in range(math.floor(t0 + 1e-4), math.ceil(t1 - 1e-4)):
            w = min(t1, i + 1) - max(t0, i)
            acc += stage_row[min(max(i, 0), stage_w - 1)] * w
        out[X] = acc / (t1 - t0)
    return out


def weights(row: np.ndarray, k: float, origin: float) -> list[float]:
    """Вес каждой вертикальной линии проверочной картинки в строке окна (затемнение 0…1), в точках разметки."""
    result = []
    for x in range(10, DESIGN[0] - 10, 7):
        lo, hi = int(origin + (x - 3) * k), int(origin + (x + 4) * k)   # соседей до следующей линии не задевает
        result.append(float(row[max(lo, 0):max(hi, 0)].sum()) / k)
    return result


def measure(mode: str, size: tuple[int, int], window: bool, tmp: Path) -> tuple[list[float], str]:
    common = ["--path", str(REPO / "godot"), "res://scenes/ui/UiFlow.tscn", "--", "--no-mods", "--scale", mode]
    if not window:
        out = godot(common + ["--window-size", f"{size[0]}x{size[1]}", "--input", "wait 1"], window=False)
        m = STAGE.search(out)
        if m is None:
            raise SystemExit(f"{mode}: нет строки stage\n{out}")
        row = model(int(m["w"]), int(m["h"]), float(m["scale"]), int(m["x"]), int(m["y"]), size, m["filter"] == "area")
    else:
        shot = tmp / f"stroke-{mode}.png"
        out = godot(["--resolution", f"{size[0]}x{size[1]}", *common, "--stage-pattern", "--stage-shot", str(shot)], window=True)
        m = STAGE.search(out)
        if m is None or not shot.exists():
            raise SystemExit(f"{mode}: снимка нет\n{out}")
        from PIL import Image
        img = np.asarray(Image.open(shot).convert("L"), dtype=float)
        if img.shape[1] != size[0]:
            raise SystemExit(f"{mode}: снимок {img.shape[1]}×{img.shape[0]}, ожидалось окно {size[0]}×{size[1]}")
        k_window = min(size[0] / DESIGN[0], size[1] / DESIGN[1])
        top = (size[1] - DESIGN[1] * k_window) / 2
        row = 1 - img[int(top + 150 * k_window)] / 255.0
    k = min(size[0] / DESIGN[0], size[1] / DESIGN[1])
    if mode == "integer":
        k = max(1, math.floor(k + 1e-4))
    origin = (size[0] - DESIGN[0] * k) / 2
    return weights(row, k, origin), m.group(0)


def main(argv: list[str]) -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--window", action="store_true", help="снимать окно (иначе модель показа)")
    p.add_argument("--size", default="1280x720", help="размер окна WxH")
    p.add_argument("--tolerance", type=float, default=0.05, help="допустимый разброс веса линии у «чёткого», точки разметки")
    a = p.parse_args(argv)
    size = tuple(int(v) for v in a.size.split("x"))
    spread = {}
    with tempfile.TemporaryDirectory() as tmp:
        for mode in MODES:
            w, stage = measure(mode, size, a.window, Path(tmp))
            spread[mode] = max(w) - min(w)
            print(f"stroke {mode} {'окно' if a.window else 'модель'} {size[0]}x{size[1]} линий={len(w)} вес_мин={min(w):.3f} "
                  f"вес_макс={max(w):.3f} разброс={spread[mode]:.3f} | {stage}")
    ok = spread["sharp"] <= a.tolerance and spread["integer"] <= a.tolerance and spread["nearest"] > a.tolerance
    print("stroke итог: " + ("линии одной толщины у «чёткого» и «целого»" if ok else "НЕ СХОДИТСЯ"))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
