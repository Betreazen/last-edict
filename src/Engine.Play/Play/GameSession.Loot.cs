using LastEdict.Data.Map;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>Вещи погибшего отряда и взятой столицы (разбор <c>Discipl2.exe</c> 28.09, <c>docs/ui-windows.md</c> §14; ранг 1, если не сказано
/// иное): у движка предметы не пропадают — уходят победителю, иначе остаются мешком на карте или в складе крепости.</summary>
public sealed partial class GameSession
{
    /// <summary>Итог переноса: новая карта; получатель (отряд или крепость; пусто — никто) и что он получил; новый мешок на карте.</summary>
    private sealed record LootResult(MapState Map, string Receiver, IReadOnlyList<string> Items, BagLook? Bag);

    /// <summary>
    /// Вещи погибшего отряда <paramref name="victim"/> (<c>CEffectKillStack</c> <c>0x43895c</c>) — до его снятия с карты. Убийца
    /// <paramref name="killer"/> — отряд: вся сумка жертвы (и надетое — снимается, <c>0x5d752e</c>) дописывается в конец его сумки в
    /// прежнем порядке (<c>0x5d75f8</c>, предела нет); крепость — в её склад. Иначе (убийцы нет, руина) — <c>CVisitorKillStack</c>
    /// <c>0x60e5a2</c>: отряд в крепости — в её склад, вне — новый мешок на его клетке (картинка 0). Бросков нет.
    /// </summary>
    private LootResult Loot(MapState map, MapStack victim, string killer)
    {
        var items = victim.Items;
        if (items.Count == 0) return new LootResult(map, "", [], null);
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        if (stacks.TryGetValue(victim.Id, out var alive)) stacks[victim.Id] = alive with { Items = [], Equipment = [] };
        if (killer.Length > 0 && !Same(killer, victim.Id) && stacks.TryGetValue(killer, out var winner))
        {
            stacks[killer] = winner with { Items = [.. winner.Items, .. items] };
            return new LootResult(With(map, stacks: stacks), killer, items, null);
        }
        var sites = map.Sites.ToList();
        int fort = killer.Length > 0 ? sites.FindIndex(s => Same(s.Id, killer) && s.Kind is SiteKind.Capital or SiteKind.Village) : -1;
        if (fort >= 0)
        {
            sites[fort] = sites[fort] with { Items = [.. sites[fort].Items, .. items] };
            return new LootResult(With(map, stacks: stacks, sites: sites), killer, items, null);
        }
        int inside = victim.Inside.Length > 0 ? sites.FindIndex(s => Same(s.Id, victim.Inside)) : -1;
        if (inside >= 0)
        {
            sites[inside] = sites[inside] with { Items = [.. sites[inside].Items, .. items] };
            return new LootResult(With(map, stacks: stacks, sites: sites), "", [], null);
        }
        var ids = map.IdNext;
        string bagId = NewId(ref ids, map.Sites.Select(s => s.Id).Concat(map.Plan.Values.SelectMany(v => v)), victim.Id, "BG");
        sites.Add(new MapSite { Id = bagId, Kind = SiteKind.Bag, RawKind = "MidBag", X = victim.X, Y = victim.Y, Items = items });
        var plan = map.Plan.ToDictionary(p => p.Key, p => p.Value);
        plan[(victim.X, victim.Y)] = [.. plan.GetValueOrDefault((victim.X, victim.Y)) ?? [], bagId];
        return new LootResult(With(map, stacks: stacks, sites: sites, plan: plan, idNext: ids), "", [], new BagLook(bagId, victim.X, victim.Y, 0));
    }

    /// <summary>Вещи погибшего отряда сейчас (до его снятия): карта и мешок — в состояние; сообщение 44 получателю — если
    /// <paramref name="announce"/> (у эффекта события его нет, флаг <c>+0x10</c>).</summary>
    private void LootNow(string victimId, string killer, bool announce = true)
    {
        if (!Map.Stacks.TryGetValue(victimId, out var victim)) return;
        var loot = Loot(Map, victim, killer);
        if (ReferenceEquals(loot.Map, Map)) return;
        State = State with
        {
            Looks = Retold(Looks, loot.Map, Looks.Stacks) with { Bags = loot.Bag is { } bag ? [.. Looks.Bags, bag] : Looks.Bags },
        };
        if (announce) AnnounceLoot(loot);
    }

    /// <summary>Сообщение 44 хозяину получателя (отряда или крепости) — после снятия погибшего (<c>0x438cc2</c>).</summary>
    private void AnnounceLoot(LootResult loot)
    {
        if (loot.Receiver.Length == 0) return;
        var map = Map;
        if (map.Stacks.TryGetValue(loot.Receiver, out var stack)) Given(TurnSightKind.ItemsGiven, stack.Owner, stack.X, stack.Y, stack.Id, loot.Items);
        else if (map.Sites.FirstOrDefault(s => Same(s.Id, loot.Receiver)) is { } fort)
            Given(TurnSightKind.ItemsGiven, fort.Owner, fort.X, fort.Y, fort.Id, loot.Items);
    }

    /// <summary>
    /// Взятие столицы (<c>CEffectCaptureCapital</c> <c>0x43496a</c>, после казны порта): весь склад столицы — в конец сумки захватившего
    /// отряда (<c>0x434e8b</c>, сообщение 44); затем один бросок R250 партии <c>Next(100)</c> &lt; <c>GAIN_SPELL</c> (<c>0x434b3b</c>) — захватчик
    /// узнаёт все заклинания погибшего, которых не знал (сообщение 43 с флагом «библиотека»). Место броска в последовательности захвата — ранг 2.
    /// </summary>
    private (MapState Map, IReadOnlyList<string> Items, IReadOnlyList<string> Spells) CaptureCapitalLoot(MapState map, MapSite capital, MapStack stack,
                                                                                                        string loserId)
    {
        var items = capital.Items;
        if (items.Count > 0)
        {
            var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            stacks[stack.Id] = stacks[stack.Id] with { Items = [.. stacks[stack.Id].Items, .. items] };
            var sites = map.Sites.Select(s => Same(s.Id, capital.Id) ? s with { Items = [] } : s).ToList();
            map = With(map, stacks: stacks, sites: sites);
        }
        IReadOnlyList<string> spells = [];
        if (LootRng.Next(100) < RequireBook().Vars.GainSpell && map.Players.TryGetValue(loserId, out var loser)
            && map.Players.TryGetValue(stack.Owner, out var captor))
        {
            spells = [.. loser.KnownSpells.Where(s => !captor.KnownSpells.Contains(s, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase)];
            if (spells.Count > 0)
            {
                var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                players[captor.Id] = captor with { KnownSpells = [.. captor.KnownSpells, .. spells] };
                map = With(map, players: players);
            }
        }
        return (map, items, spells);
    }
}
