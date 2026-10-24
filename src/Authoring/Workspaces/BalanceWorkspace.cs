using System.Collections.Concurrent;
using System.Diagnostics;
using LastEdict.Data.Battle;
using LastEdict.Data.Content;
using LastEdict.Data.Play;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;

namespace LastEdict.Authoring.Workspaces;

/// <summary>Пара отрядов арены и итог её боёв без мода и с модом: победы нападающего, средняя длина боя (ходов).</summary>
public sealed record ArenaRow(string Attacker, string Defender, string AttackerName, string DefenderName, int Fights,
                              int BaseWins, double BaseTurns, int ModWins, double ModTurns)
{
    public double BaseShare => Fights == 0 ? 0 : (double)BaseWins / Fights;

    public double ModShare => Fights == 0 ? 0 : (double)ModWins / Fights;
}

/// <summary>Раса на стенде: мест в партиях и побед без мода и с модом.</summary>
public sealed record StandRow(string Race, int Seats, int BaseWins, int ModWins)
{
    public double BaseShare => Seats == 0 ? 0 : (double)BaseWins / Seats;

    public double ModShare => Seats == 0 ? 0 : (double)ModWins / Seats;
}

/// <summary>
/// Рабочее пространство Студии «Баланс / Арена» (Э6, цикл «изменил → прогнал → сравнил» старой утилиты, roadmap §3.4) — без запуска игры:
/// <b>арена</b> — бои пар отрядов сценария на <see cref="BattleSim"/> (автобой, как снимки «порт против порта»), каждая пара с одними и теми
/// же зёрнами на правилах без мода и с модом (раздельные генераторы делают A/B честным); <b>стенд</b> — партии ИИ без людей
/// (<see cref="BalanceStand"/>, как <c>tools/BalanceStand</c>) — доли побед рас без мода и с модом. Правила «с модом» — текущий документ
/// (и несохранённый).
/// </summary>
public sealed class BalanceWorkspace(StudioSession session, string scenariosDir)
{
    public StudioSession Session { get; } = session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>Сценарии (каталог базы или test-pack).</summary>
    public IReadOnlyList<string> Scenarios =>
        Directory.Exists(scenariosDir)
            ? [.. Directory.GetFiles(scenariosDir, "*" + ScenarioFile.Extension).Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)]
            : [];

    public IReadOnlyList<ArenaRow> Arena { get; private set; } = [];

    public IReadOnlyList<StandRow> Stand { get; private set; } = [];

    /// <summary>Время последнего прогона (мс).</summary>
    public long Millis { get; private set; }

    private (GameBook Base, GameBook Mod)? Books()
    {
        if (Session.Report.Content is not { } mod) return null;
        return (GameBook.Load(Session.BaseContent), GameBook.Load(mod));
    }

    /// <summary>Бои первых <paramref name="pairs"/> пар отрядов сценария, у каждой — <paramref name="seeds"/> зёрен, без мода и с модом.
    /// Мод не грузится (ошибки проверки) — пусто, причина — отказ сессии.</summary>
    public IReadOnlyList<ArenaRow> RunArena(string scenario, int pairs = 8, int seeds = 20)
    {
        var clock = Stopwatch.StartNew();
        if (Books() is not { } books)
        {
            Session.Refuse("мод не накладывается на базу — арена без правил мода невозможна (см. ошибки)");
            return Arena = [];
        }
        var map = ScenarioFile.Read(ScenarioFile.PathOf(scenariosDir, scenario)).Looks.Map;
        var stacks = map.Stacks.Values.Where(s => s.Inside.Length == 0 && s.UnitIds.Count > 0 && s.LeaderAlive)
            .OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        var rows = new ConcurrentBag<(int Index, ArenaRow Row)>();
        var work = Enumerable.Range(0, Math.Min(pairs, stacks.Count / 2)).ToList();
        Parallel.ForEach(work, i =>
        {
            var (a, d) = (stacks[2 * i], stacks[(2 * i) + 1]);
            var (bw, bt) = Fights(map, a.Id, d.Id, books.Base, seeds);
            var (mw, mt) = Fights(map, a.Id, d.Id, books.Mod, seeds);
            rows.Add((i, new ArenaRow(a.Id, d.Id, Leader(map, a, books.Mod), Leader(map, d, books.Mod), seeds, bw, bt, mw, mt)));
        });
        Millis = clock.ElapsedMilliseconds;
        return Arena = [.. rows.OrderBy(r => r.Index).Select(r => r.Row)];
    }

    private string Leader(MapState map, MapStack stack, GameBook book) =>
        map.Units.TryGetValue(stack.LeaderId, out var unit) ? Session.NameOf(unit.TypeId) : stack.Id;

    /// <summary>Победы нападающего и средняя длина боя за <paramref name="seeds"/> боёв с зёрнами 1…N.</summary>
    private static (int Wins, double Turns) Fights(MapState map, string attacker, string defender, GameBook book, int seeds)
    {
        int wins = 0;
        long turns = 0;
        for (int seed = 1; seed <= seeds; seed++)
        {
            var state = MapBattle.Build(map, attacker, defender, book, difficulty: 1, attackerMove: 0);
            var battle = new BattleSession(state, new BattleSim(state, new EngineRng(seed), book.Rules.Constants), book.Rules.Constants) { Auto = true };
            battle.PlayOut();
            if (battle.Winner is { } w && w.Equals(attacker, StringComparison.OrdinalIgnoreCase)) wins++;
            turns += battle.Log.Count;
        }
        return (wins, (double)turns / seeds);
    }

    /// <summary>Партии ИИ сценария (<paramref name="games"/> зёрен, до <paramref name="days"/> дней) без мода и с модом: доли побед рас.</summary>
    public IReadOnlyList<StandRow> RunStand(string scenario, int games = 24, int days = 30)
    {
        var clock = Stopwatch.StartNew();
        if (Books() is not { } books)
        {
            Session.Refuse("мод не накладывается на базу — стенд без правил мода невозможен (см. ошибки)");
            return Stand = [];
        }
        var data = ScenarioFile.Read(ScenarioFile.PathOf(scenariosDir, scenario));
        var baseRows = new ConcurrentBag<BalanceRow>();
        var modRows = new ConcurrentBag<BalanceRow>();
        Parallel.For(0, games, i =>
        {
            foreach (var row in BalanceStand.Play(data, books.Base, 1 + (uint)i, days)) baseRows.Add(row);
            foreach (var row in BalanceStand.Play(data, books.Mod, 1 + (uint)i, days)) modRows.Add(row);
        });
        Millis = clock.ElapsedMilliseconds;
        var races = baseRows.Select(r => r.Race).Concat(modRows.Select(r => r.Race)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return Stand = [.. races.Select(race => new StandRow(race, baseRows.Count(r => r.Race == race),
            baseRows.Count(r => r.Race == race && r.Won), modRows.Count(r => r.Race == race && r.Won)))];
    }

    public string Line() =>
        $"balance: arena={Arena.Count} arena_changed={Arena.Count(r => r.BaseWins != r.ModWins || Math.Abs(r.BaseTurns - r.ModTurns) > 1e-9)} " +
        $"stand={Stand.Count} ms={Millis}";
}
