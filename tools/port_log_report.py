#!/usr/bin/env python3
r"""Отчёт по запуску порта: что сломалось и где — из журнала зонда порта или из лога Godot.

Источники (по умолчанию — самый свежий):
  журнал зонда  %APPDATA%\Godot\app_userdata\Disciples\probe\port-*.jsonl  (godot/autoload/PortProbe.cs)
  лог Godot     %APPDATA%\Godot\app_userdata\Disciples\logs\godot*.log     (запуски без зонда)

Отчёт — Markdown в .cache/port-report/<имя>.md и короткая сводка в консоль: ошибки по группам (сколько раз, место в коде порта,
текст, что происходило перед первой), тормоза кадров, пометки F9 (снимок и сохранение), окна, ходы и бои, аварийный конец; для
долгой партии — мир по дням (казна, города, отряды; сохранения дней), нарушения инвариантов, итоги боёв, сводка решений ИИ,
сработавшие события сценария (docs/port-probe.md).

    python tools/port_log_report.py                  # самый свежий журнал зонда, иначе самый свежий лог
    python tools/port_log_report.py путь.jsonl|путь.log
    python tools/port_log_report.py --all            # все журналы зонда по очереди
    python tools/port_log_report.py --synth [путь]   # партия на синтетической карте порта: какие вопросы закрыты (и в консоль)

Синтетические карты порта (docs/synthetic-maps-port): если партия шла на такой карте (строка session), в отчёт добавляется раздел
«Синтетическая карта» — по описанию карты (<карта>/facts.json, его пишет tools/synth-maps/make_port_synth.py) каждый вопрос: закрыт
(нужные строки журнала есть) или открыт (каких нет).
"""
from __future__ import annotations

import argparse
import collections
import json
import os
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
USER = Path(os.environ.get("APPDATA", "")) / "Godot" / "app_userdata" / "Disciples"
OUT = REPO / ".cache" / "port-report"


def latest() -> Path | None:
    journals = sorted((USER / "probe").glob("port-*.jsonl"), key=lambda p: p.stat().st_mtime)
    if journals:
        return journals[-1]
    logs = sorted((USER / "logs").glob("godot*.log"), key=lambda p: p.stat().st_mtime)
    return logs[-1] if logs else None


# ------------------------------------------------------------------ журнал зонда

def journal_report(path: Path) -> tuple[str, str]:
    rows = []
    with path.open(encoding="utf-8") as f:
        for line in f:
            if not line.strip():
                continue
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                row = None
            rows.append(row if isinstance(row, dict) else {"ev": "broken", "text": line[:200]})
    kinds = collections.Counter(r.get("ev") for r in rows)
    start = next((r for r in rows if r.get("ev") == "start"), {})
    end = next((r for r in reversed(rows) if r.get("ev") == "end"), None)
    errors = [r for r in rows if r.get("ev") == "error"]
    counts = next((r["counts"] for r in reversed(rows) if r.get("ev") == "error_counts"), None)
    groups: dict[str, dict] = {}
    for e in errors:
        key = f"{e.get('kind')}|{first_line(e.get('message', ''))}|{e.get('where')}"
        g = groups.setdefault(key, {"first": e, "n": 0})
        g["n"] += 1
    if counts:
        for key, n in counts.items():
            if key in groups:
                groups[key]["n"] = n
    slow = [r for r in rows if r.get("ev") == "slow_frame"]
    marks = [r for r in rows if r.get("ev") == "mark"]
    # Сообщения в stderr (GD.PrintErr): исключение C# порт ловит сам и печатает (логика окна упала на открытии — прогон 15.10, пометка
    # 22: окно итога вора без кнопки), строки error для них нет — без этого раздела отчёт говорил «ошибок 0».
    stderr = [r for r in rows if r.get("ev") == "stderr"]
    stderr_groups = collections.Counter(first_line(r.get("text", ""))[:200] for r in stderr)
    screens = collections.Counter(r.get("name") for r in rows if r.get("ev") == "screen" and r.get("action") == "open")
    seconds = (rows[-1].get("t", 0) if rows else 0) / 1000

    md = [f"# Отчёт по запуску порта — {path.name}", "",
          f"- Когда: {start.get('when', '?')}, коммит `{start.get('commit', '?')}`, Godot {start.get('godot', '?')}, "
          f"экран {start.get('display', '?')}, длительность {seconds / 60:.1f} мин, строк {len(rows)}",
          f"- Конец: {'штатный — ' + end.get('reason', '') if end else '**аварийный — строки end нет (процесс упал или убит)**'}",
          f"- Ошибок: {sum(g['n'] for g in groups.values())} ({len(groups)} разных), сообщений stderr: {len(stderr)}, "
          f"тормозов кадра: {len(slow)}, пометок F9: {len(marks)}, "
          f"ходов игроков: {kinds.get('turn', 0)}, шагов отрядов: {kinds.get('move', 0)}, боёв: {kinds.get('battle', 0)}",
          f"- Партия: дней (снимков `day`): {kinds.get('day', 0)}, нарушений инвариантов: {kinds.get('invariant', 0)}, "
          f"событий сценария: {kinds.get('event', 0)}, выборов ИИ: {kinds.get('ai_pick', 0)}, шагов стратегии ИИ: {kinds.get('ai_step', 0)}",
          ""]
    if groups:
        md += ["## Ошибки", ""]
        for i, (key, g) in enumerate(sorted(groups.items(), key=lambda kv: -kv[1]["n"]), 1):
            e = g["first"]
            md += [f"### {i}. {e.get('kind')} × {g['n']} — {first_line(e.get('message', ''))[:160]}", "",
                   f"- Где: `{e.get('where')}`", f"- Впервые: {e.get('t', 0) / 1000:.1f} с, строка {e.get('seq')}"]
            if e.get("context"):
                md += ["- Перед этим:", "", "```", *e["context"], "```"]
            if e.get("message", "").count("\n") > 0 or e.get("backtrace"):
                md += ["<details><summary>Полный текст</summary>", "", "```", e.get("message", ""), e.get("backtrace") or "", "```",
                       "</details>"]
            md.append("")
    if stderr:
        md += ["## Сообщения stderr", ""]
        for text, n in stderr_groups.most_common():
            first = next(r for r in stderr if first_line(r.get("text", ""))[:200] == text)
            md.append(f"- × {n}, впервые на {first.get('t', 0) / 1000:.1f} с (строка {first.get('seq')}): `{text}`")
        md.append("")
    if marks:
        md += ["## Пометки F9", ""]
        for m in marks:
            md.append(f"- **{m.get('n')}** на {m.get('t', 0) / 1000:.1f} с, экран {m.get('screen')}, день {m.get('day')}: снимок "
                      f"`{m.get('shot')}`, сохранение `{m.get('save')}`")
            before = [r for r in rows if r.get("seq", 0) < m.get("seq", 0) and r.get("ev") in ("print", "click", "key", "screen")][-8:]
            md += ["", "```", *(describe(r) for r in before), "```", ""]
    if slow:
        worst = sorted(slow, key=lambda r: -r.get("ms", 0))[:10]
        md += ["## Тормоза кадра (хуже всех)", ""] + [f"- {r.get('ms')} мс на {r.get('t', 0) / 1000:.1f} с, экран {r.get('screen')}"
                                                        for r in worst] + [""]
    synth = synth_section(rows)
    md += synth + days_section(rows) + invariants_section(rows) + battles_section(rows) + ai_section(rows) + events_section(rows)
    if screens:
        md += ["## Окна (открытий)", "", ", ".join(f"{k} {v}" for k, v in screens.most_common())]
    if not end:
        md += ["", "## Последние строки перед обрывом", "", "```", *(describe(r) for r in rows[-25:]), "```"]
    summary = (f"{path.name}: ошибок {sum(g['n'] for g in groups.values())} ({len(groups)} разных), stderr {len(stderr)} "
               f"({len(stderr_groups)} разных), пометок {len(marks)}, "
               f"тормозов {len(slow)}, дней {kinds.get('day', 0)}, нарушений {kinds.get('invariant', 0)}, "
               f"конец {'штатный' if end else 'АВАРИЙНЫЙ'}")
    for line in synth:
        if line.startswith("Итог: "):
            summary += "\n  " + line
    return "\n".join(md) + "\n", summary


# ------------------------------------------------------------------ синтетические карты порта

SYNTH = REPO / "docs" / "synthetic-maps-port"


def synth_maps() -> dict[str, dict]:
    """Описания синтетических карт: id сценария → facts.json."""
    out = {}
    for f in sorted(SYNTH.glob("*/facts.json")):
        facts = json.loads(f.read_text(encoding="utf-8"))
        out[facts["id"].lower()] = facts
    return out


def _flat(v) -> list:
    if isinstance(v, list):
        return [x for item in v for x in _flat(item)]
    return [v]


def _same(a, b) -> bool:
    return str(a).lower() == str(b).lower() if isinstance(a, str) or isinstance(b, str) else a == b


# Действие отряда с соседней клетки — та же команда хода, что и шаг (у движка CCmdMoveStackMsg с путём из одной точки): у журналов до
# 17.10 строки move у таких действий не было, а ход виден по строкам самого действия — нападающий боя, вор у цели, снос и постановка
# жезла (бросок мешка строки с отрядом не оставлял). Требование «move stack=X» закрывает и такая строка, где X — тот, кто действует.
# Только строки состоявшегося действия: начало прицела («droprod start», «pickup start», «rodbreak ask») ещё не действие.
ACTOR_LINES = re.compile(r"^(?:thief (S\d{3}KC[0-9a-f]{4}) -> |noble (S\d{3}KC[0-9a-f]{4}) |rodbreak (S\d{3}KC[0-9a-f]{4}) S\S+ (?!ask)|"
                         r"droprod placed (S\d{3}KC[0-9a-f]{4}) (?!.*отказ=))", re.IGNORECASE)


def acted(row: dict, stack: str) -> bool:
    """Строка — действие отряда stack без строки move: нападение (battle_end/battle attacker, начало боя на виду), вор, снос и постановка жезла."""
    ev = row.get("ev")
    if ev in ("battle_end", "battle"):
        return _same(row.get("attacker"), stack)
    if ev == "sight" and row.get("kind") == "BattleStarted":
        return _same(row.get("object"), stack)
    if ev == "print" and (m := ACTOR_LINES.match(str(row.get("text", "")))):
        # «thief S… -> цель», «noble S… от=…», «rodbreak S… S…RD… снесён», «droprod placed S… at=…» (без отказа)
        return _same(next(g for g in m.groups() if g), stack)
    return False


def need_met(row: dict, need: dict) -> bool:
    """Строка журнала подходит требованию: тот же ev и поля. Поле-список — содержит значение; retreated: attacker/defender — у этой
    стороны бежал хоть один; поле с окончанием «_has» — подстрока поля без окончания (без учёта регистра: строки print). Требование
    «move stack=X» без других полей закрывает и действие X с соседней клетки (acted)."""
    if need["ev"] == "move" and set(need.get("where", {})) == {"stack"} and "max" not in need and acted(row, need["where"]["stack"]):
        return True
    if row.get("ev") != need["ev"]:
        return False
    for key, want in need["where"].items():
        if key.endswith("_has"):
            if str(want).lower() not in str(row.get(key[:-4], "")).lower():
                return False
            continue
        got = row.get(key)
        if key == "retreated" and isinstance(got, list) and len(got) == 2:
            if got[0 if want == "attacker" else 1] <= 0:
                return False
        elif isinstance(got, list):
            if not any(_same(x, want) for x in _flat(got)):
                return False
        elif not _same(got, want):
            return False
    return True


def synth_rows(rows: list[dict], maps: dict[str, dict]) -> dict[str, list[dict]]:
    """Строки журнала по синтетическим картам: от строки session с картой до следующей session с другим сценарием."""
    out: dict[str, list[dict]] = {}
    current = None
    for r in rows:
        if r.get("ev") == "session":
            sid = str(r.get("scenario") or "").lower()
            current = sid if sid in maps else None
        if current:
            out.setdefault(current, []).append(r)
    return out


def synth_checks(facts: dict, rows: list[dict]) -> list[tuple[dict, bool, list[str]]]:
    """Каждый вопрос карты: закрыт ли и каких строк не хватает."""
    result = []
    for check in facts["checks"]:
        missing = []
        for need in check["needs"]:
            n = sum(1 for r in rows if need_met(r, need))
            if "max" in need and n > need["max"]:
                where = ", ".join(f"{k}={v}" for k, v in need["where"].items())
                missing.append(f"лишние `{need['ev']}` {where}: {n}, можно не больше {need['max']}")
            elif n < need.get("min", 1):
                where = ", ".join(f"{k}={v}" for k, v in need["where"].items())
                missing.append(f"`{need['ev']}` {where}" + (f" (нужно {need['min']}, есть {n})" if need.get("min", 1) > 1 else ""))
        result.append((check, not missing, missing))
    return result


def synth_section(rows: list[dict]) -> list[str]:
    maps = synth_maps()
    by_map = synth_rows(rows, maps)
    md: list[str] = []
    for sid, part in by_map.items():
        facts = maps[sid]
        checks = synth_checks(facts, part)
        closed = sum(1 for _, ok, _ in checks if ok)
        h = facts["human"]
        md += [f"## Синтетическая карта {facts['id']}", "",
               f"Человек — {h['race']}, лорд {h['lord']}, сложность {h['difficulty']}; памятка — "
               f"`docs/synthetic-maps-port/{facts['id']}/player-memo.md`.", "",
               "| Вопрос | Итог | Чего нет в журнале |", "|---|---|---|"]
        for check, ok, missing in checks:
            note = f" ({check['note']})" if check.get("note") else ""
            md.append(f"| {check['key']}: {check['question']} | {'закрыт' if ok else '**открыт**'} | "
                      f"{'; '.join(missing) if missing else '—'}{note} |")
        md += ["", f"Итог: {facts['id']} — закрыто {closed} из {len(checks)}", ""]
    return md


# ------------------------------------------------------------------ долгая партия: дни, инварианты, бои, ИИ, события

SLOW_AI_MS = 2000


def days_section(rows: list[dict]) -> list[str]:
    """Мир по дням (строки day): по игроку — золото, города (столица + деревни, бунт), отряды, юниты, уровни; сохранение дня."""
    days = [r for r in rows if r.get("ev") == "day"]
    failed = {r.get("save"): r.get("error") for r in rows if r.get("ev") == "day_save"}
    if not days:
        return []
    players = sorted({p for d in days for p in d.get("players", {})})
    md = ["## Мир по дням", "",
          "Клетка — `золото · города (бунтуют) · отряды · юниты · уровней`; города — столица + деревни.", "",
          "| день | ход | " + " | ".join(players) + " | сохранение |",
          "|---|---|" + "---|" * len(players) + "---|"]
    for d in days:
        cells = []
        for pid in players:
            p = d.get("players", {}).get(pid)
            if not p:
                cells.append("—")
                continue
            cities = (1 if p.get("capital") else 0) + len(p.get("villages") or {})
            riot = f" ({len(p['riot'])})" if p.get("riot") else ""
            cells.append(f"{(p.get('bank') or [0])[0]} · {cities}{riot} · {p.get('stacks')} · {p.get('units')} · {p.get('levels')}")
        save = d.get("save") or ""
        if not save:
            link = "—"
        elif save in failed:
            link = f"сбой: {failed[save]}"
        else:
            link = f"`{Path(save).name}`" + ("" if Path(save).exists() or not Path(save).parent.exists() else " (прорежено)")
        day = f"{d.get('day')} (подписка)" if d.get("attach") else str(d.get("day"))
        md.append(f"| {day} | {d.get('player')} | " + " | ".join(cells) + f" | {link} |")
    last = days[-1]
    rng = last.get("rng") or {}
    relations = ", ".join(f"{k} {v}" for k, v in (last.get("relations") or {}).items()) or "—"
    allies = f"; союзы {', '.join(last['allies'])}" if last.get("allies") else ""
    md += ["", f"Последний снимок: день {last.get('day')}, R250 {rng.get('r250')}, CRT {rng.get('crt')}, выключено событий "
               f"{last.get('events_off')}, квест-лог {last.get('quest_log')}; отношения {relations}{allies}", ""]
    return md


def invariants_section(rows: list[dict]) -> list[str]:
    """Нарушения инвариантов карты по кодам: сколько строк, объектов, первый день и пример."""
    bad = [r for r in rows if r.get("ev") == "invariant"]
    if not bad:
        return []
    by_code: dict[str, list[dict]] = collections.defaultdict(list)
    for r in bad:
        by_code[r.get("code", "?")].append(r)
    md = ["## Нарушения инвариантов", "", "| код | строк | объектов | первый день | пример |", "|---|---|---|---|---|"]
    for code, items in sorted(by_code.items(), key=lambda kv: (min(i.get("day") or 0 for i in kv[1]), kv[0])):
        first = min(items, key=lambda i: (i.get("day") or 0, i.get("seq") or 0))
        objects = {i.get("object") for i in items}
        md.append(f"| `{code}` | {len(items)} | {len(objects)} | {first.get('day')} | {first.get('object')}: {first.get('detail')} "
                  f"(при {first.get('when')}) |")
    return md + [""]


def battles_section(rows: list[dict]) -> list[str]:
    """Итоги боёв (battle_end): число, победы и поражения по игрокам, раунды, павшие, опыт."""
    ends = [r for r in rows if r.get("ev") == "battle_end"]
    if not ends:
        return []
    stats: dict[str, collections.Counter] = collections.defaultdict(collections.Counter)
    for e in ends:
        owners = (e.get("attacker_owner") or "-", e.get("defender_owner") or "-")
        winner = e.get("winner_owner") or "-"
        fallen, xp = e.get("fallen") or [0, 0], e.get("xp") or [0, 0]
        for side, owner in enumerate(owners):
            s = stats[owner]
            s["боёв"] += 1
            s["нападал" if side == 0 else "защищался"] += 1
            s["побед" if owner == winner else "поражений"] += 1
            s["павших"] += fallen[side]
            s["опыта"] += xp[side]
    rounds = [e.get("rounds") or 0 for e in ends]
    cols = ["боёв", "нападал", "защищался", "побед", "поражений", "павших", "опыта"]
    md = ["## Бои", "", f"Боёв {len(ends)}, раундов в среднем {sum(rounds) / len(rounds):.1f} (наибольшее {max(rounds)}); "
                        "«-» — нейтральная руина.", "",
          "| игрок | " + " | ".join(cols) + " |", "|---|" + "---|" * len(cols)]
    for owner, s in sorted(stats.items()):
        md.append(f"| {owner} | " + " | ".join(str(s[c]) for c in cols) + " |")
    return md + [""]


def ai_section(rows: list[dict]) -> list[str]:
    """Сводка ИИ: выборы тактики по видам и игрокам, шаги стратегии по видам, ходы ИИ и медленные (> 2 с)."""
    picks = [r for r in rows if r.get("ev") == "ai_pick"]
    steps = [r for r in rows if r.get("ev") == "ai_step"]
    turns = [r for r in rows if r.get("ev") == "ai_turn"]
    if not (picks or steps or turns):
        return []
    md = ["## ИИ", ""]
    for title, items, key in (("Выборы тактики (`ai_pick`)", picks, "action"), ("Шаги стратегии (`ai_step`)", steps, "kind")):
        if not items:
            continue
        players = sorted({r.get("player") or "-" for r in items})
        table: dict[str, collections.Counter] = collections.defaultdict(collections.Counter)
        for r in items:
            table[r.get(key) or "?"][r.get("player") or "-"] += 1
        md += [f"### {title}: {len(items)}", "", "| вид | всего | " + " | ".join(players) + " |",
               "|---|---|" + "---|" * len(players)]
        for kind, by in sorted(table.items(), key=lambda kv: -sum(kv[1].values())):
            md.append(f"| {kind} | {sum(by.values())} | " + " | ".join(str(by[p]) for p in players) + " |")
        md.append("")
    if turns:
        per: dict[str, list[int]] = collections.defaultdict(list)
        for t in turns:
            per[t.get("player") or "-"].append(t.get("ms") or 0)
        md += ["### Ходы ИИ (`ai_turn`)", "", "| игрок | ходов | всего, с | в среднем, мс | наибольший, мс |", "|---|---|---|---|---|"]
        for pid, ms in sorted(per.items()):
            md.append(f"| {pid} | {len(ms)} | {sum(ms) / 1000:.1f} | {sum(ms) / len(ms):.0f} | {max(ms)} |")
        slow = [t for t in turns if (t.get("ms") or 0) > SLOW_AI_MS]
        md += ["", f"Медленных ходов ИИ (> {SLOW_AI_MS // 1000} с): {len(slow)}"]
        md += ["", "`ms` — время хода по часам, вместе с ожиданием показа на главном потоке (ход ИИ ждёт, пока экран покажет шаг отряда, "
               "бой с человеком, паузу события). Разложение — по строкам главного потока между строками потока ИИ; «прочее» — расчёт ИИ "
               "и передача заявок кадрам (ход ИИ без экрана — десятки мс, `docs/perf-log.md` Г20)."] if slow else []
        md += [f"- день {t.get('turn')}, {t.get('player')}: {t.get('ms')} мс, шагов {t.get('steps')}, выборов {t.get('picks')}"
               + split_text(ai_turn_split(rows, t))
               for t in sorted(slow, key=lambda t: -(t.get("ms") or 0))[:15]]
        md.append("")
    return md


# Разложение хода ИИ: строка главного потока → вид показа, с которого идёт время до следующей отметки (MapView.AiTurn.cs: aimove — шаг
# отряда, battlestart — анимация начала боя, aibattle start — экран боя с человеком, закрытие BattleScreen — после боя, death/aisight — паузы).
SPLIT_NAMES = {"battle": "бой с человеком на экране", "walk": "шаги отрядов", "anim": "анимации и паузы событий", "rest": "прочее"}


def split_kind(r: dict) -> str | None:
    tag, text = r.get("tag"), r.get("text") or ""
    if tag == "aimove":
        return "walk"
    if tag == "aibattle" and text.startswith("aibattle start"):
        return "battle"
    if tag in ("battlestart", "death", "noble") or (tag == "aisight" and not text.startswith(("aisight бой", "aisight гибель"))):
        return "anim"
    if r.get("ev") == "screen" and r.get("action") == "close" and r.get("name") == "BattleScreen":
        return "rest"
    return None


def ai_turn_split(rows: list[dict], end: dict) -> dict[str, int]:
    """Куда ушло время хода ИИ: от строки `turn` игрока до его `ai_turn` на том же потоке; строка потока ИИ кончает отрезок показа."""
    i = next((k for k, r in enumerate(rows) if r is end), -1)
    th, player = end.get("th"), end.get("player")
    j = next((k for k in range(i - 1, -1, -1) if rows[k].get("ev") == "turn" and rows[k].get("player") == player
              and rows[k].get("th") == th), -1)
    if i < 0 or j < 0:
        return {}
    acc: collections.Counter = collections.Counter()
    kind, last = "rest", rows[j].get("t") or 0
    for r in rows[j + 1:i + 1]:
        t = r.get("t") or last
        new = "rest" if r.get("th") == th else split_kind(r)
        if new is not None:
            acc[kind] += t - last
            kind, last = new, t
        if r.get("tag") == "aimove" and (cells := re.search(r"клеток=(\d+)", r.get("text") or "")):
            acc["cells"] += int(cells.group(1))
    return dict(acc)


def split_text(split: dict[str, int]) -> str:
    parts = []
    for k in ("battle", "walk", "anim", "rest"):
        if not split.get(k):
            continue
        part = f"{SPLIT_NAMES[k]} {split[k]}"
        if k == "walk" and split.get("cells"):
            # Шаг на клетку по ключевым кадрам (StackWalk): при скорости противника 3 — 26–39 мс; больше — главный поток стоял.
            part += f" (клеток {split['cells']}, {split[k] / split['cells']:.0f} мс на клетку)"
        parts.append(part)
    return f" — {', '.join(parts)} мс" if parts else ""


def events_section(rows: list[dict]) -> list[str]:
    """Сработавшие события сценария (event): по событию — сколько раз, дни, игроки, эффекты."""
    fired = [r for r in rows if r.get("ev") == "event"]
    if not fired:
        return []
    by_id: dict[str, list[dict]] = collections.defaultdict(list)
    for r in fired:
        by_id[r.get("id") or "?"].append(r)
    md = ["## События сценария", "", "| событие | раз | дни | игроки | эффекты |", "|---|---|---|---|---|"]
    for eid, items in sorted(by_id.items(), key=lambda kv: (kv[1][0].get("day") or 0, kv[0])):
        days = sorted({i.get("day") for i in items if i.get("day") is not None})
        players = sorted({i.get("player") or "-" for i in items})
        md.append(f"| {eid} | {len(items)} | {', '.join(map(str, days))} | {', '.join(players)} | "
                  f"{', '.join(items[0].get('effects') or [])} |")
    return md + [""]


def describe(r: dict) -> str:
    ev = r.get("ev")
    if ev == "print":
        return r.get("text", "")
    if ev == "click":
        return f"[щелчок {r.get('button')} {r.get('at')} на {r.get('screen')}]"
    if ev == "key":
        return f"[клавиша {r.get('key')} на {r.get('screen')}]"
    if ev == "screen":
        return f"[окно {r.get('action')} {r.get('name')}]"
    return json.dumps(r, ensure_ascii=False)[:200]


# ------------------------------------------------------------------ лог Godot (без зонда)

ERR = re.compile(r"^(ERROR|WARNING|SCRIPT ERROR|USER ERROR|USER WARNING): (.*)")
OWN = re.compile(r"at (D2\.[\w.`<>]+(?:\([^)]*\))?) in (.+?disciples-godot.+?):line (\d+)")


def log_report(path: Path) -> tuple[str, str]:
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    groups: dict[str, dict] = {}
    recent: collections.deque[str] = collections.deque(maxlen=12)
    i = 0
    while i < len(lines):
        m = ERR.match(lines[i])
        if not m:
            if lines[i].strip() and not lines[i].startswith((" ", "\t")):
                recent.append(lines[i])
            i += 1
            continue
        block = [lines[i]]
        i += 1
        while i < len(lines) and (lines[i].startswith((" ", "\t")) or lines[i].startswith("Object name")):
            block.append(lines[i])
            i += 1
        own = next((OWN.search(b) for b in block if OWN.search(b)), None)
        where = f"{own.group(1)} — {Path(own.group(2)).name}:{own.group(3)}" if own else "-"
        key = f"{m.group(1)}|{m.group(2)[:160]}|{where}"
        g = groups.setdefault(key, {"n": 0, "kind": m.group(1), "text": m.group(2), "where": where, "block": block,
                                    "context": list(recent)})
        g["n"] += 1
    md = [f"# Отчёт по логу Godot — {path.name}", "", f"- Строк лога: {len(lines)}, ошибок: {sum(g['n'] for g in groups.values())} "
          f"({len(groups)} разных). Журнала зонда у этого запуска нет — ввод, окна и пометки не записаны.", ""]
    for n, g in enumerate(sorted(groups.values(), key=lambda g: -g["n"]), 1):
        md += [f"## {n}. {g['kind']} × {g['n']} — {g['text'][:160]}", "", f"- Где: `{g['where']}`", "- Перед первой:", "", "```",
               *g["context"], "```", "<details><summary>Полный текст</summary>", "", "```", *g["block"][:30], "```", "</details>", ""]
    return "\n".join(md) + "\n", f"{path.name}: ошибок {sum(g['n'] for g in groups.values())} ({len(groups)} разных)"


def first_line(text: str) -> str:
    return text.split("\n", 1)[0]


def report(path: Path) -> str:
    md, summary = journal_report(path) if path.suffix == ".jsonl" else log_report(path)
    OUT.mkdir(parents=True, exist_ok=True)
    out = OUT / (path.stem + ".md")
    out.write_text(md, encoding="utf-8")
    return f"{summary}\n  отчёт: {out}"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("path", nargs="?", type=Path)
    ap.add_argument("--all", action="store_true", help="все журналы зонда")
    ap.add_argument("--synth", action="store_true", help="вопросы синтетической карты — и в консоль")
    a = ap.parse_args()
    paths = sorted((USER / "probe").glob("port-*.jsonl")) if a.all else [a.path or latest()]
    if not paths or paths[0] is None:
        print(f"нет ни журнала зонда, ни лога Godot в {USER}")
        return 1
    for p in paths:
        print(report(p))
        if a.synth and p.suffix == ".jsonl":
            rows = [json.loads(line) for line in p.read_text(encoding="utf-8").splitlines() if line.strip().startswith("{")]
            section = synth_section(rows)
            print("\n".join(section) if section else "  синтетической карты в журнале нет (строки session с port-synth-*)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
