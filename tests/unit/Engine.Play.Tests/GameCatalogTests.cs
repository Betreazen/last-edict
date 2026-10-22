using D2.Data.Content;
using D2.Data.Game;
using Xunit;

namespace D2.Data.Tests;

/// <summary>Каталог меню (<c>data/game</c>): расы, лорды, сценарии. Числа пересчитаны по копии игры GOG 3.01
/// (<c>Globals/Grace.dbf</c>, <c>Glord.dbf</c>, <c>Exports/*.sg</c>).</summary>
public class GameCatalogTests
{
    private static string Dir => ImportedData.PathOf("game");

    private static GameCatalog Catalog => GameCatalog.Load(Dir);

    [SkippableFact]
    public void Races_FivePlayableWithThreeLordsEach()
    {
        var races = Catalog.Races;
        Assert.Equal([0, 1, 2, 3, 4, 5], races.Select(r => r.Type));
        Assert.Equal(["HU", "UN", "HE", "DW", "NE", "EL"], races.Select(r => r.Code));
        Assert.Equal(5, races.Count(r => r.Playable));
        Assert.False(Catalog.Race(4).Playable);
        Assert.All(races, r => Assert.Equal([0, 1, 2], r.Lords.Select(l => l.Category)));
        Assert.Equal("X000TG3001", Catalog.Race(0).Lords.Single(l => l.Category == 1).NameText);
    }

    /// <summary>33 сценария; расы — без нейтралов, и у файлов с новым заголовком совпадают с его списком рас
    /// (у «Greenskins» в заголовке 4,0,3,2,1 — нейтралы и четыре расы).</summary>
    [SkippableFact]
    public void Scenarios_HaveNamesSizesAndPlayableRaces()
    {
        var scenarios = Catalog.Scenarios;
        Assert.Equal(33, scenarios.Count);
        Assert.Equal(scenarios.Count, scenarios.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(scenarios, s =>
        {
            Assert.NotEmpty(s.Name);
            Assert.Contains(s.MapSize, new[] { 48, 72, 96 });
            Assert.NotEmpty(s.Races);
            Assert.DoesNotContain(4, s.Races);
        });
        Assert.Equal([0, 3, 2, 1], scenarios.Single(s => s.Id == "'Greenskins'").Races);
        Assert.Equal([0], scenarios.Single(s => s.Id == "Tutorial").Races);
    }

    /// <summary>Базовый каталог согласован; сценарий с расой, которой нет, и раса без лорда какого-то типа —
    /// проблемы, и <see cref="GameContent.Load"/> такой каталог не примет.</summary>
    [SkippableFact]
    public void Problems_EmptyForBase_FoundForBrokenCatalog()
    {
        Assert.Empty(Catalog.Problems());
        var broken = Catalog with
        {
            Scenarios = [new ScenarioInfo { Id = "X", File = "x.sg", Name = "X", Races = [7] }],
            Races = [.. Catalog.Races.Select(r => r.Type == 0 ? r with { Lords = r.Lords.Take(2).ToList() } : r)],
        };
        var problems = broken.Problems();
        Assert.Contains(problems, p => p.Contains("нет расы 7"));
        Assert.Contains(problems, p => p.Contains("раса 0"));
    }

    [Fact]
    public void Merge_ReplacesById_AndAppendsNew()
    {
        var baseSet = new GameCatalog
        {
            Scenarios = [new ScenarioInfo { Id = "A", File = "a.sg", Name = "A" }, new ScenarioInfo { Id = "B", File = "b.sg", Name = "B" }],
        };
        var mod = new GameCatalog
        {
            Scenarios = [new ScenarioInfo { Id = "b", File = "b2.sg", Name = "B2" }, new ScenarioInfo { Id = "C", File = "c.sg", Name = "C" }],
        };
        var merged = GameCatalog.Merge(baseSet, mod);
        Assert.Equal(["A", "B2", "C"], merged.Scenarios.Select(s => s.Name));
    }

    [SkippableFact]
    public void GameContent_LoadsCatalog()
    {
        var content = SharedContent.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        Assert.Equal(33, content.Catalog.Scenarios.Count);
        Assert.Equal(6, content.Catalog.Races.Count);
    }

    /// <summary>Ролики эпилога (<c>0x668d69</c>): сценарии <c>Exports</c> — кампания <c>C000CC0001</c>, её последняя запись
    /// (<c>DEFWIN</c>/<c>DEFLOSE</c>); глава саги — своя запись: у эльфов <c>ELFWIN</c>, у последней главы — <c>CMP3EL</c>.</summary>
    [SkippableFact]
    public void Endings_QuestsTakeLastRecord_SagaChaptersTheirOwn()
    {
        var catalog = Catalog;
        Assert.All(catalog.Scenarios, s => Assert.Equal(("DEFWIN", "DEFLOSE", true), (s.WinVideo, s.LoseVideo, s.HasEnding)));
        var elves = catalog.Campaigns.Single(c => c.Id == "C000CC0006").Chapters;
        Assert.All(elves.SkipLast(1), c => Assert.Equal("ELFWIN", c.Scenario.WinVideo));
        Assert.Equal(("CMP3EL", "DEFLOSE"), (elves[^1].Scenario.WinVideo, elves[^1].Scenario.LoseVideo));
        Assert.Equal("CMP2HU", catalog.Campaigns.Single(c => c.Id == "C000CC0012").Chapters[^1].Scenario.WinVideo);
        // Финальный ролик саги — только C000CC0002–0005 (0x404f5b); у эльфов и «Стражей Света» — сразу меню.
        Assert.Equal(["C000CC0002", "C000CC0003", "C000CC0004", "C000CC0005"],
                     catalog.Campaigns.Where(c => c.Conclusion.Length > 0).Select(c => c.Id).Order());
    }

    /// <summary>Музыка брифа (<c>CBriefingInterf</c> <c>0x48091e</c>): у саги эльфов (<c>PLAY_RACE</c> = <c>L_ELF</c>, <c>[0x8372a8]</c>) — озвучка
    /// <c>"briefel"</c> + номер главы в списке саги (<c>0x480a71</c>), звук без картинки описи роликов; у прочих саг и квестов — общая
    /// <c>GENBRIEF</c> (null). Реестр З12.</summary>
    [SkippableFact]
    public void BriefingMusic_ElfSagaHasChapterVoice_OthersGeneral()
    {
        var catalog = Catalog;
        var elves = catalog.Campaigns.Single(c => c.Id == "C000CC0006");
        Assert.Equal(Enumerable.Range(1, 8).Select(n => $"BRIEFEL{n}"), elves.Chapters.Select(c => c.BriefingMusic));
        Assert.Equal("BRIEFEL3", catalog.BriefingMusic("S137SC0000"));
        Assert.All(catalog.Campaigns.Where(c => c.Race != 5).SelectMany(c => c.Chapters), c => Assert.Null(c.BriefingMusic));
        Assert.All(catalog.Scenarios, s => Assert.Null(catalog.BriefingMusic(s.Id)));
        Assert.Null(catalog.BriefingMusic(null));
        var videos = SharedContent.Load(Path.Combine(AppContext.BaseDirectory, "data")).VideosByName;
        Assert.All(elves.Chapters, c => Assert.False(videos[c.BriefingMusic!].HasVideo));
    }

    /// <summary>Число картинок гор (<c>Globals/GTileDBI.dbf</c>, <c>GROUND</c> = M) — для перекладки гор: у всех местностей размеры 1, 2,
    /// 3, 5 — 11, 10, 9, 8 картинок; <c>data/map/mountains.json</c> — ровно результат импорта.</summary>
    [SkippableFact]
    public void MountainImages_FromTileTable()
    {
        Skip.IfNot(Directory.Exists(GamePaths.Globals), $"нет копии игры: {GamePaths.GameDir}");
        var art = D2.Data.Map.MapArtData.Import(GamePaths.GameDir);
        foreach (string terrain in new[] { "HU", "DW", "HE", "UN", "NE", "EL" })
            Assert.Equal([11, 10, 9, 8], new[] { 1, 2, 3, 5 }.Select(size => art.MountainImageCount(terrain, size)));
        string file = Path.Combine(AppContext.BaseDirectory, "data", "map", D2.Data.Map.MapArtData.MountainsFile);
        Assert.Equal(D2.Data.Map.MapArtData.ToJson(art.Mountains), File.ReadAllText(file).ReplaceLineEndings("\n"));
    }

    /// <summary>Файлы в git — ровно результат импорта: пересборка из копии игры даёт тот же текст.</summary>
    [SkippableFact]
    public void Catalog_Json_IsUpToDate()
    {
        string globals = GamePaths.Globals, exports = GamePaths.Exports;
        Skip.IfNot(Directory.Exists(globals) && Directory.Exists(exports), $"нет копии игры: {GamePaths.GameDir}");
        var races = GameImport.Races(globals);
        Assert.Equal(GameCatalog.ToJson(races), File.ReadAllText(Path.Combine(Dir, "races.json")).ReplaceLineEndings("\n"));
        Assert.Equal(GameCatalog.ToJson(GameImport.Scenarios(exports, races, Path.Combine(GamePaths.GameDir, "Campaign"))),
                     File.ReadAllText(Path.Combine(Dir, "scenarios.json")).ReplaceLineEndings("\n"));
        Assert.Equal(GameCatalog.ToJson(GameImport.Campaigns(GamePaths.GameDir, races)),
                     File.ReadAllText(Path.Combine(Dir, "campaigns.json")).ReplaceLineEndings("\n"));
    }
}
