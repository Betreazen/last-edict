#!/usr/bin/env python3
"""balance_report.py — метрики стенда баланса (tools/BalanceStand, docs/own-rules.md §5) по JSONL: строка на игрока партии.

  py tools/balance_report.py .cache/balance/six-crowns.jsonl [ещё.jsonl …] [--json out.json] [--md out.md]

Метрики (доли — с 95 % интервалом Уилсона, как у design_metrics.py скилла game-design):
  * доля побед по расе — победа: в решённой партии — победа команды, иначе — наибольший счёт (объекты и армия);
  * первый ход — доля побед того, кто ходит первым в дне, против прочих;
  * снежный ком — доля побед лидера по счёту на четверти партии против остальных (насколько ранний перевес решает);
  * длина партий и доля решённых захватом.
Строки с тем же сценарием и зерном — одна партия.
"""
from __future__ import annotations

import argparse
import json
import math
import statistics
import sys
from collections import defaultdict
from pathlib import Path

Z95 = 1.959964


def wilson(k: int, n: int) -> tuple[float, float]:
    if n == 0:
        return 0.0, 1.0
    p = k / n
    denom = 1 + Z95 * Z95 / n
    center = (p + Z95 * Z95 / (2 * n)) / denom
    half = Z95 * math.sqrt(p * (1 - p) / n + Z95 * Z95 / (4 * n * n)) / denom
    return max(0.0, center - half), min(1.0, center + half)


def rate(rows: list[dict]) -> dict:
    k, n = sum(1 for r in rows if r["won"]), len(rows)
    lo, hi = wilson(k, n)
    return {"n": n, "wins": k, "rate": k / n if n else 0.0, "ci95": [lo, hi]}


def report(rows: list[dict]) -> dict:
    games = {(r["scenario"], r["seed"]): r for r in rows}
    by_race = defaultdict(list)
    for r in rows:
        by_race[r["race"]].append(r)
    first = [r for r in rows if r["first"]]
    rest = [r for r in rows if not r["first"]]
    led = [r for r in rows if r["ledEarly"]]
    trailed = [r for r in rows if not r["ledEarly"]]
    lengths = [g["days"] for g in games.values()]
    out = {
        "games": len(games),
        "scenarios": sorted({s for s, _ in games}),
        "decided": sum(1 for g in games.values() if g["decided"]) / len(games) if games else 0.0,
        "days": {"median": statistics.median(lengths) if lengths else 0, "min": min(lengths, default=0), "max": max(lengths, default=0)},
        "race": {race: rate(rs) for race, rs in sorted(by_race.items())},
        "first": {"first": rate(first), "others": rate(rest)},
        "snowball": {"ledEarly": rate(led), "others": rate(trailed)},
    }
    races = [v["rate"] for v in out["race"].values()]
    out["race_spread"] = max(races) - min(races) if races else 0.0
    return out


def percent(m: dict) -> str:
    return f"{100 * m['rate']:5.1f} % [{100 * m['ci95'][0]:.0f}–{100 * m['ci95'][1]:.0f}] из {m['n']}"


def markdown(out: dict) -> str:
    lines = [f"Партий {out['games']} ({', '.join(out['scenarios'])}), решено захватом {100 * out['decided']:.0f} %, "
             f"длина — медиана {out['days']['median']} дн. ({out['days']['min']}–{out['days']['max']})", "",
             "| Раса | Доля побед [95 %] |", "|---|---|"]
    lines += [f"| {race} | {percent(m)} |" for race, m in out["race"].items()]
    lines += ["", f"Разброс рас: {100 * out['race_spread']:.1f} п. п.",
              f"Первый ход: {percent(out['first']['first'])} против {percent(out['first']['others'])}",
              f"Снежный ком (лидер на четверти партии): {percent(out['snowball']['ledEarly'])} против {percent(out['snowball']['others'])}"]
    return "\n".join(lines) + "\n"


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("runs", nargs="+", help="JSONL стенда")
    ap.add_argument("--json", help="записать метрики в JSON")
    ap.add_argument("--md", help="записать сводку в Markdown")
    args = ap.parse_args(argv)
    rows = [json.loads(line) for path in args.runs for line in Path(path).read_text(encoding="utf-8").splitlines() if line.strip()]
    if not rows:
        print("нет строк", file=sys.stderr)
        return 2
    out = report(rows)
    text = markdown(out)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
    print(text, end="")
    if args.json:
        Path(args.json).write_text(json.dumps(out, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    if args.md:
        Path(args.md).write_text(text, encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
