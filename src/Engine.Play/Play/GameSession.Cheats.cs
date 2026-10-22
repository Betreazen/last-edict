using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>Коды опыта из чата (<c>jump</c>, <c>stairwaytoheaven</c>) и уровень создания отряда, который они меняют.</summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Код опыта игрока <paramref name="playerId"/> (<c>CVisitorChangeStackCreationLevel</c>, 16.10, ранг 1 по листингу и сохранениям):
    /// <c>jump</c> (<paramref name="fullLevel"/> = false, <c>0x4305c6</c> → <c>0x43129a</c>) и <c>stairwaytoheaven</c> (true,
    /// <c>0x43062a</c> → <c>0x4314f9</c>) — оба зовут <c>0x4312ba</c>. Разбор чата (<c>0x42253e</c>) отдаёт код диспетчеру
    /// <c>0x43002c</c>, строки кодов хранятся XOR <c>0x24</c> (<c>0x79544c</c>, <c>0x795450</c>).
    /// <para><c>0x4312ba</c>: по всем отрядам игрока (<c>+0x8c</c>, укрепления не входят) и всем их юнитам, кроме знатных
    /// (<c>IUsNoble</c>), призванных (<c>IUsSummon</c>) и лидеров на потолке уровня (<c>IUsStackLeader</c>, уровень ≥ <c>MaxLeader</c>;
    /// <c>0x431487</c>), — прибавка <c>XP_NEXT − (полный ? 0 : 1) − опыт</c> (<c>0x5e7641</c>, опыт зажат в [0, XP_NEXT]); после юнита
    /// отряд с уровнем создания 1 получает 2 (<c>0x43141d</c> → <c>0x5e8b10</c> → <c>CVisitorChangeStackCreationLevel</c>
    /// <c>0x60f266</c> → <c>0x5ed7ed</c>, допустимо 1…99). Так подкачанного героя нельзя вывезти (<see cref="ExportRefusal"/>).
    /// Повышения код не делает — юнит растёт потом обычным путём.</para>
    /// <para>Других мест, где движок меняет <c>CREAT_LVL</c>, нет: обёртку <c>0x5e8b10</c> зовут ещё только начало сценария
    /// (<c>0x422423</c>) и вставка героя (<c>0x42f5c5</c>), сеттер <c>0x5ed7ed</c> — только этот посетитель.</para>
    /// <para>Чата у порта пока нет (окно <c>DLG_CHAT</c> — раздел 5 <c>docs/port-status-20261016.md</c>, п. 6); код — точка для него.</para>
    /// </summary>
    public void CheatExperience(string playerId, bool fullLevel)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        var book = Book ?? throw new InvalidOperationException("код опыта: справочника нет");
        var map = Map;
        int maxLeader = map.Limits?.MaxLeader ?? int.MaxValue;
        var units = map.Units.ToDictionary(u => u.Key, u => u.Value, StringComparer.OrdinalIgnoreCase);
        var stacks = map.Stacks.ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var stack in map.Stacks.Values.Where(s => s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)))
        {
            bool given = false;
            foreach (string id in stack.UnitIds)
            {
                if (!units.TryGetValue(id, out var unit) || SkipsExperienceCode(book, unit, maxLeader)) continue;
                int xpNext = book.XpNextOf(unit.TypeId, unit.Level);
                // XP_NEXT = 0 (тип без роста) — у движка та же прибавка, зажатая в [0, 0]: опыт 0 и отряд всё равно получает 2.
                units[id] = unit with { Xp = Math.Clamp(xpNext - (fullLevel ? 0 : 1), 0, xpNext) };
                given = true;
            }
            if (given && stack.CreationLevel == 1) stacks[stack.Id] = stack with { CreationLevel = 2 };
        }
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks, units: units), Looks.Stacks) };
    }

    /// <summary>Кого код опыта пропускает (<c>0x431487</c>): знатных (вор, жезлоносец), призванных и лидера (реализация
    /// <c>IUsStackLeader</c>), чей уровень уже не ниже потолка лидеров сценария.</summary>
    private static bool SkipsExperienceCode(GameBook book, MapUnit unit, int maxLeader)
    {
        int category = book.CategoryOf(unit.TypeId) ?? UnitCategory.Soldier;
        if (category is UnitCategory.Noble or UnitCategory.Summon) return true;
        return category == UnitCategory.Leader && unit.Level >= maxLeader;
    }
}
