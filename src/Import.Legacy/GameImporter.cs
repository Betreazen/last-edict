using System.Diagnostics;
using System.Globalization;
using LastEdict.Data.Content;
using LastEdict.Data.Dbf;
using LastEdict.Data.Game;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Data.Ui;

namespace LastEdict.Data.Import;

/// <summary>Полный импорт данных порта из копии игры (<c>docs/publishing.md</c>): всё, что в репозитории выведено из файлов игры, собирается
/// заново — таблицы правил, каталог меню, отрисовка карты, сценарии, экраны и строки интерфейса, переводы сценариев (из слоя русификации),
/// столица, имена героев, шрифты, звук, графика интерфейса и мира, ролики. Наш собственный контент (описания переходов, правила порта,
/// моды) импорт не трогает. Игра у пользователя зовёт его при первом запуске, разработчик — командой (<c>tools/import_game.py</c>).
/// Шаги независимы по файлам; итог — <c>import.json</c> в корне выхода (по нему игра понимает, что импорт сделан целиком).</summary>
public static class GameImporter
{
    /// <summary>Версия раскладки импорта: игра с другой версией просит импорт заново.</summary>
    public const int Format = 1;

    public const string MarkerFile = "import.json";

    /// <summary>Таблицы правил, которые читают загрузчики LastEdict.Data (как у <c>tools/RulesImport</c>).</summary>
    public static readonly string[] RuleTables =
    [
        "Gattacks.dbf", "Gunits.dbf", "GVars.dbf", "Gimmu.dbf", "GimmuC.dbf", "Gtransf.dbf", "GDynUpgr.dbf",
        "Gmodif.dbf", "GmodifL.dbf", "GleaUpg.dbf", "Glord.dbf", "Grace.dbf", "GLabi.dbf", "GItem.dbf",
        "Gspells.dbf", "GAI.dbf", "GcityInf.dbf", "Gbuild.dbf", "Gaction.dbf", "GLmark.dbf", "GMabi.dbf",
        "LleadA.dbf", "LmagItm.dbf", "LDthAnim.dbf", "LattS.dbf", "LAttR.dbf", "LattC.dbf", "GbuiList.dbf", "Tleader.dbf", "GspellR.dbf",
        "TAiMsg.dbf",
    ];

    /// <summary>Шаги по порядку: сначала данные (секунды), потом графика и звук (минуты), ролики — последними (нужен ffmpeg).</summary>
    public static readonly string[] Steps = ["rules", "game", "scenarios", "screens", "strings", "texts", "capital", "leaders", "fonts", "sound",
                                             UiAssetsImport.Step, GfxAssetsImport.Step, "video"];

    /// <summary>Что делает игра в первую очередь: всё, кроме роликов, обязательно.</summary>
    public static bool IsRequired(string step) => step != "video";

    /// <summary>Похожа ли папка на копию Disciples II, из которой можно импортировать (<c>Globals/Gunits.dbf</c>, <c>Interf/Interf.ff</c>,
    /// <c>Imgs</c>, <c>Exports</c>). Возвращает причину, если нет.</summary>
    public static string? CheckGameDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return "папки нет";
        foreach (string need in new[] { "Globals/Gunits.dbf", "Interf/Interf.ff", "Interf/Interf.dlg", "Imgs/BatUnits.ff" })
            if (!File.Exists(Path.Combine(dir, need))) return $"нет {need} — это не папка Disciples II (GOG 3.01)";
        if (!Directory.Exists(Path.Combine(dir, "Exports"))) return "нет Exports — это не папка Disciples II (GOG 3.01)";
        return null;
    }

    /// <summary>Похожа ли папка на слой русификации (русская копия игры или её тексты: <c>Globals/Tglobal.dbf</c> в кириллице).</summary>
    public static bool IsL10nDir(string? dir) =>
        !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "Globals", "Tglobal.dbf"));

    /// <summary>Импорт сделан целиком и этой версией раскладки.</summary>
    public static bool IsComplete(string outRoot)
    {
        string path = Path.Combine(outRoot, MarkerFile);
        if (!File.Exists(path)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("format", out var f) && f.GetInt32() == Format
                && doc.RootElement.TryGetProperty("complete", out var c) && c.GetBoolean();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or FormatException or IOException)
        {
            return false;
        }
    }

    /// <summary>Выполнить шаги <paramref name="only"/> (пусто — все). Отметка <c>import.json</c> пишется в конце; «целиком» — когда сделаны все
    /// обязательные шаги.</summary>
    /// <exception cref="InvalidDataException">Папка — не копия игры.</exception>
    public static IReadOnlyDictionary<string, string> Run(ImportContext ctx, IReadOnlyCollection<string>? only = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (CheckGameDir(ctx.GameDir) is { } why) throw new InvalidDataException($"{ctx.GameDir}: {why}");
        var wanted = only is { Count: > 0 } ? only.ToHashSet(StringComparer.OrdinalIgnoreCase) : Steps.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string s in wanted)
            if (!Steps.Contains(s, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException($"нет шага импорта «{s}»: {string.Join(", ", Steps)}");
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (string step in Steps)
            {
                if (!wanted.Contains(step)) continue;
                ctx.Cancel.ThrowIfCancellationRequested();
                ctx.Report(step, 0, 1);
                var clock = Stopwatch.StartNew();
                string outcome;
                try
                {
                    outcome = RunStep(ctx, step);
                }
                catch (Exception e) when (e is not OperationCanceledException && !IsRequired(step))
                {
                    // Необязательный шаг (ролики) не обнуляет импорт: игра идёт без него.
                    outcome = $"ошибка: {Unwrap(e).Message}";
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    results[step] = $"ошибка: {Unwrap(e).Message}";
                    throw;
                }
                results[step] = outcome;
                ctx.Say($"[{step}] {outcome}, {clock.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} с");
            }
        }
        finally
        {
            // Отметка — и после ошибки или отмены: сделанные шаги видны, «целиком» — только когда все обязательные «ok».
            WriteMarker(ctx, results);
        }
        return results;
    }

    /// <summary>Исключение без обёрток пула (<see cref="AggregateException"/> из <c>Parallel.ForEach</c>).</summary>
    public static Exception Unwrap(Exception e)
    {
        ArgumentNullException.ThrowIfNull(e);
        while (e is AggregateException { InnerExceptions.Count: 1 } a) e = a.InnerExceptions[0];
        return e;
    }

    private static string RunStep(ImportContext ctx, string step)
    {
        switch (step)
        {
        case "rules": Rules(ctx); return "ok";
        case "game": Catalog(ctx); return "ok";
        case "scenarios": Scenarios(ctx); return "ok";
        case "screens": Screens(ctx); return "ok";
        case "strings": return Strings(ctx);
        case "texts": return Texts(ctx);
        case "capital": CapitalImport.Run(ctx); return "ok";
        case "leaders": LeaderNamesImport.Run(ctx); return "ok";
        case "fonts": FontsImport.Run(ctx); return ctx.L10nDir is null ? "ok (без кириллицы)" : "ok";
        case "sound": SoundImport.Run(ctx); return "ok";
        case UiAssetsImport.Step: UiAssetsImport.Run(ctx); return "ok";
        case GfxAssetsImport.Step: GfxAssetsImport.Run(ctx); return "ok";
        case "video": return VideoImport.Run(ctx) ? "ok" : "пропущен";
        default: throw new ArgumentException($"нет шага импорта «{step}»", nameof(step));
        }
    }

    /// <summary>Таблицы игры → <c>data/rules/*.json</c>.</summary>
    public static void Rules(ImportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        string globals = ctx.Game("Globals"), outDir = Path.Combine(ctx.DataDir, "rules");
        Directory.CreateDirectory(outDir);
        long rows = 0;
        foreach (string name in RuleTables)
        {
            var table = GameTables.Read(globals, name);
            ImportFiles.WriteText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(name) + ".json"), GameTables.ToJson(table));
            rows += table.Rows.Count;
        }
        ctx.Say($"таблиц {RuleTables.Length}, строк {rows} -> data/rules");
        Content(ctx);
    }

    /// <summary>Таблицы игры → содержимое правил (<c>data/content/base</c>, мод <c>base</c>) и схемы JSON (<c>data/content/schema</c>):
    /// правила игры читают его, моды кладут свои слои поверх (<c>docs/content-schema.md</c>).</summary>
    public static void Content(ImportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var content = LegacyContent.Import(ctx.Game("Globals"));
        ContentFiles.WriteMod(content, Path.Combine(ctx.DataDir, ContentFiles.BaseDir), "base", "Disciples II");
        ContentFiles.WriteSchemas(Path.Combine(ctx.DataDir, ContentFiles.SchemaDir));
        ctx.Say($"сущностей {ContentSet.Kinds.Sum(k => content.Of(k.Type).Count)} -> data/{ContentFiles.BaseDir}");
    }

    /// <summary>Каталог меню (<c>data/game</c>: расы, сценарии, саги) и отрисовка карты (<c>data/map</c>).</summary>
    public static void Catalog(ImportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        string outDir = Path.Combine(ctx.DataDir, "game");
        var races = GameImport.Races(ctx.Game("Globals"));
        var scenarios = GameImport.Scenarios(ctx.Game("Exports"), races, ctx.Game("Campaign"));
        var campaigns = GameImport.Campaigns(ctx.GameDir, races);
        ImportFiles.WriteText(Path.Combine(outDir, "races.json"), GameCatalog.ToJson(races));
        ImportFiles.WriteText(Path.Combine(outDir, "scenarios.json"), GameCatalog.ToJson(scenarios));
        ImportFiles.WriteText(Path.Combine(outDir, "campaigns.json"), GameCatalog.ToJson(campaigns));
        var mapArt = MapArtData.Import(ctx.GameDir);
        mapArt.Save(Path.Combine(ctx.DataDir, "map"));
        ctx.Say($"рас {races.Count}, сценариев {scenarios.Count}, саг {campaigns.Count}; слоёв карты {mapArt.Layers.Count}, ориентиров {mapArt.Landmarks.Count}");
    }

    /// <summary>Сценарии <c>Exports/*.sg</c> и главы саг <c>Scens/*.dbf</c> → <c>data/scenarios/*.json</c>.</summary>
    public static void Scenarios(ImportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        string outDir = Path.Combine(ctx.DataDir, "scenarios");
        int count = 0, chapters = 0;
        foreach (string sg in Directory.GetFiles(ctx.Game("Exports"), "*.sg").Order(StringComparer.Ordinal))
        {
            var scenario = ScenarioFile.FromSg(sg);
            ScenarioFile.Write(ScenarioFile.PathOf(outDir, scenario.Id), scenario);
            count++;
        }
        string scens = ctx.Game("Scens");
        if (Directory.Exists(scens))
        {
            var sagas = Sg.ScensTables.Load(scens);
            foreach (string scen in sagas.Scenarios)
            {
                var chapter = ScenarioFile.FromSg(scen, Sg.SgFile.Load(Sg.SagaSg.Build(sagas, scen)));
                ScenarioFile.Write(ScenarioFile.PathOf(outDir, chapter.Id), chapter);
                chapters++;
            }
        }
        ctx.Say($"сценариев {count}, глав саг {chapters} -> data/scenarios");
    }

    /// <summary>Разметка экранов <c>Interf/*.dlg</c> → <c>data/ui/screens.json</c>. <c>CustomLobby.dlg</c> (лобби сетевой игры набора
    /// D2ModdingToolset, GPL-3) берётся из игры, иначе — из <see cref="ImportContext.ExtraDlgs"/>.</summary>
    public static void Screens(ImportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var sources = new List<string> { ctx.Game("Interf", "Interf.dlg") };
        string lobby = ctx.Game("Interf", "CustomLobby.dlg");
        if (File.Exists(lobby)) sources.Add(lobby);
        else sources.AddRange(ctx.ExtraDlgs.Where(File.Exists));
        var screens = DlgImport.LoadAll(sources);
        ImportFiles.WriteText(Path.Combine(ctx.DataDir, "ui", "screens.json"), UiScreens.ToJson(screens));
        ctx.Say($"экранов {screens.Count} ({string.Join(", ", sources.Select(Path.GetFileName))}) -> data/ui/screens.json");
    }

    /// <summary>Строки интерфейса: английские — из игры, русские — игра и поверх слой русификации (если он есть).</summary>
    public static string Strings(ImportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        string ui = Path.Combine(ctx.DataDir, "ui");
        Directory.CreateDirectory(ui);
        var en = UiStringsImport.Read([ctx.GameDir]);
        UiStrings.Save(Path.Combine(ui, UiStrings.FileName("en")), en);
        if (ctx.L10nDir is null || !UiStringsImport.IsAvailable(ctx.L10nDir))
        {
            ctx.Say($"строк en {en.Count}; слоя русификации нет — strings.ru.json не собран");
            return "ok (только en)";
        }
        var ru = UiStringsImport.Read([ctx.GameDir, ctx.L10nDir]);
        UiStrings.Save(Path.Combine(ui, UiStrings.FileName("ru")), ru);
        ctx.Say($"строк en {en.Count}, ru {ru.Count}");
        return "ok";
    }

    /// <summary>Переводы сценариев <c>data/scenarios/lang/ru</c> из слоя русификации (нужны уже собранные сценарии и каталог).</summary>
    public static string Texts(ImportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.L10nDir is null || !ScenarioTextsImport.IsAvailable(ctx.L10nDir))
        {
            ctx.Say("слоя русификации нет — переводов сценариев нет");
            return "ok (без русификации)";
        }
        string scenarios = Path.Combine(ctx.DataDir, "scenarios");
        var catalog = GameCatalog.Load(Path.Combine(ctx.DataDir, "game"));
        int written = 0;
        // Уже лежащий перевод (в репозитории разработчика — с дописанными переводчиком порта пропусками слоя и машинным переводом сценариев,
        // которых в слое нет) сохраняется, как у tools/ScenarioImport --texts; у игрока его нет, и перевод — ровно слой.
        foreach (var r in ScenarioTextsImport.Read(scenarios, catalog, ctx.GameDir, ctx.L10nDir, "ru", "Руссобит-М",
                                                   id => ScenarioTexts.Load([scenarios], "ru", id)))
        {
            if (r.File is null) continue;
            ScenarioTexts.Write(ScenarioTexts.PathOf(scenarios, "ru", r.Id), r.File);
            written++;
        }
        ctx.Say($"переводов сценариев {written} -> data/scenarios/lang/ru");
        return "ok";
    }

    private static void WriteMarker(ImportContext ctx, Dictionary<string, string> results)
    {
        string path = Path.Combine(ctx.OutRoot, MarkerFile);
        var steps = new PyObj();
        if (File.Exists(path))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("steps", out var old))
                    foreach (var p in old.EnumerateObject()) steps[p.Name] = p.Value.GetString();
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException)
            {
                // испорченная отметка — пишется заново
            }
        }
        foreach (var (k, v) in results) steps[k] = v;
        bool complete = Steps.Where(IsRequired).All(s => steps[s] is string v && v.StartsWith("ok", StringComparison.Ordinal));
        var marker = new PyObj
        {
            ["format"] = Format, ["complete"] = complete, ["game"] = ctx.GameDir, ["l10n"] = ctx.L10nDir,
            ["date"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), ["steps"] = steps,
        };
        ImportFiles.WriteText(path, PyJson.Indented(marker, 1) + "\n");
    }
}
