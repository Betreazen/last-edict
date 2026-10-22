#!/usr/bin/env python3
r"""check.py — проверка рабочей копии одним вызовом: сборка, тесты, ui-render-check и pytest по необходимости, итог таблицей.

Режимы:
  python tools/check.py               # = --affected: затронутые правкой классы + smoke (tools/affected_tests.py), в цикле работы
  python tools/check.py --full        # сборка как в CI (анализаторы, предупреждения — ошибки), все тесты; один раз в конце
  python tools/check.py --fast        # все тесты, кроме синтетических карт (Category!=Synth)
  python tools/check.py --synth       # только синтетические карты (Category=Synth)
  python tools/check.py --smoke       # тесты без корпуса — с ПУСТЫМ корпусом: пропуск или падение = класс без категории Corpus/Synth

ui-render-check запускается, когда правка против main задела вид (сцены Godot, данные интерфейса и графики, код вида — решает
`tools/affected_tests.py`); `--ui` — всегда, `--no-ui` — никогда. Перед ним собирается `godot/LastEdict.Godot.csproj`. pytest
(`tests/test_*.py`) — когда тронуты питоновские инструменты, которые эти тесты импортируют.

Корпус и графика: переменные `D2_CORPUS_DIR` и `D2_ASSETS_DIR`; не заданы, а в рабочей копии их нет (`../dg-wt-*`) — берутся
каталоги основного репозитория рядом (`../disciples-godot/corpus`, `../disciples-godot/assets`), только для чтения.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SOLUTION = "LastEdict.sln"

# Итог dotnet test одной сборки: «Пройден!   : не пройдено 0, пройдено 6, пропущено 0, всего 6» и английский вид
# «Passed!  - Failed: 0, Passed: 6, Skipped: 0, Total: 6».
TOTALS_RU = re.compile(r"не пройдено\s+(\d+),\s+пройдено\s+(\d+),\s+пропущено\s+(\d+),\s+всего\s+(\d+).*?-\s+(\S+\.dll)")
TOTALS_EN = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+).*?-\s+(\S+\.dll)")


def data_env() -> dict[str, str]:
    """Окружение прогона: корпус и графика — из переменных или основного репозитория рядом (в рабочей копии их нет)."""
    # Дочерние скрипты печатают кириллицу и знаки вне cp1251 в трубу; dotnet — по-английски (итог разбирается регуляркой).
    env = dict(os.environ, PYTHONIOENCODING="utf-8", DOTNET_CLI_UI_LANGUAGE="en", VSLANG="1033")
    main = REPO.parent / "disciples-godot"
    local_corpus = REPO / "corpus"
    if not env.get("D2_CORPUS_DIR") and not any(local_corpus.glob("2*/*.jsonl")) and (main / "corpus").is_dir():
        env["D2_CORPUS_DIR"] = str(main / "corpus")
    if not env.get("D2_ASSETS_DIR") and not (REPO / "assets").is_dir() and (main / "assets").is_dir():
        env["D2_ASSETS_DIR"] = str(main / "assets")
    return env


def run(title: str, cmd: list[str], env: dict[str, str], results: list[dict], echo: bool = False) -> tuple[bool, str]:
    """Шаг: команда, время, итог. `echo` — вывод построчно по ходу (долгие шаги), иначе — хвост только при провале."""
    print(f"\n== {title}: {' '.join(cmd)}", flush=True)
    t0 = time.monotonic()
    proc = subprocess.Popen(cmd, cwd=REPO, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                            encoding="utf-8", errors="replace")
    lines = []
    try:
        for line in proc.stdout:
            lines.append(line)
            if echo:
                print(line, end="", flush=True)
        proc.wait()
    except KeyboardInterrupt:
        # Ctrl-C: снять дерево процессов (dotnet test держит testhost, ui-render-check — Godot), не оставлять сирот.
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)], capture_output=True, check=False)
        raise
    out = "".join(lines)
    secs = time.monotonic() - t0
    ok = proc.returncode == 0
    detail = ""
    totals = [m.groups() for m in TOTALS_RU.finditer(out)] + [m.groups() for m in TOTALS_EN.finditer(out)]
    if totals:
        failed, passed, skipped, total = (sum(int(t[i]) for t in totals) for i in range(4))
        detail = f"{passed}/{total} пройдено, упало {failed}, пропущено {skipped}"
    elif not ok:
        detail = out.strip().splitlines()[-1] if out.strip() else ""
    if not ok and not echo:
        print("".join(lines[-40:]))
    print(f"   {'OK' if ok else 'ПРОВАЛ'} за {secs:.0f} с{(' — ' + detail) if detail else ''}", flush=True)
    results.append({"step": title, "ok": ok, "seconds": secs, "detail": detail, "totals": bool(totals),
                    "skipped": sum(int(t[2]) for t in totals) if totals else 0})
    return ok, out


def tests(title: str, filt: str | None, env: dict[str, str], results: list[dict]) -> bool:
    trx = REPO / ".cache" / "trx" / time.strftime("%Y%m%d-%H%M%S")
    for old in sorted((REPO / ".cache" / "trx").glob("2*"))[:-9]:   # хранить отчёты 10 последних прогонов
        shutil.rmtree(old, ignore_errors=True)
    cmd =["dotnet", "test", SOLUTION, "-c", "Release", "--no-build", "--logger", "trx", "--results-directory", str(trx)]
    if filt:
        cmd += ["--filter", filt]
    ok = run(title, cmd, env, results)[0]
    if ok and not results[-1]["totals"]:   # вывод не разобран — не выдавать «зелёное» вслепую
        results[-1]["ok"] = ok = False
        results[-1]["detail"] = "итог dotnet test не найден в выводе"
    slowest(trx)
    return ok


def slowest(trx: Path, top: int = 10) -> None:
    """Самые долгие классы прогона (xUnit гонит тесты класса подряд: прогон не короче самого долгого класса — его и делить)."""
    sys.path.insert(0, str(REPO / "tools"))
    import test_times   # noqa: PLC0415 — модуль рядом, нужен только здесь
    try:   # отчёт оборван (testhost убит) — итог прогона важнее таблицы времени
        rows = test_times.classes(sorted(trx.rglob("*.trx")))
    except (OSError, ValueError) as e:   # ET.ParseError — подкласс ValueError
        print(f"   время классов: trx не разобран — {e}")
        return
    if rows:
        print(f"   самые долгие классы (окно — от начала первого теста до конца последнего):\n{test_times.table(rows, top)}")


def affected(base: str | None) -> dict:
    cmd = [sys.executable, str(REPO / "tools" / "affected_tests.py"), "--json"] + (["--base", base] if base else [])
    proc = subprocess.run(cmd, cwd=REPO, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if proc.returncode != 0:
        raise SystemExit(f"tools/affected_tests.py упал:\n{proc.stderr.strip()}")
    return json.loads(proc.stdout)


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description="Сборка, тесты и проверки рабочей копии одним вызовом.")
    mode = ap.add_mutually_exclusive_group()
    for name, text in [("--affected", "затронутые классы + smoke (по умолчанию)"), ("--full", "CI-сборка и все тесты"),
                       ("--fast", "все, кроме Category=Synth"), ("--synth", "только Category=Synth"),
                       ("--smoke", "без корпуса, с пустым каталогом корпуса")]:
        mode.add_argument(name, action="store_true", help=text)
    ap.add_argument("--base", help="база сравнения для --affected и решения про ui-render-check (по умолчанию — main)")
    ui = ap.add_mutually_exclusive_group()
    ui.add_argument("--ui", action="store_true", help="ui-render-check всегда")
    ui.add_argument("--no-ui", action="store_true", help="ui-render-check никогда")
    args = ap.parse_args(argv)

    env = data_env()
    print(f"корпус: {env.get('D2_CORPUS_DIR') or REPO / 'corpus'}; графика: {env.get('D2_ASSETS_DIR') or REPO / 'assets'}")
    results: list[dict] = []
    t0 = time.monotonic()
    tests_only = args.fast or args.synth or args.smoke
    p = affected(args.base) if not tests_only or args.ui else {"ui": [], "pytest": [], "graph": ""}
    if p.get("graph"):
        print(f"(граф: {p['graph']})")

    build_env = dict(env, CI="true") if args.full else env
    if not run("сборка" + (" (CI)" if args.full else ""), ["dotnet", "build", SOLUTION, "-c", "Release"], build_env, results)[0]:
        return summary(results, t0)

    if args.full:
        tests("тесты: все", None, env, results)
    elif args.fast:
        tests("тесты: без синтетики", "Category!=Synth", env, results)
    elif args.synth:
        tests("тесты: синтетические карты", "Category=Synth", env, results)
    elif args.smoke:
        with tempfile.TemporaryDirectory(prefix="d2-empty-corpus-") as empty:
            ok = tests("тесты: smoke без корпуса", "Category!=Corpus&Category!=Synth", dict(env, D2_CORPUS_DIR=empty), results)
            if ok and results[-1]["skipped"]:
                results[-1]["ok"] = False
                results[-1]["detail"] += " — пропуск без корпуса: класс читает корпус, а категории Corpus/Synth у него нет"
    else:
        t = p["tests"]
        print(f"\nзатронуто: {t['why'] if isinstance(t['why'], str) else ', '.join(t['why'])}")
        tests("тесты: " + {"full": "все", "affected": "затронутые + smoke", "smoke": "smoke"}[t["mode"]], t["filter"], env, results)

    if tests_only and not args.ui:   # режимы одних тестов
        return summary(results, t0)
    want_ui = args.ui or (not args.no_ui and bool(p["ui"]))
    if want_ui and not all(r["ok"] for r in results):
        print("\nui-render-check: пропущен — тесты не зелёные; исправить и запустить снова")
    elif want_ui:
        # В цикле работы — шаги задетых областей вида; --full и --ui — все шаги.
        areas = [] if args.full or args.ui else [a for a in p.get("ui_areas", []) if a != "all"]
        print("\nui-render-check: " + ("--ui" if args.ui else "; ".join(p["ui"][:3]))
              + (f" — области {','.join(areas)}" if areas else " — все шаги"))
        if run("сборка Godot", ["dotnet", "build", "godot/LastEdict.Godot.csproj"], env, results)[0]:
            run("ui-render-check", [sys.executable, "tools/ui-render-check.py", *(["--areas", ",".join(areas)] if areas else [])],
                env, results, echo=True)
    else:
        print("\nui-render-check: не нужен" + (" (--no-ui)" if args.no_ui else " — вид не тронут"))
    if p["pytest"]:
        run("pytest", [sys.executable, "-m", "pytest", "-q", *p["pytest"]], env, results)
    return summary(results, t0)


def summary(results: list[dict], t0: float) -> int:
    print("\nИтог:")
    for r in results:
        print(f"  {'OK    ' if r['ok'] else 'ПРОВАЛ'} {r['step']:<34} {r['seconds']:6.0f} с  {r['detail']}")
    ok = all(r["ok"] for r in results)
    print(f"{'всё зелёное' if ok else 'есть провалы'} за {time.monotonic() - t0:.0f} с")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main(sys.argv[1:]))
