#!/usr/bin/env python3
"""ab.py — замер «до/после» двух рабочих копий вперемешку и вердикт попарно (бенч сцен run_all.py; перенесён из эталона в Э5).

Каждый прогон — свежий процесс. Порядок в паре чередуется (ABBA: до→после, после→до, …): у коротких замеров есть эффект
«второй в паре» и двугорбый разброс между процессами, из-за которых правило «все прогоны строго по одну сторону» даёт и ложные
«медленнее», и ложные «то же». Вердикт — по парам: «после» быстрее (или меньше памяти — mem_*) в k парах из n (критерий знаков,
p < 0,05) и медиана отношения дальше порога; рядом — медианы и разброс.

  py tools/bench/ab.py ../le-wt-base . --pairs 6
  py tools/bench/ab.py ../le-wt-base . --filter=map_open,mem_working_set --pairs 10
  py tools/bench/ab.py --report bench/ab-20261022-120000          # только пересчитать вердикт по сохранённым прогонам

Окружение — как у run_all.py: D2_DATA_DIR (импорт копии игры — у рабочей копии git его нет, указать корень с data/), D2_ASSETS_DIR,
GODOT. Пока идёт замер, машину ничем не нагружать.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import shutil
import statistics
import subprocess
import sys
from datetime import datetime
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]


def run(cmd: list[str], cwd: Path, assets: Path | None = None) -> None:
    env = {**os.environ, **({"D2_ASSETS_DIR": str(assets)} if assets else {})}
    proc = subprocess.run(cmd, cwd=cwd, env=env, capture_output=True, text=True,
                          encoding="utf-8", errors="replace", timeout=3600)
    if proc.returncode != 0:
        sys.exit(f"код {proc.returncode}: {' '.join(cmd)} в {cwd}\n{(proc.stdout + proc.stderr)[-2000:]}")


def once(tree: Path, out: Path, extra: list[str], assets: Path | None) -> None:
    run([sys.executable, "tools/bench/run_all.py", "ab-tmp", *extra], tree, assets)
    shutil.copyfile(tree / "bench" / "ab-tmp.json", out)


def scaled(name: str, value: float) -> float:
    """Время — мс (замеры в мкс), память mem_* — МБ (замеры в байтах)."""
    return value / 1048576 if name.startswith("mem_") else value / 1000


def unit(name: str) -> str:
    return "МБ" if name.startswith("mem_") else "мс"


def medians(path: Path) -> dict[str, float]:
    return {k: scaled(k, statistics.median(v)) for k, v in json.loads(path.read_text(encoding="utf-8"))["results"].items()}


def sign_p(wins: int, n: int) -> float:
    k = max(wins, n - wins)
    return min(1.0, 2 * sum(math.comb(n, i) for i in range(k, n + 1)) / 2 ** n)


def report(out: Path, threshold: float) -> int:
    pairs = []
    for b in sorted(out.glob("before*.json"), key=lambda p: int(p.stem[6:])):
        a = out / ("after" + b.stem[6:] + ".json")
        if a.exists():
            pairs.append((medians(b), medians(a)))
    if not pairs:
        sys.exit(f"в {out} нет пар before/after")
    names = sorted(set.intersection(*(set(b) & set(a) for b, a in pairs)), key=lambda n: -statistics.median(b[n] for b, _ in pairs))
    slower = False
    print(f"пар: {len(pairs)}; порог {threshold}%; вердикт — критерий знаков (p < 0,05) и медиана отношения")
    for name in names:
        before = [b[name] for b, _ in pairs]
        after = [a[name] for _, a in pairs]
        n = len(pairs)
        wins = sum(y < x for x, y in zip(before, after, strict=True))
        ratio = statistics.median(y / x for x, y in zip(before, after, strict=True))
        p = sign_p(wins, n)
        verdict = "same"
        if p < 0.05 and abs(ratio - 1) * 100 > threshold:
            verdict = "FASTER" if wins > n / 2 else "SLOWER"
        slower |= verdict == "SLOWER"
        print(f"{name:30} {statistics.median(before):9.2f} {unit(name)} [{min(before):.2f}–{max(before):.2f}] → "
              f"{statistics.median(after):9.2f} {unit(name)} [{min(after):.2f}–{max(after):.2f}]  {(ratio - 1) * 100:+6.1f}%  "
              f"{wins}/{n} p={p:.3f}  {verdict}")
    return 1 if slower else 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("before", nargs="?", type=Path)
    ap.add_argument("after", nargs="?", type=Path)
    ap.add_argument("--filter", default="", help="имена бенчмарков через запятую (как у run_all.py)")
    ap.add_argument("--pairs", type=int, default=6)
    ap.add_argument("--threshold", type=float, default=5.0)
    ap.add_argument("--out", type=Path, default=None, help="каталог прогонов (по умолчанию bench/ab-<время>)")
    ap.add_argument("--report", type=Path, default=None, help="только вердикт по сохранённым прогонам")
    ap.add_argument("--assets-before", type=Path, default=None, help="D2_ASSETS_DIR стороны «до» (графика другой сборки)")
    ap.add_argument("--assets-after", type=Path, default=None, help="D2_ASSETS_DIR стороны «после»")
    a = ap.parse_args()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")   # консоль Windows (cp1251) не печатает «→» отчёта
    if a.report:
        return report(a.report, a.threshold)
    if not (a.before and a.after):
        ap.error("нужны две рабочие копии: до и после")
    out = a.out or REPO / "bench" / f"ab-{datetime.now():%Y%m%d-%H%M%S}"
    out.mkdir(parents=True, exist_ok=True)
    extra = [f"--filter={a.filter}"] if a.filter else []
    trees = {"before": a.before.resolve(), "after": a.after.resolve()}
    for i in range(1, a.pairs + 1):
        order = ("before", "after") if i % 2 else ("after", "before")
        for side in order:
            once(trees[side], out / f"{side}{i}.json", extra, a.assets_before if side == "before" else a.assets_after)
        print(f"пара {i} из {a.pairs}", flush=True)
    print(f"прогоны: {out}")
    return report(out, a.threshold)


if __name__ == "__main__":
    sys.exit(main())
