"""Взято из disciples-mod tools/d2mod/core/sg.py (тот же автор), без изменений, кроме этой
шапки: модуль переименован в sgfile, чтобы не путать с tools/sg_golden.py.

Сценарии Disciples 2 (.sg): чтение без потерь, правка нескольких классов, запись.

Формат — reference/D2SGFormat/sg.ksy. Файл = заголовок + N записей подряд, без
индекса, смещений и контрольных сумм. Запись: WHAT <класс> OBJ_ID <id>
BEGOBJECT <тело> ENDOBJECT. Тело — цепочка «маркер + значение» в порядке,
заданном спекой класса. Разбирать все 37 классов незачем: границы записей
находятся по рамке следующей записи, а тела читаются только у классов, которые
арена читает или создаёт. Побайтовый round-trip проверяет тест на всех картах
game-dev/Exports.

Идентификаторы: 'S139KC0007' = префикс сценария (4) + класс (2) + hex (4).
"""
from __future__ import annotations

import re
import struct
from dataclasses import dataclass, replace
from pathlib import Path

ENC = "cp1251"
NULL_ID = "G000000000"
_END = b"ENDOBJECT\x00"
_FRAME = re.compile(
    rb"WHAT(.{4})(\.\?AVC[A-Za-z0-9_@]+)\x00"
    rb"OBJ_ID\x0b\x00\x00\x00(.{10})\x00BEGOBJECT\x00", re.S)

WHAT_UNIT = ".?AVCMidUnit@@"
WHAT_STACK = ".?AVCMidStack@@"
WHAT_PLAYER = ".?AVCMidPlayer@@"
WHAT_SUBRACE = ".?AVCMidSubRace@@"
WHAT_CAPITAL = ".?AVCCapital@@"
WHAT_PLAN = ".?AVCMidgardPlan@@"
WHAT_VARS = ".?AVCMidScenVariables@@"
WHAT_INFO = ".?AVCScenarioInfo@@"
# quest_name в заголовке — имя сценария в списке игры (strz 256). Смещение зависит от формата
# заголовка: у старого заголовка 0xab7 байт — 0x140 (20 из 33 штатных карт), у всех прочих длин
# (0xad8, 0xb00, 0xb24, 0xb28, 0xb50, 0xb78, 0xc20) — 0x141. Ранг 1 — замер байтов 117 файлов
# game-pristine и game-dev (T09 header_offset_check.py 33/0).
HEADER_OLD_SIZE = 0xAB7


def _u32(n: int) -> bytes:
    return struct.pack("<I", n)


# ------------------------------------------------------------------ записи и файл

@dataclass(frozen=True)
class Record:
    what: str       # '.?AVCMidStack@@'
    obj_id: str     # 'S139KC0007'
    body: bytes     # между BEGOBJECT\0 и ENDOBJECT\0

    @property
    def cls(self) -> str:
        return self.obj_id[4:6]

    def dumps(self) -> bytes:
        return (b"WHAT" + _u32(len(self.what) + 1) + self.what.encode() + b"\0"
                + b"OBJ_ID" + _u32(11) + self.obj_id.encode() + b"\0BEGOBJECT\0"
                + self.body + _END)


@dataclass(frozen=True)
class Sg:
    header: bytes           # всё до 'SxxxOB0000' + счётчика объектов
    prefix: str             # 'S139'
    records: tuple[Record, ...]

    @classmethod
    def loads(cls, data: bytes) -> Sg:
        frames = list(_FRAME.finditer(data))
        if not frames:
            raise ValueError("не найдено ни одной записи WHAT/OBJ_ID/BEGOBJECT")
        start = frames[0].start() - 14
        if data[start + 4:start + 10] != b"OB0000":
            raise ValueError("перед первой записью нет счётчика объектов SxxxOB0000")
        total = struct.unpack_from("<I", data, start + 10)[0]
        if total != len(frames):
            raise ValueError(f"в заголовке {total} объектов, найдено рамок {len(frames)}")
        records = []
        for m, nxt in zip(frames, frames[1:] + [None]):
            end = nxt.start() if nxt else len(data)
            if struct.unpack("<I", m.group(1))[0] != len(m.group(2)) + 1:
                raise ValueError(f"битая рамка WHAT со смещения {m.start()}")
            if data[end - 10:end] != _END:
                raise ValueError(f"запись со смещения {m.start()} не кончается ENDOBJECT")
            records.append(Record(m.group(2).decode(), m.group(3).decode(),
                                  data[m.end():end - 10]))
        return cls(data[:start], data[start:start + 4].decode(), tuple(records))

    @classmethod
    def read(cls, path: Path) -> Sg:
        return cls.loads(Path(path).read_bytes())

    def dumps(self) -> bytes:
        head = self.header + f"{self.prefix}OB0000".encode() + _u32(len(self.records))
        return head + b"".join(r.dumps() for r in self.records)

    def write(self, path: Path) -> None:
        Path(path).write_bytes(self.dumps())

    def find(self, what: str) -> list[Record]:
        return [r for r in self.records if what in r.what]

    def one(self, what: str) -> Record:
        found = self.find(what)
        if len(found) != 1:
            raise ValueError(f"ожидалась одна запись {what}, найдено {len(found)}")
        return found[0]

    def next_ids(self, cls2: str, n: int = 1) -> list[str]:
        """n свободных id класса: нумерация не сплошная, берём max+1."""
        taken = [int(r.obj_id[6:], 16) for r in self.records if r.cls == cls2]
        start = max(taken, default=-1) + 1
        return [f"{self.prefix}{cls2}{start + k:04x}" for k in range(n)]

    def with_record(self, old: Record, new: Record) -> Sg:
        return replace(self, records=tuple(new if r is old else r for r in self.records))

    def appended(self, *recs: Record) -> Sg:
        return replace(self, records=self.records + recs)


# --------------------------------------------------------------- примитивы тела

def f_str(marker: str, value: str) -> bytes:
    raw = value.encode(ENC)
    return marker.encode() + _u32(len(raw) + 1) + raw + b"\0"


def f_int(marker: str, value: int) -> bytes:
    return marker.encode() + struct.pack("<i", value)


def f_bool(marker: str, value: int) -> bytes:
    return marker.encode() + bytes([value])


def f_count(obj_id: str, n: int) -> bytes:
    """Голая копия id объекта (10 байт, без маркера и NUL) + u32 длина списка."""
    return obj_id.encode() + _u32(n)


class Cursor:
    """Последовательное чтение тела: порядок полей задан sg.ksy, маркеры сверяются."""

    def __init__(self, body: bytes, pos: int = 0) -> None:
        self.body, self.pos = body, pos

    def _marker(self, marker: str) -> None:
        raw = marker.encode()
        if self.body[self.pos:self.pos + len(raw)] != raw:
            got = self.body[self.pos:self.pos + 12]
            raise ValueError(f"ожидался маркер {marker} со смещения {self.pos}, есть {got!r}")
        self.pos += len(raw)

    def int(self, marker: str) -> int:
        self._marker(marker)
        (v,) = struct.unpack_from("<i", self.body, self.pos)
        self.pos += 4
        return v

    def bool(self, marker: str) -> int:
        self._marker(marker)
        v = self.body[self.pos]
        self.pos += 1
        return v

    def str(self, marker: str) -> str:
        self._marker(marker)
        (n,) = struct.unpack_from("<I", self.body, self.pos)
        v = self.body[self.pos + 4:self.pos + 4 + n - 1].decode(ENC)
        self.pos += 4 + n
        return v

    def count(self) -> int:
        self.pos += 10
        (n,) = struct.unpack_from("<I", self.body, self.pos)
        self.pos += 4
        return n

    def fields(self, layout: str) -> dict[str, str | int]:
        """Пачка полей по описанию 'UNIT_ID:s TYPE:s LEVEL:i' (s строка, i число, b байт)."""
        out: dict[str, str | int] = {}
        for item in layout.split():
            name, kind = item.rsplit(":", 1)
            out[name] = {"s": self.str, "i": self.int, "b": self.bool}[kind](name)
        return out


def fields(body: bytes, layout: str) -> dict[str, str | int]:
    """Префикс тела: достаточно перечислить поля до нужного (см. LAYOUT_*)."""
    return Cursor(body).fields(layout)


LAYOUT_UNIT = "UNIT_ID:s TYPE:s LEVEL:i"
LAYOUT_PLAYER = ("PLAYER_ID:s NAME_TXT:s DESC_TXT:s LORD_ID:s RACE_ID:s FOG_ID:s "
                 "KNOWN_ID:s BUILDS_ID:s FACE:i QTY_BREAKS:i BANK:s IS_HUMAN:b")
LAYOUT_SUBRACE = "SUBRACE_ID:s SUBRACE:i PLAYER_ID:s"
LAYOUT_CAPITAL = "CITY_ID:s NAME_TXT:s DESC_TXT:s OWNER:s SUBRACE:s STACK:s POS_X:i POS_Y:i"
_LAYOUT_INFO_HEAD = "INFO_ID:s CAMPAIGN:s SOURCE_M:b QTY_CITIES:i"
_GROUP = ("GROUP_ID:s " + " ".join(f"UNIT_{k}:s" for k in range(6))
          + " " + " ".join(f"POS_{k}:i" for k in range(6)))
_STACK_TAIL = ("STACK_ID:s SRCTMPL_ID:s LEADER_ID:s LEADR_ALIV:b POS_X:i POS_Y:i MORALE:i "
               "MOVE:i FACING:i BANNER:s TOME:s BATTLE1:s BATTLE2:s ARTIFACT1:s ARTIFACT2:s "
               "BOOTS:s OWNER:s INSIDE:s SUBRACE:s INVISIBLE:b AI_IGNORE:b UPGCOUNT:i "
               "ORDER:i ORDER_TARG:s AIORDER:i AIORDERTAR:s AIPRIORITY:i CREAT_LVL:i NBBATTLE:i")
_ITEM_SLOTS = ("BANNER", "TOME", "BATTLE1", "BATTLE2", "ARTIFACT1", "ARTIFACT2", "BOOTS")


# ------------------------------------------------------------- юнит и отряд

def unit_body(unit_id: str, impl_id: str, level: int, hp: int, xp: int = 0) -> bytes:
    """Свежий MidUnit: без модификаторов и трансформации; level/xp/hp — как у прокачанного."""
    return (f_str("UNIT_ID", unit_id) + f_str("TYPE", impl_id) + f_int("LEVEL", level)
            + f_count(unit_id, 0) + f_int("CREATION", 0) + f_str("NAME_TXT", "")
            + f_bool("TRANSF", 0) + f_bool("DYNLEVEL", 0) + f_int("HP", hp) + f_int("XP", xp))


def stack_body(stack_id: str, units: list[str], cells: list[int], leader_id: str,
               x: int, y: int, owner: str, subrace: str,
               order: int = 1, move: int = 0, target: str = NULL_ID,
               inside: str = NULL_ID) -> bytes:
    """MidStack без предметов. units — id MidUnit (до 6), cells — POS_0..POS_5:
    POS[клетка] = индекс в units, -1 пусто; 0/2/4 передний ряд, 1/3/5 задний.
    inside — id города или столицы, в котором стоит отряд (гарнизон), иначе пусто."""
    if len(units) > 6 or len(cells) != 6:
        raise ValueError("отряд: до 6 юнитов и ровно 6 клеток")
    units = units + [NULL_ID] * (6 - len(units))
    return (f_str("GROUP_ID", stack_id)
            + b"".join(f_str(f"UNIT_{k}", u) for k, u in enumerate(units))
            + b"".join(f_int(f"POS_{k}", c) for k, c in enumerate(cells))
            + f_count(stack_id, 0)
            + f_str("STACK_ID", stack_id) + f_str("SRCTMPL_ID", NULL_ID)
            + f_str("LEADER_ID", leader_id) + f_bool("LEADR_ALIV", 1)
            + f_int("POS_X", x) + f_int("POS_Y", y)
            + f_int("MORALE", 0) + f_int("MOVE", move) + f_int("FACING", 0)
            + b"".join(f_str(slot, NULL_ID) for slot in _ITEM_SLOTS)
            + f_str("OWNER", owner) + f_str("INSIDE", inside) + f_str("SUBRACE", subrace)
            + f_bool("INVISIBLE", 0) + f_bool("AI_IGNORE", 0) + f_int("UPGCOUNT", 0)
            + f_int("ORDER", order) + f_str("ORDER_TARG", target)
            + f_int("AIORDER", order) + f_str("AIORDERTAR", target)
            # значения как у отрядов реальных карт (ScenEdit): приоритет ИИ 3, уровень создания 1
            + f_int("AIPRIORITY", 3) + f_int("CREAT_LVL", 1) + f_int("NBBATTLE", 0))


def parse_stack(body: bytes) -> dict:
    """Все поля MidStack; предметы — списком под ключом ITEMS."""
    c = Cursor(body)
    out = c.fields(_GROUP)
    out["ITEMS"] = [c.str("ITEM_ID") for _ in range(c.count())]
    out.update(c.fields(_STACK_TAIL))
    return out


# ------------------------------------------------- переменные, план, имя карты

def parse_vars(body: bytes) -> list[tuple[int, str, int]]:
    c = Cursor(body)
    return [(c.int("ID"), c.str("NAME"), c.int("VALUE")) for _ in range(c.count())]


def vars_body(obj_id: str, items: list[tuple[int, str, int]]) -> bytes:
    return f_count(obj_id, len(items)) + b"".join(
        f_int("ID", i) + f_str("NAME", n) + f_int("VALUE", v) for i, n, v in items)


def set_variable(sg: Sg, name: str, value: int) -> Sg:
    """Переменная сценария: заменить значение или добавить новую (id = max+1)."""
    found = sg.find(WHAT_VARS)
    if not found:
        rec = Record(WHAT_VARS, sg.next_ids("SV")[0], vars_body(f"{sg.prefix}SV0000", []))
        sg, found = sg.appended(rec), [rec]
    rec = found[0]
    items = parse_vars(rec.body)
    if any(n == name for _, n, _ in items):
        items = [(i, n, value if n == name else v) for i, n, v in items]
    else:
        items = items + [(max((i for i, _, _ in items), default=0) + 1, name, value)]
    return sg.with_record(rec, replace(rec, body=vars_body(rec.obj_id, items)))


def parse_plan(body: bytes) -> tuple[int, list[tuple[int, int, str]]]:
    """(размер карты, [(x, y, id элемента)]) — по записи на занятую клетку."""
    c = Cursor(body)
    size = c.count()
    n = c.count()
    return size, [(c.int("POS_X"), c.int("POS_Y"), c.str("ELEMENT")) for _ in range(n)]


def plan_body(obj_id: str, size: int, entries: list[tuple[int, int, str]]) -> bytes:
    return f_count(obj_id, size) + f_count(obj_id, len(entries)) + b"".join(
        f_int("POS_X", x) + f_int("POS_Y", y) + f_str("ELEMENT", e) for x, y, e in entries)


def add_plan_entries(sg: Sg, entries: list[tuple[int, int, str]]) -> Sg:
    rec = sg.one(WHAT_PLAN)
    size, old = parse_plan(rec.body)
    return sg.with_record(rec, replace(rec, body=plan_body(rec.obj_id, size, old + entries)))


def scenario_name(sg: Sg) -> str:
    c = Cursor(sg.one(WHAT_INFO).body)
    c.fields(_LAYOUT_INFO_HEAD)
    return c.str("NAME")


def header_name_offset(header: bytes) -> int:
    """Смещение quest_name: 0x140 у старого заголовка 0xab7, 0x141 у остальных."""
    return 0x140 if len(header) == HEADER_OLD_SIZE else 0x141


def header_name(sg: Sg) -> str:
    """quest_name из заголовка — то, что игра покажет в списке сценариев."""
    at = header_name_offset(sg.header)
    return sg.header[at:at + 256].split(b"\x00")[0].decode(ENC)


def set_scenario_name(sg: Sg, name: str) -> Sg:
    rec = sg.one(WHAT_INFO)
    c = Cursor(rec.body)
    c.fields(_LAYOUT_INFO_HEAD)
    start = c.pos
    c.str("NAME")
    body = rec.body[:start] + f_str("NAME", name) + rec.body[c.pos:]
    # список сценариев в игре показывает quest_name из заголовка файла (strz 256),
    # а не NAME записи ScenarioInfo — пишем в оба места
    at = header_name_offset(sg.header)
    raw = name.encode(ENC)[:255]
    header = sg.header[:at] + raw.ljust(256, b"\x00") + sg.header[at + 256:]
    return replace(sg.with_record(rec, replace(rec, body=body)), header=header)


# --------------------------------------------------------------- террейн

def tile_grid(scen: Sg) -> list[list[int]]:
    """Байт типа каждой клетки: биты 0-2 — террейн (Lterrain, 1..6), биты 3+ — грунт
    (Lground: 0 равнина, 1 лес, 3 вода, 4 горы). Блоки 8x4, id блока S139MB<yy><xx> (hex).
    Проверено на 33 картах: вода доминирует на островных, столица стоит на равнине своей расы."""
    size = struct.unpack_from("<I", scen.one("MidgardMap@@").body, 10)[0]
    grid = [[0] * size for _ in range(size)]
    for blk in scen.find("MidgardMapBlock"):
        c = Cursor(blk.body)
        bid, n = c.str("BLOCKID"), c.int("BLOCKDATA")
        data = blk.body[len(blk.body) - n:]
        by, bx = int(bid[6:8], 16), int(bid[8:10], 16)
        for i in range(n // 4):
            grid[by + i // 8][bx + i % 8] = data[i * 4]
    return grid


def passable(tile: int) -> bool:
    return (tile >> 3) in (0, 1)
