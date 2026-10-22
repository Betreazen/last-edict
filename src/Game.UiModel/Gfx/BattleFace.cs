using LastEdict.Data.Ui;
using LastEdict.Rules.Battle;

namespace LastEdict.Data.Gfx;

/// <summary>
/// Состояние портрета в панели боя — индекс картинки <c>CFaceImgImpl</c> (<c>IFaceImg</c> vftable <c>+0x24</c>/<c>+0x28</c>, поле <c>+0x40</c>;
/// разбор 20.10, <c>docs/battle-screen.md</c> §9). Номера — движка: по ним три таблицы рисования (<see cref="BattleFaces.Look"/>) и
/// выбор состояния по событию боя. Имена — по тому, кто ставит.
/// </summary>
public enum BattleFace
{
    /// <summary>Обычный: только красная доля ран (база живого, <c>0x64c269</c>; снятие паралича и окаменения).</summary>
    Normal = 0,

    /// <summary>Без подкраски и без ран (выросший после вспышки опыта, <c>0x64d5de</c>).</summary>
    Plain = 1,

    /// <summary>Удар: красным (урон, вампиризм, страх, по умолчанию, <c>0x63bcc2</c>).</summary>
    Hit = 2,

    /// <summary>Промах первой атаки: жёлтым.</summary>
    Missed = 3,

    /// <summary>Удар остановлен защитой (иммунитет, щит): цвет жёлтый, но заливки нет (<c>0x5b19fa</c>).</summary>
    Shielded = 4,

    /// <summary>Лечение: синим.</summary>
    Healed = 5,

    /// <summary>Усиление урона: зелёным.</summary>
    Boosted = 6,

    /// <summary>Паралич: серым и раны (база).</summary>
    Paralyzed = 7,

    /// <summary>Окаменение: серым и раны (базой не ставится — ошибка движка, <c>0x64c2ab</c> опрашивает паралич второй раз).</summary>
    Petrified = 8,

    /// <summary>Вампиризм на бьющем («+N», <c>0x63c34f</c>): ни заливки, ни ран.</summary>
    DrainHealed = 9,

    /// <summary>Павший: чёрным (или маской-черепом) без ран (база).</summary>
    Dead = 10,

    /// <summary>Опыт в конце боя: светло-жёлтым (255, 255, 64), без ран (<c>0x64d4a4</c>).</summary>
    Experience = 11,

    /// <summary>«Пас» парализованного (шаг 1, <c>0x635aba</c>): ни заливки, ни ран.</summary>
    Pass = 12,

    /// <summary>Чёрным наполовину — ставящего не найдено.</summary>
    Unused13 = 13,

    /// <summary>Ослабление урона: пурпурным.</summary>
    LowerDamage = 14,

    /// <summary>Замедление: цвет пурпурный, но заливки нет.</summary>
    LowerInitiative = 15,

    /// <summary>Яд: оливковым и раны (база, тик).</summary>
    Poisoned = 16,

    /// <summary>Обморожение: тёмно-синим и раны (база, тик).</summary>
    Frostbitten = 17,

    /// <summary>Ожог: оранжево-коричневым и раны (база, тик).</summary>
    Blistered = 18,

    /// <summary>Обереги: зелёным.</summary>
    Warded = 19,

    /// <summary>Разрушение брони: голубым (94, 218, 249).</summary>
    Shattered = 20,

    /// <summary>Воскрешение: синим.</summary>
    Revived = 21,

    /// <summary>Исцеление: синим.</summary>
    Cured = 22,

    /// <summary>Понижение уровня (и возврат уровня, шаг 16): пурпурным.</summary>
    DrainLevel = 23,

    /// <summary>Дар атаки: заливки нет.</summary>
    GiveAttack = 24,

    /// <summary>«Ждать» (шаг 9): только раны.</summary>
    Waited = 25,

    /// <summary>«Защита» (шаг 10; и ход окаменевшего — <c>0x628e81</c>): только раны.</summary>
    Defending = 26,

    /// <summary>Превращение, двойник, «Прежний облик!»: чёрным целиком (альфа 255), держится до сборки панелей конца шага.</summary>
    Transformed = 27,

    /// <summary>Призыв: чёрным целиком, держится.</summary>
    Summoned = 28,

    /// <summary>«Изгнать» призванного (шаг 14): чёрным целиком, держится.</summary>
    Banished = 29,
}

/// <summary>Как рисуется состояние: заливка всего портрета (null — нет), красная доля ран и сила смешения (128 — наполовину, 255 — цвет
/// вместо точки).</summary>
public readonly record struct BattleFaceLook(UiColor? Whole, bool Wounds, byte Alpha);

/// <summary>Вспышка на портрете: состояние и держится ли она после своих тиков (<c>+0x28</c>, <c>0x64cfeb</c>) — до сборки панелей.</summary>
public readonly record struct BattleFlash(BattleFace Face, bool Keep);

/// <summary>
/// Таблицы и выбор состояния портрета панели боя по листингу (ранг 1):
/// <list type="bullet">
/// <item>цвет — <c>0x5b191f</c> (<c>COLORREF</c>, таблица <c>0x5b19dc</c>/<c>0x5b198c</c>), есть ли заливка — <c>0x5b19fa</c>, красная доля ран —
/// <c>0x5b1a91</c>, сила смешения — <c>0x5b1add</c> (27–29 — 255, прочие — 128); рисует <c>0x5b149d</c> → <c>0x5b1af4</c> → <c>0x5b1c12</c>;</item>
/// <item>база по состоянию юнита — <c>0x64c269</c> (сборка панели <c>0x64bc74</c>); поле <c>+0x40</c> записи группы, если поставлено
/// (<c>0x64d2c3</c>), — вместо базы (<c>0x64bc1b…0x64bc4e</c>);</item>
/// <item>вспышка — <c>CBatUnitGroup2</c> <c>0x64cee8</c>: на <c>+0x24</c> записи столько тиков, сколько держится надпись (<c>+0x44</c> группы,
/// <c>0x64a4cd</c>: 15 при скорости 1), по концу (<c>0x64d356</c> → <c>0x64d467</c>) — назад, если не <see cref="BattleFlash.Keep"/>; 7, 8, 10
/// вспышкой не ставятся (только переход между ними, <c>0x64d040</c>/<c>0x64d05f</c>) — порт их не ставит вовсе.</item>
/// </list>
/// </summary>
public static class BattleFaces
{
    /// <summary>Как рисуется состояние (<c>0x5b191f</c>, <c>0x5b19fa</c>, <c>0x5b1a91</c>, <c>0x5b1add</c>).</summary>
    public static BattleFaceLook Look(BattleFace face)
    {
        int s = (int)face;
        bool whole = s is 2 or 3 or (>= 5 and <= 8) or 10 or 11 or 13 or 14 or (>= 16 and <= 23) or (>= 27 and <= 29);
        bool wounds = s is 0 or 7 or 8 or 16 or 17 or 18 or 25 or 26;
        return new BattleFaceLook(whole ? Color(face) : null, wounds, s is >= 27 and <= 29 ? (byte)255 : (byte)128);
    }

    /// <summary>Цвет состояния (<c>0x5b191f</c>; <c>COLORREF</c> 0x00BBGGRR).</summary>
    public static UiColor Color(BattleFace face) => (int)face switch
    {
        0 or 2 or 9 => new UiColor(255, 0, 0),
        3 or 4 => new UiColor(255, 255, 0),
        5 or 21 or 22 => new UiColor(0, 0, 255),
        6 or 19 => new UiColor(0, 255, 0),
        7 or 8 => new UiColor(128, 128, 128),
        11 => new UiColor(255, 255, 64),
        14 or 15 or 23 => new UiColor(255, 0, 255),
        16 => new UiColor(128, 128, 0),
        17 => new UiColor(0, 0, 128),
        18 => new UiColor(192, 128, 0),
        20 => new UiColor(94, 218, 249),
        _ => new UiColor(0, 0, 0),
    };

    /// <summary>База по состоянию юнита (<c>0x64c269</c>): павший, паралич, (второй опрос паралича вместо окаменения), яд, обморожение, ожог.</summary>
    public static BattleFace Of(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.Hp <= 0) return BattleFace.Dead;
        if (unit.Paralyzed) return BattleFace.Paralyzed;
        if (HasDot(unit, AttackClass.Poison)) return BattleFace.Poisoned;
        if (HasDot(unit, AttackClass.Frostbite)) return BattleFace.Frostbitten;
        if (HasDot(unit, AttackClass.Blister)) return BattleFace.Blistered;
        return BattleFace.Normal;
    }

    /// <summary>Вспышка цели удара (<c>0x63b391</c>, хвост <c>0x63bcc2…0x63bfa8</c>): защита — <see cref="BattleFace.Shielded"/>, промах — <see cref="BattleFace.Missed"/>,
    /// иначе по классу атаки, по умолчанию — <see cref="BattleFace.Hit"/>. Паралич и окаменение (7, 8) вспышкой не ложатся (<c>0x64d05f</c>) — null.
    /// Промах второй атаки до сюда не доходит (выход <c>0x63b402</c>) — его отсекает вызывающий.</summary>
    public static BattleFlash? Strike(AttackClass? cls, bool missed, bool shielded)
    {
        if (shielded) return new(BattleFace.Shielded, false);
        if (missed) return new(BattleFace.Missed, false);
        BattleFace face = cls switch
        {
            AttackClass.Paralyze or AttackClass.Petrify => BattleFace.Paralyzed,
            AttackClass.Heal => BattleFace.Healed,
            AttackClass.BoostDamage => BattleFace.Boosted,
            AttackClass.LowerDamage => BattleFace.LowerDamage,
            AttackClass.LowerInitiative => BattleFace.LowerInitiative,
            AttackClass.Poison => BattleFace.Poisoned,
            AttackClass.Frostbite => BattleFace.Frostbitten,
            AttackClass.Blister => BattleFace.Blistered,
            AttackClass.BestowWards => BattleFace.Warded,
            AttackClass.Shatter => BattleFace.Shattered,
            AttackClass.Revive => BattleFace.Revived,
            AttackClass.Cure => BattleFace.Cured,
            AttackClass.DrainLevel => BattleFace.DrainLevel,
            AttackClass.GiveAttack => BattleFace.GiveAttack,
            AttackClass.Doppelganger or AttackClass.TransformSelf or AttackClass.TransformOther => BattleFace.Transformed,
            AttackClass.Summon => BattleFace.Summoned,
            _ => BattleFace.Hit,   // урон, вампиризм, страх
        };
        if (face == BattleFace.Paralyzed) return null;
        return new(face, face is BattleFace.Transformed or BattleFace.Summoned);
    }

    /// <summary>Вспышка тика в начале хода (шаги 6–8, <c>0x635cd7</c>/<c>0x635d88</c>/<c>0x635e39</c>): яд, мороз, ожог.</summary>
    public static BattleFlash Tick(AttackClass cls) => new(cls switch
    {
        AttackClass.Poison => BattleFace.Poisoned,
        AttackClass.Frostbite => BattleFace.Frostbitten,
        _ => BattleFace.Blistered,
    }, false);

    private static bool HasDot(BattleUnit unit, AttackClass kind) => unit.Dots.Any(d => d.Class == kind);
}
