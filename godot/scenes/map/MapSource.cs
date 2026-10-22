using System;
using System.IO;
using LastEdict.Data.Gfx;
using LastEdict.Data.Map;
using LastEdict.Rules.Map;
using Godot;

namespace LastEdict.Godot;

/// <summary>Откуда сцены карты берут карту: <c>--scenario "Имя"</c> — сценарий порта <c>data/scenarios/Имя.json</c>
/// (по умолчанию Tutorial; импорт <c>tools/ScenarioImport</c>), либо <c>--sg путь</c> — сохранение или сценарий игры для
/// проверок по корпусу (читается на входе и сразу становится <see cref="MapState"/>). Копия игры (<see cref="GameDir"/>:
/// <c>--game</c>, <c>D2_GAME_DIR</c>, иначе соседний <c>disciples-modding/game-pristine</c>) нужна только инструментам.</summary>
public static class MapSource
{
    public static (MapState Map, string Path) Load(string[] args)
    {
        var (looks, path) = LoadLooks(args);
        return (looks.Map, path);
    }

    /// <summary>Карта вместе с объектами для отрисовки (<see cref="MapLooks"/>): <c>--sg</c> — файл игры (проверки по
    /// сохранениям корпуса), иначе сценарий порта <c>data/scenarios/&lt;--scenario&gt;.json</c> (или слоя мода).</summary>
    public static (MapLooks Looks, string Path) LoadLooks(string[] args)
    {
        if (Arg(args, "--sg") is { } sg) return (MapLooksLoader.Load(sg), sg);
        string path = GameData.Content.ScenarioPath(Arg(args, "--scenario") ?? "Tutorial");
        return (GameData.ReadScenario(path).Looks, path);
    }

    public static string GameDir(string[] args) =>
        Arg(args, "--game") ?? System.Environment.GetEnvironmentVariable("D2_GAME_DIR")
        ?? GameData.FromRoot(System.IO.Path.Combine("..", "disciples-modding", "game-pristine"));

    /// <summary>Типы земли клеток для <see cref="GroundComposer"/>.</summary>
    public static GroundType[,] GroundCells(MapState map)
    {
        int n = map.Size;
        var cells = new GroundType[n, n];
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++) cells[x, y] = GroundGeometry.TypeOf(map.Tiles[x, y]);
        return cells;
    }

    public static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
