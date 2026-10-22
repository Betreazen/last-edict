#!/usr/bin/env python3
r"""affected_tests.py — какие проверки нужны после правки: по `git diff` против базы (по умолчанию — точка ветвления с main).

Что печатает:
  * тесты — фильтр `dotnet test --filter …`: затронутые классы тестов плюс быстрый набор smoke (тесты без корпуса,
    `Category!=Corpus&Category!=Synth`, ~15 с) — или «весь прогон», если правка общая (сборка, данные правил, больше
    половины классов);
  * нужен ли `tools/ui-render-check.py` — тронуты сцены Godot, данные интерфейса и графики или код вида, который сцены берут
    напрямую;
  * нужен ли pytest (`tests/test_*.py`) — тронуты питоновские инструменты, которые эти тесты импортируют.

Как ищутся затронутые классы. Изменённый файл кода (`src/**.cs`) — затравка: все узлы графа проекта
(`graphify-out/graph.json`, узлы с этим `source_file`), затем обратный обход по рёбрам вызовов, ссылок и наследования до
неподвижной точки. Файлы тестов, до которых дошёл обход, дают свои классы. Граф строится по коммиту (`built_at_commit`) и
может отставать — поэтому к нему добавляются прямые пользователи по тексту: файлы, где встречается имя типа, объявленного в
изменённом файле. Изменённый файл тестов даёт свои классы сам (и обход — для общих помощников вроде `CorpusFiles`).

Категории тестов (`[Trait("Category", …)]`): `Corpus` — читает корпус журналов и сохранений, `Synth` — партии синтетических
карт (`corpus/*-port-synth`, `mods/port-synth`); без категории — smoke. Проверка разметки — `python tools/check.py --smoke`:
smoke идёт с пустым корпусом и обязан пройти без пропусков.

    python tools/affected_tests.py                  # против точки ветвления с main
    python tools/affected_tests.py --base HEAD~3    # против своей базы
    python tools/affected_tests.py --json           # для tools/check.py
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from collections import defaultdict, deque
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]

SMOKE = "Category!=Corpus&Category!=Synth"
TEST_PROJECTS = ("tests/unit/Engine.Play.Tests/", "tests/unit/Engine.Rules.Tests/", "tests/unit/GodotRef.Tests/")
DATA_TESTS, RULES_TESTS, GODOT_REF_TESTS = TEST_PROJECTS

# Рёбра графа, по которым правка доходит до пользователя (обход — против направления ребра).
USE_RELATIONS = {"calls", "references", "inherits", "implements", "indirect_call", "uses", "dispatches_to", "imports_from"}

# Звеньев цепочки «по тексту» для файлов, которых граф не знает: дальше текст захватывает почти всё (общие имена типов).
TEXT_DEPTH = 3

# Больше этой доли классов — фильтр не короче полного прогона.
FULL_SHARE = 0.5

IGNORED_PREFIXES = ("docs/", "corpus/", "graphify-out/", ".vscode/", ".claude/", "bench/")
IGNORED_FILES = {"LICENSE", ".gitignore", ".graphifyignore", ".gitattributes"}
FULL_FILES = {"Directory.Build.props", "global.json", ".editorconfig", "LastEdict.sln"}

# Вид, который сцены Godot берут для показа: правка в нём — повод для ui-render-check.
UI_PREFIXES = ("godot/", "data/ui/", "data/gfx/", "data/map/", "data/sound/", "data/music/", "data/video/", "mods/",
               "src/Game.UiModel/", "src/Engine.Play/Map/", "src/Engine.Content/", "tools/ui-render-check.py", "tools/ui-assets/", "tools/gfx-assets/", "tools/sound-assets/")

# Области вида (шаги ui-render-check, ключ --areas): правка здесь задевает только эту область; прочее в UI_PREFIXES (автозагрузка, звук,
# графика, моды, содержимое, сама проверка) — все шаги. Карты своей области нет: godot/scenes/map держит общих помощников (GfxTextures,
# GfxBlending, GameSound — их берут бой и окна), src/Engine.Play/Map — настройки, которые читает и бой. Окна интерфейса есть в каждой сцене,
# так что «ui» на деле — тоже все шаги; выбор сужает только правка одного боя.
UI_AREAS = {"godot/scenes/battle/": "battle", "godot/scenes/ui/": "ui", "src/Game.UiModel/Ui/": "ui", "src/Game.UiModel/Menu/": "ui",
            "data/ui/": "ui", "data/video/": "ui"}


def ui_areas(files: list[str]) -> list[str]:
    """Области вида по файлам (правка вида или сцены, которые прямо берут правленый код): ["all"] — если хоть один вне областей."""
    areas = set()
    for f in files:
        area = next((a for prefix, a in UI_AREAS.items() if f.startswith(prefix)), "all")
        if area == "all":
            return ["all"]
        areas.add(area)
    return sorted(areas)


TEST_ATTR = re.compile(r"\[(?:Skippable)?(?:Fact|Theory)\b")
_MODS = r"(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|unsafe|ref|new|file)\s+)*"
_ATTRS = r"(?:\[[^\]\n]*\]\s*)*"
# Классы тестов (и записи) верхнего уровня: имя и всё до конца строки — оттуда берётся база. Вложенные помощники (отступ 8)
# не делят тело класса тестов.
CLASS_DECL = re.compile(r"^ {0,4}" + _ATTRS + _MODS + r"(?:class|record(?:\s+struct|\s+class)?)\s+(\w+)([^\n]*)", re.MULTILINE)
# Типы верхнего уровня (отступ не больше 4: пространство имён файла или блоком). Вложенные (разборщик .sg: Size, Group,
# Position…) дали бы ложные совпадения по всему коду.
TYPE_DECL = re.compile(r"^ {0,4}" + _ATTRS + _MODS + r"(?:class|record|struct|interface|enum|delegate\s+[\w<>\[\],.? ]+?)\s+"
                       r"(?:struct\s+|class\s+)?([A-Z]\w*)", re.MULTILINE)
NAMESPACE = re.compile(r"^\s*namespace\s+([\w.]+)\s*[;{]", re.MULTILINE)


def git(*args: str) -> str:
    return subprocess.run(["git", "-c", "core.quotepath=false", *args], cwd=REPO, capture_output=True, text=True,
                          encoding="utf-8", check=True).stdout


def changed_files(base: str | None) -> tuple[str, list[str]]:
    """Файлы, изменённые против базы: коммиты ветки, правки рабочей копии и новые неотслеживаемые файлы (переименование —
    оба пути: старый тоже был кодом, его пользователи задеты)."""
    if base is None:
        base = git("merge-base", "HEAD", "main").strip()
    files = set(git("diff", "--name-only", "--no-renames", "-z", base).split("\0"))
    files |= set(git("ls-files", "--others", "--exclude-standard", "-z").split("\0"))
    return base, sorted(f for f in files if f)


def read(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8-sig", errors="replace")
    except OSError:
        return ""


def cs_files(*prefixes: str) -> list[Path]:
    return [p for prefix in prefixes for p in (REPO / prefix).rglob("*.cs")
            if "/bin/" not in p.as_posix() and "/obj/" not in p.as_posix()]


def indent_of(text: str, pos: int) -> int:
    """Отступ строки, в которой стоит позиция `pos`."""
    line = text[text.rfind("\n", 0, pos) + 1:pos]
    return len(line) - len(line.lstrip(" "))


def base_of(tail: str) -> str | None:
    """Первый тип в списке баз объявления: `(ITestOutputHelper output) : Base(output);` → Base."""
    while (stripped := re.sub(r"\([^()]*\)", "", tail)) != tail:   # параметры основного конструктора, вложенные скобки
        tail = stripped
    m = re.match(r"\s*(?:<[^>]*>)?\s*:\s*([\w.]+)", tail)
    return m.group(1).split(".")[-1] if m else None


class TestIndex:
    """Классы тестов по файлам — полными именами (с пространством имён): одно имя бывает в обоих проектах тестов."""

    def __init__(self) -> None:
        self.by_file: dict[str, set[str]] = defaultdict(set)
        texts = {p.relative_to(REPO).as_posix(): read(p) for p in cs_files(*TEST_PROJECTS)}
        decls = []   # (файл, пространство имён, имя, база, абстрактный)
        has_tests: set[tuple[str, str]] = set()   # (пространство имён, имя)
        for rel, text in texts.items():
            ns = m.group(1) if (m := NAMESPACE.search(text)) else ""
            # Верхний уровень — отступ 0 (пространство имён файла) или 4 (блоком); глубже — вложенные помощники.
            top = 4 if re.search(r"^\s*namespace\s+[\w.]+\s*\{", text, re.MULTILINE) else 0
            matches = [d for d in CLASS_DECL.finditer(text) if indent_of(text, d.start(1)) == top]
            for i, d in enumerate(matches):
                decls.append((rel, ns, d.group(1), base_of(d.group(2)), " abstract " in f" {d.group(0)} "))
                # Свои [Fact]/[Theory] — между этим объявлением и следующим (помощники в файле тестов — не тесты).
                if TEST_ATTR.search(text, d.end(), matches[i + 1].start() if i + 1 < len(matches) else len(text)):
                    has_tests.add((ns, d.group(1)))
        by_name = defaultdict(list)
        for d in decls:
            by_name[d[2]].append(d)

        def parent(d) -> tuple | None:
            """Объявление базы: в том же файле, затем в том же пространстве имён, затем любое."""
            cands = by_name.get(d[3] or "", [])
            return next((c for c in cands if c[0] == d[0]), None) or next((c for c in cands if c[1] == d[1]), None) \
                or (cands[0] if cands else None)

        def is_test(d, depth: int = 0) -> bool:
            if (d[1], d[2]) in has_tests:
                return True
            return depth < 6 and (p := parent(d)) is not None and is_test(p, depth + 1)

        self.fqns: set[str] = set()
        for d in decls:
            if d[4] or not is_test(d):
                continue
            fqn = f"{d[1]}.{d[2]}" if d[1] else d[2]
            self.fqns.add(fqn)
            self.by_file[d[0]].add(fqn)
            # Части класса (partial) в других файлах и файлы баз: их правка задевает класс.
            for other in by_name[d[2]]:
                if other[1] == d[1]:
                    self.by_file[other[0]].add(fqn)
            p, depth = parent(d), 0
            while p is not None and depth < 6:
                self.by_file[p[0]].add(fqn)
                p, depth = parent(p), depth + 1

    @property
    def total(self) -> int:
        return len(self.fqns)


class Graph:
    """Граф проекта graphify: узлы по файлам и обратные рёбра использования."""

    def __init__(self, path: Path) -> None:
        self.ok = path.exists()
        self.commit = ""
        self.nodes_of: dict[str, list[str]] = defaultdict(list)
        self.file_of: dict[str, str] = {}
        self.users: dict[str, list[str]] = defaultdict(list)
        if not self.ok:
            return
        g = json.loads(path.read_text(encoding="utf-8"))
        self.commit = g.get("built_at_commit") or ""
        for n in g["nodes"]:
            if (f := n.get("source_file")) and n.get("file_type") == "code":
                self.nodes_of[f].append(n["id"])
                self.file_of[n["id"]] = f
        for e in g.get("links") or g.get("edges") or []:
            if e.get("relation") in USE_RELATIONS:
                self.users[e["target"]].append(e["source"])

    def direct_users(self, files: list[str]) -> set[str]:
        """Файлы, которые прямо используют узлы из `files` (одно ребро)."""
        return {self.file_of[u] for f in files for n in self.nodes_of.get(f, []) for u in self.users.get(n, []) if u in self.file_of}

    def reach(self, files: list[str]) -> set[str]:
        """Файлы, которые используют что-то из `files` — прямо или через цепочку."""
        seen = {n for f in files for n in self.nodes_of.get(f, [])}
        queue = deque(seen)
        while queue:
            for user in self.users.get(queue.popleft(), []):
                if user not in seen:
                    seen.add(user)
                    queue.append(user)
        return {self.file_of[n] for n in seen if n in self.file_of}


class TextIndex:
    """Пользователи типов по тексту: файл, где встречается имя типа верхнего уровня из другого файла."""

    def __init__(self, files: list[Path]) -> None:
        self.texts = {p.relative_to(REPO).as_posix(): read(p) for p in files}

    def declared(self, rels: set[str] | list[str]) -> set[str]:
        """Типы верхнего уровня файлов (отступ 0, у пространства имён блоком — 4)."""
        names = set()
        for f in rels:
            text = self.texts.get(f) or read(REPO / f)
            top = 4 if re.search(r"^\s*namespace\s+[\w.]+\s*\{", text, re.MULTILINE) else 0
            names |= {m.group(1) for m in TYPE_DECL.finditer(text) if indent_of(text, m.start(1)) == top}
        return names

    def users(self, rels: list[str], depth: int) -> set[str]:
        """Пользователи типов из `rels`: `depth` — сколько звеньев цепочки (1 — прямые, A ← B ← тест — 2)."""
        names, seen, front = set(), set(), set(rels)
        for _ in range(depth):
            new_names = self.declared(front) - names
            if not new_names:
                break
            names |= new_names
            pattern = re.compile(r"\b(?:" + "|".join(sorted(map(re.escape, new_names))) + r")\b")
            hits = {f for f, text in self.texts.items() if f not in seen and pattern.search(text)}
            seen |= hits
            front = hits
        return seen


def pytest_targets(changed: list[str]) -> list[str]:
    """Питоновские тесты `tests/test_*.py`, которые импортируют изменённый модуль `tools/*.py` (или изменены сами)."""
    modules = {Path(f).stem for f in changed if f.startswith("tools/") and f.endswith(".py")}
    targets = []
    for test in sorted((REPO / "tests").glob("test_*.py")):
        rel = test.relative_to(REPO).as_posix()
        text = read(test)
        if rel in changed or any(re.search(rf"^\s*(?:import|from)\s+{re.escape(m)}\b", text, re.MULTILINE) for m in modules):
            targets.append(rel)
    return targets


IMPORTERS = ("tools/ksy/", "tools/GameImport/", "tools/UiImport/", "tools/UiFlowImport/", "tools/UiStringsImport/",
             "tools/RulesImport/", "tools/ScenarioImport/")


def classify(changed: list[str]) -> dict:
    """Решение по списку файлов: что задето и как (полный прогон, проекты целиком, код, тесты, данные, вид)."""
    full_reasons, ui_reasons, code, tests, data = [], [], [], [], []
    whole_projects: set[str] = set()
    for f in changed:
        if f in IGNORED_FILES or f.startswith(IGNORED_PREFIXES) or f.endswith(".md"):
            continue
        if f.startswith(UI_PREFIXES):
            ui_reasons.append(f)
        if f in FULL_FILES or f.endswith((".csproj", ".props", ".targets")) or f.startswith("data/rules/"):
            full_reasons.append(f)
        elif f.startswith(TEST_PROJECTS):
            if f.endswith(".cs"):
                tests.append(f)
            else:   # эталоны и данные тестов (golden/…) — проект целиком
                whole_projects.add(next(p for p in TEST_PROJECTS if f.startswith(p)))
        elif f.startswith("src/"):
            if f.endswith(".cs"):
                code.append(f)
            else:
                full_reasons.append(f)   # ресурс сборки — неизвестно, кто его берёт
        elif f.startswith(("data/", "mods/")):
            whole_projects.add(DATA_TESTS)
            data.append(f)
        elif f.startswith(IMPORTERS):
            whole_projects.add(DATA_TESTS)
        elif f.startswith("tools/godot-ref/"):
            whole_projects.add(GODOT_REF_TESTS)
        elif f.startswith(("godot/", "tools/")) or (f.startswith("tests/") and f.endswith(".py")):
            pass   # сцены и инструменты — без dotnet-тестов (ui-render-check и pytest решаются отдельно)
        else:
            full_reasons.append(f)   # неизвестное — на всякий случай весь прогон
    return {"full": full_reasons, "ui": ui_reasons, "code": code, "tests": tests, "data": data, "projects": sorted(whole_projects)}


def plan(base: str | None, files: list[str] | None = None) -> dict:
    if files:
        base, changed = "(список)", sorted({Path(f.replace("\\", "/")).as_posix().removeprefix("./") for f in files})
    else:
        base, changed = changed_files(base)
    c = classify(changed)
    view_files = list(c["ui"])   # файлы вида и сцены — пользователи правленого кода: по ним области ui-render-check
    index = TestIndex()
    classes: set[str] = set()
    graph_note = ""
    if c["code"] or c["tests"]:
        graph = Graph(REPO / "graphify-out" / "graph.json")
        seeds = c["code"] + c["tests"]
        reached: set[str] = set(c["tests"])
        if graph.ok:
            reached |= graph.reach(seeds)
            head = git("rev-parse", "HEAD").strip()
            if graph.commit and not head.startswith(graph.commit[:7]):
                graph_note = (f"граф снят на {graph.commit[:8]}, HEAD {head[:8]} — новые файлы — по тексту, цепочкой до 3 звеньев; "
                              "после серии правок `PYTHONHASHSEED=0 graphify update .`")
        else:
            graph_note = "нет graphify-out/graph.json — пользователи только по тексту, по цепочке"
        text = TextIndex(cs_files("src", "tests", "godot"))
        # Прямые пользователи по тексту — всегда (граф мог отстать); файл, которого в графе нет (новый), — по цепочке (A ← B ← тест).
        unknown = [f for f in seeds if not graph.nodes_of.get(f)]
        reached |= text.users(seeds, depth=1 if graph.ok else TEXT_DEPTH)
        if unknown and graph.ok:
            reached |= text.users(unknown, depth=TEXT_DEPTH)
        for f in reached:
            classes |= index.by_file.get(f, set())
        # Прямые пользователи в сценах Godot — код, который сцены берут: повод для ui-render-check.
        godot = TextIndex(cs_files("godot"))
        for f in c["code"]:
            users = {u for u in godot.users([f], depth=1) if u.startswith("godot/")}
            if graph.ok:
                users |= {u for u in graph.direct_users([f]) if u.startswith("godot/")}
            if users and f not in c["ui"]:
                c["ui"].append(f"{f} — его прямо берут {len(users)} файлов godot/ (например, {sorted(users)[0]})")
                view_files += sorted(users)
    for project in c["projects"]:
        classes |= {n for f, names in index.by_file.items() if f.startswith(project) for n in names}
    # Данные (data/<каталог>/…): классы D2.Rules.Tests, которые читают тот же каталог данных репозитория.
    for sub in {f.split("/")[1] for f in c["data"] if f.count("/") >= 2}:
        for p in cs_files(RULES_TESTS):
            t = read(p)
            if '"data"' in t and f'"{sub}"' in t:
                classes |= index.by_file.get(p.relative_to(REPO).as_posix(), set())

    if c["full"] or (index.total and len(classes) > FULL_SHARE * index.total):
        tests = {"mode": "full", "filter": None, "why": c["full"][:5] or [f"затронуто {len(classes)} классов из {index.total}"]}
    elif classes:
        terms = [f"FullyQualifiedName~{n}." for n in sorted(classes)]
        tests = {"mode": "affected", "filter": "(" + SMOKE + ")|" + "|".join(terms), "classes": sorted(classes),
                 "why": f"затронуто {len(classes)} классов из {index.total}"}
    else:
        tests = {"mode": "smoke", "filter": SMOKE, "why": "код тестов не задет — только быстрый набор"}
    return {"base": base, "changed": changed, "tests": tests, "ui": c["ui"], "ui_areas": ui_areas(view_files) if view_files else [],
            "pytest": pytest_targets(changed), "graph": graph_note}


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description="Какие тесты и проверки нужны после правки (по git diff).")
    ap.add_argument("--base", help="база сравнения (по умолчанию — точка ветвления с main)")
    ap.add_argument("--json", action="store_true", help="машинный вывод для tools/check.py")
    ap.add_argument("--files", nargs="+", help="свой список изменённых файлов вместо git diff (пути от корня)")
    args = ap.parse_args(argv)
    p = plan(args.base, args.files)
    if args.json:
        print(json.dumps(p, ensure_ascii=False, indent=1))
        return 0
    t = p["tests"]
    print(f"База {p['base'][:10]}, изменено файлов: {len(p['changed'])}")
    if p["graph"]:
        print(f"  ({p['graph']})")
    if t["mode"] == "full":
        print(f"Тесты: весь прогон — {', '.join(t['why'])}")
        print('  dotnet test Disciples.sln -c Release')
    else:
        print(f"Тесты: {t['why']}")
        for name in t.get("classes", [])[:40]:
            print(f"  {name}")
        if len(t.get("classes", [])) > 40:
            print(f"  … и ещё {len(t['classes']) - 40}")
        print(f'  dotnet test Disciples.sln -c Release --filter "{t["filter"]}"')
    if p["ui"]:
        print(f"ui-render-check: нужен — {'; '.join(p['ui'][:4])}{' …' if len(p['ui']) > 4 else ''}")
        print(f"  python tools/ui-render-check.py --areas {','.join(p['ui_areas'])}   (области: {', '.join(p['ui_areas'])}; "
              "полностью — ~3 мин)")
    else:
        print("ui-render-check: не нужен (сцены, данные вида и код вида не тронуты)")
    print(f"pytest: {' '.join(p['pytest'])}" if p["pytest"] else "pytest: не нужен")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main(sys.argv[1:]))
