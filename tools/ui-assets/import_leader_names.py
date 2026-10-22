#!/usr/bin/env python3
"""import_leader_names.py — имена героев по умолчанию из Tleader.dbf игры в data/ui/leader-names.{en,ru}.json.

Окно имени нового героя (DLG_GETINFO_BOX, CChangeLeaderNameInterf, 0x4b02f3) предлагает имя из Tleader.dbf: по расе и полу
нанятого, из наименее занятых лидерами на карте — случайное. Английская таблица — в копии игры (game-pristine), русская — в
локализации «Руссобит-М» (disciples-vanilla/l10n/russobit, кодировка cp866). Порт эти .dbf в рантайме не читает — импорт один
раз (docs/principles.md §3). Правила (data/rules/Tleader.json, имена ИИ) этот файл не трогает.

Запуск: python tools/ui-assets/import_leader_names.py
"""
from __future__ import annotations

import json
import os
import struct
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
PROJECTS = REPO.parent
SOURCES = {
    "en": (Path(os.environ.get("D2_GAME_DIR") or PROJECTS / "disciples-modding" / "game-pristine") / "Globals" / "Tleader.dbf", "cp1252"),
    "ru": (PROJECTS / "disciples-vanilla" / "l10n" / "russobit" / "Globals" / "Tleader.dbf", "cp866"),
}


def read_dbf(path: Path, encoding: str) -> list[dict[str, str]]:
    data = path.read_bytes()
    count = struct.unpack("<I", data[4:8])[0]
    header, record = struct.unpack("<HH", data[8:12])
    fields, offset = [], 32
    while data[offset] != 0x0D:
        name = data[offset:offset + 11].split(b"\0")[0].decode("ascii")
        fields.append((name, data[offset + 16]))
        offset += 32
    rows = []
    for i in range(count):
        raw = data[header + i * record:header + (i + 1) * record]
        if raw[:1] == b"*":
            continue
        pos, row = 1, {}
        for name, length in fields:
            row[name] = raw[pos:pos + length].decode(encoding).strip()
            pos += length
        rows.append(row)
    return rows


def main() -> None:
    for lang, (path, encoding) in SOURCES.items():
        if not path.exists():
            print(f"нет {path} — {lang} пропущен")
            continue
        races: dict[str, dict[str, list[str]]] = {}
        for row in read_dbf(path, encoding):
            race = races.setdefault(row["RACE_ID"].upper(), {"Male": [], "Female": []})
            # SEX_M: T — мужское; F и пусто («?» у части эльфийских) — женское.
            race["Male" if row["SEX_M"].upper() == "T" else "Female"].append(row["TEXT"])
        out = REPO / "data" / "ui" / f"leader-names.{lang}.json"
        out.write_text(json.dumps({"Races": races}, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
        print(f"{lang}: {sum(len(v['Male']) + len(v['Female']) for v in races.values())} имён -> {out}")


if __name__ == "__main__":
    main()
