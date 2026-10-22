#!/usr/bin/env python3
"""Самопроверка новых аргументов генераторов (пачка 0 синтетических карт):
запись -> разбор -> поле равно заданному; умолчания дают прежние байты.

    python tools/gen_args_check.py

Читает только шаблон `make_c4_map.TEMPLATE`, ничего не пишет.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import sgfile as sg  # noqa: E402
import make_c4_map as c4  # noqa: E402
import make_city_map as city  # noqa: E402
import make_f7_patch_maps as mp  # noqa: E402
import f7_plan as fp  # noqa: E402
import scenario_squads as sq  # noqa: E402
import sg_second_player as sp  # noqa: E402

FAILS: list[str] = []


def eq(name: str, got, want) -> None:
    ok = got == want
    print(f"{name}: {'ok' if ok else 'РАЗОШЛОСЬ'} got={repr(got)[:60]} want={repr(want)[:60]}")
    if not ok:
        FAILS.append(name)


def check_village() -> None:
    args = ("S143FT0001", "Город", "S143PL0000", "S143SR0000", 7, 9, 2)
    for el in (True, False):
        default = city.village_body(*args, has_p_o_el=el)
        eq(f"village el={el}: умолчание = явные прежние значения",
           city.village_body(*args, has_p_o_el=el, protect_b=sg.NULL_ID, regen_b=0, riot_t=0),
           default)
        body = city.village_body(*args, has_p_o_el=el, protect_b="G000UM0021", regen_b=15, riot_t=3)
        v = city.parse_village(body, has_p_o_el=el)
        eq(f"village el={el}: PROTECT_B", v["PROTECT_B"], "G000UM0021")
        eq(f"village el={el}: REGEN_B", v["REGEN_B"], 15)
        eq(f"village el={el}: RIOT_T", v["RIOT_T"], 3)
        eq(f"village el={el}: длина тела не изменилась", len(body), len(default))


def unit_hp_xp(rec: sg.Record) -> tuple[int, int]:
    c = sg.Cursor(rec.body)
    c.fields(sg.LAYOUT_UNIT)
    for _ in range(c.count()):
        c.str("MODIF_ID")
    c.fields("CREATION:i NAME_TXT:s TRANSF:b DYNLEVEL:b")
    hp, xp = c.int("HP"), c.int("XP")
    if c.pos != len(rec.body):
        raise SystemExit(f"тело юнита {rec.obj_id} разобрано не до конца")
    return hp, xp


def check_party() -> None:
    t = sq.Tables()
    scen = sg.Sg.read(c4.TEMPLATE)
    human, _ = mp.players(scen)
    cap = next(r for r in scen.find(sg.WHAT_CAPITAL)
               if sg.fields(r.body, sg.LAYOUT_CAPITAL)["OWNER"] == human["PLAYER_ID"])
    subrace = str(sg.fields(cap.body, sg.LAYOUT_CAPITAL)["SUBRACE"])
    base = next(p for k in fp.ORDER for p in fp.profile(k)["parties"] if len(p["units"]) >= 2)
    base = {**base, "xy": (3, 3), "leader_hp": None,
            "units": [(base["units"][0][0], None), (base["units"][1][0], 7)]}
    before = {r.obj_id for r in scen.find(sg.WHAT_UNIT)}

    def new_units(s: sg.Sg) -> list[sg.Record]:
        return [r for r in s.find(sg.WHAT_UNIT) if r.obj_id not in before]

    plain = new_units(mp.add_party(scen, base, human["PLAYER_ID"], subrace, t))
    eq("party без опыта: XP везде 0", [unit_hp_xp(r)[1] for r in plain], [0, 0, 0])
    xp = {**base, "leader_xp": 123, "units_xp": [45, None]}
    got = new_units(mp.add_party(scen, xp, human["PLAYER_ID"], subrace, t))
    full = [int(t.unit(u)["HIT_POINT"]) for u in (base["leader"], base["units"][0][0])]
    eq("party: (HP, XP) лидера", unit_hp_xp(got[0]), (full[0], 123))
    eq("party: солдат без раны с опытом — полное HP", unit_hp_xp(got[1]), (full[1], 45))
    eq("party: раненый без опыта", unit_hp_xp(got[2]), (7, 0))
    eq("party: прочие записи не тронуты", [r.body for r in plain[2:]], [r.body for r in got[2:]])


def check_relation() -> None:
    for cur, prev in ((0, None), (3, None), (5, 2), (127, 127)):
        w = sp.relation_word(cur, prev)
        eq(f"relation {cur},{prev}: умолчание без союза", w & 1, 0)
        a = sp.relation_word(cur, prev, alliance=True)
        eq(f"relation {cur},{prev}: alliance ставит только бит 0", (a & 1, a & ~1), (1, w))
        eq(f"relation {cur},{prev}: разбор", ((a >> 1) & 0x7f, (a >> 8) & 0x7f),
           (cur, cur if prev is None else prev))
    word = sp.relation_word(4, alliance=True)
    scen = sp.set_diplomacy(sg.Sg.read(c4.TEMPLATE), [(0, 1, word)])
    c = sg.Cursor(scen.one(sp.WHAT_DIPLO).body)
    c.count()
    eq("relation: запись -> разбор RELATION", c.fields("RACE_1:i RACE_2:i RELATION:i")["RELATION"], word)


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    check_village()
    check_party()
    check_relation()
    print(f"провалов {len(FAILS)}: {FAILS}")
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
