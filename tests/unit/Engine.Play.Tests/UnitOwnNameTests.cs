using LastEdict.Data.Content;
using LastEdict.Data.Play;
using LastEdict.Rules.Battle;

namespace LastEdict.Data.Tests;

/// <summary>Имя юнита <c>CMidUnit::getName</c> (<c>0x5e97dc</c>) — подпись крупного портрета боя <c>X100TA0608</c> (<c>%NAME%</c>,
/// вызов <c>0x64f864</c>): собственное имя — только у героя во главе отряда (реализация <c>IUsStackLeader</c>, не <c>IUsNoble</c> и не
/// <c>IUsSummon</c>), даже пустое; прочим — null, подпись берёт имя типа. Прогон 14.10, пометка 2: у рядовых в подписи было одно «ОЗ».</summary>
public class UnitOwnNameTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly Lazy<(GameContent Content, GameBook Book)> Shared = new(() =>
        (SharedContent.Load(Data), GameBook.Load(Path.Combine(Data, "rules"))));

    /// <summary>Партия на сценарии порта «Tutorial»; <paramref name="names"/> — замена собственных имён лидеров отрядов.</summary>
    private static GameSession Tutorial(IReadOnlyDictionary<string, string>? names = null)
    {
        string path = ScenarioFile.PathOf(Path.Combine(Data, "scenarios"), "Tutorial");
        Skip.IfNot(File.Exists(path), $"нет {path}");
        var looks = ScenarioFile.Read(path).Looks;
        if (names is not null)
            looks = looks with { Stacks = [.. looks.Stacks.Select(s => names.TryGetValue(s.Id, out var n) ? s with { LeaderName = n } : s)] };
        return new GameSession(new GameState(new GameInfo("Tutorial", "Tutorial", [], 1), looks), Shared.Value.Content.MapArt, Shared.Value.Book);
    }

    [SkippableFact]
    public void OwnName_OnlyHeroLeadingStack_EvenEmpty()
    {
        var session = Tutorial();
        var book = Shared.Value.Book;
        var map = session.Map;
        // Отряд человека: лидер — герой «Jagare» (S001UN0001).
        var hero = map.Stacks.Values.First(s => s.LeaderId.Equals("S001UN0001", StringComparison.OrdinalIgnoreCase));
        string heroType = map.Units[hero.LeaderId].TypeId;
        Assert.Equal(UnitCategory.Leader, book.CategoryOf(heroType));
        Assert.Equal("Jagare", session.OwnName(hero.LeaderId, heroType));

        // Рядовой любого отряда — имени нет (подпись — имя типа), хотя сценарий пишет имя каждому юниту.
        var soldier = map.Stacks.Values.SelectMany(s => s.UnitIds.Where(u => !u.Equals(s.LeaderId, StringComparison.OrdinalIgnoreCase)))
                         .Select(u => map.Units[u]).First(u => book.CategoryOf(u.TypeId) == UnitCategory.Soldier);
        Assert.Null(session.OwnName(soldier.Id, soldier.TypeId));

        // Отряд во главе с воином (нейтралы) — у лидера нет реализации IUsStackLeader: имя типа.
        var ledBySoldier = map.Stacks.Values.FirstOrDefault(s => book.CategoryOf(map.Units[s.LeaderId].TypeId) == UnitCategory.Soldier);
        if (ledBySoldier is not null) Assert.Null(session.OwnName(ledBySoldier.LeaderId, map.Units[ledBySoldier.LeaderId].TypeId));

        // Герой в бою превращён в облик вора или призванного — по текущему типу (IUsNoble, IUsSummon): имя типа.
        foreach (int category in (int[])[UnitCategory.Noble, UnitCategory.Summon])
        {
            string other = book.Rules.Units.Keys.First(t => book.CategoryOf(t) == category);
            Assert.Null(session.OwnName(hero.LeaderId, other));
        }

        // Герой без имени — пустое имя, а не имя типа (снимок оригинала 28.09 DLG_BATTLE_A-f0-2648341: эльф ai-q14-crit без имени,
        // в подписи одно «ОЗ»).
        var nameless = Tutorial(new Dictionary<string, string> { [hero.Id] = "" });
        Assert.Equal("", nameless.OwnName(hero.LeaderId, heroType));
    }
}
