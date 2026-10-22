#!/usr/bin/env python3
r"""Режим «снимки для попиксельной сверки» в Disciple.ini копии игры и обратно.

Сверка карты и окон со снимком (docs/ai-screen.md §14) ждёт кадр движка 800×600 без HD-обёртки: HD-режим
обёртки DisciplesGL рисует в другом разрешении, HD-пакет подменяет картинки, широкий и зеркальный бой меняют
раскладку. Снимок по F12 пишет обёртка (`ScreenShots\ScreenShotNNN.png`; до движка F12 не доходит — обёртка его
глотает), зонд в момент нажатия пишет клетку камеры (событие `screenshot`, `via = key`).

Правка байтовая — через `../disciples-modding/tools/gamedrv/ini.py` (правило того репозитория: ini целиком не
переписывать). Прежние значения сохраняются в `.cache/pixel-ini.json` и возвращаются командой `off`.

    python tools/pixel_ini.py on       # перед игрой
    python tools/pixel_ini.py off      # после, когда игра закрыта
    python tools/pixel_ini.py status
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
GAMEDRV = REPO.parent / "disciples-modding" / "tools" / "gamedrv"
STATE = REPO / ".cache" / "pixel-ini.json"

# (секция, ключ, значение на время снимков)
PIXEL = [
    ("Wrapper", "HD", "0"),
    ("Wrapper", "HDPack", "0"),
    ("Wrapper", "WideBattle", "0"),
    ("Wrapper", "MirrorBattle", "0"),
    ("Wrapper", "EnableZoom", "0"),
    ("HD", "Enabled", "0"),
]


def ini_module():
    if not (GAMEDRV / "ini.py").exists():
        raise SystemExit(f"нет {GAMEDRV / 'ini.py'} — нужен соседний репозиторий disciples-modding")
    sys.path.insert(0, str(GAMEDRV))
    import ini  # noqa: PLC0415
    return ini


def status(ini) -> None:
    for section, key, want in PIXEL:
        now = ini.get(section, key)
        print(f"[{section}] {key} = {now!r}  (для снимков {want!r})")
    print(f"сохранённые значения: {'есть' if STATE.exists() else 'нет'} ({STATE})")


def on(ini) -> int:
    if STATE.exists():
        print(f"режим уже включён ({STATE}); сначала `off`")
        return 1
    before = {f"{s}.{k}": ini.get(s, k) for s, k, _ in PIXEL}
    missing = [name for name, value in before.items() if value is None]
    if missing:
        print("в Disciple.ini нет ключей: " + ", ".join(missing) + " — ничего не менял")
        return 1
    STATE.parent.mkdir(exist_ok=True)
    STATE.write_text(json.dumps(before, ensure_ascii=False, indent=1), encoding="utf-8")
    for section, key, value in PIXEL:
        ini.set(section, key, value)
    status(ini)
    return 0


def off(ini) -> int:
    if not STATE.exists():
        print("режим не включён — возвращать нечего")
        return 1
    before = json.loads(STATE.read_text(encoding="utf-8"))
    for section, key, _ in PIXEL:
        ini.set(section, key, before[f"{section}.{key}"])
    STATE.unlink()
    status(ini)
    return 0


def main() -> int:
    command = sys.argv[1] if len(sys.argv) > 1 else "status"
    ini = ini_module()
    if command == "on":
        return on(ini)
    if command == "off":
        return off(ini)
    status(ini)
    return 0


if __name__ == "__main__":
    sys.exit(main())
