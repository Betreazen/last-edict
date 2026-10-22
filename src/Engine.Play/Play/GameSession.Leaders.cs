using LastEdict.Rules.Units;

namespace LastEdict.Data.Play;

/// <summary>Навык лидера за уровень — общее для окна выбора человека и списка 1 ИИ (<c>docs/ai-economy.md</c> §6).
/// Повышение уровня копит отложенные выборы в <see cref="LastEdict.Rules.Map.MapStack.UpgradeCount"/>; выбор снимает один и
/// кладёт <c>MODIF_ID</c> строки <c>GleaUpg</c> в цепочку модификаторов лидера (<see cref="SkillChoice"/>).</summary>
public sealed partial class GameSession
{
    /// <summary>Предложение навыков лидеру отряда (<c>0x5d8b84</c>): строки лорда владельца с <c>MIN_LEVEL</c> ≤ уровня,
    /// ещё не взятые, без дающих врождённую способность типа (<see cref="LeaderUpgrades.Available"/>), в порядке таблицы.
    /// Пусто — нет отряда, лидер пал или выбирать нечего.</summary>
    public IReadOnlyList<LeaderUpgrade> SkillOffer(string stackId)
    {
        var book = RequireBook();
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack) || !stack.LeaderAlive || stack.UpgradeCount <= 0
            || !map.Units.TryGetValue(stack.LeaderId, out var leader) || !map.Players.TryGetValue(stack.Owner, out var player))
            return [];
        return LeaderUpgrades.Available(book.LeaderUpgrades, player.LordId, leader.Level, Taken(book, leader),
                                        LastEdict.Rules.Units.LeaderAbilities.Innate(leader.TypeId, book.InnateAbilities), book.Rules.Modifiers);
    }

    /// <summary>Выбор навыка <paramref name="modifId"/> лидером отряда (<c>visitorLeaderUpgrade</c> <c>0x5e84c2</c>,
    /// <c>apply = 1</c>): навык из <see cref="SkillOffer"/>, у лидера меньше <c>GVars.LDRMAXABIL</c> навыков; очередь
    /// <see cref="LastEdict.Rules.Map.MapStack.UpgradeCount"/> уменьшается на один. Броска нет (журнал: <c>rng_before</c> =
    /// <c>rng_after</c>).</summary>
    public SkillRefusal ChooseSkill(string stackId, string modifId)
    {
        var book = RequireBook();
        var map = Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack) || !stack.LeaderAlive
            || !map.Units.TryGetValue(stack.LeaderId, out var leader) || !map.Players.TryGetValue(stack.Owner, out var player))
            return SkillRefusal.NotAvailable;
        var chosen = SkillChoice.ByPlayer(book.LeaderUpgrades, player.LordId, leader.Level, stack.UpgradeCount,
                                          Taken(book, leader), modifId, book.Vars.LeaderMaxAbilities,
                                          LastEdict.Rules.Units.LeaderAbilities.Innate(leader.TypeId, book.InnateAbilities), book.Rules.Modifiers);
        if (chosen.Refusal != SkillRefusal.None) return chosen.Refusal;
        string modif = chosen.Upgrade!.ModifId.ToUpperInvariant();
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var upgraded = leader with { Modifiers = [.. leader.Modifiers, modif] };
        // Сервер после visitorLeaderUpgrade (0x5d6e21: 0x5e84c2, затем hasElement модификатора, слот +0x1c): обзор — туман вокруг
        // отряда (ScoutingRange, 0x5d52fe); запас хода — до нового наибольшего (MoveAllowance: 0x5ed035, 0x5e780d с «текущий −
        // наибольший»; корпус — 13 строк ap_restore cause skill_pick, у всех move_after = max); здоровье лидера — до нового наибольшего
        // (Hp: 0x5e987c — наибольшее, 0x479bd6 — текущее здоровье +0x20, визитёр 0x5e75f3 прибавляет разность). И ход, и здоровье
        // встают ровно на новый наибольший, сколько бы ни было до выбора (0x5e780d вычитает «текущий − наибольший»).
        var elements = book.Rules.Modifiers?.GetValueOrDefault(modif)?.Elements ?? [];
        if (elements.Any(e => e.Type == ModifierElementType.Hp) && HpMax(upgraded, book) is { } hpMax)
            upgraded = upgraded with { Hp = hpMax };
        units[leader.Id] = upgraded;
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var after = stack with { UpgradeCount = chosen.Pending };
        if (elements.Any(e => e.Type == ModifierElementType.MoveAllowance))
            after = after with { Move = Allowance(With(map, units: units), after) };
        stacks[stack.Id] = after;
        var next = With(map, units: units, stacks: stacks);
        if (elements.Any(e => e.Type == ModifierElementType.ScoutingRange)) RevealAround(next, after, after.X, after.Y);
        State = State with { Looks = Retold(Looks, next, Looks.Stacks) };
        return SkillRefusal.None;
    }

    /// <summary>Уже взятые навыки: часть цепочки лидера, которая есть в <c>GleaUpg</c> (надетые предметы — не навыки).</summary>
    private static IReadOnlyList<string> Taken(GameBook book, LastEdict.Rules.Map.MapUnit leader) =>
        [.. leader.Modifiers.Where(id => book.Rules.LeaderUpgradeModifiers?.Contains(id) ?? false)];
}
