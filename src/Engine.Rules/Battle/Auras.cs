using LastEdict.Rules.Units;

namespace LastEdict.Rules.Battle;

/// <summary>Кому действует аура юнита в бою.</summary>
public enum AuraReach
{
    /// <summary>Весь свой отряд, кроме самого юнита.</summary>
    Group = 0,

    /// <summary>Соседи по своему отряду: клетка рядом по ряду или колонке (большой юнит — по обеим своим клеткам).</summary>
    Adjacent = 1,

    /// <summary>Весь отряд врага.</summary>
    Enemies = 2,

    /// <summary>Сам юнит (постоянный модификатор, пока он в бою).</summary>
    Self = 3,
}

/// <summary>Аура типа юнита: модификатор (<see cref="Modifier.Id"/>) и кому он действует.</summary>
public sealed record UnitAura(string ModifierId, AuraReach Reach);

/// <summary>
/// Ауры (Студия 2, своё правило Last Edict только для модов): юнит, пока он на поле боя (<see cref="BattleUnit.OnField"/>), даёт модификатор
/// своему отряду, соседям или врагу. Модификатор ауры входит в цепочку цели (<see cref="BattleUnit.Chain"/>) и читается вживую, как варды и
/// зелья: броня, урон, точность, инициатива, иммунитеты, способности; здоровье (<see cref="BattleUnit.HpMax"/>) аура не меняет — оно
/// считается при сборке боя. Ауры пересчитываются в начале раунда, хода, до и после удара (погиб, отступил, призван — после удара; внутри
/// одного площадного удара — прежние). Оценка силы отряда ИИ на карте ауры не знает (бой ИИ видит их через цепочку). У юнитов
/// базы аур нет — без них пересчёт ничего не делает и бросков генератора не трогает: бои базы те же.
/// </summary>
public static class Auras
{
    /// <summary>Пересчитать ауры всех юнитов боя; модификаторы — из справочника правил боя. Одна и та же аура от двух юнитов не
    /// складывается (модификатор с тем же id — один раз).</summary>
    public static void Refresh(BattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        bool any = false;
        foreach (var u in state.Units)
        {
            any |= u.Type.Auras.Count > 0;
            if (u.Auras.Count > 0) u.Auras.Clear();
        }
        if (!any || state.Rules?.Modifiers is not { } catalogue) return;
        foreach (var source in state.Units.Where(u => u.OnField && u.Type.Auras.Count > 0))
            foreach (var aura in source.Type.Auras)
            {
                if (!catalogue.TryGetValue(aura.ModifierId, out var modifier)) continue;
                // Модификатор, который уже в цепочке цели (свой, вард, зелье, другая аура), второй раз не ложится.
                foreach (var target in state.Units.Where(t => t.OnField && Reaches(source, t, aura.Reach)))
                    if (!target.Chain.Any(m => m.Id.Equals(modifier.Id, StringComparison.OrdinalIgnoreCase))) target.Auras.Add(modifier);
            }
    }

    /// <summary>Достаёт ли аура источника до цели.</summary>
    public static bool Reaches(BattleUnit source, BattleUnit target, AuraReach reach)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        return reach switch
        {
            AuraReach.Self => ReferenceEquals(source, target),
            AuraReach.Group => !ReferenceEquals(source, target) && source.Attacker == target.Attacker,
            AuraReach.Enemies => source.Attacker != target.Attacker,
            AuraReach.Adjacent => !ReferenceEquals(source, target) && source.Attacker == target.Attacker && Adjacent(source, target),
            _ => false,
        };
    }

    /// <summary>Соседи по группе: клетки рядом в сетке «три колонки по два ряда» (<see cref="Formation"/>).</summary>
    private static bool Adjacent(BattleUnit a, BattleUnit b)
    {
        foreach (int x in Cells(a))
            foreach (int y in Cells(b))
                if (Math.Abs(Formation.Column(x) - Formation.Column(y)) + Math.Abs((x % 2) - (y % 2)) == 1) return true;
        return false;
    }

    private static IEnumerable<int> Cells(BattleUnit u) => u.Type.SizeSmall ? [u.Slot] : [u.Slot, Formation.Pair(u.Slot)];
}
