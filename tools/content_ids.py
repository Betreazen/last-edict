#!/usr/bin/env python3
r"""content_ids.py — устойчивые id содержимого по смыслу для сущностей оригинала: `legacyId → base:<вид>/<имя>`.

Пишет `src/Engine.Content/Legacy/legacy-ids.json` (ресурс сборки D2.Content): по нему импорт таблиц игры (`LegacyContent`) даёт сущностям
id вроде `base:unit/squire` вместо `g000uu0001`. Таблица лежит в репозитории, а не выводится при импорте: английские имена есть не в
каждой копии игры (русская копия — только русские тексты), а id обязаны быть одинаковыми у всех — по ним моды ссылаются на базу.
Запускается вручную, когда в базе появляются новые сущности оригинала; прежние id не меняются.

Правило имени: английское имя (`data/ui/strings.en.json` по `NAME_TXT`) в нижнем регистре через дефис. Совпало у нескольких —
уточняется (юниты — расой, ориентиры — размером), затем номером по порядку legacyId. Атака — по первому владельцу (`<юнит>`, вторая
атака — `<юнит>-2nd`, предмет — `<предмет>`, запасная — `<основная>-alt`), рост `GDynUpgr` — `<юнит>-early`/`<юнит>-late`, модификатор —
по первому пользователю (заклинание, предмет, навык лидера, вард атаки, защита столицы и города) или по комментарию таблицы.

  py -3 tools/content_ids.py            # дописать новые, прежние оставить
  py -3 tools/content_ids.py --check    # только проверить, что таблица покрывает все сущности data/rules
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
RULES = REPO / "data" / "rules"
OUT = REPO / "src" / "D2.Content" / "Legacy" / "legacy-ids.json"
EMPTY = {"", "g000000000"}
RACE_SHORT = {0: "empire", 1: "undead", 2: "legions", 3: "clans", 4: "neutral", 5: "elves"}


def rows(table: str) -> list[dict]:
    return json.loads((RULES / f"{table}.json").read_text(encoding="utf-8"))["rows"]


def slug(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def ref(value: str | None) -> str:
    v = (value or "").strip().lower()
    return "" if v in EMPTY else v


class Namer:
    """Имена одного вида: сначала база, совпавшие — уточнение, затем номер."""

    def __init__(self, kind: str):
        self.kind = kind
        self.wanted: list[tuple[str, str, str]] = []   # legacy, base, qualifier

    def add(self, legacy: str, base: str, qualifier: str = "") -> None:
        self.wanted.append((legacy.lower(), base or "unnamed", qualifier))

    def names(self) -> dict[str, str]:
        by_base: dict[str, list[tuple[str, str]]] = defaultdict(list)
        for legacy, base, q in self.wanted:
            by_base[base].append((legacy, q))
        first: dict[str, str] = {}
        for base, items in by_base.items():
            for legacy, q in items:
                first[legacy] = base if len(items) == 1 or not q else f"{base}-{q}"
        taken: dict[str, list[str]] = defaultdict(list)
        for legacy, _, _ in sorted(self.wanted):
            taken[first[legacy]].append(legacy)
        out = {}
        for name, legacies in taken.items():
            for n, legacy in enumerate(legacies):
                out[legacy] = f"base:{self.kind}/{name}" + (f"-{n + 1}" if n else "")
        return out


def tail(content_id: str) -> str:
    return content_id.split("/", 1)[1]


def build(en: dict[str, str]) -> dict[str, str]:
    def name(r: dict, col: str = "NAME_TXT") -> str:
        return slug(en.get((r.get(col) or "").upper(), ""))

    races = {r["RACE_ID"].lower(): r for r in rows("Grace")}
    race_short = {k: RACE_SHORT.get(r["RACE_TYPE"], slug(k)) for k, r in races.items()}
    ids: dict[str, str] = {}

    n = Namer("race")
    for r in rows("Grace"):
        n.add(r["RACE_ID"], name(r))
    ids |= n.names()

    units = rows("Gunits")
    n = Namer("unit")
    for u in units:
        n.add(u["UNIT_ID"], name(u), race_short.get(u["RACE_ID"].lower(), ""))
    unit_ids = n.names()
    ids |= unit_ids

    items = rows("GItem")
    n = Namer("item")
    for i in items:
        n.add(i["ITEM_ID"], name(i))
    item_ids = n.names()
    ids |= item_ids

    owner: dict[str, str] = {}
    for u in units:
        for col, suffix in (("ATTACK_ID", ""), ("ATTACK2_ID", "-2nd")):
            if (a := ref(u[col])) and a not in owner:
                owner[a] = tail(unit_ids[u["UNIT_ID"].lower()]) + suffix
    for i in items:
        if (a := ref(i["ATTACK_ID"])) and a not in owner:
            owner[a] = tail(item_ids[i["ITEM_ID"].lower()])
    attacks = rows("Gattacks")
    for a in attacks:
        if (alt := ref(a["ALT_ATTACK"])) and alt not in owner and a["ATT_ID"].lower() in owner:
            owner[alt] = owner[a["ATT_ID"].lower()] + "-alt"
    n = Namer("attack")
    for a in attacks:
        n.add(a["ATT_ID"], owner.get(a["ATT_ID"].lower()) or name(a))
    ids |= n.names()

    growth_owner: dict[str, str] = {}
    for u in units:
        for col, suffix in (("DYN_UPG1", "-early"), ("DYN_UPG2", "-late")):
            if (g := ref(u[col])) and g not in growth_owner:
                growth_owner[g] = tail(unit_ids[u["UNIT_ID"].lower()]) + suffix
    n = Namer("growth")
    for g in rows("GDynUpgr"):
        n.add(g["UPGRADE_ID"], growth_owner.get(g["UPGRADE_ID"].lower(), ""))
    ids |= n.names()

    spells = rows("Gspells")
    n = Namer("spell")
    for s in spells:
        n.add(s["SPELL_ID"], name(s))
    spell_ids = n.names()
    ids |= spell_ids

    n = Namer("building")
    for b in rows("Gbuild"):
        n.add(b["BUILD_ID"], name(b))
    ids |= n.names()

    n = Namer("lord")
    for lord in rows("Glord"):
        n.add(lord["LORD_ID"], name(lord))
    ids |= n.names()

    n = Namer("landmark")
    for m in rows("GLmark"):
        n.add(m["LMARK_ID"], name(m), "{}x{}".format(m["CX"], m["CY"]))
    ids |= n.names()

    n = Namer("thief-action")
    for a in rows("Gaction"):
        n.add(a["ACTION_ID"], name(a))
    ids |= n.names()

    mod_owner: dict[str, str] = {}

    def claim(modifier: str, label: str) -> None:
        if (m := ref(modifier)) and m not in mod_owner:
            mod_owner[m] = label

    for s in spells:
        claim(s["MODIF_ID"], tail(spell_ids[s["SPELL_ID"].lower()]))
    for i in items:
        claim(i["MOD_EQUIP"], tail(item_ids[i["ITEM_ID"].lower()]))
        claim(i["MOD_POTION"], tail(item_ids[i["ITEM_ID"].lower()]) + "-potion")
    for u in rows("GleaUpg"):
        claim(u["MODIF_ID"], "skill-" + name(u))
    for a in attacks:
        for col in ("WARD1", "WARD2", "WARD3", "WARD4"):
            claim(a[col], (owner.get(a["ATT_ID"].lower()) or name(a)) + "-ward")
    for r in rows("Grace"):
        claim(r["PROTECTION"], race_short[r["RACE_ID"].lower()] + "-capital")
    for c in rows("GcityInf"):
        claim(c["PROTECTION"], "{}-city-{}".format(race_short.get(c["RACE_ID"].lower(), "any"), c["SIZE"]))
    n = Namer("modifier")
    for m in rows("Gmodif"):
        n.add(m["MODIF_ID"], mod_owner.get(m["MODIF_ID"].lower()) or slug(m.get("COMMENTS") or ""))
    ids |= n.names()
    return ids


def main(argv: list[str]) -> int:
    sys.stdout.reconfigure(encoding="utf-8")  # type: ignore[union-attr]
    ap = argparse.ArgumentParser(description="id содержимого по смыслу для сущностей оригинала")
    ap.add_argument("--check", action="store_true")
    a = ap.parse_args(argv)
    en = {k.upper(): v for k, v in json.loads((REPO / "data" / "ui" / "strings.en.json").read_text(encoding="utf-8")).items()}
    fresh = build(en)
    old = json.loads(OUT.read_text(encoding="utf-8")) if OUT.exists() else {}
    missing = sorted(k for k in fresh if k not in old)
    if a.check:
        print(f"сущностей {len(fresh)}, в таблице {len(old)}, нет в таблице {len(missing)}")
        return 1 if missing else 0
    merged = dict(old)
    used = set(old.values())
    for k in missing:
        v, n = fresh[k], 2
        while v in used:   # новое имя не должно совпасть с прежним
            v = f"{fresh[k]}-{n}"
            n += 1
        merged[k] = v
        used.add(v)
    if len(merged) != len(set(merged.values())):
        raise SystemExit("повторы id")
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(dict(sorted(merged.items())), ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")
    print(f"{OUT.relative_to(REPO)}: {len(merged)} id ({len(missing)} новых)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
