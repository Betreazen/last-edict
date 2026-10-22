using System.Reflection;
using D2.Data.Content;
using D2.Data.Play;

namespace D2.Data.Tests;

/// <summary>Своё мини-содержимое <c>content/test-pack</c> (<c>tools/test_pack.py</c>, CC0): правила читают его так же, как импорт копии
/// игры, — на нём CI нового репозитория проверяет цепочку «содержимое → правила» и гоняет снимки без данных оригинала.</summary>
public class TestPackTests
{
    internal static string Dir { get; } = typeof(TestPackTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "TestPackDir").Value!;

    [Fact]
    public void TestPack_LoadsIntoRules()
    {
        var book = GameBook.Load(Dir);
        Assert.Equal(3, book.Economy.Races.Count);
        Assert.True(book.Rules.Units.Count > 20);
    }
}

/// <summary>Снимки «порт против порта» уровня CI (решение пользователя 22.10): свои карты <c>content/test-pack/scenarios</c> на своём
/// содержимом, все игроки — ИИ, зерно 1, 20 дней; бои — автобой пар отрядов карты. Данных игры не нужно — их гоняет CI нового репозитория.</summary>
[Trait("Category", "Snapshot")]
public class TestPackSnapshotTests
{
    private const int Days = 20;
    private const int Battles = 12;
    private const uint Seed = 1;

    public static TheoryData<string> Maps => new() { "twin-fields", "long-march" };

    [Theory]
    [MemberData(nameof(Maps))]
    public void AiDays_MatchTheSnapshot(string map) =>
        PortSnapshot.Verify($"test-pack-{map}-days", PortSnapshot.AiDays(Start(map, out _), Days));

    [Theory]
    [MemberData(nameof(Maps))]
    public void Battles_MatchTheSnapshot(string map)
    {
        var session = Start(map, out var book);
        PortSnapshot.Verify($"test-pack-{map}-battles", PortSnapshot.Battles(session.Map, book, Battles, (int)Seed));
    }

    private static GameSession Start(string map, out GameBook book)
    {
        book = GameBook.Load(TestPackTests.Dir);
        var data = ScenarioFile.Read(ScenarioFile.PathOf(Path.Combine(TestPackTests.Dir, "scenarios"), map));
        var state = new GameState(GameInfo.FromMap(data.Id, data.Looks), data.Looks)
        {
            Events = data.Events, EventProgress = D2.Rules.Map.EventProgress.From(data.Events),
        }.Reseeded(Seed);
        return new GameSession(state, D2.Data.Map.MapArtData.Empty, book);
    }
}
