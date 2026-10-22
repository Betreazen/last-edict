using System.Text.Json;
using D2.Data.Map;
using D2.Rules.Map;

namespace D2.Data.Play;

/// <summary>Сценарий в данных порта: карта с объектами для отрисовки и события. Из него начинается партия
/// (<c>NewGame.Start</c>); <c>.sg</c> игры читает только импорт (<c>tools/ScenarioImport</c>,
/// <c>docs/principles.md</c> §3).</summary>
/// <param name="Id">Имя файла сценария без расширения (<c>Power to Corrupt</c>) — оно же имя файла в <c>data/scenarios</c>.</param>
public sealed record ScenarioData(string Id, MapLooks Looks, ScenarioEventSet Events);

/// <summary>Файл сценария порта: JSON <c>d2-port-scenario</c> с версией, <c>data/scenarios/&lt;имя&gt;.json</c>.</summary>
public static class ScenarioFile
{
    public const string Format = "d2-port-scenario";

    /// <summary>Версия формата. Меняется, когда старый файл нельзя прочитать новой моделью как есть или в файле появились новые
    /// данные импорта.
    /// 2 — имена и описания объектов карты (<c>MapSite.Name</c>/<c>Description</c>: города, площадки, руины); файл версии 1
    /// читается без них.
    /// 3 — выигранные сражения отряда (<c>MapStack.BattlesWon</c>, <c>NBBATTLE</c>); версии 1–2 читаются с нулём.
    /// 4 — могилы (<c>MapState.Tombs</c>) и уровень создания отряда (<c>MapStack.CreationLevel</c>); версии 1–3 — без могил, уровень 1.
    /// 5 — приоритет редактора для ИИ (<c>MapStack.AiPriority</c>, <c>MapSite.AiPriority</c>, <c>AIPRIORITY</c>); версии 1–4 — 3.</summary>
    public const int Version = 5;

    public const string Extension = ".json";

    private sealed record Envelope(string Format, int Version, string Id, MapLooks Looks, ScenarioEventSet Events);

    /// <summary>Сценарий из <c>.sg</c> игры — только для импорта.</summary>
    public static ScenarioData FromSg(string sgPath) =>
        new(Path.GetFileNameWithoutExtension(sgPath), MapLooksLoader.Load(sgPath), EventLoader.Load(sgPath));

    /// <summary>Сценарий из уже разобранного <c>.sg</c> (глава саги из таблиц <c>Scens</c>) — только для импорта.</summary>
    public static ScenarioData FromSg(string id, D2.Data.Sg.SgFile file) =>
        new(id, MapLooksLoader.Load(id, file), EventLoader.Load(id, file));

    public static string ToJson(ScenarioData scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return JsonSerializer.Serialize(new Envelope(Format, Version, scenario.Id, scenario.Looks, scenario.Events), SaveJson.Options);
    }

    /// <exception cref="InvalidDataException">Не сценарий порта или версия новее, чем умеет эта сборка.</exception>
    public static ScenarioData FromJson(string json)
    {
        Envelope? e;
        try
        {
            e = JsonSerializer.Deserialize<Envelope>(json, SaveJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"сценарий не читается: {ex.Message}", ex);
        }
        if (e is null || e.Format != Format || e.Looks is null) throw new InvalidDataException("не сценарий порта");
        if (e.Version > Version) throw new InvalidDataException($"сценарий версии {e.Version}, сборка знает до {Version}");
        // Счётчики id в файл не пишутся (MapState.IdNext): по объектам карты, как у движка после чтения сценария (0x5ee996). Без них
        // новая партия отдавала номер павшего до первой выдачи класса снова (правило «наибольший из живых + 1»).
        // Изученные заклинания — по возрастанию id, как после чтения движком (D2.Rules.Map.KnownSpellOrder, 0x5fe926).
        var map = GameSession.KnownSpellsSorted(e.Looks.Map);
        var looks = e.Looks with { Map = GameSession.With(map, idNext: GameSession.IdNextOf(map)) };
        return new ScenarioData(e.Id, looks, e.Events ?? ScenarioEventSet.Empty);
    }

    /// <summary>Файл сценария <paramref name="id"/> в каталоге <paramref name="dir"/> (<c>data/scenarios</c>).</summary>
    public static string PathOf(string dir, string id) => Path.Combine(dir, id + Extension);

    public static ScenarioData Read(string path) => FromJson(File.ReadAllText(path));

    public static void Write(string path, ScenarioData scenario)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToJson(scenario));
    }
}
