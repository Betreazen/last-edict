using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LastEdict.Data.Game;

/// <summary>Лорд расы: тип (<c>Llord</c>: 0 — маг, 1 — воин, 2 — вор/дипломат), имя и описание — id строк.</summary>
public sealed record LordInfo(string Id, int Category, string NameText, string DescText);

/// <summary>Раса: тип (<c>LRaceCategory</c>: 0 Империя, 1 Нежить, 2 Легионы, 3 Кланы, 4 нейтралы, 5 эльфы),
/// id в таблицах игры, код в именах графики (<c>HU</c>, <c>UN</c>, <c>HE</c>, <c>DW</c>, <c>EL</c>), имя (id строки),
/// играбельна ли и её лорды.</summary>
public sealed record RaceInfo
{
    public required int Type { get; init; }
    public required string Id { get; init; }
    public required string Code { get; init; }
    public required string NameText { get; init; }
    public bool Playable { get; init; }

    /// <summary>Полное имя расы в именах графики (<c>HUMAN</c> в <c>DLG_CHOOSE_LORD_HUMAN_BG</c>).</summary>
    public string Art { get; init; } = "";

    /// <summary>Ролик брифинга сценария за эту расу (имя из описи роликов).</summary>
    public string BriefingVideo { get; init; } = "";

    /// <summary>Строка полного имени расы в окне выбора расы (<c>TXT_RACE_DESC</c> по шаблону <c>X005TA0484</c>, <c>0x4ee08f</c>):
    /// «The Empire», «The Elven Alliance»…; пусто — «Not available» (<c>X005TA0479</c>).</summary>
    public string TitleText { get; init; } = "";

    public IReadOnlyList<LordInfo> Lords { get; init; } = [];
}

/// <summary>Сценарий одиночной игры: то, что показывают выбор сценария и брифинг. <see cref="Id"/> — имя файла
/// без расширения (устойчивый ключ, по нему мод заменяет сценарий), <see cref="File"/> — путь от каталога сценариев.
/// <see cref="Races"/> — типы рас игроков без нейтралов, в порядке игроков в файле.</summary>
public sealed record ScenarioInfo
{
    public required string Id { get; init; }
    public required string File { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string Briefing { get; init; } = "";
    public string BriefingLong { get; init; } = "";
    public int MapSize { get; init; }
    public int Difficulty { get; init; }
    public int SuggestedLevel { get; init; }
    public string Designer { get; init; } = "";
    public IReadOnlyList<int> Races { get; init; } = [];

    /// <summary>Текст эпилога при победе (<c>DEBUNKW…DEBUNKW5</c>, <c>CScenarioInfo +0x18</c>) и поражении (<c>DEBUNKL</c>,
    /// <c>+0x1C</c>) — экран <c>DLG_END_WIN_LOSE_</c> (<c>docs/scenario-events.md</c> §4).</summary>
    public string WinText { get; init; } = "";

    public string LoseText { get; init; } = "";

    /// <summary>Ролик эпилога при победе и поражении (<c>Campaign\CScen.dbf</c>: <c>SMK_WIN</c>, <c>SMK_LOSE</c>).</summary>
    public string WinVideo { get; init; } = "";

    public string LoseVideo { get; init; } = "";

    /// <summary>Звук победы записи эпилога (<c>CScen.WAV_WIN</c>, дорожка каталога <c>Music</c>): играет экран эпилога
    /// (<see cref="LastEdict.Data.Play.GameEnd.EpilogueSound"/>).</summary>
    public string WinSound { get; init; } = "";

    /// <summary>Запись эпилога в <c>CScen.dbf</c> найдена (кампания сценария есть в <c>CCamp.dbf</c>, <c>0x669464</c>); нет — у эпилога нет
    /// ни ролика, ни текста, ни звука (<c>0x4bc78f</c>, ранг 1).</summary>
    public bool HasEnding { get; init; } = true;
}

/// <summary>Глава кампании: номер (<c>CScen.SCEN_NO</c>), сценарий главы, ролик брифинга (<c>SMK_BRIEF</c>), звук победы
/// (<c>WAV_WIN</c>) и музыка брифа (<see cref="BriefingMusic"/>).</summary>
/// <param name="BriefingMusic">Своя музыка брифа главы — озвучка из <c>Briefing</c> (опись роликов, звук без картинки), играет один раз;
/// null — общая <c>GENBRIEF</c> по кругу. У движка (<c>CBriefingInterf</c> <c>0x48091e</c>): сага сценария из <c>CCamp</c> с
/// <c>PLAY_RACE</c> = <c>L_ELF</c> (<c>[0x8372a8]</c>) — <c>"briefel"</c> + номер главы в списке саги с 1 (<c>0x480a71</c>, папка
/// <c>Briefing</c> <c>0x61a2cf</c>, без повтора); иначе <c>GENBRIEF</c> (<c>0x480b36</c>, папка <c>Music</c>, по кругу). Решение —
/// при импорте (<see cref="GameImport.Campaigns"/>), мод может задать свою.</param>
public sealed record ChapterInfo(int Number, ScenarioInfo Scenario, string BriefingVideo = "", string WinSound = "", string? BriefingMusic = null);

/// <summary>Кампания (сага) игры — строка <c>Campaign\CCamp.dbf</c>: id, раса игрока (<c>PLAY_RACE</c>, тип расы), имя и описание
/// (id строк), финальный ролик (<c>CONCLUSION</c>), можно ли брать героя из файла (<c>CANIMPORT</c>), главы по порядку.</summary>
public sealed record CampaignInfo
{
    public required string Id { get; init; }
    public int Race { get; init; }
    public string NameText { get; init; } = "";
    public string DescText { get; init; } = "";
    public string Conclusion { get; init; } = "";
    public bool CanImport { get; init; }
    public IReadOnlyList<ChapterInfo> Chapters { get; init; } = [];

    /// <summary>Своя кампания (<c>Campaign\*.csg</c>, переведённая в <c>user://campaigns</c>): имя, описание и автор — текстом
    /// (<c>HEADER.CPN</c>), уровень лидера (<c>+0x14C</c> — подсказка окна импорта героя).</summary>
    public bool Custom { get; init; }

    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Author { get; init; } = "";
    public int LeaderLevel { get; init; }
}

/// <summary>Каталог игры для меню: расы с лордами и сценарии. Данные порта (<c>data/game/*.json</c>), собранные
/// импортом из таблиц и файлов сценариев игры (<c>tools/GameImport</c>); в рантайме порт форматов игры не читает.</summary>
public sealed record GameCatalog
{
    public IReadOnlyList<RaceInfo> Races { get; init; } = [];
    public IReadOnlyList<ScenarioInfo> Scenarios { get; init; } = [];

    /// <summary>Саги (<c>game/campaigns.json</c>); их главы в <see cref="Scenarios"/> не входят — это не квесты.</summary>
    public IReadOnlyList<CampaignInfo> Campaigns { get; init; } = [];

    /// <summary>Сценарий по id: квест или глава саги; нет — null.</summary>
    public ScenarioInfo? Scenario(string id) =>
        Scenarios.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? Campaigns.SelectMany(c => c.Chapters).Select(c => c.Scenario).FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Сага и глава, к которой относится сценарий <paramref name="id"/>; не глава — null.</summary>
    public (CampaignInfo Campaign, ChapterInfo Chapter)? ChapterOf(string id) =>
        Campaigns.SelectMany(c => c.Chapters.Select(ch => (c, ch)))
                 .Where(p => p.ch.Scenario.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                 .Select(p => ((CampaignInfo, ChapterInfo)?)p).FirstOrDefault();

    /// <summary>Музыка брифа сценария <paramref name="scenarioId"/>, если она своя (<see cref="ChapterInfo.BriefingMusic"/>); иначе null.</summary>
    public string? BriefingMusic(string? scenarioId) =>
        scenarioId is null ? null : ChapterOf(scenarioId)?.Chapter.BriefingMusic;

    public RaceInfo Race(int type) => Races.FirstOrDefault(r => r.Type == type)
                                      ?? throw new KeyNotFoundException($"нет расы типа {type}");

    /// <summary>Что в каталоге не сходится: сценарий ссылается на расу, которой нет, у играбельной расы нет лорда
    /// какого-то из трёх типов. Логика меню на это рассчитывает, поэтому <see cref="Content.GameContent"/> проверяет
    /// каталог при загрузке и не принимает несогласованный — лучше сразу, с названием, чем посреди меню.</summary>
    public IReadOnlyList<string> Problems()
    {
        var types = Races.Select(r => r.Type).ToHashSet();
        var problems = Scenarios.SelectMany(s => s.Races.Where(t => !types.Contains(t))
                                                  .Select(t => $"сценарий {s.Id}: нет расы {t}")).ToList();
        foreach (var race in Races.Where(r => r.Playable))
            for (int category = 0; category <= 2; category++)
                if (race.Lords.Count(l => l.Category == category) != 1)
                    problems.Add($"раса {race.Type}: лордов типа {category} не ровно один");
        return problems;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToJson<T>(IReadOnlyList<T> items) =>
        JsonSerializer.Serialize(items, Json).ReplaceLineEndings("\n") + "\n";

    public static IReadOnlyList<T> FromJson<T>(string json) =>
        JsonSerializer.Deserialize<List<T>>(json, Json) ?? throw new FormatException($"пустой список {typeof(T).Name}");

    /// <summary>Каталог из <paramref name="dir"/> (<c>races.json</c>, <c>scenarios.json</c>, <c>campaigns.json</c>); нет файла — пусто.</summary>
    public static GameCatalog Load(string dir) => new()
    {
        Races = Read<RaceInfo>(Path.Combine(dir, "races.json")),
        Scenarios = Read<ScenarioInfo>(Path.Combine(dir, "scenarios.json")),
        Campaigns = Read<CampaignInfo>(Path.Combine(dir, "campaigns.json")),
    };

    /// <summary>Наложить мод: раса опознаётся по типу, сценарий — по <c>Id</c>; совпавшее заменяется целиком,
    /// новое добавляется в конец.</summary>
    public static GameCatalog Merge(GameCatalog baseSet, GameCatalog overlay)
    {
        ArgumentNullException.ThrowIfNull(baseSet);
        ArgumentNullException.ThrowIfNull(overlay);
        return new GameCatalog
        {
            Races = Overlay(baseSet.Races, overlay.Races, r => r.Type.ToString(CultureInfo.InvariantCulture)),
            Scenarios = Overlay(baseSet.Scenarios, overlay.Scenarios, s => s.Id),
            Campaigns = Overlay(baseSet.Campaigns, overlay.Campaigns, c => c.Id),
        };
    }

    private static IReadOnlyList<T> Read<T>(string path) => File.Exists(path) ? FromJson<T>(File.ReadAllText(path)) : [];

    private static IReadOnlyList<T> Overlay<T>(IReadOnlyList<T> baseSet, IReadOnlyList<T> overlay, Func<T, string> key)
    {
        var over = overlay.ToDictionary(key, StringComparer.OrdinalIgnoreCase);
        var result = baseSet.Select(b => over.Remove(key(b), out var o) ? o : b).ToList();
        result.AddRange(overlay.Where(o => over.ContainsKey(key(o))));
        return result;
    }
}
