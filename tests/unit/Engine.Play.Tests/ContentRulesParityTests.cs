using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using D2.Data.Battle;
using D2.Data.Dbf;
using D2.Data.Magic;
using D2.Data.Map;
using D2.Data.Play;
using D2.Data.Tests.LegacyLoaders;
using D2.Rules.Map;
using Xunit.Abstractions;

namespace D2.Data.Tests;

/// <summary>
/// Сверка «старая модель = новая» (этап Э2, задача C, <c>docs/content-schema.md</c>): правила, собранные прежними загрузчиками прямо
/// из строк таблиц (<c>LegacyLoaders</c> — их код до схемы), и правила, собранные из содержимого (таблицы → <c>LegacyContent</c> →
/// записи схемы → загрузчики), совпадают целиком — каждая модель, каждое поле, каждый элемент списка по порядку. Таблицы — выгрузка
/// <c>data/rules</c> и <c>.dbf</c> копии игры (<c>GamePaths.Globals</c>).
/// <para>Сравнение — по JSON моделей: ключи словарей упорядочены, строки без учёта регистра (id правил регистронезависимы: в таблицах
/// одна ссылка пишется <c>g000LR0001</c>, другая на того же лорда — <c>g000lr0001</c>), «пустая ссылка» оригинала <c>g000000000</c> —
/// как пустая строка (правила и так их не различают). Различаться может только то, что схема сознательно не несёт: висячие строки
/// подчинённых таблиц (хозяина нет) — они перечислены в <see cref="Dangling"/> и проверяются отдельно.</para>
/// </summary>
public class ContentRulesParityTests(ITestOutputHelper output)
{
    public static TheoryData<string> Dirs => ["rules", "globals"];

    private static string? DirOf(string which) => which switch
    {
        "rules" => File.Exists(Path.Combine(ImportedData.Dir, "rules", "Gunits.json")) ? Path.Combine(ImportedData.Dir, "rules") : null,
        _ => GamePaths.IsAvailable ? GamePaths.Globals : null,
    };

    [SkippableTheory, MemberData(nameof(Dirs))]
    public void RuleModels_FromContent_EqualLegacyTableModels(string which)
    {
        string? dir = DirOf(which);
        Skip.If(dir is null, $"нет таблиц «{which}»");
        var content = D2.Data.Content.LegacyContent.Import(dir);
        var checks = new (string Name, object Old, object New)[]
        {
            ("GameRules", LegacyRulesLoader.Load(dir), RulesLoader.Load(content)),
            ("RulesExtras", Strip(LegacyRulesLoader.LoadExtras(dir), "immunityBySource", "immunityByClass", "transformations"),
                RulesLoader.LoadExtras(content)),
            ("ModifierTables", Strip(LegacyModifierLoader.Load(dir), "orphans"), ModifierLoader.Load(content)),
            ("EconomyRules", LegacyEconomyLoader.Load(dir), EconomyLoader.Load(content)),
            ("TreasuryRules", LegacyEconomyLoader.LoadTreasury(dir), EconomyLoader.LoadTreasury(content)),
            ("LandQuotas", LegacyEconomyLoader.LoadLand(dir), EconomyLoader.LoadLand(content)),
            ("MapVariables", LegacyEconomyLoader.LoadVars(dir), EconomyLoader.LoadVars(content)),
            ("DiplomacyRules", LegacyEconomyLoader.LoadDiplomacy(dir), EconomyLoader.LoadDiplomacy(content)),
            ("AiCheatRules", LegacyEconomyLoader.LoadAiCheats(dir), EconomyLoader.LoadAiCheats(content)),
            ("AiWeights", LegacyEconomyLoader.LoadAiWeights(dir), EconomyLoader.LoadAiWeights(content)),
            ("AiLeaderShares", LegacyEconomyLoader.LoadAiLeaderShares(dir), EconomyLoader.LoadAiLeaderShares(content)),
            ("ThiefRules", LegacyEconomyLoader.LoadThieves(dir), EconomyLoader.LoadThieves(content)),
            ("MountainLandmarks", LegacyEconomyLoader.LoadMountainLandmarks(dir), EconomyLoader.LoadMountainLandmarks(content)),
            ("Spells", LegacySpellLoader.Load(dir), SpellLoader.Load(content)),
            ("Items", LegacyItemLoader.Load(dir), ItemLoader.Load(content)),
            ("InnateAbilities", Dangling.WithoutUnits(LegacyEquipLoader.LoadInnateAbilities(dir), LegacyRulesLoader.Load(dir).Units.Keys),
                EquipLoader.LoadInnateAbilities(content)),
            ("LeaderUpgrades", LegacyModifierLoader.LoadLeaderUpgrades(dir), ModifierLoader.LoadLeaderUpgrades(content)),
            ("Lords", LegacyModifierLoader.LoadLords(dir), ModifierLoader.LoadLords(content)),
        };
        foreach (var (name, old, now) in checks) Same(name, old, now);
        output.WriteLine($"{which}: моделей {checks.Length}, все совпали");
    }

    /// <summary><see cref="GameBook"/> целиком: модели загрузчиков и то, что прежний <c>GameBook.Load</c> читал из таблиц сам (лидеры
    /// рас, имена героев, постройки и исследования лордов, бюджеты и пороги ИИ, сообщения ИИ, значки свитков, побочная ветка).</summary>
    [SkippableTheory, MemberData(nameof(Dirs))]
    public void GameBook_FromContent_EqualsLegacyTables(string which)
    {
        string? dir = DirOf(which);
        Skip.If(dir is null, $"нет таблиц «{which}»");
        var book = GameBook.Load(D2.Data.Content.LegacyContent.Import(dir));
        T Rows<T>(string table, Func<IReadOnlyList<DbfRow>, T> f) => f(GameTables.Read(dir, table + ".dbf").Rows);
        const string empty = "g000000000";
        var gvars = Rows("GVars", r => r[0]);
        var gai = Rows("GAI", r => r);
        var legacy = new Dictionary<string, object>
        {
            ["RaceLeaders"] = Rows("Grace", rows => rows.ToDictionary(r => r.GetString("RACE_ID"),
                r => (IReadOnlyList<string>)[.. new[] { "LEADER_1", "LEADER_2", "LEADER_3", "LEADER_4", "NOBLE" }
                    .Select(r.GetString).Where(id => id.Length > 0 && !id.Equals(empty, StringComparison.OrdinalIgnoreCase))])),
            ["LeaderNames"] = Rows("Tleader", rows => rows.Where(r => r.GetBool("SEX_M")).GroupBy(r => r.GetString("RACE_ID"))
                .ToDictionary(g => g.Key, g => g.Select(r => r.GetString("TEXT").Trim()).ToList())),
            ["LordBuildings"] = Rows("GbuiList", rows => rows.GroupBy(r => r.GetString("LORD_ID"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(r => r.GetString("BUILD_ID")).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList())),
            ["AiTacticPurseShare"] = gai.ToDictionary(r => r.GetInt("CATEGORY"), r => r.GetInt("B_ISO")),
            ["AiBudgetBases"] = gai.ToDictionary(r => r.GetInt("CATEGORY"), r => new Dictionary<string, int>
            {
                ["HireUnit"] = r.GetInt("B_UNITS"), ["HireLeader"] = r.GetInt("B_LEADERS"), ["Capital"] = r.GetInt("B_CAP"),
                ["GrowCities"] = r.GetInt("B_CITIES"),
            }),
            ["AiStayMinHp"] = gai.ToDictionary(r => r.GetInt("CATEGORY"), r => r.GetInt("HP_STAYMIN")),
            ["AiHealMinHp"] = gai.ToDictionary(r => r.GetInt("CATEGORY"), r => r.GetInt("HP_HEALMIN")),
            ["SpellAiCategory"] = Rows("Gspells", rows => rows.ToDictionary(r => r.GetString("SPELL_ID"), r => r.GetInt("AI_CAT"))),
            ["LordResearch"] = Rows("GspellR", rows => rows.GroupBy(r => r.GetString("LORD_ID"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(r => new { Item1 = r.GetString("SPELL_ID"), Item2 = Bank.Parse(r.GetString("RESEARCH")) }).ToList())),
            ["AllianceBreak"] = new { Item1 = gvars.GetInt("D_BKA_TURN"), Item2 = gvars.GetInt("D_BKA_CHNC") },
            ["GuardRange"] = gvars.GetInt("GU_RANGE"), ["PartyRange"] = gvars.GetInt("PA_RANGE"), ["LocationRange"] = gvars.GetInt("LO_RANGE"),
            ["SpyDiscovery"] = gvars.GetInt("SPY_DISCOV"),
            ["SpellPower"] = Enumerable.Range(0, 5).Select(i => gvars.GetInt($"SPLPWR_{i}")).ToList(),
            ["StartBankPercent"] = new[] { "E", "A", "H", "V" }.Select(l => gvars.GetInt($"BONUS_{l}")).ToList(),
            ["AiCityArmor"] = Enumerable.Range(1, 5).Select(i => gvars.GetInt($"PROT_{i}")).ToList(),
            ["AiCapitalArmor"] = gvars.GetInt("PROT_CAP"),
        };
        var now = new Dictionary<string, object>
        {
            ["RaceLeaders"] = book.RaceLeaders, ["LeaderNames"] = book.LeaderNames, ["LordBuildings"] = book.LordBuildings,
            ["AiTacticPurseShare"] = book.AiTacticPurseShare,
            ["AiBudgetBases"] = book.AiBudgetBases.ToDictionary(p => p.Key, p => p.Value.ToDictionary(q => q.Key.ToString(), q => q.Value)),
            ["AiStayMinHp"] = book.AiStayMinHp, ["AiHealMinHp"] = book.AiHealMinHp, ["SpellAiCategory"] = book.SpellAiCategory,
            ["LordResearch"] = book.LordResearch, ["AllianceBreak"] = book.AllianceBreak,
            ["GuardRange"] = book.GuardRange, ["PartyRange"] = book.PartyRange, ["LocationRange"] = book.LocationRange,
            ["SpyDiscovery"] = book.SpyDiscovery, ["SpellPower"] = book.SpellPower, ["StartBankPercent"] = book.StartBankPercent,
            ["AiCityArmor"] = book.AiCityArmor, ["AiCapitalArmor"] = book.AiCapitalArmor,
        };
        foreach (var key in legacy.Keys) Same(key, legacy[key], now[key]);

        // Закрытое — через открытые вопросы к книге, по всем ключам таблиц.
        foreach (var lord in Rows("Glord", r => r))
            Assert.Equal(lord.GetInt("CATEGORY"), book.LordCategory(lord.GetString("LORD_ID")));
        foreach (var race in Rows("Grace", r => r))
        {
            string id = race.GetString("RACE_ID");
            var sideshow = Rows("Gunits", rows => rows.FirstOrDefault(u => u.GetInt("UNIT_CAT") == 0 && u.GetInt("BRANCH") == 4
                && u.GetString("RACE_ID").Equals(id, StringComparison.OrdinalIgnoreCase)
                && (u.GetString("PREV_ID").Length == 0 || u.GetString("PREV_ID").Equals(empty, StringComparison.OrdinalIgnoreCase))
                && u.GetString("ENROLL_B").Length > 0 && !u.GetString("ENROLL_B").Equals(empty, StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(sideshow?.GetString("ENROLL_B") ?? "", book.SideshowBuilding(id), StringComparer.OrdinalIgnoreCase);
            foreach (var kind in Enum.GetValues<AiMessage>())
                Assert.Equal(Rows("TAiMsg", rows => rows.Where(m => m.GetString("RACE_ID").Equals(id, StringComparison.OrdinalIgnoreCase)
                                                                   && m.GetInt("MSG_CAT") == (int)kind).Select(m => m.GetString("TEXT").Trim()).ToList()),
                             book.AiMessages(id, kind));
        }
        var raceTypes = Rows("Grace", rows => rows.ToDictionary(r => r.GetString("RACE_ID"), r => r.GetInt("RACE_TYPE"), StringComparer.OrdinalIgnoreCase));
        var research = Rows("GspellR", rows => rows.GroupBy(r => r.GetString("LORD_ID"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(r => r.GetString("SPELL_ID")).ToList(), StringComparer.OrdinalIgnoreCase));
        var spellRace = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var lord in Rows("Glord", r => r))
            if (raceTypes.TryGetValue(lord.GetString("RACE_ID"), out int type) && research.TryGetValue(lord.GetString("LORD_ID"), out var spells))
                foreach (string spell in spells) spellRace.TryAdd(spell, type);
        foreach (var item in book.Items.Values.Where(i => i.Category == D2.Rules.Magic.ItemCategory.Scroll))
        {
            int t = spellRace.TryGetValue(item.SpellId, out int found) ? found : 0;
            Assert.Equal(t switch { 0 => "SCROLLHU", 1 => "SCROLLUN", 2 => "SCROLLHE", 3 => "SCROLLDW", 5 => "SCROLLEL", _ => "" },
                         book.ItemIcon(item.Id));
        }
        output.WriteLine($"{which}: GameBook — {legacy.Count} полей, лорды, побочные ветки, сообщения ИИ, значки свитков совпали");
    }

    /// <summary>Висячие строки подчинённых таблиц оригинала — то, что схема сознательно не несёт: строки без хозяина правила и так не
    /// читали. Их число — факт о таблицах, его держит <c>F6DataTests</c>.</summary>
    private static class Dangling
    {
        public static IReadOnlyDictionary<string, IReadOnlyList<int>> WithoutUnits(IReadOnlyDictionary<string, IReadOnlyList<int>> byUnit,
                                                                                 IEnumerable<string> units)
        {
            var known = units.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return byUnit.Where(p => known.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static JsonNode Strip(object model, params string[] fields)
    {
        var node = JsonSerializer.SerializeToNode(model, model.GetType(), Json)!;
        foreach (var f in fields) node.AsObject().Remove(f);
        return node;
    }

    private void Same(string name, object old, object now)
    {
        string a = Normal(old), b = Normal(now);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return;
        var la = a.Split('\n');
        var lb = b.Split('\n');
        int at = Enumerable.Range(0, Math.Min(la.Length, lb.Length)).FirstOrDefault(i => !string.Equals(la[i], lb[i], StringComparison.OrdinalIgnoreCase));
        output.WriteLine($"{name}: расходится со строки {at}:");
        for (int i = Math.Max(0, at - 6); i < Math.Min(Math.Max(la.Length, lb.Length), at + 4); i++)
            output.WriteLine($"  было  {(i < la.Length ? la[i] : "—")}\n  стало {(i < lb.Length ? lb[i] : "—")}");
        Assert.Fail($"{name}: модель из содержимого расходится с моделью из таблиц (строка {at} JSON, см. вывод)");
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, IncludeFields = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() },
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    /// <summary>JSON модели: ключи объектов по порядку, «пустая ссылка» оригинала — пустая строка.</summary>
    private static string Normal(object model)
    {
        var node = model as JsonNode ?? JsonSerializer.SerializeToNode(model, model.GetType(), Json);
        return Sorted(node)?.ToJsonString(Json) ?? "null";
    }

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => KeyValuePair.Create(p.Key.ToLowerInvariant(), Sorted(p.Value)))),
        JsonArray arr => new JsonArray([.. arr.Select(Sorted)]),
        JsonValue v when v.GetValueKind() == JsonValueKind.String && v.GetValue<string>().Equals("g000000000", StringComparison.OrdinalIgnoreCase)
            => JsonValue.Create(""),
        _ => node?.DeepClone(),
    };
}
