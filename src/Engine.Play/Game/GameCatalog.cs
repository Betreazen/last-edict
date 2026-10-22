using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LastEdict.Data.Content;

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

    /// <summary>Место в круге выбора расы (<c>RaceLook.ChoiceOrder</c>); null — расы в круге нет.</summary>
    public int? ChoiceOrder { get; init; }

    /// <summary>Вода и светлячки окна выбора расы (анимации интерфейса); null — нет.</summary>
    public string? ChoiceWater { get; init; }

    public string? ChoiceFireflies { get; init; }

    /// <summary>Сага расы (кампания «Новой саги»); null — нет.</summary>
    public string? Saga { get; init; }

    /// <summary>Раса оригинала (есть id оригинала); раса мода — нет. Род расы оригинала единственный — по нему переводятся сценарии и
    /// герои импорта, где раса записана родом.</summary>
    public bool Original { get; init; } = true;

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

    /// <summary>Расы игроков по id (Э5: меню опознаёт расу по id — раса мода может быть того же рода, что и раса оригинала). У сценариев
    /// импорта — из <see cref="Races"/> по роду (<see cref="GameCatalog.WithRaces"/>), сценарий мода задаёт их сам.</summary>
    public IReadOnlyList<string> RaceIds { get; init; } = [];

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

    /// <summary>Раса игрока саги по id; у саг импорта — по роду <see cref="Race"/> (<see cref="GameCatalog.WithRaces"/>).</summary>
    public string RaceId { get; init; } = "";

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

    /// <summary>Раса по роду (<c>LRaceCategory</c>) — для данных, где раса записана родом (герои <c>.ldr</c>, свои кампании): раса оригинала
    /// этого рода, иначе первая такого рода.</summary>
    public RaceInfo Race(int type) => Races.Where(r => r.Type == type).OrderBy(r => r.Original ? 0 : 1).FirstOrDefault()
                                      ?? throw new KeyNotFoundException($"нет расы типа {type}");

    /// <summary>Раса по id.</summary>
    public RaceInfo Race(string id) => Races.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                                       ?? throw new KeyNotFoundException($"нет расы {id}");

    /// <summary>Раса игрока меню: выбранная <paramref name="id"/>, а не выбрана (пусто — меню её ещё не ставило) или её нет — первая
    /// играбельная по кругу выбора (Империя у рас оригинала, как прежний род 0 по умолчанию).</summary>
    public RaceInfo PlayerRace(string? id) =>
        Races.FirstOrDefault(r => r.Id.Equals(id ?? "", StringComparison.OrdinalIgnoreCase))
        ?? Races.Where(r => r.Playable).OrderBy(r => r.ChoiceOrder ?? int.MaxValue).ThenBy(r => r.Original ? 0 : 1).FirstOrDefault()
        ?? throw new KeyNotFoundException("в каталоге нет играбельной расы");

    /// <summary>Каталог с расами <paramref name="races"/> (из содержимого — <see cref="MenuRaces"/>): у сценариев и саг без id рас они
    /// выводятся из рода — расой оригинала этого рода.</summary>
    public GameCatalog WithRaces(IReadOnlyList<RaceInfo> races)
    {
        ArgumentNullException.ThrowIfNull(races);
        var byType = races.GroupBy(r => r.Type).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Original ? 0 : 1).First().Id);
        string IdOf(int type) => byType.GetValueOrDefault(type, "");
        ScenarioInfo Resolve(ScenarioInfo s) =>
            s.RaceIds.Count > 0 && s.RaceIds.All(id => id.Length > 0) ? s : s with { RaceIds = [.. s.Races.Select(IdOf)] };
        return this with
        {
            Races = races,
            Scenarios = [.. Scenarios.Select(Resolve)],
            Campaigns = [.. Campaigns.Select(c => c with
            {
                RaceId = c.RaceId.Length > 0 ? c.RaceId : IdOf(c.Race),
                Chapters = [.. c.Chapters.Select(ch => ch with { Scenario = Resolve(ch.Scenario) })],
            })],
        };
    }

    /// <summary>Что в каталоге не сходится: сценарий ссылается на расу, которой нет, у играбельной расы нет лорда
    /// какого-то из трёх типов. Логика меню на это рассчитывает, поэтому <see cref="Content.GameContent"/> проверяет
    /// каталог при загрузке и не принимает несогласованный — лучше сразу, с названием, чем посреди меню.</summary>
    public IReadOnlyList<string> Problems()
    {
        var ids = Races.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Сценарий без id рас — по роду (WithRaces): рода нет среди рас — пустой id, это и есть «нет расы».
        var problems = WithRaces(Races).Scenarios.SelectMany(s => s.RaceIds.Where(id => !ids.Contains(id))
                                                  .Select(id => $"сценарий {s.Id}: нет расы {(id.Length > 0 ? id : "рода " + string.Join(",", s.Races))}"))
                                .ToList();
        foreach (var race in Races.Where(r => r.Playable))
            for (int category = 0; category <= 2; category++)
                if (race.Lords.Count(l => l.Category == category) != 1)
                    problems.Add($"раса {race.Id}: лордов типа {category} не ровно один");
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

    /// <summary>Каталог из <paramref name="dir"/> (<c>races.json</c>, <c>scenarios.json</c>, <c>campaigns.json</c>); нет файла — пусто. Расы и
    /// лорды — по id содержимого: файлы импорта пишут id оригинала, они переводятся здесь (<see cref="LegacyIds.Base"/>).</summary>
    public static GameCatalog Load(string dir)
    {
        var races = Read<RaceInfo>(Path.Combine(dir, "races.json")).Select(WithContentIds).ToList();
        return new GameCatalog
        {
            Races = races,
            Scenarios = Read<ScenarioInfo>(Path.Combine(dir, "scenarios.json")),
            Campaigns = Read<CampaignInfo>(Path.Combine(dir, "campaigns.json")),
        }.WithRaces(races);
    }

    /// <summary>Наложить мод: раса и сценарий опознаются по <c>Id</c>; совпавшее заменяется целиком,
    /// новое добавляется в конец.</summary>
    public static GameCatalog Merge(GameCatalog baseSet, GameCatalog overlay)
    {
        ArgumentNullException.ThrowIfNull(baseSet);
        ArgumentNullException.ThrowIfNull(overlay);
        var merged = new GameCatalog
        {
            Races = Overlay(baseSet.Races, overlay.Races, r => r.Id),
            Scenarios = Overlay(baseSet.Scenarios, overlay.Scenarios, s => s.Id),
            Campaigns = Overlay(baseSet.Campaigns, overlay.Campaigns, c => c.Id),
        };
        return merged.WithRaces(merged.Races);
    }

    /// <summary>Раса файла импорта: id содержимого и вид расы оригинала по роду (круг выбора, вода, сага — <see cref="RaceLooks.Legacy"/>).</summary>
    private static RaceInfo WithContentIds(RaceInfo race)
    {
        var look = Content.Schema.RaceLooks.Legacy((Content.Schema.RaceKind)race.Type, race.Id);
        return race with
        {
            Id = LegacyIds.Base.IdOf(race.Id) ?? race.Id,
            Lords = [.. race.Lords.Select(l => l with { Id = LegacyIds.Base.IdOf(l.Id) ?? l.Id })],
            ChoiceOrder = race.ChoiceOrder ?? (race.Playable ? look.ChoiceOrder : null),
            ChoiceWater = race.ChoiceWater ?? look.ChoiceWater,
            ChoiceFireflies = race.ChoiceFireflies ?? look.ChoiceFireflies,
            Saga = race.Saga ?? look.Saga,
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
