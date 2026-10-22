using System.Text.Json.Nodes;
using LastEdict.Data.Content;
using LastEdict.Data.Play;

namespace LastEdict.Data.Tests;

/// <summary>Ключ правил — id содержимого (Э4): сценарии, сохранения и герои прежних версий переводятся при чтении (<see cref="LegacyRefs"/>,
/// Р18 — перенос между версиями Last Edict).</summary>
public class LegacyRefsTests
{
    [Fact]
    public void ToContentIds_ValuesKeysAndCompositeKeys_OtherStringsUntouched()
    {
        var node = JsonNode.Parse("""
            {
              "Units": { "S143UN0001": { "Id": "S143UN0001", "TypeId": "G000UU0019", "Modifiers": ["g000um9031", "G000000000"] } },
              "Players": { "S143PL0001": { "RaceId": "G000RR0004", "Buildings": ["G000BB0023"], "KnownSpells": ["g000ss0001"] } },
              "ItemTypes": { "S143IM0001": "G000IG0001" },
              "Memo": { "4|g000bb0023": 0, "8|S143PL0002": 6 },
              "G000UU0019": 1,
              "Subrace": "g000rr0006"
            }
            """);

        LegacyRefs.ToContentIds(node, LegacyIds.Base);

        var unit = node!["Units"]!["S143UN0001"]!;
        Assert.Equal(("S143UN0001", "base:unit/pegasus-knight"), (unit["Id"]!.GetValue<string>(), unit["TypeId"]!.GetValue<string>()));
        Assert.Equal("G000000000", unit["Modifiers"]![1]!.GetValue<string>());   // пустая ссылка оригинала — не сущность
        var player = node["Players"]!["S143PL0001"]!;
        Assert.Equal(("base:race/neutral", "base:building/temple", "base:spell/air-ward"),
                     (player["RaceId"]!.GetValue<string>(), player["Buildings"]![0]!.GetValue<string>(), player["KnownSpells"]![0]!.GetValue<string>()));
        Assert.Equal(LegacyIds.Base.IdOf("g000ig0001"), node["ItemTypes"]!["S143IM0001"]!.GetValue<string>());
        Assert.Equal(["4|base:building/temple", "8|S143PL0002"], node["Memo"]!.AsObject().Select(p => p.Key));
        Assert.NotNull(node["base:unit/pegasus-knight"]);
        Assert.Equal("g000rr0006", node["Subrace"]!.GetValue<string>());          // подраса нейтралов вне содержимого
    }

    [Fact]
    public void LegacyIds_ArtAndOrder()
    {
        var ids = LegacyIds.Base;
        Assert.Equal("g000uu0019", ids.ArtOf("base:unit/pegasus-knight"));
        Assert.Equal("g000uu9999", ids.ArtOf("base:unit/g000uu9999"));            // сущность мода оригинала, которой нет в таблице
        Assert.Equal("mymod:unit/hero", ids.ArtOf("mymod:unit/hero"));
        // Порядок оригинала: по id оригинала, новые сущности модов — после.
        Assert.Equal(["base:building/temple", "base:spell/air-ward", "mymod:spell/a"],   // g000bb0023 < g000ss0001
                     new[] { "mymod:spell/a", "base:building/temple", "base:spell/air-ward" }.Order(ids));
    }

    /// <summary>Сценарии импорта прежней версии (5, id оригинала) читаются с id содержимого: каждая ссылка юнита, игрока и предмета ведёт к
    /// сущности правил.</summary>
    [SkippableFact]
    public void ImportedScenarios_ResolveInRules()
    {
        ImportedData.Require("scenarios", "content/base/units.json");
        var book = GameBook.Load(ImportedData.Content);
        int units = 0;
        foreach (string path in Directory.GetFiles(ImportedData.PathOf("scenarios"), "*" + ScenarioFile.Extension))
        {
            var map = ScenarioFile.Read(path).Looks.Map;
            foreach (var unit in map.Units.Values)
            {
                Assert.True(book.Rules.Units.ContainsKey(unit.TypeId), $"{Path.GetFileName(path)}: {unit.Id} — {unit.TypeId}");
                units++;
            }
            foreach (var player in map.Players.Values)
                Assert.True(book.Economy.Races.ContainsKey(player.RaceId), $"{Path.GetFileName(path)}: {player.Id} — {player.RaceId}");
            Assert.All(map.ItemTypes.Values, t => Assert.True(book.Items.ContainsKey(t), $"{Path.GetFileName(path)}: предмет {t}"));
        }
        Assert.True(units > 1000);
    }
}
