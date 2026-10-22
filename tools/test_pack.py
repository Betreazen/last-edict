#!/usr/bin/env python3
r"""test_pack.py — выпускает content/test-pack: своё мини-содержимое правил с чистой лицензией (CC0) для CI.

Данных оригинальной игры в нём нет: имена, числа и раскладка придуманы здесь (две играбельные расы «Солнце» и «Луна» и дикие
нейтралы). Формат — тот же, что у мода base (docs/content-schema.md): правила читают его через ContentSource.Open, как импорт копии
игры. Тесты CI (снимки «порт против порта» уровня test-pack, проверка загрузки) берут его без импорта — docs/own-version-roadmap.md §3.7.

  py tools/test_pack.py            # переписать content/test-pack/*.json
  py tools/test_pack.py --check    # код 1, если файлы на диске разошлись с генератором
"""
from __future__ import annotations

import argparse
import base64
import json
import random
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "content" / "test-pack"
SCHEMA = "../../data/content/schema/{}.schema.json"
MOD = "test"


def ref(kind: str, name: str) -> str:
    return f"{MOD}:{kind}/{name}"


# ---- расы ---------------------------------------------------------------------------------------------------------------------------
# Каждая играбельная раса: (имя, род, подраса, мана, земля, место в очереди, флаг событий, имена героев).
RACES = [
    ("sun", "human", "human", "life", "human", 0, 0, ["Aldric", "Bertha", "Cedric", "Dalia", "Edmund", "Freya"]),
    ("moon", "undead", "undead", "death", "undead", 1, 1, ["Morwen", "Nyx", "Orrin", "Pyra", "Quill", "Rook"]),
]

# Юниты играбельной расы: (имя, роль, ветка, уровень, здоровье, броня, атака: (эффект, источник, досягаемость, урон/лечение,
# инициатива, точность), опыт за гибель, опыт до уровня, цена найма, прочее).
def race_units(race: str, mana: str) -> list[dict]:
    special = ("heal", "life", "any", 25, 20, 100) if race == "sun" else ("paralyze", "death", "any", 0, 40, 75)
    rows = [
        ("colossus", "guardian", "hero", 1, 700, 40, ("damage", "weapon", "adjacent", 90, 50, 85), 5000, 6000, 8000,
         {"move": 20, "scout": 4, "leadership": 6, "negotiate": 60, "regeneration": 40}),
        ("thief", "noble", "noble", 1, 90, 0, ("damage", "weapon", "adjacent", 20, 60, 80), 20, 90, 180,
         {"move": 24, "scout": 5, "leadership": 1, "negotiate": 15, "leaderCategory": "noble"}),
        ("captain", "leader", "hero", 1, 140, 0, ("damage", "weapon", "adjacent", 45, 50, 80), 55, 140, 450,
         {"move": 20, "scout": 4, "leadership": 4, "negotiate": 10, "leaderCategory": "fighter"}),
        ("ranger", "leader", "hero", 1, 110, 0, ("damage", "weapon", "any", 30, 60, 80), 45, 130, 420,
         {"move": 26, "scout": 6, "leadership": 3, "negotiate": 10, "leaderCategory": "explorer"}),
        ("sage", "leader", "hero", 1, 90, 0, ("damage", "fire" if race == "sun" else "death", "all", 25, 40, 80), 50, 140, 480,
         {"move": 20, "scout": 4, "leadership": 3, "negotiate": 10, "leaderCategory": "mage"}),
        ("warden", "leader", "hero", 1, 100, 0, ("damage", "weapon", "any", 25, 50, 80), 45, 130, 400,
         {"move": 20, "scout": 4, "leadership": 3, "negotiate": 10, "leaderCategory": "rod"}),
        ("footman", "soldier", "fighter", 1, 110, 0, ("damage", "weapon", "adjacent", 28, 50, 80), 18, 85, 55, {}),
        ("bowman", "soldier", "archer", 1, 50, 0, ("damage", "weapon", "any", 22, 60, 80), 16, 75, 45, {}),
        ("adept", "soldier", "mage", 1, 40, 0, ("damage", "air" if race == "sun" else "death", "all", 14, 40, 80), 18, 80, 50, {}),
        ("healer" if race == "sun" else "hexer", "soldier", "special", 1, 55, 0, special, 18, 80, 50, {}),
        ("veteran", "soldier", "fighter", 2, 160, 0, ("damage", "weapon", "adjacent", 45, 50, 85), 55, 400, 280,
         {"previousLevel": "footman", "upgradeBuilding": "barracks"}),
        ("marksman", "soldier", "archer", 2, 75, 0, ("damage", "weapon", "any", 38, 60, 85), 50, 380, 260,
         {"previousLevel": "bowman", "upgradeBuilding": "range"}),
    ]
    return [unit(race, race, mana, *row) for row in rows]


NEUTRAL_UNITS = [
    ("wolf", "neutralSoldier", "fighter", 1, 70, 0, ("damage", "weapon", "adjacent", 22, 55, 80), 15, 80, 60, {}),
    ("ogre", "neutralSoldier", "fighter", 2, 260, 10, ("damage", "weapon", "adjacent", 60, 25, 75), 80, 300, 350, {"small": False}),
    ("shaman", "neutralSoldier", "mage", 1, 45, 0, ("poison", "earth", "any", 12, 45, 80), 20, 90, 70, {}),
    ("chief", "neutralLeader", "hero", 2, 150, 0, ("damage", "weapon", "adjacent", 50, 50, 80), 70, 250, 400,
     {"move": 18, "scout": 4, "leadership": 4, "leaderCategory": "fighter"}),
    ("bat", "neutralSoldier", "fighter", 1, 40, 0, ("drain", "death", "adjacent", 18, 70, 80), 12, 70, 40, {}),
]


def unit(race: str, prefix: str, mana: str, name: str, role: str, branch: str, level: int, hp: int, armor: int,
         attack: tuple, xp_kill: int, xp_next: int, price: int, extra: dict) -> dict:
    full = f"{prefix}-{name}"
    u = {
        "id": ref("unit", full), "role": role, "level": level, "race": ref("race", race),
        "subrace": "human" if race == "sun" else "undead" if race == "moon" else "neutral",
        "branch": branch, "small": extra.pop("small", True), "male": True, "hireCost": {"gold": price},
        "attack": ref("attack", full), "attacksTwice": False, "hitPoints": hp, "armor": armor,
        "regeneration": extra.pop("regeneration", 5), "reviveCost": {"gold": max(50, price // 2)}, "healCost": {"gold": 1},
        "trainingCost": {"gold": 4}, "xpForKill": xp_kill, "xpForNextLevel": xp_next,
        "growth": {"early": ref("growth", full), "switchLevel": 10, "late": ref("growth", full)},
        "deathAnimation": "human" if race == "sun" else "undead" if race == "moon" else "neutral",
    }
    if "previousLevel" in extra:
        u["previousLevel"] = ref("unit", f"{prefix}-{extra.pop('previousLevel')}")
    if "upgradeBuilding" in extra:
        u["upgradeBuilding"] = ref("building", f"{prefix}-{extra.pop('upgradeBuilding')}")
    u.update(extra)
    u["_attack"] = attack
    return u


def attack_of(u: dict) -> dict:
    effect, source, reach, amount, initiative, power = u.pop("_attack")
    a = {"id": u["attack"], "initiative": initiative, "source": source, "effect": ref("effect", effect), "power": power,
         "reach": reach}
    if effect == "heal":
        a["heal"] = amount
    elif amount:
        a["damage"] = amount
    return a


def growth_of(u: dict) -> dict:
    hp = max(5, u["hitPoints"] // 10)
    return {"id": u["growth"]["early"], "hireCost": {"gold": 5}, "hitPoints": hp, "xpForKill": 2, "move": 0, "negotiate": 0,
            "damage": 3, "power": 1}


EFFECTS = {
    "damage": {"classFactor": 1, "classFactorAlt": 1, "secondFactor": 1, "xpFactor": 1},
    "heal": {"classFactor": 1, "classFactorAlt": 1, "secondFactor": 1, "fullPower": True, "xpFactor": 2, "xpRank": 0},
    "paralyze": {"classFactor": 25, "classFactorAlt": 1, "secondFactor": 1, "infiniteCounts": True, "xpFactor": 6, "xpRank": 1},
    "poison": {"classFactor": 1, "classFactorAlt": 1, "secondFactor": 1.2, "infiniteCounts": True, "xpFactor": 1},
    "drain": {"classFactor": 1.4, "classFactorAlt": 1, "secondFactor": 1, "xpFactor": 1},
}


def modifier(name: str, comment: str, elements: list[dict], scope: str = "unit") -> dict:
    return {"id": ref("modifier", name), "scope": scope, "comment": comment, "elements": elements}


def armor(number: int) -> dict:
    return {"type": "armor", "number": number, "immuneLevel": 2}


def build() -> dict[str, list[dict]]:
    units, races, buildings, lords, cities, modifiers, spells, skills = [], [], [], [], [], [], [], []
    for name, kind, _sub, mana, land, order, flag, heroes in RACES:
        mine = race_units(name, mana)
        units += mine
        modifiers += [modifier(f"{name}-capital", "capital protection", [armor(40)]),
                      modifier(f"{name}-city", "city protection", [armor(15)])]
        races.append({
            "id": ref("race", name), "guardian": ref("unit", f"{name}-colossus"), "noble": ref("unit", f"{name}-thief"),
            "leaders": [ref("unit", f"{name}-{x}") for x in ("captain", "ranger", "sage", "warden")],
            "soldiers": [ref("unit", f"{name}-{x}") for x in ("footman", "bowman", "adept", "healer" if name == "sun" else "hexer")],
            "scout": 4, "capitalRegeneration": 30, "income": {"gold": 120, mana: 20}, "protection": ref("modifier", f"{name}-capital"),
            "kind": kind, "playable": True, "land": land, "turnOrder": order, "eventFlag": flag, "neutral": False,
            "heroNames": [{"text": h, "male": i % 2 == 0} for i, h in enumerate(heroes)],
        })
        own = [("barracks", "unit", "fighter", 2, 250), ("range", "unit", "archer", 2, 230), ("guild", "guild", None, 1, 300),
               ("temple", "heal", None, 1, 300), ("tower", "magic", None, 1, 350)]
        for b, category, branch, level, cost in own:
            entry = {"id": ref("building", f"{name}-{b}"), "cost": {"gold": cost}, "level": level, "category": category}
            if branch:
                entry["branch"] = branch
            buildings.append(entry)
        spell_ids = [ref("spell", f"{name}-bolt"), ref("spell", f"{name}-ward")]
        modifiers.append(modifier(f"{name}-ward", "spell: armor", [armor(20)]))
        spells += [
            {"id": spell_ids[0], "category": "attack", "level": 1, "castCost": {mana: 80}, "buyCost": {"gold": 250}, "area": 1,
             "damageSource": "fire" if name == "sun" else "death", "damage": 30, "aiUse": "all"},
            {"id": spell_ids[1], "category": "boost", "level": 1, "castCost": {mana: 60}, "buyCost": {"gold": 200}, "area": 1,
             "modifier": ref("modifier", f"{name}-ward"), "aiUse": "all"},
        ]
        lords.append({"id": ref("lord", f"{name}-lord"), "race": ref("race", name), "category": "warrior",
                      "buildings": [ref("building", f"{name}-{b[0]}") for b in own],
                      "research": [{"spell": s, "cost": {mana: 150}} for s in spell_ids]})
        modifiers.append(modifier(f"{name}-skill-armor", "skill: armor", [armor(10)]))
        skills.append({"id": ref("leader-skill", f"{name}-lord--armor"), "lord": ref("lord", f"{name}-lord"),
                       "modifier": ref("modifier", f"{name}-skill-armor"), "minLevel": 2, "priority": 50})
        for size in range(1, 6):
            cities.append({"id": ref("city", f"{name}-{size}"), "race": ref("race", name), "size": size, "scout": 2 + size,
                           "regeneration": 5 + 5 * size, "protection": ref("modifier", f"{name}-city"),
                           "growth": {"gold": 100 + 50 * size}, "resourceMoney": 0})
    neutral = [unit("wild", "wild", "gold", *row) for row in NEUTRAL_UNITS]
    units += neutral
    modifiers.append(modifier("wild-city", "neutral city protection", [armor(10)]))
    races.append({"id": ref("race", "wild"), "income": {"gold": 50}, "kind": "neutral", "playable": False, "land": "neutral",
                  "turnOrder": 5, "eventFlag": 4, "neutral": True, "heroNames": [{"text": "Wilds", "male": True}]})
    for size in range(1, 6):
        cities.append({"id": ref("city", f"wild-{size}"), "race": ref("race", "wild"), "size": size, "scout": 2 + size,
                       "regeneration": 5 + 5 * size, "protection": ref("modifier", "wild-city"), "growth": {"gold": 100 + 50 * size},
                       "resourceMoney": 0})
    attacks = [attack_of(u) for u in units]
    growth = [growth_of(u) for u in units]
    effects = [{"id": ref("effect", k), "behavior": k, "ai": v} for k, v in EFFECTS.items()]
    modifiers.append(modifier("draught", "potion: armor", [armor(25)]))
    modifiers.append(modifier("charm", "item: armor", [armor(10)]))
    items = [
        {"id": ref("item", "healing-draught"), "category": "potionHeal", "value": {"gold": 120}, "potionHeal": 50},
        {"id": ref("item", "iron-draught"), "category": "potionBoost", "value": {"gold": 150},
         "potionModifier": ref("modifier", "draught")},
        {"id": ref("item", "iron-charm"), "category": "jewel", "value": {"gold": 400}, "equipModifier": ref("modifier", "charm")},
        {"id": ref("item", "gem"), "category": "valuable", "value": {"gold": 300}},
    ]
    landmarks = [
        {"id": ref("landmark", "stone"), "width": 1, "height": 1, "mountain": False, "category": "structure"},
        {"id": ref("landmark", "ridge"), "width": 2, "height": 2, "mountain": True, "category": "terrain"},
    ]
    thief_kinds = ["poisonStack", "spy", "stealItem", "assassinate", "misfit", "duel", "poisonCity", "stealSpell", "bribe",
                   "stealGold", "stealMerchant", "stealMage", "spyRuin", "riotCity"]
    thief = [{"id": ref("thief-action", k), "kind": k, "chance": 40 + 4 * i} for i, k in enumerate(thief_kinds)]
    attitudes = ["small", "medium", "large", "humongous", "smallNoResources", "mediumNoResources", "mediumNoResourcesRuins"]
    ai = [ai_attitude(i, a) for i, a in enumerate(attitudes)]
    return {
        "units": units, "growth": growth, "attacks": attacks, "effects": effects, "modifiers": modifiers, "spells": spells,
        "items": items, "buildings": buildings, "races": races, "cities": cities, "lords": lords, "leaderSkills": skills,
        "landmarks": landmarks, "thiefActions": thief, "ai": ai, "globals": [GLOBALS],
    }


def ai_attitude(i: int, attitude: str) -> dict:
    return {
        "id": ref("ai", attitude), "attitude": attitude,
        "budget": {"units": 70, "leaders": 20, "capital": 45, "cities": 35, "spellCast": 40, "spellResearch": 60, "tacticPurse": 60},
        "leaders": {"fighterMin": 1, "fighterMax": 2 + i // 3, "fighterPercent": 40, "thiefMin": 1, "thiefMax": 1, "thiefPercent": 30,
                    "rodMin": 1, "rodMax": 1, "rodPercent": 30},
        "weights": {"citiesNeutral": 105, "citiesRival": 110, "citiesPlayer": 115, "stacksNeutral": 100, "stacksRival": 110,
                    "stacksPlayer": 115, "bags": 90, "ruins": 100, "towers": 95, "merchants": 90, "hireUnits": 115,
                    "hireMercenaries": 110, "heal": 125, "plantRod": 115, "attackRod": 100, "neutralCities": 100,
                    "neutralStacks": 100, "trainers": 95, "objectMove": 100},
        "cheats": {"resourcePercent": [80, 100, 120, 140], "xpPercent": [0, 1, 3, 5]},
        "stayMinHp": 60, "healMinHp": 45,
        "spells": {"castChance": 30, "castChance2": 45, "castDoubleMin": 400, "levelChance": [55, 80, 100], "highWeight": 85,
                   "highChance": 25, "mediumWeight": 75, "mediumChance": 60, "lowWeight": 65, "lowChance": 100,
                   "levelPower": [250, 180, 110, 50], "criticalWeight": [35, 55, 70, 85], "criticalChance": [15, 35, 55, 70]},
    }


GLOBALS = {
    "id": "test:globals",
    "battle": {"initiativeRandom": 8, "damageRandom": 5, "rounds": 12, "break": 85, "breakModifier": 12,
               "boostDamagePercent": [20, 40, 60, 80], "lowerDamagePercent": [40, 30], "lowerInitiativePercent": 40,
               "defendBonus": 45},
    "thieves": {"morale": [12, 22, 32, 42, 52, 62], "spyDiscovery": 12, "poisonStack": 18, "poisonCity": 22, "bribe": 4,
                "stealRace": 20, "stealNeutral": 200, "riotMin": 1, "riotMax": 3, "riotDamage": 15},
    "land": {"capturePercent": 100, "capital": 8, "cities": [12, 14, 16, 18, 20], "rodCost": {"gold": 120}, "rodRange": 2},
    "diplomacy": {"peace": 90, "war": 30, "neutral": 60, "goldPerPoint": 40, "makeAlly": 90, "attackStack": -6, "attackFort": -9,
                  "attackAtPeace": 60, "rodDestroyed": -12, "refuseAlly": 45, "breakAlly": 8, "nobleAction": -4,
                  "allianceBreakChance": 4, "allianceBreakTurn": 8},
    "difficulty": {"startBankPercent": [250, 100, 60, 30], "incomePercent": [25, 0, 0, 0]},
    "ai": {"cityArmor": [8, 12, 16, 20, 24], "capitalArmor": 40, "guardRange": 4, "partyRange": 4, "locationRange": 3,
           "constructionUrgency": 85},
    "economy": {"sellRatio": 25, "crystalProfit": 40, "ruinRegeneration": 15},
    "heroes": {"weaponMaster": 20, "maxAbilities": 12, "warlordRegeneration": 12, "talismanCharges": 4},
    "spells": {"areaPowerPercent": [100, 75, 55, 40, 25], "gainOnCapture": 40},
    "tutorial": "",
}

FILES = {
    "units": "units", "growth": "growth", "attacks": "attacks", "effects": "effects", "modifiers": "modifiers", "spells": "spells",
    "items": "items", "buildings": "buildings", "races": "races", "cities": "cities", "lords": "lords",
    "leaderSkills": "leader-skills", "landmarks": "landmarks", "thiefActions": "thief-actions", "ai": "ai", "globals": "globals",
}

MOD_JSON = {"id": MOD, "name": "Test pack (CC0)"}


# ---- карты ---------------------------------------------------------------------------------------------------------------------------
# Сценарий в формате порта (d2-port-scenario, src/Engine.Play/Play/ScenarioFile.cs): клетки, игроки, столицы с героем и стражем, деревни
# нейтралов с охраной, нейтральные отряды. Раскладка — от зерна генератора (random.Random), файл лежит в репозитории готовым.
# Клетка — байт: земля расы (1 Империя-род, 4 Нежить-род, 5 нейтралы) | грунт << 3 (0 равнина, 1 лес, 3 вода, 4 горы).
LAND = {"sun": 1, "moon": 4, "wild": 5}
SUBRACE = {"wild": 5, "sun": 1, "moon": 2}          # подраса отряда (перечень Subrace)
KIND = {"sun": 0, "moon": 1, "wild": 4}             # род расы в отношениях (RaceKind)
MAPS = [
    # (имя, номер, сторона, зерно, деревень, нейтральных отрядов)
    ("twin-fields", 901, 48, 11, 2, 10),
    ("long-march", 902, 64, 23, 4, 16),
]
NEUTRAL_GROUPS = [["wolf"], ["wolf", "wolf"], ["shaman", "wolf"], ["bat", "bat", "wolf"], ["chief", "shaman", "wolf"],
                  ["chief", "bat", "shaman", "wolf"]]
ZERO_BANK = {"Gold": 0, "Infernal": 0, "Life": 0, "Death": 0, "Runic": 0, "Grove": 0}


class MapBuilder:
    def __init__(self, num: int, size: int, hp: dict[str, int]) -> None:
        self.p, self.size, self.hp = f"S{num}", size, hp
        self.units: dict[str, dict] = {}
        self.stacks: dict[str, dict] = {}
        self.objects: dict[str, dict] = {}
        self.plan: dict[tuple[int, int], list[str]] = {}
        self.sites: list[dict] = []
        self.forts: list[dict] = []
        self.looks_stacks: list[dict] = []
        self.next = {"UN": 0, "KC": 0, "FT": 0}

    def new_id(self, kind: str) -> str:
        n = self.next[kind]
        self.next[kind] = n + 1
        return f"{self.p}{kind}{n:04x}"

    def unit(self, name: str) -> str:
        uid = self.new_id("UN")
        self.units[uid] = {"Id": uid, "TypeId": ref("unit", name), "Level": 1, "Hp": self.hp[name], "Xp": 0,
                           "Modifiers": [], "DynLevel": False}
        return uid

    def cover(self, x: int, y: int, w: int, oid: str) -> None:
        for yy in range(y, y + w):
            for xx in range(x, x + w):
                self.plan.setdefault((xx, yy), []).append(oid)

    def stack(self, owner: str, race: str, names: list[str], x: int, y: int, inside: str = "", move: int = 20, order: int = 2) -> str:
        sid = self.new_id("KC")
        ids = [self.unit(n) for n in names]
        cells = [2, 0, 4, 3, 1, 5]   # первый — в центр переднего ряда, дальше передний ряд, затем задний
        slots = [-1] * 6
        for i in range(len(ids)):
            slots[cells[i]] = i
        self.stacks[sid] = {
            "Id": sid, "Owner": owner, "Subrace": self.subrace(race), "LeaderId": ids[0], "LeaderAlive": True, "X": x, "Y": y,
            "Move": move, "Inside": inside, "UnitIds": ids, "Slots": slots, "Items": [], "TemplateId": "", "Equipment": [""] * 7,
            "Order": order, "OrderTarget": "", "AiOrder": 1, "AiOrderTarget": "", "AiIgnore": False, "UpgradeCount": 0,
            "BattlesWon": 0, "CreationLevel": 1, "AiPriority": 3,
        }
        self.objects[sid] = {"Id": sid, "Kind": "KC", "X": x, "Y": y, "Resource": 0, "TypeId": ""}
        self.plan.setdefault((x, y), []).append(sid)
        self.looks_stacks.append({"Id": sid, "X": x, "Y": y, "Owner": owner, "Subrace": self.subrace(race),
                                  "LeaderType": self.units[ids[0]]["TypeId"], "LeaderAlive": True, "AnyAlive": True, "Facing": 0,
                                  "Inside": inside, "Invisible": False, "LeaderName": names[0].title()})
        return sid

    def subrace(self, race: str) -> str:
        return f"{self.p}SR{list(SUBRACE).index(race):04x}"

    def fort(self, owner: str, race: str, x: int, y: int, capital: bool, name: str, guards: list[str], hero: list[str] | None) -> None:
        fid = self.new_id("FT")
        w = 5 if capital else 4
        self.objects[fid] = {"Id": fid, "Kind": "FT", "X": x, "Y": y, "Resource": 0, "TypeId": ""}
        self.cover(x, y, w, fid)
        visitor = self.stack(owner, race, hero, x, y, inside=fid, order=1) if hero else ""
        guard_ids = [self.unit(g) for g in guards]
        slots = [-1] * 6
        for i, cell in enumerate([2, 0, 4][:len(guard_ids)]):
            slots[cell] = i
        city = {} if capital else {"Size": 1, "GrowthTurn": 0, "RiotTurn": 0, "RegenBonus": 0, "Morale": 0}
        self.sites.append({
            "Id": fid, "Kind": "Capital" if capital else "Village", "RawKind": "Capital" if capital else "MidVillage", "X": x, "Y": y,
            "Name": name, "Description": "", "Owner": owner, "Cash": ZERO_BANK, "ItemId": "", "LooterId": "", "Items": [],
            "Guards": guard_ids, "GuardSlots": slots, "Resource": 0, "VisitorId": "", "Gone": False, "StackId": visitor,
            "Subrace": self.subrace(race), "Size": None, "GrowthTurn": None, "RiotTurn": None, "RegenBonus": None,
            "ProtectBonus": "", "Morale": None, "Visitors": [], "Stock": [], "Buys": None, "Mission": False, "Roster": [],
            "Spells": [], "AiPriority": 3, **city,
        })
        self.forts.append({"Id": fid, "Capital": capital, "X": x, "Y": y, "Owner": owner, "Level": 0 if capital else 1,
                           "Riot": False, "Garrison": visitor})


def grid(size: int, value: int | bytes) -> dict:
    data = value if isinstance(value, bytes) else bytes([value]) * (size * size)
    return {"w": size, "h": size, "data": base64.b64encode(data).decode("ascii")}


def scenario(name: str, num: int, size: int, seed: int, villages: int, neutrals: int) -> dict:
    rnd = random.Random(seed)
    hp = {u["id"].split("/", 1)[1]: u["hitPoints"] for u in build()["units"]}
    b = MapBuilder(num, size, hp)
    players = {"wild": f"S{num}PL0000", "sun": f"S{num}PL0001", "moon": f"S{num}PL0002"}
    tiles = bytearray([LAND["wild"]] * (size * size))

    def put(x: int, y: int, ground: int, land: int = LAND["wild"]) -> None:
        tiles[y * size + x] = land | (ground << 3)

    for _ in range(size // 3):                       # перелески
        cx, cy = rnd.randrange(size), rnd.randrange(size)
        for dx in range(-2, 3):
            for dy in range(-1, 2):
                if 0 <= cx + dx < size and 0 <= cy + dy < size:
                    put(cx + dx, cy + dy, 1)
    lake = size // 2 - 2                              # озеро в центре и горы по краям
    for x in range(lake, lake + 4):
        for y in range(lake, lake + 3):
            put(x, y, 3)
    for i in range(0, size, 7):
        put(i, 0, 4)
        put(size - 1, i, 4)
    corners = {"sun": (4, 4), "moon": (size - 9, size - 9)}
    for race, (x, y) in corners.items():
        for yy in range(y - 2, y + 7):
            for xx in range(x - 2, x + 7):
                put(xx, yy, 0, LAND[race])
        b.fort(players[race], race, x, y, True, f"{race.title()} Keep", [f"{race}-colossus"],
               [f"{race}-captain", f"{race}-footman", f"{race}-bowman"])
    taken = {(x, y) for (x, y) in b.plan}
    for i in range(villages):
        while True:
            x, y = rnd.randrange(2, size - 6), rnd.randrange(2, size - 6)
            if all((xx, yy) not in taken for xx in range(x - 1, x + 5) for yy in range(y - 1, y + 5)):
                break
        for xx in range(x, x + 4):
            for yy in range(y, y + 4):
                put(xx, yy, 0)
        b.fort(players["wild"], "wild", x, y, False, f"Hamlet {i + 1}", ["wild-wolf", "wild-shaman"], None)
        taken |= {(xx, yy) for xx in range(x - 1, x + 5) for yy in range(y - 1, y + 5)}
    for i in range(neutrals):
        while True:
            x, y = rnd.randrange(1, size - 1), rnd.randrange(1, size - 1)
            if (x, y) not in taken and tiles[y * size + x] >> 3 in (0, 1):
                break
        taken.add((x, y))
        group = NEUTRAL_GROUPS[min(len(NEUTRAL_GROUPS) - 1, i * len(NEUTRAL_GROUPS) // neutrals)]
        b.stack(players["wild"], "wild", [f"wild-{u}" for u in group], x, y)

    def player(race: str, num_: int) -> dict:
        pid = players[race]
        gold = 0 if race == "wild" else 600
        mana = {"sun": "Life", "moon": "Death"}.get(race)
        bank = dict(ZERO_BANK, Gold=gold, **({mana: 150} if mana else {}))
        return {"Id": pid, "RaceId": ref("race", race), "LordId": ref("lord", f"{race}-lord") if race != "wild" else ref("lord", "sun-lord"), "Name": race.title(), "Face": 0, "IsHuman": False,
                "Bank": bank, "FogId": f"S{num}FG{num_:04x}", "BuildingsId": f"S{num}PB{num_:04x}", "Buildings": [],
                "SpellBank": ZERO_BANK, "QtyBreaks": 0, "Attitude": 1, "CapturedBy": "", "ConstructionTurn": 0, "ResearchTurn": 0,
                "KnownSpells": [], "Spied": []}

    order = ["wild", "sun", "moon"]
    subraces = {b.subrace(r): SUBRACE[r] for r in SUBRACE}
    the_map = {
        "Size": size, "Tiles": grid(size, bytes(tiles)), "Stacks": b.stacks, "Units": b.units,
        "Players": {players[r]: player(r, i) for i, r in enumerate(order)}, "Objects": b.objects,
        "Plan": [{"X": x, "Y": y, "Ids": ids} for (x, y), ids in sorted(b.plan.items(), key=lambda kv: (kv[0][1], kv[0][0]))],
        "Fog": {f"S{num}FG{i:04x}": grid(size, 0) for i in range(len(order))}, "Turn": 0, "Rods": {}, "Sites": b.sites,
        "Relations": [{"Race1": KIND["wild"], "Race2": KIND["sun"], "Raw": 0}, {"Race1": KIND["wild"], "Race2": KIND["moon"], "Raw": 0},
                      {"Race1": KIND["sun"], "Race2": KIND["moon"], "Raw": 0}],
        "TalismanCharges": {}, "SpellCasts": [], "ItemCasts": [], "SpellEffects": [], "ItemTypes": {},
        "Limits": {"MaxUnit": 3, "MaxSpell": 3, "MaxLeader": 10, "MaxCity": 5, "DifficultyScenario": 2, "DifficultyGame": 1,
                   "SuggestedLevel": 1},
        "Subraces": subraces, "Destroyed": [], "Tombs": [], "MapSeed": seed * 7919,
    }
    looks = {"Map": the_map, "TreeImage": grid(size, 0), "TreeGroup": grid(size, 0), "Forts": b.forts, "Ruins": [], "Sites": [],
             "Crystals": [], "Bags": [], "Tombs": [], "Rods": [], "Landmarks": [], "Mountains": [], "Roads": [],
             "Stacks": b.looks_stacks, "Banners": {sr: i for i, sr in enumerate(subraces)}}
    events = {"Events": [], "Zones": {}, "Variables": {}, "Templates": {},
              "SubraceOwners": {b.subrace(r): players[r] for r in SUBRACE}}
    return {"Format": "d2-port-scenario", "Version": 6, "Id": name, "Looks": looks, "Events": events}


def render() -> dict[Path, str]:
    out = {OUT / "mod.json": json.dumps(MOD_JSON, ensure_ascii=False, indent=1) + "\n"}
    kinds = build()
    for key, entities in kinds.items():
        stem = FILES[key]
        doc = {"$schema": SCHEMA.format(stem), key: entities}
        out[OUT / f"{stem}.json"] = json.dumps(doc, ensure_ascii=False, indent=1) + "\n"
    for name, num, size, seed, villages, neutrals in MAPS:
        doc = scenario(name, num, size, seed, villages, neutrals)
        out[OUT / "scenarios" / f"{name}.json"] = json.dumps(doc, ensure_ascii=False, indent=1) + "\n"
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--check", action="store_true", help="не писать, а сверить файлы на диске с генератором")
    args = ap.parse_args()
    files = render()
    if args.check:
        stale = [p for p, text in files.items() if not p.exists() or p.read_text(encoding="utf-8") != text]
        for p in stale:
            print(f"разошёлся с генератором: {p.relative_to(ROOT)}")
        return 1 if stale else 0
    OUT.mkdir(parents=True, exist_ok=True)
    for p, text in files.items():
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(text.encode("utf-8"))
    print(f"{len(files)} файлов в {OUT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
