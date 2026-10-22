#!/usr/bin/env python3
r"""test_times.py — время классов тестов по отчётам trx (`dotnet test --logger trx`).

xUnit гонит тесты одного класса подряд, а классы — параллельно, поэтому прогон не может кончиться раньше самого долгого класса:
его и надо делить (`DailySaveAiTurnsHarnessTests_Legions`). Класс в таблице — сумма длительностей его тестов и окно от начала первого
до конца последнего (окно больше суммы — класс ждал потоков).

  python tools/test_times.py .cache/trx                 # все trx каталога: 10 самых долгих классов
  python tools/test_times.py .cache/trx --top 0 --csv   # все классы, CSV
"""
from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
REPO = Path(__file__).resolve().parents[1]
DURATIONS = REPO / "tests" / "test-durations.json"


@dataclass(frozen=True)
class ClassTime:
    assembly: str
    name: str
    tests: int
    seconds: float   # сумма длительностей тестов
    span: float      # от начала первого теста до конца последнего


def _duration(text: str) -> float:
    h, m, s = text.split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)


def _stamp(text: str) -> datetime:
    # «2026-10-21T17:40:01.1234567+03:00»: дробь — до микросекунд, как понимает fromisoformat.
    head, _, rest = text.partition(".")
    if not rest:
        return datetime.fromisoformat(text)
    digits = "".join(c for c in rest if c.isdigit())
    zone = rest[len(digits):]
    return datetime.fromisoformat(f"{head}.{digits[:6].ljust(6, '0')}{zone}")


def classes(trx_files: list[Path]) -> list[ClassTime]:
    """Классы всех отчётов, самые долгие (по окну) сверху."""
    out: list[ClassTime] = []
    for trx in trx_files:
        root = ET.parse(trx).getroot()
        owner: dict[str, tuple[str, str]] = {}   # id теста → (сборка, класс)
        for unit in root.iterfind("t:TestDefinitions/t:UnitTest", NS):
            method = unit.find("t:TestMethod", NS)
            if method is not None:
                owner[unit.get("id", "")] = (Path(method.get("codeBase") or "").stem, method.get("className") or "?")
        acc: dict[tuple[str, str], list] = {}   # (сборка, класс) → [тестов, сумма, начало, конец]
        for r in root.iterfind("t:Results/t:UnitTestResult", NS):
            a = acc.setdefault(owner.get(r.get("testId", ""), ("?", "?")), [0, 0.0, None, None])
            a[0] += 1
            a[1] += _duration(r.get("duration") or "0:0:0")
            start, end = r.get("startTime"), r.get("endTime")
            if start and end:
                t0, t1 = _stamp(start), _stamp(end)
                a[2] = t0 if a[2] is None or t0 < a[2] else a[2]
                a[3] = t1 if a[3] is None or t1 > a[3] else a[3]
        out += [ClassTime(asm, name.rsplit(".", 1)[-1], n, secs, (t1 - t0).total_seconds() if t0 and t1 else secs)
                for (asm, name), (n, secs, t0, t1) in acc.items()]
    return sorted(out, key=lambda c: -c.span)


def table(rows: list[ClassTime], top: int) -> str:
    shown = rows[:top] if top else rows
    lines = [f"  {c.span:6.1f} с окно {c.seconds:6.1f} с сумма {c.tests:4d} тестов  {c.assembly}: {c.name}" for c in shown]
    return "\n".join(lines)


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description="Время классов тестов по trx.")
    ap.add_argument("path", type=Path, help="файл .trx или каталог с ними")
    ap.add_argument("--top", type=int, default=10, help="сколько классов (0 — все)")
    ap.add_argument("--csv", action="store_true", help="CSV: сборка;класс;тестов;сумма;окно")
    ap.add_argument("--durations", nargs="?", const=DURATIONS, type=Path, metavar="ФАЙЛ",
                    help="обновить время классов для порядка запуска (tests/Shared/LongestFirst.cs; по умолчанию "
                         f"{DURATIONS.relative_to(REPO)}): классы этих отчётов — заново, прочие — как были; ключ — после пути к trx")
    args = ap.parse_args(argv)
    files = sorted(args.path.rglob("*.trx")) if args.path.is_dir() else [args.path]
    rows = classes(files)
    if args.durations:
        try:   # частичный прогон (--affected, --smoke) не стирает время остальных классов
            by_asm: dict[str, dict[str, float]] = json.loads(args.durations.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            by_asm = {}
        for c in rows:
            by_asm.setdefault(c.assembly, {})[c.name] = round(max(c.span, c.seconds), 1)   # сумма больше окна — тесты ждали общий кэш
        by_asm = {a: dict(sorted(v.items())) for a, v in sorted(by_asm.items())}
        args.durations.write_text(json.dumps(by_asm, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
        print(f"обновлено {len(rows)} классов, всего {sum(len(v) for v in by_asm.values())} в {args.durations}")
        return 0
    if args.csv:
        shown = rows[:args.top] if args.top else rows
        print("\n".join(f"{c.assembly};{c.name};{c.tests};{c.seconds:.2f};{c.span:.2f}" for c in shown))
    else:
        print(f"{len(rows)} классов в {len(files)} отчётах; самые долгие:\n{table(rows, args.top)}")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main(sys.argv[1:]))
