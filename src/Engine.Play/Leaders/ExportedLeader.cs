namespace LastEdict.Data.Leaders;

/// <summary>
/// Герой, вывезенный из партии (экспорт после победы, <c>0x408307</c>), — для импорта в новую партию (лобби «горячего стула»,
/// выбор лорда). Поля — запись <c>.ldr</c> игры (<c>docs/day-cycle.md</c> §12); в порту хранится своим форматом
/// (<see cref="LeaderFile"/>). На старте партии используются только тип, уровень, имя, опыт, модификаторы, снаряжение, победы и
/// отложенные навыки (<c>CImportLeaderMsg</c>): характеристики (здоровье, атаки, иммунитеты) — для окна просмотра, у героя в партии
/// они берутся заново из типа и уровня.
/// </summary>
public sealed record ExportedLeader
{
    public required string Name { get; init; }

    /// <summary>Раса (<c>LRaceCategory</c> = <see cref="Game.RaceInfo.Type"/>: 0 Империя, 1 Орды нежити, 2 Легионы, 3 Кланы, 5 Эльфы).</summary>
    public int Race { get; init; }

    /// <summary>Базовый тип юнита (<c>G000UU…</c>).</summary>
    public required string UnitType { get; init; }

    public int Level { get; init; }
    public int Leadership { get; init; }
    public int Xp { get; init; }
    public int XpNext { get; init; }

    /// <summary>Выигранные сражения отряда (<c>NBBATTLE</c>).</summary>
    public int BattlesWon { get; init; }

    public int HitPoints { get; init; }
    public int Armor { get; init; }
    public required LeaderAttack Attack { get; init; }

    /// <summary>Вторая атака: id (пусто — нет), название, точность.</summary>
    public string Attack2 { get; init; } = "";
    public string Attack2Name { get; init; } = "";
    public int Attack2Power { get; init; }
    public bool AttacksTwice { get; init; }
    public int Move { get; init; }
    public int Scout { get; init; }
    public int Negotiate { get; init; }

    /// <summary>Бонус хода на равнине, в лесу и на воде сразу (у Рыцаря на Пегасе; ранг 2 — «полёт»).</summary>
    public bool TerrainBonus { get; init; }

    /// <summary>Иммунитеты (<c>L_ALWAYS</c>) и защиты (<c>L_ONCE</c>) — по источникам (<c>LAttackSource</c>) и классам (<c>LAttackClass</c>).</summary>
    public IReadOnlyList<int> ImmuneSources { get; init; } = [];
    public IReadOnlyList<int> ImmuneClasses { get; init; } = [];
    public IReadOnlyList<int> WardSources { get; init; } = [];
    public IReadOnlyList<int> WardClasses { get; init; } = [];

    /// <summary>Способности лидера (<c>LLeaderAbility</c>).</summary>
    public IReadOnlyList<int> Abilities { get; init; } = [];

    /// <summary>Модификаторы юнита (<c>G000UM…</c>), без модификаторов надетых предметов.</summary>
    public IReadOnlyList<string> Modifiers { get; init; } = [];

    public IReadOnlyList<LeaderItem> Equipment { get; init; } = [];

    /// <summary>Отложенные выборы навыка (<c>UPGCOUNT</c>, с версии 6 файла).</summary>
    public int UpgradeCount { get; init; }
}

/// <summary>Первая атака героя: id, класс (<c>LAttackClass</c>), название, точность, «уровень» (только у усиления/ослабления),
/// урон, источник (<c>LAttackSource</c>), инициатива, досягаемость (<c>LAttackReach</c>).</summary>
public sealed record LeaderAttack(string Id, int Class, string Name, int Power, int Level, int Damage, int Source, int Initiative, int Reach);

/// <summary>Надетый предмет: слот снаряжения (порядок <c>MapStack.Equipment</c>: знамя, книга, боевые 1–2, артефакты 1–2, обувь),
/// глобальный тип (<c>G000IG…</c>), заряды (только у талисмана).</summary>
public sealed record LeaderItem(int Slot, string ItemType, int Charges);
