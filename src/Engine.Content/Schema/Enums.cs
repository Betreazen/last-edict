namespace D2.Data.Content.Schema;

// Перечни содержимого. Значения — числа таблиц-перечней оригинала (Globals/L*.dbf), имена — их TEXT по смыслу; в JSON — строкой
// (camelCase: «boostDamage»). Перечни правил, которые уже есть в D2.Rules (AttackClass, ItemCategory, SpellCategory,
// ModifierElementType, ImmunityLevel, LordCategory), содержимое берёт оттуда — второй копии нет. Имена, совпавшие бы с константами
// правил (UnitCategory, UnitBranch, AttackReach), — свои: UnitRole, BranchKind, ReachKind.

/// <summary>Категория юнита (<c>Gunits.UNIT_CAT</c>, <c>LunitC.dbf</c>).</summary>
public enum UnitRole { Soldier = 0, Noble = 1, Leader = 2, Summon = 4, Illusion = 5, NeutralLeader = 6, NeutralSoldier = 7, Guardian = 8 }

/// <summary>Ветка юнита (<c>Gunits.BRANCH</c>, <c>LunitB.dbf</c>): по ней строятся ветки зданий найма.</summary>
public enum BranchKind { Fighter = 0, Archer = 1, Mage = 2, Special = 3, Sideshow = 4, Hero = 5, Noble = 6, Summon = 7 }

/// <summary>Категория лидера (<c>Gunits.LEADER_CAT</c>, <c>LleadC.dbf</c>).</summary>
public enum LeaderCategory { Fighter = 1, Explorer = 2, Mage = 3, Rod = 4, Noble = 5 }

/// <summary>Подраса (<c>Gunits.SUBRACE</c>, <c>LSubRace.dbf</c>).</summary>
public enum Subrace
{
    Custom = 0, Human = 1, Undead = 2, Heretic = 3, Dwarf = 4, Neutral = 5, NeutralHuman = 6, NeutralElf = 7, NeutralGreenSkin = 8,
    NeutralDragon = 9, NeutralMarsh = 10, NeutralWater = 11, NeutralBarbarian = 12, NeutralWolf = 13, Elf = 14,
}

/// <summary>Род расы (<c>Grace.RACE_TYPE</c>, <c>Lrace.dbf</c>): значки свитков, земля, эльфийские правила.</summary>
public enum RaceKind { Human = 0, Undead = 1, Heretic = 2, Dwarf = 3, Neutral = 4, Elf = 5 }

/// <summary>Анимация гибели (<c>Gunits.DEATH_ANIM</c>, <c>LDthAnim.dbf</c>).</summary>
public enum DeathAnimation { Human = 1, Heretic = 2, Dwarf = 3, Undead = 4, Neutral = 5, Dragon = 6, Ghost = 7, Elf = 8 }

/// <summary>Источник атаки (<c>Gattacks.SOURCE</c>, <c>LattS.dbf</c>) — по нему иммунитеты.</summary>
public enum AttackSource { Weapon = 0, Mind = 1, Life = 2, Death = 3, Fire = 4, Water = 5, Earth = 6, Air = 7 }

/// <summary>Досягаемость атаки (<c>Gattacks.REACH</c>, <c>LAttR.dbf</c>).</summary>
public enum ReachKind { All = 1, Any = 2, Adjacent = 3 }

/// <summary>Способность лидера (<c>LleadA.dbf</c>): врождённые (<c>GLabi</c>) и выданные модификаторами.</summary>
public enum LeaderAbility
{
    Zealot = 1, Incorruptible = 3, WeaponMaster = 6, WandScrollUse = 7, WeaponArmorUse = 8, BannerUse = 9, JewelryUse = 10, Rod = 13,
    OrbUse = 14, TalismanUse = 15, TravelItemUse = 16, CriticalHit = 17,
}

/// <summary>Категория постройки столицы (<c>Gbuild.CATEGORY</c>, <c>Lbuild.dbf</c>).</summary>
public enum BuildingCategory { Guild = 0, Heal = 1, Magic = 2, Unit = 3 }

/// <summary>Категория ориентира (<c>GLmark.CATEGORY</c>, <c>LLMCat.dbf</c>).</summary>
public enum LandmarkCategory { Misc = 0, Building = 1, Structure = 2, Terrain = 3 }

/// <summary>Действие вора (<c>Gaction.CATEGORY</c>, <c>Laction.dbf</c>).</summary>
public enum ThiefActionKind
{
    PoisonStack = 0, Spy = 1, StealItem = 2, Assassinate = 3, Misfit = 4, Duel = 5, PoisonCity = 6, StealSpell = 7, Bribe = 8,
    StealGold = 9, StealMerchant = 10, StealMage = 11, SpyRuin = 12, RiotCity = 13,
}

/// <summary>Отношение ИИ (<c>GAI.CATEGORY</c>, <c>LaiAtt.dbf</c>): размер карты и наличие ресурсов.</summary>
public enum AiAttitude { Small = 0, Medium = 1, Large = 2, Humongous = 3, SmallNoResources = 4, MediumNoResources = 5, MediumNoResourcesRuins = 6 }

/// <summary>Подсказка ИИ, кому полезно заклинание (<c>Gspells.AI_CAT</c>, <c>LAiSpell.dbf</c>).</summary>
public enum AiSpellUse { All = 1, None = 2, Human = 3, Undead = 4, Heretic = 5, Dwarf = 6 }

/// <summary>Земля расы (<c>Gspells.GROUND_CAT</c>, <c>Lterrain.dbf</c>) — во что заклинание превращает землю.</summary>
public enum TerrainKind { Human = 1, Dwarf = 2, Heretic = 3, Undead = 4, Neutral = 5, Elf = 6 }

/// <summary>Повод сообщения ИИ (<c>TAiMsg.MSG_CAT</c>, <c>LAiMsg.dbf</c>).</summary>
public enum AiMessageKind
{
    PlayerAcceptsAll = 0, PlayerRefusedAll = 1, AiBreakAlliance = 2, PlayerBreakAll = 3, AiOffersAlliance = 4, PlayerGiveGold = 5,
    AiGiveGold = 6, TradeAccepted = 7, TradeRefused = 8, HateMessage = 9, NoGold = 10, PlayerGiveGoldN = 11,
}

/// <summary>Область модификатора (<c>Gmodif.SOURCE</c>, <c>LModifS.dbf</c>): юнит, отряд или атака.</summary>
public enum ModifierScope { Unit = 0, Stack = 1, Attack = 3 }

/// <summary>Способность хода по карте (<c>GMabi.M_ABILITY</c>, <c>Lground.dbf</c>): по какой земле юнит ходит без штрафа.</summary>
public enum MoveAbility { Plain = 0, Forest = 1, Water = 3, Mountain = 4 }
