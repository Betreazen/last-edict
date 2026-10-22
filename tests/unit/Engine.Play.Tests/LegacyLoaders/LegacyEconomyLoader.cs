// Загрузчики правил ДО схемы содержимого (main 639961ee): правила читались прямо из строк таблиц оригинала. Эталон сверки
// «старая модель = новая» (ContentRulesParityTests) — не править; удаляется вместе с ней, когда таблицы уйдут из проверок.
using LastEdict.Data.Dbf;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Tests.LegacyLoaders;

/// <summary>
/// <c>Grace / GcityInf / Gbuild / Gunits / GVars</c> -> <see cref="EconomyRules"/>.
/// Прямая проекция строк <c>.dbf</c>, без вычислений: всё, что складывается в доход, живёт в
/// <see cref="Economy"/>. Ключи регистронезависимы (в <c>.dbf</c> нижний регистр, в <c>.sg</c>
/// и корпусе — верхний).
/// </summary>
public static class LegacyEconomyLoader
{
    /// <summary>Пустая ссылка в <c>.dbf</c> — те же десять знаков, что и в <c>.sg</c>;
    /// приводится к пустой строке, чтобы «нет требуемой постройки» проверялось одним способом.</summary>
    private const string EmptyId = "g000000000";

    private static List<string> Refs(DbfRow r, params string[] columns) =>
        [.. columns.Select(c => Ref(r.GetString(c))).Where(id => id.Length > 0)];

    private static string Ref(string id) =>
        id.Equals(EmptyId, StringComparison.OrdinalIgnoreCase) ? "" : id;

    public static EconomyRules Load(string globalsDir)
    {
        var races = new Dictionary<string, RaceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in GameTables.Read(globalsDir, "Grace.dbf").Rows)
        {
            var id = r.GetString("RACE_ID");
            races[id] = new RaceInfo(id, Bank.Parse(r.GetString("INCOME")), r.GetInt("SCOUT"),
                r.GetInt("REGEN_H"), Ref(r.GetString("PROTECTION")), r.GetInt("RACE_TYPE"),
                r.GetBool("PLAYABLE"))
            {
                Soldiers = Refs(r, "SOLDIER_1", "SOLDIER_2", "SOLDIER_3", "SOLDIER_4", "SOLDIER_5"),
                Leaders = Refs(r, "LEADER_1", "LEADER_2", "LEADER_3", "LEADER_4", "NOBLE"),
            };
        }

        var cities = GameTables.Read(globalsDir, "GcityInf.dbf").Rows
            .Select(r => new CityInfo(r.GetString("RACE_ID"), r.GetInt("SIZE"), r.GetInt("SCOUT"),
                r.GetInt("REGEN_F"), Ref(r.GetString("PROTECTION")), Bank.Parse(r.GetString("GROWTH")),
                r.GetInt("RES_M")))
            .ToList();

        var buildings = new Dictionary<string, BuildingType>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in GameTables.Read(globalsDir, "Gbuild.dbf").Rows)
        {
            var id = r.GetString("BUILD_ID");
            buildings[id] = new BuildingType(id, Ref(r.GetString("REQUIRED")),
                Bank.Parse(r.GetString("COST")), r.GetInt("LEVEL"), r.GetInt("CATEGORY"),
                r.GetInt("BRANCH"));
        }

        var hire = new Dictionary<string, UnitHire>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in GameTables.Read(globalsDir, "Gunits.dbf").Rows)
        {
            var id = r.GetString("UNIT_ID");
            hire[id] = new UnitHire(id, r.GetString("RACE_ID"), Bank.Parse(r.GetString("ENROLL_C")),
                Ref(r.GetString("ENROLL_B")), r.GetInt("LEVEL"));
        }

        var gvars = GameTables.Read(globalsDir, "GVars.dbf").Rows[0];
        return new EconomyRules(races, cities, buildings, hire,
            // Порядок — Ldiff.dbf: 0 L_EASY, 1 L_AVERAGE, 2 L_HARD, 3 L_VERY_HARD.
            IncomeBonusPercent: new[]
            {
                gvars.GetInt("INCOME_E"), gvars.GetInt("INCOME_A"),
                gvars.GetInt("INCOME_H"), gvars.GetInt("INCOME_V"),
            },
            CrystalProfit: gvars.GetInt("CRYSTAL_P"),
            SellRatio: gvars.GetInt("SELL_RATIO"),
            ConstructionUrgency: gvars.GetInt("CONST_URG"));
    }

    /// <summary><c>Gunits / GVars / Glord</c> -> <see cref="TreasuryRules"/>: цены юнитов для лечения,
    /// воскрешения и обучения, цена жезла и категории лордов. Прямая проекция, как и <see cref="Load"/>;
    /// лорды берутся тем же загрузчиком, что у модификаторов (<c>LegacyModifierLoader.LoadLords</c>).</summary>
    public static TreasuryRules LoadTreasury(string globalsDir)
    {
        var units = new Dictionary<string, UnitCosts>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in GameTables.Read(globalsDir, "Gunits.dbf").Rows)
        {
            var id = r.GetString("UNIT_ID");
            units[id] = new UnitCosts(id, Bank.Parse(r.GetString("REVIVE_C")), Bank.Parse(r.GetString("HEAL_C")),
                Bank.Parse(r.GetString("TRAINING_C")), r.GetInt("XP_NEXT"));
        }

        var gvars = GameTables.Read(globalsDir, "GVars.dbf").Rows[0];
        var lords = LegacyModifierLoader.LoadLords(globalsDir)
            .ToDictionary(p => p.Key, p => p.Value.Category, StringComparer.OrdinalIgnoreCase);
        return new TreasuryRules(units, Bank.Parse(gvars.GetString("ROD_COST")), lords);
    }

    /// <summary><c>GVars</c> -> <see cref="LandQuotas"/>: квоты роста земли <c>T_CAPITAL</c>,
    /// <c>T_CITY1…5</c>, <c>ROD_RANGE</c> и <c>T_CAPTURE</c> (задача 7 Ф7). Прямая проекция строки.</summary>
    public static LandQuotas LoadLand(string globalsDir)
    {
        var gvars = GameTables.Read(globalsDir, "GVars.dbf").Rows[0];
        return new LandQuotas(gvars.GetInt("T_CAPITAL"),
            [.. Enumerable.Range(1, 5).Select(i => gvars.GetInt($"T_CITY{i}"))],
            gvars.GetInt("ROD_RANGE"), gvars.GetInt("T_CAPTURE"));
    }

    /// <summary><c>GVars</c> -> <see cref="MapVariables"/>: <c>REGEN_LWAR</c>, <c>WEAPN_MSTR</c>,
    /// <c>TALIS_CHRG</c>, <c>LDRMAXABIL</c> (задачи 4, 6, 10 и 11 Ф7), <c>GAIN_SPELL</c> (заклинания взятой столицы) и <c>REGEN_RUIN</c>.</summary>
    public static MapVariables LoadVars(string globalsDir)
    {
        var gvars = GameTables.Read(globalsDir, "GVars.dbf").Rows[0];
        return new MapVariables(gvars.GetInt("REGEN_LWAR"), gvars.GetInt("WEAPN_MSTR"),
                                gvars.GetInt("TALIS_CHRG"), gvars.GetInt("LDRMAXABIL"), gvars.GetInt("GAIN_SPELL"),
                                gvars.GetInt("REGEN_RUIN"));
    }

    /// <summary><c>GVars</c> -> <see cref="DiplomacyRules"/>: столбцы <c>D_*</c> дипломатии
    /// (задача 13 Ф7). Прямая проекция строки, как и <see cref="LoadLand"/>.</summary>
    public static DiplomacyRules LoadDiplomacy(string globalsDir)
    {
        var gvars = GameTables.Read(globalsDir, "GVars.dbf").Rows[0];
        return new DiplomacyRules(gvars.GetInt("D_PEACE"), gvars.GetInt("D_WAR"), gvars.GetInt("D_NEUTRAL"),
            gvars.GetInt("D_GOLD"), gvars.GetInt("D_MK_ALLY"), gvars.GetInt("D_BK_ALLY"),
            gvars.GetInt("D_REF_ALLY"), gvars.GetInt("D_ATTAK_PC"), gvars.GetInt("D_ATTAK_SC"),
            gvars.GetInt("D_ATTAK_FO"), gvars.GetInt("D_ROD"), gvars.GetInt("D_NOBLE"));
    }

    /// <summary><c>GAI.dbf</c> -> <see cref="AiCheatRules"/>. Строки различаются отношением ИИ; если в столбцах
    /// читов они разошлись (мод), порт этого не различает — исключение, а не «первая строка».</summary>
    public static AiCheatRules LoadAiCheats(string globalsDir)
    {
        string[] levels = ["EASY", "AVG", "HARD", "VHARD"];
        var rows = GameTables.Read(globalsDir, "GAI.dbf").Rows
            .Select(r => new AiCheatRules([.. levels.Select(l => r.GetInt($"D_{l}_M"))],
                                          [.. levels.Select(l => r.GetInt($"D_{l}_XP"))]))
            .ToList();
        if (rows.Count == 0 || rows.Any(r => !r.ResourcePercent.SequenceEqual(rows[0].ResourcePercent)
                                             || !r.UnitXpPercent.SequenceEqual(rows[0].UnitXpPercent)))
            throw new InvalidDataException("GAI.dbf: столбцы D_*_M / D_*_XP пусты или различаются по отношению ИИ");
        return rows[0];
    }

    /// <summary><c>GAI.dbf</c> -> <see cref="AiWeights"/> по строке на отношение ИИ (<c>CATEGORY</c> 0…6, ключ).
    /// Веса тактики <c>I_*</c> в строках разные (у 4–6 нули жезла и городов), поэтому — словарь, а не одна строка.</summary>
    public static IReadOnlyDictionary<int, AiWeights> LoadAiWeights(string globalsDir) =>
        GameTables.Read(globalsDir, "GAI.dbf").Rows
            .Select(r => new AiWeights(r.GetInt("CATEGORY"), r.GetInt("I_CITIES_N"), r.GetInt("I_CITIES_R"),
                r.GetInt("I_CITIES_P"), r.GetInt("I_STACKS_N"), r.GetInt("I_STACKS_R"), r.GetInt("I_STACKS_P"),
                r.GetInt("I_BAGS"), r.GetInt("I_RUINS"), r.GetInt("I_TOWERS"), r.GetInt("I_MERCHANT"),
                r.GetInt("I_H_UNITS"), r.GetInt("I_H_MERCS"), r.GetInt("I_HEAL"), r.GetInt("I_P_ROD"),
                r.GetInt("I_A_ROD"), r.GetInt("I_N_CITIES"), r.GetInt("I_N_STACK"), r.GetInt("I_TRAINER")))
            .ToDictionary(w => w.Category);

    /// <summary><c>GAI.dbf</c> → доли и пределы лидеров по категории отношения.</summary>
    public static IReadOnlyDictionary<int, AiStrategicRules.AiLeaderShares> LoadAiLeaderShares(string globalsDir) =>
        GameTables.Read(globalsDir, "GAI.dbf").Rows
            .Select(r => new AiStrategicRules.AiLeaderShares(r.GetInt("CATEGORY"), r.GetInt("PCT_FIGHTR"), r.GetInt("PCT_ROD"),
                r.GetInt("PCT_THIEF"), r.GetInt("L_MIN"), r.GetInt("L_MAX"), r.GetInt("T_MIN"), r.GetInt("T_MAX"),
                r.GetInt("R_MIN"), r.GetInt("R_MAX")))
            .ToDictionary(w => w.Category);

    /// <summary><c>Gaction / GVars</c> -> <see cref="ThiefRules"/>: <c>CHANCE</c> по категории
    /// действия вора и <c>MORALE_1…6</c> (задача 12 Ф7). Прямая проекция, как и <see cref="Load"/>.
    /// Дважды объявленная категория — дрейф таблицы, исключение, а не «последняя побеждает».</summary>
    public static ThiefRules LoadThieves(string globalsDir)
    {
        var chance = new Dictionary<int, int>();
        foreach (var r in GameTables.Read(globalsDir, "Gaction.dbf").Rows)
        {
            int category = r.GetInt("CATEGORY");
            if (!chance.TryAdd(category, r.GetInt("CHANCE")))
                throw new InvalidDataException($"Gaction.DBF: CATEGORY {category} встречается дважды");
        }

        var gvars = GameTables.Read(globalsDir, "GVars.dbf").Rows[0];
        return new ThiefRules(chance,
            [.. Enumerable.Range(1, Thieves.CapitalMoraleIndex).Select(i => gvars.GetInt($"MORALE_{i}"))])
        {
            PoisonStack = gvars.GetInt("POISON_S"), PoisonCity = gvars.GetInt("POISON_C"), Bribe = gvars.GetInt("BRIBE"),
            StealRace = gvars.GetInt("STEAL_RACE"), StealNeut = gvars.GetInt("STEAL_NEUT"),
            RiotMin = gvars.GetInt("RIOT_MIN"), RiotMax = gvars.GetInt("RIOT_MAX"), RiotDamage = gvars.GetInt("RIOT_DMG"),
        };
    }

    /// <summary><c>GLmark.dbf</c> -> типы ландмарков с <c>MOUNTAIN = T</c>: их клетки рост земли отвергает
    /// (<see cref="LandModel.BlockedCells"/>, ai-q25 ранг 1). Пустое поле у <c>g000mg8001…</c> — не блокирует.</summary>
    public static IReadOnlySet<string> LoadMountainLandmarks(string globalsDir) =>
        GameTables.Read(globalsDir, "GLmark.dbf").Rows
            .Where(r => r.GetBool("MOUNTAIN"))
            .Select(r => r.GetString("LMARK_ID"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Регистр имени файла на диске непредсказуем (<c>Gbuild.DBF</c> / <c>Grace.dbf</c>) —
    /// ищем без учёта регистра, а не полагаемся на нечувствительность файловой системы.</summary>
    private static string FindFile(string dir, string name) =>
        Directory.EnumerateFiles(dir).FirstOrDefault(
            f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
        ?? throw new FileNotFoundException($"{name} не найден в {dir}");
}
