using LastEdict.Data.Play;
using LastEdict.Data.Sg;

namespace LastEdict.Data.Game;

/// <summary>Однократный импорт готового перевода сценариев из локализованной копии игры (слой Руссобит-М
/// <c>disciples-vanilla/l10n/russobit</c>: <c>Exports/*.sg</c> квестов и <c>Scens/Sinfo</c>, <c>Scens/Tscen</c> глав саг) в языковой
/// слой порта <c>data/scenarios/lang/&lt;язык&gt;/</c> (<see cref="ScenarioTexts"/>). Сценарий слоя разбирается тем же импортом, что и
/// английский (<see cref="ScenarioFile.FromSg(string)"/>, <see cref="SagaSg"/>), и тексты сводятся по ключам полей — id объектов и
/// номерам эффектов, а не по порядку строк. Старые форматы — только здесь (<c>docs/principles.md</c> §3).</summary>
public static partial class ScenarioTextsImport
{
    /// <summary>Итог по сценарию: файл слоя (null — у сценария нет источника в слое), ключи английского без перевода и ключи
    /// перевода, которых нет в английском (структура локализованного файла разошлась).</summary>
    public sealed record Result(string Id, ScenarioTexts.LangFile? File, IReadOnlyList<string> Missing, IReadOnlyList<string> Extra);

    /// <summary>Слой русификации Руссобит-М рядом с эталонной копией игры (<c>disciples-vanilla/l10n/russobit</c>), как у
    /// <see cref="Ui.UiStringsImport.DefaultRoots"/>.</summary>
    public static string DefaultRoot =>
        Path.GetFullPath(Path.Combine(GamePaths.GameDir, "..", "..", "disciples-vanilla", "l10n", "russobit"));

    /// <summary>Есть ли в корне слоя хоть что-то для сценариев (<c>Exports</c> или <c>Scens</c>).</summary>
    public static bool IsAvailable(string root) =>
        Directory.Exists(Path.Combine(root, "Exports")) || Directory.Exists(Path.Combine(root, "Scens"));

    /// <summary>Перевод всех сценариев каталога <paramref name="scenariosDir"/>: английский — сами файлы порта и карточки
    /// <paramref name="catalog"/>, перевод — <paramref name="layerRoot"/> (поверх таблиц саг игры <paramref name="gameDir"/>).
    /// <paramref name="existing"/> — уже лежащий перевод сценария: ключи, которых в слое нет (пропуски Руссобит-М, дописанные
    /// переводчиком порта), из него сохраняются, а текст слоя всегда берётся заново. Текст слоя, равный английскому, — не перевод, а
    /// пропуск.</summary>
    public static IReadOnlyList<Result> Read(string scenariosDir, GameCatalog catalog, string gameDir, string layerRoot,
                                             string language, string source,
                                             Func<string, IReadOnlyDictionary<string, string>>? existing = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        string layerScens = Path.Combine(layerRoot, "Scens"), gameScens = Path.Combine(gameDir, "Scens");
        var sagas = Directory.Exists(layerScens) && Directory.Exists(gameScens) ? ScensTables.Load(gameScens, layerScens) : null;
        var chapters = new HashSet<string>(sagas?.Scenarios ?? [], StringComparer.OrdinalIgnoreCase);
        var results = new List<Result>();
        foreach (string path in Directory.GetFiles(scenariosDir, "*" + ScenarioFile.Extension).Order(StringComparer.Ordinal))
        {
            var english = ScenarioFile.Read(path);
            string id = english.Id;
            var info = catalog.Scenario(id);
            var en = Texts(english, info);

            ScenarioData? translated = null;
            ScenarioInfo? translatedInfo = null;
            string sg = Path.Combine(layerRoot, "Exports", id + ".sg");
            if (File.Exists(sg))
            {
                translated = ScenarioFile.FromSg(sg);
                translatedInfo = GameImport.ScenarioOf(SgFile.Open(sg), id, "", catalog.Races);
            }
            else if (sagas is not null && chapters.Contains(id))
            {
                var file = SgFile.Load(SagaSg.Build(sagas, id));
                translated = ScenarioFile.FromSg(id, file);
                translatedInfo = GameImport.ScenarioOf(file, id, "", catalog.Races);
            }
            if (translated is null)
            {
                results.Add(new Result(id, null, [.. en.Select(p => p.Key)], []));
                continue;
            }

            var tr = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, text) in Texts(translated, info is null ? null : translatedInfo)) tr.TryAdd(key, text);
            var keys = en.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
            var kept = existing?.Invoke(id) ?? ScenarioTexts.None;
            var texts = new List<KeyValuePair<string, string>>();
            var missing = new List<string>();
            // Текст слоя, совпавший с оригиналом, — не перевод (Руссобит-М оставил «Defeat King Virgil.», «Konrad»), как и id поля вместо
            // текста («NAME_TXT» у лидера отряда): ключ считается пропуском, и его можно дописать.
            foreach (var (key, original) in en)
                if (tr.TryGetValue(key, out var text) && text != original && !FieldName().IsMatch(text)) texts.Add(new(key, text));
                else if (kept.TryGetValue(key, out var own) && own.Length > 0 && own != original && !FieldName().IsMatch(own)) texts.Add(new(key, own));
                else missing.Add(key);
            results.Add(new Result(id, new ScenarioTexts.LangFile(id, language, source, texts), missing,
                                   [.. tr.Keys.Where(k => !keys.Contains(k))]));
        }
        return results;
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Z][A-Z0-9]*_[A-Z0-9_]+$")]
    private static partial System.Text.RegularExpressions.Regex FieldName();

    private static List<KeyValuePair<string, string>> Texts(ScenarioData scenario, ScenarioInfo? info) =>
        [.. (info is null ? [] : ScenarioTexts.Extract(info)), .. ScenarioTexts.Extract(scenario)];
}
