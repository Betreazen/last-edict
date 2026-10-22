#!/usr/bin/env python3
r"""trx_diff.py — сверка выводов всех тестов двух прогонов `dotnet test --logger trx`: исход, вывод (StdOut) и сообщение ошибки каждого
теста, строка в строку. Так проверяется каждый шаг рефакторинга Э2 (`docs/session-e2-prompt.md`): харнессы обязаны печатать прежние числа.

Время из вывода вырезается (`12,3 с`, `450 ms`, `0:01:02`): оно меняется от прогона к прогону и сравнению не подлежит.

  python tools/trx_diff.py .cache/trx/до .cache/trx/после            # итог и первые расхождения
  python tools/trx_diff.py .cache/trx/до .cache/trx/после --all      # все расхождения построчно
Код выхода 0 — совпало всё, 1 — есть расхождения.
"""
from __future__ import annotations

import argparse
import difflib
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

# Время: «12,3 с», «12.3 s», «450 мс», «450 ms», «за 3 с», «0:01:02.5», «00:00:01.2345678».
TIME = re.compile(r"\b\d+(?:[.,]\d+)?\s?(?:мс|ms|с|s|сек|мин|min)\b|\b\d{1,2}:\d{2}:\d{2}(?:[.,]\d+)?\b")


def load(root: Path) -> dict[str, tuple[str, str]]:
    """Тест (полное имя с аргументами) → (исход, вывод и ошибка без времени)."""
    files = sorted(root.rglob("*.trx")) if root.is_dir() else [root]
    if not files:
        raise SystemExit(f"нет trx в {root}")
    tests: dict[str, tuple[str, str]] = {}
    for f in files:
        tree = ET.parse(f)
        names: dict[str, str] = {}
        for d in tree.iterfind(".//t:UnitTest", NS):
            method = d.find("t:TestMethod", NS)
            names[d.get("id", "")] = f"{method.get('className', '') if method is not None else ''}.{d.get('name', '')}"
        for r in tree.iterfind(".//t:UnitTestResult", NS):
            name = names.get(r.get("testId", ""), r.get("testName", ""))
            out = r.find("t:Output", NS)
            text = []
            if out is not None:
                for tag in ("t:StdOut", "t:ErrorInfo/t:Message", "t:ErrorInfo/t:StackTrace"):
                    node = out.find(tag, NS)
                    if node is not None and node.text:
                        text.append(TIME.sub("<t>", node.text) if tag == "t:StdOut" else node.text)
            key = name
            n = 2
            while key in tests:   # одноимённые строки теорий с одинаковым показом аргументов
                key = f"{name} #{n}"
                n += 1
            tests[key] = (r.get("outcome", ""), "\n".join(text))
    return tests


def main(argv: list[str]) -> int:
    sys.stdout.reconfigure(encoding="utf-8")  # type: ignore[union-attr]
    ap = argparse.ArgumentParser(description="Сверка выводов тестов двух прогонов trx.")
    ap.add_argument("before", type=Path)
    ap.add_argument("after", type=Path)
    ap.add_argument("--all", action="store_true", help="все расхождения, а не первые пять")
    a = ap.parse_args(argv)
    before, after = load(a.before), load(a.after)
    only_before = sorted(before.keys() - after.keys())
    only_after = sorted(after.keys() - before.keys())
    changed = sorted(k for k in before.keys() & after.keys() if before[k] != after[k])
    print(f"тестов: до {len(before)}, после {len(after)}; общих {len(before.keys() & after.keys())}, "
          f"расходятся {len(changed)}, только до {len(only_before)}, только после {len(only_after)}")
    limit = None if a.all else 5
    for k in only_before[:limit]:
        print(f"  только до: {k}")
    for k in only_after[:limit]:
        print(f"  только после: {k}")
    for k in changed[:limit]:
        (o1, t1), (o2, t2) = before[k], after[k]
        print(f"\n== {k}: {o1} → {o2}")
        diff = difflib.unified_diff(t1.splitlines(), t2.splitlines(), "до", "после", lineterm="", n=1)
        lines = list(diff)
        print("\n".join(lines if a.all else lines[:40]))
    return 0 if not (changed or only_before or only_after) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
