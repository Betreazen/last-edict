using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;

namespace LastEdict.Rules.Units;

/// <summary>Строка <c>GDynUpgr.DBF</c> — прибавка за один уровень. Пустая колонка <c>.dbf</c> здесь
/// значит нулевую прибавку (в отличие от <see cref="ModifierElement"/>, где «не задано» несёт смысл:
/// там за этим стоит <c>ModifierValue.initialized</c>, а тут — обычное целое поле
/// <c>CDynUpgrade</c>).
/// <para>Дом записи — <c>LastEdict.Rules</c>, строит её <c>LastEdict.Data.Battle.RulesLoader</c>: ссылка идёт
/// только <c>LastEdict.Data → LastEdict.Rules</c>. Ровно так задача 3 свела в одно место
/// <see cref="Modifier"/>.</para>
/// <para><c>EnrollCost</c> и три остальных банка (<c>REVIVE_C</c>, <c>HEAL_C</c>,
/// <c>TRAINING_C</c>) движок тоже умножает на число шагов (GOG <c>0x598d55</c>, <c>0x598dd3</c>,
/// <c>0x598e4e</c>, <c>0x598ec9</c> — те же множители, что у чисел). Здесь растят только боевые
/// статы: цена найма прокачанного юнита — экономика, её считает <c>LastEdict.Rules.Map</c>. Поле оставлено,
/// потому что оно и так читается из таблицы.</para></summary>
public sealed record DynUpgrade(string Id, Bank EnrollCost, int HitPoints, int Armor, int Regen,
                                int XpKilled, int XpNext, int Move, int Negotiate, int Damage,
                                int Heal, int Initiative, int Power);

/// <summary>Сколько раз применена каждая из двух прибавок: <c>Gunits.DYN_UPG1</c> и
/// <c>DYN_UPG2</c>.</summary>
public readonly record struct GrowthSteps(int First, int Second);

/// <summary>Юнит, выращенный до уровня <see cref="Level"/>. <see cref="Type"/> — тот же
/// <see cref="UnitType"/> с выросшими HP, бронёй и порогами опыта; регенерация и запас хода в
/// <see cref="UnitType"/> не живут (их читает <c>LastEdict.Data.RulesLoader.LoadExtras</c>) и потому едут
/// рядом — в том же виде, в каком их ждёт <see cref="EffectiveStats.Of"/>.</summary>
public sealed record GrownUnit(UnitType Type, int Level, int Regeneration, int MoveAllowance);

/// <summary>Уровневый рост юнита: <c>Gunits.DYN_UPG1</c> действует до уровня <c>DYN_UPG_LV</c>,
/// дальше <c>DYN_UPG2</c>.
///
/// <para><b>Порядок слоёв.</b> Рост — это база, а не модификатор: движок собирает выращенную
/// реализацию юнита (<c>CUnitGenerator::generateUnitImpl</c>), и уже поверх неё вешаются
/// <c>CUmModifier</c>. Поэтому правильная последовательность —
/// <c>UnitGrowth.Of(...)</c>, а затем <see cref="EffectiveStats.Of"/> на результат; наоборот —
/// значит посчитать процент от невыращенного стата.</para>
///
/// <para><b>Ранг 1 — прочитано в дизассемблере <c>game-pristine/Discipl2.exe</c> (GOG 3.01).</b>
/// Число шагов считает <c>0x598a86</c> (шесть аргументов, <c>ret 0x18</c>):
/// <c>baseLevel = soldier-&gt;getLevel()</c> (слот vftable <c>+0x20</c>),
/// <c>dynUpgLvl = soldier-&gt;getDynUpgLvl()</c> (<c>+0x68</c>), обе прибавки берутся
/// <c>findDynUpgradeById</c> (<c>0x597781</c>) по <c>getDynUpg1</c> (<c>+0x64</c>) и
/// <c>getDynUpg2</c> (<c>+0x6c</c>), после чего
/// <c>0x598b40</c>: <c>second = level &gt; dynUpgLvl ? level − dynUpgLvl : 0</c>,
/// <c>0x598b4c</c>: <c>first = level − baseLevel − second</c>.
/// Применение — <c>0x598b68</c> (статы юнита) и <c>0x5998f5</c> (статы атаки): у каждого поля
/// <c>база + first·upg1.поле + second·upg2.поле</c>, без округлений и потолков.
/// Смещения полей <c>CDynUpgrade</c> подтверждены геттерами: <c>0x45303d</c> → <c>+0x10</c> (hp),
/// <c>0x44fcb1</c> → <c>+0x14</c> (armor), <c>0x5340b9</c> → <c>+0x18</c> (regen),
/// <c>0x597654</c> → <c>+0x40</c> (xpKilled), <c>0x597658</c> → <c>+0x44</c> (xpNext),
/// <c>0x597668</c> → <c>+0x50</c> (initiative), <c>0x47d4a8</c> → <c>+0x54</c> (power),
/// <c>0x47d4ac</c> → <c>+0x58</c> (damage), <c>0x597664</c> → <c>+0x5c</c> (heal).</para>
///
/// <para><b><c>UPGRADE_B</c> сюда не входит.</b> Она ведёт в <c>Gbuild</c> (107 из 107, измерено
/// задачей 1) — это постройка, открывающая улучшение юнита, а не рост.</para>
///
/// <para><b>Потолок уровня — не отсюда.</b> Лидера дальше <c>CScenarioInfo.leaderMaxLevel</c>
/// (<c>+0x68</c>) не поднимают: <c>0x5d8a41</c> сравнивает <c>getLevel()</c> с ним и выходит.
/// Это свойство сценария, а не таблицы роста.</para>
/// </summary>
public static class UnitGrowth
{
    /// <summary>Число применений каждой прибавки. Отрицательный <c>First</c> движок не отсекает
    /// (отсекается только <c>Second</c>, <c>0x598b58 jns</c>) — в ванили это недостижимо: у всех
    /// 356 юнитов <c>DYN_UPG_LV</c> = 10, а <c>LEVEL</c> не больше 5. Воспроизводим движок как есть,
    /// а невозможность проверяет тест <c>GrowthTests.VanillaSteps_AreNeverNegative</c>.</summary>
    public static GrowthSteps StepsFor(int baseLevel, int dynUpgLevel, int level)
    {
        if (level < baseLevel)
            throw new ArgumentOutOfRangeException(nameof(level),
                $"уровень {level} ниже собственного уровня типа ({baseLevel}): " +
                "движок такой юнит не порождает, вызывающий обязан ограничить уровень снизу");

        int second = level > dynUpgLevel ? level - dynUpgLevel : 0;
        return new GrowthSteps(level - baseLevel - second, second);
    }

    /// <summary>Статы юнита на уровне <paramref name="level"/>. <paramref name="first"/> и
    /// <paramref name="second"/> — строки <c>GDynUpgr</c> по <c>DYN_UPG1</c>/<c>DYN_UPG2</c>;
    /// <c>null</c> означает пустую ссылку <c>g000000000</c> и даёт нулевую прибавку — ровно как
    /// проверка на <c>emptyId</c> в <c>0x598acb</c>.
    /// <para><paramref name="regeneration"/> и <paramref name="moveAllowance"/> — база из
    /// <c>Gunits.REGEN</c>/<c>MOVE</c>: в <see cref="UnitType"/> этих колонок нет, их читает
    /// <c>LastEdict.Data.RulesLoader.LoadExtras</c>. Тот же приём, что у
    /// <see cref="EffectiveStats.Of"/>.</para></summary>
    public static GrownUnit Of(UnitType type, int dynUpgLevel, int level,
                               DynUpgrade? first, DynUpgrade? second,
                               int regeneration = 0, int moveAllowance = 0)
    {
        var steps = StepsFor(type.Level, dynUpgLevel, level);
        return new GrownUnit(
            type with
            {
                HitPoints = Grow(type.HitPoints, steps, first?.HitPoints, second?.HitPoints),
                Armor = Grow(type.Armor, steps, first?.Armor, second?.Armor),
                XpKilled = Grow(type.XpKilled, steps, first?.XpKilled, second?.XpKilled),
                XpNext = Grow(type.XpNext, steps, first?.XpNext, second?.XpNext),
            },
            level,
            Grow(regeneration, steps, first?.Regen, second?.Regen),
            Grow(moveAllowance, steps, first?.Move, second?.Move));
    }

    /// <summary>Статы атаки на том же числе шагов (<c>0x5998f5</c>).
    /// <para><b>Три поля из четырёх растут только с ненулевой базы</b> — это ветвления движка, а не
    /// упрощение: инициатива прибавляется всегда, сила — при <c>power &gt; 0</c> (<c>0x5999b7 jle</c>),
    /// урон — при <c>qtyDamage &gt; 0</c> (<c>0x5999ef jle</c>), лечение — при <c>qtyHeal ≠ 0</c>
    /// (<c>0x599a27 je</c>). Иначе лечащая атака набирала бы силу, а бьющая — лечение: в
    /// <c>GDynUpgr</c> у одной и той же строки заполнены и <c>DAMAGE</c>, и <c>HEAL</c>.</para></summary>
    public static AttackType OfAttack(AttackType attack, GrowthSteps steps,
                                      DynUpgrade? first, DynUpgrade? second) =>
        attack with
        {
            Initiative = Grow(attack.Initiative, steps, first?.Initiative, second?.Initiative),
            Power = attack.Power > 0 ? Grow(attack.Power, steps, first?.Power, second?.Power) : attack.Power,
            QtyDamage = attack.QtyDamage > 0
                ? Grow(attack.QtyDamage, steps, first?.Damage, second?.Damage) : attack.QtyDamage,
            QtyHeal = attack.QtyHeal != 0
                ? Grow(attack.QtyHeal, steps, first?.Heal, second?.Heal) : attack.QtyHeal,
        };

    private static int Grow(int value, GrowthSteps steps, int? first, int? second) =>
        value + steps.First * (first ?? 0) + steps.Second * (second ?? 0);
}
