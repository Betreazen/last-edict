using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Tests;

/// <summary>
/// Снимки «порт против порта» на импортированных сценариях (локальный уровень, решение пользователя 22.10): четыре карты разного размера,
/// все игроки — ИИ, зерно 1, 15 дней; бои — автобой пар отрядов карты; в файлах только хеши (данные карт оригинала в репозиторий не идут). Нужен импорт из копии игры (<c>data/content/base</c>,
/// <c>data/scenarios</c>, <c>data/map</c>); без него тест пропускается — CI нового репозитория гоняет снимки на <c>content/test-pack</c>.
/// Эталон (<c>vanilla-reference</c>) и Last Edict обязаны давать одни и те же строки.
/// </summary>
[Trait("Category", "Snapshot")]
public class PortSnapshotTests
{
    private const int Days = 15;
    private const int Battles = 12;
    private const uint Seed = 1;

    public static TheoryData<string, string> Maps => new()
    {
        { "mercantile", "Mercantile" },                            // 48², 3 игрока
        { "arrival-of-the-pilgrims", "Arrival of the Pilgrims" },  // 72², 4
        { "power-to-corrupt", "Power to Corrupt" },                // 96², 5
        { "s142", "S142SC0000" },                                  // 120², 5
    };

    [SkippableTheory]
    [MemberData(nameof(Maps))]
    public void AiDays_MatchTheSnapshot(string name, string scenario)
    {
        var (session, _) = Start(scenario);
        PortSnapshot.Verify($"imported-{name}-days", PortSnapshot.AiDays(session, Days, hashesOnly: true));
    }

    [SkippableTheory]
    [MemberData(nameof(Maps))]
    public void Battles_MatchTheSnapshot(string name, string scenario)
    {
        var (session, book) = Start(scenario);
        PortSnapshot.Verify($"imported-{name}-battles", PortSnapshot.Battles(session.Map, book, Battles, (int)Seed, hashesOnly: true));
    }

    /// <summary>Партия из сценария без людей (игроки сценария — ИИ) с генератором от зерна <see cref="Seed"/>.</summary>
    private static (GameSession Session, GameBook Book) Start(string scenario)
    {
        ImportedData.Require("scenarios", "map");
        var book = GameBook.Load(ImportedData.Content);
        var data = ScenarioFile.Read(ScenarioFile.PathOf(ImportedData.PathOf("scenarios"), scenario));
        var state = new GameState(GameInfo.FromMap(data.Id, data.Looks), data.Looks)
        {
            Events = data.Events, EventProgress = EventProgress.From(data.Events),
        }.Reseeded(Seed);
        return (new GameSession(state, MapArtData.Load(ImportedData.PathOf("map")), book), book);
    }
}
