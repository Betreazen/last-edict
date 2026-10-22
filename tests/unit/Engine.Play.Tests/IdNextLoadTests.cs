using LastEdict.Data.Content;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Tests;

/// <summary>
/// Счётчики id после чтения карты (<see cref="MapState.IdNext"/>): движок счётчиков в файле не держит — после чтения карты
/// (<c>0x5eea31</c>) <c>0x5ee996</c> обнуляет массив <c>CMidgardScenarioMap +0x30</c> и по каждому объекту ставит счётчик его класса не
/// меньше номера + 1 (<c>docs/new-game.md</c> §6). Так же обязаны читаться все три входа порта: <c>.sg</c>
/// (<see cref="MapLoader"/>), сценарий порта (<see cref="ScenarioFile"/>) и сохранение порта (<see cref="GameSave"/>).
/// </summary>
public sealed class IdNextLoadTests
{
    private static string Mods => Path.Combine(AppContext.BaseDirectory, "mods");

    private static string ScenarioPath(string id)
    {
        string path = Path.Combine(Mods, "port-synth", "data", "scenarios", id + ScenarioFile.Extension);
        Skip.IfNot(File.Exists(path), $"нет синтетической карты порта {id} (mods/port-synth)");
        return path;
    }

    /// <summary>Классы, которые порт выдаёт (<see cref="GameSession.NewId"/> и могилы).</summary>
    internal static readonly string[] Issued = ["UN", "KC", "IM", "RD", "BG", "TB"];

    /// <summary>Сценарий порта (<c>data/scenarios</c>, слой мода): счётчики — по объектам карты, как у <c>.sg</c>. До 18.10 они не
    /// читались (<see cref="MapState.IdNext"/> в файл не пишется), и новая партия начиналась без счётчиков: номер класса, наибольший
    /// объект которого пал до первой выдачи, порт отдавал снова (правило «наибольший из живых + 1»), движок — нет.</summary>
    [SkippableTheory]
    [InlineData("port-synth-13-path3")]
    [InlineData("port-synth-15-diplo")]
    public void ScenarioFile_ReadsCounters_LikeSgLoad(string id)
    {
        var map = ScenarioFile.Read(ScenarioPath(id)).Looks.Map;

        Assert.NotEmpty(map.IdNext);
        Assert.Equal(Of(GameSession.IdNextOf(map)), Of(map.IdNext));
        Assert.True(map.IdNext["UN"] > 0 && map.IdNext["KC"] > 0);
    }

    /// <summary>Отряд с наибольшим номером пал до первого найма новой партии: следующий отряд — номер за ним, а не его номер.</summary>
    [SkippableFact]
    public void NewGame_DoesNotReuseNumberOfStackLostBeforeFirstHire()
    {
        var map = ScenarioFile.Read(ScenarioPath("port-synth-13-path3")).Looks.Map;
        string top = map.Stacks.Keys.MaxBy(k => Number(k))!;
        var stacks = map.Stacks.Where(p => p.Key != top).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var after = GameSession.With(map, stacks: stacks);

        var next = after.IdNext;
        string id = GameSession.NewId(ref next, after.Stacks.Keys, top, "KC");

        Assert.Equal(Number(top) + 1, Number(id));
    }

    /// <summary>Замена стартовых лидеров новой партии (<c>0x4220eb</c>, <c>docs/new-game.md</c> §2) берёт номера из счётчика карты, как у
    /// движка (<c>id_alloc</c> из <c>0x4223fc</c>: <c>UN003e…UN0041</c>): прежде порт брал «наибольший из живых + 1» мимо счётчика, и после
    /// гибели лидера с наибольшим номером до первого найма отдавал его номер снова.</summary>
    [SkippableFact]
    public void NewGame_StartLeaders_TakeCounter()
    {
        string data = Path.Combine(AppContext.BaseDirectory, "data");
        string path = ScenarioFile.PathOf(Path.Combine(data, "scenarios"), "Power to Corrupt");
        Skip.IfNot(File.Exists(path), "нет сценария порта");
        var content = SharedContent.Load(data);
        var book = GameBook.Load(Path.Combine(data, "rules"));
        var scenario = ScenarioFile.Read(path);
        var setup = new LastEdict.Data.Menu.NewGameSetup
        {
            Scenario = content.Catalog.Scenarios.Single(s => s.Id == "Power to Corrupt"), Difficulty = 1,
            Seats = [new LastEdict.Data.Menu.PlayerSeat(BaseRaces.Clans, "", 0, 0), new LastEdict.Data.Menu.PlayerSeat(BaseRaces.Legions, "", 0, 1)],
        };
        var map = NewGame.Start(scenario, setup, content.Catalog, book, new Random(1)).Looks.Map;

        string top = map.Units.Keys.MaxBy(Number)!;
        Assert.True(Number(top) >= scenario.Looks.Map.IdNext["UN"], "новый лидер — номер из счётчика сценария");
        Assert.Equal(Number(top) + 1, map.IdNext["UN"]);
        var units = map.Units.Where(p => p.Key != top).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var next = map.IdNext;
        Assert.Equal(Number(top) + 1, Number(GameSession.NewId(ref next, units.Keys, top, "UN")));
    }

    /// <summary>Сохранение порта: счётчики в файл не пишутся и после чтения — наибольший номер + 1 по объектам, как у движка после
    /// загрузки <c>.sg</c>: номер объекта, выданного и снятого до записи, снова свободен (<c>20261014-replay</c>: могила <c>S135TB0000</c>
    /// выдана до загрузки, seq 2585338, и снова после, seq 2646745).</summary>
    [SkippableFact]
    public void GameSave_RecountsCounters_FromObjects()
    {
        var looks = ScenarioFile.Read(ScenarioPath("port-synth-13-path3")).Looks;
        var bumped = new Dictionary<string, int>(looks.Map.IdNext, StringComparer.OrdinalIgnoreCase) { ["KC"] = looks.Map.IdNext["KC"] + 5 };
        var state = new GameState(GameInfo.FromMap("path3", looks), looks with { Map = GameSession.With(looks.Map, idNext: bumped) });

        var back = GameSave.FromJson(GameSave.ToJson(state)).Looks.Map;

        Assert.Equal(Of(looks.Map.IdNext), Of(back.IdNext));
    }

    private static int Number(string id) =>
        int.Parse(id.AsSpan(6), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);

    internal static string Of(IReadOnlyDictionary<string, int> next) =>
        string.Join(" ", Issued.Select(k => $"{k}={next.GetValueOrDefault(k):x}"));
}
