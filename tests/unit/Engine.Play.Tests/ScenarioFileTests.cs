using D2.Data.Play;
using D2.Rules.Map;

namespace D2.Data.Tests;

/// <summary>Сценарий в данных порта (<see cref="ScenarioFile"/>): запись и чтение ничего не теряют.</summary>
public class ScenarioFileTests
{
    /// <summary>Все сценарии игры: <c>.sg</c> → JSON → модель → JSON даёт тот же текст; события и области на месте.</summary>
    [SkippableFact]
    public void EveryScenario_RoundTrips()
    {
        Skip.IfNot(Directory.Exists(GamePaths.Exports), "нет копии игры");
        int files = 0, events = 0, templates = 0;
        foreach (string sg in Directory.GetFiles(GamePaths.Exports, "*.sg"))
        {
            var scenario = ScenarioFile.FromSg(sg);
            string json = ScenarioFile.ToJson(scenario);
            var back = ScenarioFile.FromJson(json);
            Assert.Equal(json, ScenarioFile.ToJson(back));
            Assert.Equal(scenario.Id, back.Id);
            Assert.Equal(scenario.Events.Events.Count, back.Events.Events.Count);
            Assert.Equal(scenario.Events.Zones.Keys.Order(), back.Events.Zones.Keys.Order());
            Assert.Equal(scenario.Events.Templates.Count, back.Events.Templates.Count);
            templates += back.Events.Templates.Count;
            if (scenario.Id == "'A Return To Simpler Times'")
            {
                // S001TM0000: гоблин-лидер и два юнита, клетки POS_0…5 = 2,2,−1,0,1,−1 (большой юнит в колонке 0–1).
                var t = back.Events.Templates["S001TM0000"];
                Assert.Equal(("G000UU0098", 3, 10), (t.LeaderType, t.Units.Count, t.Order));
                Assert.Equal([2, 2, -1, 0, 1, -1], t.Slots);
            }
            files++;
            events += back.Events.Events.Count;
        }
        Assert.Equal(33, files);
        Assert.Equal(1291, events);
        Assert.True(templates > 100, $"шаблонов отрядов {templates}");
    }

    /// <summary>Импорт повторяем и свеж: <c>data/scenarios</c> — ровно то, что <c>tools/ScenarioImport</c> даёт из копии игры.</summary>
    [SkippableFact]
    public void Imported_IsUpToDate()
    {
        Skip.IfNot(Directory.Exists(GamePaths.Exports), "нет копии игры");
        string dir = Path.Combine(AppContext.BaseDirectory, "data", "scenarios");
        var sgs = Directory.GetFiles(GamePaths.Exports, "*.sg");
        var sagas = Directory.Exists(GamePaths.Scens) ? D2.Data.Sg.ScensTables.Load(GamePaths.Scens) : null;
        Assert.Equal(sgs.Length + (sagas?.Scenarios.Count ?? 0), Directory.GetFiles(dir, "*" + ScenarioFile.Extension).Length);
        var all = sgs.Select(ScenarioFile.FromSg).Concat((sagas?.Scenarios ?? []).Select(s =>
            ScenarioFile.FromSg(s, D2.Data.Sg.SgFile.Load(D2.Data.Sg.SagaSg.Build(sagas!, s)))));
        foreach (var scenario in all)
            Assert.True(ScenarioFile.ToJson(scenario) == File.ReadAllText(ScenarioFile.PathOf(dir, scenario.Id)),
                        $"{scenario.Id}.json устарел — перезапустите tools/ScenarioImport");
    }

    /// <summary>Версия 2: имена и описания объектов карты из редактора — город (<c>NAME_TXT</c>), площадка (<c>TXT_TITLE</c>,
    /// <c>TXT_DESC</c>), руина (<c>TITLE</c>) — для окон; файл версии 1 читается с пустыми именами.</summary>
    [SkippableFact]
    public void Sites_HaveNamesAndDescriptions()
    {
        string path = ScenarioFile.PathOf(Path.Combine(AppContext.BaseDirectory, "data", "scenarios"), "Power to Corrupt");
        Skip.IfNot(File.Exists(path), "нет сценария порта");
        var sites = ScenarioFile.Read(path).Looks.Map.Sites.ToDictionary(s => s.Id);
        Assert.Equal(("Draco Training Camp", "Every famous Dragon Warrior has trained here!"),
                     (sites["S073SI0000"].Name, sites["S073SI0000"].Description));
        Assert.Equal("Satoria", sites["S073FT0001"].Name);
        Assert.Equal("Aragonia", sites["S073FT0004"].Name);
        Assert.Equal("Crypt", sites["S073RU0002"].Name);

        string v1 = File.ReadAllText(path).Replace($"\"Version\":{ScenarioFile.Version}", "\"Version\":1", StringComparison.Ordinal);
        Assert.Equal("Satoria", ScenarioFile.FromJson(v1).Looks.Map.Sites.Single(s => s.Id == "S073FT0001").Name);
    }

    [Fact]
    public void Rejects_ForeignJsonAndNewerVersion()
    {
        Assert.Throws<InvalidDataException>(() => ScenarioFile.FromJson("{\"Format\":\"другое\",\"Version\":1}"));
        Assert.Throws<InvalidDataException>(() => ScenarioFile.FromJson($"{{\"Format\":\"{ScenarioFile.Format}\",\"Version\":99}}"));
        Assert.Throws<InvalidDataException>(() => ScenarioFile.FromJson("не json"));
    }

    /// <summary>Вид условия и эффекта пишется полем <c>Kind</c> и читается обратно тем же видом.</summary>
    [SkippableFact]
    public void Events_KeepTheirKinds()
    {
        string tutorial = Path.Combine(GamePaths.Exports, "Tutorial.sg");
        Skip.IfNot(File.Exists(tutorial), "нет копии игры");
        var e = new ScenarioEvent("S001EV0000", true, true, 100, 0, [true, false, false, false, false, false],
            [new FrequencyCondition(1), new OwnCityCondition("S001FT0001"), new UnsupportedCondition(1, "L_STACK")],
            [new PopupEffect("текст"), new WinEffect(true, "S001PL0001"), new UnfogEffect("S001LO0000", true, 2, [new UnfogEntry("S001EV0001", "S001PL0001")])],
            "имя", [true, true, false, false, false, false]);
        var set = new ScenarioEventSet([e], new Dictionary<string, EventZone> { ["S001LO0000"] = new("S001LO0000", 3, 4, 1) },
                                       new Dictionary<int, int> { [2] = 7 });
        var looks = ScenarioFile.FromJson(ScenarioFile.ToJson(new ScenarioData("x", D2.Data.Map.MapLooksLoader.Load(tutorial), set))).Events;
        var got = Assert.Single(looks.Events);
        Assert.Equal(e.Conditions, got.Conditions);
        Assert.IsType<UnfogEffect>(got.Effects[2]);
        Assert.Equal("S001EV0001", ((UnfogEffect)got.Effects[2]).Entries[0].EventId);
        Assert.Equal(e.Effects.Take(2), got.Effects.Take(2));
        Assert.Equal(e.TriggerRaces, got.TriggerRaces);
        Assert.Equal(7, looks.Variables[2]);
        Assert.Equal(1, looks.Zones["s001lo0000"].Radius);
    }
}
