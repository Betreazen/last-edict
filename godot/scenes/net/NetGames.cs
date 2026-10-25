using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastEdict.Data.Content;
using LastEdict.Data.Game;
using LastEdict.Data.Menu;
using LastEdict.Data.Play;
using LastEdict.Net;

namespace LastEdict.Godot;

/// <summary>Сетевая партия в игре — общее у лобби и запуска без меню (<c>MapScreen.Net.cs</c>): новая партия комнаты, набор правил, сессия
/// над видом клиента, игроки сценария для мест лобби.</summary>
public static class NetGames
{
    /// <summary>Набор правил этой игры: база содержимого и включённые моды (графические — не в счёт).</summary>
    public static Ruleset Ruleset() => RulesetHash.Of(GameData.RulesDir, GameData.Mods);

    /// <summary>Сессия над видом клиента (правила и графика карты — свои).</summary>
    public static GameSession ViewSession(GameState view) => new(view, GameData.Content.MapArt, GameData.Book);

    /// <summary>Свои настройки сети игрока (<c>user://net.json</c>): посредник и имя.</summary>
    public static string PrefsPath => global::Godot.ProjectSettings.GlobalizePath("user://net.json");

    /// <summary>Свои комнаты игрока (<c>user://net-rooms.json</c>).</summary>
    public static string RoomsPath => global::Godot.ProjectSettings.GlobalizePath("user://net-rooms.json");

    /// <summary>Игроки сценария (id и раса) — для мест лобби.</summary>
    public static IReadOnlyList<(string Player, string RaceId)> Players(ScenarioInfo scenario) =>
        [.. Read(scenario).Looks.Map.Players.Values.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => (p.Id, p.RaceId))];

    private static ScenarioData Read(ScenarioInfo scenario) =>
        GameData.ReadScenario(GameData.Content.ScenarioPath(scenario.File.Length > 0 ? Path.GetFileNameWithoutExtension(scenario.File) : scenario.Id));

    /// <summary>Новая партия сценария с людьми на местах <paramref name="humans"/> (игроки сценария), как из лобби: раса — игрока сценария,
    /// лорд — воин, имя — <paramref name="names"/> (нет — id игрока). Начинается с записанного и прочитанного состояния: чтение кое-что
    /// упорядочивает (известные заклинания — как движок при загрузке), и журнал, асинхронная комната и возвращение видят ту же партию.</summary>
    public static GameSession NewGame(string scenarioId, IReadOnlyList<string> humans, uint seed, IReadOnlyDictionary<string, string>? names = null)
    {
        var catalog = GameData.Catalog;
        var scenario = catalog.Scenario(scenarioId) ?? throw new InvalidOperationException($"сценария «{scenarioId}» нет");
        var data = Read(scenario);
        var players = data.Looks.Map.Players;
        var setup = new NewGameSetup
        {
            Scenario = scenario, Difficulty = 1,
            Seats = [.. humans.Select(id => players.TryGetValue(id, out var p)
                ? new PlayerSeat(p.RaceId, names?.GetValueOrDefault(id) ?? id, 0, 1, PlayerId: id)
                : throw new InvalidOperationException($"в сценарии нет игрока {id}"))],
        };
        var begun = LastEdict.Data.Play.NewGame.Begin(data, setup, catalog, GameData.Book, GameData.Content.MapArt, reseed: seed);
        var read = GameSave.FromJson(GameSave.ToJson(begun.SnapshotWithoutSummaries(), "", DateTime.UnixEpoch));
        return new GameSession(read, GameData.Content.MapArt, GameData.Book) { EventBattlesOnScreen = false };
    }
}
