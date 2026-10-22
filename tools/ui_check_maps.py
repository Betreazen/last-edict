"""Клетки и отряды для шагов ui-render-check из сценария импорта (Э5, задача 0).

Шаги эталона шли от сохранений корпуса журналов (`corpus/20260919-human/…`) с зашитыми клетками и id отрядов; в Last Edict корпуса нет.
Здесь шаг берёт положение на начале импортированного сценария (`data/scenarios/<имя>.json`) и находит клетки по смыслу: свой отряд,
чужой отряд, свободная проходимая клетка рядом, гора, город. Ничего не зашито: другая копия игры или мод — те же правила поиска.
"""

from __future__ import annotations

import base64
import json
import os
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path

PLAIN, FOREST, WATER, MOUNTAIN = 0, 1, 3, 4   # грунт клетки — биты 3–5 байта (src/Engine.Rules/Map/Model.cs, Tile.Ground)


@dataclass(frozen=True)
class Stack:
    id: str
    owner: str
    x: int
    y: int
    inside: str      # город, в котором стоит («» — на карте)
    leader_alive: bool
    items: int = 0   # предметов в сумке

    @property
    def cell(self) -> str:
        return f"{self.x},{self.y}"


@dataclass(frozen=True)
class Fort:
    id: str
    owner: str
    x: int
    y: int
    capital: bool


@dataclass(frozen=True)
class ScenarioMap:
    name: str
    size: int
    ground: bytes                       # грунт по клеткам, строка за строкой (y * size + x)
    plan: dict[tuple[int, int], tuple[str, ...]]   # клетка → id объектов на ней
    stacks: tuple[Stack, ...]
    forts: tuple[Fort, ...]
    objects: dict[str, tuple[str, int, int]]       # id → (вид, x, y)
    players: dict[str, str]                        # id игрока → id расы

    def ground_at(self, x: int, y: int) -> int:
        return (self.ground[y * self.size + x] >> 3) & 0b111

    def free(self, x: int, y: int) -> bool:
        """Проходимая пешим клетка без объектов и отрядов."""
        return (0 <= x < self.size and 0 <= y < self.size and self.ground_at(x, y) in (PLAIN, FOREST)
                and (x, y) not in self.plan and not any(s.x == x and s.y == y and not s.inside for s in self.stacks))

    def field_stacks(self, owner: str) -> list[Stack]:
        """Отряды игрока на карте (не в городе), с живым лидером, по id."""
        return sorted((s for s in self.stacks if s.owner == owner and not s.inside and s.leader_alive), key=lambda s: s.id)

    def free_near(self, x: int, y: int, radius: int = 1) -> tuple[int, int]:
        """Свободная клетка в квадрате ±radius вокруг (x, y), ближняя первой (по строкам — детерминированно)."""
        for r in range(1, radius + 1):
            for dy in range(-r, r + 1):
                for dx in range(-r, r + 1):
                    if max(abs(dx), abs(dy)) == r and self.free(x + dx, y + dy):
                        return x + dx, y + dy
        raise LookupError(f"{self.name}: нет свободной клетки в ±{radius} от {x},{y}")

    def nearest_foe(self, owner: str, x: int, y: int) -> Stack:
        """Ближний к клетке чужой отряд на карте (у нейтралов — тоже враг)."""
        foes = [s for s in self.stacks if s.owner != owner and not s.inside]
        return min(foes, key=lambda s: (max(abs(s.x - x), abs(s.y - y)), s.id))

    def landmark_cells(self, kind: str = "MM") -> list[tuple[int, int]]:
        """Клетки объектов вида (`MM` — ориентиры: горы, деревья…), по плану клеток."""
        return sorted(cell for cell, ids in self.plan.items() if any(self.objects.get(i, ("",))[0] == kind for i in ids))

    def capital(self, owner: str) -> Fort:
        return next(f for f in self.forts if f.owner == owner and f.capital)

    def empty_cell(self) -> tuple[int, int]:
        """Пустая клетка подальше от всего: проходимая, без объектов в квадрате ±2 (карточки нет)."""
        for y in range(2, self.size - 2):
            for x in range(2, self.size - 2):
                if all(self.free(x + dx, y + dy) for dx in range(-2, 3) for dy in range(-2, 3)):
                    return x, y
        raise LookupError(f"{self.name}: нет пустой клетки")


def scenario_path(repo: Path, name: str) -> Path:
    root = Path(os.environ.get("D2_DATA_DIR") or repo)
    return root / "data" / "scenarios" / f"{name}.json"


@lru_cache(maxsize=None)
def load(path: Path) -> ScenarioMap:
    """Карта сценария импорта (`data/scenarios/<имя>.json`) или сохранения Last Edict (`.d2save`, tools/CheckSaves) — у обоих `Looks.Map`.
    Нет файла — FileNotFoundError."""
    data = json.loads(path.read_text(encoding="utf-8"))
    m = data["Looks"]["Map"]
    return ScenarioMap(
        name=path.stem, size=m["Size"], ground=base64.b64decode(m["Tiles"]["data"]),
        plan={(p["X"], p["Y"]): tuple(p["Ids"]) for p in m["Plan"]},
        stacks=tuple(Stack(s["Id"], s["Owner"], s["X"], s["Y"], s.get("Inside") or "", s.get("LeaderAlive", True),
                           len(s.get("Items") or []))
                     for s in m["Stacks"].values()),
        forts=tuple(Fort(f["Id"], f.get("Owner") or "", f["X"], f["Y"], f["Kind"] == "Capital")
                    for f in m["Sites"] if f["Kind"] in ("Capital", "Village")),
        objects={o["Id"]: (o["Kind"], o["X"], o["Y"]) for o in m["Objects"].values()},
        players={p["Id"]: p["RaceId"] for p in m["Players"].values()})
