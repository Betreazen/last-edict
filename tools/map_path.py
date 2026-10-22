#!/usr/bin/env python3
"""Поиск пути по штатной карте: цена в очках хода от выхода укрепления до объекта.

Вынесено из `disciples-modding/docs/ai-tasks/reports/T09/scripts/make_ai_q53_map.py`
(там считалась дорога к лагерю наёмников), чтобы сборщики синтетических карт не писали свой.

Модель (ранг 3): Дейкстра по восьми соседям, цена шага — цена клетки, НА которую ступают:
равнина 2, лес 4, вода 6, дорога 1 (`Movement.cs`, 0x6027f3); без бонусов лидера и масок
движка. Горы и клетки плана непроходимы, кроме дороги, сундука, локации и (по выбору) отряда.
Старт — свободные соседи выхода укрепления (сам выход не оплачивается), цель — любая
свободная соседняя клетка входа объекта (`make_sites_map.standing`).

    python tools/map_path.py --selftest     # пересчёт всей таблицы T09 (camps.csv), ждём 44
"""
from __future__ import annotations

import argparse
import csv
import heapq
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import sgfile as sg  # noqa: E402
import make_sites_map as ms  # noqa: E402
from make_city_map import WHAT_VILLAGE, VILLAGE_TILES  # noqa: E402
from scenario_c4 import CAPITAL_SIZE  # noqa: E402

GROUND_COST = {0: 2, 1: 4, 3: 6}          # равнина/лес/вода; горы (4) — нельзя
ROAD_COST = 1
WHAT_ROAD = ".?AVCMidRoad@@"
PASSABLE_CLASSES = {WHAT_ROAD, sg.WHAT_STACK, ".?AVCMidBag@@", ".?AVCMidLocation@@"}
SITE_CLASSES = {ms.WHAT_MERCHANT, ms.WHAT_MAGE, ms.WHAT_MERCS, ms.WHAT_TRAINER}

Walls = tuple[set, set]                   # (непроходимые клетки, клетки с дорогой)


def walls(scen: sg.Sg, grid: list[list[int]], stacks_block: bool) -> Walls:
    """Непроходимы: горы и объекты плана, кроме дороги, сундука, локации и (если
    `stacks_block=False`) отряда. Второе множество — клетки с дорогой."""
    what = {r.obj_id: r.what for r in scen.records}
    _, plan = sg.parse_plan(scen.one(sg.WHAT_PLAN).body)
    size = len(grid)
    out = {(x, y) for x, y, e in plan
           if what.get(e) not in PASSABLE_CLASSES or (stacks_block and what.get(e) == sg.WHAT_STACK)}
    out |= {(x, y) for y in range(size) for x in range(size) if (grid[y][x] >> 3) not in GROUND_COST}
    roads = {(x, y) for x, y, e in plan if what.get(e) == WHAT_ROAD}
    return out, roads


def corner(scen: sg.Sg, obj_id: str) -> tuple[int, int]:
    """Выход укрепления / вход лавки — правый нижний угол пятна: столица 5x5, город 4x4,
    лавка 3x3 (`ms.entrance`, `ms.entrance_of_capital`)."""
    rec = next((r for r in scen.records if r.obj_id == obj_id), None)
    if rec is None:
        raise KeyError(f"объекта {obj_id} нет в карте")
    if rec.what in SITE_CLASSES:
        f = ms.parse_site(rec.what, rec.body)
        return ms.entrance((int(f["POS_X"]), int(f["POS_Y"])))
    tiles = {sg.WHAT_CAPITAL: CAPITAL_SIZE, WHAT_VILLAGE: VILLAGE_TILES}.get(rec.what)
    if tiles is None:
        raise ValueError(f"{obj_id}: класс {rec.what} — не укрепление и не лавка")
    f = sg.fields(rec.body, sg.LAYOUT_CAPITAL)
    return int(f["POS_X"]) + tiles - 1, int(f["POS_Y"]) + tiles - 1


def capital_exit(scen: sg.Sg, player_id: str) -> tuple[int, int] | None:
    """Выход столицы игрока; None — у игрока нет столицы (нейтралы)."""
    for r in scen.find(sg.WHAT_CAPITAL):
        if sg.fields(r.body, sg.LAYOUT_CAPITAL)["OWNER"] == player_id:
            return corner(scen, r.obj_id)
    return None


def path_cost(w: Walls, grid: list[list[int]], src: tuple, dst: tuple) -> int | None:
    """Очки хода от выхода `src` до соседней с входом `dst` клетки; None — не дойти."""
    blocked, roads = w
    size = len(grid)

    def cost(c: tuple) -> int:
        return ROAD_COST if c in roads else GROUND_COST[grid[c[1]][c[0]] >> 3]

    goals = ms.standing(dst, blocked, size)
    dist = {c: cost(c) for c in ms.standing(src, blocked, size)}
    queue = [(d, c) for c, d in dist.items()]
    heapq.heapify(queue)
    while queue:
        d, cur = heapq.heappop(queue)
        if cur in goals:
            return d
        if d > dist[cur]:
            continue
        for nxt in ms.around([cur], size) - blocked:
            nd = d + cost(nxt)
            if nd < dist.get(nxt, 1 << 30):
                dist[nxt] = nd
                heapq.heappush(queue, (nd, nxt))
    return None


def route_cost(scen: sg.Sg, player_id: str, obj_id: str, stacks_block: bool = False) -> int | None:
    """Удобная обёртка: от выхода столицы игрока до объекта `obj_id` (лавка, город, столица)."""
    grid = sg.tile_grid(scen)
    src = capital_exit(scen, player_id)
    if src is None:
        raise ValueError(f"у игрока {player_id} нет столицы")
    return path_cost(walls(scen, grid, stacks_block), grid, src, corner(scen, obj_id))


# ------------------------------------------------------------------ самопроверка

T09_CSV = ms.MODDING / "docs" / "ai-tasks" / "reports" / "T09" / "data" / "camps.csv"
PRISTINE = ms.MODDING / "game-pristine" / "Exports"


def _player_by_race(scen: sg.Sg, race: str) -> str:
    return next(str(p["PLAYER_ID"]) for p in (sg.fields(r.body, sg.LAYOUT_PLAYER)
                                              for r in scen.find(sg.WHAT_PLAYER))
                if str(p["RACE_ID"]).upper() == race)


def _recompute(rows: list[dict]) -> tuple[int, int, list[str]]:
    same = diff = 0
    notes: list[str] = []
    cache: dict[str, tuple] = {}
    for row in rows:
        if row["map"] not in cache:
            scen = sg.Sg.read(PRISTINE / row["map"])
            grid = sg.tile_grid(scen)
            cache[row["map"]] = (scen, grid, {k: walls(scen, grid, k) for k in (False, True)})
        scen, grid, w = cache[row["map"]]
        pid = _player_by_race(scen, row["race"])
        src, dst = capital_exit(scen, pid), corner(scen, row["camp"])
        got = (f"{src[0]},{src[1]}", f"{dst[0]},{dst[1]}",
               str(path_cost(w[False], grid, src, dst)), str(path_cost(w[True], grid, src, dst)))
        want = (row["capital_exit"], row["entrance"],
                row["move_cost_through_stacks"], row["move_cost_around_stacks"])
        got = tuple("" if g == "None" else g for g in got)     # csv пишет None пустой строкой
        ok = got == want
        same += ok
        diff += not ok
        if not ok:
            notes.append(f"  {row['map']} {row['camp']} {row['race']}: {got} != {want}")
    return same, diff, notes


def selftest() -> int:
    global ROAD_COST
    rows = list(csv.DictReader(T09_CSV.open(encoding="utf-8")))
    same, diff, notes = _recompute(rows)
    print("\n".join(notes))
    print(f"camps_csv сошлось={same} разошлось={diff}")
    scen = sg.Sg.read(PRISTINE / "'A Return To Simpler Times'.sg")
    legion = _player_by_race(scen, "G000RR0002")
    got = [route_cost(scen, legion, "S001SI0002", k) for k in (False, True)]
    print(f"t09_legions_to_S001SI0002 {got} (ждём [44, 44])")
    ROAD_COST = 2                          # порча модели: сторож обязан поймать
    try:
        _, bad, _ = _recompute(rows)
    finally:
        ROAD_COST = 1
    print(f"порча «дорога стоит 2»: разошлось={bad} {'ПОЙМАЛ' if bad else 'ПРОПУСТИЛ'}")
    ok = diff == 0 and same == len(rows) > 0 and got == [44, 44] and bad > 0
    print("selftest", "OK" if ok else "FAIL")
    return 0 if ok else 1


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    p.add_argument("--selftest", action="store_true")
    a = p.parse_args(argv)
    if a.selftest:
        return selftest()
    p.print_help()
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
