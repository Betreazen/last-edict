using D2.Rules.Rng;

namespace D2.Rules.Map;

/// <summary>Запись квест-лога (<c>MidQuestLog</c>): попап события, показанный игроку. Пишет только <c>L_POPUP</c>
/// (<c>0x43b290</c>); <c>TYPE</c> у движка всегда 0 и здесь не хранится.</summary>
/// <param name="Seq">Номер записи у игрока: наибольший + 1 (ранг 2).</param>
/// <param name="EffectNum">Номер эффекта-попапа в событии (<c>NUM</c>).</param>
public sealed record QuestLogEntry(string PlayerId, int Seq, int Turn, string EventId, int EffectNum);

/// <summary>Состояние событий по ходу партии — то, что пишется в сохранение: какие события включены, значения переменных,
/// квест-лог и текст цели, если его сменило событие. Неизменяемое: эффекты строят новое.</summary>
/// <param name="Disabled">Выключенные события (<c>ENABLED</c> = 0): выключенные в файле, отработавшие <c>OCCUR_ONCE</c> и
/// выключенные эффектом <c>L_ENABLE_EVENT</c>.</param>
/// <param name="Objective">Текст цели после <c>L_CHANGE_OBJECTIVE</c>; пусто — как в сценарии.</param>
public sealed record EventProgress(IReadOnlyList<string> Disabled, IReadOnlyDictionary<int, int> Variables,
                                   IReadOnlyList<QuestLogEntry> Log, string Objective = "")
{
    public static readonly EventProgress Empty = new([], new Dictionary<int, int>(), []);

    /// <summary>Начало партии: выключено то, что выключено в файле; переменные — из сценария.</summary>
    public static EventProgress From(ScenarioEventSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return new EventProgress([.. set.Events.Where(e => !e.Enabled).Select(e => e.Id)],
                                 new Dictionary<int, int>(set.Variables), []);
    }

    public bool IsEnabled(string eventId) => !Disabled.Contains(eventId, StringComparer.OrdinalIgnoreCase);

    /// <summary>Включить или выключить событие (<c>CVisitorEnableEvent</c> <c>0x5e8c4a</c>).</summary>
    public EventProgress Enable(string eventId, bool enable) =>
        enable == IsEnabled(eventId) ? this
        : this with { Disabled = enable ? [.. Disabled.Where(d => !d.Equals(eventId, StringComparison.OrdinalIgnoreCase))]
                                        : [.. Disabled, eventId] };

    /// <summary>Переменная после <c>L_MODIFY_VARIABLE</c> (<c>0x43995c</c>): 0 +, 1 −, 2 × (32 бита), 3 ÷ (к нулю; на ноль
    /// движок падает — здесь переменная не меняется), иначе присвоить; итог в [−99 999 999; 999 999 999]. Ранг 1.</summary>
    public EventProgress Modify(ModifyVariableEffect v)
    {
        ArgumentNullException.ThrowIfNull(v);
        int now = Variables.GetValueOrDefault(v.Variable);
        // Все четыре действия — в 32 битах с переполнением, зажим потом (0x43995c, REA 13.10): 999 999 999 + 2 000 000 000 у движка
        // переполняется в отрицательное и даёт −99 999 999, а не 999 999 999.
        int next = v.Operation switch
        {
            0 => unchecked(now + v.Operand),
            1 => unchecked(now - v.Operand),
            2 => unchecked(now * v.Operand),
            3 when v.Operand == 0 => now,
            3 => now / v.Operand,
            _ => v.Operand,
        };
        var variables = new Dictionary<int, int>(Variables) { [v.Variable] = Math.Clamp(next, VariableMin, VariableMax) };
        return this with { Variables = variables };
    }

    /// <summary>Запись попапа в квест-лог игрока.</summary>
    public EventProgress Logged(string playerId, int turn, string eventId, int effectNum)
    {
        int seq = Log.Where(l => l.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase)).Select(l => l.Seq).DefaultIfEmpty(0).Max() + 1;
        return this with { Log = [.. Log, new QuestLogEntry(playerId, seq, turn, eventId, effectNum)] };
    }

    public const int VariableMin = -99_999_999;

    public const int VariableMax = 999_999_999;
}

/// <summary>Одно выполнение события для кандидата: эффекты по порядку <c>NUM</c>. У <c>OCCUR_ONCE</c> первым идёт выключение
/// самого события (<see cref="DisablesItself"/>) — движок кладёт его в начало списка эффектов (<c>0x4378c2</c>), и применяется
/// оно вместе с остальными, после проверки всех кандидатов.</summary>
/// <param name="PlayerId">Кандидат X, для которого событие сработало.</param>
/// <param name="TriggererId">Отряд-триггерер проверки; пусто — проверка без отряда.</param>
public sealed record EventFiring(ScenarioEvent Event, string PlayerId, string TriggererId)
{
    public bool DisablesItself => Event.OccurOnce;

    /// <summary>Срабатывание упреждающей проверки пути, которое путь оборвало (<c>0x440284</c>): эффекты в ход не уходят — список
    /// проверки остаётся у неё и уничтожается (перенос <c>0x43f8f5</c> — только у безвредных человека), а событие проверяется заново
    /// после настоящего хода со своим броском шанса (<c>StackMove 0x41e46b</c>). Только обрыв и показ невидимого.</summary>
    public bool CutOnly { get; init; }
}

/// <summary>Что нужно событиям о правилах: номера рас (<c>Grace.RACE_TYPE</c>) и играбельность — по расе игрока; пороги
/// отношений для <c>L_DIPLOMACY</c>.</summary>
public sealed record EventRules(IReadOnlyDictionary<string, RaceInfo> Races, DiplomacyRules Diplomacy)
{
    /// <summary>Расы по id без учёта регистра: в сохранениях id рас бывают строчными (<c>g000rr0000</c>).</summary>
    public IReadOnlyDictionary<string, RaceInfo> Races { get; } =
        new Dictionary<string, RaceInfo>(Races, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// События сценария по коду движка (<c>docs/scenario-events.md</c>): когда событие срабатывает и для кого. Эффекты применяет
/// партия (<c>GameSession</c>) — здесь только выбор события, кандидатов и чистые эффекты состояния событий.
/// </summary>
public static class ScenarioEvents
{
    /// <summary>Порядок проверки: <c>ORDER</c> по возрастанию, при равенстве — id (<c>0x4297b5</c>, ранг 1).</summary>
    public static IReadOnlyList<ScenarioEvent> InOrder(ScenarioEventSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return [.. set.Events.OrderBy(e => e.Order).ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Один вызов <c>FilterAndProcessEvents</c> (<c>0x4419aa</c>): первое по порядку событие, не выполненное в этой проверке
    /// (<paramref name="executed"/>), которое сработало хоть для одного кандидата, — со всеми кандидатами, для которых сработало.
    /// Кандидаты — игроки, чья раса есть в <c>VER…</c> (<see cref="ScenarioEvent.TriggerRaces"/>); при отряде-триггерере — только
    /// его владелец. У каждого кандидата — все условия (<see cref="EventConditions.Holds"/>), затем бросок R250(100) ≤ <c>CHANCE</c>.
    /// Событие попадает в <paramref name="executed"/>. В нулевой день событий нет (<c>0x4422c8</c>). Пусто — ничего не сработало.
    /// </summary>
    /// <param name="turnPlayer">Игрок, чей ход начинается (проверка начала хода, <c>0x41f4ca</c>); иначе пусто — тогда
    /// <c>L_FREQUENCY</c> ложно у всех.</param>
    /// <param name="triggererId">Отряд-триггерер (шаг, конец боя, созданный эффектом отряд); пусто — без отряда. Отряда нет на
    /// карте — проверка без него.</param>
    public static IReadOnlyList<EventFiring> Next(ScenarioEventSet set, EventProgress progress, MapState map, EventRules rules,
                                                  string turnPlayer, string triggererId, IRng rng, ISet<string> executed)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(executed);
        if (map.Turn == 0) return [];
        var triggerer = triggererId.Length > 0 ? map.Stacks.GetValueOrDefault(triggererId) : null;
        var world = new EventWorld(map, rules, set);
        foreach (var e in InOrder(set))
        {
            if (executed.Contains(e.Id) || !progress.IsEnabled(e.Id)) continue;
            var fired = new List<EventFiring>();
            foreach (var player in Candidates(e, map, rules, triggerer))
            {
                bool samePlayer = turnPlayer.Length > 0 && player.Id.Equals(turnPlayer, StringComparison.OrdinalIgnoreCase);
                if (!e.Conditions.All(c => EventConditions.Holds(c, e, world, progress, player.Id, samePlayer, triggerer))) continue;
                if (rng.Next(ChanceDie) > e.Chance) continue;
                fired.Add(new EventFiring(e, player.Id, triggerer?.Id ?? ""));
            }
            if (fired.Count == 0) continue;
            executed.Add(e.Id);
            return fired;
        }
        return [];
    }

    /// <summary>Кубик шанса: R250(100), выполнено при значении не больше <c>CHANCE</c> (<c>0x441d9f</c>: CHANCE 0 — 1 %).</summary>
    public const int ChanceDie = 100;

    /// <summary>Кандидаты события: игроки с расой в <c>VER…</c>, при триггерере — только его владелец. Порядок — по id игрока.
    /// Движок (<c>0x41cffd</c>) обходит хеш-таблицу карты объектов (<c>CMidgardScenarioMap</c>, <c>0x5eeee7</c>): корзина =
    /// <c>(ci·2²² + 0x1E·2¹⁶ + idx) mod n</c> (<c>ci</c> — номер сценария, <c>idx</c> — номер <c>PL</c>), n = 1013, при числе объектов
    /// больше ⌊0,75·n⌋ n ← 2n + 1. Это возрастание idx с циклическим сдвигом; для всех карт корпуса сдвига нет, и порядок по id
    /// совпадает (ранг 1 — устройство таблицы, ранг 2 — n по числу объектов). Сдвиг при idx ≤ 15 возможен только у сценариев с
    /// ci ∈ {61, 151, 241} (n = 1013), {37, 60} (2027), {40, 122} (4055), {138} (8111) — там порядок порта может разойтись.</summary>
    public static IEnumerable<MapPlayer> Candidates(ScenarioEvent e, MapState map, EventRules rules, MapStack? triggerer)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rules);
        var flags = e.TriggerRaces is { Count: > 0 } t ? t : e.Races;
        foreach (var p in map.Players.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (triggerer is not null && !p.Id.Equals(triggerer.Owner, StringComparison.OrdinalIgnoreCase)) continue;
            if (rules.Races.TryGetValue(p.RaceId, out var race) && RaceFlag(flags, race)) yield return p;
        }
    }

    /// <summary>
    /// Флаг расы события. Порядок флагов — порядок полей записи <c>HUMAN, DWARF, UNDEAD, HERETIC, NEUTRAL, ELF</c>
    /// (поток <c>0x5f0c93</c>), номера рас — <c>Grace.RACE_TYPE</c>: 0 Империя = human, 1 Орды = undead, 2 Легионы = heretic,
    /// 3 Кланы = dwarf, 4 нейтралы, 5 Эльфы. Список короче шести (старый файл без <c>ELF</c>) — флага нет.
    /// </summary>
    public static bool RaceFlag(IReadOnlyList<bool> flags, int raceType) => Flag(flags, LegacyRaces.EventFlag(raceType));

    /// <summary>Флаг расы события по признаку расы (<see cref="RaceInfo.EventFlag"/>).</summary>
    public static bool RaceFlag(IReadOnlyList<bool> flags, RaceInfo race)
    {
        ArgumentNullException.ThrowIfNull(race);
        return Flag(flags, race.EventFlag);
    }

    private static bool Flag(IReadOnlyList<bool> flags, int flag)
    {
        ArgumentNullException.ThrowIfNull(flags);
        return flag >= 0 && flag < flags.Count && flags[flag];
    }

    /// <summary>Затрагивает ли событие расу (<c>HUMAN…</c>-флаги) — для <c>L_WIN</c> при захвате столицы
    /// (<see cref="CapitalCapture.HasWinEvent"/>).</summary>
    public static bool RaceAllowed(ScenarioEvent e, int raceType)
    {
        ArgumentNullException.ThrowIfNull(e);
        return RaceFlag(e.Races, raceType);
    }

    /// <summary>Получатели попапа (<c>CEffectPopup</c> <c>0x43b165</c>) по <c>POPUP_SHOW</c> — строке (<c>0x5ffff2</c>): <c>TRI</c> или пусто
    /// (0) — кандидат X; <c>ALL</c> (1) — все, кроме неиграбельной расы; <c>AFF</c> (2) — игроки играбельных рас из <c>HUMAN…</c>-флагов
    /// события; любая другая строка (3) — никто. Захваченные (<c>capturedById</c>) не получают. Ранг 1.</summary>
    public static IReadOnlyList<string> PopupRecipients(PopupEffect popup, ScenarioEvent e, string candidate, MapState map,
                                                        EventRules rules)
    {
        ArgumentNullException.ThrowIfNull(popup);
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rules);
        int mode = PopupMode(popup.ShowTo);
        bool Playable(MapPlayer p) => rules.Races.TryGetValue(p.RaceId, out var r) && r.Playable;
        IEnumerable<MapPlayer> who = mode switch
        {
            1 => map.Players.Values.Where(Playable),
            2 => map.Players.Values.Where(p => Playable(p) && RaceFlag(e.Races, rules.Races[p.RaceId])),
            0 => map.Players.Values.Where(p => p.Id.Equals(candidate, StringComparison.OrdinalIgnoreCase)),
            _ => [],
        };
        return [.. who.Where(p => p.CapturedBy.Length == 0).OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Select(p => p.Id)];
    }

    /// <summary>Режим показа попапа по строке <c>POPUP_SHOW</c> (<c>0x5ffff2</c>): <c>TRI</c>/пусто — 0, <c>ALL</c> — 1, <c>AFF</c> — 2, иное — 3.</summary>
    public static int PopupMode(string showTo) => (showTo ?? "").Trim().ToUpperInvariant() switch
    {
        "" or "TRI" => 0,
        "ALL" => 1,
        "AFF" => 2,
        _ => 3,
    };

    /// <summary>Имена условий, <c>Globals/LEvCond.DBF</c> (ранг 1, прочитано из файла игры).
    /// Номер 13 в таблице отсутствует.</summary>
    public static readonly IReadOnlyDictionary<int, string> ConditionNames = new Dictionary<int, string>
    {
        [0] = "L_FREQUENCY", [1] = "L_STACK", [2] = "L_LOCATION", [3] = "L_ENTER_CITY",
        [4] = "L_OWN_CITY", [5] = "L_KILL_STACK", [6] = "L_OWN_ITEM", [7] = "L_LEADER_OWN_ITEM",
        [8] = "L_DIPLOMACY", [9] = "L_ALLIANCE", [10] = "L_LOOT_RUIN", [11] = "L_TRANSFORM_LAND",
        [12] = "L_VISIT_SITE", [14] = "L_LEADER_TO_ZONE", [15] = "L_LEADER_TO_CITY",
        [16] = "L_ITEM_TO_LOCATION", [17] = "L_STACK_EXISTS", [18] = "L_VAR_IN_RANGE",
    };

    /// <summary>Имена эффектов, <c>Globals/LEvEffct.DBF</c> (ранг 1).</summary>
    public static readonly IReadOnlyDictionary<int, string> EffectNames = new Dictionary<int, string>
    {
        [0] = "L_WIN", [1] = "L_CREATE_STACK", [2] = "L_CAST_SPELL", [3] = "L_CAST_SPELL_MAP",
        [4] = "L_CHANGE_OWNER", [5] = "L_MOVE_BESIDE", [6] = "L_BATTLE", [7] = "L_ENABLE_EVENT",
        [8] = "L_GIVE_SPELL", [9] = "L_GIVE_ITEM", [10] = "L_MOVE_STACK", [11] = "L_ALLY",
        [12] = "L_DIPLOMACY", [13] = "L_UNFOG", [14] = "L_REMOVE_MOUNTAIN", [15] = "L_REMOVE_LMARK",
        [16] = "L_CHANGE_OBJECTIVE", [17] = "L_POPUP", [18] = "L_CHANGE_ORDER",
        [19] = "L_DESTROY_ITEM", [20] = "L_REMOVE_STACK", [21] = "L_CHANGE_LANDMARK",
        [22] = "L_CHANGE_TERRAIN", [23] = "L_MODIFY_VARIABLE",
    };

    public static string ConditionName(int category) =>
        ConditionNames.TryGetValue(category, out var name) ? name : $"условие {category}";

    public static string EffectName(int category) =>
        EffectNames.TryGetValue(category, out var name) ? name : $"эффект {category}";
}
