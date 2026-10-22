#!/usr/bin/env python3
"""import_capital.py — слои картинки столицы из Imgs/capital.dat игры в data/ui/capital.json.

Столица на экране — стопка картинок 640×600 из Capital.ff: фон, сама столица, анимации и по картинке на каждую
постройку. Какая картинка какой постройке принадлежит и в каком порядке они кладутся, игра читает из capital.dat
(ini: раздел на расу, строки LAYER_<номер>=<постройка>,<картинка>,<?>,<x>,<y>,<…>,<x1>,<y1>,<x2>,<y2>). Порт этот файл
в рантайме не читает — импорт один раз, как у экранов и графики (docs/principles.md §3).

Номер слоя — порядок наложения (меньший ниже). Картинка — кадр или анимация Capital.ff (у эльфов постройки —
анимации своего размера в точке x, y). Последние четыре числа — прямоугольник постройки на картинке
(-1 — нет): по нему игра подсвечивает постройку под мышью. Остальные поля переносятся как есть (Legacy).

Игра берётся из D2_GAME_DIR (по умолчанию game-pristine рядом). Запуск: python tools/ui-assets/import_capital.py
"""
from __future__ import annotations

import json
import os
import re
from pathlib import Path

DEFAULT_GAME_DIR = Path(__file__).resolve().parents[3] / "disciples-modding" / "game-pristine"  # рядом с репозиторием
REPO = Path(__file__).resolve().parents[2]

# Раздел capital.dat → Grace.RACE_TYPE (0 Империя, 1 Орды нежити, 2 Легионы, 3 Кланы, 5 Эльфы).
RACE_TYPES = {"HUMAN": 0, "UNDEAD": 1, "HERETIC": 2, "DWARF": 3, "ELF": 5}


def parse(text: str) -> list[dict]:
    races: list[dict] = []
    section = None
    for raw in text.splitlines():
        line = raw.strip()
        if m := re.fullmatch(r"\[(\w+)\]", line):
            section = {"Race": m.group(1), "RaceType": RACE_TYPES[m.group(1)], "Layers": []}
            races.append(section)
            continue
        m = re.fullmatch(r"LAYER_(\d+)=(.*)", line)
        if not m or section is None:
            continue
        f = m.group(2).split(",")
        rect = [int(v) for v in f[-4:]]
        layer = {
            "Z": int(m.group(1)),
            "Image": f[1],
            "X": int(f[3] or 0),
            "Y": int(f[4] or 0),
        }
        if f[0]:
            layer["Building"] = f[0].upper()
        if rect[0] >= 0:
            layer["Hotspot"] = {"X": rect[0], "Y": rect[1], "Width": rect[2] - rect[0], "Height": rect[3] - rect[1]}
        layer["Legacy"] = [f[2], *f[5:-4]]
        section["Layers"].append(layer)
    for r in races:
        r["Layers"].sort(key=lambda l: l["Z"])
    return races


def main() -> None:
    game = Path(os.environ.get("D2_GAME_DIR") or DEFAULT_GAME_DIR)
    text = (game / "Imgs" / "capital.dat").read_bytes().decode("cp1251")
    races = parse(text)
    out = REPO / "data" / "ui" / "capital.json"
    out.write_text(json.dumps({"Races": races}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"{out}: рас {len(races)}, слоёв {sum(len(r['Layers']) for r in races)}")


if __name__ == "__main__":
    main()
