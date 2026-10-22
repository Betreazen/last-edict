namespace LastEdict.Rules.Map;

/// <summary>
/// Итог взятия столицы. Одна запись на оба исхода: <see cref="Victory"/> — партия кончилась
/// (<c>CEffectWin</c>), иначе игрок погиб (<c>CEffectCaptureCapital</c>). Отказ
/// (<see cref="Refused"/> непусто) значит, что <c>apply</c> вышел, ничего не сделав.
/// </summary>
/// <param name="Victory">Вместо захвата сработала победа: ни казны, ни <c>CAPT_BY</c>, ни сноса
/// жезлов, ни стирания гарнизона.</param>
/// <param name="Refused">Почему захват не применился, словами; <c>null</c> — применился.</param>
/// <param name="Captor">Казна захватчика после захвата.</param>
/// <param name="Loser">Казна погибшего после захвата (ноль).</param>
/// <param name="CapturedBy">Чем стало поле <c>CAPT_BY</c> погибшего; пусто — не менялось.</param>
/// <param name="Capital">Столица после захвата: гарнизон стёрт, ВЛАДЕЛЕЦ ТОТ ЖЕ.</param>
/// <param name="RodsRazed">Id снесённых жезлов погибшего.</param>
/// <param name="Berserk">Id отрядов погибшего, которым поставлен приказ
/// <see cref="CapitalCapture.BerserkOrder"/>.</param>
public sealed record CapitalCaptureOutcome(
    bool Victory,
    string? Refused,
    Bank Captor,
    Bank Loser,
    string CapturedBy,
    MapSite Capital,
    IReadOnlyList<string> RodsRazed,
    IReadOnlyList<string> Berserk);

/// <summary>
/// Гибель игрока и ветка победы — цепочка <c>0x440EE0</c> (план Ф7 §1.9, разбор
/// <c>capture-treasury.md</c>, задача 14). Всё ранг 1, кроме оговорённого.
///
/// <para><b>Гибель игрока — это взятие его СТОЛИЦЫ, и только оно.</b> Генератор эффектов
/// <c>0x440EE0</c> на <c>0x440F20</c> сравнивает категорию крепости с <c>LFortCategory capital</c>
/// (<c>[0x837A78]</c>): деревня даёт <c>CEffectCapture</c> (<see cref="Fortifications.Capture"/>),
/// столица — <c>CEffectWin</c> либо <c>CEffectCaptureCapital</c>, и эти два взаимоисключающие.
/// Условие применения (<c>CEffectCaptureCapital::apply 0x43496A</c>) — четыре проверки:
/// отряд-захватчик есть, объект — столица, внутри неё нет отряда (<c>0x434A11</c>), живых в
/// гарнизоне ноль (<c>0x434A22</c>). Ни других городов, ни жезлов, ни казны <b>не проверяется</b>
/// (искал в генераторе и в <c>apply</c> целиком).</para>
///
/// <para><b>Владелец <c>CCapital</c> не меняется.</b> В <c>apply</c> нет смены владельца (у города
/// это <c>0x5E724A</c>) — поэтому <see cref="Sites.AfterVisit"/> для столицы стирает гарнизон и
/// больше ничего. Сверено сохранениями: <c>FT0001.OWNER</c> в <c>f7-m22-capture3</c> и
/// <c>FT0000.OWNER</c> второй большой партии после захвата те же.</para>
/// </summary>
public static class CapitalCapture
{
    /// <summary>Приказ, который получают все отряды погибшего — <c>L_BEZERK</c>
    /// (<c>LOrder.DBF</c> 10, глобал <c>0x837848</c>, визитор <c>CVisitorChangeStackOrder</c>
    /// <c>0x5E811A</c> из <c>0x435327</c>). Ранг 1 по корпусу: четыре отряда Империи
    /// <c>S073PL0009</c> между <c>t21-023</c> и <c>t21-024</c> сменили <c>ORDER</c> 1 на 10.</summary>
    public const int BerserkOrder = 10;

    /// <summary>Категория эффекта сценария <c>L_WIN</c> (<c>LEvEffct.DBF</c> 0, глобал
    /// <c>LEventEffectCategory win 0x837C08</c>): если у захватчика есть такое событие,
    /// автоматической победы нет (<c>0x441090</c>). Ранг 1 — значение таблицы.</summary>
    public const int WinEffectCategory = 0;

    /// <summary>Кубик шанса получить заклинания погибшего — <c>rng(100)</c> (<c>push 0x64</c> на
    /// <c>0x434B3B</c>). Порог — <c>GVars.GAIN_SPELL</c> = 50 («Chance to get spells with capture of
    /// a capital»). Ранг 2: сам кубик и сравнение в коде видны, имя порога взято из
    /// <c>reference/D2ModdingToolset globalvariables.h:87</c>, наблюдений в корпусе нет ни одного.</summary>
    public const int SpellChanceDie = 100;

    /// <summary>
    /// Выпал ли шанс получить заклинания погибшего: <c>rng(100) &lt; GAIN_SPELL</c>.
    /// <para>ponytail: порог приходит параметром, потому что <c>GVars.GAIN_SPELL</c> в порт не
    /// загружается (<see cref="MapVariables"/> — два поля), а потребителя у величины пока нет:
    /// заклинания погибшего никуда не переносятся, в корпусе этого не наблюдалось ни разу.
    /// Потолок: как только появится перенос <c>KnownSpells</c>, поле читается в
    /// <c>EconomyLoader.LoadVars</c> одной строкой, а какие именно заклинания достаются —
    /// открытый вопрос (<c>0x434B3B</c> разобран только до кубика).</para>
    /// </summary>
    public static bool GainsSpells(int roll, int gainSpellPercent) => roll < gainSpellPercent;

    /// <summary>
    /// Пала ли столица: объект — столица, внутри нет отряда (<c>City.STACK</c> пуст) и в гарнизоне
    /// нет живых. Это и есть условие гибели игрока (<c>0x4349D6…0x434A22</c>): мёртвые тела в
    /// гарнизоне остаются, живых быть не должно.
    /// </summary>
    public static bool CapitalFalls(MapSite capital, IReadOnlyDictionary<string, MapUnit> units)
    {
        ArgumentNullException.ThrowIfNull(capital);
        ArgumentNullException.ThrowIfNull(units);
        return capital.Kind == SiteKind.Capital && capital.StackId.Length == 0
               && LiveGuards(capital, units) == 0;
    }

    /// <summary>Живых в гарнизоне укрепления. Юнит, которого нет в <paramref name="units"/>,
    /// считается живым: молчаливый ноль здесь означал бы «столица пала», то есть отдал бы
    /// гибель игрока неполноте данных.</summary>
    public static int LiveGuards(MapSite capital, IReadOnlyDictionary<string, MapUnit> units)
    {
        ArgumentNullException.ThrowIfNull(capital);
        ArgumentNullException.ThrowIfNull(units);
        return capital.Guards.Count(id => !units.TryGetValue(id, out var u) || u.Hp > 0);
    }

    /// <summary>
    /// Сколько у захватчика живых соперников (<c>0x44120D</c>): игроки играбельной расы, кроме
    /// самого захватчика и его команды (<see cref="Teams"/>, Э4), у которых <c>CAPT_BY</c> пуст. Погибающий в этот момент ещё в счёте —
    /// проверка идёт до записи его <c>CAPT_BY</c>.
    /// </summary>
    public static int LiveOpponents(MapState map, string captorId, EconomyRules rules)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rules);
        return map.Players.Values.Count(
            p => !Teams.SameTeam(map, p.Id, captorId) && p.CapturedBy.Length == 0
                 && rules.Races.TryGetValue(p.RaceId, out var race) && race.Playable);
    }

    /// <summary>
    /// Есть ли у расы захватчика сценарное событие с эффектом <c>L_WIN</c> (<c>0x441090</c>): если
    /// есть, автоматической победы не бывает — партию заканчивает событие.
    /// <para>ponytail: фильтр — только флаг расы (<see cref="ScenarioEvents.RaceAllowed"/>), как в
    /// разборе; включено ли событие и не отработало ли оно однажды, здесь не смотрится (ранг 2 на
    /// состав фильтра, <c>0x441090</c> разобран по структуре). Потолок виден: у карт корпуса
    /// событий с <c>L_WIN</c> нет вовсе, поэтому различить варианты фильтра корпусом нельзя.</para>
    /// </summary>
    public static bool HasWinEvent(ScenarioEventSet events, int raceType)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events.Events.Any(e => ScenarioEvents.RaceAllowed(e, raceType)
                                      && e.Effects.Any(x => x.Category == WinEffectCategory));
    }

    /// <summary>
    /// Взятие столицы целиком: выбор между победой и гибелью игрока и всё, что делает
    /// <c>CEffectCaptureCapital::apply</c>. Состояние не меняется — что применить, лежит в ответе.
    /// </summary>
    /// <param name="capital">Столица в состоянии ПОСЛЕ боя: защитники уже пали
    /// (<see cref="CapitalFalls"/>).</param>
    /// <param name="captorId">Игрок, чей отряд взял столицу.</param>
    /// <param name="events">События сценария для проверки <c>L_WIN</c>; <c>null</c> — событий нет.</param>
    /// <param name="multiplayer">Партия сетевая (<c>CMidgardData.multiplayerGame</c>,
    /// <c>0x4025E7</c>): в <c>.sg</c> этого поля нет, признак приходит извне.</param>
    public static CapitalCaptureOutcome Apply(MapState map, MapSite capital, string captorId,
                                              EconomyRules rules, ScenarioEventSet? events = null,
                                              bool multiplayer = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(capital);
        ArgumentNullException.ThrowIfNull(rules);
        if (!map.Players.TryGetValue(capital.Owner, out var loser))
            return Refuse(capital, $"владельца столицы {capital.Owner} нет среди игроков карты");
        if (!map.Players.TryGetValue(captorId, out var captor))
            return Refuse(capital, $"захватчика {captorId} нет среди игроков карты");
        if (!CapitalFalls(capital, map.Units))
            return Refuse(capital, capital.Kind != SiteKind.Capital
                ? $"объект {capital.Id} — не столица ({capital.Kind}): это захват города"
                : $"столица {capital.Id} не пала: внутри отряд {capital.StackId} " +
                  $"или живых в гарнизоне {LiveGuards(capital, map.Units)}");

        if (RaceTypeOf(captor, rules) is not { } raceType)
            return Refuse(capital, $"расы {captor.RaceId} захватчика {captorId} нет в Grace.dbf");

        if (Treasury.CaptureEndsInVictory(LiveOpponents(map, captorId, rules),
                                          HasWinEvent(events ?? ScenarioEventSet.Empty, raceType),
                                          loser.IsHuman, multiplayer))
            return new CapitalCaptureOutcome(true, null, captor.Bank, loser.Bank, "", capital, [], []);

        var (winnerBank, loserBank) = Treasury.CapitalCapture(captor.Bank, loser.Bank, victory: false);
        return new CapitalCaptureOutcome(
            Victory: false, Refused: null, Captor: winnerBank, Loser: loserBank,
            // Сеттер 0x5E4F91 пишет capturedById только в пустое поле: повторный захват уже
            // погибшего игрока прежнюю метку не переписывает.
            CapturedBy: loser.CapturedBy.Length == 0 ? captor.Id : loser.CapturedBy,
            Capital: capital with { Guards = [] },
            RodsRazed: [.. map.Rods.Values.Where(r => Same(r.Owner, loser.Id)).Select(r => r.Id)],
            Berserk: [.. map.Stacks.Values.Where(s => Same(s.Owner, loser.Id)).Select(s => s.Id)]);
    }

    private static CapitalCaptureOutcome Refuse(MapSite capital, string why) =>
        new(false, why, Bank.Zero, Bank.Zero, "", capital, [], []);

    /// <summary>Колонка <c>Grace.RACE_TYPE</c> игрока; <c>null</c> — расы нет в справочнике
    /// (тогда захват отвергается с причиной, а не падает внутри проверки событий).</summary>
    private static int? RaceTypeOf(MapPlayer player, EconomyRules rules) =>
        rules.Races.TryGetValue(player.RaceId, out var race) ? race.RaceType : null;

    private static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
}
