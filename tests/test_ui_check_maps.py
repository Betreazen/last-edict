"""tools/ui_check_maps.py — поиск клеток для шагов ui-render-check на выдуманной карте 8×8 (без данных игры)."""
from __future__ import annotations

import base64
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import ui_check_maps  # noqa: E402

SIZE = 8
PLAIN, WATER, MOUNTAIN = 0, 3, 4


def grid(fill: int, special: dict[tuple[int, int], int] | None = None) -> str:
    cells = [fill] * SIZE * SIZE
    for (x, y), v in (special or {}).items():
        cells[y * SIZE + x] = v
    return base64.b64encode(bytes(cells)).decode()


def write_map(tmp: Path) -> Path:
    tiles = grid(PLAIN << 3, {(3, 2): WATER << 3, (5, 5): MOUNTAIN << 3})
    fog = grid(0, {(7, 7): 1, (6, 7): 1})
    data = {"Looks": {"Map": {
        "Size": SIZE, "Tiles": {"w": SIZE, "h": SIZE, "data": tiles},
        "Plan": [{"X": 5, "Y": 5, "Ids": ["S1MM0000"]}, {"X": 1, "Y": 1, "Ids": ["S1FT0000"]}],
        "Objects": {"S1MM0000": {"Id": "S1MM0000", "Kind": "MM", "X": 5, "Y": 5}},
        "Units": {"U1": {"TypeId": "base:unit/thief", "Hp": 50}, "U2": {"TypeId": "base:unit/knight", "Hp": 100},
                  "G1": {"Hp": 10}, "G2": {"Hp": 0}},
        "Stacks": {
            "S1KC0001": {"Id": "S1KC0001", "Owner": "P1", "X": 2, "Y": 2, "Inside": "", "LeaderId": "U1", "Items": ["i1", "i2"]},
            "S1KC0002": {"Id": "S1KC0002", "Owner": "P1", "X": 1, "Y": 1, "Inside": "S1FT0000", "LeaderId": "U2"},
            "S1KC0003": {"Id": "S1KC0003", "Owner": "P2", "X": 4, "Y": 4, "Inside": "", "LeaderId": "U2"},
        },
        "Sites": [{"Id": "S1FT0000", "Kind": "Capital", "X": 1, "Y": 1, "Owner": "P1"},
                  {"Id": "S1RU0000", "Kind": "Ruin", "X": 6, "Y": 1, "Guards": ["G1", "G2"]},
                  {"Id": "S1RU0001", "Kind": "Ruin", "X": 2, "Y": 5, "Guards": ["G2"]}],
        "Players": {"P1": {"Id": "P1", "RaceId": "base:race/a", "FogId": "F1"}, "P2": {"Id": "P2", "RaceId": "base:race/b", "FogId": "F2"}},
        "Fog": {"F1": {"w": SIZE, "h": SIZE, "data": fog}},
    }}}
    path = tmp / "tiny.d2save"
    path.write_text(json.dumps(data), encoding="utf-8")
    return path


def test_field_stacks_skip_garrisons_and_carry_leader_and_items(tmp_path: Path) -> None:
    m = ui_check_maps.load(write_map(tmp_path))
    own = m.field_stacks("P1")
    assert [s.id for s in own] == ["S1KC0001"]
    assert own[0].items == 2 and own[0].leader == "base:unit/thief" and own[0].cell == "2,2"


def test_free_near_skips_water_objects_and_stacks(tmp_path: Path) -> None:
    m = ui_check_maps.load(write_map(tmp_path))
    assert not m.free(3, 2)            # вода
    assert not m.free(1, 1)            # план клеток — город
    assert not m.free(4, 4)            # чужой отряд
    assert m.free_near(2, 2) == (2, 1)  # первая по строкам среди свободных вокруг (1,1 — город)


def test_nearest_foe_ruins_and_capital(tmp_path: Path) -> None:
    m = ui_check_maps.load(write_map(tmp_path))
    assert m.nearest_foe("P1", 2, 2).id == "S1KC0003"
    assert m.nearest_site("Ruin", 2, 2).id == "S1RU0001"                  # ближняя
    assert m.nearest_site("Ruin", 2, 2, guarded=True).id == "S1RU0000"    # у ближней охрана мертва
    assert m.capital("P1").id == "S1FT0000"


def test_fog_and_landmarks(tmp_path: Path) -> None:
    m = ui_check_maps.load(write_map(tmp_path))
    assert m.seen("P1", 0, 0) and not m.seen("P1", 7, 7)
    assert m.seen("P2", 7, 7)          # у P2 тумана в карте нет — всё открыто
    assert m.fogged_cell("P1", 0, 7) == (6, 7)
    assert m.landmark_cells(seen_by="P1") == [(5, 5)]
