#!/usr/bin/env python3
"""ui-render-check.py — сборка экранов порта в PNG без окна и без глаз.

Гоняет сцену godot/scenes/ui/UiScreen.tscn в headless по списку экранов. Моды при этом выключены
(--no-mods): проверка отвечает за базовый набор данных, а не за то, что лежит в mods/. Сцена сама печатает одну
строку на экран (нарисовано / пропущено / доля маджентовых точек / доля непрозрачных / текст) и возвращает 1,
если кадра не нашлось или маджента осталась. Скрипт добавляет к этому три проверки:

* картинка не пустая — экран из одних прозрачных точек собрался бы «успешно», а показывать было бы нечего;
* каждому TextId нашлась строка локализации — кроме заведомо отсутствующих (KNOWN_MISSING ниже);
* разметка оформления разобрана вся — непонятая директива попадает в `unknown=` и валит проверку.

Отдельным шагом проверяется шрифт: режим сцены `--ink` растеризует русский образец в кэш глифов и печатает
число закрашенных точек до и после. Снимка экрана в headless не бывает, поэтому кириллица проверяется так.

Последний шаг — живые элементы: сцена прогоняет сценарий ввода (`--input`) настоящим путём, через вьюпорт
и масштаб окна, и печатает на каждый шаг события и сменившиеся кадры спрайтов. Ожидания — в INPUT_CASES:
щелчок в точку должен дать ровно такие события и такой кадр. И переходы по графу экранов (сцена UiFlow.tscn,
FLOW_CASES): после каждого щелчка стек экранов должен стать ровно таким.

Godot берётся из переменной GODOT (по умолчанию C:\\Tools\\bin\\godot.cmd), PNG кладутся в .cache/ui-check
(этот каталог в .gitignore).

Запуск:
  python tools/ui-render-check.py                 # семь экранов по умолчанию + шрифт
  python tools/ui-render-check.py DLG_CITY ...    # свой список
"""
from __future__ import annotations

import argparse
import inspect
import os
import json
import math
import queue
import re
import shutil
import subprocess
import sys
import tempfile
import threading
import time
from collections.abc import Callable, Iterator
from concurrent.futures import ThreadPoolExecutor, as_completed
from contextlib import contextmanager
from functools import partial
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ui_check_maps  # noqa: E402  (рядом, в tools/)

# Семь экранов: маленький диалог, боевой экран (58 элементов), самый большой по элементам (77), городской,
# книга заклинаний и два с настоящими подписями — главное меню (8 надписей) и выбор одиночной игры (6).
# У всех семи вся графика есть в описи — проверка обязана быть зелёной.
SCREENS = ["DLG_ACTION_RESULT", "DLG_BATTLE_A", "DLG_OPTIONS", "DLG_CITY_STACK", "DLG_CAST_SPELL",
           "DLG_MAIN_MENU", "DLG_SINGLE_PLAYER"]

# Ниже этой доли непрозрачных точек считаем, что экран не собрался: даже у самого дырявого из них
# заполнено больше четверти.
MIN_OPAQUE = 0.05

# Идентификаторы, которых нет ни в одной таблице строк игры (проверено пересчётом TApp.dbf, Tglobal.dbf
# и TAppEdit.dbf — 8 из 167 на весь набор экранов). Это не пропажа перевода, а мусор в самой разметке:
#   * DLG_BATTLE_A_* — в поле подсказки записано имя кадра, а не строка;
#   * 255 — число вместо идентификатора;
#   * Login, Register — английские литералы в CustomLobby.dlg, которые игра не переводила.
# Всё остальное отсутствующее — дефект импорта строк, и проверка обязана падать.
KNOWN_MISSING = {
    "255",
    "DLG_BATTLE_A_LITEMSBG", "DLG_BATTLE_A_LUNITGROUP", "DLG_BATTLE_A_MAINCOMBATBG",
    "DLG_BATTLE_A_RITEMSBG", "DLG_BATTLE_A_RUNITGROUP",
    "Login", "Register",
}

# Образец для проверки покрытия кириллицы: игра локализована на русский, и без этих букв порт молча
# нарисовал бы пустые прямоугольники.
INK_SAMPLE = "Русский текст ЁЙЩЪ"

# Живые элементы: экран, --items, шаги и ожидание на каждый шаг — события (через запятую, «-» — ни одного)
# и, если задано, «элемент:кадр», который должен оказаться среди сменившихся. Само поведение — модель
# UiScreenState, её проверяет dotnet test; здесь — что сцена доносит до неё ввод и меняет кадры спрайтов.
INPUT_CASES = [
    ("DLG_MAIN_MENU", None, [
        ("move BTN_SINGLE", "-", "BTN_SINGLE:DLG_MAIN_MENU_MAIN01BH"),
        ("press BTN_SINGLE", "-", "BTN_SINGLE:DLG_MAIN_MENU_MAIN01BC"),
        ("move 10,10", "-", "BTN_SINGLE:DLG_MAIN_MENU_MAIN01BN"),
        ("release 10,10", "-", None),
        ("click BTN_QUIT", "Clicked:BTN_QUIT", "BTN_QUIT:DLG_MAIN_MENU_MAIN05BH"),
    ]),
    ("DLG_OPTIONS", "SPIN_ACKNO=3", [
        ("click TOG_AUTOSAVE", "Toggled:TOG_AUTOSAVE=1", "TOG_AUTOSAVE:_MENU_CRYSTAL_N"),
        ("click BTN_ACKNORIGHT", "Clicked:BTN_ACKNORIGHT,Spin:SPIN_ACKNO=1", None),
        ("click BTN_ACKNORIGHT", "Clicked:BTN_ACKNORIGHT,Spin:SPIN_ACKNO=2", None),
        # Последнее значение — стрелка гаснет (0x539e6a) и не жмётся.
        ("click BTN_ACKNORIGHT", "-", None),
        ("key 27", "Clicked:BTN_CLOSE", None),
    ]),
    ("DLG_CHOOSE_LORD", None, [
        ("click TOG_LORD_MAGE", "Group:RAD_LORD=1:TOG_LORD_MAGE", "TOG_LORD_WARRIOR:DLG_CHOOSE_LORD_WARRIORD"),
        ("click TOG_LORD_MAGE", "-", None),
    ]),
    # Подсказка только рисуется: щелчок в точку под ней (рамка подсказки — ниже и левее мыши) доходит до кнопки. Четвёртое поле —
    # показанная подсказка (иначе проверка ничего не проверяла бы).
    ("DLG_ACTION_RESULT", None, [
        ("move BTN_YES", "-", None),
        ("wait 300", "-", None, "Закрыть"),
        ("click 444,510", "Clicked:BTN_YES", None),
    ]),
    # Над картой подсказки нет (прогон 11.10, замечание 6): у IMG_MAIN вида карты в поле подсказки «255» — не id строки, движок такую
    # подсказку не ставит (0x5c868e); прежде порт показывал «255» над всей картой.
    ("DLG_ISO_VIEW", None, [
        ("move 300,300", "-", None),
        ("wait 300", "-", None, "-"),
    ]),
    ("DLG_LOAD_GAME", "TLBOX_GAME_LIST=30", [
        ("click 60,130", "Selected:TLBOX_GAME_LIST=0", None),
        ("key 40", "Clicked:BTN_LIST_DOWN,Selected:TLBOX_GAME_LIST=1", None),
        ("key 34", "Clicked:BTN_PG_DN,Selected:TLBOX_GAME_LIST=18", None),
        # 17 строк на странице (место 300 точек, строка — шаг шрифта Normal 17): выбор 18 прокрутил список до пункта 2 — он теперь в
        # первой строке.
        ("double 60,130", "Selected:TLBOX_GAME_LIST=2,Confirmed:TLBOX_GAME_LIST=2", None),
        # Кнопка подтверждения списка — одно событие (партия 16.10: два события — два действия).
        ("key 13", "Clicked:BTN_LOAD", None),
    ]),
]

# Переходы по графу экранов (сцена UiFlow.tscn): шаги и ожидаемый стек после каждого («>» — снизу вверх,
# «-» — стек пуст, игра окончена). Проверяет, что щелчок доходит до верхнего экрана, событие — до навигатора,
# а узлы экранов — до стека. Сами правила стека проверяет dotnet test (UiNavigatorTests).
# Третье поле шага, если есть, — сколько узлов экранов построено к этому шагу (Goto на тот же экран обязан
# строить его заново, со свежим состоянием).
FLOW_CASES = [
    # Выбор исхода — логика экрана (есть ли ещё предметы); без неё «ОК» закрывает окно (в журналах чаще), поэтому
    # переход на себя оставлен модом-приспособлением tools/ui-check-mods/self-goto — заодно проверяется слой модов.
    ("ОК в окне предмета", "DLG_ITEM", [
        ("key 13", "DLG_ITEM", 2),
    ]),
    ("до брифинга", None, [
        ("click BTN_SINGLE", "DLG_SINGLE_PLAYER"),
        ("click BTN_NEW_SKIRMISH", "DLG_CHOOSE_SKIRMISH"),
        ("key 13", "DLG_CHOOSE_RACE"),
        ("key 13", "DLG_CHOOSE_LORD"),
        ("key 13", "DLG_SCENARIO_BRIEFING"),
    ]),
    # «Мультиплеер» (Э7): лобби сетевой игры окном поверх меню (docs/network.md §7); Esc — назад в меню.
    ("мультиплеер — лобби", None, [
        ("click BTN_MULTI", "DLG_MAIN_MENU>LE_LOBBY"),
        ("key 27", "DLG_MAIN_MENU"),
        ("click BTN_SINGLE", "DLG_SINGLE_PLAYER"),
    ]),
    # Титры — пустая страница (решение пользователя 28.09), любой щелчок или клавиша — в главное меню (переход без элемента).
    ("титры", None, [
        ("click BTN_CREDITS", "DLG_CREDITS"),
        ("key 13", "DLG_MAIN_MENU"),
        ("click BTN_CREDITS", "DLG_CREDITS"),
        ("click 400,300", "DLG_MAIN_MENU"),
    ]),
    ("назад и окно поверх", None, [
        ("click BTN_SINGLE", "DLG_SINGLE_PLAYER"),
        ("click BTN_BACK", "DLG_MAIN_MENU"),
        ("click BTN_OPTIONS", "DLG_MAIN_MENU>LE_SETTINGS"),   # окно настроек Last Edict (Э5) на месте DLG_OPTIONS
        ("click 637,34", "DLG_MAIN_MENU>LE_SETTINGS"),  # кнопка главного меню под окном — ввод не доходит
        ("key 27", "DLG_MAIN_MENU"),
        ("click BTN_QUIT", "-"),
    ]),
    # Окно загрузки из меню (DLG_LOAD, «Одиночная игра» → «Загрузить»): Esc и «Назад» — в главное меню. Окно загрузки в партии
    # (DLG_LOAD_GAME) — в autosave(): Esc и «Назад» возвращают к настройкам (пометка 4 прогона 14.10).
    ("загрузка из меню: Esc и Назад", None, [
        ("click BTN_SINGLE", "DLG_SINGLE_PLAYER"),
        ("click BTN_LOAD_SKIRMISH", "DLG_LOAD"),
        ("key 27", "DLG_MAIN_MENU"),
        ("click BTN_SINGLE", "DLG_SINGLE_PLAYER"),
        ("click BTN_LOAD_SKIRMISH", "DLG_LOAD"),
        ("click BTN_BACK", "DLG_MAIN_MENU"),
    ]),
]

FLOW = re.compile(r"flow step=\d+ do=\S+ at=\S+ events=\S+ go=\S+ stack=(?P<stack>\S+) phase=(?P<phase>\S+) "
                  r"quit=\d built=(?P<built>\d+) video=(?P<video>\S+)(?: vrect=(?P<vrect>\S+))?")

# Логика экранов «новой игры» (сцена UiFlow.tscn с --probe): шаги и ожидания по содержимому элементов верхнего
# экрана после шага — (номер шага с 1, элемент, поле probe, значение). Сама логика проверена dotnet test
# (MenuLogicTests); здесь — что сцена её вызывает, передаёт ей ввод и показывает результат, включая ролики смены.
CONTENT_STEPS = ["click BTN_SINGLE", "click BTN_NEW_SKIRMISH", "key 40", "key 13", "key 39", "key 13",
                 "click BTN_FACE", "click BTN_FACE", "click EDIT_LORD_NAME", "key 8", "type Z", "key 13"]
CONTENT_EXPECT = [
    (1, None, "video", "TRANS_MAIN2SINGLE"),
    (2, "TLBOX_GAME_SLOT", "items", "33@0"),
    (2, "IMG_RACE_1", "frame", "GODHU"),   # значок 45×73 — ровно клетка (0x4f1095)
    (3, "TLBOX_GAME_SLOT", "items", "33@1"),
    (3, "TXT_MAP_SIZE", "text", r"Размер_карты:\n_96_х_96"),
    (4, None, "video", "TRANS_NEWQUEST2GOD"),
    (4, "UNDERLAY", "frame", "DLG_CHOOSE_RACE_GODHU_BG"),   # картинка расы — под рамкой (седьмой круг)
    (4, "BACKGROUND", "frame", "DLG_CHOOSE_RACE_BG_CHOOSE_RACE"),
    (5, "UNDERLAY", "frame", "DLG_CHOOSE_RACE_GODUN_BG"),
    (5, "BACKGROUND", "video", "GOD_HU2UN#1"),
    (6, None, "video", "TRANS_GOD2LORD_UN"),
    (6, "UNDERLAY", "frame", "DLG_CHOOSE_LORD_UNDEAD_BG"),
    (6, "BACKGROUND", "frame", "DLG_CHOOSE_LORD_BG1TRANS"),
    (6, "TXT_TITLE", "text", "Выберите_лорда"),
    (6, "TXT_DIFF", "text", "Средне"),
    (6, "BTN_FACE", "frame", "FACE_UN_0"),        # в покое лицо неподвижное (снимок 28.09)
    (6, "BTN_FACE", "video", "-"),
    (7, "BTN_FACE", "frame", "FACE_UN_1"),        # смена: неподвижное новое лицо под разовым роликом старого
    (7, "BTN_FACE", "video", "FACE_UN_0#1"),
    (8, "BTN_FACE", "frame", "FACE_UN_1"),        # нажатие во время ролика не принято (0x4e6945, [+0x44])
    (8, "BTN_FACE", "video", "FACE_UN_0#1"),
    (12, "IMG_BRIEFING", "video", "BRIEF2U#1"),
]
PROBES = sorted({e[1] for e in CONTENT_EXPECT if e[1]})
PROBE = re.compile(r"probe screen=\S+ id=(?P<id>\S+) text=(?P<text>\S+) frame=(?P<frame>\S+) "
                   r"video=(?P<video>\S+) items=(?P<items>\S+)")

INPUT = re.compile(r"input step=\d+ do=\S+ at=\S+ hover=\S+ events=(?P<events>\S+) changed=(?P<changed>\S+)(?: tip=(?P<tip>\S+))?")

LINE = re.compile(
    r"screen=(?P<screen>\S+) drawn=(?P<drawn>\d+) skipped=(?P<skipped>\d+) "
    r"magenta=(?P<magenta>[\d.]+) opaque=(?P<opaque>[\d.]+) lang=(?P<lang>\S+) "
    r"textids=(?P<textids>\d+) strings=(?P<strings>\d+) nostring=(?P<nostring>\d+) "
    r"captions=(?P<captions>\d+) missing=(?P<missing>\S+) unknown=(?P<unknown>\S+)"
)
INK = re.compile(r"ink font=(?P<font>\S+) size=(?P<size>\d+) chars=(?P<chars>\d+) "
                 r"nochar=(?P<nochar>\d+) ink0=(?P<ink0>\d+) ink1=(?P<ink1>\d+)")


# Необработанное исключение C# Godot не роняет — движок пишет ошибку и крутит цикл дальше (docs/godot-notes.md),
# так что сцена без Quit висела бы вечно. Прогон одной сцены укладывается в секунды.
RUN_TIMEOUT = 120


def no_synth(repo: Path, name: str) -> tuple[bool, str] | None:
    """Синтетические карты порта (слой mods/port-synth) в публичное дерево не входят (docs/publishing.md): проверки на них там пропускаются
    с причиной, а не падают. В рабочем репозитории слой есть — None, проверка идёт."""
    if (repo / "mods" / "port-synth" / "mod.json").is_file():
        return None
    return True, f"{name}: пропуск — нет синтетических карт (mods/port-synth вне публичного дерева)"


# Шаг, который идёт в этом потоке: `calls` — время его запусков Godot (пишет godot(), читает timed()), `extra` — переменные окружения
# шага (scene_env). os.environ шаги не трогают: шаги идут параллельно, а его видят все потоки.
_STEP = threading.local()

# Слоты — сколько Godot идёт разом (-j). У слота свой APPDATA: у Godot на Windows user:// лежит в %APPDATA%\Godot\app_userdata, так у
# процессов, идущих разом, свои настройки, сохранения и журналы зонда. Нет слотов (0) — запуск без очереди и в общем APPDATA.
_SLOTS: queue.Queue[int] = queue.Queue()
_SLOT_HOMES: list[Path] = []

# Запуски Godot внутри шага разом (godot_all): потоки только ждут процессы, число процессов держат слоты.
_INNER = ThreadPoolExecutor(max_workers=64)


def set_slots(repo: Path, count: int) -> None:
    """Слоты запусков Godot, у каждого чистый APPDATA на прогон проверки."""
    for i in range(count):
        home = repo / ".cache" / "ui-check" / "workers" / f"w{i}"
        shutil.rmtree(home, ignore_errors=True)
        (home / "appdata").mkdir(parents=True)
        _SLOT_HOMES.append(home / "appdata")
        _SLOTS.put(i)


@contextmanager
def scene_env(**extra: str) -> Iterator[None]:
    """Переменные окружения для запусков Godot внутри блока — только у этого шага."""
    saved = getattr(_STEP, "extra", {})
    _STEP.extra = {**saved, **extra}
    try:
        yield
    finally:
        _STEP.extra = saved


def child_env(slot: int | None = None) -> dict[str, str]:
    """Окружение дочернего процесса: общее, слота и шага. D2_VERBOSE=1 — болтливый вывод сцен (aimove, walked, fx…, godot/autoload/GameLog.cs)
    печатается и в сценах без --check и --input, как прежде: шаги разбирают эти строки."""
    return {**os.environ, "D2_VERBOSE": "1", "D2_COMMAND_AUDIT": "1", **({"APPDATA": str(_SLOT_HOMES[slot])} if slot is not None else {}),
            **getattr(_STEP, "extra", {})}


# Аудит команд (docs/game-commands.md §3): места, где сцена меняла партию мимо GameSession.Execute, — строки «command-audit» всех запусков.
_AUDIT: dict[str, str] = {}
_AUDIT_LOCK = threading.Lock()


def note_audit(out: str) -> None:
    with _AUDIT_LOCK:
        for line in out.splitlines():
            if line.startswith("command-audit "):
                _AUDIT.setdefault(line[len("command-audit "):].strip(), getattr(_STEP, "name", "?"))


def godot_all(repo: Path, runs: list[tuple]) -> list[tuple[int, str]]:
    """Независимые запуски одного шага разом: (аргументы, сцена[, окружение]) → (код, вывод) в том же порядке. Время и окружение
    шага переходят в потоки запусков."""
    calls, extra, name = getattr(_STEP, "calls", None), dict(getattr(_STEP, "extra", {})), getattr(_STEP, "name", "?")

    def one(run: tuple) -> tuple[int, str]:
        _STEP.extra, _STEP.name = {**extra, **(run[2] if len(run) > 2 else {})}, name
        if calls is not None:
            _STEP.calls = calls
        return godot(repo, run[0], run[1])
    return list(_INNER.map(one, runs))


def kill_tree(proc: subprocess.Popen) -> None:
    """Снять процесс с потомками: godot.cmd и консольный Godot запускают настоящий Godot дочерним процессом, и kill() одного родителя
    оставил бы его висеть с открытыми трубами вывода."""
    if os.name == "nt":
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)], capture_output=True, check=False)
    else:
        proc.kill()


_TOOLS_LOCK = threading.Lock()
_TOOLS_BUILT: set[str] = set()


def dotnet_tool(repo: Path, name: str) -> list[str]:
    """Команда готовой сборки инструмента tools/<name>: один `dotnet build` (Release) за прогон проверки — `dotnet run --project`
    собирал бы его на каждом шаге."""
    with _TOOLS_LOCK:
        if name not in _TOOLS_BUILT:
            built = subprocess.run(["dotnet", "build", str(repo / "tools" / name), "-c", "Release", "-nologo", "-v", "q"],
                                   capture_output=True, text=True, encoding="utf-8", errors="replace")
            if built.returncode != 0:
                raise RuntimeError(f"сборка tools/{name} упала:\n{built.stdout[-3000:]}{built.stderr[-2000:]}")
            _TOOLS_BUILT.add(name)
    return ["dotnet", str(repo / "tools" / name / "bin" / "Release" / "net10.0" / f"{name}.dll")]


# Партии для шагов карты (Э5): сохранений живых партий корпуса в Last Edict нет — их заменяют снимки партии стенда (tools/CheckSaves:
# новая партия меню за игрока, затем ИИ за всех с постоянным зерном, игрок — человек в снимке; день 1 — начало партии).
# (сценарий импорта, игрок) → дни. Снимки пишутся раз за прогон.
STAND_SCENARIO, STAND_PLAYER, LEGIONS = "Power to Corrupt", "S073PL000a", "S073PL000b"
CHECK_SAVES = {(STAND_SCENARIO, STAND_PLAYER): (5, 21, 23, 30, 35), (STAND_SCENARIO, LEGIONS): (1,)}
STAND_GOLD = 5000   # казна человека в снимке: ИИ за него тратит, а окнам нужны найм и дань
_SAVES_LOCK = threading.Lock()
_SAVES_DONE: dict[tuple[str, str], str] = {}   # (сценарий, игрок) → «» (готово) или причина пропуска


def check_save(repo: Path, scenario: str, day: int, player: str = STAND_PLAYER) -> tuple[Path | None, str]:
    """Сохранение дня `day` партии стенда на сценарии импорта: (путь, «») или (None, причина пропуска — нет импорта)."""
    key = (scenario, player)
    if day not in CHECK_SAVES[key]:
        raise ValueError(f"дня {day} нет в CHECK_SAVES[{key}] — добавить его в набор снимков")
    out = repo / ".cache" / "ui-check" / "check-saves" / player
    with _SAVES_LOCK:
        if key not in _SAVES_DONE:
            if not ui_check_maps.scenario_path(repo, scenario).is_file():
                _SAVES_DONE[key] = f"нет сценария импорта «{scenario}»"
            else:
                run = subprocess.run([*dotnet_tool(repo, "CheckSaves"), "--scenario", scenario, "--player", player,
                                      "--days", ",".join(map(str, CHECK_SAVES[key])), "--gold", str(STAND_GOLD), "--out", str(out)],
                                     capture_output=True, text=True, encoding="utf-8", errors="replace", cwd=repo)
                if run.returncode != 0:
                    raise RuntimeError(f"tools/CheckSaves «{scenario}» за {player} упал:\n{run.stdout[-2000:]}{run.stderr[-2000:]}")
                _SAVES_DONE[key] = ""
    if _SAVES_DONE[key]:
        return None, _SAVES_DONE[key]
    return out / f"{scenario}-d{day:02}.d2save", ""


def ruin_raid(m: ui_check_maps.ScenarioMap) -> tuple[ui_check_maps.Stack, ui_check_maps.Site]:
    """Отряд игрока стенда с сумкой (герой) и ближняя к нему руина с охраной — бой за руину приходом (`arrive`)."""
    hero = max(m.field_stacks(STAND_PLAYER), key=lambda st: (st.items, st.id))
    return hero, m.nearest_site("Ruin", hero.x, hero.y, guarded=True)


def stand(repo: Path, day: int, player: str = STAND_PLAYER) -> tuple[Path | None, ui_check_maps.ScenarioMap | None, str]:
    """Сохранение дня `day` партии стенда «Power to Corrupt» за `player` и его карта; (None, None, причина) — нет импорта."""
    save, why = check_save(repo, STAND_SCENARIO, day, player)
    return (save, ui_check_maps.load(save), "") if save is not None else (None, None, why)


def godot(repo: Path, extra: list[str], scene: str = "res://scenes/ui/UiScreen.tscn") -> tuple[int, str]:
    slot = _SLOTS.get() if _SLOT_HOMES else None
    t0 = time.monotonic()
    try:
        return _godot(repo, extra, scene, slot)
    finally:
        if slot is not None:
            _SLOTS.put(slot)
        if getattr(_STEP, "calls", None) is not None:
            _STEP.calls.append(time.monotonic() - t0)


# Бой-образец сцены боя, когда шаг не задал свой (--stacks, --journal, --synth): журнала корпуса (corpus/sample) в Last Edict нет —
# бой с карты импортированного сценария.
BATTLE_SCENE = "res://scenes/battle/BattleScreen.tscn"
BATTLE_SAMPLE = ["--scenario", "Mercantile", "--stacks", "S135KC0000,S135KC0020"]


def _godot(repo: Path, extra: list[str], scene: str, slot: int | None) -> tuple[int, str]:
    exe = os.environ.get("GODOT", r"C:\Tools\bin\godot.cmd")
    if scene == BATTLE_SCENE and not {"--stacks", "--journal", "--synth"} & set(extra):
        extra = [*BATTLE_SAMPLE, *extra]
    proc = subprocess.Popen([exe, "--headless", "--path", str(repo / "godot"), scene,
                             "--", *([] if "--mods" in extra else ["--no-mods"]), *extra],
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8", errors="replace",
                            env=child_env(slot))
    try:
        out, err = proc.communicate(timeout=RUN_TIMEOUT)
    except subprocess.TimeoutExpired:
        kill_tree(proc)
        out, err = proc.communicate()
        return 124, f"таймаут {RUN_TIMEOUT} с — сцена не вышла (исключение C#?)\n{out or ''}{err or ''}"
    note_audit(out or "")
    return proc.returncode, (out or "") + (err or "")


def items(field: str) -> list[str]:
    """Список из строки итога: «-» значит пусто."""
    return [] if field == "-" else field.split(",")


def run(repo: Path, screen: str) -> tuple[bool, str]:
    png = repo / ".cache" / "ui-check" / f"{screen}.png"
    png.parent.mkdir(parents=True, exist_ok=True)
    code, out = godot(repo, ["--screen", screen, "--png", str(png)])
    match = next((m for m in LINE.finditer(out)), None)
    if match is None:
        return False, f"{screen}: сцена не напечатала итог (код {code})\n{out.strip()}"

    line = match.group(0)
    if code != 0:
        return False, f"{line}  <-- код {code}\n{out.strip()}"
    if float(match["opaque"]) < MIN_OPAQUE:
        return False, f"{line}  <-- пусто: непрозрачных меньше {MIN_OPAQUE}"

    unexpected = [i for i in items(match["missing"]) if i not in KNOWN_MISSING]
    if unexpected:
        return False, f"{line}  <-- нет строк: {', '.join(unexpected)}"
    unknown = items(match["unknown"])
    if unknown:
        return False, f"{line}  <-- неразобранная разметка: {', '.join(unknown)}"
    return True, f"{line}  -> {png.name}"


def ink(repo: Path) -> tuple[bool, str]:
    code, out = godot(repo, ["--ink", "--text", INK_SAMPLE])
    match = next((m for m in INK.finditer(out)), None)
    if match is None:
        return False, f"шрифт: сцена не напечатала итог (код {code})\n{out.strip()}"
    line = match.group(0)
    if code != 0 or int(match["nochar"]) > 0:
        return False, f"{line}  <-- код {code}, нет глифов {match['nochar']}\n{out.strip()}"
    if int(match["ink1"]) <= int(match["ink0"]):
        return False, f"{line}  <-- кириллица не нарисовалась: ink1 не больше ink0"
    return True, f"{line}  -> кириллица есть ({match['chars']} символов)"


LINES = re.compile(r"^lines face=(?P<face>\S+) size=\d+ font=\S+ ascent=\S+ spacing=\S+ pitch=(?P<pitch>[\d.]+) want=(?P<want>\d+)$",
                   re.MULTILINE)

# Шаг строки растровых шрифтов оригинала — высота ячейки плюс межстрочный интервал из заголовка Interf/*.mft (u32 height, u32 lineSpacing);
# снимки 28.09: базовые линии строк через 17 точек у Normal (окно летописи, сообщение). Нет копии игры — таблица этих же чисел.
MFT_PITCH = {"small": 16, "normal": 17, "medbold": 17, "medium": 18, "menu": 18, "large": 18, "vlarge": 25}


def mft_pitch() -> tuple[dict[str, int], str]:
    """Шаг строки по заголовкам MFT из копии игры (D2_GAME_DIR/Interf), иначе — таблица."""
    game = os.environ.get("D2_GAME_DIR")
    folder = Path(game) / "Interf" if game else None
    if folder is None or not folder.is_dir():
        return MFT_PITCH, "таблица"
    got = {}
    for name in MFT_PITCH:
        path = folder / f"{name}.mft"
        if not path.exists():
            return MFT_PITCH, "таблица"
        data = path.read_bytes()
        name_len = int.from_bytes(data[8:12], "little")
        at = 12 + name_len
        got[name] = int.from_bytes(data[at:at + 4], "little") + int.from_bytes(data[at + 4:at + 8], "little")
    return got, "Interf/*.mft"


def lines(repo: Path) -> tuple[bool, str]:
    """Межстрочный шаг подписей порта (ответ пользователя 11.10, снимок 12: строки карточки города налезали) — у каждого начертания
    data/ui/fonts.json подпись в три строки идёт с шагом растрового шрифта оригинала (UiFont.Line → LabelSettings.LineSpacing)."""
    code, out = godot(repo, ["--lines"])
    want, source = mft_pitch()
    got = {m["face"].lower(): (float(m["pitch"]), int(m["want"])) for m in LINES.finditer(out)}
    bad = [f"{face} {got.get(face)} ≠ {pitch}" for face, pitch in want.items()
           if face not in got or abs(got[face][0] - pitch) > 0.01 or got[face][1] != pitch]
    if code != 0 or CSHARP_ERROR in out or bad:
        return False, f"шаг строки: код {code}, {'; '.join(bad)}\n{out.strip()}"
    return True, "шаг строки как у оригинала (" + source + "): " + ", ".join(f"{f} {int(p)}" for f, (p, _) in got.items())


TABS = re.compile(r"^tabs tab=(?P<tab>\d+) width=(?P<width>[\d.]+) want=(?P<want>[\d.]+) без_места=(?P<none>[\d.]+) слитно=(?P<joined>[\d.]+)$",
                  re.MULTILINE)


def tabs(repo: Path) -> tuple[bool, str]:
    """Табуляция подписи (прогон 2 пользователя 11.10, снимок 33: «Уровень:1», «Опыт:0» слитно) — строка карточки воина X005TA0423 с местом
    табуляции \\s110;: значение начинается с точки 110 (ширина «Уровень:», табуляция, «1» = 110 + ширина «1»). Без места табуляции
    знак табуляции шрифтом почти не рисуется — ширина как у слитного «Уровень:1»: это и было причиной."""
    code, out = godot(repo, ["--tabs"])
    m = TABS.search(out)
    if code != 0 or CSHARP_ERROR in out or m is None or int(m["tab"]) != 110 or abs(float(m["width"]) - float(m["want"])) > 0.01:
        return False, f"табуляция: код {code}\n{out.strip()}"
    return True, (f"табуляция: «Уровень:\\t1» — {float(m['width']):.0f} = 110 + «1»; без места табуляции было {float(m['none']):.0f} "
                  f"(слитно {float(m['joined']):.0f})")


LABEL = re.compile(r"^label (?P<id>\S+) место=(?P<box>\S+) верх=(?P<top>-?\d+) строк=(?P<lines>\d+) выс=(?P<h>\d+)$", re.MULTILINE)

# Верх блока строк от верха места — по снимку оригинала 28.09 CCapitalInterf-58839 (верх ячейки первой строки; глифы Normal — с 4-й
# строки ячейки): имя героя в плашке 18 точек — 0, «Скорость: 20 / 20» (две строки) в 85 — 25, ресурсы в 25 — 4. До исправления — 5,
# 27, 4.
TEXT_BLOCK_EXPECT = {"DLG_CAPITAL/TXT_LEADER_NAME": (0, 18), "DLG_CAPITAL/TXT_LEADER_STATS": (25, 60), "DLG_ISO_RESPOPUP/TXT_GOLD": (4, 21)}


def text_block(repo: Path) -> tuple[bool, str]:
    """Положение строк подписи по вертикали (прогон 3 пользователя 11.10, пункт 2): правило движка 0x52ad04 (LastEdict.Data.Ui.UiTextBlock) —
    блок = строки × шаг шрифта, середина с отбрасыванием дроби, не влез — от верха; подпись не растёт по шрифту темы. Экран карты
    столицы (без окна), --labels: верх блока и высота подписи. Last Edict: день 30 партии стенда, герой Кланов гостем в столице. Нет
    импорта — пропуск."""
    save, m, why = stand(repo, 30)
    if save is None:
        return True, f"строки подписей: пропуск — {why}"
    hero = m.field_stacks(STAND_PLAYER)[0]
    code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--clicks",
                             f"arrive {hero.id} {m.capital(STAND_PLAYER).id};panel click BTN_CAPITAL", "--labels"],
                      "res://scenes/map/MapScreen.tscn")
    got = {m["id"]: (int(m["top"]), int(m["h"])) for m in LABEL.finditer(out)}
    bad = [f"{k} {got.get(k)} ≠ {v}" for k, v in TEXT_BLOCK_EXPECT.items() if got.get(k) != v]
    if code != 0 or CSHARP_ERROR in out or bad:
        return False, f"строки подписей: код {code}, {'; '.join(bad)}\n{out.strip()}"
    return True, ("строки подписей как у оригинала: " + ", ".join(f"{k.split('/')[1]} верх {v[0]}" for k, v in TEXT_BLOCK_EXPECT.items()))


STAGE = re.compile(r"^stage показ=(?P<mode>\S+) буфер=(?P<w>\d+)x(?P<h>\d+) масштаб=(?P<scale>[\d.]+) угол=(?P<x>-?\d+),(?P<y>-?\d+) "
                   r"фильтр=(?P<filter>\S+)$", re.MULTILINE)

# Показ в окне 1280×720 (LastEdict.Data.Ui.ScreenFit): чёткий — буфер 2133×1200 (экраны в нём ×2), сжатие 0,6 по площади; целый — 1067×600
# точка в точку, рамка 106,60.
SCALED_FLOW = {"sharp": ("2133", "1200", "0.6", "area"), "integer": ("1067", "600", "1", "nearest")}


def scaled_flow(repo: Path) -> tuple[bool, str]:
    """Щелчки через показ (прогон 3, пункт 1): проход «назад и окно поверх» и до брифинга при чётком и целом масштабе с окном
    1280×720 (--window-size: у headless окно 64×64). События идут настоящим путём — в корневой вьюпорт в точках окна и обратным
    переводом показа в буфер: стек после каждого щелчка тот же, что и при прежнем масштабе."""
    cases = [c for c in FLOW_CASES if c[0] in ("назад и окно поверх", "до брифинга")]
    for mode, want in SCALED_FLOW.items():
        for name, start, steps in cases:
            extra = ["--scale", mode, "--window-size", "1280x720"]
            ok, message = flow(repo, name, start, steps, extra)
            if not ok:
                return False, f"показ {mode}: {message}"
        code, out = godot(repo, ["--scale", mode, "--window-size", "1280x720", "--input", "wait 1"], "res://scenes/ui/UiFlow.tscn")
        m = STAGE.search(out)
        if m is None or (m["w"], m["h"], m["scale"], m["filter"]) != want:
            return False, f"показ {mode}: {m.group(0) if m else 'нет строки stage'} — ожидалось буфер {want[0]}x{want[1]} масштаб {want[2]} {want[3]}"
    return True, "показ: щелчки доходят при чётком (буфер 2133×1200, сжатие 0,6) и целом (1067×600 в рамке) масштабе"


def strokes(repo: Path, window: bool) -> tuple[bool, str]:
    """Толщина линий растра после масштаба 1,2 (прогон 3, пункт 1; tools/stroke-check.py): у «чёткого» и «целого» вес каждой линии в
    точку разметки одинаков, у прежнего — то 0,83, то 1,67. Без --window — модель показа по раскладке сцены; с --window — снимки окна."""
    proc = subprocess.run([sys.executable, str(repo / "tools" / "stroke-check.py"), *(["--window"] if window else [])],
                          capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=600, env=child_env())
    out = proc.stdout.strip()
    lines = [l for l in out.splitlines() if l.startswith("stroke ") and "итог" not in l]
    if proc.returncode != 0:
        return False, f"толщина линий: {out}\n{proc.stderr}"
    return True, "толщина линий (" + ("окно" if window else "модель") + "): " + "; ".join(
        " ".join(l.split()[1:2] + [w for w in l.split() if w.startswith("разброс=")]) for l in lines)


def live(repo: Path, screen: str, counts: str | None, steps: list) -> tuple[bool, str]:
    extra = ["--screen", screen, "--input", "; ".join(step[0] for step in steps)]
    if counts:
        extra += ["--items", counts]
    code, out = godot(repo, extra)
    got = list(INPUT.finditer(out))
    if code != 0 or len(got) != len(steps):
        return False, f"{screen}: ввод — код {code}, шагов {len(got)} из {len(steps)}\n{out.strip()}"
    for step, match in zip(steps, got):
        action, events, frame = step[:3]
        if len(step) > 3 and match["tip"] != step[3]:
            return False, f"{screen}: после «{action}» подсказка {match['tip']}, ожидалась {step[3]}"
        if match["events"] != events:
            return False, f"{screen}: «{action}» дал {match['events']}, ожидалось {events}"
        if frame is not None and frame not in items(match["changed"]):
            return False, f"{screen}: «{action}» сменил кадры {match['changed']}, ожидался {frame}"
    return True, f"{screen}: ввод — {len(steps)} шагов, события и кадры как ожидалось"


def flow(repo: Path, name: str, start: str | None, steps: list, more: list[str] | None = None) -> tuple[bool, str]:
    # Окна загрузки читают каталог сохранений — свой пустой, а не сохранения пользователя.
    saves = repo / ".cache" / "ui-check" / "flow-saves"
    saves.mkdir(parents=True, exist_ok=True)
    extra = ["--input", "; ".join(step[0] for step in steps), "--saves", str(saves), *(more or [])]
    if start:
        extra += ["--start", start, "--mods", "../tools/ui-check-mods"]
    code, out = godot(repo, extra, "res://scenes/ui/UiFlow.tscn")
    got = list(FLOW.finditer(out))
    if code != 0 or len(got) != len(steps):
        return False, f"граф «{name}»: код {code}, шагов {len(got)} из {len(steps)}\n{out.strip()}"
    for step, match in zip(steps, got):
        action, stack = step[0], step[1]
        if match["stack"] != stack:
            return False, f"граф «{name}»: после «{action}» стек {match['stack']}, ожидался {stack}"
        if len(step) > 2 and int(match["built"]) != step[2]:
            return False, f"граф «{name}»: после «{action}» построено экранов {match['built']}, ожидалось {step[2]}"
    return True, f"граф «{name}»: {len(steps)} шагов, стек как ожидалось (фаза в конце {got[-1]['phase']})"


# Ролики «Вступления» (LastEdict.Data.Menu.CinematicLogic): шаг сценария → (стек, ролик IMG_FULL, ролик IMG_ANIM, рамка IMG_ANIM_OVER, фон, ролик
# смены). Таймер показа — раз в 1000 мс от открытия (0x4c66be): пропуск (левая кнопка 0x4c6668, Enter/пробел/Esc 0x4c668f) действует на
# ближайшем тике; конец показа — главное меню через TRANS_INTRO2MAIN (0x4ea1f3(1), 0x4eb909).
CINEMATIC_STEPS = [
    ("wait 999", "DLG_CINEMATIC", "SF#1", "-", "-", "-", "-"),             # запуск: SF без рамки, фон пуст (CIntroAnim, первый ролик)
    ("click 400,300", "DLG_CINEMATIC", "SF#1", "-", "-", "-", "-"),        # пропуск — ещё до тика
    ("wait 1", "DLG_CINEMATIC", "-", "INTRO#2", "_CINEMATIC_OVER", "_CINEMATIC", "-"),   # тик 1000: INTRO в рамке
    ("wait 1500", "DLG_CINEMATIC", "-", "INTRO#2", "_CINEMATIC_OVER", "_CINEMATIC", "-"),
    ("key 32", "DLG_CINEMATIC", "-", "INTRO#2", "_CINEMATIC_OVER", "_CINEMATIC", "-"),
    ("wait 499", "DLG_CINEMATIC", "-", "INTRO#2", "_CINEMATIC_OVER", "_CINEMATIC", "-"),
    ("wait 1", "DLG_MAIN_MENU", None, None, None, None, "TRANS_INTRO2MAIN"),   # тик 3000 — меню
    ("click BTN_INTRO", "DLG_CINEMATIC", "-", "INTRO#1", "_CINEMATIC_OVER", "_CINEMATIC", "-"),   # «Вступление» — один INTRO в рамке
    ("key 65", "DLG_CINEMATIC", "-", "INTRO#1", "_CINEMATIC_OVER", "_CINEMATIC", "-"),          # прочие клавиши не пропускают
    ("wait 1000", "DLG_CINEMATIC", "-", "INTRO#1", "_CINEMATIC_OVER", "_CINEMATIC", "-"),
    ("key 27", "DLG_CINEMATIC", "-", "INTRO#1", "_CINEMATIC_OVER", "_CINEMATIC", "-"),
    ("wait 1000", "DLG_MAIN_MENU", None, None, None, None, "TRANS_INTRO2MAIN"),
]


def cinematic(repo: Path) -> tuple[bool, str]:
    """Ролики «Вступления» (CIntroAnim 0x4c6af3, CAnimInterf 0x4c635a, CMenuFullScreenAnimSnd 0x4daba0): при запуске (--intro) — заставка
    SF без рамки, затем INTRO в рамке DLG_CINEMATIC (фон _CINEMATIC, рамка _CINEMATIC_OVER поверх ролика); кнопка «Вступление» — INTRO в
    рамке; пропуск — левая кнопка, Enter, пробел, Esc, на ближайшем секундном тике; конец — меню через TRANS_INTRO2MAIN; музыка меню
    затихает до ролика «Вступления» (music stop)."""
    extra = ["--intro", "--input", "; ".join(s[0] for s in CINEMATIC_STEPS), "--probe", "IMG_FULL,IMG_ANIM,IMG_ANIM_OVER,BACKGROUND"]
    code, out = godot(repo, extra, "res://scenes/ui/UiFlow.tscn")
    steps: list[dict] = []
    for line in out.splitlines():
        if (m := FLOW.search(line)) is not None:
            steps.append({"stack": m["stack"], "video": m["video"], "probes": {}})
        elif (p := PROBE.search(line)) is not None and steps:
            steps[-1]["probes"][p["id"]] = p.groupdict()
    if code != 0 or len(steps) != len(CINEMATIC_STEPS):
        return False, f"вступление: код {code}, шагов {len(steps)} из {len(CINEMATIC_STEPS)}\n{out.strip()}"
    for (action, stack, full, anim, over, back, video), got in zip(CINEMATIC_STEPS, steps):
        probes = got["probes"]
        seen = (got["stack"], got["video"])
        if stack == "DLG_CINEMATIC":
            seen += (probes.get("IMG_FULL", {}).get("video"), probes.get("IMG_ANIM", {}).get("video"),
                     probes.get("IMG_ANIM_OVER", {}).get("frame"), probes.get("BACKGROUND", {}).get("frame"))
            want = (stack, video, full, anim, over, back)
        else:
            want = (stack, video)
        if seen != want:
            return False, f"вступление: после «{action}» {seen}, ожидалось {want}"
    if "music stop" not in out:
        return False, "вступление: музыка меню не затихла перед роликом «Вступления»"
    return True, "вступление: запуск SF → INTRO в рамке → меню (TRANS_INTRO2MAIN), «Вступление» — INTRO, пропуск на секундном тике"


def content(repo: Path) -> tuple[bool, str]:
    """Цепочка «новой игры» с содержимым экранов: строки flow step и probe по шагам."""
    extra = ["--input", "; ".join(CONTENT_STEPS), "--probe", ",".join(PROBES)]
    code, out = godot(repo, extra, "res://scenes/ui/UiFlow.tscn")
    steps: list[dict] = []
    for line in out.splitlines():
        if (m := FLOW.search(line)) is not None:
            steps.append({"video": m["video"], "vrect": m["vrect"], "stack": m["stack"], "probes": {}})
        elif (p := PROBE.search(line)) is not None and steps:
            steps[-1]["probes"][p["id"]] = p.groupdict()
    if code != 0 or len(steps) != len(CONTENT_STEPS):
        return False, f"логика новой игры: код {code}, шагов {len(steps)} из {len(CONTENT_STEPS)}\n{out.strip()}"
    for step, element, field, expected in CONTENT_EXPECT:
        got = steps[step - 1]
        value = got["video"] if element is None else got["probes"].get(element, {}).get(field)
        if value != expected:
            where = "ролик смены" if element is None else f"{element}.{field}"
            return False, f"логика новой игры: шаг {step} «{CONTENT_STEPS[step - 1]}»: {where} = {value}, ожидалось {expected}"
    # Ролик смены — на месте экрана 800×600, а не во всё окно (CMenuFullScreenAnim 0x4da85c — по центру, без растяжения): 4:3 и по
    # центру окна, как экраны (UiScreenView.Letterbox); поля окна — чёрные (GameData, цвет очистки).
    for i, got in enumerate(steps, 1):
        if got["video"] == "-":
            continue
        rect = re.fullmatch(r"(-?\d+),(-?\d+),(\d+)x(\d+)", got["vrect"] or "")
        if rect is None or int(rect[3]) * 3 != int(rect[4]) * 4 or (int(rect[1]) != 0 and int(rect[2]) != 0):
            return False, f"логика новой игры: шаг {i}: ролик {got['video']} в {got['vrect']}, ожидалось место экрана 4:3 по центру"
    if "flow clear=000000" not in out:
        return False, "логика новой игры: поля окна не чёрные (нет строки flow clear=000000)"
    if out.count("fx Midgard/CHNGFACE ") != 1:   # смена лица лорда — звук CHNGFACE, второе нажатие во время ролика беззвучно (docs/ai-screen.md §12)
        return False, f"логика новой игры: звук CHNGFACE {out.count('fx Midgard/CHNGFACE ')} раз, ожидался 1"
    return True, (f"логика новой игры: {len(CONTENT_STEPS)} шагов до {steps[-1]['stack']}, "
                  f"{len(CONTENT_EXPECT)} проверок содержимого и роликов, ролики смены 4:3 по центру "
                  f"({next(s['vrect'] for s in steps if s['video'] != '-')}), поля чёрные, звук смены лица")


GROUND = re.compile(r"ground scenario=\S+ size=(?P<size>\d+) seed=\d+ types=\S+ image=\S+ "
                    r"painted=(?P<painted>[\d.]+) magenta=(?P<magenta>\d+)")


def ground(repo: Path) -> tuple[bool, str]:
    """Земля Tutorial из собранной графики (сцена dev/GroundTest): вся карта закрашена, мадженты нет. Нужны
    assets/gfx (tools/gfx-assets) и копия игры (сценарий читается стендом из Exports)."""
    png = repo / ".cache" / "ui-check" / "ground-Tutorial.png"
    code, out = godot(repo, ["--scenario", "Tutorial", "--png", str(png)], "res://scenes/dev/GroundTest.tscn")
    match = GROUND.search(out)
    if code != 0 or match is None:
        return False, f"земля: код {code}\n{out.strip()}"
    return True, f"{match.group(0)}  -> {png.name}"


MAPSCREEN = re.compile(r"mapscreen map=\S+ viewer=\S+ screens=(?P<screens>\d+) ground_ready=(?P<ready>\w+) "
                       r"sprites=(?P<sprites>\d+) minimap=(?P<minimap>\d+) лидер=(?P<leader>\S+) "
                       r"ходы=(?P<left>\d+)/(?P<max>\d+) день=(?P<turn>\d+)")

# Ход на Tutorial: отряд стоит в (24, 2) — выбрать его, показать путь на соседнюю клетку, пойти, кончить ход.
MAP_CLICKS = "24,2;25,3;25,3;end"


def mapscreen(repo: Path) -> tuple[bool, str]:
    """Экран карты (сцена map/MapScreen) на Tutorial: пять экранов собраны, земля под камерой готова, объекты и
    мини-карта нарисованы, а щелчки проходят весь ход — выбрать отряд, показать путь, пойти и кончить ход (день
    становится вторым, очки хода возвращаются). Имена картинок на всех 33 картах проверяет dotnet test."""
    # Игрок S001PL0000 в сценарии — ИИ; проверка ведёт его человеком (иначе его сценарные приказы ИИ двигают отряд после «end»).
    code, out = godot(repo, ["--scenario", "Tutorial", "--check", "--player", "S001PL0000", "--humans", "S001PL0000", "--clicks", MAP_CLICKS],
                      "res://scenes/map/MapScreen.tscn")
    match = MAPSCREEN.search(out)
    if (code != 0 or match is None or match["ready"] != "True" or int(match["screens"]) != 5
            or int(match["sprites"]) == 0 or int(match["minimap"]) != 1 or match["leader"] == "-"
            or int(match["turn"]) < 1 or "walked S001KC0004 " not in out or not re.search(r"^aiturn .* ошибка=0", out, re.M)):
        return False, f"экран карты: код {code}\n{out.strip()}"
    # Узел звука один (GameAudio.Of дважды в одном кадре создавал второй); графика карты во вьюпорте — точками (1 = Nearest у
    # корня MapView, а фильтр SubViewport по умолчанию — Linear).
    extra = re.search(r" звук=(?P<audio>\d+) фильтр=(?P<filter>\d+) ", out)
    if extra is None or extra["audio"] != "1" or extra["filter"] != "1":
        return False, f"экран карты: узлов звука/фильтр {extra.group(0) if extra else '-'} (ожидалось звук=1 фильтр=1)"
    return True, match.group(0)


def leader_face(repo: Path) -> tuple[bool, str]:
    """Портрет лидера в панели команд (пометка 1 прогона 14.10): «Arrival of the Pilgrims» выдаёт отряд Karasten с лидером-ассасином
    G000UU5354 — своего FACE в Faces.ff у типа нет, движок (0x5b1e6a) берёт базовый тип BASE_UNIT G000UU0154. Строка выбора отряда
    печатает, какая картинка в слоте портрета."""
    code, out = godot(repo, ["--scenario", "Arrival of the Pilgrims", "--check", "--player", "S143PL0001", "--clicks",
                             "select S143KC004b"], "res://scenes/map/MapScreen.tscn")
    line = re.search(r"^panel select S143KC004b .*$", out, re.MULTILINE)
    if code != 0 or line is None or " портрет=1 лицо=G000UU0154FACE " not in line.group(0) + " ":
        return False, f"портрет лидера: код {code}\n{line.group(0) if line else out.strip()}"
    message = "портрет лидера: ассасин сценария (G000UU5354) — G000UU0154FACE по базовому типу"
    # Павший лидер (партия 17.10, пометка 3 — BI2): панель отряда 0x48d8f3 — состояние 10, маска-череп MASKDEADS поверх лица, без красной
    # доли; портрет панели отражён (setLeftSide(0), снимок оригинала 4,9 против 37,4 без отражения). Сохранение оригинала дня 3 Кланов:
    # у S073KC0003 лидер пал (LEADR_ALIV 0), S073KC0002 — живой и здоровый. «отражённое=» — разница с отражённым исходным лицом
    # (0 — ни маски, ни доли), «сырое=» — с неотражённым. Last Edict: день 30 партии стенда — первый отряд с павшим лидером на карте и
    # живой отряд того же игрока, его глазами.
    save, m, _ = stand(repo, 30)
    if save is None:
        return True, message + "; павший — пропуск: нет импорта"
    fallen = next((st for st in sorted(m.stacks, key=lambda st: st.id) if not st.leader_alive and not st.inside), None)
    if fallen is None:
        return False, message + "; павший — на дне 30 партии стенда нет отряда с павшим лидером (сменился импорт или правила?)"
    alive = m.field_stacks(fallen.owner)[0]
    code, out = godot(repo, ["--state", str(save), "--check", "--player", fallen.owner, "--clicks", f"select {fallen.id};select {alive.id}"],
                      "res://scenes/map/MapScreen.tscn")
    face = {m["stack"]: m for m in re.finditer(r"^panel select (?P<stack>\S+) .* портрет=(?P<shown>\d) .* отражено=(?P<mirror>\d) "
                                                r"павший=(?P<dead>\d) маска=(?P<mask>\S+) доля=(?P<share>\d+) значки=\S+ "
                                                r"сырое=(?P<raw>[\d.]+) отражённое=(?P<flip>[\d.]+) ", out, re.M)}
    dead, live = face.get(fallen.id), face.get(alive.id)
    if (code != 0 or dead is None or live is None or dead["shown"] != "1" or dead["dead"] != "1" or dead["mask"] != "MASKDEADS"
            or dead["share"] != "100" or float(dead["flip"]) < 10 or live["dead"] != "0" or live["mask"] != "-"
            or (live["share"] == "100" and float(live["flip"]) != 0) or float(live["raw"]) < 5 or dead["mirror"] != "1"
            or live["mirror"] != "1"):
        return False, f"портрет павшего лидера: код {code}\n{out.strip()}"
    return True, (f"{message}; павший {fallen.id} — {dead['mask']}, доля {dead['share']}, разница с лицом {dead['flip']}; живой {alive.id} — "
                  f"отражён (с отражённым лицом {live['flip']}, с исходным {live['raw']})")


MAPBATTLE = re.compile(r"mapbattle end winner=(?P<winner>\S+) attacker_alive=(?P<a>\d) defender_alive=(?P<d>\d) turns=(?P<turns>\d+)")


def mapbattle(repo: Path) -> tuple[bool, str]:
    """Бой с карты (пункт 6): на Tutorial отряд человека выходит из столицы к нейтралу на (42,18) — путь кончается
    рядом, отряд нападает, бой доигрывается автобоем, проигравший снят с карты, карта возвращается."""
    code, out = godot(repo, ["--scenario", "Tutorial", "--check", "--player", "S001PL0009", "--clicks", "30,9;42,18;42,18"],
                      "res://scenes/map/MapScreen.tscn")
    battle = MAPBATTLE.search(out)
    screen = MAPSCREEN.search(out)
    if (code != 0 or battle is None or screen is None or battle["winner"] == "-"
            or int(battle["a"]) + int(battle["d"]) != 1):
        return False, f"бой с карты: код {code}\n{out.strip()}"
    # Подпись крупного портрета (0x5e97dc): у героя человека — его имя «Джагар» (перевод Руссобит-М, data/scenarios/lang/ru;
    # в сценарии — «Jagare»), у нейтрала — своё или имя типа, пустых нет.
    opened = re.search(r"battle open .* подпись=(?P<caption>\S+) имена=(?P<names>\S+)", out)
    if (opened is None or not opened["caption"].startswith("Джагар|") or "S001UN0001:Джагар" not in opened["names"].split(",")
            or any(n.endswith(":-") for n in opened["names"].split(","))):
        return False, f"бой с карты: подпись крупного портрета {opened.group(0) if opened else '-'}"
    # Конец боя — на экране боя до «Закрыть» (замечание 33 прогона 11.10): конец боя модели и его показ (mapbattle over, строки
    # battle end … — уход призванных, откаты, рост UPGRADE), затем опыт и кнопки (battle end shown), и только потом итог в карту.
    lines = out.splitlines()
    ending = next((i for i, line in enumerate(lines) if line.startswith("mapbattle over ")), -1)
    shown = next((i for i, line in enumerate(lines) if line.startswith("battle end shown ")), -1)
    closed = next((i for i, line in enumerate(lines) if line.startswith("mapbattle end ")), -1)
    if not 0 <= ending < shown < closed:
        return False, f"бой с карты: конец боя не до «Закрыть» (over {ending}, shown {shown}, end {closed})\n{out.strip()}"
    # Опыт конца боя (0x64d4a4, docs/battle-screen.md §9): у получивших опыт — «Опыт: +N» и портрет светло-жёлтым до закрытия (состояние
    # Experience), у выросших — вспышка (Experience, затем Plain); прочие — своё.
    xp = re.search(r"опыт=(?P<gains>\S+) портреты=(?P<faces>\S*)", lines[shown])
    faces = dict(f.split("=", 1) for f in (xp["faces"].split(",") if xp and xp["faces"] else []))
    entries = xp["gains"].split(",") if xp and xp["gains"] != "-" else []
    gained = [g.split("+")[0] for g in entries if "+" in g]
    grown = [g.split(":")[0] for g in entries if g.endswith(":рост")]
    if (xp is None or not entries or any(not faces.get(u, "").startswith("Experience@") for u in gained)
            or any(not faces.get(u, "").startswith(("Experience@", "Plain@")) for u in grown)):
        return False, f"бой с карты: опыт без жёлтого портрета: {lines[shown].strip()}"
    # «Опыт: +N» без срока (0x64d23e, Зс53): надпись держится вместе с жёлтым до закрытия боя — после сборки панелей конца боя она на месте.
    steady = re.search(r"стойких=(\d+)", lines[shown])
    if gained and (steady is None or int(steady[1]) == 0):
        return False, f"бой с карты: «Опыт: +N» не держится до закрытия: {lines[shown].strip()}"
    return True, f"{battle.group(0)}; конец боя показан до «Закрыть»: {lines[shown].strip()}"


FLASH = re.compile(r"battle flash (?P<unit>\S+) база=(?P<base>\w+)@(?P<plain>[\d;]+) (?P<faces>.*?) срок=(?P<gone>[\d.]+)с "
                   r"после=(?P<after>\w+)@(?P<after_px>[\d;]+) держится=(?P<kept>\w+) после_сборки=(?P<rebuilt>\w+) понижение_строк=(?P<wrap>\S+)")


def battle_flash(repo: Path) -> tuple[bool, str]:
    """Вспышки портретов боя (docs/battle-screen.md §9, таблицы 0x5b191f/0x5b19fa/0x5b1a91/0x5b1add): точка середины портрета основной
    панели под каждой вспышкой — смешение наполовину с цветом состояния (у превращения — чёрным целиком), без заливки — как база; вспышка
    уходит по сроку надписи, держащаяся — сборкой панелей; «Понижение уровня» на малом портрете — две строки, ни одна не шире 70."""
    code, out = godot(repo, ["--check", "--flash"], "res://scenes/battle/BattleScreen.tscn")
    m = FLASH.search(out)
    if code != 0 or m is None:
        return False, f"вспышки портретов: код {code}\n{out.strip()}"

    def px(text: str) -> tuple[int, ...]:
        return tuple(int(v) for v in text.split(";"))

    base = px(m["plain"])
    got = {k: px(v) for k, v in (f.split("=") for f in m["faces"].split())}
    colours = {"Hit": (255, 0, 0), "Missed": (255, 255, 0), "Healed": (0, 0, 255), "Boosted": (0, 255, 0), "LowerDamage": (255, 0, 255),
               "Poisoned": (128, 128, 0), "Shattered": (94, 218, 249), "Experience": (255, 255, 64)}
    bad = [f"{k}={got.get(k)} ждали≈{tuple((b + c) // 2 for b, c in zip(base, colour))}" for k, colour in colours.items()
           if got.get(k) is None or any(abs(g - (b + c) // 2) > 1 for g, b, c in zip(got[k], base, colour))]
    bad += [f"{k}={got.get(k)} ждали базу {base}" for k in ("Shielded", "Pass") if got.get(k) != base]
    if got.get("Transformed") != (0, 0, 0):
        bad.append(f"Transformed={got.get('Transformed')} ждали 0;0;0")
    if m["base"] != "Normal" or m["after"] != "Normal" or px(m["after_px"]) != base or not 0.3 <= float(m["gone"]) <= 2.0:
        bad.append(f"срок {m['gone']} с, после {m['after']}@{m['after_px']}")
    if m["kept"] != "Transformed" or m["rebuilt"] != "Normal":
        bad.append(f"держащаяся {m['kept']} → {m['rebuilt']}")
    if m["wrap"] != "2x0":
        bad.append(f"«Понижение уровня»: строк×шире {m['wrap']}, ждали 2x0")
    if bad:
        return False, f"вспышки портретов: {'; '.join(bad)}\n{m.group(0)}"
    return True, f"вспышки портретов: {m.group(0)}"


def paperdoll(repo: Path) -> tuple[bool, str]:
    """«Меню героя» после боя (BTN_PAPERDOLL → closeBattleWindow(true) 0x633e20 → CRequestPaperdollMsg → RequestPaperdoll 0x41f397):
    бой отряда человека с победой; бой закрыт
    «Меню героя» (--paperdoll): после итога боя проверка событий с отрядом и окно отряда DLG_MANAGE_STACK (CCmdStackActionMsg(отряд, 0) →
    0x48ee26 → CManageStkInterf). Last Edict: день 5 партии стенда, герой Кланов берёт ближнюю охраняемую руину. Нет импорта — пропуск."""
    save, m, why = stand(repo, 5)
    if save is None:
        return True, f"«Меню героя»: пропуск — {why}"
    hero, ruin = ruin_raid(m)
    code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--clicks", f"arrive {hero.id} {ruin.id};arrows",
                             "--paperdoll"], "res://scenes/map/MapScreen.tscn")
    lines = out.splitlines()
    closed = next((i for i, line in enumerate(lines) if line.startswith(f"mapbattle end winner={hero.id} ")), -1)
    asked = next((i for i, line in enumerate(lines) if line.startswith(f"paperdoll stack={hero.id} запрос окно=DLG_MANAGE_STACK")), -1)
    shown = next((i for i, line in enumerate(lines) if line.startswith("arrows ") and "окна=DLG_MANAGE_STACK" in line), -1)
    if code != 0 or not 0 <= closed < asked < shown:
        return False, f"«Меню героя»: код {code}, итог боя {closed}, запрос {asked}, окно {shown}\n{out.strip()}"
    return True, f"«Меню героя»: {lines[asked].strip()} — после итога боя, окно открыто"


def settings_file(path: Path) -> dict:
    """Записанный игрой файл настроек (схема 2 — разделы, src/Game.UiModel/Map/SettingsFile.cs): «раздел.ключ» → значение и «version»;
    нет файла — пусто."""
    if not path.exists():
        return {}
    data = json.loads(path.read_text(encoding="utf-8"))
    flat = {f"{section}.{key}": value for section, values in data.items() if isinstance(values, dict) for key, value in values.items()}
    return {**flat, "version": data.get("version")}


def rebinding(repo: Path) -> tuple[bool, str]:
    """Переназначение клавиш (Э5, Р12; вкладка «Управление», data/ui/input.json): на карте дня 21 стенда «Конец хода» — E вместо пробела,
    «Охранять» — пробел; ожидание клавиши «Активировать посох» снято Esc без изменений. «Применить» — файл настроек (controls.bindings);
    закрыли окно — пробел конец хода больше не жмёт. Запуск 2 с тем же файлом: E — конец хода. Запуск 3 без файла: кнопка Y геймпада —
    конец хода по умолчанию. Конец хода — строка aiturn. Нет импорта — пропуск."""
    save, _, why = stand(repo, 21)
    if save is None:
        return True, f"переназначение клавиш: пропуск — {why}"
    settings = repo / ".cache" / "ui-check" / "rebind-settings.json"
    if settings.exists():
        settings.unlink()
    base = ["--state", str(save), "--check", "--player", STAND_PLAYER]
    runs = [
        ([*base, "--settings", str(settings)],
         "ui click BTN_OPTIONS;ui tab Controls;ui capture map.BTN_END_TURN key;ui key 69;ui capture map.BTN_GUARD key;ui key 32;"
         "ui capture map.BTN_WAND_SCROLL key;ui key 27;ui apply;ui click BTN_CLOSE;ui key 32"),
        ([*base, "--settings", str(settings)], "ui key 69"),
        (base, "ui pad Y"),
    ]
    outs = []
    for extra, clicks in runs:
        code, out = godot(repo, [*extra, "--clicks", clicks], "res://scenes/map/MapScreen.tscn")
        outs.append((code, out, len(re.findall(r"^aiturn ", out, re.M))))
    first = outs[0][1]
    bound = re.findall(r"^settings привязка (\S+) (\S+) теперь=(\S+)$", first, re.M)
    saved = settings_file(settings).get("controls.bindings")
    want_bound = [("map.BTN_END_TURN", "key=key:E", "pad:Y,key:E"), ("map.BTN_GUARD", "key=key:Space", "key:Space")]
    want_saved = {"map.BTN_END_TURN": ["pad:Y", "key:E"], "map.BTN_GUARD": ["key:Space"]}
    open_after_esc = re.search(r"^ui key_27 on=LE_SETTINGS .* окна=LE_SETTINGS ", first, re.M)
    turns = [t for _, _, t in outs]
    if (any(code != 0 for code, _, _ in outs) or bound != want_bound or saved != want_saved or open_after_esc is None
            or turns != [0, 1, 1]):
        return False, (f"переназначение клавиш: коды {[c for c, _, _ in outs]}, привязки {bound}, файл {saved}, Esc оставил окно "
                       f"{open_after_esc is not None}, концов хода {turns}\n{first.strip()[-3000:]}")
    return True, ("переназначение клавиш: «Конец хода» — E, «Охранять» — пробел, Esc снял ожидание; файл записан по «Применить»; "
                  "пробел хода не кончает, E — кончает (между запусками), Y геймпада — по умолчанию")


PAD_FOCUS = re.compile(r"^pad focus (\S+) (\S+) → (\S+) pointer (\d+),(\d+) ", re.M)
POINTER = re.compile(r"^pointer (\d+),(\d+) геймпад=(\d) крупный=\d подсказки=(\S+) окна=(\S+)", re.M)


def gamepad(repo: Path) -> tuple[bool, str]:
    """Геймпад (Э5, Р12; GamepadPointer, UiNavigation, data/ui/input.json). Меню: крестовина переводит указатель по кнопкам главного меню
    (вниз, вниз, вверх), левый стик сдвигает его (−1 по X 0,4 с — на 1,25 × 720 × 0,4 = 360 точек), крестовина возвращает на кнопку, A —
    щелчок («Одиночная игра»), B — «Назад». Карта (день 21 стенда): указатель в середину поля карты над своим отрядом, A — отряд выделен
    настоящим путём (кнопка геймпада → щелчок мыши → вьюпорт карты); Start — окно настроек, указатель к строке с фокусом, крестовина вниз —
    к следующей; B — закрыто; Y — конец хода. Подсказки кнопок внизу: на карте — A, B, Y (конец хода), LB, RB и прокрутка RS; в окне
    настроек — A, B и «✥» (крестовина по фокусу). B на пустой карте опций не открывает (Esc — горячая клавиша «Опций», а у них на
    геймпаде своя кнопка — Start)."""
    code, out = godot(repo, ["--input", "pad DpadDown; pad DpadDown; pad DpadUp; stick -1,0 400; pad DpadDown; pad DpadUp; pad A; pad B"],
                      "res://scenes/ui/UiFlow.tscn")
    focus = [(m[2], m[3]) for m in PAD_FOCUS.finditer(out)]
    stick = re.search(r"^flow step=4 do=stick at=pointer (\d+),(\d+) ", out, re.M)
    stacks = re.findall(r"^flow step=\d+ .* stack=(\S+) ", out, re.M)
    # «Мультиплеер» открыт (Э7, лобби) — крестовина идёт на него: «Одиночная игра» → «Мультиплеер».
    want = [("down", "BTN_SINGLE"), ("down", "BTN_MULTI"), ("up", "BTN_SINGLE"), ("down", "BTN_MULTI"), ("up", "BTN_SINGLE")]
    first = PAD_FOCUS.search(out)
    moved = stick is not None and first is not None and int(first[4]) - int(stick[1]) == 360 and stick[2] == first[5]
    if code != 0 or focus != want or not moved or stacks[-2:] != ["DLG_SINGLE_PLAYER", "DLG_MAIN_MENU"]:
        return False, f"геймпад, меню: код {code}, фокус {focus}, стик {stick and stick.group(0)}, экраны {stacks}\n{out.strip()[-3000:]}"
    save, m, why = stand(repo, 21)
    if save is None:
        return True, f"геймпад: меню — крестовина, стик, A, B; карта — пропуск: {why}"
    own = m.field_stacks(STAND_PLAYER)[0]
    code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--clicks",
                             f"padcell {own.x},{own.y};pointer;ui pad Start;pointer;ui pad DpadDown;pointer;ui pad B;ui pad B;pointer;ui pad Y"],
                      "res://scenes/map/MapScreen.tscn")
    picked = re.search(rf"^padcell {own.x},{own.y} выбран=(\S+) ", out, re.M)
    rows = [(int(x), int(y), pad, hints, windows) for x, y, pad, hints, windows in POINTER.findall(out)]
    turns = len(re.findall(r"^aiturn ", out, re.M))
    points = rows[1:3]
    no_options = len(rows) == 4 and rows[3][4] == "-"   # B на пустой карте — окон нет
    follows = (len(points) == 2 and all(w == "LE_SETTINGS" and pad == "1" for _, _, pad, _, w in points)
               and points[1][1] > points[0][1])
    map_hints = rows[0][3].split("___") if rows else []
    hinted = ([h.split("_")[0] for h in map_hints[:2]] == ["A", "B"] and {"Y", "LB", "RB"} <= {h.split("_")[0] for h in map_hints}
              and map_hints[-1].startswith("RS_") and len(points) == 2 and points[0][3].split("___")[-1].startswith("✥_"))
    if code != 0 or picked is None or picked[1] != own.id or not follows or turns != 1 or not hinted or not no_options:
        return False, (f"геймпад, карта: код {code}, выбран {picked and picked[1]} (ждали {own.id}), указатель {rows}, концов хода {turns}"
                       f"\n{out.strip()[-3000:]}")
    return True, (f"геймпад: меню — крестовина по кнопкам, стик −360 точек, A — «Одиночная игра», B — назад; карта — A выделил {own.id}, "
                  f"Start — настройки, указатель за фокусом {points[0][:2]} → {points[1][:2]}, B — закрыто, B на пустой карте — без опций, Y — конец хода; подсказки "
                  f"{len(map_hints)} на карте, «✥» в окне")


HD_SCREENS = ["DLG_MAIN_MENU", "DLG_SINGLE_PLAYER", "DLG_CHOOSE_RACE", "DLG_ACTION_RESULT", "DLG_BATTLE_A", "DLG_STRATEGIC",
              "DLG_ISO_PAL", "DLG_CAPITAL", "DLG_CITY_STACK"]
SPRITE = re.compile(r"^sprite (\S+) (\S+?)(?::(\d+))?$", re.M)


def hd_mods(repo: Path) -> Path | str:
    """Синтетический HD-слой (Р19) для проверки: интерфейс и портреты выгружены tools/hd/hd_layer.py, «апскейл» ×2 ближайшим соседом
    (вместо Topaz — точки те же, только вдвое гуще), импорт тем же инструментом в мод. Строится один раз в .cache/ui-check/hd."""
    base = repo / ".cache" / "ui-check" / "hd"
    mod = base / "mods" / "hd"
    if (mod / "import-report.txt").exists():
        return base / "mods"
    sys.path.insert(0, str(repo / "tools" / "hd"))
    import hd_layer
    from PIL import Image
    export, up = base / "export", base / "up"
    if hd_layer.export(export, ["ui", "faces"], repo / "data", hd_layer.assets_root()) != 0:
        return "выгрузка не прошла"
    up.mkdir(parents=True, exist_ok=True)
    for f in export.glob("*.png"):
        with Image.open(f) as im:
            im.resize((im.width * 2, im.height * 2), Image.Resampling.NEAREST).save(up / f.name)
    hd_layer.import_layer(export, up, mod, "hd-check")
    hd_layer.compress(mod)   # DDS BC7 рядом с PNG — текстуры берут его
    return base / "mods"


def hd_layer_step(repo: Path) -> tuple[bool, str]:
    """HD-графика (Р19, scale в описях): слой ×2 интерфейса и портретов (hd_mods) — экраны с ним и без совпадают по каждому спрайту:
    кадр, место и нарисованный размер (текстура HD — логического размера), непрозрачных точек почти столько же (логическая копия
    HD — уменьшение). Портрет панели карты — того же места и размера. Нет импорта — пропуск."""
    if not (repo / "data" / "ui" / "assets.json").exists():
        return True, "HD-слой: пропуск — нет импорта"
    mods = hd_mods(repo)
    if isinstance(mods, str):
        return False, f"HD-слой: {mods}"
    report = (mods / "hd" / "import-report.txt").read_text(encoding="utf-8").splitlines()[0]
    runs = [(["--screen", sid, "--check", "--sprites"], "res://scenes/ui/UiScreen.tscn") for sid in HD_SCREENS]
    plain = godot_all(repo, runs)
    layered = godot_all(repo, [([*extra, "--mods", str(mods)], scene) for extra, scene in runs])
    compared = 0
    packed = sum(int(m) for _, out in layered for m in re.findall(r"^hd текстур_из_dds=(\d+)", out, re.M))
    for sid, (c1, o1), (c2, o2) in zip(HD_SCREENS, plain, layered):
        a = {m[1]: (m[2], int(m[3] or 0)) for m in SPRITE.finditer(o1)}
        b = {m[1]: (m[2], int(m[3] or 0)) for m in SPRITE.finditer(o2)}
        if c1 != 0 or c2 != 0 or not a or a.keys() != b.keys():
            return False, f"HD-слой, {sid}: коды {c1}/{c2}, спрайты {sorted(a)} / {sorted(b)}\n{o2.strip()[-2000:]}"
        for key, (place, opaque) in a.items():
            place2, opaque2 = b[key]
            if place != place2 or abs(opaque - opaque2) > max(20, opaque // 30):
                return False, f"HD-слой, {sid} {key}: {place}:{opaque} без слоя, {place2}:{opaque2} со слоем"
            compared += 1
    save, m, why = stand(repo, 21)
    if save is None:
        return True, f"HD-слой: {compared} спрайтов на {len(HD_SCREENS)} экранах совпали ({report}); карта — пропуск: {why}"
    own = m.field_stacks(STAND_PLAYER)[0]
    lines = []
    for extra in ([], ["--mods", str(mods)]):
        code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--clicks", f"select {own.id}", *extra],
                          "res://scenes/map/MapScreen.tscn")
        face = re.search(r"^panel select .* портрет=1 лицо=(\S+) кадр=(\S+) ", out, re.M)
        if code != 0 or face is None:
            return False, f"HD-слой, карта: код {code}, портрета нет\n{out.strip()[-2000:]}"
        lines.append((face[1], face[2]))
    if lines[0] != lines[1]:
        return False, f"HD-слой, портрет панели карты: без слоя {lines[0]}, со слоем {lines[1]}"
    dds = f"текстур из DDS {packed}" if packed else "DDS нет (слой не сжат)"
    return True, (f"HD-слой: {compared} спрайтов на {len(HD_SCREENS)} экранах — те же места и размеры со слоем ×2 ({report}; {dds}); "
                  f"портрет панели карты {lines[0][0]} — то же место {lines[0][1]}")


# Окна города (docs/ui-windows.md): шаги проверки сцены карты и что должна содержать строка каждого шага. Клетки групп —
# «x» воин, «b» большой, «.» пусто; позиции 0…5, чётные — передний ряд.
def city_cases(repo: Path) -> list[tuple[Path, str, list[tuple[str, list[str]]]]] | str:
    """Случаи окон на карте (пункт 6, задача 4.6; docs/ui-windows.md) — на сохранениях партии стенда (Э5): эталон шёл от живой партии
    корпуса с её золотом, сумками и составом гарнизона, здесь ожидания — по смыслу: какое окно открыто, что выбрано, есть ли рамки и
    карточка. Логику окон с числами держат CityWindowTests в dotnet test. Нет импорта — причина пропуска строкой."""
    saves = {}
    for day in (21, 30):
        save, m, why = stand(repo, day)
        if save is None:
            return why
        saves[day] = (save, m)
    s21, m21 = saves[21]
    s30, m30 = saves[30]
    hero = max(m21.field_stacks(STAND_PLAYER), key=lambda st: (st.items, st.id))   # с сумкой: «Бросить предмет»
    capital = m30.capital(STAND_PLAYER)
    empty = "%d,%d" % m21.empty_cell()
    return [
        # Столица: экран столицы, наведение на картинку мимо построек — прежняя панель; щелчок — окно построек; окно армии без гостя
        # с подсказкой найма героя (X005TA0159, CDDUnitNoGroup 0x573131), найм героя — окно имени, набранная буква, герой в столице.
        (s30, STAND_PLAYER, [
            ("panel click BTN_CAPITAL", ["панель=DLG_CAPITAL"]),
            ("cap 5,5", ["постройка=-", "пал=DLG_ISO_PAL"]),
            ("cap click", ["окна=DLG_BUILD_STRUCTURE "]),
            ("ui click 200,15", ["ветка=base:building-branch/mage", "вкладок=5"]),
            # Дерево построек данными (Э6): вкладка, зум колесом к курсору, тащение средней кнопкой, крестовина по значкам, A — выбор.
            ("ui click 30,15", ["ветка=base:building-branch/fighter"]),
            ("ui wheelup 200,300", ["зум=1.088"]),
            ("ui mdrag 200,300 -40,-30", ["зум=1.088", "вид=66.7,-69.6"]),   # место дерева — левее карточки
            ("ui pad DpadDown", ["выбрано=-"]),
            ("ui pad A", ["выбрано=base:building/"]),
            ("ui click BTN_CLOSE", ["окна=-"]),
            ("panel click BTN_RESEARCH_SPELL", ["окна=DLG_RESEARCH_SPELL "]),
            ("ui click BTN_CLOSE", ["окна=-"]),
            ("panel click BTN_BACK", ["панель=DLG_STRATEGIC"]),
            ("city capital", ["гость=0", "подсказка_найма=Щелкнитемышкойчт"]),
            ("ui click 265,216", ["найм=Leader"]),
            ("ui click BTN_HIRE_LEADER", ["DLG_HIRE_LEADER_2>DLG_GETINFO_BOX "]),
            ("ui type Z", ["DLG_GETINFO_BOX "]),
            ("ui click BTN_CLOSE", ["окна=DLG_CITY_STACK ", "гость=1"]),
            ("ui click BTN_EXIT_CITY", ["окна=-", "панель=DLG_STRATEGIC"]),
        ]),
        # Щелчок вне сердца столицы выбирает столицу (CTaskSelectCity 0x4d45a6), второй — экран столицы (0x4cf79d; замечания 8, 28).
        (s30, STAND_PLAYER, [
            (f"{capital.x + 2},{capital.y}", ["выбран=- ", f"укрепление={capital.id}", "столица=0", "окна=-"]),
            (f"{capital.x + 2},{capital.y}", ["столица=1", "окна=-"]),
        ]),
        # Окно отряда с портрета панели команд; «Бросить предмет» — панель DLG_DROP_BAG.
        (s21, STAND_PLAYER, [
            (f"select {hero.id}", [f"выделен={hero.id}"]),
            ("panel release IMG_LEADER", ["окна=DLG_MANAGE_STACK ", f"отряд={hero.id}"]),
            ("ui click TOG_PAPERDOLL", ["кукла="]),
            ("ui click BTN_PICK_UP_DROP", ["окна=-"]),
            (f"drop {empty}", ["окна=-", "панель=DLG_DROP_BAG"]),
        ]),
        # Курсор окон (CInterface::setCursor 0x5308aa из строки DIALOG): над окном — кинжал _CUDEFAUL разметки, окно закрылось — снова курсор
        # карты (решение пользователя 15.10).
        (s21, STAND_PLAYER, [
            (f"select {hero.id}", [f"выделен={hero.id}"]),
            ("wcursor", ["окна=0 ", "чей=карта"]),
            ("ui click BTN_OPTIONS", ["окна=LE_SETTINGS"]),
            ("wcursor", ["верх=LE_SETTINGS ", "курсор=ui:_CUDEFAUL ", "кадров=1 ", "точка=0,0 ", "чей=окно"]),
            ("ui click BTN_CLOSE", ["окна=-"]),
            ("wcursor", ["окна=0 ", "чей=карта"]),
            ("panel release IMG_LEADER", ["окна=DLG_MANAGE_STACK "]),
            ("wcursor", ["верх=DLG_MANAGE_STACK ", "курсор=ui:_CUDEFAUL ", "точка=0,0 "]),
            ("ui click BTN_CLOSE", ["окна=-"]),
            ("wcursor", ["окна=0 ", "чей=карта"]),
        ]),
        # Площадки выбранным отрядом (по виду — первая площадка вида на карте): торговец — кинжал окна, фон места группы, кукла; маг —
        # книга известных заклинаний; тренер — окно обучения. Колесо над картой — стрелки ↑/↓ (0x5626a3).
        (s21, STAND_PLAYER, [
            (f"select {hero.id}", [f"выделен={hero.id}"]),
            ("site merchant", ["окна=DLG_MERCHANT ", "курсор=ui:_CUDEFAUL ", "фон=DLG_MERCHANT_GROUP_BG@0,0:350x386:"]),
            ("ui click TOG_PAPERDOLL", ["кукла=1"]),
            ("ui click TOG_PAPERDOLL", ["кукла=0"]),
            ("ui click BTN_CLOSE", ["окна=-"]),
            ("site mage", ["окна=DLG_MAGE "]),
            ("ui click BTN_KNOWN_SPELL", ["окна=DLG_MAGE>DLG_CAST_SPELL "]),
            ("ui click BTN_CLOSE", ["окна=DLG_MAGE "]),
            ("ui click BTN_CLOSE", ["окна=-"]),
            ("wheel down", ["сдвиг=0,16 "]),
            ("wheel up", ["сдвиг=0,-16 "]),
            ("site trainer", ["окна=DLG_TRAINING_CAMP "]),
            ("ui click BTN_BACK", ["окна=-"]),
        ]),
        # Дипломатия — поле на месте карты и панель; карточка игрока DLG_R_C_PLAYER, пока держится правая кнопка (0x48522d → 0x577a0c); дань —
        # стрелка с автоповтором: нажатие без шага, 600 мс удержания — два шага, отпускание — ещё один; подсказка через 250 мс покоя.
        # «Задание», журнал, окно полученного (docs/ui-windows.md §14).
        (s30, STAND_PLAYER, [
            ("panel click BTN_DIPLOMACY", ["панель=DLG_DIPLOMACY"]),
            ("diplo 2", ["полоса_золота=1"]),
            ("panel field rpress IMG_RACE1", ["окна=DLG_R_C_PLAYER ", f"игрок={STAND_PLAYER} ", "Вы_за_него_играете!"]),
            ("panel field rrelease IMG_RACE1", ["окна=- "]),
            ("panel click BTN_GIVE_RESS", ["окна=DLG_GIVE_RESOURCE ", "дань=0"]),
            ("ui press BTN_UP", ["дань=0 "]),
            ("ui wait 600", ["дань=100 "]),
            ("ui release BTN_UP", ["дань=150 "]),
            ("ui move BTN_OK", ["подсказка=-"]),
            ("ui wait 300", ["подсказка=Подтвердить"]),
            ("ui move BTN_CANCEL", ["подсказка=Отмена"]),
            ("ui click BTN_CANCEL", ["окна=-"]),
            ("panel click BTN_BACK", ["панель=DLG_STRATEGIC"]),
            ("ui click BTN_REPORT", ["окна=DLG_SCENARIO_BRIEFING ", "задание="]),
            ("ui click BTN_CONTINUE", ["окна=-"]),
            ("ui click BTN_QUESTLOG", ["окна=DLG_QUEST_LOG ", "журнал="]),
            ("ui click BTN_CLOSE", ["окна=-"]),
            ("given empty", ["окна=DLG_MESSAGE_BOX ", "получено=ничего"]),
            ("ui click BTN_OK", ["окна=-"]),
            ("given spell", ["окна=DLG_ITEM ", "получено=заклинание", "библиотека=1 первое=1"]),
            ("ui click BTN_OK", ["окна=DLG_ITEM ", "первое=0"]),
            ("ui click BTN_OK", ["окна=-"]),
        ]),
        # Окно настроек Last Edict (Э5): вкладка, выбор (черновик), «Применить» — сохранено, «Закрыть». Итоги хода — флажок вкладки «Игра».
        (s21, STAND_PLAYER, [
            ("ui click BTN_OPTIONS", ["окна=LE_SETTINGS", "settings вкладка=Display изменено=0"]),
            ("ui tab Game", ["вкладка=Game"]),
            ("ui set turnSummary 1", ["изменено=1", "применить=1"]),
            ("ui apply", ["изменено=0", "применить=0", "показ_подтверждения=0"]),
            ("ui click BTN_CLOSE", ["окна=-"]),
        ]),
    ]


def mod_reload(repo: Path) -> tuple[bool, str]:
    """Горячая перезагрузка мода (Э6, Студия): мод собирается командами d2mod (постройка-каталог со строкой, лорды Кланов), игра с --watch
    открывает окно построек на ветке «здания», затем d2mod переносит постройку мода в другую клетку — игра перечитывает мод,
    перезапускает карту со снимка партии (тот же день) и снова открывает окно на той же ветке с постройкой мода на новом месте. Правка —
    после отметки игры «жду» (шаг reloadwait), а не по часам."""
    save, _m, why = stand(repo, 30)
    if save is None:
        return True, f"перезагрузка мода: пропуск — {why}"
    d2mod = dotnet_tool(repo, "d2mod")
    mod = repo / ".cache" / "ui-check" / f"mod-reload-{os.getpid()}-{threading.get_ident()}" / "towers"
    if mod.parent.exists():
        shutil.rmtree(mod.parent)
    base_lords = json.loads((repo / "data" / "content" / "base" / "lords.json").read_text(encoding="utf-8"))["lords"]
    clans = [l for l in base_lords if l["id"].startswith("base:lord/mountain-clans")]
    if not clans:
        return True, "перезагрузка мода: пропуск — в базе нет лордов Кланов"
    cmds = [["new", str(mod), "--id", "towers"],
            ["add", str(mod), "building", "spire", "--json",
             json.dumps({"level": 3, "category": "magic", "requires": "base:building/magic-tower-2", "cost": {"gold": 900}})],
            ["text", str(mod), "towers:building/spire", "ru", "name", "Шпиль"],
            ["file", str(mod), "towers:building/spire", "icon.png", str(mod.parent / "spire.png")]]
    cmds += [["set", str(mod), l["id"], "buildings", json.dumps(l["buildings"] + ["towers:building/spire"])] for l in clans]
    mod.parent.mkdir(parents=True, exist_ok=True)
    write_png(mod.parent / "spire.png", 154, 148)   # свой значок ×2 (HD)
    for c in cmds:
        r = subprocess.run([*d2mod, *c], capture_output=True, text=True, encoding="utf-8", errors="replace", cwd=repo)
        if r.returncode != 0:
            return False, f"перезагрузка мода: d2mod {c[0]} — код {r.returncode}\n{r.stdout}{r.stderr}"

    def touch() -> None:
        # Правка — когда игра дошла до шага reloadwait (отметка .studio/waiting): под нагрузкой пула старт игры долгий.
        mark, deadline = mod / ".studio" / "waiting", time.monotonic() + 120
        while not mark.exists() and time.monotonic() < deadline:
            time.sleep(0.2)
        time.sleep(0.5)
        subprocess.run([*d2mod, "set", str(mod), "towers:building/spire", "tree", '{"column":3,"row":2}'],
                       capture_output=True, text=True, encoding="utf-8", errors="replace", cwd=repo)
    threading.Thread(target=touch, daemon=True).start()
    code, out = godot(repo, ["--mods", str(mod), "--watch", "--state", str(save), "--check", "--player", STAND_PLAYER,
                             "--clicks", "panel click BTN_CAPITAL;cap click;ui click 700,15;reloadwait"], "res://scenes/map/MapScreen.tscn")
    before = next((l for l in out.splitlines() if l.startswith("ui click_700,15")), "")
    after = next((l for l in out.splitlines() if l.startswith("mapscreen ")), "")
    reload = next((l for l in out.splitlines() if l.startswith("reload revision=")), "")
    if code != 0 or "towers:building/spire@" not in before or "rules=1" not in reload:
        return False, f"перезагрузка мода: код {code}, до: {before[:300]}\nперезагрузка: {reload}\n{out[-2000:]}"
    wants = ["ревизия=1 ", "день=30 ", "окна=DLG_BUILD_STRUCTURE ", "ветка=base:building-branch/other ", "towers:building/spire@"]
    # Все значки открытой ветки нарисованы — и значок постройки мода (icon.png каталога, HD ×2).
    shown = len(re.findall(r"@\d+,\d+:", after.split(" значки=")[1].split(" ")[0])) if " значки=" in after else -1
    if f"значков_нарисовано={shown} " not in after + " ":
        return False, f"перезагрузка мода: нарисованы не все значки ветки ({shown})\n{after}"
    if missing := [w for w in wants if w not in after + " "]:
        return False, f"перезагрузка мода: после перезагрузки нет {missing}\n{after}"
    old = before.split("towers:building/spire@")[1].split(":")[0]
    new = after.split("towers:building/spire@")[1].split(":")[0]
    if old == new:
        return False, f"перезагрузка мода: постройка мода осталась на месте {old}"
    shutil.rmtree(mod.parent, ignore_errors=True)
    return True, f"перезагрузка мода: d2mod {len(cmds)} команд, постройка мода в окне @{old} → после правки @{new}, карта со снимка (день 30), окно и ветка — снова"


STUDIO_STEPS = [
    {"op": "lord", "id": "base:lord/mountain-clans-mage-lord"},
    {"op": "add-building", "name": "spire", "title": "Шпиль", "requires": "base:building/magic-tower-2"},
    {"op": "connect", "from": "towers:building/spire", "to": "base:building/magic-tower-2"},
    {"op": "drag", "building": "towers:building/spire", "column": 2, "row": 1},
    {"op": "undo"},
    {"op": "redo"},
    {"op": "building-select"},
    {"op": "building-form", "title": "Башня мага", "requires": "base:building/magic-tower-2", "gold": 900},
    {"op": "building-picture", "path": "{picture}"},
    {"op": "building-apply"},
    {"op": "undo"},
    {"op": "redo"},
    {"op": "building-rename", "id": "towers:building/bashnya-maga", "name": "mage-tower"},
    {"op": "building-form", "title": "Башня магов", "level": 5},
    {"op": "building-apply"},
    {"op": "unit-select", "id": "base:unit/squire-empire"},
    {"op": "unit-new", "title": "Алебардщик"},
    {"op": "unit-form", "hp": 150, "hireable": True},
    {"op": "unit-apply"},
    {"op": "unit-attack", "damage": 40},
    {"op": "unit-aura", "modifier": "base:modifier/holy-armor", "reach": "adjacent"},
    {"op": "unit-immune", "source": "fire"},
    {"op": "item-select", "id": "base:item/tome-of-air"},
    {"op": "item-new", "title": "Кольцо стража"},
    {"op": "item-form", "gold": 777, "equip": "base:modifier/holy-armor"},
    {"op": "item-apply"},
    {"op": "item-picture", "path": "{face}"},
    {"op": "lord-select", "id": "base:lord/mountain-clans-mage-lord"},
    {"op": "lord-new", "title": "Рунный лорд"},
    {"op": "lord-research", "spell": "base:spell/air-ward", "gold": 300},
    {"op": "unit-picture", "file": "face.png", "path": "{face}"},
    {"op": "undo"},
    {"op": "redo"},
    {"op": "content-open", "kind": "unit"},
    {"op": "content-select", "id": "base:unit/axe-thrower"},
    {"op": "content-set", "field": "hitPoints", "text": "200"},
    {"op": "content-set", "field": "hitPoints", "text": "много"},
    {"op": "arena", "scenario": "Mercantile"},
    {"op": "map-new", "id": "big", "size": 1024, "seats": 4},
    {"op": "map-stroke", "x0": 100, "y0": 100, "x1": 300, "y1": 180, "brush": "Water", "radius": 3},
    {"op": "map-undo"},
    {"op": "map-stroke", "x0": 10, "y0": 500, "x1": 1000, "y1": 520, "brush": "Mountain", "radius": 2},
    {"op": "map-place", "kind": "City", "x": 600, "y": 600, "units": ["base:unit/squire-empire"]},
    {"op": "map-place", "kind": "Ruin", "x": 620, "y": 600, "units": ["base:unit/archer"], "items": ["base:item/tome-of-air"]},
    {"op": "map-place", "kind": "Bag", "x": 610, "y": 620, "items": ["base:item/tome-of-air"]},
    {"op": "map-place", "kind": "Stack", "x": 605, "y": 615, "units": ["base:unit/squire-empire", "base:unit/archer"]},
    {"op": "map-place", "kind": "Bag", "x": 601, "y": 601},
    {"op": "map-select", "x": 605, "y": 615},
    {"op": "map-move", "x": 606, "y": 616},
    {"op": "map-player", "race": "base:race/undead-hordes"},
    {"op": "map-event", "name": "Привет", "condition": "Frequency", "effect": "Popup", "text": "Добро пожаловать"},
    {"op": "map-check"},
    {"op": "map-save"},
    {"op": "mod-version", "version": "0.3.0"},
    {"op": "mod-pack"},
    {"op": "save"},
]
# (шаг, что должно быть в строке шага)
STUDIO_WANTS = [
    ("lord", ["ok=1", "lord=base:lord/mountain-clans-mage-lord", "errors=0"]),
    ("add-building", ["ok=1", "dirty=1", "граф_узлов=25"]),
    ("connect", ["ok=0", "refusal=требования_по_кругу"]),
    ("drag", ["ok=1", "ветка=base:building-branch/other"]),
    ("undo", ["ok=1", "redo=1"]),
    ("redo", ["ok=1", "redo=0"]),
    ("building-select", ["ok=1", "форма: новая"]),
    ("building-form", ["ok=1", "имя=bashnya-maga", "требует=base:building/magic-tower-2", "уровень=3"]),
    ("building-picture", ["ok=1", "картинка=1"]),
    ("building-apply", ["ok=1", "граф_узлов=26", "форма: постройка=towers:building/bashnya-maga", "errors=0"]),
    ("undo", ["ok=1", "граф_узлов=25", "форма: новая"]),
    ("redo", ["ok=1", "граф_узлов=26"]),
    ("building-rename", ["ok=1", "форма: постройка=towers:building/mage-tower"]),
    ("building-form", ["ok=1", "уровень=5"]),
    ("building-apply", ["ok=1", "уровень=5", "errors=0"]),
    ("unit-select", ["ok=1", "units: selected=base:unit/squire-empire", "картинок=3"]),
    ("unit-new", ["ok=1", "selected=towers:unit/alebardshchik", "own=1", "errors=0"]),
    ("unit-form", ["ok=1", "форма_жизнь=150"]),
    ("unit-apply", ["ok=1", "errors=0"]),
    ("unit-attack", ["ok=1", "errors=0", "атака_эффект=base:effect/damage"]),
    ("unit-aura", ["ok=1", "errors=0", "ауры=1"]),
    ("unit-immune", ["ok=1", "errors=0"]),
    ("item-select", ["ok=1", "items: selected=base:item/tome-of-air"]),
    ("item-new", ["ok=1", "selected=towers:item/koltso-strazha", "own=1"]),
    ("item-form", ["ok=1"]),
    ("item-apply", ["ok=1", "errors=0"]),
    ("item-picture", ["ok=1", "значок=1", "errors=0"]),
    ("lord-select", ["ok=1", "lords: selected=base:lord/mountain-clans-mage-lord"]),
    ("lord-new", ["ok=1", "selected=towers:lord/runnyy-lord", "own=1"]),
    ("lord-research", ["ok=1", "errors=0"]),
    ("unit-picture", ["ok=1", "errors=0"]),
    ("undo", ["ok=1", "own=1"]),
    ("redo", ["ok=1", "own=1"]),
    ("content-open", ["ok=1", "kind=unit"]),
    ("content-select", ["ok=1", "selected=base:unit/axe-thrower"]),
    ("content-set", ["ok=1", "patched=1"]),
    ("content-set", ["ok=0", "не_integer"]),
    ("arena", ["ok=1", "arena=8"]),
    ("map-new", ["ok=1", "size=1024", "cells=1048576"]),
    ("map-stroke", ["ok=1", "undo=1"]),
    ("map-undo", ["ok=1", "undo=0", "redo=1"]),
    ("map-stroke", ["ok=1", "undo=1", "redo=0"]),
    ("map-place", ["ok=1", "undo=2"]),
    ("map-place", ["ok=1", "undo=3"]),
    ("map-place", ["ok=1", "undo=4"]),
    ("map-place", ["ok=1", "undo=5"]),
    ("map-place", ["ok=0", "undo=5"]),
    ("map-select", ["ok=1", "selected=S"]),
    ("map-move", ["ok=1", "undo=6"]),
    ("map-player", ["ok=1", "undo=7"]),
    ("map-event", ["ok=1", "undo=8", "событий=1"]),
    ("map-check", ["ok=1"]),
    ("map-save", ["ok=1", "dirty=0"]),
    ("mod-version", ["ok=1", "версия=0.3.0"]),
    ("mod-pack", ["ok=1"]),
    ("save", ["ok=1", "dirty=0"]),
]


def write_png(path: Path, w: int, h: int) -> None:
    """Свой PNG w×h (градиент RGBA) — картинка мода в проверках без графики игры."""
    import struct
    import zlib
    raw = b"".join(b"\x00" + b"".join(bytes([x * 255 // w, y * 255 // h, 120, 255]) for x in range(w)) for y in range(h))

    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))


def png_size(path: Path) -> tuple[int, int]:
    import struct
    head = path.read_bytes()[:24]
    return struct.unpack(">II", head[16:24]) if head[:8] == b"\x89PNG\r\n\x1a\n" else (0, 0)


def studio(repo: Path) -> tuple[bool, str]:
    """Студия (Э6) без окна: мод собирает d2mod, окно Студии проходит сценарий — лорд, новая постройка, связь по кругу (отказ), перенос
    значка на живой странице, отмена и повтор, правка поля по схеме (и отказ неверного числа), арена «база против мода», карта 1024²:
    новая, мазки, отмена, запись в мод; запись мода. Каждый шаг печатает строки пространств."""
    if not (repo / "data" / "content" / "base" / "lords.json").is_file():
        return True, "Студия: пропуск — нет импорта (data/content/base)"
    mod = repo / ".cache" / "ui-check" / f"studio-{os.getpid()}-{threading.get_ident()}" / "towers"
    if mod.parent.exists():
        shutil.rmtree(mod.parent)
    r = subprocess.run([*dotnet_tool(repo, "d2mod"), "new", str(mod), "--id", "towers"], capture_output=True, text=True,
                       encoding="utf-8", errors="replace", cwd=repo)
    if r.returncode != 0:
        return False, f"Студия: d2mod new — код {r.returncode}\n{r.stdout}{r.stderr}"
    script = mod.parent / "steps.json"
    picture = mod.parent / "picture.png"
    write_png(picture, 300, 300)   # своя картинка (не графика игры): Студия впишет её в значок 77×74 ×3
    face = mod.parent / "face.png"
    write_png(face, 140, 170)   # своё лицо ×2
    files = {"{picture}": str(picture), "{face}": str(face)}
    steps = [{k: files.get(v, v) if isinstance(v, str) else v for k, v in step.items()} for step in STUDIO_STEPS]
    script.write_text(json.dumps(steps, ensure_ascii=False), encoding="utf-8")
    code, out = godot(repo, ["--open", str(mod), "--check", "--script", str(script)], "res://scenes/studio/Studio.tscn")
    lines = [l for l in out.splitlines() if l.startswith("step ")]
    if (check := next((l for l in out.splitlines() if l.startswith("map-check ")), "")) and "нарушений=0" not in check:
        return False, f"Студия: карта с объектами — {check}"
    if code != 0 or len(lines) != len(STUDIO_WANTS):
        return False, f"Студия: код {code}, шагов {len(lines)} из {len(STUDIO_WANTS)}\n{out[-3000:]}"
    for (op, wants), line in zip(STUDIO_WANTS, lines):
        if not line.startswith(f"step {op} ") or (missing := [w for w in wants if w not in line]):
            return False, f"Студия: шаг «{op}» — нет {missing if line.startswith(f'step {op} ') else op}\n{line[:1500]}"
    if not (mod / "content" / "buildings" / "spire" / "entity.json").is_file() or not (mod / "data" / "scenarios" / "big.json").is_file():
        return False, "Студия: запись мода — нет каталога постройки или карты"
    unit = json.loads((mod / "content" / "units" / "alebardshchik" / "entity.json").read_text(encoding="utf-8"))
    attack = mod / "content" / "attacks" / "alebardshchik" / "entity.json"
    if unit.get("hitPoints") != 150 or unit.get("baseUnit") != "base:unit/squire-empire" or not attack.is_file() \
            or unit.get("auras") != [{"modifier": "base:modifier/holy-armor", "reach": "adjacent"}] \
            or unit.get("immunities") != [{"source": "fire", "level": "always"}] \
            or json.loads(attack.read_text(encoding="utf-8")).get("damage") != 40 \
            or png_size(mod / "content" / "units" / "alebardshchik" / "face.png") != (140, 170):
        return False, f"Студия: юнит формой — {unit}"
    item = json.loads((mod / "content" / "items" / "koltso-strazha" / "entity.json").read_text(encoding="utf-8"))
    if item.get("equipModifier") != "base:modifier/holy-armor" or item.get("value") != {"gold": 777} \
            or png_size(mod / "content" / "items" / "koltso-strazha" / "icon.png") != (140, 170):
        return False, f"Студия: предмет формой — {item}"
    # «Играть на карте»: игра открывает карту мода с объектами (1024², город, руина, мешок, отряд, событие).
    code, played = godot(repo, ["--mods", str(mod), "--scenario", "big", "--check"], "res://scenes/map/MapScreen.tscn")
    shown = next((l for l in played.splitlines() if l.startswith("mapscreen ")), "")
    if code != 0 or "ground_ready=True" not in shown:
        return False, f"Студия: игра на карте мода — код {code}\n{shown or played[-1500:]}"
    packed = next((l for l in out.splitlines() if l.startswith("mod-pack ")), "")
    archive = mod / ".studio" / "pack" / "towers-0.3.0.zip"
    if "упаковано:" not in packed or not archive.is_file():
        return False, f"Студия: упаковка — {packed or 'нет строки'}"
    import zipfile
    with zipfile.ZipFile(archive) as z:
        names = set(z.namelist())
        listed = {f["file"] for f in json.loads(z.read("manifest.json"))["files"]}
    if "mod.json" not in names or not listed <= names or any(n.startswith(".studio") for n in names):
        return False, f"Студия: архив мода — {sorted(names)[:10]}"
    icon = mod / "content" / "buildings" / "mage-tower" / "icon.png"
    if not icon.is_file() or (size := png_size(icon)) != (231, 222):
        return False, f"Студия: значок постройки формой — {icon} {'нет' if not icon.is_file() else size}, ждали 231×222 (77×74 ×3)"
    shutil.rmtree(mod.parent, ignore_errors=True)
    return True, (f"Студия: {len(lines)} шагов — дерево, постройка формой со значком (HD ×3), юнит на основе сквайра (своя атака, аура, иммунитет, лицо ×2), предмет со значком, лорд с исследованием, "
                  "содержимое, арена, "
                  "карта 1024² с объектами (город, руина, мешок, отряд; перенос; игрок и событие; нарушений 0), запись мода, игра на этой карте, архив мода с описью")


def mod_battle(repo: Path) -> tuple[bool, str]:
    """Бой с атакой и аурой мода (Студия 2, задача 3): d2mod собирает мод — своя атака «яд» и аура «на себя» у рыцаря на пегасе бой-образца;
    сцена боя с модом досчитывает бой: под аурой — 1 юнит, среди действий — яд мода; без мода — ни аур, ни яда (ваниль та же)."""
    if not (repo / "data" / "content" / "base" / "units.json").is_file():
        return True, "бой с атакой мода: пропуск — нет импорта"
    d2mod = dotnet_tool(repo, "d2mod")
    mod = repo / ".cache" / "ui-check" / f"mod-battle-{os.getpid()}-{threading.get_ident()}" / "towers"
    if mod.parent.exists():
        shutil.rmtree(mod.parent)
    cmds = [["new", str(mod), "--id", "towers"],
            ["add", str(mod), "attack", "venom", "--json", json.dumps({"initiative": 60, "source": "death", "effect": "base:effect/poison",
                                                                        "power": 100, "reach": "adjacent", "damage": 20, "infinite": True})],
            ["set", str(mod), "base:unit/pegasus-knight", "attack", '"towers:attack/venom"'],
            ["set", str(mod), "base:unit/pegasus-knight", "auras", '[{"modifier":"base:modifier/holy-armor","reach":"self"}]']]
    for c in cmds:
        r = subprocess.run([*d2mod, *c], capture_output=True, text=True, encoding="utf-8", errors="replace", cwd=repo)
        if r.returncode != 0:
            return False, f"бой с атакой мода: d2mod {c[0]} — код {r.returncode}\n{r.stdout}{r.stderr}"
    lines = {}
    for name, extra in (("мод", ["--mods", str(mod)]), ("база", [])):
        code, out = godot(repo, [*extra, "--check", "--play-out"], BATTLE_SCENE)
        line = next((l for l in out.splitlines() if l.startswith("battle played ")), "")
        if code != 0 or not line:
            return False, f"бой с атакой мода ({name}): код {code}\n{out[-2000:]}"
        lines[name] = line
    if "ауры=1 " not in lines["мод"] or "Poison" not in lines["мод"] or "ауры=0 " not in lines["база"] or "Poison" in lines["база"]:
        return False, f"бой с атакой мода: с модом — {lines['мод']}; без — {lines['база']}"
    shutil.rmtree(mod.parent, ignore_errors=True)
    return True, f"бой с атакой мода: {lines['мод'][len('battle played '):]} (без мода: {lines['база'][len('battle played '):]})"


def city_windows(repo: Path) -> tuple[bool, str]:
    """Окна города (пункт 6, задача 4.6): столица и её окно армии, перетаскивание, найм воина и героя, рост деревни.
    Каждый шаг печатает строку со стеком окон и клетками групп. Нет импорта — пропуск."""
    results = []
    cases = city_cases(repo)
    if isinstance(cases, str):
        return True, f"окна города: пропуск — {cases}"
    outs = godot_all(repo, [(["--state", str(path), "--check", "--player", player,
                              "--clicks", ";".join(step for step, _ in steps)], "res://scenes/map/MapScreen.tscn")
                            for path, player, steps in cases])
    for n, ((path, player, steps), (code, out)) in enumerate(zip(cases, outs)):
        save = f"{path.stem} №{n + 1}"
        lines = [l for l in out.splitlines() if l.startswith(("ui ", "panel ", "city ", "target ", "hover ", "cap ", "diplo ", "site ", "drop ", "pick ", "rod ", "given", "end день=", "click ", "wcursor ", "wheel ", "rename "))
                 and not l.startswith(("panel Open", "panel Goto"))]
        if code != 0 or len(lines) != len(steps):
            return False, f"окна города {save}: код {code}, строк {len(lines)} из {len(steps)}\n{out.strip()}"
        for (step, wants), line in zip(steps, lines):
            # «без_значка=» — карточка списка без картинки (видно одно число): так свиток без своего значка выглядел «призраком» в ячейке.
            for bad in ("нет_кадров=", "без_значка="):
                if bad in line:
                    return False, f"окна города {save}: шаг «{step}» — {line[line.index(bad):].split()[0]}"
            # «!строка» — такой строки быть не должно (ячейка опустела).
            missing = [w for w in wants if not w.startswith("!") and w not in line + " "]
            present = [w[1:] for w in wants if w.startswith("!") and w[1:] in line + " "]
            if missing or present:
                return False, f"окна города {save}: шаг «{step}» — нет {missing}, лишнее {present}\n{line}"
        # Окно полученного: TAKEBAG — раз на сообщение, за первым окном, и у пустого мешка (0x4d95f5, 0x4d9aa9; docs/ai-screen.md §11).
        given = sum(1 for step, _ in steps if step.startswith("given"))
        if out.count("fx Midgard/TAKEBAG ") != given:
            return False, f"окна города {save}: TAKEBAG {out.count('fx Midgard/TAKEBAG ')} раз, ожидалось {given}"
        results.append(f"{save}: {len(steps)} шагов" + (f", TAKEBAG {given}" if given else ""))
    return True, "окна города: " + ", ".join(results)


def siege(repo: Path) -> tuple[bool, str]:
    """Бой за руину (пункт 6): день 21 партии стенда, герой Кланов приходит к ближней охраняемой руине, бой с охраной на фоне RUIN
    доигрывается. Нет импорта — пропуск."""
    save, m, why = stand(repo, 21)
    if save is None:
        return True, f"бой за руину: пропуск — {why}"
    hero, ruin = ruin_raid(m)
    code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--clicks", f"arrive {hero.id} {ruin.id}"],
                      "res://scenes/map/MapScreen.tscn")
    start = re.search(rf"mapbattle start attacker={hero.id} defender={ruin.id} units=\d+ place=RUIN", out)
    end = MAPBATTLE.search(out)
    if code != 0 or start is None or end is None or end["winner"] == "-":
        return False, f"бой за руину: код {code}\n{out.strip()}"
    return True, f"бой за руину: {start.group(0)} → победитель {end['winner']}"


GAME = re.compile(r"game scenario=\S+ from=(?P<from>\w+) human=(?P<human>\S+) .*?day=(?P<day>\d+) gold=(?P<gold>\d+)")


def gameloop(repo: Path) -> tuple[bool, str]:
    """Игровой цикл (пункт 6): меню до брифинга → партия (день 1) → конец хода (день 2, доход) → настройки → запись →
    загрузка той же партии из настроек. Сохранения — во временный каталог (--saves). После конца хода ИИ может предложить союз
    (окно DLG_DIPLO_BOX) — «ui click BTN_CANCEL» отказывает, «ui click BTN_OK» закрывает
    реплику ИИ (DLG_CHAT_AI); без окон шаги ничего не делают."""
    saves = repo / ".cache" / "ui-check" / "saves"
    if saves.exists():
        for f in saves.iterdir():
            f.unlink()
    extra = ["--check", "--saves", str(saves), "--input", "; ".join(CONTENT_STEPS + ["click BTN_CONTINUE"]),
             "--clicks", "ok;end;ok;ui click BTN_CANCEL;ui click BTN_OK;ok;ui click BTN_OPTIONS;ui click BTN_SAVE;ui click BTN_SAVE;ui click BTN_CLOSE;"
                         "ui click BTN_LOAD;ui click BTN_LOAD"]
    code, out = godot(repo, extra, "res://scenes/ui/UiFlow.tscn")
    games = list(GAME.finditer(out))
    screen = MAPSCREEN.search(out)
    if (code != 0 or len(games) != 2 or screen is None or games[0]["from"] != "new" or games[1]["from"] != "save"
            or "load briefing" not in out or "resume message day=2 " not in out
            or games[0]["day"] != "1" or games[1]["day"] != "2" or int(games[1]["gold"]) <= int(games[0]["gold"])):
        return False, f"игровой цикл: код {code}\n{out.strip()}"
    return True, (f"игровой цикл: новая партия день 1, золото {games[0]['gold']} → конец хода → запись → загрузка через брифинг: "
                  f"день {games[1]['day']}, золото {games[1]['gold']}, окно «Повтор миссии с дня 2»")


def autosave(repo: Path) -> tuple[bool, str]:
    """Автосохранение и окно загрузки в партии. Новая партия из меню, «Конец хода» — при флажке «Автосохранение» (по умолчанию включён)
    архив «<сценарий> T0001» в каталоге сохранений (--saves; имя как у обёртки DisciplesGL-HD); настройки → «Загрузить» → Esc и «Назад»
    возвращают к настройкам (пометка 4 прогона 14.10: не возвращали — у DLG_LOAD_GAME не было перехода BTN_CLOSE); снова «Загрузить» — в
    списке автосохранение, «Загрузить» поднимает его тем же днём. Затем — чистка (autosave_archives). С выключенным флажком (--settings) —
    конец хода без файла."""
    steps = "; ".join(CONTENT_STEPS + ["click BTN_CONTINUE"])
    saves = repo / ".cache" / "ui-check" / "autosave"
    saves.mkdir(parents=True, exist_ok=True)
    for f in saves.iterdir():
        f.unlink()
    code, out = godot(repo, ["--check", "--saves", str(saves), "--input", steps, "--clicks",
                             "ok;end;ok;ui click BTN_CANCEL;ui click BTN_OK;ok;ui click BTN_OPTIONS;ui click BTN_LOAD;ui key 27;"
                             "ui click BTN_LOAD;ui click BTN_CLOSE;ui click BTN_LOAD;ui click BTN_LOAD"], "res://scenes/ui/UiFlow.tscn")
    files = [f.name for f in saves.glob("*.d2save")]
    games = list(GAME.finditer(out))
    back = [l for l in out.splitlines() if l.startswith(("ui key_27 ", "ui click_BTN_CLOSE "))]
    if (code != 0 or len(files) != 1 or not files[0].endswith(" T0001.d2save") or "autosave " not in out
            or len(back) != 2 or any(" on=DLG_LOAD_GAME " not in l or " окна=LE_SETTINGS" not in l + " " for l in back)
            or len(games) != 2 or games[1]["from"] != "save" or games[1]["day"] != "1"):
        return False, f"автосохранение: код {code}, файлы {files}\n" + "\n".join(back) + f"\n{out.strip()}"
    ok, archives = autosave_archives(repo, saves, files[0][:-len(" T0001.d2save")], steps)
    if not ok:
        return False, archives
    settings = repo / ".cache" / "ui-check" / "autosave-off.json"
    settings.write_text(json.dumps({"AutoSave": False}), encoding="utf-8")
    for f in saves.iterdir():
        f.unlink()
    code, out = godot(repo, ["--check", "--saves", str(saves), "--settings", str(settings), "--input", steps, "--clicks", "ok;end"],
                      "res://scenes/ui/UiFlow.tscn")
    left = [f.name for f in saves.iterdir()]
    # Конец хода состоялся (ход ИИ дня 1 стоит на окне предложения союза — день не важен), автосохранения нет.
    if code != 0 or left or "autosave " in out or "end день=" not in out:
        return False, f"автосохранение выключено: код {code}, файлы {left}\n{out.strip()}"
    return True, (f"автосохранение: конец хода — «{files[0]}», Esc и «Назад» окна загрузки — к настройкам, загрузка — день 1; "
                  f"{archives}; флажок выключен — файла нет")


def autosave_archives(repo: Path, saves: Path, scenario: str, steps: str) -> tuple[bool, str]:
    """Чистка архивов автосохранения (TrimAutoSaveArchives обёртки): в каталоге 15 старых архивов сценария (T0100…T0114, по времени
    записи по порядку) и ручные сохранения с похожими именами; два конца хода дописывают T0001 и T0002 — архивов снова 15, ушли два
    самых старых (T0100, T0101), ручные — на месте."""
    source = saves / f"{scenario} T0001.d2save"
    body = source.read_bytes()
    for f in saves.iterdir():
        f.unlink()
    old = time.time() - 86400
    for i in range(15):
        path = saves / f"{scenario} T{100 + i:04d}.d2save"
        path.write_bytes(body)
        os.utime(path, (old + i * 60, old + i * 60))
    manual = [saves / f"{scenario} T0100 копия.d2save", saves / f"{scenario} — день 1.d2save", saves / "AutoSave T0001.d2save"]
    for path in manual:
        path.write_bytes(body)
        os.utime(path, (old - 3600, old - 3600))
    # Окно, если ИИ что-то предложит в ход дня 1, закрывается отказом — иначе второй конец хода не наступит.
    code, out = godot(repo, ["--check", "--saves", str(saves), "--input", steps, "--clicks",
                             "ok;end;ui click BTN_CANCEL;ui click BTN_OK;ok;end"], "res://scenes/ui/UiFlow.tscn")
    pattern = re.compile(rf"^{re.escape(scenario)} T(\d{{4}})\.d2save$", re.I)
    turns = sorted(int(m[1]) for f in saves.iterdir() if (m := pattern.match(f.name)))
    counts = re.findall(r"^autosave \S+ день=\d+ архивов=(\d+)", out, re.M)
    want = [1, 2] + list(range(102, 115))
    if code != 0 or turns != want or counts != ["15", "15"] or not all(p.exists() for p in manual):
        return False, (f"архивы автосохранения: код {code}, ходы {turns}, архивов {counts}, "
                       f"ручные {[p.name for p in manual if p.exists()]}\n{out.strip()}")
    return True, (f"чистка архивов: 15 старых + 2 конца хода → {len(turns)} (ушли T0100, T0101), "
                  f"ручные «{manual[0].name}», «{manual[1].name}», «{manual[2].name}» на месте")


def quicksave(repo: Path) -> tuple[bool, str]:
    """Быстрое сохранение (Q на карте, QuickSave): новая партия из меню; Q под окном задания (F1) — записи нет; Q дважды на открытой
    карте — один файл QuickSave.d2save (второе перезаписывает), в чате карты «Игра записана под именем QuickSave»; настройки →
    «Загрузить» → «Загрузить» — партия из него."""
    saves = repo / ".cache" / "ui-check" / "quicksave"
    saves.mkdir(parents=True, exist_ok=True)
    for f in saves.iterdir():
        f.unlink()
    steps = "; ".join(CONTENT_STEPS + ["click BTN_CONTINUE"])
    code, out = godot(repo, ["--check", "--saves", str(saves), "--input", steps, "--clicks",
                             "ok;ui click BTN_REPORT;panel key 81;ui key 27;panel key 81;panel key 81;"
                             "ui click BTN_OPTIONS;ui click BTN_LOAD;ui click BTN_LOAD"],
                      "res://scenes/ui/UiFlow.tscn")
    files = [f.name for f in saves.iterdir()]
    lines = re.findall(r"^quicksave (\S+) день=(\d+) чат=(\S*)", out, re.M)
    games = list(GAME.finditer(out))
    if (code != 0 or files != ["QuickSave.d2save"] or len(lines) != 2
            or any(name != "QuickSave.d2save" or not chat.endswith("Игра_записана_под_именем_QuickSave") for name, _, chat in lines)
            or len(games) != 2 or games[1]["from"] != "save" or games[1]["day"] != lines[-1][1]):
        return False, f"быстрое сохранение: код {code}, файлы {files}, строки {lines}\n{out.strip()}"
    return True, (f"быстрое сохранение: Q под окном — нет записи, Q ×2 на карте — один файл {files[0]}, в чате «Игра записана под именем QuickSave», "
                  f"загрузка — день {games[1]['day']}")


def map_arrows(repo: Path) -> tuple[bool, str]:
    """Прокрутка карты стрелкой (MapView.Scroll): на открытой карте вид едет, под окном поверх карты (настройки) — стоит,
    как у движка. Партия своя (меню → брифинг), корпус не нужен."""
    extra = ["--check", "--input", "; ".join(CONTENT_STEPS + ["click BTN_CONTINUE"]),
             "--clicks", "ok;arrows;ui click BTN_OPTIONS;arrows"]
    code, out = godot(repo, extra, "res://scenes/ui/UiFlow.tscn")
    rows = re.findall(r"^arrows сдвиг=(-?\d+) окна=(\S+)", out, re.M)
    if (code != 0 or len(rows) != 2 or int(rows[0][0]) <= 0 or rows[0][1] != "-"
            or rows[1][0] != "0" or rows[1][1] == "-"):
        return False, f"прокрутка стрелкой: код {code}, {rows}\n" + "\n".join(l for l in out.splitlines() if l.startswith("arrows"))
    return True, f"прокрутка стрелкой: карта — сдвиг {rows[0][0]} точек, под окном {rows[1][1]} — стоит"


# Предложение союза ИИ S092PL0003 (Greenskins) — в ход ИИ после третьего конца хода: с потоком ИИ от зерна партии (Э4,
# docs/own-rules.md §2) броски стратегии другие, чем у общего генератора оригинала (там — день 1). OFFER_DAYS — две строки
# «end» до него.
OFFER_CLICKS = "ok;end;end;end"
OFFER_DAYS = [["end день=2 окна=- "], ["end день=3 окна=- "]]
OFFER_DAY = 3


def command_journal(repo: Path) -> tuple[bool, str]:
    """Журнал команд живой партии (docs/game-commands.md §3): сцена с D2_COMMAND_JOURNAL пишет сохранение начала и команды, tools/JournalReplay
    повторяет партию без сцены и сверяет хеш после каждой команды. Две партии: «Drega Zul» — конец хода с ходом ИИ, ход в зону события,
    попап и бой события на экране (действия боя — в журнале); новая партия из меню — ИИ предлагает союз в свой ход, «Принять» — команда,
    поданная, пока ход ИИ ждёт (решение снаружи). Корпус не нужен."""
    out_dir = repo / ".cache" / "ui-check" / "journal"
    shutil.rmtree(out_dir, ignore_errors=True)
    out_dir.mkdir(parents=True, exist_ok=True)
    battle, offer = out_dir / "event-battle.jsonl", out_dir / "alliance.jsonl"
    runs = [(["--check", "--scenario", "Drega Zul", "--player", "S001PL000a", "--humans", "S001PL000a",
              "--clicks", "end;ok;16,26;21,32;21,32;ok"], "res://scenes/map/MapScreen.tscn", {"D2_COMMAND_JOURNAL": str(battle)}),
            (["--check", "--input", "; ".join(CONTENT_STEPS + ["click BTN_CONTINUE"]),
              "--clicks", f"{OFFER_CLICKS};ui click BTN_OK;ui click BTN_OK"], "res://scenes/ui/UiFlow.tscn", {"D2_COMMAND_JOURNAL": str(offer)})]
    outs = godot_all(repo, runs)
    found = []
    for (code, out), journal, want in zip(outs, (battle, offer), ("endBattle", "answerAlliance")):
        if code != 0 or not journal.exists():
            return False, f"журнал команд: {journal.name} не записан, код {code}\n{out.strip()[-3000:]}"
        lines = [json.loads(l) for l in journal.read_text(encoding="utf-8").splitlines()[1:]]
        commands = [e["Command"]["$cmd"] for e in lines]
        nested = [a["Command"]["$cmd"] for e in lines for a in e.get("Answers", []) if a["$ans"] == "command"]
        played = any(e["Command"]["$cmd"] == "endBattle" and e["Command"].get("Play", {}).get("Actions") for e in lines)
        outside = sum(1 for e in lines if e.get("Outside"))
        if want == "endBattle" and not played or want == "answerAlliance" and want not in nested or outside:
            return False, f"журнал команд: {journal.name} — команды {commands}, вложенные {nested}, бой с действиями {played}, мимо команд {outside}"
        replay = subprocess.run([*dotnet_tool(repo, "JournalReplay"), str(journal)], capture_output=True, text=True, encoding="utf-8")
        if replay.returncode != 0 or not replay.stdout.startswith("повтор сошёлся"):
            return False, f"журнал команд: {journal.name} — {replay.stdout.strip()} {replay.stderr.strip()}"
        found.append(f"{journal.stem}: {replay.stdout.split(';')[0].removeprefix('повтор сошёлся: ')}")
    return True, "журнал команд: повтор сошёлся — " + "; ".join(found)


def alliance_offer(repo: Path) -> tuple[bool, str]:
    """Предложение союза от ИИ в сцене (docs/ui-windows.md §6): новая партия порта из меню, два конца хода (OFFER_CLICKS) — ИИ
    предлагает союз, окно DLG_DIPLO_BOX сразу, в ход ИИ (как у движка, CEffectProposeAlliance 0x43b5c8; ход ИИ ждёт ответа);
    «Принять» — реплика ИИ DLG_CHAT_AI, «ОК» закрывает; «Отказать» — тоже реплика. Партия своя (меню → брифинг), корпус не нужен."""
    results = []
    for button, word in (("BTN_OK", "принято"), ("BTN_CANCEL", "отказ")):
        extra = ["--check", "--input", "; ".join(CONTENT_STEPS + ["click BTN_CONTINUE"]),
                 "--clicks", f"{OFFER_CLICKS};ui click {button};ui click BTN_OK"]
        code, out = godot(repo, extra, "res://scenes/ui/UiFlow.tscn")
        lines = [l for l in out.splitlines() if l.startswith(("end ", "ui "))]
        wants = OFFER_DAYS + [[f"end день={OFFER_DAY} окна=DLG_DIPLO_BOX ", "предлагает="], ["окна=DLG_CHAT_AI ", "реплика="],
                              ["окна=- "]]
        if code != 0 or len(lines) != len(wants) or any(w not in line + " " for want, line in zip(wants, lines) for w in want):
            return False, f"предложение союза ({word}): код {code}\n" + "\n".join(lines or [out.strip()])
        # Сообщения ИИ человеку в ход ИИ дня 1 (ненависть, CCmdChatMsg → DLG_CHAT_AI 0x4b9747): каждое — окно, ход ждёт «Закрыть» (Ин36).
        said = len(re.findall(r"^aichat ok реплика=\S", out, re.M))
        if said < 1 or said != len(re.findall(r"^notice AiChat for=", out, re.M)) - 1:
            return False, f"предложение союза ({word}): сообщений ИИ в ход ИИ {said}, окон " + str(len(re.findall(r"^notice AiChat", out, re.M)))
        results.append(f"{word} (окон сообщений ИИ в ход ИИ {said})")
    ok, message = alliance_offer_loaded(repo)
    if not ok:
        return False, message
    return True, "предложение союза: " + ", ".join(results) + " — окно в ход ИИ, реплика ИИ, закрыто; " + message


def alliance_offer_loaded(repo: Path) -> tuple[bool, str]:
    """Ждущее предложение после загрузки: партия порта с отказом записывается, в сохранение дописывается предложение того же
    ИИ (сохранить при открытом окне нельзя), загрузка из настроек — окно DLG_DIPLO_BOX поверх загруженной партии."""
    saves = repo / ".cache" / "ui-check" / "offer-saves"
    saves.mkdir(parents=True, exist_ok=True)
    for f in saves.iterdir():
        f.unlink()
    steps = "; ".join(CONTENT_STEPS + ["click BTN_CONTINUE"])
    # Одно сохранение — записанное вручную: автосохранение концов хода (по умолчанию включено) здесь выключено.
    settings = repo / ".cache" / "ui-check" / "offer-settings.json"   # свой файл: шаг «автосохранение» может идти параллельно
    settings.write_text(json.dumps({"AutoSave": False}), encoding="utf-8")
    code, out = godot(repo, ["--check", "--saves", str(saves), "--settings", str(settings), "--input", steps, "--clicks",
                             f"{OFFER_CLICKS};ui click BTN_CANCEL;ui click BTN_OK;ok;ui click BTN_OPTIONS;ui click BTN_SAVE;ui click BTN_SAVE"],
                      "res://scenes/ui/UiFlow.tscn")
    offer = re.search(r"предлагает=(\S+) кому=(\S+)", out)
    files = list(saves.glob("*.d2save"))
    if code != 0 or offer is None or len(files) != 1:
        return False, f"предложение после загрузки: запись — код {code}, файлов {len(files)}\n{out.strip()}"
    save = json.loads(files[0].read_text(encoding="utf-8"))
    save["Offers"] = [{"From": offer[1], "To": offer[2], "Day": save["Day"]}]
    files[0].write_text(json.dumps(save, ensure_ascii=False), encoding="utf-8")
    code, out = godot(repo, ["--check", "--saves", str(saves), "--input", steps, "--clicks",
                             "ok;ui click BTN_OPTIONS;ui click BTN_LOAD;ui click BTN_LOAD"], "res://scenes/ui/UiFlow.tscn")
    shown = re.search(rf"offers shown 1 from={re.escape(offer[1])} окна=DLG_DIPLO_BOX", out)
    if code != 0 or shown is None or "from=save" not in out:
        return False, f"предложение после загрузки: код {code}\n{out.strip()}"
    return True, f"после загрузки — окно предложения {offer[1]}"


BATTLE = re.compile(r"battle units=(?P<units>\d+) sprites=(?P<sprites>\d+) missing=(?P<missing>\d+) "
                    r"round=(?P<round>\d+) turns=(?P<turns>\d+) card=(?P<card>\d) winner=(?P<winner>\S+)")


def battle(repo: Path) -> tuple[bool, str]:
    """Экран боя (сцена battle/BattleScreen) на бое-образце (BATTLE_SAMPLE): поле собрано, имён без картинок нет,
    бой доигрывается до победителя. Правила берутся из data/rules, картинки — из assets/gfx."""
    # Варианты сцены независимы — разом (godot_all), проверки — по порядку.
    variants = [["--check"], ["--check", "--item"], ["--check", "--marks"], ["--check", "--hover"], ["--check", "--refusal"],
                ["--check", "--cursor"], ["--check", "--cursor", "--still", "--speed", "4"], ["--check", "--resolve"],
                ["--check", "--d1"]]
    outs = dict(zip(map(str, variants), godot_all(repo, [(v, "res://scenes/battle/BattleScreen.tscn") for v in variants])))
    code, out = outs[str(["--check"])]
    match = BATTLE.search(out)
    if (code != 0 or match is None or int(match["sprites"]) == 0 or int(match["missing"]) != 0
            or match["winner"] == "-"):
        return False, f"экран боя: код {code}\n{out.strip()}"
    # Поле во вьюпорте экрана боя — точками: 1 = Nearest у корня BattleView (фильтр SubViewport по умолчанию — Linear).
    if re.search(r"^battle units=.* фильтр=1 ", out, re.M) is None:
        return False, f"экран боя: фильтр поля не Nearest\n{match.group(0)}"
    # Боевой предмет перетаскиванием (docs/ui-windows.md §7): зелье лечения мимо цели — не применено, на раненого своего — применено.
    code, out = outs[str(["--check", "--item"])]
    drops = re.findall(r"battle item drop 0 на=\S+ применён=(\d)", out)
    frames = re.findall(r"battle item drag 0 \S+ целей=\d+ рамка=(\w+) нет_кадров=(\d+)", out)
    hover = re.search(r"battle item hover \S+ курсор=Battle:CUATTACKA?\+\S+:(\d+)", out)   # «A» — при BattleAnim (DragCursor)
    # Анимация применения (BatItems.ff, по категории: зелье — G000IG1111, 0x682eb0).
    if (code != 0 or drops != ["0", "1"] or frames != [("ISEL", "0")] * 2 or "battle item card " not in out
            or hover is None or int(hover[1]) < 2 or "battle item show G000IG1111HMOVA1B00 " not in out):
        return False, f"экран боя, предмет: код {code}, броски {drops}\n{out.strip()}"
    # Панель юнитов (docs/ui-windows.md §9): полоса — «сейчас/макс» текстом; значки состояний — усиление, паралич, яд, защита;
    # паралич красит портрет серым.
    code, out = outs[str(["--check", "--marks"])]
    meters = re.search(r"battle panel полосы=(\d+/\d+(?:,\d+/\d+)*) ", out)
    marks = re.search(r"battle panel marks значков=(\d+) подкраска=(\S+)", out)
    # Рамка ходящего — в панели (PLY), метка под курсором на поле (MRK), надписи удара — на портрете («-N»).
    frames = re.search(r"рамки=(\S*)", out)
    hovered = re.search(r"battle hover меток=(\d+)", out)
    notes = re.search(r"battle panel end .* надписи=(\S*)", out)
    both = re.search(r"battle both groups меток=(\d+) целей=(\d+)", out)
    if (code != 0 or meters is None or marks is None or marks[1] != "4" or marks[2] != "128;128;128"
            or frames is None or not frames[1].startswith("PLY") or hovered is None or hovered[1] != "1"
            or notes is None or not re.match(r"[-+]\d+", notes[1]) or both is None or both[1] != both[2]):
        return False, f"экран боя, панель: код {code}\n{out.strip()}"
    # Наведение (слова пользователя 15.10): зона мыши — место и 32 px под ним (0x6326b9), метка ловится и под ногами; пока мышь ходит
    # внутри зоны одного юнита, анимация метки не начинается заново (0x633012), у другого юнита — с первого кадра.
    code, out = outs[str(["--check", "--hover"])]
    zone = re.search(r"battle hoverzone юнит=(?P<unit>\S+) .* под_ногами\+16=(?P<below>\S+) край\+31=(?P<edge>\S+) "
                     r"за_краем\+32=(?P<past>\S+) у_ног-4=(?P<feet>\S+) тело-60=(?P<body>\S+) ", out)
    anim = re.search(r"battle hoveranim кадров=(\d+) кадры=\S+ сбросов=(\d+) другой=(\S+):(-?\d+) мимо_меток=(\d+)", out)
    if (code != 0 or zone is None or {zone["below"], zone["edge"], zone["feet"], zone["body"]} != {zone["unit"]}
            or zone["past"] == zone["unit"] or anim is None or int(anim[1]) < 2 or anim[2] != "0"
            or anim[3] in ("-", zone["unit"]) or anim[4] != "0" or anim[5] != "0"
            # Перекрытие мест 2 и 3 левой группы: движок обходит юнитов в порядке позиций 1, 0, 3, 2, 5, 4 (0x63eed0) — ловится позиция 1.
            or re.search(r"battle hoveroverlap .* ловится=поз1\b", out) is None):
        return False, f"экран боя, наведение: код {code}\n{out.strip()}"
    # Щелчок по негодной цели (0x63cce6): повторное воскрешение — окно X008TA0031, «ОК» закрывает.
    code, out = outs[str(["--check", "--refusal"])]
    # Ин114: окно — по павшему своей стороны (0x63cce6), по павшему врагу — нет.
    if (code != 0 or "battle message X008TA0031" not in out or "battle refusal окно=DLG_MESSAGE_BOX" not in out
            or "battle refusal враг окно=-" not in out
            or "battle refusal closed окно=-" not in out):
        return False, f"экран боя, отказ цели: код {code}\n{out.strip()}"
    # Курсор цели (0x633a1f): над годной целью — пара класса атаки, над своим — «нельзя», вне мест — CUDEFAULT.
    code, out = outs[str(["--check", "--cursor"])]
    cursor = re.search(r"battle cursor цель=Battle:(CU[A-Z]+?)A? свой=Battle:(CU[A-Z]+?)A? вне=Battle:(CU[A-Z]+?)A? ", out)
    if (code != 0 or cursor is None or cursor[1] not in ("CUATTACK", "CUHEAL", "CUTRANSF") or cursor[2] != cursor[1] + "N"
            or cursor[3] != "CUDEFAULT" or re.search(r"battle cursor .* кадров=0\b", out)):
        return False, f"экран боя, курсор цели: код {code}\n{out.strip()}"
    # «Дополнительная анимация в бою» выключена и скорость «Сразу» (MapSettings): курсоры без «A», покой STIL — картинок хватает.
    code, out = outs[str(["--check", "--cursor", "--still", "--speed", "4"])]
    still = re.search(r"battle cursor цель=Battle:(CU[A-Z]+) свой=Battle:(CU[A-Z]+) вне=Battle:CUDEFAULT ", out)
    done = BATTLE.search(out)
    if code != 0 or still is None or still[1].endswith("A") or done is None or done["missing"] != "0":
        return False, f"экран боя, без анимации: код {code}\n{out.strip()}"
    # «Быстрый поединок» (docs/battle-screen.md §5.2): вопрос X005TA0922, «Да» досчитывает бой (бой двух людей вырезан, Р33).
    code, out = outs[str(["--check", "--resolve"])]
    if (code != 0 or "battle message X005TA0922" not in out or "battle resolve окно=вопрос" not in out
            or "battle resolve closed окно=- конец=1" not in out):
        return False, f"экран боя, быстрый поединок: код {code}\n{out.strip()}"
    # Интерфейс боя (прогон 11.10, пункты 14–30): у подложек разметки подсказок нет (движок снимает картинки с диалога, 0x62e017), у кнопки —
    # есть; подпись крупного портрета — две строки (имя MedBold, «ОЗ» Small); предметы — только у ходящего лидера (0x649775); портрет панели —
    # цель (основная панель, своя после TOG_RIGHTUNITS, своя над SPOT); окно сообщения 337×367 — по середине 800×600 на слое окон над
    # портретами; место боя — фон, тень и передний план (UNDEAD_0_FG на 2000, NEUTRAL_0_SHADOW на 138).
    code, out = outs[str(["--check", "--d1"])]
    d1 = re.search(r"battle d1 подсказки=(?P<tips>\S+) подпись=(?P<caption>\S+) имена=(?P<names>\S+) предметы_не_лидер=(?P<foreign>-?\d+) "
                   r"предметы_лидер=(?P<own>\d+) выпито=(?P<drunk>\S+) панель_цель=(?P<target>\S+) панель_свой=(?P<self>\S+) spot=(?P<spot>\S+) "
                   r"окно=(?P<window>\S+ слой=\d+) закрыто=(?P<closed>\d) место=(?P<place>\S+) панели=(?P<panels>\S+) "
                   r"вторая=(?P<second>\S+) предметы_ИИ=(?P<ai>-?\d+) лекарь=(?P<healer>\S+) ждать=(?P<wait>\S+)", out)

    def picked(pair: str) -> bool:
        return pair.count("=") == 1 and pair.split("=")[0] == pair.split("=")[1] != "-"

    tips = "IMG_LITEMSBG:-,IMG_RITEMSBG:-,IMG_BIGFACESBG:-,IMG_LUNITGROUPBG:-,IMG_RUNITGROUPBG:-,TOG_RIGHTUNITS:X005TA0754"
    if (code != 0 or d1 is None or d1["tips"] != tips
            # Имя в подписи — CMidUnit::getName (0x5e97dc): без хозяина (бой из журнала) у всех имя типа, пустых нет (прогон 14.10, пометка 2).
            or not re.match(r"[^|]+\|__ОЗ:_\d+/\d+_y=\d+_высоты=\d+\+\d+$", d1["caption"])
            or d1["caption"].split("|")[0] not in [n.split(":", 1)[1] for n in d1["names"].split(",")]
            or any(n.endswith(":-") for n in d1["names"].split(","))
            or d1["foreign"] != "0" or d1["own"] != "101"
            # Пометка 4 прогона 15.10: выпитое зелье из слота уходит сразу (0x649939 → 0x622fee): значков 2 → 1, мышь ловит только второй.
            or d1["drunk"] != "2>1:01"
            or not (picked(d1["target"]) and picked(d1["self"]) and picked(d1["spot"]))
            or d1["window"] != "231,116 слой=1" or d1["closed"] != "1"
            or not all(p in d1["place"].split(",") for p in ("UNDEAD_0_BG:10", "UNDEAD_0_FG:2000", "NEUTRAL_0_SHADOW:138"))
            # Ответы пользователя 11.10: обе панели сразу (группы по разные стороны, портрет второй — цель), у лидера ИИ на его ходу
            # предметов нет, у лекаря после победы нет «Защищаться»/«Ждать»/«Отступить» — только лечение раненого, «Ждать» — ход в конце раунда.
            or d1["panels"] not in ("LR", "RL") or not picked(d1["second"]) or d1["ai"] != "0"
            or d1["healer"] != "BTN_DEFEND:0,BTN_WAIT:0,BTN_RETREAT:0,лечит=F"
            or not re.match(r"\S+:ходов=[1-9]\d*:последний=1:повтор=0$", d1["wait"])):
        return False, f"экран боя, интерфейс (пункты 14–30): код {code}\n{out.strip()}"
    return True, match.group(0) + (f"; предмет: мимо — нет, на цель — применён; панель: полосы {meters[1]}, значков {marks[1]}; "
                                   "наведение — и под ногами (+32), анимация метки без сброса; отказ цели — X008TA0031; быстрый поединок — вопрос/запрет двум людям; автобой по сторонам; "
                                   "у подложек подсказок нет, предметы — у ходящего лидера-человека (у ИИ нет), цель портретом обеих панелей, "
                                   "окно по центру над панелью, передний план места; лекарь после победы — только лечение; ждущий ходит в конце раунда")


QUEUE_TURN = re.compile(r"^battle queue ход=\d+ раунд=\d+ ходит=\S+ модель=(?P<model>\S+) полоса=(?P<strip>\S+) совпало=(?P<ok>\d)$", re.M)
QUEUE_SUM = re.compile(r"battle queue итог ходов=(?P<turns>\d+) сверок=(?P<checks>\d+) совпало=(?P<same>\d+) новых_раундов=(?P<rounds>\d+) "
                       r"ждал=(?P<waiters>\S+) пометка_ждёт=(?P<waits>\S+) пропуск=(?P<skip>\S+) портретов_макс=(?P<most>\d+) вне=(?P<out>\d+) "
                       r"рамка=(?P<x0>\d+),(?P<y0>\d+),(?P<x1>\d+),(?P<y1>\d+) элементов_в_полосе=(?P<elements>\d+) зона_мыши_верх=(?P<hit>\d+) "
                       r"событий=(?P<events>\d+) пересборок=(?P<rebuilds>\d+) наведение_поле=(?P<field>\S+) наведение_полоса=(?P<strip>\S+) "
                       r"полосок_под_рамкой=(?P<covered>\d+) отражение=(?P<mirror_same>\d+)/(?P<mirror_seen>\d+) отражённых=(?P<mirrored>\d+) ")


def battle_queue(repo: Path) -> tuple[bool, str]:
    """Полоса очереди ходов (docs/own-version-roadmap.md §3.3, BattleQueueStrip): под --check она выключена без --queue-strip (прежние шаги
    боя идут без неё). С ключом — бой ход за ходом сессией (--queue-check): на каждом ходу и после каждого действия порядок и пометки портретов
    полосы — те же, что BattleSession.Upcoming (первые 10), портреты в полосе x 174–627, y 0–85, в ней нет элементов разметки и зон мыши
    поля; полоса пересобирается только по событию очереди (событий = пересборок); «Ждать» — пометка «ждёт» (дважды, ход ждавшего в конце
    раунда сверяется), паралич — «пропустит»; наведение на юнита поля — рамка его портрета, на портрет — метка под юнитом на поле; рамки
    ходящего и наведённого обводят портрет с полоской стороны снаружи (полоска открыта); портрет отражён, как его портрет в боковой панели.
    Бои: бой-образец (BATTLE_SAMPLE) и полигон A5 port-synth-1-battle (11 юнитов — предел полосы 10), он же с --flip (стороны поменяны),
    если слой есть."""
    scene = "res://scenes/battle/BattleScreen.tscn"
    runs = [(["--check", "--queue-check"], scene), (["--check", "--queue-strip", "--queue-check"], scene)]
    synth = no_synth(repo, "полоса очереди боя") is None
    if synth:
        for flip in ([], ["--flip"]):
            runs.append((["--mods", "../mods", "--check", *flip, "--scenario", "port-synth-1-battle", "--stacks", "S143KC000c,S143KC000d",
                          "--queue-strip", "--queue-check"], scene))
    outs = godot_all(repo, runs)
    code, out = outs[0]
    if code != 0 or "battle queue итог полосы нет" not in out or re.search(r"^battle queue (ход|конец)", out, re.M):
        return False, f"полоса очереди: под --check без --queue-strip она есть — код {code}\n{out.strip()}"
    found = []
    for name, (code, out) in zip(("образец", "A5", "A5 --flip"), outs[1:]):
        turns = list(QUEUE_TURN.finditer(out))
        m = QUEUE_SUM.search(out)
        bad = [t.group(0) for t in turns if t["ok"] != "1" or t["model"] != t["strip"] or len(t["model"].split(",")) > 10]
        if (code != 0 or m is None or not turns or bad or m["checks"] != m["same"] or m["out"] != "0" or int(m["most"]) > 10
                or int(m["x0"]) < 174 or int(m["x1"]) > 627 or int(m["y1"]) > 85 or m["elements"] != "0" or int(m["hit"]) <= int(m["y1"])
                or m["events"] != m["rebuilds"] or int(m["events"]) == 0 or m["covered"] != "0"
                or m["mirror_same"] != m["mirror_seen"] or m["mirror_seen"] == "0"
                or not re.fullmatch(r"(\S+):\1", m["field"]) or "W" not in m["waits"].split(",")
                or "battle queue конец портретов=0 модель=0" not in out):
            return False, f"полоса очереди, {name}: код {code}, расхождений {len(bad)}\n" + "\n".join(bad[:5]) + f"\n{out.strip()}"
        # У образца (два юнита) к концу проверки в полосе один портрет: «пропустит» и наведение на портрет — на полигоне.
        if name.startswith("A5") and (m["most"] != "10" or m["mirrored"] == "0" or int(m["rounds"]) < 2 or m["waits"] != "W,W" or not m["skip"].endswith(":S")
                             or not re.fullmatch(r"(\S+):[1-9]\d*:\1", m["strip"])):
            return False, f"полоса очереди, A5: предел, раунды, «ждёт», «пропустит» или наведение на портрет — {m.group(0)}"
        found.append(f"{name}: ходов {m['turns']}, сверок {m['checks']}, портретов до {m['most']}, событий {m['events']}, отражение {m['mirror_same']}/{m['mirror_seen']}")
    return True, ("полоса очереди: без ключа под --check нет; " + "; ".join(found)
                  + (" (полигона нет — mods/port-synth вне дерева)" if not synth else "")
                  + "; порядок и пометки = Upcoming, «ждёт» и «пропустит», наведение в обе стороны, рамки не закрывают полоски, отражение как в панели, место свободно")


VISIBLE = re.compile(r"battle visible на_поле=(?P<field>\d+) без_кадра=(?P<noframe>\d+) в_панели=(?P<panel>\d+) "
                     r"без_портрета=(?P<noface>\d+) нет_поля=(?P<nofield>\S+) нет_портрета=(?P<noportrait>\S+) под_панелью=(?P<under>\S+)")
RMB = re.compile(r"battle rmb юнит=\S+ нажата_карточка=(?P<down>\d) отпущена_карточка=(?P<up>\d)")


def battle_visible(repo: Path) -> tuple[bool, str]:
    """Прогон 3 пользователя 11.10, пункты 4, 5, 7 — бои карты 'The Seals' из журнала зонда порта (mapbattle start …):
    у каждого юнита на поле — кадр покоя с непрозрачными точками, у каждого в панели — портрет. Выросший уровнем юнит в бою — порождённая
    реализация G000UG…: картинки ищутся по базовому типу (GlobalId), иначе Титан-лидер нейтралов 2-го уровня (G000UU5218 → G000UG978c) и
    выросший лидер игрока пропадали с поля и из панели. Карточка по правой кнопке держится, пока кнопка нажата. «под_панелью» — юниты,
    которых закрывает фон показанной панели (задние места своей стороны при обеих панелях сразу) — печатается, не валит."""
    runs = [
        ("Титан", ["--scenario", "'The Seals'", "--stacks", "S109KC0000,S109KC001a", "--rmb"]),
        ("грифоны, лидер 10-го уровня", ["--scenario", "'The Seals'", "--stacks", "S109KC0000,S109KC000c", "--squad",
                                         "G000UU0019@10:2,G000UU0157:0,G000UU0154:1,G000UU0151:3,G000UU0010:5"]),
    ]
    notes = []
    for name, extra in runs:
        code, out = godot(repo, ["--check", *extra], "res://scenes/battle/BattleScreen.tscn")
        seen = VISIBLE.search(out)
        rmb = RMB.search(out)
        if (code != 0 or seen is None or seen["noframe"] != "0" or seen["noface"] != "0" or int(seen["field"]) < 4
                or ("--rmb" in extra and (rmb is None or rmb["down"] != "1" or rmb["up"] != "0"))):
            return False, f"экран боя, видимость ({name}): код {code}\n{out.strip()}"
        notes.append(f"{name}: на поле {seen['field']} с кадром, портретов {seen['panel']}, под панелью {seen['under']}"
                     + ("; карточка ПКМ закрылась при отпускании" if rmb else ""))
    return True, "; ".join(notes)


def game_over(repo: Path) -> tuple[bool, str]:
    """Конец партии (docs/scenario-events.md §4): партия с победителем — окно сообщения, эпилог DLG_END_WIN_LOSE_, выход.
    Победа человека и победа другого игрока на «Power to Corrupt» (сценарий порта data/scenarios). Звук эпилога (0x4bcba0): у квеста
    запись C000CC0001 с WAV_WIN=GENBRIEF — и при победе, и при поражении GENBRIEF после затухания музыки 750 мс; конец звука включает
    «Повтор» (0x4bcd11)."""
    results = []
    # Победа: вместо сообщения — вопрос о вывозе героя (docs/day-cycle.md §14), «Да» — окно выбора, «Отмена» — эпилог.
    for winner, won, clicks in (("S073PL000a", "1", "ui click BTN_YES;ui click BTN_CANCEL;ui click BTN_CLOSE"),
                                ("S073PL0009", "0", "ui click BTN_OK;ui click BTN_CLOSE")):
        code, out = godot(repo, ["--check", "--scenario", "Power to Corrupt", "--player", "S073PL000a", "--winner", winner,
                                 "--clicks", clicks], "res://scenes/map/MapScreen.tscn")
        exported = won == "0" or ("export ask answer=BTN_YES" in out and "окна=DLG_LEADER_PICKER" in out)
        if (code != 0 or f"game over winner={winner} viewer=S073PL000a won={won}" not in out or not exported
                or "overlay DLG_MESSAGE_BOX" not in out or "overlay DLG_END_WIN_LOSE_" not in out or "game over closed" not in out
                or "overlay tune GENBRIEF затухание=750" not in out or not re.search(r"^music GENBRIEF ", out, re.M)):
            return False, f"конец партии ({winner}): код {code}\n{out.strip()}"
        results.append("победа (вопрос о вывозе, окно выбора героя)" if won == "1" else "поражение")
    return True, f"конец партии: {', '.join(results)} — эпилог (звук GENBRIEF) → выход в меню"


def saga(repo: Path) -> tuple[bool, str]:
    """Сага от меню (docs/day-cycle.md §15–16): «Новая сага» → раса (эльфы) → лорд → брифинг → глава 1 (вступление — окна событий)
    → победа (шаг win) → сообщение → эпилог → окно переноса DLG_CARRY_ITEM_LEADER → «Закрыть» — один вопрос → «Да» → глава 2.
    Названия глав — по-русски (перевод Руссобит-М, data/scenarios/lang/ru): язык по умолчанию — ru."""
    steps = ["click BTN_SINGLE", "click BTN_NEW_TOURNEMENT", "key 13", "click BTN_FACE", "key 13", "click BTN_CONTINUE"]
    clicks = "ok;ok;ok;win;ok;ok;ok;ui click BTN_OK;ui click BTN_CLOSE;ui click BTN_CLOSE;ui click BTN_YES"
    code, out = godot(repo, ["--check", "--input", "; ".join(steps), "--clicks", clicks], "res://scenes/ui/UiFlow.tscn")
    games = re.findall(r"game scenario=(\S+) from=new", out)
    asked = re.search(r"ui click_BTN_CLOSE on=DLG_CARRY_ITEM_LEADER ok=1 \S+ окна=(\S+)", out)
    if (code != 0 or games != ["Грехи_отцов", "Охота"] or "chapter won → carry over" not in out
            or asked is None or asked.group(1) != "DLG_CARRY_ITEM_LEADER>DLG_MESSAGE_BOX"
            or "next chapter campaign=C000CC0006 chapter=1 scenario=S136SC0000" not in out
            or "overlay tune WINFGEN затухание=750" not in out):
        return False, f"сага: код {code}, главы {games}, окна {asked.group(1) if asked else '-'}\n{out.strip()}"
    return True, "сага: эльфы, глава 1 → победа → эпилог (звук WINFGEN) → окно переноса (один вопрос) → глава 2"


CYRILLIC = re.compile("[А-Яа-яЁё]")
SCENARIO_TEXT_STEPS = ["click BTN_SINGLE", "click BTN_NEW_SKIRMISH", "key 13", "key 13", "key 13"]


def scenario_texts(repo: Path) -> tuple[bool, str]:
    """Перевод сценариев (data/scenarios/lang/<язык>, LastEdict.Data.Play.ScenarioTexts): с языком ru (по умолчанию) описание в выборе
    сценария и задание брифинга первого квеста ('A Return To Simpler Times', перевод порта) и попап события «Ненависти» в ход ИИ
    дня 1 (S073EV0002, текст Руссобит-М) — русские; с --lang en (перевода нет) — оригинал."""
    found = {}
    for lang in ("ru", "en"):
        extra = ["--input", "; ".join(SCENARIO_TEXT_STEPS), "--probe", "TXT_DESCRIPTION,TXT_TEXT_SCROLL"] + (["--lang", lang] if lang != "ru" else [])
        code, out = godot(repo, extra, "res://scenes/ui/UiFlow.tscn")
        texts = {p["id"]: p["text"] for p in PROBE.finditer(out) if p["text"] != "-"}
        code2, out2 = godot(repo, ["--check", "--scenario", "Hatred", "--player", "S073PL0009", "--humans", "S073PL0009", "--clicks", "end;ok"]
                            + (["--lang", lang] if lang != "ru" else []), "res://scenes/map/MapScreen.tscn")
        popup = re.search(r"event popup S073EV0002 for=\S+ day=1(?: ai=1)? text=(\S+)", out2)
        if code != 0 or code2 != 0 or "TXT_DESCRIPTION" not in texts or "TXT_TEXT_SCROLL" not in texts or popup is None:
            return False, f"перевод сценариев ({lang}): код {code}/{code2}, тексты {sorted(texts)}, попап {bool(popup)}\n{out.strip()}\n{out2.strip()}"
        found[lang] = (texts["TXT_DESCRIPTION"], texts["TXT_TEXT_SCROLL"], popup.group(1))
    ru, en = found["ru"], found["en"]
    # Перевод первого квеста — машинный перевод порта (data/scenarios/lang/ru, в публичное дерево не входит): без него проверяется только
    # попап «Ненависти» из слоя русификации.
    if not (repo / "data" / "scenarios" / "lang" / "ru" / "'A Return To Simpler Times'.json").is_file():
        if not CYRILLIC.search(ru[2]) or CYRILLIC.search(en[2]):
            return False, f"перевод сценариев (без перевода порта): ru {ru[2]}, en {en[2]}"
        return True, f"перевод сценариев: перевода порта нет (публичное дерево) — попап ru «{ru[2][:24]}…», en «{en[2][:16]}…»"
    if not all(CYRILLIC.search(t) for t in ru) or any(CYRILLIC.search(t) for t in en) or not en[0].startswith("Average"):
        return False, f"перевод сценариев: ru {ru}, en {en}"
    return True, (f"перевод сценариев: ru — выбор «{ru[0][:24]}…», брифинг «{ru[1][:24]}…», попап «{ru[2][:24]}…»; "
                  f"en — «{en[0][:16]}…», «{en[2][:16]}…»")


def event_battle(repo: Path) -> tuple[bool, str]:
    """Бой эффекта L_BATTLE в ход человека (docs/day-cycle.md §7): «Drega Zul», день 1, отряд нежити S001KC0001 входит в область
    S001LO000d — событие S001EV001a: попап, затем экран боя (нападает оруженосец S001KC0014), закрытие — итог в карту."""
    code, out = godot(repo, ["--check", "--scenario", "Drega Zul", "--player", "S001PL000a", "--humans", "S001PL000a",
                             "--clicks", "end;ok;16,26;21,32;21,32;ok"], "res://scenes/map/MapScreen.tscn")
    lines = out.splitlines()
    popup = next((i for i, line in enumerate(lines) if "event popup S001EV001a" in line), -1)
    start = next((i for i, line in enumerate(lines) if "eventbattle start attacker=S001KC0014 defender=S001KC0001" in line), -1)
    end = next((i for i, line in enumerate(lines) if i > start >= 0 and line.startswith("mapbattle end")), -1)
    if code != 0 or not 0 <= popup < start < end:
        return False, f"бой эффекта: код {code}\n{out.strip()}"
    return True, f"бой эффекта: попап S001EV001a → экран боя S001KC0014 против S001KC0001 → {lines[end].strip()}"


def leader_import(repo: Path) -> tuple[bool, str]:
    """Импорт героя в одиночной игре (docs/day-cycle.md §12; лобби «горячего стула» вырезано, Р33): герои игры (.ldr из Exports копии
    с экспортом) переводятся в каталог героев порта; выбор лорда → «Импорт героя» → DLG_IMPORT_LEADER → просмотр DLG_VIEW_EXPLEAD →
    «ОК» → вопрос → «Да» → имя героя у лорда → брифинг → партия."""
    exports = repo.parent / "disciples-modding" / "game-dev" / "Exports"
    if not any(exports.glob("*.ldr")):
        return True, "импорт героя: нет образцов .ldr — пропуск"
    leaders = repo / ".cache" / "ui-check" / "leaders"
    if leaders.exists():
        for f in leaders.iterdir():
            f.unlink()
    steps = ["click BTN_SINGLE", "click BTN_NEW_SKIRMISH", "key 13", "key 13", "click BTN_IMPORT_LEADER", "key 40", "click BTN_VIEW_LEADER",
             "click BTN_CLOSE", "click BTN_OK", "click BTN_YES", "key 13", "key 13"]
    code, out = godot(repo, ["--check", "--leaders", str(leaders), "--ldr", str(exports), "--probe", "TXT_IMPORT_NAME",
                             "--input", "; ".join(steps)], "res://scenes/ui/UiFlow.tscn")
    windows = re.findall(r"flow window (\S+)", out)
    named = re.search(r"id=TXT_IMPORT_NAME text=(\S+)", out)
    if (code != 0 or "leaders imported=" not in out or windows != ["DLG_IMPORT_LEADER", "DLG_VIEW_EXPLEAD", "DLG_MESSAGE_BOX"]
            or named is None or GAME.search(out) is None):
        return False, f"импорт героя: код {code}, окна {windows}\n{out.strip()}"
    return True, f"импорт героя: .ldr → герои порта, выбор лорда → окно выбора → просмотр → вопрос → «{named.group(1)}» → партия"


AITURN = re.compile(r"aiturn игроков=(?P<players>\d+) ходов=(?P<moves>\d+) показано=(?P<shown>\d+) под_туманом=(?P<hidden>\d+) "
                    r"боёв=(?P<battles>\d+) с_человеком=(?P<human>\d+) день=(?P<day>\d+) ошибка=(?P<error>\d)")

# Окно ожидания к концу хода ИИ (MapScreen.OnAiFinished): у движка IMG_LEADER и TXT_LEADER_NAME в DLG_STRAT_WAIT пусты (Ин39,
# смена панели 0x48d858 ставит только IMG_LORD_FACE; портрет и имя лидера — только в DLG_STRATEGIC, проверка 0x48d934).
AIWAIT = re.compile(r"^aiwait лидер=(?P<leader>\S+) имя=(?P<name>\S+) лорд=(?P<lord>\S+)$", re.MULTILINE)

# Мигание в ход ИИ (замечание 11.10, п. 22): кусок земли под камерой не пропадает ни на кадр (старая картинка стоит до новой), пересборка
# объектов оставляет нетронутым узлам их кадр анимации (0x5ace8a), а не сбрасывает все анимации карты к кадрам Random(1).
AIFLICKER = re.compile(r"провалов_земли=(?P<gaps>\d+) удержано=(?P<held>\d+) кадров=(?P<frames>\d+) пересборок=(?P<builds>\d+) "
                       r"кадр_сохранён=(?P<kept>\d+) скачков_кадра_снято=(?P<jumps>\d+)")

# Ход ИИ на экране (docs/ai-screen.md): сохранение, глазами человека, что должно быть видно.
AI_TURN_CASES = [
    # День 1 новой партии за Легионы, туман закрыт: ходы ИИ за туманом не показываются, открытые — идут по клеткам.
    (1, LEGIONS, "туман"),
    # День 23 партии стенда за Кланы: ИИ нападает на отряд человека — экран боя поверх карты, итог в ход ИИ.
    (23, STAND_PLAYER, "бой"),
]


WALKED = re.compile(r"walked (?P<stack>\S+) в=\((?P<x>\d+),(?P<y>\d+)\) событий=\d+ туман_по_шагам=(?P<opened>\d+)/(?P<steps>\d+) "
                    r"кадров=(?P<frames>\d+) вид_на_отряде=(?P<followed>\d+) кадр_мс=(?P<key_ms>[\d.]+) кадр_мин_мс=(?P<key_min>[\d.]+) "
                    r"группа=(?P<group>\d+)")


def walk_clock_ok(w: re.Match, period: int) -> bool:
    """Часы хода (CTwoSpeedAnimator, 0x4f5e7c): группа хода — period мс, ни один ключевой кадр не короче периода (тик без догона), в среднем —
    не больше четырёх периодов (кадр за тик; без окна кадр Godot под нагрузкой бывает длиннее периода — тик тогда на границе кадра)."""
    return w["group"] == str(period) and float(w["key_min"]) >= period - 0.5 and float(w["key_ms"]) < 4 * period


def walk_steps(repo: Path) -> tuple[bool, str]:
    """Ход отряда человека (замечание 11.10, пп. 19 и 34): ключевые кадры задачи движения (StackWalk), вид на отряде на каждом кадре
    (0x4f6539), туман открывается на первом кадре каждого шага (0x4f6856 → 0x5d52fe), а не куском по концу пути. Легионы, день 1 (туман
    закрыт): S073KC0002 из столицы к 14,92 — 7 шагов. Второе — мешок (п. 26): на дне 24 Кланов брошенный рядом мешок берётся щелчком
    по нему (путь из одной точки — только подбор, 0x43fcb5), окно полученного DLG_ITEM. Last Edict: день 1 — новая партия за Легионы,
    мешок — день 21 партии стенда, герой с сумкой бросает предмет на соседнюю клетку. Нет импорта — пропуск."""
    first, _, why = stand(repo, 1, LEGIONS)
    save, m, _ = stand(repo, 21)
    if first is None or save is None:
        return True, f"ход и туман по шагам: пропуск — {why}"
    start = ["--state", str(first)]
    code, out = godot(repo, [*start, "--check", "--player", LEGIONS, "--clicks", "select S073KC0002;14,92;14,92"],
                      "res://scenes/map/MapScreen.tscn")
    w = WALKED.search(out)
    if (code != 0 or w is None or (w["x"], w["y"]) != ("14", "91") or w["steps"] != "7" or int(w["opened"]) < 2
            or int(w["frames"]) < int(w["steps"]) or w["followed"] != w["frames"] or not walk_clock_ok(w, 33)):
        return False, f"ход и туман по шагам: код {code}\n{out.strip()}"
    walk = (f"ход {w['stack']} в {w['x']},{w['y']}: шагов {w['steps']}, туман открылся на {w['opened']}, ключевых кадров {w['frames']}, "
            f"вид на отряде {w['followed']}, кадр {w['key_ms']} мс (не короче {w['key_min']}, группа {w['group']})")
    # Флажок «Плавная прокрутка при движении отряда» выключен: шаг кадра вдвое длиннее (14 точек), группа хода медленная — 66 мс (0x4f5e7c).
    settings = repo / ".cache" / "ui-check" / "walk-settings.json"
    settings.parent.mkdir(parents=True, exist_ok=True)
    settings.write_text(json.dumps({"StackSmoothScroll": False}), encoding="utf-8")
    code, out = godot(repo, [*start, "--check", "--player", LEGIONS, "--settings", str(settings),
                             "--clicks", "select S073KC0002;14,92;14,92"], "res://scenes/map/MapScreen.tscn")
    slow = WALKED.search(out)
    if (code != 0 or slow is None or (slow["x"], slow["y"]) != ("14", "91") or not walk_clock_ok(slow, 66)
            or abs(int(slow["frames"]) * 2 - int(w["frames"])) > int(w["steps"]) * 2):
        return False, f"ход без плавности: код {code}\n{out.strip()}"
    walk += (f"; без плавности — ключевых кадров {slow['frames']}, кадр {slow['key_ms']} мс (не короче {slow['key_min']}, "
             f"группа {slow['group']})")
    hero = max(m.field_stacks(STAND_PLAYER), key=lambda st: (st.items, st.id))
    bag = "%d,%d" % m.free_near(hero.x, hero.y)
    code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--clicks",
                             f"select {hero.id};panel release IMG_LEADER;ui click BTN_PICK_UP_DROP;drop {bag};ui press 50,413;"
                             f"ui release 494,413;ui click BTN_BACK;{bag};{bag};ui click BTN_OK"], "res://scenes/map/MapScreen.tscn")
    visit = re.search(r"^visit (S073BG\w+) добыча", out, re.M)
    if code != 0 or visit is None or "fx Midgard/TAKEBAG " not in out or "ui click_BTN_OK on=DLG_ITEM " not in out:
        return False, f"мешок рядом: код {code}\n{out.strip()}"
    return True, f"ход и туман по шагам: {walk}; мешок {visit.group(1)} взят щелчком, окно DLG_ITEM"


def land_spread_ok(out: str) -> bool:
    """Растекание земли по пачкам (CMidAnim2Terrain, docs/ai-screen.md §11): есть хоть одно; пачек столько же, сколько звуков, и последняя
    пачка каждого растекания перекрасила все его клетки."""
    spreads = re.findall(r"^landspread клеток=(\d+) групп=\d+ пачки=\S+ звуков=(\d+)", out, re.M)
    packs = re.findall(r"^landspread пачка (\d+)/(\d+) новых=(\d+)/(\d+)", out, re.M)
    last = [p for p in packs if p[0] == p[1]]
    return (len(spreads) > 0 and len(packs) == sum(int(s[1]) for s in spreads) and len(last) == len(spreads)
            and all(p[2] == p[3] for p in last))


def ai_turn(repo: Path) -> tuple[bool, str]:
    """Ход ИИ на экране (пункт 6, задача 4.4): «конец хода» проводит ходы ИИ в фоне; на каждого игрока ИИ — строка окна
    ожидания DLG_STRAT_WAIT с портретом лорда, на каждый показанный ход отряда — строка aimove; показано + под туманом = все
    ходы отрядов ИИ; бой ИИ с человеком открывает экран боя (aibattle start/end), и ход ИИ продолжается; вода анимирована
    (WATERBKGND1, 16 кадров). Нет импорта — пропуск."""
    results = []
    for day, player, want in AI_TURN_CASES:
        path, _, why = stand(repo, day, player)
        if path is None:
            return True, f"ход ИИ: пропуск — {why}"
        save = f"день {day} за {player}"
        code, out = godot(repo, ["--state", str(path), "--check", "--player", player, "--clicks", "end"], "res://scenes/map/MapScreen.tscn")
        turn = AITURN.search(out)
        flicker = AIFLICKER.search(out)
        wait = AIWAIT.search(out)
        moves = [l for l in out.splitlines() if l.startswith("aimove ")]
        players = [l for l in out.splitlines() if l.startswith("aiplayer ") and "лорд=Lords:LORD" in l]
        starts, ends = out.count("aibattle start "), out.count("aibattle end ")
        if (code != 0 or turn is None or turn["error"] != "0" or len(moves) != int(turn["shown"]) or "water кадров=16" not in out
                or len(players) != int(turn["players"]) or int(turn["shown"]) + int(turn["hidden"]) != int(turn["moves"])
                or flicker is None or flicker["gaps"] != "0" or int(flicker["kept"]) == 0
                or wait is None or wait["leader"] != "-" or wait["name"] != "-"
                or starts != ends or starts != int(turn["human"])
                or (want == "туман" and (int(turn["hidden"]) == 0 or int(turn["shown"]) == 0))
                or (want == "бой" and (starts == 0 or re.search(r"aibattle start .* flip=[01] bg=\d+", out) is None
                                       or not land_spread_ok(out) or "fx Midgard/OCCUPY " not in out))):
            return False, f"ход ИИ {save}: код {code}\n{out.strip()}"
        results.append(f"{save}: игроков {turn['players']}, ходов {turn['moves']} (показано {turn['shown']}, "
                       f"под туманом {turn['hidden']}), боёв с человеком {turn['human']}, день {turn['day']}; мигание: провалов земли "
                       f"{flicker['gaps']} из {flicker['frames']} кадров, пересборок {flicker['builds']}, скачков кадра снято {flicker['jumps']}")
    return True, "ход ИИ: " + "; ".join(results)


SMOOTH = re.compile(r"^smoothscroll в=\((?P<x>\d+),(?P<y>\d+)\) положений=(?P<count>\d+) тиков=(?P<ticks>\d+) мс=(?P<ms>\d+) "
                    r"тик_мин_мс=(?P<min>[\d.]+) сдвиги=(?P<path>\S+)$", re.MULTILINE)


def engine_line(x0: int, y0: int, x1: int, y1: int) -> list[tuple[int, int]]:
    """Отрезок движка 0x51f452 → 0x51f4fe (своя запись, не код порта): max(|dx|,|dy|)+1 точек, Брезенхэм сверху вниз — у отрезка вверх
    точки строятся от конца и кладутся с последней."""
    n = max(abs(x0 - x1), abs(y0 - y1)) + 1
    pts: list[tuple[int, int]] = [(0, 0)] * n
    at, step_at = 0, 1
    sx, sy, ex, ey = x0, y0, x1, y1
    if y0 > y1:
        at, step_at, sx, sy, ex, ey = n - 1, -1, x1, y1, x0, y0
    dx, dy, xs = ex - sx, ey - sy, 1
    if dx <= 0:
        dx, xs = -dx, -1
    x, y = sx, sy
    pts[at] = (x, y)
    major, minor = (dx, dy) if dx > dy else (dy, dx)
    err = 2 * minor - major
    for _ in range(major):
        at += step_at
        if err >= 0:
            err += 2 * minor - 2 * major
            if dx > dy:
                y += 1
            else:
                x += xs
        else:
            err += 2 * minor
        if dx > dy:
            x += xs
        else:
            y += 1
        pts[at] = (x, y)
    return pts


def engine_scroll(dx: int, dy: int) -> list[tuple[int, int]]:
    """Положения CMidAnimSmoothScroll (0x4f4b51): точки отрезка от сдвига до нуля с номерами 0, 4, 12, 24, … до половины (шаг +4 до 128) и
    симметрично с конца."""
    line = engine_line(dx, dy, 0, 0)
    n, half = len(line), len(line) // 2
    head, i, step = [], 0, 0
    while i < half:
        head.append(line[i])
        step += 4 if step < 128 else 0
        i += step
    tail, i, step = [], n - 1, 0
    while i >= half:
        tail.append(line[i])
        step += 4 if step < 128 else 0
        i -= step
    return head + tail[::-1]


FOGCHANGE = re.compile(r"^fogchange в=\((?P<x>\d+),(?P<y>\d+)\) радиус=(?P<r>\d+) шагов=(?P<steps>\d+) мс=(?P<ms>\d+) "
                       r"тик_мин_мс=(?P<min>[\d.]+) сменилось=(?P<changed>\d+) по_шагам=(?P<per>[\d,]+) клетки=(?P<cells>.*)$", re.MULTILINE)
FOGTEST = re.compile(r"^fogtest done (?P<x>\d+),(?P<y>\d+),(?P<r>\d+) открыто=(?P<opened>\d+) показ=1 идёт=0 расхождение=(?P<left>\d+) "
                     r"карта=(?P<size>\d+)$",
                     re.MULTILINE)


def engine_fog_rings(x: int, y: int, r: int, size: int) -> list[set[tuple[int, int]]]:
    """Кольца CMidAnim2ChangeFog (своя запись по 0x4f1a29/0x4f1b63): прямоугольник (x,y)…(x+1,y+1) шире на r и зажат в [0, size]; центр
    ((л+п)>>1, (в+н)>>1), кольцо 1, 3, …; клетки периметра, углы на клетку внутрь; конец — когда кольцо + 2 > сторона."""
    left, top, right, bottom = max(0, x - r), max(0, y - r), min(size, x + 1 + r), min(size, y + 1 + r)
    side, cx, cy = right - left, (left + right) >> 1, (top + bottom) >> 1
    rings, ring = [], 1
    while True:
        cells = set()
        for row in range(ring):
            for col in range(ring):
                if row not in (0, ring - 1) and col not in (0, ring - 1):
                    continue
                px, py = cx + col, cy + row
                if ring > 1 and col in (0, ring - 1) and row in (0, ring - 1):
                    px += 1 if col == 0 else -1
                    py += 1 if row == 0 else -1
                if 0 <= px < size and 0 <= py < size:
                    cells.add((px, py))
        rings.append(cells)
        cx, cy = cx - 1, cy - 1
        if ring + 2 > side:
            return rings
        ring += 2


def fog_change(repo: Path) -> tuple[bool, str]:
    """Смена тумана кольцами (CMidAnim2ChangeFog, MapView.Fog.cs): Легионы, день 1 — эффект CEffectFog зрителю (шаг fogtest: квадрат ±r без
    углов открыт в модели) в 60,60 с радиусом 3 и у угла карты 1,1 с радиусом 4. Сначала вид едет к центру (плавная прокрутка), затем туман
    догоняет модель кольцами: шагов и клетки каждого шага — по своему пересчёту колец, шаг не короче 66 мс (медленная группа); после
    показа показанный туман равен модели. Last Edict: день 1 новой партии за Легионы. Нет импорта — пропуск."""
    save, _, why = stand(repo, 1, LEGIONS)
    if save is None:
        return True, f"смена тумана: пропуск — {why}"
    code, out = godot(repo, ["--state", str(save), "--check", "--player", LEGIONS, "--clicks", "fogtest 60,60,3;fogtest 1,1,4"],
                      "res://scenes/map/MapScreen.tscn")
    changes, tests = list(FOGCHANGE.finditer(out)), list(FOGTEST.finditer(out))
    if code != 0 or len(changes) != 2 or len(tests) != 2 or any(t["left"] != "0" for t in tests):
        return False, f"смена тумана: код {code}, показов {len(changes)}, проверок {len(tests)}\n{out.strip()}"
    results = []
    for change, test in zip(changes, tests):
        x, y, r = int(change["x"]), int(change["y"]), int(change["r"])
        rings = engine_fog_rings(x, y, r, int(test["size"]))
        per = [int(v) for v in change["per"].split(",")]
        steps = [[tuple(int(v) for v in c.split(",")) for c in part.split("/") if c] for part in change["cells"].split(";")]
        scrolled = re.findall(r"^smoothscroll в=\((\d+),(\d+)\)", out[:change.start()], re.MULTILINE)
        if (int(change["steps"]) != len(rings) or len(per) != len(rings) or len(steps) != len(rings)
                or any(not set(cells) <= ring for cells, ring in zip(steps, rings)) or sum(per) != int(change["changed"])
                or (len(rings) > 1 and float(change["min"]) < 65.5) or not scrolled or scrolled[-1] != (change["x"], change["y"])):
            return False, f"смена тумана в {x},{y} r={r}: {change.group(0)[:400]}\nкольца по движку {[len(g) for g in rings]}"
        results.append(f"{x},{y} r={r}: {len(rings)} колец, по кольцам {change['per']}, всего {change['changed']} из {test['opened']}")
    if int(changes[0]["changed"]) != int(tests[0]["opened"]):
        return False, f"смена тумана: в середине карты кольца сменили не все клетки эффекта\n{out.strip()}"
    return True, "смена тумана кольцами: " + "; ".join(results) + " — после показа расхождений с моделью 0"


def smooth_scroll(repo: Path) -> tuple[bool, str]:
    """Плавная прокрутка к событиям (CMidAnimSmoothScroll, MapView.Scroll.cs): ход ИИ дня 30 Кланов — перед ходами отрядов и событиями на
    виду вид едет к месту; положения по тикам совпадают с разгоном движка (свой пересчёт отрезка 0x51f452 и номеров 0x4f4b51 от первого
    сдвига), последнее — (0, 0), тик не короче 33 мс (быстрая группа). Флажок «Прокрутка между действиями» выключен — ни одной плавной
    прокрутки, вид встаёт сразу. Last Edict: ход ИИ дня 30 партии стенда. Нет импорта — пропуск."""
    save, _, why = stand(repo, 30)
    if save is None:
        return True, f"плавная прокрутка: пропуск — {why}"
    code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--clicks", "end"], "res://scenes/map/MapScreen.tscn")
    scrolls = list(SMOOTH.finditer(out))
    if code != 0 or not scrolls or AITURN.search(out) is None:
        return False, f"плавная прокрутка: код {code}, прокруток {len(scrolls)}\n{out.strip()}"
    positions = ticks = 0
    for s in scrolls:
        path = [tuple(int(v) for v in p.split(",")) for p in s["path"].split(";")]
        want = engine_scroll(*path[0])
        if (path != want or int(s["count"]) != len(path) or int(s["ticks"]) != len(path) - 1
                or (len(path) > 1 and (float(s["min"]) < 32.5 or int(s["ms"]) >= 132 * (len(path) - 1)))):
            return False, f"плавная прокрутка в {s['x']},{s['y']}: положения {path[:6]}…, по движку {want[:6]}…, {s.group(0)[:300]}"
        positions += len(path)
        ticks += int(s["ticks"])
    ms = sum(int(s["ms"]) for s in scrolls)
    settings = repo / ".cache" / "ui-check" / "scroll-settings.json"
    settings.parent.mkdir(parents=True, exist_ok=True)
    settings.write_text(json.dumps({"SmoothScroll": False}), encoding="utf-8")
    code, off = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--settings", str(settings), "--clicks", "end"],
                      "res://scenes/map/MapScreen.tscn")
    turn = AITURN.search(off)
    if code != 0 or turn is None or turn["error"] != "0" or SMOOTH.search(off):
        return False, f"плавная прокрутка выключена: код {code}\n{off.strip()}"
    return True, (f"плавная прокрутка: {len(scrolls)} прокруток, {positions} положений по движку, {ticks} тиков за {ms} мс "
                  f"({ms / max(ticks, 1):.1f} мс на тик); флажок выключен — прокруток 0")


def sound(repo: Path) -> tuple[bool, str]:
    """Звук и музыка (docs/ai-screen.md §12). Карта (день 23 партии стенда, Кланы): играет музыка карты; у своего отряда звучат
    ближайшие фоны мест; выбор отряда — голос лидера; в ходе ИИ шаги отрядов звучат, нападение ИИ на отряд человека — на последнем шаге
    ATTK, перед боем SBATTLE (CMidAnim2Unit, CMidAnim2Battle). У руины (вид на клетку правее и ниже) её источник RUIN молчит и места не
    занимает (CAudioRgnRuin 0x50851a: имя RUIN, в AudioRgn.wdb только RUIN1..4 — 0x5674ce не находит). Экран боя: звуки действий по
    записям хода (замах, удар, гибель). Нет импорта — только бой."""
    results = []
    save, m, _ = stand(repo, 23)
    if save is not None:
        own = m.field_stacks(STAND_PLAYER)[0]
        ruin = m.nearest_site("Ruin", own.x, own.y)
        near = f"{ruin.x + 1},{ruin.y + 1}"
        code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER,
                                 "--clicks", f"regions {own.cell};regions {near};{own.cell};end"], "res://scenes/map/MapScreen.tscn")
        music = re.search(r"^music (INGAME|AMB)\d+ ", out, re.M)
        regions = re.search(rf"regions {own.cell} источников=(\d+) звучат=\w+:\d+:\d+", out)
        silent = re.search(rf"regions {near} источников=\d+ звучат=(\S*) молчат=(\S+)", out)
        ruin_ok = silent is not None and f"RUIN@{ruin.x},{ruin.y}" in silent[2].split(";") and "RUIN" not in silent[1]
        attack = re.search(r"^fx Midgard/G000UU\d{4}ATTK ", out, re.M) and "fx Midgard/SBATTLE " in out
        voice = re.search(r"^fx Midgard/G000UU\d{4}ACK[123] ", out, re.M)
        steps = re.findall(r"^fx Midgard/(?:WALK|FLY)\w+ ", out, re.M)
        if code != 0 or music is None or regions is None or not ruin_ok or voice is None or not steps or not attack:
            return False, (f"звук карты: код {code}, музыка {music is not None}, фон {regions is not None}, руина {ruin_ok}, "
                           f"голос {voice is not None}, шагов {len(steps)}, нападение {bool(attack)}\n{out.strip()}")
        results.append(f"карта: музыка, фон мест ({regions[1]} источников; у руины {ruin.x},{ruin.y} звучат {silent[1] or '-'}, RUIN молчит), "
                       f"голос, шагов {len(steps)}, нападение ATTK и SBATTLE")
    code, out = godot(repo, ["--check"], "res://scenes/battle/BattleScreen.tscn")
    actions = re.findall(r"^battle sounds \S+ (\S+)", out, re.M)
    # Звуки действий идут очередью по таймерам, а проверка выходит сразу — сверяется план, не проигрывание.
    if code != 0 or not actions or not any("DEATH@" in a for a in actions):
        return False, f"звук боя: код {code}, действий {len(actions)}\n{out.strip()}"
    results.append(f"бой: действий со звуком {len(actions)}, гибель звучит")
    return True, "звук: " + "; ".join(results)


def battle_opening(repo: Path) -> tuple[bool, str]:
    """Экран боя от хозяина доигрывает ходы ИИ до первого хода человека (docs/ai-screen.md §5): бои за ближнюю охраняемую руину всех
    отрядов дня 21 партии стенда разом — экран всегда открывается с ходом человека, и хотя бы в одном бою охрана ходит первой. Нет
    импорта — пропуск."""
    save, m, why = stand(repo, 21)
    if save is None:
        return True, f"начало боя: пропуск — {why}"
    raids = [(st, m.nearest_site("Ruin", st.x, st.y, guarded=True)) for st in m.field_stacks(STAND_PLAYER)]
    outs = godot_all(repo, [(["--state", str(save), "--check", "--player", STAND_PLAYER, "--clicks", f"arrive {st.id} {ruin.id}"],
                             "res://scenes/map/MapScreen.tscn") for st, ruin in raids])
    opened = []
    for (st, ruin), (code, out) in zip(raids, outs):
        found = re.search(r"battle open ходов_ИИ_до_игрока=(?P<steps>\d+) ждёт_игрока=(?P<waits>\d)", out)
        if code != 0:
            return False, f"начало боя {st.id} у {ruin.id}: код {code}\n{out.strip()}"
        if found is not None:   # вор и прочие без боя у руины экран не открывают
            if found["waits"] != "1":
                return False, f"начало боя {st.id} у {ruin.id}: экран открыт не на ходе человека\n{found.group(0)}"
            opened.append((st.id, int(found["steps"])))
    first = [f"{sid} ({steps})" for sid, steps in opened if steps > 0]
    if not first:
        return False, f"начало боя: ни в одном из {len(opened)} боёв охрана не ходила первой — {opened}"
    return True, f"начало боя: {len(opened)} боёв, экран — на ходе человека; ИИ ходил первым у {', '.join(first)}"


HIGHLIGHT = re.compile(r"^highlight (?P<cell>\d+,\d+) клетка=(?P<shown>\S+) фаза_до=(?P<before>-?\d+) фаза=(?P<phase>-?\d+) "
                       r"настройка=(?P<on>\d) видно=(?P<seen>\d+)/(?P<all>\d+)$", re.MULTILINE)


def highlight(repo: Path) -> tuple[bool, str]:
    """Подсветка клетки под курсором TILE_HIGHLIGHT (docs/ai-screen.md): над любой клеткой (решение пользователя 28.09), слой 320 в общем
    порядке отрисовки, при смене клетки анимация продолжается с того же кадра (0x4d4bc9); переключатель TOG_ISOHIGHLIGHT окна настроек
    убирает её (настройка IsoHighlight). «видно» — точки круга, не закрытые картинками, что рисуются позже (отряд, лес, туман): у
    большого юнита круг под ним закрыт телом сильнее, как у движка (слой 320 под STACKS 391, клетка — по мыши, 0x540f6a)."""
    settings = repo / ".cache" / "ui-check" / "highlight-settings.json"
    if settings.exists():
        settings.unlink()
    code, out = godot(repo, ["--check", "--scenario", "Tutorial", "--settings", str(settings), "--clicks",
                             "highlight 10,10;highlight 11,10;order highlight;ui click BTN_OPTIONS;ui set cellHighlight 0;"
                             "ui apply;ui click BTN_CLOSE;highlight 12,10"], "res://scenes/map/MapScreen.tscn")
    rows = [m.groupdict() for m in HIGHLIGHT.finditer(out)]
    order = re.search(r"^order highlight картинок=1 ", out, re.MULTILINE)
    if (code != 0 or len(rows) != 3 or order is None or rows[0]["shown"] != "10,10" or rows[1]["shown"] != "11,10"
            or int(rows[1]["before"]) < 0 or rows[1]["phase"] != rows[1]["before"] or rows[2]["shown"] != "-" or rows[2]["on"] != "0"
            or int(rows[0]["seen"]) == 0 or rows[2]["all"] != "0"):
        return False, f"подсветка клетки: код {code}\n{out.strip()}"
    return True, (f"подсветка клетки: 10,10 → 11,10 с кадра {rows[1]['before']} на {rows[1]['phase']}, в общем порядке, видно "
                  f"{rows[0]['seen']}/{rows[0]['all']} точек; TOG_ISOHIGHLIGHT выключил")


def clouds(repo: Path) -> tuple[bool, str]:
    """Облака над картой (CMapClouds 0x5bd6d3, docs/ai-screen.md): на карте 48×48 — ⌊(48/48)²·10⌋ = 10 облаков в общем порядке отрисовки,
    за полсекунды сдвигаются вправо; TOG_ISOCLOUDS (IsoBirds) убирает их."""
    code, out = godot(repo, ["--check", "--scenario", "Mercantile", "--clicks",
                             "clouds;ui click BTN_OPTIONS;ui set clouds 0;ui apply;ui click BTN_CLOSE;clouds"], "res://scenes/map/MapScreen.tscn")
    rows = re.findall(r"^clouds облаков=(\d+) в_порядке=(\d+)/\d+ сдвиг=(-?\d+) настройка=(\d)$", out, re.MULTILINE)
    if code != 0 or len(rows) != 2 or rows[0][:2] != ("10", "10") or int(rows[0][2]) <= 0 or rows[1][0] != "0" or rows[1][3] != "0":
        return False, f"облака: код {code}\n{out.strip()}"
    return True, f"облака: 10 на Mercantile, сдвиг {rows[0][2]} точек за 0,5 с, TOG_ISOCLOUDS убрал"


def mountains(repo: Path) -> tuple[bool, str]:
    """Снос гор L_REMOVE_MOUNTAIN (docs/ai-screen.md, CMidAnim2Mountain): RMVMOUNTAIN7 по центру области, на середине — OCCUPY и
    перерисовка карты. Шаг mountains — показ по точке MountainsRemoved (ядро — сессия В)."""
    code, out = godot(repo, ["--check", "--scenario", "Tutorial", "--clicks", "mountains 20,20"], "res://scenes/map/MapScreen.tscn")
    played = re.search(r"^mountains CHECK в=\(20,20\) картинка=RMVMOUNTAIN7 кадров=(\d+)$", out, re.MULTILINE)
    if code != 0 or played is None or "mountains done 20,20 показ=1 играет=0" not in out:
        return False, f"снос гор: код {code}\n{out.strip()}"
    return True, f"снос гор: RMVMOUNTAIN7, {played[1]} кадров, доиграл"


def ai_popup(repo: Path) -> tuple[bool, str]:
    """Попап события человеку в ходе ИИ (docs/ai-screen.md): у движка окно сразу (CCmdPopupMsg → 0x489e90), показ хода ждёт его закрытия.
    S095SC0000, человек S095PL0009, конец дня 0: событие S095EV0000 (попап, затем смена владельца отряда S095KC0022) срабатывает на рабочем
    потоке хода — окно встаёт раньше анимации смены владельца и конца хода, а не после хода."""
    code, out = godot(repo, ["--check", "--scenario", "S095SC0000", "--player", "S095PL0009", "--humans", "S095PL0009",
                             "--clicks", "ok;end"], "res://scenes/map/MapScreen.tscn")
    asked, shown = out.find("aipopup для=S095PL0009"), out.find("event popup S095EV0000")
    owner, turn = out.find("aisight владелец:S095PL0009 S095KC0022"), out.find("aiturn ")
    if code != 0 or not 0 <= asked < shown < owner < turn:
        return False, f"попап в ходе ИИ: код {code}\n{out.strip()}"
    return True, "попап в ходе ИИ: S095EV0000 — окно на рабочем потоке, до смены владельца S095KC0022 и конца хода"


SETTINGS_LINE = re.compile(r"^flow settings вкладка=(?P<tab>\S+) изменено=(?P<changed>\d) экран=(?P<display>\d) подтверждение=(?P<left>\S+) "
                           r"окно=(?P<mode>\S+) .* язык=(?P<lang>\S+) сохранено_окно=(?P<saved>\S+) показ_подтверждения=(?P<shown>\d) "
                           r"применить=(?P<apply>\d)$", re.MULTILINE)


def settings_window(repo: Path) -> tuple[bool, str]:
    """Окно настроек Last Edict (Э5, roadmap §3.3) из главного меню: экран LE_SETTINGS на месте DLG_OPTIONS в графе, окно на Control.
    Режим экрана ждёт «Применить» и подтверждения: не ответили 15 с — откат к прежнему; «Оставить» — сохранено. Язык — из меню: «Применить»
    строит экраны меню заново на новом языке. Esc — «Закрыть» (событие графу)."""
    script = ("click BTN_OPTIONS; set windowMode 2; apply; wait 14000; wait 1000; set windowMode 1; apply; keep; tab Language; "
              "set language 2; apply; key 27")
    code, out = godot(repo, ["--input", script], "res://scenes/ui/UiFlow.tscn")
    rows = list(SETTINGS_LINE.finditer(out))
    steps = re.findall(r"^flow step=\d+ .* stack=(\S+) .* built=(\d+) ", out, re.M)
    want = [  # после каждого шага сценария, кроме последнего (Esc закрыл окно)
        ("Display", "0", "-", "windowed", "windowed", "0"),
        ("Display", "1", "-", "fullscreen", "windowed", "0"),      # выбран полный экран — ещё не применён
        ("Display", "1", "15", "fullscreen", "windowed", "1"),     # «Применить» — подтверждение 15 с
        ("Display", "1", "1", "fullscreen", "windowed", "1"),      # 14 с — осталась секунда
        ("Display", "0", "-", "windowed", "windowed", "0"),        # время вышло — откат
        ("Display", "1", "-", "borderless", "windowed", "0"),
        ("Display", "1", "15", "borderless", "windowed", "1"),
        ("Display", "0", "-", "borderless", "borderless", "0"),    # «Оставить»
        ("Language", "0", "-", "borderless", "borderless", "0"),
        ("Language", "0", "-", "borderless", "borderless", "0"),
        ("Display", "0", "-", "borderless", "borderless", "0"),    # язык применён — экраны заново (вкладка с начала)
    ]
    got = [(r["tab"], r["changed"] if r["tab"] != "Language" else "0", r["left"], r["mode"], r["saved"], r["shown"]) for r in rows]
    langs = [r["lang"] for r in rows]
    if (code != 0 or got != want or langs[-1] != "en" or "settings применено язык=en" not in out or len(steps) != 12
            or steps[-1][0] != "DLG_MAIN_MENU" or int(steps[10][1]) <= int(steps[9][1])):
        return False, f"окно настроек: код {code}, строки {got}, языки {langs}, шаги {steps}\n{out.strip()[-3000:]}"
    return True, ("окно настроек: из меню — режим экрана по «Применить» с подтверждением, через 15 с без ответа — откат, «Оставить» — "
                  "сохранено; язык en — экраны меню заново; Esc закрыл")


def map_settings(repo: Path) -> tuple[bool, str]:
    """Настройки в окне настроек Last Edict на карте (Э5; docs/ai-screen.md): «скорость движения противника» на шаг вверх и показ ходов ИИ
    выключен, «Применить» — файл настроек (--settings) записан, и на конце хода все показанные ходы ИИ — прыжком в конечную клетку. Затем —
    между запусками (пометка 5 прогона 14.10, «не сохраняются настройки»): запуск 1 меняет автосохранение, прокрутку, плавность за отрядом и
    фоны мест и применяет; запуск 2 с тем же файлом читает их (окно открыто без изменений), «Низкое качество» оригинала (MapSettings.Preset) и
    «Применить» — фоны 0, за отрядом и между действиями нет, слабая видеокарта. «Отмена» не пишет. День 21 партии стенда. Нет импорта — пропуск."""
    save, _, why = stand(repo, 21)
    if save is None:
        return True, f"настройки показа: пропуск — {why}"
    settings = repo / ".cache" / "ui-check" / "settings.json"
    if settings.exists():
        settings.unlink()
    code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--settings", str(settings), "--clicks",
                             "ui click BTN_OPTIONS;ui tab Game;ui set opponentSpeed 2;ui set showAiMoves 0;ui apply;ui click BTN_CLOSE;end"],
                      "res://scenes/map/MapScreen.tscn")
    moves = re.findall(r"^aimove .* прыжок=(\d)$", out, re.MULTILINE)
    saved = settings_file(settings)
    if (code != 0 or "settings применено противник=2 ходы_ИИ=0" not in out or not moves or set(moves) != {"1"}
            or saved.get("version") != 2 or saved.get("game.opponentSpeed") != 2 or saved.get("game.showAiMoves") is not False):
        return False, f"настройки показа: код {code}, файл {saved}\n{out.strip()}"
    runs = [
        ("ui set autoSave 0;ui set scrollSpeed 50;ui set stackSmoothScroll 0;ui set ambientSources 2;ui apply",
         {"game.autoSave": False, "game.scrollSpeed": 50, "game.stackSmoothScroll": False, "audio.ambientSources": 2,
          "display.lowEndVideoCard": False}),
        ("ui preset low;ui apply",
         {"game.autoSave": False, "game.scrollSpeed": 50, "game.stackSmoothScroll": False, "audio.ambientSources": 0,
          "display.lowEndVideoCard": True, "game.smoothScroll": False}),
        ("ui set autoSave 1;ui cancel",   # «Отмена» — в файл не идёт
         {"game.autoSave": False}),
    ]
    for clicks, fields in runs:
        code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--settings", str(settings), "--clicks",
                                 f"ui click BTN_OPTIONS;{clicks};ui click BTN_CLOSE"], "res://scenes/map/MapScreen.tscn")
        saved = settings_file(settings)
        opened = re.search(r"^ui click_BTN_OPTIONS .* settings вкладка=\S+ изменено=(\d)", out, re.M)
        if (code != 0 or opened is None or opened[1] != "0" or any(saved.get(k) != v for k, v in fields.items())
                or saved.get("game.opponentSpeed") != 2):
            return False, f"настройки показа: окно настроек — код {code}, «{clicks}», файл {saved}\n{out.strip()[-3000:]}"
    return True, (f"настройки показа: противник 2, ходы ИИ выключены — {len(moves)} ходов прыжком, файл записан по «Применить»; "
                  "автосохранение, прокрутка, плавность за отрядом и фоны мест — между запусками, «Низкое качество», «Отмена» не пишет")


SPELL = re.compile(r"^spell (?P<spell>\S+) в=\((?P<x>\d+),(?P<y>\d+)\) картинок=(?P<parts>\d+) кадров=(?P<frames>\d+) звук=(?P<sound>[\d.,]+)$", re.MULTILINE)


def spell_anim(repo: Path) -> tuple[bool, str]:
    """Литьё заклинания на карте (IsoSpell, docs/ai-screen.md §6): в ходе ИИ дня 35 партии стенда ИИ бросает заклинание на виду — камера к
    месту, анимация и звук (Midgard, §9), ход ИИ ждёт их конца; человек дня 30 бросает первое заклинание книги прицелом на свой отряд —
    анимация и звук в клетке литья. Отпускание без своего нажатия (хвост двойного щелчка по заклинанию книги) прицел не бросает — партия
    17.10, карта 11; бросает щелчок. Нет импорта — пропуск."""
    ai_save, _, why = stand(repo, 35)
    human_save, m, _ = stand(repo, 30)
    if ai_save is None or human_save is None:
        return True, f"литьё на карте: пропуск — {why}"
    code, out = godot(repo, ["--state", str(ai_save), "--check", "--player", STAND_PLAYER, "--clicks", "end"], "res://scenes/map/MapScreen.tscn")
    ai = [m for m in SPELL.finditer(out)]
    if code != 0 or not ai or f"aisight заклинание {ai[0]['spell']}" not in out or AITURN.search(out) is None or Seconds(ai[0]) <= 0:
        return False, f"литьё на карте (ИИ): код {code}\n{out.strip()}"
    cell = m.field_stacks(STAND_PLAYER)[0].cell
    code, out = godot(repo, ["--state", str(human_save), "--check", "--player", STAND_PLAYER,
                             "--clicks", f"panel click BTN_CAST_SPELL;ui click BTN_CAST_SPELL;aimrelease {cell};aimpress {cell}"],
                      "res://scenes/map/MapScreen.tscn")
    human = SPELL.search(out)
    held = re.search(rf"^aimrelease {cell} прицел=(?P<spell>\S+)", out, re.MULTILINE)
    if (code != 0 or human is None or f"{human['x']},{human['y']}" != cell or f"aim target {cell} брошено" not in out
            or held is None or held["spell"] == "-" or out.index(f"aim target {cell}") < held.start() or Seconds(human) <= 0):
        return False, f"литьё на карте (человек): код {code}\n{out.strip()}"
    return True, (f"литьё на карте: ИИ {ai[0]['spell']} — {ai[0]['parts']} картинка, {ai[0]['frames']} кадров, звук {ai[0]['sound']} с; "
                  f"человек {human['spell']} — {human['parts']} картинки, {human['frames']} кадров, звук {human['sound']} с")

def Seconds(match: re.Match) -> float:
    """Длительность звука литья из строки spell (запятая — десятичный знак культуры)."""
    return float(match["sound"].replace(",", "."))


BATTLE_OPEN = re.compile(r"battle open ходов_ИИ_до_игрока=\d+ ждёт_игрока=\d предметы_нап=(?P<att>\S+) предметы_защ=(?P<def>\S+) "
                         r"значков=(?P<icons>\d+) у_ходящего=(?P<mover>\d+)")


def battle_items(repo: Path) -> tuple[bool, str]:
    """Боевые предметы лидеров на экране боя с хозяином (BATTLE1/2, docs/ai-screen.md §8): бой ИИ с человеком дня 30 — у нападающего
    лидера нежити два предмета; нападение человека дня 21 — два предмета у его лидера. Значки в слотах — только у ходящего лидера
    (updateBattleItems 0x6383d0 → 0x649775, прогон 11.10 пункт 24): их столько, сколько предметов у ходящего, если он лидер, иначе 0.
    Last Edict: бой ИИ с человеком дня 23 партии стенда и нападение героя человека с зельями на руину дня 21. Нет импорта — пропуск."""
    s21, m21, why = stand(repo, 21)
    s23, _, _ = stand(repo, 23)
    if s21 is None or s23 is None:
        return True, f"предметы в бою: пропуск — {why}"
    hero, ruin = ruin_raid(m21)
    cases = [(s23, "end", None), (s21, f"arrive {hero.id} {ruin.id}", "att")]
    results = []
    for path, clicks, side in cases:
        save = f"{path.stem}: {clicks}"
        code, out = godot(repo, ["--state", str(path), "--check", "--player", STAND_PLAYER, "--clicks", clicks],
                          "res://scenes/map/MapScreen.tscn")
        opened = BATTLE_OPEN.search(out)
        items = [] if opened is None or side is None or opened[side] == "-" else opened[side].split(",")
        if (code != 0 or CSHARP_ERROR in out or opened is None or (side is not None and not items)
                or opened["icons"] != opened["mover"]):
            return False, f"предметы в бою {save}: код {code}\n{out.strip()}"
        results.append(f"{save}: {','.join(items)} — значков {opened['icons']} (у ходящего {opened['mover']})")
    return True, "предметы в бою: " + "; ".join(results)


DEATH = re.compile(r"^death (?P<stack>\S+) в=\((?P<x>\d+),(?P<y>\d+)\) кадров=(?P<frames>\d+) призыв=(?P<summon>\d) "
                   r"могила=(?P<tomb>\d) звук=(?P<sound>[\d.,]+)$", re.MULTILINE)
CSHARP_ERROR = "ERROR: System."


def stack_death(repo: Path) -> tuple[bool, str]:
    """Гибель отряда на карте (CMidAnim2Dying, docs/ai-screen.md §8): в ходе ИИ дня 23 партии стенда погибшие в боях на виду отряды —
    анимация G000UU9999DYIN8 и звуки, ход ждёт их конца; на середине гибели, где модель ставит могилу, встаёт её картинка (могила=1 хотя бы
    у одной); после боя человека на Tutorial — гибель его отряда. Необработанных исключений C# нет. Нет импорта — только Tutorial."""
    results = []
    save, _, _ = stand(repo, 23)
    if save is not None:
        code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--clicks", "end"],
                          "res://scenes/map/MapScreen.tscn")
        deaths = {m["stack"]: m for m in DEATH.finditer(out)}
        shown = len(re.findall(r"^aisight гибель(?:-могила)? ", out, re.MULTILINE))
        tombs = sum(m["tomb"] == "1" for m in deaths.values())
        if (code != 0 or CSHARP_ERROR in out or not deaths or shown != len(deaths)
                or any(m["frames"] != "16" for m in deaths.values()) or tombs == 0):
            return False, f"гибель отряда (ход ИИ): код {code}\n{out.strip()}"
        results.append(f"ход ИИ — {len(deaths)} гибели ({', '.join(deaths)}), с могилой {tombs}")
    code, out = godot(repo, ["--scenario", "Tutorial", "--check", "--player", "S001PL0009", "--clicks", "30,9;42,18;42,18"],
                      "res://scenes/map/MapScreen.tscn")
    death = DEATH.search(out)
    if code != 0 or CSHARP_ERROR in out or death is None or death["stack"] != "S001KC0000":
        return False, f"гибель отряда (бой человека): код {code}\n{out.strip()}"
    results.append(f"бой человека — {death['stack']}, звук {death['sound']} с")
    return True, "гибель отряда: " + "; ".join(results)


CURSOR = re.compile(r"^cursor (?P<cell>\d+,\d+) курсор=(?P<name>\S+) картинка=(?P<size>\S+)$", re.MULTILINE)

MENU_CURSOR = re.compile(r"flow step=\d+ .* stack=(?P<stack>\S+) .* cursor=(?P<cursor>\S+)$", re.MULTILINE)


def menu_cursors(repo: Path) -> tuple[bool, str]:
    """Курсор меню (docs/ui-windows.md, «Курсор окна»): у экранов меню и окна поверх них в разметке курсор _CUDEFAUL (DIALOG, поле 6) —
    кинжал Interf.ff, а не системная стрелка; верхний экран отвечает за курсор (0x53ca03 → 0x5308d5). Проход «назад и окно поверх»
    до выхода и «до брифинга»: после каждого шага курсор — ui:_CUDEFAUL."""
    shown = []
    for name in ("назад и окно поверх", "до брифинга"):
        steps = next(c[2] for c in FLOW_CASES if c[0] == name)
        steps = [s for s in steps if s[1] != "-"]   # выход из игры — курсора уже нет
        code, out = godot(repo, ["--input", "; ".join(s[0] for s in steps)], "res://scenes/ui/UiFlow.tscn")
        got = [(m["stack"], m["cursor"]) for m in MENU_CURSOR.finditer(out)]
        if code != 0 or len(got) != len(steps) or any(c != "ui:_CUDEFAUL" for _, c in got):
            return False, f"курсор меню «{name}»: код {code}, {got}\n{out.strip()}"
        shown += [st.split(">")[-1] for st, _ in got]
    return True, f"курсор меню: кинжал ui:_CUDEFAUL на {len(set(shown))} экранах ({', '.join(sorted(set(shown)))})"


def map_cursors(repo: Path) -> tuple[bool, str]:
    """Курсоры карты (CTaskStrat/CTaskSelectUnit, docs/ai-screen.md §10): день 21 партии стенда — над своим отрядом SELECTUNIT, выбрав
    его — MANAGESTK, над ближним чужим отрядом BATTLE, свободная проходимая клетка рядом MOVE, гора DEFAULT; картинки обрезаны до размера
    курсора Godot (не больше 256×256). Нет импорта — пропуск."""
    save, why = check_save(repo, "Power to Corrupt", 21)
    if save is None:
        return True, f"курсоры карты: пропуск — {why}"
    m = ui_check_maps.load(save)
    own = m.field_stacks("S073PL000a")[0]
    foe = m.nearest_foe(own.owner, own.x, own.y)
    move, rock = "%d,%d" % m.free_near(own.x, own.y), "%d,%d" % m.landmark_cells(seen_by=own.owner)[0]
    want = [(own.cell, "SELECTUNIT"), (own.cell, "MANAGESTK"), (foe.cell, "BATTLE"), (move, "MOVE"), (rock, "DEFAULT")]
    code, out = godot(repo, ["--state", str(save), "--check", "--player", own.owner, "--clicks",
                             f"cursor {own.cell};{own.cell};cursor {own.cell};cursor {foe.cell};cursor {move};cursor {rock}"],
                      "res://scenes/map/MapScreen.tscn")
    got = [(m["cell"], m["name"], m["size"]) for m in CURSOR.finditer(out)]
    small = all(s != "-" and max(int(v) for v in s.split("x")) <= 256 for _, _, s in got)
    if code != 0 or CSHARP_ERROR in out or [(c, n) for c, n, _ in got] != want or not small:
        return False, f"курсоры карты: код {code}, {got}\n{out.strip()}"
    return True, "курсоры карты: " + ", ".join(f"{c} {n}" for c, n, _ in got)


CARD = re.compile(r"^card (?P<cell>\d+,\d+) карточка=(?P<screen>\S+)(?: текст=(?P<text>\S*))?"
                  r"(?: лидер=(?P<leader>\S+) нет_кадров=(?P<missing>\S+) портреты=(?P<faces>\d+) в_рамке=(?P<framed>\d+) рамки=(?P<borders>\S+))?$",
                  re.MULTILINE)


def map_cards(repo: Path) -> tuple[bool, str]:
    """Карточки правой кнопки на карте (DLG_R_C_*, docs/ai-screen.md §10): день 21 партии стенда — свой отряд и ближний чужой —
    DLG_R_C_STACK с именем лидера, гора — DLG_R_C_LANDMARK, пустая клетка — окна нет. Нет импорта — пропуск."""
    save, m, why = stand(repo, 21)
    if save is None:
        return True, f"карточки карты: пропуск — {why}"
    own = m.field_stacks(STAND_PLAYER)[0]
    foe = m.nearest_foe(own.owner, own.x, own.y)
    rock, empty = "%d,%d" % m.landmark_cells(seen_by=STAND_PLAYER)[0], "%d,%d" % m.empty_cell(seen_by=STAND_PLAYER)
    code, out = godot(repo, ["--state", str(save), "--check", "--player", own.owner,
                             "--clicks", f"card {own.cell};card {foe.cell};card {rock};card {empty}"], "res://scenes/map/MapScreen.tscn")
    matches = {m["cell"]: m for m in CARD.finditer(out)}
    got = {c: (m["screen"], m["text"] or "") for c, m in matches.items()}
    want = {own.cell: "DLG_R_C_STACK", foe.cell: "DLG_R_C_STACK", rock: "DLG_R_C_LANDMARK", empty: "-"}
    # Карточка отряда (11.10, замечание 7): лидер — картина во весь рост из Events (0x57c0a9), а не круглый FACEB; все кадры нашлись.
    pictures = all((matches[c]["leader"] or "").startswith("gfx:Events/") and matches[c]["missing"] == "-"
                   for c in (own.cell, foe.cell) if c in matches)
    if (code != 0 or CSHARP_ERROR in out or {c: s for c, (s, _) in got.items()} != want
            or not all(got[c][1] for c in (own.cell, foe.cell, rock)) or not pictures):
        return False, f"карточки карты: код {code}, {got}\n{out.strip()}"
    ok, framed = card_frames(repo)
    if not ok:
        return False, framed
    ok, battle = battle_card(repo)
    if not ok:
        return False, battle
    return True, "карточки карты: " + ", ".join(f"{c} {s} {t}".rstrip() for c, (s, t) in got.items()) + \
        f"; лидер {matches[own.cell]['leader']}; {framed}; {battle}"


def card_frames(repo: Path) -> tuple[bool, str]:
    """Рамки портретов карточек (прогон 2 пользователя 11.10, снимок 31: у портретов карточки столицы не было рамок): день 5 живой партии,
    столица Кланов — клетка 50,30 (DLG_R_C_CITY: гость и гарнизон, в гарнизоне большой), вход — 49,30 (DLG_R_C_STACK гостя). Каждый портрет —
    в рамке Icons BORDERUNITSMALL 84×99, большой — BORDERUNITLARGE 164×99 (0x57c443 → 0x5b6559, таблица 0x7ae938); кадры все нашлись.
    Last Edict: день 5 партии стенда — столица Кланов (гарнизон с большим юнитом) и свой отряд на карте."""
    save, m, why = stand(repo, 5)
    if save is None:
        return True, f"рамки карточек: пропуск — {why}"
    capital, own = m.capital(STAND_PLAYER), m.field_stacks(STAND_PLAYER)[0]
    town = f"{capital.x + 1},{capital.y + 1}"
    code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--clicks", f"card {town};card {own.cell}"],
                      "res://scenes/map/MapScreen.tscn")
    got = {m["cell"]: m for m in CARD.finditer(out)}
    city, stack = got.get(town), got.get(own.cell)

    def framed(m, *borders: str) -> bool:
        return (m is not None and m["missing"] == "-" and int(m["faces"]) > 0 and m["faces"] == m["framed"]
                and all(b in m["borders"] for b in borders))

    if (code != 0 or CSHARP_ERROR in out or city is None or city["screen"] != "DLG_R_C_CITY" or stack is None
            or stack["screen"] != "DLG_R_C_STACK" or not framed(city, "BORDERUNITSMALL:84x99", "BORDERUNITLARGE:164x99")
            or not framed(stack, "BORDERUNITSMALL:84x99")):
        return False, f"рамки карточек: код {code}\n{out.strip()}"
    return True, f"рамки: город {city['framed']} из {city['faces']} ({city['borders']}), отряд {stack['framed']} из {stack['faces']}"


BATTLE_CARD = re.compile(r"^battle card карточка=(?P<kind>\S+) таб=(?P<tab>\d+) нет_кадров=(?P<missing>\S+) статы=(?P<stats>\S*)$", re.MULTILINE)


def battle_card(repo: Path) -> tuple[bool, str]:
    """Карточка воина в бою с карты по правой кнопке (прогон 2 пользователя 11.10, снимок 33): та же, что на карте (UnitCardLogic.InBattle),
    место табуляции 110, «Опыт:» табуляция «N / M», «Иммунитет» и «Стойкость» — каждая своей строкой со значением. Бой A (32,38) против
    A (34,38) из сохранения ai-q14-crit; в Last Edict — свой отряд дня 21 партии стенда против ближнего чужого."""
    save, m, why = stand(repo, 21)
    if save is None:
        return True, f"карточка в бою: пропуск — {why}"
    own = m.field_stacks(STAND_PLAYER)[0]
    foe = m.nearest_foe(own.owner, own.x, own.y)
    code, out = godot(repo, ["--state", str(save), "--check", "--player", own.owner, "--clicks", f"fight {own.cell} {foe.cell} 1;battle card"],
                      "res://scenes/map/MapScreen.tscn")
    m = BATTLE_CARD.search(out)
    lines = m["stats"].split("|") if m else []
    # Без CSHARP_ERROR, как у «раскладки боя»: бой остаётся открытым до выхода, и GameAudio.FadeThen при выходе падает на снятом узле
    # (ObjectDisposedException, не связано с карточкой).
    xp = next((l for l in lines if l.startswith("Опыт:→")), "")
    if (code != 0 or m is None or m["kind"] != "общая" or m["tab"] != "110" or m["missing"] != "-"
            or not re.fullmatch(r"Опыт:→\d+_/_\d+", xp)
            or not any(re.fullmatch(r"Иммунитет:→.+", l) for l in lines) or not any(re.fullmatch(r"Стойкость:→.+", l) for l in lines)):
        return False, f"карточка в бою: код {code}\n{out.strip()}"
    return True, f"карточка в бою: {m['kind']}, табуляция {m['tab']}, {xp.replace('_', ' ').replace('→', ' ')}"


PALETTE = re.compile(r"^(?P<step>palette|minimap_\S+) палитра=(?P<screen>\S+) ресурсы=(?P<res>\d) знамёна=(?P<shown>\d+)/(?P<all>\d+) "
                     r"мини=(?P<mini>\d)(?: кнопка_над_окном=(?P<covered>\d))?$", re.MULTILINE)
NEXT = re.compile(r"^next выбран=(?P<sel>\S+) кнопка=(?P<button>\d)$", re.MULTILINE)


def map_palette(repo: Path) -> tuple[bool, str]:
    """Палитра карты (замечания 36, 38 прогона 11.10; MapPalette): в начале партии полоса ресурсов видна (ответ пользователя 11.10),
    TOG_RESPOPUP её прячет и показывает, TOG_BANNERS — знамёна, BTN_RES_PAL меняет мини-карту на владения DLG_ISO_LAND и обратно;
    угловые кнопки над окном мини-карты (TOG_BANNERS, BTN_RES_PAL) мини-карта не перехватывает. Знамёна сохраняются в настройках порта
    (--settings): включены в файле — включены при старте, выключили — в файле false. Сценарий карты пользователя — из data/scenarios.
    «Следующий лидер» (замечание 32) — кнопка доступна, отряд выбран."""
    scenario = ["--scenario", "'A Return To Simpler Times'", "--check", "--player", "S001PL0001", "--humans", "S001PL0001"]
    code, out = godot(repo, [*scenario,
                             "--clicks", "palette;ui click TOG_RESPOPUP;palette;ui click TOG_RESPOPUP;palette;ui click TOG_BANNERS;palette;"
                             "ui click BTN_RES_PAL;palette;ui click BTN_RES_PAL;palette;minimap 10,10;minimap 2,110;minimap 70,70;next"],
                      "res://scenes/map/MapScreen.tscn")
    rows = [(m["step"], m["screen"], m["res"], m["shown"] == m["all"] and int(m["all"]) > 0, m["shown"] == "0", m["mini"], m["covered"])
            for m in PALETTE.finditer(out)]
    want = [("palette", "DLG_ISO_PAL", "1", False, True, "1", None),
            ("palette", "DLG_ISO_PAL", "0", False, True, "1", None),
            ("palette", "DLG_ISO_PAL", "1", False, True, "1", None),
            ("palette", "DLG_ISO_PAL", "1", True, False, "1", None),
            ("palette", "DLG_ISO_LAND", "1", True, False, "0", None),
            ("palette", "DLG_ISO_PAL", "1", True, False, "1", None),
            ("minimap_10,10", "DLG_ISO_PAL", "1", True, False, "1", "1"),
            ("minimap_2,110", "DLG_ISO_PAL", "1", True, False, "1", "1"),
            ("minimap_70,70", "DLG_ISO_PAL", "1", True, False, "1", "0")]
    nxt = NEXT.search(out)
    if code != 0 or CSHARP_ERROR in out or rows != want or nxt is None or nxt["sel"] == "-" or nxt["button"] != "1":
        return False, f"палитра карты: код {code}, {rows}\n{out.strip()}"
    # Знамёна из сохранённой настройки: файл с Banners=true — знамёна видны сразу и переключатель включён; щелчок — выключены и в файле.
    with tempfile.TemporaryDirectory(prefix="d2-banners-") as tmp:
        settings = Path(tmp) / "settings.json"
        settings.write_text(json.dumps({"Banners": True}), encoding="utf-8")
        # Там же — полоса золота дипломатии видна и при спрятанной полосе ресурсов; после «Назад» полоса ресурсов остаётся спрятанной.
        code2, out2 = godot(repo, [*scenario, "--settings", str(settings),
                                   "--clicks", "palette;ui click TOG_BANNERS;palette;ui click TOG_RESPOPUP;panel click BTN_DIPLOMACY;"
                                   "diplo 1;panel click BTN_BACK;palette"], "res://scenes/map/MapScreen.tscn")
        saved = settings_file(settings).get("display.banners")   # файл схемы 1 ({"Banners": true}) игра переписала схемой 2
    rows2 = [(m["res"], m["shown"] == m["all"] and int(m["all"]) > 0, m["shown"] == "0") for m in PALETTE.finditer(out2)]
    gold = re.search(r"^diplo 1 .*полоса_золота=(\d)", out2, re.MULTILINE)
    if (code2 != 0 or CSHARP_ERROR in out2 or rows2 != [("1", True, False), ("1", False, True), ("0", False, True)]
            or saved is not False or gold is None or gold[1] != "1"):
        return False, f"палитра карты: знамёна из настроек — код {code2}, {rows2}, в файле {saved}\n{out2.strip()}"
    return True, (f"палитра карты: полоса ресурсов видна при старте и по переключателю, знамёна по переключателю "
                  f"(знамён {PALETTE.search(out)['all']}) и из сохранённой настройки (включены при старте, выключены — в файле false), "
                  f"полоса золота в дипломатии видна и при спрятанной полосе ресурсов, мини-карта ↔ владения, угловые кнопки над окном мини-карты — панели; Tab выбрал {nxt['sel']}")


UNDER_FOG = re.compile(r"^order aim картинок=(?P<n>\d+) .*под_туманом=(?P<under>\d+)/(?P<fogged>\d+)", re.MULTILINE)
FRAME = re.compile(r"^frame рамка=(?P<outline>\S+) мини_фон=(?P<mini>\S+) цели=(?P<goals>\d+) кругов=(?P<circles>\d+)$", re.MULTILINE)


# Формы окна Э5 (Р13): основное 16:9, 16:10 (Steam Deck), 21:9, 4:3 — окно проверки без окна и холст в точках разметки 800×600.
ASPECTS = [("16:9", "1280x720", 1067), ("16:10", "1280x800", 960), ("21:9", "2560x1080", 1422), ("4:3", "1024x768", 800)]
FIT_LAYOUT = re.compile(r"^layout холст=(?P<w>\d+)x(?P<h>\d+) карта=(?P<map>\S+) вьюпорт=(?P<viewport>\S+) ресурсы=(?P<res>\S+) "
                        r"панель=(?P<panel>\S+) .* панели=(?P<views>\S+)$", re.MULTILINE)
FIT_BATTLE = re.compile(r"^battle fit поле=(?P<mx>\d+),(?P<my>\d+) холст=(?P<w>\d+)x(?P<h>\d+) края=(?P<edges>\d)$", re.MULTILINE)
FIT_SCREEN = re.compile(r"^fit screen=(?P<id>\S+) политика=(?P<fit>\S+) холст=(?P<w>\d+)x(?P<h>\d+) масштаб=\S+ угол=\S+ паспарту=(?P<border>\S+)$",
                        re.MULTILINE)


def screen_fit(repo: Path) -> tuple[bool, str]:
    """Вписывание экранов в окно любой формы (Э5, Р13; ui/layout.json, UiLayout): на 16:9, 16:10, 21:9 и 4:3 — карта на холсте формы окна
    (место карты IMG_MAIN растёт на весь прирост, вьюпорт карты — его размера в точках окна, панели IMG_RESOURCE и IMG_PALETTE и их экраны — у правого
    края), бой (поле прироста — по половине с каждой стороны, края — растяжкой фона, на 4:3 — нет), главное меню — по центру в паспарту
    BORDER_1280 (дыра 800×600 точка в точку). Сценарий карты — Mercantile, бой — бой-образец."""
    runs = []
    for _, size, _ in ASPECTS:
        runs += [(["--scenario", "Mercantile", "--check", "--player", "S135PL0001", "--window-size", size], "res://scenes/map/MapScreen.tscn"),
                 ([*BATTLE_SAMPLE, "--check", "--window-size", size], BATTLE_SCENE),
                 (["--screen", "DLG_MAIN_MENU", "--fit", "--window-size", size], "res://scenes/ui/UiScreen.tscn")]
    outs = godot_all(repo, runs)
    results = []
    for i, (name, size, width) in enumerate(ASPECTS):
        (cm, om), (cb, ob), (cs, os_) = outs[3 * i:3 * i + 3]
        lay, bat, scr = FIT_LAYOUT.search(om), FIT_BATTLE.search(ob), FIT_SCREEN.search(os_)
        right = width - 160
        want_map = (str(width), "600", f"0,0:{right}x600", f"{right},0:160x203", f"{right},203:160x397")
        views = set(lay["views"].split(";")) if lay else set()
        # Вьюпорт карты — в точках окна (масштаб карты, ответ пользователя 24.10): место карты × масштаб окна, до точки.
        vw, vh = (int(v) for v in size.split("x"))
        k = vh / 600
        port = [int(v) for v in lay["viewport"].split("x")] if lay else [0, 0]
        if (cm != 0 or lay is None
                or (lay["w"], lay["h"], lay["map"], lay["res"], lay["panel"]) != want_map
                or abs(port[0] - right * k) > 1.5 or abs(port[1] - 600 * k) > 1.5
                or not {f"DLG_ISO_PAL@{right},0", f"DLG_STRATEGIC@{right},203", "DLG_ISO@0,0", "DLG_ISO_RESPOPUP@0,0"} <= views):
            return False, f"вписывание {name} ({size}): карта — код {cm}, {lay and lay.group(0)}\n{om.strip()[-2000:]}"
        margin = max(0, math.ceil((600 * vw / vh - 800) / 2 - 0.01))   # поле боя покрывает холст с запасом в точку
        if (cb != 0 or bat is None or (int(bat["mx"]), bat["my"], bat["h"]) != (margin, "0", "600")
                or int(bat["w"]) != 800 + 2 * margin or bat["edges"] != ("1" if margin else "0")):
            return False, f"вписывание {name} ({size}): бой — код {cb}, {bat and bat.group(0)}\n{ob.strip()[-2000:]}"
        if cs != 0 or scr is None or (scr["fit"], scr["w"], scr["h"], scr["border"]) != ("Letterbox", "800", "600", "BORDER_1280@-240,-212"):
            return False, f"вписывание {name} ({size}): меню — код {cs}, {scr and scr.group(0)}\n{os_.strip()[-2000:]}"
        results.append(f"{name} — карта {right}×600 + панели у края, поле боя ±{margin}")
    return True, "вписывание экранов: " + "; ".join(results) + "; меню — в паспарту BORDER_1280"


def map_frame(repo: Path) -> tuple[bool, str]:
    """Рамка и слои экрана карты (docs/ai-screen.md §11): над картой — рамка края поля ISO_OUTLINE, фон мини-карты чёрный; квадрат
    прицела обычного заклинания (base:spell/ice-spirits, оригинала g000ss0086, слой 895) на клетке под туманом лежит под туманом, туман (g000ss0052, SPLAREAFOG 1590) —
    над ним. День 30 партии стенда, ближняя к своему отряду клетка под туманом Кланов. Нет импорта — пропуск."""
    save, m, why = stand(repo, 30)
    if save is None:
        return True, f"рамка и слои карты: пропуск — {why}"
    own = m.field_stacks(STAND_PLAYER)[0]   # вид карты встаёт на первом своём отряде — клетка тумана рядом с ним на экране
    fog = "%d,%d" % m.fogged_cell(STAND_PLAYER, own.x, own.y)
    code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--clicks",
                             f"frame;aim base:spell/ice-spirits;hover {fog};order aim;aim base:spell/divis-nocte;hover {fog};order aim"],
                      "res://scenes/map/MapScreen.tscn")
    frame = FRAME.search(out)
    orders = [(int(m["n"]), int(m["under"]), int(m["fogged"])) for m in UNDER_FOG.finditer(out)]
    ok = (code == 0 and CSHARP_ERROR not in out and frame is not None and frame["outline"] == "ISO_OUTLINE"
          and frame["mini"] == "000000" and len(orders) == 2
          and orders[0][2] > 0 and orders[0][1] == orders[0][2]   # обычное — все клетки под туманом легли под него
          and orders[1][2] > 0 and orders[1][1] == 0)               # туман — над туманом
    if not ok or frame is None:
        return False, f"рамка и слои карты: код {code}, рамка {frame and frame.group(0)}, прицел {orders}\n{out.strip()}"
    # Круг цели сценария (SYM_OBJECTIVE): Mercantile глазами Империи — у трёх деревень-целей по кругу (сценарий порта, без корпуса).
    code, goals_out = godot(repo, ["--scenario", "Mercantile", "--check", "--player", "S135PL0001", "--clicks", "frame"],
                            "res://scenes/map/MapScreen.tscn")
    goals = FRAME.search(goals_out)
    if code != 0 or goals is None or goals["goals"] != "3" or goals["circles"] != "3":
        return False, f"рамка и слои карты: круг цели — код {code}, {goals and goals.group(0)}\n{goals_out.strip()}"
    return True, (f"рамка и слои карты: {frame['outline']}, мини-карта #{frame['mini']}, квадрат под туманом {orders[0][1]}/{orders[0][2]}, "
                  f"туман над туманом {orders[1][2] - orders[1][1]}/{orders[1][2]}, круги целей Mercantile {goals['circles']}/{goals['goals']}")


def noble(repo: Path) -> tuple[bool, str]:
    """Вор у цели (CEffectNobleChoose, docs/ai-screen.md §11): день 21 партии стенда — вор Кланов приходит к ближнему чужому отряду: ANOBLE
    между ними и звук SNOBLE, после анимации — окно действий вора. Нет импорта — пропуск."""
    save, m, why = stand(repo, 21)
    if save is None:
        return True, f"вор у цели: пропуск — {why}"
    thief = next(st for st in m.field_stacks(STAND_PLAYER) if "thief" in st.leader)
    target = m.nearest_foe(thief.owner, thief.x, thief.y)
    code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--clicks", f"arrive {thief.id} {target.id}"],
                      "res://scenes/map/MapScreen.tscn")
    shown = re.search(rf"^noble {thief.id} .* кадров=(\d+)", out, re.M)
    if (code != 0 or CSHARP_ERROR in out or shown is None or "fx Midgard/SNOBLE " not in out
            or not re.search(rf"^arrive {thief.id} {target.id} окна=DLG_THIEF_ACTIONS ", out, re.M)):
        return False, f"вор у цели: код {code}\n{out.strip()}"
    return True, f"вор у цели: ANOBLE {shown[1]} кадров, SNOBLE, затем окно действий"


def upgrade_leader(repo: Path) -> tuple[bool, str]:
    """Окно выбора навыка лидера (DLG_UPGRADE_LEADER, CUpgradeLdrInterf): новая партия из меню (Greenskins) — у героя отложены выборы
    навыка, окно за окном начала хода; «ОК» берёт выбранный навык, пока выборов не останется; кадры всех навыков нашлись."""
    code, out = godot(repo, ["--check", "--input", "; ".join(CONTENT_STEPS + ["click BTN_CONTINUE"]), "--clicks", "ok"],
                      "res://scenes/ui/UiFlow.tscn")
    offers = re.findall(r"^upgrade leader stack=(\S+) pending=(\d+) offer=(\d+)", out, re.M)
    taken = re.findall(r"^upgrade accept навык отряд=(\S+) навыков=\d+ выбран=(\S+) взят=(\S+) отложено=(\d+)$", out, re.M)
    if (code != 0 or CSHARP_ERROR in out or not offers or "нет_кадров=" in out or len(taken) != int(offers[0][1])
            or any(chosen != took or took == "-" for _, chosen, took, _ in taken) or taken[-1][3] != "0"):
        return False, f"выбор навыка лидера: код {code}, окон {offers}, взято {taken}\n{out.strip()[-3000:]}"
    return True, (f"выбор навыка лидера: герой {offers[0][0]} — {len(taken)} выбора по окну, навыков в первом {offers[0][2]}, "
                  f"взято: {', '.join(t[2] for t in taken)}; выборов не осталось")

def briefing_scroll(repo: Path) -> tuple[bool, str]:
    """«Задание» в партии (docs/ui-windows.md §11): автопрокрутка истории — 1500 мс ждёт, затем точка за 2 тика по 66 мс; кнопка
    BTN_SCROLL_UP — ещё 10 точек. «Power to Corrupt» (сценарий порта, история есть)."""
    code, out = godot(repo, ["--check", "--scenario", "Power to Corrupt", "--player", "S073PL000a", "--clicks",
                             "ok;ui click BTN_REPORT;ui wait 1400;ui wait 3000;ui click BTN_SCROLL_UP;ui press 700,20;ui wait 3000;"
                             "ui click BTN_CONTINUE"],
                      "res://scenes/map/MapScreen.tscn")
    got = re.findall(r"^ui (\S+) .*?прокручено=(\d+)", out, re.MULTILINE)
    want = [("click_BTN_REPORT", "0"), ("wait_1400", "0"), ("wait_3000", "21"), ("click_BTN_SCROLL_UP", "31")]
    # Седьмой круг: щелчок вне рамки текста — сразу к концу (низ текста на 76 от верха рамки), дальше автопрокрутка стоит.
    end = got[4:6]
    if (code != 0 or got[:4] != want or len(end) != 2 or end[0][0] != "press_700,20" or int(end[0][1]) <= 31
            or end[1] != ("wait_3000", end[0][1])):
        return False, f"автопрокрутка «Задания»: код {code}, шаги {got}\n{out.strip()}"
    # Восьмой круг: любая клавиша, кроме служебных (0x48201d), — сразу к концу; Esc — горячая клавиша «Продолжить».
    code, keyed = godot(repo, ["--check", "--scenario", "Power to Corrupt", "--player", "S073PL000a", "--clicks",
                               "ok;ui click BTN_REPORT;ui key 81;ui key 27"], "res://scenes/map/MapScreen.tscn")
    keys = re.findall(r"^ui (key_\d+) .*?окна=(\S+)(?:.*?прокручено=(\d+))?", keyed, re.MULTILINE)
    if code != 0 or len(keys) != 2 or keys[0][2] != end[0][1] or keys[1][1] != "-":
        return False, f"клавиши «Задания»: код {code}, шаги {keys}, конец {end[0][1]}\n{keyed.strip()}"
    return True, (f"автопрокрутка «Задания»: 1,4 с — 0 точек, ещё 3 с — 21, кнопка — 31, щелчок вне рамки — конец ({end[0][1]}); "
                  "клавиша Q — конец, Esc — закрыто")


def battle_playback(repo: Path) -> tuple[bool, str]:
    """Показ боя по действиям без окна (D2_BATTLE_PLAYBACK=1, бой-образец; замечания 12, 13, 25, 35 прогона 11.10): замах и удар —
    один проход, затем покой (повторов кадров действия 0); синхронная часть шага (сборка поля, модель, подготовка картинок) — меньше
    150 мс (прежде первый замах и удар разбирали PNG в главном потоке: 0,39 и 0,59 с); шаг начинается не раньше плана и не позже чем на
    50 мс (таймер дерева отсчитывал от начала долгого кадра и срабатывал раньше); павший остаётся в боковой панели; трупы — у ног места.
    Прогон 2 (11.10): кости на поле у каждого павшего (DEAD_HUMAN_* у любой расы, пункт 4), на портрете каждого павшего — череп
    MASKDEAD* (пункт 3); кадры в видеопамяти обрезаны по занятой части — после боя меньше 60 МБ (прежде холсты 800×600 целиком, ~150 МБ)."""
    with scene_env(D2_BATTLE_PLAYBACK="1"):
        code, out = godot(repo, ["--check"], "res://scenes/battle/BattleScreen.tscn")
    steps = re.findall(r"^battle step \S+ поле=[\d.]+мс синхронно=(?P<sync>[\d.]+)мс показ=\d+мс опоздание=(?P<late>-?[\d.]+)мс", out, re.M)
    line = re.search(r"^battle playback шагов=(?P<steps>\d+) действий=(?P<acts>\d+) повторов=(?P<rep>\d+) синхронно_макс=(?P<sync>\d+) "
                     r"портреты=(?P<faces>\S*) трупов=(?P<corpses>\d+) у_ног=(?P<feet>\d+) костей_на_поле=(?P<bones>\d+) "
                     r"черепов=(?P<skulls>\d+) видеопамять_мб=(?P<vram>[\d.]+)", out, re.M)
    if code != 0 or line is None or not steps:
        return False, f"показ боя: код {code}, шагов {len(steps)}\n{out.strip()}"
    syncs = [float(a) for a, _ in steps]
    lates = [float(b) for _, b in steps]
    fallen = [f for f in line["faces"].split(",") if f.endswith(":павший")]
    if (int(line["acts"]) == 0 or int(line["rep"]) != 0 or max(syncs) >= 150 or min(lates) < 0 or max(lates) > 50
            or not fallen or line["corpses"] != line["feet"] or int(line["corpses"]) == 0 or line["bones"] != line["corpses"]
            or int(line["skulls"]) != len(fallen) or float(line["vram"]) >= 60):
        return False, f"показ боя: синхронно {syncs}, опоздание {lates}\n{line.group(0)}"
    return True, (f"показ боя: шагов {len(steps)}, действий {line['acts']}, повторов 0, синхронно до {max(syncs):.0f} мс, "
                  f"опоздание {min(lates):.0f}…{max(lates):.0f} мс, павших в панели {len(fallen)} (черепов {line['skulls']}), "
                  f"трупов у ног {line['feet']}/{line['corpses']}, костей на поле {line['bones']}, кадры в видеопамяти {line['vram']} МБ")


# Анимации, листы которых держат ключ в индексе 0 палитры при чёрном цвете (opacity 255): атака гоблина и G000UU8023 (пункт 2 прогона 2).
BLACK_KEY_ANIMS = ["G000UU5017HMOVA1A00", "G000UU5017HMOVA1D00", "G000UU8023HHITA1A00", "G000UU8023HMOVA1A00",
                   "G000UU8023IDLEA1A00", "G000UU8023STILA1A00"]


def black_key_frames(repo: Path) -> tuple[bool, str]:
    """Кадры с ключом прозрачности в индексе палитры (tools/ui-assets/ff_extract.py, transparent_index из -IMAGES.OPT): вокруг атакующего
    гоблина не должно быть непрозрачного чёрного прямоугольника (снимок 32). Числа: непрозрачных точек ровно (0,0,0) в кадрах — 0 (прежде
    до 3653 на кадр), доля прозрачных точек в занятом прямоугольнике кадра — не меньше 20 % (прежде 0: весь прямоугольник непрозрачен)."""
    import numpy as np
    from PIL import Image
    root = Path(os.environ.get("D2_ASSETS_DIR") or repo / "assets") / "gfx" / "BatUnits" / "anims"
    if not root.exists():
        return True, "ключ в индексе палитры: пропуск — нет графики"
    worst_black, worst_share, frames = 0, 1.0, 0
    for anim in BLACK_KEY_ANIMS:
        for f in sorted((root / anim).glob("*.webp")):
            a = np.array(Image.open(f).convert("RGBA"))
            opaque = a[..., 3] > 0
            if not opaque.any():
                continue
            ys, xs = np.nonzero(opaque)
            box = opaque[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
            worst_black = max(worst_black, int((opaque & (a[..., :3].sum(-1) == 0)).sum()))
            worst_share = min(worst_share, 1 - box.mean())
            frames += 1
    if frames == 0 or worst_black > 0 or worst_share < 0.2:
        return False, f"ключ в индексе палитры: кадров {frames}, непрозрачного чёрного до {worst_black}, прозрачных в рамке от {worst_share:.0%}"
    return True, f"ключ в индексе палитры: {frames} кадров атаки гоблина и G000UU8023 — непрозрачного чёрного 0, прозрачных в рамке от {worst_share:.0%}"


def battle_layout(repo: Path) -> tuple[bool, str]:
    """Раскладка экрана боя (docs/ui-windows.md §11–§12, снимки 28.09): свой отряд дня 21 партии стенда против ближнего чужого.
    Без зеркала — обе панели (ответ пользователя 11.10): справа отряд врага, слева свой; пока видны обе, переключатель TOG_RIGHTUNITS их
    не переставляет (0x638035/0x638298(1), пометка 1 партии 14.10); «Поменяться сторонами» (FlippedBattle) — сдвиг +173, фон с 0, панель слева.
    Нет импорта — пропуск."""
    save, m, why = stand(repo, 21)
    if save is None:
        return True, f"раскладка боя: пропуск — {why}"
    own = m.field_stacks(STAND_PLAYER)[0]
    foe = m.nearest_foe(own.owner, own.x, own.y)
    fight = f"fight {own.cell} {foe.cell}"
    code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--clicks",
                             f"{fight} 1;battle ui click TOG_RIGHTUNITS;battle hover 10,200"], "res://scenes/map/MapScreen.tscn")
    lines = re.findall(r"^(?:fight|battle) .*$", out, re.MULTILINE)
    lines = [l for l in lines if l.startswith(("fight ", "battle ui", "battle hover"))]
    # Обе панели сразу (ответ пользователя 11.10, BattleScene.BothPanels): враг справа, своя группа слева с рамкой ходящего; переключатель
    # группы панелей не переставляет — свой отряд всегда на своей стороне (пометка 1 партии 14.10); SPOT ничего не прячет и не открывает.
    want = [["зеркало=0", "сдвиг=0", "фон=-150", "панель_нап=L", "панель_защ=R", "рамки=PLYSMALL"],
            ["панель_нап=L", "панель_защ=R", "PLYSMALL"],
            ["панель_нап=L", "панель_защ=R", "spot=1"]]
    if code != 0 or len(lines) != 3 or any(w not in l for l, ws in zip(lines, want) for w in ws):
        return False, f"раскладка боя: код {code}\n" + "\n".join(lines) + f"\n{out.strip()}"
    settings = repo / ".cache" / "ui-check" / "flip-settings.json"
    settings.parent.mkdir(parents=True, exist_ok=True)
    settings.write_text('{"FlippedBattle": true}', encoding="utf-8")
    code, out = godot(repo, ["--state", str(save), "--check", "--player", STAND_PLAYER, "--settings", str(settings), "--clicks", f"{fight} 2"],
                      "res://scenes/map/MapScreen.tscn")
    flipped = re.search(r"^fight .*$", out, re.MULTILINE)
    if code != 0 or flipped is None or not all(w in flipped[0] for w in ["зеркало=1", "сдвиг=173", "фон=0", "панель_защ=L"]):
        return False, f"раскладка боя, зеркало: код {code}\n{out.strip()}"
    return True, "раскладка боя: обе панели — враг справа, свой слева, переключатель их не переставляет; зеркало — +173, фон 0, панель врага слева"


SIDES = re.compile(r"(?P<what>[а-я_]+):(?P<mover>\S+):(?P<whose>свой|чужой) свои=(?P<own>[LR-]) враг=(?P<foe>[LR-]) лица=L=(?P<left>\S+),R=(?P<right>\S+)")


def battle_sides(repo: Path) -> tuple[bool, str]:
    """Стороны панелей и крупных портретов (пометка 1 партии 14.10 «в ход лекаря панели меняются местами»; ответ пользователя о крупных
    портретах): бой партии «A Return To Simpler Times» S001KC0000 против S001KC001a, свой отряд — два сквайра и две послушницы.
    На ходу своего бойца, своего лекаря и врага свой отряд — в левой панели, враг — в правой (0x638035, 0x638298(1)); крупный портрет
    ходящего — слева у своего, справа у врага (0x639d89/0x639e08); на другой стороне — юнит под мышью и первая цель удара (0x630f15)."""
    code, out = godot(repo, ["--scenario", "'A Return To Simpler Times'", "--stacks", "S001KC0000,S001KC001a",
                             "--squad", "G000UU0001:0,G000UU0001:2,G000UU0011:1,G000UU0011:3", "--check", "--sides"],
                      "res://scenes/battle/BattleScreen.tscn")
    line = re.search(r"^battle sides (.*)$", out, re.MULTILINE)
    steps = {m["what"]: m for m in SIDES.finditer(line[1])} if line else {}
    need = [f"{k}_{s}" for k in ("боец", "лекарь") for s in ("ход", "мышь", "удар")] + ["враг"]
    if code != 0 or any(n not in steps for n in need):
        return False, f"стороны боя: код {code}, шаги {sorted(steps)}\n{out.strip()}"
    bad = [n for n in need if (steps[n]["own"], steps[n]["foe"]) != ("L", "R")]
    for kind in ("боец", "лекарь"):
        turn, hover, hit = steps[f"{kind}_ход"], steps[f"{kind}_мышь"], steps[f"{kind}_удар"]
        if turn["left"] != turn["mover"] or hover["left"] != turn["mover"] or hit["left"] != turn["mover"]:
            bad.append(f"{kind}: слева не ходящий")
        if hover["right"] == turn["mover"] or hit["right"] != hover["right"]:
            bad.append(f"{kind}: справа не цель")
    foe = steps["враг"]
    if foe["whose"] != "чужой" or foe["right"] != foe["mover"] or foe["left"] == foe["mover"]:
        bad.append("враг: ходящий не справа или цель не слева")
    start = steps.get("начало")
    if start is None or sorted((start["left"], start["right"])) != sorted((start["mover"], "-")):
        bad.append("начало: второй крупный портрет не пуст (CBatBigFace 0x64ec10 — пуст до мыши и показа действия)")
    if bad:
        return False, f"стороны боя: {', '.join(bad)}\n{line[0]}"
    return True, (f"стороны боя: свои слева, враг справа на ходу бойца, лекаря и врага; портреты — боец {steps['боец_удар']['left']}"
                  f"/{steps['боец_удар']['right']}, лекарь {steps['лекарь_удар']['left']}/{steps['лекарь_удар']['right']}, "
                  f"враг {foe['left']}/{foe['right']}")


def pixel_tool(repo: Path) -> tuple[bool, str]:
    """Сверка со снимком (tools/pixel-compare.py, docs/ui-windows.md §10) на поддельном снимке: экран порта DLG_OPTIONS вклеен в
    1024×768 со сдвигом 37,91 и испорченным квадратом 20×20 — инструмент должен найти сдвиг и ровно этот квадрат."""
    import importlib.util
    from PIL import Image
    spec = importlib.util.spec_from_file_location("pixel_compare", repo / "tools" / "pixel-compare.py")
    pc = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(pc)
    port = pc.port_screen("DLG_OPTIONS")
    # Имя не совпадает с настоящим снимком: файл состояния tools/pixel-states/DLG_OPTIONS.json к поддельному не относится.
    shot = repo / ".cache" / "pixel" / "selftest" / "DLG_OPTIONS-selftest.png"
    shot.parent.mkdir(parents=True, exist_ok=True)
    canvas = Image.new("RGBA", (1024, 768), (10, 10, 10, 255))
    canvas.alpha_composite(Image.open(port).convert("RGBA"), (37, 91))
    canvas.paste((255, 255, 0, 255), (425, 321, 445, 341))   # экран 388,230 — орнамент без подписей и счётчиков
    canvas.save(shot)
    code = subprocess.run([sys.executable, str(repo / "tools" / "pixel-compare.py"), str(shot), "--port", str(port),
                           "--screen", "DLG_OPTIONS"], capture_output=True, text=True, encoding="utf-8", errors="replace", env=child_env())
    out = code.stdout.strip()
    if "сдвиг=37,91 " not in out or "худшая_область=388,230,20,20 " not in out:
        return False, f"сверка со снимком: {out}\n{code.stderr}"
    return True, "сверка со снимком: поддельный снимок — сдвиг 37,91 и испорченный квадрат 20×20 найдены"


def steps(repo: Path, screens: list[str], window: bool) -> list[tuple[str, Callable[[], tuple[bool, str]]]]:
    """Шаги проверки по порядку: имя (оно же строка в итоге провала и ключ выбора `--only`) и вызов."""
    out: list[tuple[str, Callable[[], tuple[bool, str]]]] = [(screen, partial(run, repo, screen)) for screen in screens]
    out += [("шрифт", partial(ink, repo)), ("шаг строки", partial(lines, repo)), ("табуляция", partial(tabs, repo)),
            ("строки подписей", partial(text_block, repo)), ("толщина линий", partial(strokes, repo, window))]
    out += [(f"ввод {screen}", partial(live, repo, screen, counts, case)) for screen, counts, case in INPUT_CASES]
    out += [(f"граф {name}", partial(flow, repo, name, start, case)) for name, start, case in FLOW_CASES]
    out += [(name, partial(fn, repo)) for name, fn in [
        ("щелчки через показ", scaled_flow), ("логика новой игры", content), ("вступление", cinematic), ("земля", ground),
        ("экран карты", mapscreen), ("портрет лидера", leader_face), ("экран боя", battle), ("экран боя: видимость", battle_visible),
        ("вспышки портретов", battle_flash), ("полоса очереди боя", battle_queue),
        ("бой с карты", mapbattle), ("«Меню героя»", paperdoll), ("автопрокрутка «Задания»", briefing_scroll),
        ("сверка со снимком: инструмент", pixel_tool), ("раскладка боя", battle_layout), ("показ боя", battle_playback),
        ("стороны боя", battle_sides), ("ключ в индексе палитры", black_key_frames), ("бой за руину", siege),
        ("игровой цикл", gameloop), ("автосохранение", autosave),
        ("быстрое сохранение", quicksave), ("предложение союза", alliance_offer), ("журнал команд", command_journal), ("прокрутка стрелкой", map_arrows),
        ("сага", saga), ("перевод сценариев", scenario_texts), ("конец партии", game_over), ("бой эффекта", event_battle),
        ("импорт героя", leader_import), ("окна города", city_windows), ("перезагрузка мода", mod_reload), ("Студия", studio), ("бой с атакой мода", mod_battle),
        ("ход и туман по шагам, мешок", walk_steps), ("ход ИИ", ai_turn), ("плавная прокрутка", smooth_scroll),
        ("смена тумана", fog_change), ("звук", sound), ("начало боя", battle_opening), ("подсветка клетки", highlight),
        ("облака", clouds), ("снос гор", mountains), ("попап в ходе ИИ", ai_popup), ("настройки показа", map_settings), ("окно настроек", settings_window), ("переназначение клавиш", rebinding), ("геймпад", gamepad), ("HD-слой", hd_layer_step),
        ("литьё на карте", spell_anim), ("предметы в бою", battle_items), ("гибель отряда", stack_death),
        ("курсоры карты", map_cursors), ("курсор меню", menu_cursors), ("карточки карты", map_cards),
        ("вписывание экранов", screen_fit), ("рамка и слои карты", map_frame), ("палитра карты", map_palette), ("вор у цели", noble), ("выбор навыка лидера", upgrade_leader)]]
    return out


def timed(name: str, fn: Callable[[], tuple[bool, str]]) -> tuple[str, bool, str, float, list[float]]:
    """Шаг с замером: стена шага и время каждого запуска Godot в нём (`godot()` пишет их в _STEP.calls шага)."""
    _STEP.calls, _STEP.extra, _STEP.name = [], {}, name
    t0 = time.monotonic()
    try:
        ok, message = fn()
    except Exception as e:   # упавший шаг — провал с причиной, а не обрыв всей проверки
        ok, message = False, f"{name}: исключение {type(e).__name__}: {e}"
    return name, ok, message, time.monotonic() - t0, _STEP.calls


# Что сцена может показать: меню и окна (ui), карту (map), бой (battle). Меню ведёт в партию и бой, карта открывает окна и бои, окна
# интерфейса есть везде. Шаг выбирается по задетому (--areas), если его сцены показывают задетую область.
SCENE_AREAS = {"UiScreen": {"ui"}, "UiFlow": {"ui", "map", "battle"}, "MapScreen": {"ui", "map", "battle"},
               "BattleScreen": {"ui", "battle"}, "GroundTest": {"map"}, "ShotCompare": {"map"}}
AREAS = ("ui", "map", "battle")


def call_arity(text: str, head: str) -> list[int]:
    """Число аргументов каждого вызова `head…)` в тексте (запятые верхнего уровня скобок)."""
    out = []
    for m in re.finditer(re.escape(head), text):
        depth, commas, i = 1, 0, m.end()
        while i < len(text) and depth:
            c = text[i]
            depth += c in "([{"
            depth -= c in ")]}"
            commas += c == "," and depth == 1
            i += 1
        out.append(commas + 1)
    return out


def step_areas(fn: Callable) -> set[str]:
    """Области шага — по сценам, которые запускают он и вызванные им функции этого файла (поиск по исходнику; `godot()` без сцены —
    UiScreen). Внешние скрипты (stroke-check, pixel-compare) — по их тексту. Сцена не найдена — все области."""
    module = sys.modules[__name__]
    seen: set[str] = set()
    scenes: set[str] = set()

    def walk(f: Callable) -> None:
        f = getattr(f, "func", f)   # partial
        if f.__name__ in seen:
            return
        seen.add(f.__name__)
        text = inspect.getsource(f)
        scenes.update(re.findall(r"res://scenes/\w+/(\w+)\.tscn", text))
        if any(args == 2 for args in call_arity(text, "godot(")):
            scenes.add("UiScreen")
        for tool in re.findall(r'"tools" / "([\w-]+\.py)"', text):
            path = Path(__file__).with_name(tool)
            if path.is_file():
                scenes.update(re.findall(r"res://scenes/\w+/(\w+)\.tscn", path.read_text(encoding="utf-8")))
        for name in set(re.findall(r"(?<![.\w])([a-z_]\w*)\(", text)) - seen:   # без «.run(», «.items(» — это не функции файла
            g = getattr(module, name, None)
            if inspect.isfunction(g) and g.__module__ == __name__ and name not in ("godot", "_godot", "godot_all", "main"):
                walk(g)

    walk(fn)
    areas = set().union(*(SCENE_AREAS.get(s, set(AREAS)) for s in scenes)) if scenes else set(AREAS)
    return areas


# Время шагов прошлого прогона: по нему пул берёт долгие шаги первыми (иначе самый долгий, начатый последним, растянул бы стену).
TIMES = Path(".cache") / "ui-check" / "times.json"

# Шаги, которые меряют время кадров и таймеров сцены (синхронная часть шага боя, опоздание таймера, мс прокрутки и тумана): под нагрузкой
# соседних Godot их пороги ложно падали бы — они идут после пула, по одному, на свободной машине (perf-log: «показ боя» дал 2458 мс при
# нагруженной машине).
SOLO = {"ход и туман по шагам, мешок", "плавная прокрутка", "показ боя"}


def default_jobs() -> int:
    return int(os.environ.get("UI_CHECK_JOBS") or max(1, (os.cpu_count() or 2) // 2))


def previous_times(path: Path) -> dict[str, float]:
    try:
        # Работа шага — время его Godot без ожидания слота: стена шага в хвосте очереди слотов раскачивала бы порядок.
        return {s["name"]: s.get("work", s["seconds"]) for s in json.loads(path.read_text(encoding="utf-8"))["steps"]}
    except (OSError, ValueError, KeyError):
        return {}


def prewarm(repo: Path) -> None:
    """Свежая рабочая копия без каталога .godot: первый запуск Godot пишет туда кэши проекта — один раз до пула, а не разом из всех
    работников."""
    if not (repo / "godot" / ".godot").is_dir():
        exe = os.environ.get("GODOT", r"C:\Tools\bin\godot.cmd")
        subprocess.run([exe, "--headless", "--path", str(repo / "godot"), "--import"], capture_output=True, timeout=600, check=False)


def run_pool(repo: Path, plan: list[tuple[str, Callable[[], tuple[bool, str]]]], jobs: int,
             before: dict[str, float]) -> dict[str, tuple[str, bool, str, float, list[float]]]:
    """Все шаги разом (потоки только ждут Godot), а Godot идёт не больше `jobs` разом — слоты; долгие по прошлому прогону встают в
    очередь слотов первыми. SOLO — после, по одному. Строка шага печатается по готовности; итог собирает main в порядке плана."""
    set_slots(repo, max(1, jobs))
    pooled = sorted((s for s in plan if s[0] not in SOLO), key=lambda s: -before.get(s[0], 1e9))
    solo = [s for s in plan if s[0] in SOLO]
    done = {}
    with ThreadPoolExecutor(max_workers=max(1, len(pooled), len(solo))) as pool:
        futures = []
        for name, fn in pooled:   # по одному с паузой: потоки встают в очередь слотов в порядке «долгие первыми»
            futures.append(pool.submit(timed, name, fn))
            time.sleep(0.005)
        for future in as_completed(futures):
            result = future.result()
            print(f"{result[2]}  [{result[3]:.1f} с]", flush=True)
            done[result[0]] = result
        for name, fn in solo:
            result = pool.submit(timed, name, fn).result()
            print(f"{result[2]}  [{result[3]:.1f} с, отдельно]", flush=True)
            done[name] = result
    return done


def main(argv: list[str]) -> int:
    repo = Path(__file__).resolve().parents[1]
    ap = argparse.ArgumentParser(description="Экраны порта без окна: сборка, ввод, графы, живые сцены — числами.")
    ap.add_argument("screens", nargs="*", help=f"экраны для сборки в PNG (по умолчанию — {len(SCREENS)})")
    ap.add_argument("--window", action="store_true", help="снимки окна для толщины линий (открывает окно на несколько секунд)")
    ap.add_argument("--only", action="append", default=[], metavar="ПОДСТРОКА",
                    help="только шаги, в имени которых есть подстрока (можно несколько раз)")
    ap.add_argument("--areas", help=f"только шаги, чьи сцены показывают задетое: через запятую из {', '.join(AREAS)} или all "
                                    "(решает tools/affected_tests.py, передаёт check.py)")
    ap.add_argument("--list", action="store_true", help="напечатать имена шагов с их областями и выйти")
    ap.add_argument("--times", type=Path, default=TIMES,
                    help="куда записать время шагов (JSON: имя, секунды, запуски Godot); прошлый файл задаёт порядок пула")
    ap.add_argument("-j", "--jobs", type=int, default=default_jobs(),
                    help="работников пула (по умолчанию UI_CHECK_JOBS или половина логических ядер); 1 — строго по очереди")
    args = ap.parse_args(argv[1:])

    plan = steps(repo, args.screens or SCREENS, args.window)
    if args.only:
        plan = [(n, f) for n, f in plan if any(o.lower() in n.lower() for o in args.only)]
    if args.areas and "all" not in args.areas.split(","):
        want = set(args.areas.split(","))
        plan = [(n, f) for n, f in plan if step_areas(f) & want]
    if args.list:
        print("\n".join(f"{n}  [{','.join(sorted(step_areas(f)))}]" for n, f in plan))
        return 0
    if not plan:   # опечатка в --only или области без шагов — не «зелёное» из пустоты
        print("ПРОВАЛ: ни одного шага не выбрано (--only/--areas)", file=sys.stderr)
        return 2

    prewarm(repo)
    t0 = time.monotonic()
    done = run_pool(repo, plan, args.jobs, previous_times(repo / args.times))
    results = [done[name] for name, _ in plan]
    wall = time.monotonic() - t0

    slow = sorted(results, key=lambda r: -r[3])[:10]
    print(f"\nвремя: {wall:.0f} с стены ({args.jobs} работников), шаги в сумме {sum(r[3] for r in results):.0f} с, запусков Godot "
          f"{sum(len(r[4]) for r in results)} ({sum(sum(r[4]) for r in results):.0f} с в них); самые долгие шаги: "
          + ", ".join(f"{r[0]} {r[3]:.0f} с" for r in slow))
    times = repo / args.times
    times.parent.mkdir(parents=True, exist_ok=True)
    times.write_text(json.dumps({"wall": round(wall, 1), "jobs": args.jobs, "steps": [
        {"name": r[0], "ok": r[1], "seconds": round(r[3], 2), "work": round(sum(r[4]), 2), "godot": [round(c, 2) for c in r[4]]}
        for r in results]},
        ensure_ascii=False, indent=1), encoding="utf-8")

    if _AUDIT:
        print(f"\nмимо команд: {len(_AUDIT)} мест (партия менялась вне GameSession.Execute, docs/game-commands.md §3):")
        for site, step in sorted(_AUDIT.items()):
            print(f"  {site}  [{step}]")
    failures = [r[0] for r in results if not r[1]] + (["мимо команд"] if _AUDIT else [])
    if failures:
        print(f"\nПРОВАЛ: {len(failures)} из {len(results)} — {', '.join(failures)}", file=sys.stderr)
        return 1
    print(f"\nОК: {len(results)} шагов — экраны собраны без пропусков и мадженты, строки на месте, кириллица есть, ввод, графы "
          f"экранов, живые сцены карты и боя, сохранения, ход ИИ, звук и сверки со снимками проходят")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main(sys.argv))
