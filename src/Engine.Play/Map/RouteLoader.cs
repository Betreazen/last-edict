using System.Text.Json;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Map;

/// <summary>
/// Читает <c>route.json</c> — маршрут, который построил <c>tools/scenario_c4.py route</c> и сыграл
/// <c>tools/corpus_c4_run.py</c>.
///
/// <para>План задачи 9 называл это <c>MapRoute.Load</c>, но статический метод записи из
/// <c>LastEdict.Rules</c> не может лежать в <c>LastEdict.Data</c>, а чтение файлов в <c>LastEdict.Rules</c> запрещено
/// (Global Constraints). Поэтому загрузчик — отдельный класс здесь, рядом с
/// <see cref="MapLoader"/>.</para>
///
/// <para>Поля файла: <c>scenario</c>, <c>stack</c>, <c>path</c> (все клетки подряд), <c>turns</c>
/// (<c>[{turn, target:[x,y]}]</c>), <c>checkpoints</c>. Отсутствие любого из них — отказ с именем
/// поля, а не тихий пустой маршрут: пустой маршрут дал бы «проверено 0» и зелёный вид.</para>
/// </summary>
public static class RouteLoader
{
    public static MapRoute Load(string jsonPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = doc.RootElement;

        var path = Cells(Require(root, "path", jsonPath), jsonPath);
        var turns = new List<(int Turn, int X, int Y)>();
        foreach (var t in Require(root, "turns", jsonPath).EnumerateArray())
        {
            var target = Cells(Require(t, "target", jsonPath), jsonPath, single: true);
            turns.Add((Require(t, "turn", jsonPath).GetInt32(), target[0].X, target[0].Y));
        }

        var checkpoints = Require(root, "checkpoints", jsonPath).EnumerateArray()
            .Select(c => c.GetInt32()).ToList();

        if (path.Count == 0) throw new InvalidDataException($"{jsonPath}: маршрут пуст (path)");
        if (turns.Count == 0) throw new InvalidDataException($"{jsonPath}: в маршруте нет ходов (turns)");

        return new MapRoute(
            Scenario: Require(root, "scenario", jsonPath).GetString() ?? "",
            StackId: Require(root, "stack", jsonPath).GetString() ?? "",
            Path: path,
            Turns: turns,
            Checkpoints: checkpoints);
    }

    private static JsonElement Require(JsonElement parent, string name, string file)
    {
        if (!parent.TryGetProperty(name, out var value))
            throw new InvalidDataException($"{file}: нет поля {name} — маршрут строит scenario_c4.py route");
        return value;
    }

    /// <summary>Клетки как <c>[[x,y], ...]</c>; <paramref name="single"/> — одна пара <c>[x,y]</c>.</summary>
    private static List<(int X, int Y)> Cells(JsonElement element, string file, bool single = false)
    {
        var cells = new List<(int X, int Y)>();
        foreach (var pair in single ? new[] { element } : element.EnumerateArray().ToArray())
        {
            var xy = pair.EnumerateArray().Select(v => v.GetInt32()).ToArray();
            if (xy.Length != 2)
                throw new InvalidDataException($"{file}: клетка задана {xy.Length} числами вместо двух");
            cells.Add((xy[0], xy[1]));
        }
        return cells;
    }
}
