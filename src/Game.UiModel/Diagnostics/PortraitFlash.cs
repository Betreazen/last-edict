namespace LastEdict.Data.Diagnostics;

/// <summary>
/// Состояния портрета панели боя (картинка лица <c>CFaceImgImpl</c> в <c>CBatUnitGroup2</c>) — факты движка для строк <c>bat_flash</c>
/// схемы 7.6 (<c>docs/probe-av.md</c> §8; разбор capstone, ранг 1, если не сказано иное). Вспышка — состояние на N тиков панели
/// (<c>0x64cee8</c>), стойкое — до перестройки панели (<c>0x64d2c3</c>, паралич, окаменение, павший — сразу через <c>0x64cee8</c>).
/// Цвет — <c>0x5b18ef</c>: есть ли (<c>0x5b19fa</c>) и какой (<c>0x5b191f</c>, <c>COLORREF</c>); им смешивается весь портрет — тот же
/// цвет, что у подкраски по состоянию (<see cref="LastEdict.Data.Gfx.BattlePanel.Tint"/>: яд, мороз, ожог, паралич, павший) — ранг 2 для
/// вспышек (картинка лица одна, состояние одно). Номера — те же, что у вида порта (выбор состояния по удару — у вида, здесь — только то,
/// что нужно журналу и сверке: цвет и имя).
/// </summary>
public static class PortraitFlash
{
    /// <summary>Рост (выросший в конце боя юнит, стойкое; цвета нет).</summary>
    public const int Upgraded = 1;

    /// <summary>Урон (и страх, вытягивание, перелив — классы без своего состояния).</summary>
    public const int Damage = 2;

    public const int Miss = 3, Immune = 4, Heal = 5, Boost = 6, Paralyze = 7, Petrify = 8, HealOverflow = 9, Dead = 10;

    /// <summary>Опыт в конце боя (жёлтый, <c>0x64d4a4</c>): стойкий у получившего опыт, вспышкой — у выросшего.</summary>
    public const int Xp = 11;

    public const int Lower = 14, LowerInitiative = 15, Poison = 16, Frostbite = 17, Blister = 18, BestowWards = 19, Shatter = 20,
                     Revive = 21, Cure = 22, DrainLevel = 23, GiveAttack = 24, Transform = 27, Summon = 28;

    /// <summary>Цвета состояний <c>#RRGGBB</c> (null — без цвета: 0, 1, 4, 9, 12, 15, 24–26).</summary>
    private static readonly string?[] Colors =
    [
        null, null, "#FF0000", "#FFFF00", null, "#0000FF", "#00FF00", "#808080", "#808080", null,
        "#000000", "#FFFF40", null, "#000000", "#FF00FF", null, "#808000", "#000080", "#C08000", "#00FF00",
        "#5EDAF9", "#0000FF", "#0000FF", "#FF00FF", null, null, null, "#000000", "#000000", "#000000",
    ];

    /// <summary>Цвет состояния (<c>0x5b18ef</c>), null — портрет не красится.</summary>
    public static string? ColorOf(int state) => state >= 0 && state < Colors.Length ? Colors[state] : null;

    /// <summary>Имя состояния для отчёта.</summary>
    public static string Name(int state) => state switch
    {
        Upgraded => "рост",
        Damage => "урон",
        Miss => "промах",
        Immune => "иммунитет",
        Heal => "лечение",
        Boost => "усиление",
        Paralyze => "паралич",
        Petrify => "окаменение",
        HealOverflow => "перелив",
        Dead => "павший",
        Xp => "опыт",
        Lower => "ослабление",
        LowerInitiative => "ослабление инициативы",
        Poison => "яд",
        Frostbite => "мороз",
        Blister => "ожог",
        BestowWards => "обереги",
        Shatter => "разрушение брони",
        Revive => "воскрешение",
        Cure => "исцеление",
        DrainLevel => "понижение уровня",
        GiveAttack => "дар атаки",
        Transform => "превращение",
        Summon => "призыв",
        _ => state.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>Состояние ставится сразу, без срока (<c>0x64d040</c>).</summary>
    public static bool Steady(int state) => state is Paralyze or Petrify or Dead;
}
