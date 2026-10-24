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

    /// <summary>Каталог карт самого мода (карты вкладки «Карта»).</summary>
    private string ModScenarios => Path.Combine(Session.Project.Dir, "data", "scenarios");

    /// <summary>Сценарии: карты мода, затем базы (или test-pack).</summary>
    public IReadOnlyList<string> Scenarios =>
        [.. new[] { ModScenarios, scenariosDir }.Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*" + ScenarioFile.Extension).Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)];

    private ScenarioData Read(string scenario) =>
        ScenarioFile.Read(File.Exists(ScenarioFile.PathOf(ModScenarios, scenario)) ? ScenarioFile.PathOf(ModScenarios, scenario) : ScenarioFile.PathOf(scenariosDir, scenario));

    /// <summary>Потоков прогона: на один меньше ядер — окну Студии остаётся процессор.</summary>
    private static ParallelOptions Threads => new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };

    public IReadOnlyList<ArenaRow> Arena { get; private set; } = [];

    public IReadOnlyList<StandRow> Stand { get; private set; } = [];

    /// <summary>Время последнего прогона (мс).</summary>
    public long Millis { get; private set; }

    /// <summary>Отказ последнего прогона (мод не накладывается, сценарий не читается, бой не собирается); нет — null. Прогон идёт в рабочем
    /// потоке окна — отказ сессии окно ставит само, на своём потоке.</summary>
    public string? Error { get; private set; }

    private void Fail(string text) => Error = text;

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
        Error = null;
        if (Books() is not { } books)
        {
            Fail("мод не накладывается на базу — арена без правил мода невозможна (см. ошибки)");
            return Arena = [];
        }
        MapState map;
        try
        {
            map = Read(scenario).Looks.Map;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Fail($"сценарий «{scenario}» не читается — {e.Message}");
            return Arena = [];
        }
        var stacks = map.Stacks.Values.Where(s => s.Inside.Length == 0 && s.UnitIds.Count > 0 && s.LeaderAlive)
            .OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        var rows = new ConcurrentBag<(int Index, ArenaRow Row)>();
        var work = Enumerable.Range(0, Math.Min(pairs, stacks.Count / 2)).ToList();
        try
        {
            Parallel.ForEach(work, Threads, i =>
            {
                var (a, d) = (stacks[2 * i], stacks[(2 * i) + 1]);
                var (bw, bt) = Fights(map, a.Id, d.Id, books.Base, seeds);
                var (mw, mt) = Fights(map, a.Id, d.Id, books.Mod, seeds);
                rows.Add((i, new ArenaRow(a.Id, d.Id, Leader(map, a, books.Mod), Leader(map, d, books.Mod), seeds, bw, bt, mw, mt)));
            });
        }
        catch (AggregateException e)
        {
            // Мод убрал или поменял юнит отряда сценария — бой не собирается: отказ с текстом, а не падение окна.
            Fail($"арена: {e.InnerExceptions[0].Message}");
            return Arena = [];
        }
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
        Error = null;
        if (Books() is not { } books)
        {
            Fail("мод не накладывается на базу — стенд без правил мода невозможен (см. ошибки)");
            return Stand = [];
        }
        ScenarioData data;
        try
        {
            data = Read(scenario);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Fail($"сценарий «{scenario}» не читается — {e.Message}");
            return Stand = [];
        }
        var baseRows = new ConcurrentBag<BalanceRow>();
        var modRows = new ConcurrentBag<BalanceRow>();
        try
        {
            Parallel.For(0, games, Threads, i =>
            {
                foreach (var row in BalanceStand.Play(data, books.Base, 1 + (uint)i, days)) baseRows.Add(row);
                foreach (var row in BalanceStand.Play(data, books.Mod, 1 + (uint)i, days)) modRows.Add(row);
            });
        }
        catch (AggregateException e)
        {
            Fail($"стенд: {e.InnerExceptions[0].Message}");
            return Stand = [];
        }
        Millis = clock.ElapsedMilliseconds;
        var races = baseRows.Select(r => r.Race).Concat(modRows.Select(r => r.Race)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return Stand = [.. races.Select(race => new StandRow(race, baseRows.Count(r => r.Race == race),
            baseRows.Count(r => r.Race == race && r.Won), modRows.Count(r => r.Race == race && r.Won)))];
    }

    public string Line() =>
        $"balance: arena={Arena.Count} arena_changed={Arena.Count(r => r.BaseWins != r.ModWins || Math.Abs(r.BaseTurns - r.ModTurns) > 1e-9)} " +
        $"stand={Stand.Count} ms={Millis}";
}
