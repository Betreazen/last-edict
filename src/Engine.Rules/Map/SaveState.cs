namespace D2.Rules.Map;

// Состояние карты из сохранения, нужное задачам 3–14 Ф7. Прямые проекции полей .sg (ранг 1 — поле файла,
// tools/ksy/sg.ksy), без логики механик. Разбор битов RELATION — ранг 1 по плану Ф7 §1.8.

/// <summary>Жезл (<c>CMidRod</c>): <c>ROD_ID</c>, <c>OWNER</c> — игрок, клетка.</summary>
public sealed record MapRod(string Id, string Owner, int X, int Y);

/// <summary>
/// Пара сторон дипломатии (<see cref="MapPlayer.Party"/>) и слово <c>RELATION</c>. У движка — пара рас <c>CMidDiplomacy</c>: <c>RACE_1</c>,
/// <c>RACE_2</c> (<c>LRaceCategory::id</c>: 0 Империя, 1 Нежить, 2 Легионы, 3 Кланы, 4 нейтралы, 5 эльфы); сторона игрока без своей —
/// род его расы, так что у партий оригинала это те же числа. Раскладка битов — ранг 1:
/// hook <c>relation</c> (<c>probediplo.cpp</c>, мутатор <c>0x5d9366</c>, <c>setCurrentRelation</c>
/// <c>0x5f799c</c> меняет только биты 1…7), план Ф7 §1.8, run4 §4.4; сверка с журналом — харнесс
/// <c>diplo_decode</c>. Пара неупорядочена.
/// </summary>
public readonly record struct Relation(int Race1, int Race2, uint Raw)
{
    /// <summary>Бит 0 — союз.</summary>
    public bool Ally => (Raw & 1) != 0;

    /// <summary>Биты 1…7 — текущее отношение 0…100 (война ≤ 33, нейтралитет ≤ 66, мир &gt; 66).</summary>
    public int Current => (int)((Raw >> 1) & 0x7F);

    /// <summary>Биты 8…14 — прошлое отношение (пишет только сценарное событие, run4 §4.1).</summary>
    public int Previous => (int)((Raw >> 8) & 0x7F);

    /// <summary>Биты 15…29 — ход последней смены союза.</summary>
    public int AllianceTurn => (int)((Raw >> 15) & 0x7FFF);

    /// <summary>Бит 30 — вечная война: мутатор пару не пишет.</summary>
    public bool EternalWar => ((Raw >> 30) & 1) != 0;

    /// <summary>Бит 31 — ИИ не может разорвать союз.</summary>
    public bool AiCannotBreak => (Raw >> 31) != 0;

    /// <summary>Та ли это пара, в любом порядке рас.</summary>
    public bool Pairs(int a, int b) => (Race1 == a && Race2 == b) || (Race1 == b && Race2 == a);
}

/// <summary>Запись <c>CMidSpellCast</c>: <c>PLAYER_ID</c>, <c>ORIGIN</c> и <c>TURN_STOP</c>. В первом
/// списке <c>ORIGIN</c> — заклинание (<c>G000SS…</c>), во втором — экземпляр предмета (<c>IM</c>); второй
/// пуст в начале дня — это суточный запрет посоха на экземпляр (план Ф7 §1.5; разбор двух списков —
/// <c>sg.ksy</c> mid_spell_cast).</summary>
public sealed record CastRecord(string PlayerId, string Origin, int TurnStop);

/// <summary>Запись <c>CMidSpellEffects</c> — модификатор заклинания или зелья карты на юните (<c>UNIT_ID</c>, <c>ORIGIN_ID</c> —
/// заклинание или тип предмета, <c>MODIF_ID</c>, <c>CASTER_ID</c>, <c>TURN_STOP</c>). Сам модификатор лежит и в цепочке юнита
/// (<see cref="MapUnit.Modifiers"/>); запись нужна, чтобы снять его в начале хода владельца (<c>CEffectRemoveSpell</c>
/// <c>0x43b9ef</c>: владелец — <c>CASTER_ID</c>, пустой — хозяин юнита; ход ≥ <c>TURN_STOP</c>).</summary>
public sealed record SpellEffectRecord(string UnitId, string OriginId, string ModifierId, string CasterId, int TurnStop);

/// <summary>Потолки сценария из <c>CScenarioInfo</c>: <c>MAX_UNIT</c> (потолок уровня постройки),
/// <c>MAX_SPELL</c>, <c>MAX_LEADER</c> (потолок уровня лидера), <c>MAX_CITY</c>, сложность сценария и
/// игры (<c>DIFFSCEN</c>, <c>DIFFGAME</c>). Ранг 1 — поля; смысл <c>MAX_UNIT</c>/<c>MAX_LEADER</c> — план
/// Ф7 §1.5 (ранг 1 по levels §2.2). <c>SUGG_LVL</c> (<c>CScenarioInfo +0x70</c>) — рекомендуемый уровень сценария: при ≥ 10 у ИИ
/// наёмники и тренер иначе, пределы лидеров, заклинания «на всю карту» и «натиск», нет защиты крепостей, нейтралы без чита опыта.
/// До 13.10 загрузка <c>.sg</c> его теряла (партия из сохранения шла с уровнем 0): 20261014-replay r2b (SUGG_LVL 10) — HireMercs у
/// полного отряда, которого движок не строит, и 3082 лишних повышения опыта нейтралам.</summary>
public sealed record ScenarioLimits(int MaxUnit, int MaxSpell, int MaxLeader, int MaxCity,
                                    int DifficultyScenario, int DifficultyGame, int SuggestedLevel = 0);

/// <summary>Строка склада торговца (<c>ITEM_ID</c>, <c>ITEM_COUNT</c>). Склад конечен (sites §2.1).</summary>
public sealed record MerchantStock(string Item, int Count);

/// <summary>Флаги <c>BUY_*</c> торговца: какие категории он берёт в продажу (sites §2.1).</summary>
public sealed record MerchantBuys(bool Armor, bool Jewel, bool Weapon, bool Banner, bool Potion, bool Scroll,
                                  bool Wand, bool Value);

/// <summary>Строка ростера наёмника (<c>UNIT_ID</c>, <c>UNIT_LEVEL</c>, <c>UNIT_UNIQ</c>): уникальный
/// после найма уходит из ростера (sites §2.3). <see cref="UnitType"/> — ранг 2 на то, что это тип
/// (<c>G000UU…</c>), а не экземпляр.</summary>
public sealed record MercenaryOffer(string UnitType, int Level, bool Unique);
