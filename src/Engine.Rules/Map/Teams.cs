namespace LastEdict.Rules.Map;

/// <summary>
/// Команды (Р15, Э4): игроки с одной <see cref="MapPlayer.Team"/> — вечные союзники с общей победой (2×3, 2×2+1 — задаёт сценарий или
/// лобби). Союз команды — пара отношений своих сторон: союз, мир и «ИИ не разорвёт» (бит 31, <see cref="Relation.AiCannotBreak"/>);
/// человек разорвать его тоже не может (<see cref="Unbreakable"/>). Победа команды — когда не осталось живых соперников вне её
/// (<see cref="CapitalCapture.LiveOpponents"/>), и победили все её игроки, даже павшие.
/// </summary>
public static class Teams
{
    /// <summary>Отношение союзников команды: мир — наибольшее (0…100).</summary>
    public const int TeamRelation = 100;

    /// <summary>Отношение новой пары сторон, которой нет в отношениях карты: середина шкалы — нейтралитет.</summary>
    public const int FreshRelation = 50;

    /// <summary>Слово отношений союзников команды: союз (бит 0), <see cref="TeamRelation"/> (биты 1…7), «ИИ не разорвёт» (бит 31).</summary>
    public const uint TeamRaw = 1u | ((uint)TeamRelation << 1) | (1u << 31);

    /// <summary>Одна команда: у обоих задана и совпадает (регистр не важен).</summary>
    public static bool SameTeam(MapPlayer? a, MapPlayer? b) =>
        a?.Team is { Length: > 0 } ta && b?.Team is { Length: > 0 } tb && ta.Equals(tb, StringComparison.OrdinalIgnoreCase);

    /// <summary>Игроки одной команды (или один и тот же игрок).</summary>
    public static bool SameTeam(MapState map, string a, string b)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        return map.Players.TryGetValue(a, out var pa) && map.Players.TryGetValue(b, out var pb) && SameTeam(pa, pb);
    }

    /// <summary>Союз пары нерасторжим: стороны — одна команда.</summary>
    public static bool Unbreakable(MapState map, string a, string b) => !a.Equals(b, StringComparison.OrdinalIgnoreCase) && SameTeam(map, a, b);

    /// <summary>
    /// Отношения карты, где у каждой пары сторон играбельных игроков есть запись (новой паре — <see cref="FreshRelation"/>), а пары одной команды
    /// — союз команды (<see cref="TeamRaw"/>). Остальные пары — как были. Ничего не изменилось — та же ссылка.
    /// </summary>
    public static IReadOnlyList<Relation> Bound(MapState map, EconomyRules rules)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rules);
        var playable = map.Players.Values.Where(p => rules.Races.TryGetValue(p.RaceId, out var r) && r.Playable)
            .OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        var relations = map.Relations.ToList();
        bool changed = false;
        for (int i = 0; i < playable.Count; i++)
            for (int j = i + 1; j < playable.Count; j++)
            {
                int a = Diplomacy.PartyOf(playable[i], rules.Races)!.Value, b = Diplomacy.PartyOf(playable[j], rules.Races)!.Value;   // раса есть — отобраны выше
                if (a == b) continue;
                int at = relations.FindIndex(r => r.Pairs(a, b));
                bool team = SameTeam(playable[i], playable[j]);
                if (at < 0)
                {
                    relations.Add(new Relation(a, b, team ? TeamRaw : (uint)FreshRelation << 1));
                    changed = true;
                }
                else if (team && relations[at].Raw != TeamRaw)
                {
                    relations[at] = relations[at] with { Raw = TeamRaw };
                    changed = true;
                }
            }
        return changed ? relations : map.Relations;
    }
}
