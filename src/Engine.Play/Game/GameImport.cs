using LastEdict.Data.Dbf;
using LastEdict.Data.Sg;
using KSg = Kaitai.Sg;

namespace LastEdict.Data.Game;

/// <summary>Сборка каталога меню из копии игры: расы и лорды — из <c>Globals/Grace.dbf</c> и <c>Glord.dbf</c>,
/// сценарии — из <c>Exports/*.sg</c>. Запускается инструментом <c>tools/GameImport</c>; порт в рантайме читает уже
/// <c>data/game/*.json</c>.</summary>
public static class GameImport
{
    /// <summary>Код расы в именах графики и роликов игры (<c>FACE_HU_0</c>, <c>GOD_HU2UN</c>,
    /// <c>DLG_CHOOSE_LORD_UNDEAD_BG</c> — последнее по полному имени, см. <see cref="RaceInfo.Code"/>). В таблицах
    /// его нет: это соглашение имён файлов, поэтому таблица здесь, у импорта, а не в логике экранов.</summary>
    private static readonly Dictionary<int, string> Codes = new()
    {
        [0] = "HU", [1] = "UN", [2] = "HE", [3] = "DW", [4] = "NE", [5] = "EL",
    };

    /// <summary>Полное имя расы в именах графики (<c>DLG_CHOOSE_LORD_UNDEAD_BG</c>) и ролик брифинга сценария за эту
    /// расу (<c>Briefing/brief2h.bik</c> — Империя, <c>brief2u</c> — Нежить, <c>brief2l</c> — Легионы, <c>brief2d</c> —
    /// Кланы, <c>brief3el</c> — эльфы: буква расы в имени файла; вывод по именам, не по игре).</summary>
    private static readonly Dictionary<int, (string Art, string Briefing)> Art = new()
    {
        [0] = ("HUMAN", "BRIEF2H"), [1] = ("UNDEAD", "BRIEF2U"), [2] = ("HERETIC", "BRIEF2L"),
        [3] = ("DWARF", "BRIEF2D"), [5] = ("ELF", "BRIEF3EL"),
    };

    /// <summary>Полное имя расы в окне выбора расы (<c>0x4ee08f</c>: строки зашиты в коде по типу расы, в таблицах их нет).</summary>
    private static readonly Dictionary<int, string> Titles = new()
    {
        [0] = "X005TA0480", [1] = "X005TA0481", [3] = "X005TA0482", [2] = "X005TA0483", [5] = "X160TA0003",
    };

    private static readonly HashSet<string> ConclusionPlayed = ["C000CC0002", "C000CC0003", "C000CC0004", "C000CC0005"];

    private const int Neutral = LastEdict.Rules.Map.LegacyRaces.Neutral;

    public static IReadOnlyList<RaceInfo> Races(string globalsDir)
    {
        var lords = GameTables.Read(globalsDir, "Glord.dbf").Rows
            .Select(r => (Race: Str(r, "RACE_ID"), Lord: new LordInfo(Str(r, "LORD_ID"), Int(r, "CATEGORY"),
                                                                      Str(r, "NAME_TXT"), Str(r, "DESC_TXT"))))
            .ToList();
        return [.. GameTables.Read(globalsDir, "Grace.dbf").Rows
            .Select(r =>
            {
                string id = Str(r, "RACE_ID");
                int type = Int(r, "RACE_TYPE");
                return new RaceInfo
                {
                    Type = type, Id = id, Code = Codes.GetValueOrDefault(type, "??"), NameText = Str(r, "NAME_TXT"),
                    Playable = r.GetBool("PLAYABLE"),
                    Art = Art.TryGetValue(type, out var art) ? art.Art : "",
                    BriefingVideo = Art.TryGetValue(type, out var brief) ? brief.Briefing : "",
                    TitleText = Titles.GetValueOrDefault(type, ""),
                    Lords = [.. lords.Where(l => l.Race.Equals(id, StringComparison.OrdinalIgnoreCase))
                                     .Select(l => l.Lord).OrderBy(l => l.Category)],
                };
            })
            .OrderBy(r => r.Type)];
    }

    /// <summary>Сценарии каталога <paramref name="exportsDir"/> по имени файла. Расы — по игрокам сценария
    /// (<c>MidPlayer.RACE_ID</c> → <c>Grace.RACE_TYPE</c>), без нейтралов: у старых файлов в заголовке списка рас
    /// нет, а игроки есть у всех.</summary>
    /// <param name="campaignDir">Каталог <c>Campaign</c> игры (<c>CScen.dbf</c>, <c>CCamp.dbf</c>) — ролики эпилога; нет — без роликов.</param>
    public static IReadOnlyList<ScenarioInfo> Scenarios(string exportsDir, IReadOnlyList<RaceInfo> races, string campaignDir = "")
    {
        var typeById = races.ToDictionary(r => r.Id, r => r.Type, StringComparer.OrdinalIgnoreCase);
        var endings = new Endings(campaignDir);
        return [.. Directory.GetFiles(exportsDir, "*.sg")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(path => Scenario(SgFile.Open(path), Path.GetFileNameWithoutExtension(path),
                                     Path.GetRelativePath(exportsDir, path).Replace('\\', '/'), typeById, endings.Of))];
    }

    /// <summary>Запись эпилога <c>Campaign\CScen.dbf</c>: ролики <c>SMK_WIN</c>/<c>SMK_LOSE</c> и звук победы <c>WAV_WIN</c> (поле записи
    /// <c>+0x1F</c>, чтение <c>0x6689c5</c>).</summary>
    private sealed record Ending(string Win, string Lose, string WinSound = "");

    /// <summary>
    /// Поиск записи эпилога, как у движка (ролик <c>0x4bc4fa</c>, текст <c>0x4bc6f1</c> и звук <c>0x4bcba0</c> ищут одинаково; ранг 1):
    /// кампания сценария (<c>CScenarioInfo +0x5C</c>) — строка <c>CCamp.dbf</c> (<c>0x669464</c>), нет её — эпилога нет; глава
    /// (<c>0x668d69</c>): у кампании <c>C000CC0001</c> — последняя её запись без сравнения сценария (<c>0x668d2c</c> → <c>0x669504</c>;
    /// у всех 33 сценариев <c>Exports</c> эта кампания — они получают <c>S021SC0000</c>), у прочих — запись с <c>SCEN_ID</c> карты
    /// (<c>0x6699e4</c>). Колонку <c>SKIRMISH</c> движок не читает.
    /// </summary>
    private sealed class Endings
    {
        private const string Quests = "C000CC0001";
        private readonly HashSet<string> _campaigns = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string Campaign, string Scenario), Ending> _chapters = [];
        private readonly Dictionary<string, Ending> _last = new(StringComparer.OrdinalIgnoreCase);

        public Endings(string campaignDir)
        {
            if (campaignDir.Length == 0 || !Directory.Exists(campaignDir)) return;
            _campaigns.UnionWith(GameTables.Read(campaignDir, "CCamp.dbf").Rows.Select(r => Str(r, "CAMP_ID")));
            foreach (var r in GameTables.Read(campaignDir, "CScen.dbf").Rows)
            {
                var ending = new Ending(Str(r, "SMK_WIN"), Str(r, "SMK_LOSE"), Str(r, "WAV_WIN"));
                _chapters.TryAdd((Str(r, "CAMP_ID"), Str(r, "SCEN_ID")), ending);
                _last[Str(r, "CAMP_ID")] = ending;
            }
        }

        /// <summary>Запись для сценария (id карты — <c>INFO_ID</c> с типом <c>SC</c> вместо <c>IF</c>); null — эпилога нет.</summary>
        public Ending? Of(KSg.ScenarioInfo info)
        {
            string campaign = (info.Campaign?.String ?? "").Trim().ToUpperInvariant();
            if (!_campaigns.Contains(campaign)) return null;
            if (campaign == Quests) return _last.GetValueOrDefault(campaign);
            string infoId = (info.InfoId?.String ?? "").Trim().ToUpperInvariant();
            string scenario = infoId.Length == 10 ? string.Concat(infoId.AsSpan(0, 4), "SC", infoId.AsSpan(6)) : infoId;
            return _chapters.GetValueOrDefault((campaign, scenario));
        }
    }

    /// <summary>Саги игры (<c>Campaign\CCamp.dbf</c> без «одиночных» <c>SKIRMISH</c>) с главами из <c>CScen.dbf</c> по <c>SCEN_NO</c>;
    /// сценарий главы — из таблиц <c>Scens</c> (<see cref="SagaSg"/>), id — <c>SCEN_ID</c>. Нет каталогов — пусто.</summary>
    public static IReadOnlyList<CampaignInfo> Campaigns(string gameDir, IReadOnlyList<RaceInfo> races)
    {
        string campaignDir = Path.Combine(gameDir, "Campaign"), scensDir = Path.Combine(gameDir, "Scens");
        if (!Directory.Exists(campaignDir) || !Directory.Exists(scensDir)) return [];
        var typeById = races.ToDictionary(r => r.Id, r => r.Type, StringComparer.OrdinalIgnoreCase);
        var tables = ScensTables.Load(scensDir);
        var chapters = GameTables.Read(campaignDir, "CScen.dbf").Rows.ToLookup(r => Str(r, "CAMP_ID"));
        return [.. GameTables.Read(campaignDir, "CCamp.dbf").Rows.Where(r => !r.GetBool("SKIRMISH")).Select(r => new CampaignInfo
        {
            Id = Str(r, "CAMP_ID"),
            Race = Int(r, "PLAY_RACE"),
            NameText = Str(r, "NAME_ID"),
            DescText = Str(r, "DESC_ID"),
            // Финальный ролик движок играет только сагам C000CC0002–0005 (0x404f5b, список зашит): у эльфов и «Стражей Света»
            // после последней главы — сразу меню, хотя CONCLUSION в таблице есть.
            Conclusion = ConclusionPlayed.Contains(Str(r, "CAMP_ID")) ? Str(r, "CONCLUSION") : "",
            CanImport = r.GetBool("CANIMPORT"),
            Chapters = [.. chapters[Str(r, "CAMP_ID")].OrderBy(c => Int(c, "SCEN_NO")).Select((c, index) =>
            {
                string id = c.GetString("SCEN_ID").Trim();
                var file = SgFile.Load(SagaSg.Build(tables, id));
                // Запись главы — сама строка CScen (кампания саги и SCEN_ID главы): у эльфов ELFWIN, у последней главы — CMP3EL.
                var ending = new Ending(Str(c, "SMK_WIN"), Str(c, "SMK_LOSE"), Str(c, "WAV_WIN"));
                // Музыка брифа (0x48091e): у саги эльфов — озвучка "briefel" + номер главы в списке саги с 1, у прочих — общая GENBRIEF.
                string? music = Int(r, "PLAY_RACE") == LastEdict.Rules.Map.LegacyRaces.Elf ? $"BRIEFEL{index + 1}" : null;
                return new ChapterInfo(Int(c, "SCEN_NO"), Scenario(file, id, "", typeById, _ => ending), Str(c, "SMK_BRIEF"), Str(c, "WAV_WIN"),
                                       music);
            })],
        })];
    }

    /// <summary>Сценарий уже разобранного <c>.sg</c> (глава своей кампании) без роликов эпилога; текст эпилога — из файла (ранг 2:
    /// попадает ли своя кампания в таблицу кампаний движка, не разобрано).</summary>
    public static ScenarioInfo ScenarioOf(SgFile sg, string id, string file, IReadOnlyList<RaceInfo> races) =>
        Scenario(sg, id, file, races.ToDictionary(r => r.Id, r => r.Type, StringComparer.OrdinalIgnoreCase), _ => new Ending("", ""));

    private static ScenarioInfo Scenario(SgFile sg, string id, string file, Dictionary<string, int> typeById,
                                         Func<KSg.ScenarioInfo, Ending?> endingOf)
    {
        string path = id;
        var objects = sg.Raw.Objects.Select(o => o.Data).ToList();
        var info = objects.OfType<KSg.ScenarioInfo>().Single();
        var races = objects.OfType<KSg.MidPlayer>()
            .Select(p => typeById.TryGetValue(p.RaceId.String, out int t)
                         || typeById.TryGetValue(LastEdict.Data.Content.LegacyIds.Base.IdOf(p.RaceId.String) ?? "", out t) ? t   // каталог — по id содержимого
                         : throw new InvalidDataException($"{path}: неизвестная раса {p.RaceId.String}"))
            .Where(t => t != Neutral)
            .Distinct()
            .ToList();
        var ending = endingOf(info);
        string[] longParts = [info.Brieflong1.String, info.Brieflong2.String, info.Brieflong3.String,
                              info.Brieflong4.String, info.Brieflong5.String];
        return new ScenarioInfo
        {
            Id = id,
            File = file,
            Name = info.Name.String,
            Description = info.Desc.String,
            Briefing = info.Briefing.String,
            BriefingLong = string.Concat(longParts),
            MapSize = (int)info.MapSize.Value,
            Difficulty = (int)info.Diffscen.Value,
            SuggestedLevel = (int)info.SuggLvl.Value,
            Designer = info.Creator.String,
            Races = races,
            // Старые файлы несут не все куски текста — недостающие пусты.
            WinText = string.Concat(info.Debunkw?.String, info.Debunkw2?.String, info.Debunkw3?.String, info.Debunkw4?.String,
                                    info.Debunkw5?.String),
            LoseText = info.Debunkl?.String ?? "",
            WinVideo = ending?.Win ?? "",
            LoseVideo = ending?.Lose ?? "",
            WinSound = ending?.WinSound ?? "",
            HasEnding = ending is not null,
        };
    }

    private static string Str(DbfRow row, string column) => row.GetString(column).Trim().ToUpperInvariant();

    private static int Int(DbfRow row, string column) => row.GetInt(column);
}
