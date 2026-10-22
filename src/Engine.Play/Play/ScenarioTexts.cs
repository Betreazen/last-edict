using System.Text.Encodings.Web;
using System.Text.Json;
using D2.Data.Game;
using D2.Data.Map;
using D2.Rules.Map;

namespace D2.Data.Play;

/// <summary>Перевод сценария — языковой слой поверх файла порта (<c>data/scenarios/lang/&lt;язык&gt;/&lt;сценарий&gt;.json</c>), а не
/// правка самого сценария: английский файл остаётся как импортирован, перевод лежит рядом и накладывается при чтении.
///
/// <para>Слой — плоская таблица «ключ → текст». Ключ называет поле сценария (<see cref="Extract(ScenarioData)"/>):
/// <c>name</c>, <c>description</c>, <c>briefing</c>, <c>briefingLong</c>, <c>winText</c>, <c>loseText</c> — карточка сценария
/// (<see cref="ScenarioInfo"/>); <c>player:&lt;id&gt;</c> — имя лорда игрока; <c>site:&lt;id&gt;:name</c>/<c>:desc</c> — город, площадка,
/// руина; <c>stack:&lt;id&gt;</c> — имя лидера отряда; <c>template:&lt;id&gt;</c> — имя лидера отряда, который создаст событие;
/// <c>event:&lt;id&gt;:&lt;n&gt;</c> — текст n-го эффекта события (окно <c>L_POPUP</c> или новая цель <c>L_CHANGE_OBJECTIVE</c>).</para>
///
/// <para>Нет файла, нет ключа или текст пуст — остаётся текст сценария (откат на оригинал). Ключ, которого в сценарии нет,
/// пропускается: перевод под другую версию сценария не роняет партию (проверку полноты делает <c>tools/scenario-ru/check.py</c>
/// и тест данных).</para></summary>
public static class ScenarioTexts
{
    public const string Format = "d2-port-scenario-texts";
    public const int Version = 1;

    /// <summary>Каталог языковых слоёв внутри каталога сценариев.</summary>
    public const string LangDir = "lang";

    public const string Name = "name", Description = "description", Briefing = "briefing", BriefingLong = "briefingLong",
                        WinText = "winText", LoseText = "loseText";

    private static readonly JsonWriterOptions WriteOptions = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Пустой слой: перевода нет.</summary>
    public static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Файл перевода сценария <paramref name="id"/> на <paramref name="language"/> в каталоге сценариев <paramref name="scenariosDir"/>.</summary>
    public static string PathOf(string scenariosDir, string language, string id) =>
        Path.Combine(scenariosDir, LangDir, language, id + ScenarioFile.Extension);

    /// <summary>Переводимые тексты карточки сценария (непустые), в порядке полей.</summary>
    public static IEnumerable<KeyValuePair<string, string>> Extract(ScenarioInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return NonEmpty([new(Name, info.Name), new(Description, info.Description), new(Briefing, info.Briefing),
                         new(BriefingLong, info.BriefingLong), new(WinText, info.WinText), new(LoseText, info.LoseText)]);
    }

    /// <summary>Переводимые тексты самого сценария (непустые), в порядке файла: игроки, объекты, отряды, шаблоны, события.</summary>
    public static IEnumerable<KeyValuePair<string, string>> Extract(ScenarioData scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var map = scenario.Looks.Map;
        var texts = new List<KeyValuePair<string, string>>();
        texts.AddRange(map.Players.Values.Select(p => Pair($"player:{p.Id}", p.Name)));
        foreach (var site in map.Sites)
        {
            texts.Add(Pair($"site:{site.Id}:name", site.Name));
            texts.Add(Pair($"site:{site.Id}:desc", site.Description));
        }
        texts.AddRange(scenario.Looks.Stacks.Select(s => Pair($"stack:{s.Id}", s.LeaderName)));
        texts.AddRange(scenario.Events.Templates.Values.Select(t => Pair($"template:{t.Id}", t.Name)));
        foreach (var e in scenario.Events.Events)
            for (int i = 0; i < e.Effects.Count; i++)
                if (EffectText(e.Effects[i]) is { } text) texts.Add(Pair($"event:{e.Id}:{i}", text));
        return NonEmpty(texts);
    }

    /// <summary>Карточка сценария с переводом; чего в слое нет — как было.</summary>
    public static ScenarioInfo Apply(ScenarioInfo info, IReadOnlyDictionary<string, string> texts)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0) return info;
        return info with
        {
            Name = Pick(texts, Name, info.Name),
            Description = Pick(texts, Description, info.Description),
            Briefing = Pick(texts, Briefing, info.Briefing),
            BriefingLong = Pick(texts, BriefingLong, info.BriefingLong),
            WinText = Pick(texts, WinText, info.WinText),
            LoseText = Pick(texts, LoseText, info.LoseText),
        };
    }

    /// <summary>Сценарий с переводом: новые записи, исходный сценарий не меняется; чего в слое нет — как было.</summary>
    public static ScenarioData Apply(ScenarioData scenario, IReadOnlyDictionary<string, string> texts)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0) return scenario;
        var map = scenario.Looks.Map;
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value with { Name = Pick(texts, $"player:{p.Value.Id}", p.Value.Name) },
                                               StringComparer.OrdinalIgnoreCase);
        var sites = map.Sites.Select(s => s with
        {
            Name = Pick(texts, $"site:{s.Id}:name", s.Name),
            Description = Pick(texts, $"site:{s.Id}:desc", s.Description),
        }).ToList();
        var stacks = scenario.Looks.Stacks.Select(s => s with { LeaderName = Pick(texts, $"stack:{s.Id}", s.LeaderName) }).ToList();
        var templates = scenario.Events.Templates.ToDictionary(t => t.Key, t => t.Value with { Name = Pick(texts, $"template:{t.Value.Id}", t.Value.Name) },
                                                               StringComparer.OrdinalIgnoreCase);
        var events = scenario.Events.Events.Select(e => e with
        {
            Effects = [.. e.Effects.Select((x, i) => WithText(x, texts.GetValueOrDefault($"event:{e.Id}:{i}")))],
        }).ToList();
        return scenario with
        {
            Looks = scenario.Looks with { Map = GameSession.With(map, players: players, sites: sites), Stacks = stacks },
            Events = scenario.Events with { Events = events, Templates = templates },
        };
    }

    /// <summary>Каталог меню с переводом карточек: квесты и главы саг (<paramref name="textsOf"/> — слой по id сценария).</summary>
    public static GameCatalog Apply(GameCatalog catalog, Func<string, IReadOnlyDictionary<string, string>> textsOf)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(textsOf);
        return catalog with
        {
            Scenarios = [.. catalog.Scenarios.Select(s => Apply(s, textsOf(s.Id)))],
            Campaigns = [.. catalog.Campaigns.Select(c => c with
            {
                Chapters = [.. c.Chapters.Select(ch => ch with { Scenario = Apply(ch.Scenario, textsOf(ch.Scenario.Id)) })],
            })],
        };
    }

    /// <summary>Слой сценария <paramref name="id"/> на <paramref name="language"/> из каталогов сценариев по порядку (база, затем
    /// моды): совпавший ключ берётся из последнего слоя. Файла нет ни в одном — <see cref="None"/>.</summary>
    /// <exception cref="InvalidDataException">Файл не читается, не перевод сценария или перевод другого сценария.</exception>
    public static IReadOnlyDictionary<string, string> Load(IEnumerable<string> scenariosDirs, string language, string id)
    {
        ArgumentNullException.ThrowIfNull(scenariosDirs);
        ArgumentException.ThrowIfNullOrEmpty(language);
        ArgumentException.ThrowIfNullOrEmpty(id);
        Dictionary<string, string>? result = null;
        foreach (string dir in scenariosDirs)
        {
            string path = PathOf(dir, language, id);
            if (!File.Exists(path)) continue;
            var file = Read(path);
            if (!file.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{path}: перевод сценария «{file.Id}», а не «{id}»");
            result ??= new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, text) in file.Texts) result[key] = text;
        }
        return result ?? None;
    }

    /// <summary>Файл перевода: id сценария, язык, откуда текст (<c>Source</c>: «Руссобит-М», «машинный перевод»…) и тексты в порядке файла.</summary>
    public sealed record LangFile(string Id, string Language, string Source, IReadOnlyList<KeyValuePair<string, string>> Texts);

    /// <exception cref="InvalidDataException">Не перевод сценария или версия новее, чем умеет сборка.</exception>
    public static LangFile Read(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (Str(root, "Format") != Format) throw new InvalidDataException($"{path}: не перевод сценария");
            if (root.TryGetProperty("Version", out var v) && v.GetInt32() > Version)
                throw new InvalidDataException($"{path}: версия {v.GetInt32()}, сборка знает до {Version}");
            var texts = root.TryGetProperty("Texts", out var t) && t.ValueKind == JsonValueKind.Object
                ? t.EnumerateObject().Select(p => new KeyValuePair<string, string>(p.Name, p.Value.GetString() ?? "")).ToList()
                : throw new InvalidDataException($"{path}: нет Texts");
            return new LangFile(Str(root, "Id"), Str(root, "Language"), Str(root, "Source"), texts);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"{path}: {e.Message}", e);
        }
    }

    /// <summary>Записать файл перевода (UTF-8, отступы, кириллица без экранирования, перевод строки <c>\n</c>) — тексты в данном порядке.</summary>
    public static void Write(string path, LangFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, WriteOptions))
        {
            w.WriteStartObject();
            w.WriteString("Format", Format);
            w.WriteNumber("Version", Version);
            w.WriteString("Id", file.Id);
            w.WriteString("Language", file.Language);
            w.WriteString("Source", file.Source);
            w.WriteStartObject("Texts");
            foreach (var (key, text) in file.Texts) w.WriteString(key, text);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        File.WriteAllText(path, System.Text.Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n") + "\n");
    }

    private static string? EffectText(EventEffect effect) => effect switch
    {
        PopupEffect p => p.Text,
        ChangeObjectiveEffect o => o.Text,
        _ => null,
    };

    private static EventEffect WithText(EventEffect effect, string? text) => string.IsNullOrEmpty(text) ? effect : effect switch
    {
        PopupEffect p => p with { Text = text },
        ChangeObjectiveEffect o => o with { Text = text },
        _ => effect,
    };

    private static string Pick(IReadOnlyDictionary<string, string> texts, string key, string original) =>
        texts.TryGetValue(key, out var text) && text.Length > 0 ? text : original;

    private static KeyValuePair<string, string> Pair(string key, string text) => new(key, text);

    private static IEnumerable<KeyValuePair<string, string>> NonEmpty(IEnumerable<KeyValuePair<string, string>> texts) =>
        texts.Where(p => !string.IsNullOrWhiteSpace(p.Value));

    private static string Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
