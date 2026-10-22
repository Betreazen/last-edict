using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>Имя героя человека в первый ход (окно <c>CChangeFirstLeaderNameInterf</c>, <c>DLG_GETINFO_BOX</c>; разбор —
/// <c>docs/day-cycle.md</c> §17).</summary>
public sealed partial class GameSession
{
    /// <summary>Герой человека для окна имени первого хода: отряд в его столице, тип лидера и нынешнее имя (стартовое имя расы,
    /// <see cref="NewGame"/>); нет столицы, отряда или живого лидера — null.</summary>
    public (string StackId, string LeaderType, string Name)? FirstLeader(string playerId)
    {
        var capital = Map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Capital && s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase));
        if (capital is null || capital.StackId.Length == 0 || !Map.Stacks.TryGetValue(capital.StackId, out var stack) || !stack.LeaderAlive
            || !Map.Units.TryGetValue(stack.LeaderId, out var leader)) return null;
        string name = Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase))?.LeaderName ?? "";
        return (stack.Id, leader.TypeId, name);
    }

    /// <summary>Переименовать лидера отряда (кнопка окна имени, <c>0x4ba115</c> → <c>0x5d74b6</c>): имя без пробелов по краям; пустое —
    /// отказ (строка окна <c>X005TA0345</c>), нет отряда — отказ словами.</summary>
    public string? RenameLeader(string stackId, string name)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return "пустое имя";
        if (!Map.Stacks.ContainsKey(stackId)) return "нет отряда";
        State = State with
        {
            Looks = Looks with
            {
                Stacks = [.. Looks.Stacks.Select(s => s.Id.Equals(stackId, StringComparison.OrdinalIgnoreCase) ? s with { LeaderName = trimmed } : s)],
            },
        };
        return null;
    }
}
