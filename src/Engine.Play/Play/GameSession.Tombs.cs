using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>
/// Могилы (<c>CMidTomb</c>; разбор — <c>docs/day-cycle.md</c> §13). Ставит их только гибель целого отряда в бою
/// (<c>CEffectKillStack</c> → <c>CVisitorKillStack</c> <c>0x60e367</c>; снос отряда эффектом <c>L_REMOVE_STACK</c> — без могилы). Могилы нет
/// (<c>CanCreateTomb</c> <c>0x605f14</c>), если отряд в городе или руине, лидера нет, лидер призванный или «знатный», клетка — лес
/// или вода. На клетке уже есть могила — запись дописывается в конец, иначе новая могила. Запись: владелец, имя лидера, убийца (игрок;
/// нейтрал или пусто — тип лидера отряда-победителя, <c>0x605847</c>), день. Могилы не снимаются. Корпус (433 сохранения): 252 эпитафии
/// там, где погиб отряд, и 97 отрядов без могилы — все по этим правилам.
/// </summary>
public sealed partial class GameSession
{
    private MapState Buried(MapState after, IEnumerable<MapStack> dead, string winnerGroup, string killer)
    {
        var book = RequireBook();
        var tombs = after.Tombs.ToList();
        var plan = after.Plan.ToDictionary(p => p.Key, p => p.Value);
        bool changed = false;
        foreach (var stack in dead)
        {
            if (stack.Inside.Length > 0 || !Map.Units.TryGetValue(stack.LeaderId, out var leader)
                || book.CategoryOf(leader.TypeId) is UnitCategory.Summon or UnitCategory.Noble
                || !Map.Contains(stack.X, stack.Y) || Map.Tiles[stack.X, stack.Y].Ground is Ground.Forest or Ground.Water) continue;
            string name = Looks.Stacks.FirstOrDefault(s => Same(s.Id, stack.Id))?.LeaderName ?? "";
            var entry = new TombEntry(stack.Owner, KillerOf(winnerGroup, killer), Map.Turn, name);
            int at = tombs.FindIndex(t => t.X == stack.X && t.Y == stack.Y);
            if (at >= 0) tombs[at] = tombs[at] with { Entries = [.. tombs[at].Entries, entry] };
            else
            {
                var tomb = new MapTomb(NextTombId(tombs, stack.Id), stack.X, stack.Y, [entry]);
                tombs.Add(tomb);
                plan[(tomb.X, tomb.Y)] = [.. plan.GetValueOrDefault((tomb.X, tomb.Y)) ?? [], tomb.Id];
            }
            changed = true;
        }
        return changed ? With(after, plan: plan, tombs: tombs) : after;
    }

    /// <summary>Убийца в записи: игрок-победитель; нейтрал или пусто — тип лидера отряда-победителя (<c>G000UU…</c>).</summary>
    private string KillerOf(string winnerGroup, string killer)
    {
        if (killer.Length > 0 && !IsNeutral(killer)) return killer;
        return Map.Stacks.TryGetValue(winnerGroup, out var stack) && Map.Units.TryGetValue(stack.LeaderId, out var leader)
            ? leader.TypeId : killer;
    }

    /// <summary>Id новой могилы: префикс сценария, <c>TB</c>, номер на единицу больше наибольшего.</summary>
    private static string NextTombId(IReadOnlyList<MapTomb> tombs, string anyId)
    {
        int max = tombs.Select(t => t.Id.Length == 10 && int.TryParse(t.Id[6..], System.Globalization.NumberStyles.HexNumber,
                                                                       System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : -1)
                       .DefaultIfEmpty(-1).Max();
        string prefix = tombs.Count > 0 ? tombs[0].Id[..4] : anyId[..4];
        return $"{prefix}TB{(max + 1).ToString("x4", System.Globalization.CultureInfo.InvariantCulture)}";
    }
}
