using LastEdict.Data.Battle;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Map;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Play;

/// <summary>Правила, которые нужны партии: бой (<see cref="GameRules"/>), поля юнитов, которых в бою нет
/// (<see cref="RulesLoader.RulesExtras"/>: категория, постройка улучшения, рост уровнем), экономика, ИИ, дипломатия. Собираются из
/// содержимого (<see cref="ContentSet"/>: база <c>data/content/base</c> и моды поверх) один раз на запуск.</summary>
public sealed class GameBook
{
    private GameBook(GameRules rules, RulesLoader.RulesExtras extras, EconomyRules economy, MapVariables vars)
    {
        Rules = rules;
        Extras = extras;
        Economy = economy;
        Vars = vars;
        UpgradeBuilding = extras.Units.Values.ToDictionary(u => u.Id, u => u.UpgradeBuildingId, StringComparer.OrdinalIgnoreCase);
        MoveByType = extras.Units.Values.ToDictionary(u => u.Id, u => u.Move, StringComparer.OrdinalIgnoreCase);
        ScoutByType = extras.Units.Values.ToDictionary(u => u.Id, u => u.Scout, StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyDictionary<string, int> Lords { get; init; } = new Dictionary<string, int>();

    /// <summary>Тип лорда (<c>Glord.CATEGORY</c>: 0 маг, 1 воин, 2 гильдмастер); нет лорда — −1.</summary>
    public int LordCategory(string lordId) => Lords.TryGetValue(lordId, out int c) ? c : -1;

    /// <summary>Экономика: доход рас, города, постройки, найм, надбавка дохода по сложности.</summary>
    public EconomyRules Economy { get; }

    /// <summary><c>GVars</c> роста юнитов: прибавка регенерации Воителя, Инструктор, заряды талисмана.</summary>
    public MapVariables Vars { get; }

    /// <summary><c>Gunits.MOVE</c> и <c>SCOUT</c> по типу — база запаса хода и обзора лидера.</summary>
    public IReadOnlyDictionary<string, int> MoveByType { get; }

    public IReadOnlyDictionary<string, int> ScoutByType { get; }

    public GameRules Rules { get; }

    public RulesLoader.RulesExtras Extras { get; }

    /// <summary><c>Gunits.UPGRADE_B</c> по типу: постройка, открывающая этот тип как улучшение.</summary>
    public IReadOnlyDictionary<string, string> UpgradeBuilding { get; }

    /// <summary>Список найма лидеров расы — <c>Grace.LEADER_1..4</c> и <c>NOBLE</c> в этом порядке
    /// (<c>getLeadersHireList</c> <c>0x5d475e</c>).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> RaceLeaders { get; private init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Мужские имена героев расы (<c>Tleader.dbf</c>): человеку при старте партии даётся случайное.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> LeaderNames { get; private init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Броня укрепления для силы ИИ (<c>0x5dcf60</c>): деревни уровня 1..5 — <c>GVars.PROT_1..5</c>
    /// (<c>+0xf0</c>), столица — <c>PROT_CAP</c> (<c>+0x104</c>). Не броня боя (та — модификаторы города).</summary>
    public IReadOnlyList<int> AiCityArmor { get; private init; } = [];

    public int AiCapitalArmor { get; private init; }

    /// <summary>Квоты роста земли (<c>GVars.T_CAPITAL</c>, <c>T_CITY1…5</c>, <c>ROD_RANGE</c>, <c>T_CAPTURE</c>).</summary>
    public LandQuotas Land { get; private init; } = LandQuotas.Vanilla;

    /// <summary>Типы ландмарков с <c>GLmark.MOUNTAIN = T</c>: их клетки рост земли отвергает (<see cref="LandModel.BlockedCells"/>).</summary>
    public IReadOnlySet<string> MountainLandmarks { get; private init; } = new HashSet<string>();

    /// <summary>Постройки лорда (<c>GbuiList.dbf</c>: <c>LORD_ID → BUILD_ID</c>) в порядке id.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> LordBuildings { get; private init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Цены юнитов (лечение, воскрешение, обучение) и жезла.</summary>
    public TreasuryRules Treasury { get; private init; } = new(new Dictionary<string, UnitCosts>(), Bank.Zero,
                                                                 new Dictionary<string, int>());

    /// <summary>Веса тактики ИИ (<c>GAI.I_*</c>) по отношению игрока (<c>CATEGORY</c>).</summary>
    public IReadOnlyDictionary<int, AiWeights> AiWeights { get; private init; } = new Dictionary<int, AiWeights>();

    /// <summary>Доли и пределы лидеров <c>GAI</c> по категории отношения.</summary>
    public IReadOnlyDictionary<int, AiStrategicRules.AiLeaderShares> AiLeaderShares { get; private init; } =
        new Dictionary<int, AiStrategicRules.AiLeaderShares>();

    /// <summary>Доля казны в кошелёк тактики (<c>GAI.B_ISO</c>, поле <c>+0x34</c> строки, <c>0x58f6c0</c>) по категории
    /// отношения: кошелёк <c>CAiTactic +0x515c</c> заполняется в начале хода тактики (<c>0x4659e2</c>).</summary>
    public IReadOnlyDictionary<int, int> AiTacticPurseShare { get; private init; } = new Dictionary<int, int>();

    /// <summary>Базы бюджета дня ИИ (<c>GAI.B_UNITS</c>, <c>B_LEADERS</c>, <c>B_CAP</c>, <c>B_CITIES</c>) по категории отношения — движок читает
    /// их из строки <c>GAI</c> (аудит 13.10: у порта была зашита таблица ванили, и мод с другими долями не действовал).</summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<AiStepType, int>> AiBudgetBases { get; private init; } =
        new Dictionary<int, IReadOnlyDictionary<AiStepType, int>>();

    /// <summary>Порог «раненые в крепости» (<c>GAI.HP_STAYMIN</c>, поле <c>+0xc8</c> строки, <c>0x58f7a5</c>) по категории отношения:
    /// юнит отряда в крепости со здоровьем ниже этого процента держит отряд на лечении (<c>0x46779d</c>).</summary>
    public IReadOnlyDictionary<int, int> AiStayMinHp { get; private init; } = new Dictionary<int, int>();

    /// <summary>Порог «ранен» для счёта лечения (<c>GAI.HP_HEALMIN</c>, поле <c>+0xcc</c> строки, <c>0x58f7c5</c>) по категории отношения:
    /// юнит или группа ниже этого процента удваивают счёт <c>HealRevive</c> (<c>0x457d39</c>, <c>0x457e58</c>).</summary>
    public IReadOnlyDictionary<int, int> AiHealMinHp { get; private init; } = new Dictionary<int, int>();

    /// <summary><c>Gunits.RACE_ID</c> типа; пусто — типа нет.</summary>
    public string RaceOf(string typeId) =>
        TryGlobal(typeId, out var id, out _) && Extras.Units.TryGetValue(id, out var e) ? e.Race : "";

    /// <summary><c>Grace.RACE_TYPE</c> эльфов (<c>L_ELF</c>, GOG <c>0x8372a0</c>).</summary>
    public const int ElfRaceType = LegacyRaces.Elf;

    /// <summary>Тип юнита — эльфийской расы: <c>Gunits.RACE_ID</c> (<see cref="RaceOf"/>) → <c>Grace.RACE_TYPE</c> <see cref="ElfRaceType"/>; у
    /// движка — <c>IUsSoldier::getRaceId</c> ходящего и категория расы (<c>0x635358…0x6353b6</c>, картинка второй атаки «лечение»). Нет типа — нет.</summary>
    public bool IsElf(string typeId) =>
        Economy.Races.TryGetValue(RaceOf(typeId), out var race) && race.RaceType == ElfRaceType;

    /// <summary>Читы ИИ по сложности (<c>GAI.D_*_M</c>, <c>D_*_XP</c>).</summary>
    public AiCheatRules AiCheats { get; private init; } = new([100, 100, 100, 100], [0, 0, 0, 0]);

    /// <summary>Здание найма базового юнита побочной ветки расы (<c>L_SIDESHOW</c>, <c>0x837f28</c>): <c>ENROLL_B</c> солдата
    /// расы с <c>BRANCH</c> 4 без <c>PREV_ID</c>. Им ИИ без храма заканчивает запасной выбор постройки (ранг 2).</summary>
    public string SideshowBuilding(string raceId) => Sideshow.GetValueOrDefault(raceId, "");

    private IReadOnlyDictionary<string, string> Sideshow { get; init; } = new Dictionary<string, string>();

    /// <summary>Навыки лидера по уровню (<c>GleaUpg.dbf</c>) в порядке таблицы — он же порядок равенства при выборе ИИ.</summary>
    public IReadOnlyList<LastEdict.Rules.Units.LeaderUpgrade> LeaderUpgrades { get; private init; } = [];

    /// <summary>Врождённые способности типа лидера (<c>GLabi.dbf</c>): фильтр предложения навыков.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> InnateAbilities { get; private init; } =
        new Dictionary<string, IReadOnlyList<int>>();

    /// <summary>Заклинания (<c>Gspells.dbf</c>): уровень и категория — для исследования.</summary>
    public IReadOnlyDictionary<string, LastEdict.Rules.Magic.Spell> Spells { get; private init; } =
        new Dictionary<string, LastEdict.Rules.Magic.Spell>();

    /// <summary>Предметы (<c>GItem.dbf</c>): категория, цена, модификаторы, лечение, атака.</summary>
    public IReadOnlyDictionary<string, LastEdict.Rules.Magic.Item> Items { get; private init; } =
        new Dictionary<string, LastEdict.Rules.Magic.Item>();

    /// <summary>Подсказка выбора ИИ (<c>Gspells.AI_CAT</c>, перечень <c>LAiSpell</c>).</summary>
    public IReadOnlyDictionary<string, int> SpellAiCategory { get; private init; } = new Dictionary<string, int>();

    /// <summary>Что лорд может исследовать (<c>GspellR.dbf</c>: <c>LORD_ID → SPELL_ID, RESEARCH</c>) в порядке таблицы.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<(string SpellId, Bank Price)>> LordResearch { get; private init; } =
        new Dictionary<string, IReadOnlyList<(string SpellId, Bank Price)>>();

    /// <summary>Тип расы заклинания для значка свитка (<c>0x5c5f4f</c>): <c>Grace.RACE_TYPE</c> расы первого лорда по порядку <c>Glord</c>, у
    /// которого заклинание есть в <c>GspellR</c>.</summary>
    private IReadOnlyDictionary<string, int> SpellRaceType { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Имя значка предмета в архиве <c>IconItem</c> (<c>0x5c5d89</c>): у свитка (<see cref="LastEdict.Rules.Magic.ItemCategory.Scroll"/>) своих
    /// значков нет — значок расы его заклинания: <c>SCROLLHU</c>, <c>SCROLLUN</c>, <c>SCROLLHE</c>, <c>SCROLLDW</c>, <c>SCROLLEL</c> по категории
    /// расы (<c>0x5c5e1d</c>…<c>0x5c5e8b</c>; раса — первого лорда, у которого заклинание в списке исследований, <c>0x5c5f4f</c>; ни у кого —
    /// люди, <c>0x5c6005</c>); нейтральная — пустое имя (значка нет). Прочие предметы — по своему id. Соответствие категорий именам значков —
    /// по именам (<c>L_HUMAN</c> ↔ <c>HU</c> и т. д.), ранг 2.
    /// </summary>
    public string ItemIcon(string itemType)
    {
        ArgumentNullException.ThrowIfNull(itemType);
        if (!Items.TryGetValue(itemType, out var item) || item.Category != LastEdict.Rules.Magic.ItemCategory.Scroll) return itemType.ToUpperInvariant();
        int raceType = SpellRaceType.TryGetValue(item.SpellId, out int t) ? t : HumanRace;
        return raceType switch
        {
            HumanRace => "SCROLLHU",
            1 => "SCROLLUN",
            2 => "SCROLLHE",
            3 => "SCROLLDW",
            5 => "SCROLLEL",
            _ => "",
        };
    }

    /// <summary><c>LRace</c>: 0 — <c>L_HUMAN</c> (1 нежить, 2 еретики, 3 гномы, 4 нейтралы, 5 эльфы).</summary>
    private const int HumanRace = 0;

    /// <summary>Дипломатия (<c>GVars.D_*</c>): пороги войны и мира, величины мутатора отношений.</summary>
    public DiplomacyRules Diplomacy { get; private init; } = DiplomacyRules.Vanilla;

    /// <summary>Правила вора (<c>Gaction.CHANCE</c>, <c>GVars</c>: мораль, отравление, кража, подкуп, мятеж).</summary>
    public ThiefRules Thieves { get; private init; } = ThiefRules.Vanilla;

    /// <summary><c>GVars.SPY_DISCOV</c> (<c>+0x50</c>, геттер <c>0x566368</c>): шанс в процентах, что разведанный игрок в начале
    /// хода разведчика «раскроет шпиона» и выпадет из его множества (<see cref="GameSession"/>, <c>0x44430f</c>).</summary>
    public int SpyDiscovery { get; private init; } = 10;

    /// <summary><c>GVars.GU_RANGE</c> (<c>+0x120</c>, <c>0x5879fd</c>): охрана ИИ берёт цели не дальше половины.</summary>
    public int GuardRange { get; private init; } = 5;

    /// <summary><c>GVars.PA_RANGE</c> (<c>+0x118</c>, геттер <c>0x5879eb</c>) — радиус охраны отряда (<c>DefendParty</c>) вдвое.</summary>
    public int PartyRange { get; private init; } = 5;

    /// <summary><c>GVars.LO_RANGE</c> (<c>+0x11c</c>, геттер <c>0x5879f4</c>) — радиус фильтра приказа <c>L_DEFEND_LOCATION</c>.</summary>
    public int LocationRange { get; private init; } = 3;

    /// <summary><c>GVars.D_BKA_TURN</c> и <c>D_BKA_CHNC</c> — порог дней и потолок шанса разрыва союза ИИ
    /// (<see cref="AiDiplomacy.BreakChance"/>).</summary>
    public (int Turn, int Chance) AllianceBreak { get; private init; } = (10, 5);

    /// <summary>Сообщения ИИ (<c>TAiMsg.dbf</c>) по расе и категории, в порядке таблицы; номер — бросок чата
    /// (<see cref="AiDiplomacy.Chat"/>).</summary>
    public IReadOnlyList<string> AiMessages(string raceId, AiMessage category) =>
        Messages.GetValueOrDefault($"{raceId}|{(int)category}") ?? [];

    private IReadOnlyDictionary<string, IReadOnlyList<string>> Messages { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary><c>GVars.SPLPWR_0..4</c> — сила заклинания на площади по расстоянию от центра (Чебышёв), в процентах
    /// (<c>0x41bd90</c>, <c>GlobalVariables +0x13c</c>).</summary>
    public IReadOnlyList<int> SpellPower { get; private init; } = [100, 80, 60, 40, 30];

    /// <summary><c>GVars.BONUS_E/A/H/V</c> — множитель стартовой казны человека по сложности, в процентах.</summary>
    public IReadOnlyList<int> StartBankPercent { get; private init; } = [100, 100, 100, 100];

    /// <summary>Правила из каталога: база содержимого или таблицы оригинала (<see cref="ContentSource.Open"/>).</summary>
    public static GameBook Load(string rulesDir) => Load(ContentSource.Open(rulesDir));

    /// <summary>Правила из содержимого (база и моды поверх — <see cref="ContentLayers"/>).</summary>
    public static GameBook Load(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var g = content.Globals;
        var ai = content.AiAttitudes;
        return new(RulesLoader.Load(content), RulesLoader.LoadExtras(content), EconomyLoader.Load(content), EconomyLoader.LoadVars(content))
        {
            Lords = content.Lords.ToDictionary(l => l.Key, l => (int)l.Category, StringComparer.OrdinalIgnoreCase),
            RaceLeaders = content.Races.ToDictionary(r => r.Key, r => EconomyLoader.LeadersOf(content, r), StringComparer.OrdinalIgnoreCase),
            LeaderNames = content.Races.Where(r => r.HeroNames.Any(n => n.Male))
                .ToDictionary(r => r.Key, r => (IReadOnlyList<string>)[.. r.HeroNames.Where(n => n.Male).Select(n => n.Text.Trim())],
                              StringComparer.OrdinalIgnoreCase),
            LordBuildings = content.Lords.Where(l => l.Buildings.Count > 0)
                .ToDictionary(l => l.Key, l => (IReadOnlyList<string>)[.. l.Buildings.Select(content.KeyOf).OrderBy(id => id, StringComparer.OrdinalIgnoreCase)],
                              StringComparer.OrdinalIgnoreCase),
            AiCheats = EconomyLoader.LoadAiCheats(content),
            AiWeights = EconomyLoader.LoadAiWeights(content),
            AiLeaderShares = EconomyLoader.LoadAiLeaderShares(content),
            AiTacticPurseShare = ai.ToDictionary(a => (int)a.Attitude, a => a.Budget.TacticPurse),
            AiBudgetBases = ai.ToDictionary(a => (int)a.Attitude, a => (IReadOnlyDictionary<AiStepType, int>)new Dictionary<AiStepType, int>
            {
                [AiStepType.HireUnit] = a.Budget.Units, [AiStepType.HireLeader] = a.Budget.Leaders,
                [AiStepType.Capital] = a.Budget.Capital, [AiStepType.GrowCities] = a.Budget.Cities,
            }),
            AiStayMinHp = ai.ToDictionary(a => (int)a.Attitude, a => a.StayMinHp),
            AiHealMinHp = ai.ToDictionary(a => (int)a.Attitude, a => a.HealMinHp),
            Sideshow = content.Units
                .Where(u => u.Role == UnitRole.Soldier && u.Branch == BranchKind.Sideshow && u.PreviousLevel is null && u.HireBuilding is not null)
                .GroupBy(u => content.KeyOf(u.Race), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(gr => gr.Key, gr => content.KeyOf(gr.First().HireBuilding), StringComparer.OrdinalIgnoreCase),
            Treasury = EconomyLoader.LoadTreasury(content),
            LeaderUpgrades = LastEdict.Data.Battle.ModifierLoader.LoadLeaderUpgrades(content),
            Spells = LastEdict.Data.Magic.SpellLoader.Load(content),
            Items = LastEdict.Data.Magic.ItemLoader.Load(content),
            SpellAiCategory = content.Spells.ToDictionary(sp => sp.Key, sp => (int?)sp.AiUse ?? 0, StringComparer.OrdinalIgnoreCase),
            LordResearch = content.Lords.Where(l => l.Research.Count > 0)
                .ToDictionary(l => l.Key, l => (IReadOnlyList<(string, Bank)>)[.. l.Research.Select(r => (content.KeyOf(r.Spell), Prices.ToBank(r.Cost)))],
                              StringComparer.OrdinalIgnoreCase),
            SpellRaceType = SpellRaces(content),
            InnateAbilities = LastEdict.Data.Magic.EquipLoader.LoadInnateAbilities(content),
            Diplomacy = EconomyLoader.LoadDiplomacy(content),
            AllianceBreak = (g.Diplomacy.AllianceBreakTurn, g.Diplomacy.AllianceBreakChance),
            GuardRange = g.Ai.GuardRange,
            PartyRange = g.Ai.PartyRange,
            LocationRange = g.Ai.LocationRange,
            Thieves = EconomyLoader.LoadThieves(content),
            SpyDiscovery = g.Thieves.SpyDiscovery,
            SpellPower = g.Spells.AreaPowerPercent,
            Messages = content.Races.SelectMany(r => r.AiMessages.Select(m => (Race: r.Key, m.Kind, m.Text)))
                .GroupBy(m => $"{m.Race}|{(int)m.Kind}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(gr => gr.Key, gr => (IReadOnlyList<string>)[.. gr.Select(m => m.Text.Trim())], StringComparer.OrdinalIgnoreCase),
            StartBankPercent = g.Difficulty.StartBankPercent,
            AiCityArmor = g.Ai.CityArmor,
            AiCapitalArmor = g.Ai.CapitalArmor,
            Land = EconomyLoader.LoadLand(content),
            MountainLandmarks = EconomyLoader.LoadMountainLandmarks(content),
        };
    }

    /// <summary>Заклинание → род расы первого лорда (порядок лордов), у которого оно в списке исследований (<c>0x5c5f4f</c>).</summary>
    private static Dictionary<string, int> SpellRaces(ContentSet content)
    {
        var raceKinds = content.Races.ToDictionary(r => r.Id, r => (int)r.Kind, StringComparer.Ordinal);
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var lord in content.Lords)
        {
            if (lord.Race is null || !raceKinds.TryGetValue(lord.Race, out int kind)) continue;
            foreach (var research in lord.Research) result.TryAdd(content.KeyOf(research.Spell), kind);
        }
        return result;
    }

    /// <summary><c>Gunits.LEADER_CAT</c> типа (<c>LleadC</c>: 1 воин, 2 следопыт, 3 маг, 4 жезлоносец, 5 вор); нет — 0.</summary>
    public int LeaderCategoryOf(string typeId) =>
        TryGlobal(typeId, out var id, out _) && Extras.Units.TryGetValue(id, out var e) ? e.LeaderCategory : 0;

    /// <summary>Глобальный тип и уровень реализации: строка <c>Gunits</c> как есть, порождённая (<c>UG</c>) — разбором.</summary>
    public bool TryGlobal(string implId, out string globalId, out int level)
    {
        globalId = implId;
        level = 0;
        if (Rules.Units.TryGetValue(implId, out var type)) { level = type.Level; return true; }
        return Rules.Generator is not null && Rules.Generator.TryDecode(implId, out globalId, out level);
    }

    /// <summary>Тип <paramref name="typeId"/>, выращенный до уровня <paramref name="level"/> (<see cref="UnitGrowth"/>).</summary>
    public GrownUnit? Grown(string typeId, int level)
    {
        if (!TryGlobal(typeId, out var id, out _) || !Rules.Units.TryGetValue(id, out var type)
            || !Extras.Units.TryGetValue(id, out var ext)) return null;
        return UnitGrowth.Of(type, ext.DynUpgLevel, Math.Max(level, type.Level), Upgrade(ext.DynUpg1Id),
                             Upgrade(ext.DynUpg2Id), ext.Regen, ext.Move);
    }

    /// <summary>Порог опыта типа на уровне — <c>XP_NEXT</c> с ростом <c>GDynUpgr</c>; 0 — типа нет.</summary>
    public int XpNextOf(string typeId, int level) => Grown(typeId, level)?.Type.XpNext ?? 0;

    /// <summary>Цена найма юнита типа <paramref name="typeId"/> уровня <paramref name="level"/> — <c>ENROLL_C</c> с шагами
    /// <c>GDynUpgr.ENROLL_C</c> (<see cref="LastEdict.Rules.Map.Economy.HireCost(UnitHire, int, int, DynUpgrade?, DynUpgrade?)"/>, слот
    /// <c>0x54</c> порождённой реализации): так платят у наёмника за предложение его уровня. null — типа нет в справочниках.</summary>
    public Bank? HirePrice(string typeId, int level)
    {
        if (!TryGlobal(typeId, out var id, out _) || !Economy.Hire.TryGetValue(id, out var hire)
            || !Extras.Units.TryGetValue(id, out var ext)) return null;
        return LastEdict.Rules.Map.Economy.HireCost(hire, level, ext.DynUpgLevel, DynRow(ext.DynUpg1Id), DynRow(ext.DynUpg2Id));
    }

    private DynUpgrade? DynRow(string id) => id.Length > 0 && Extras.DynUpgrades.TryGetValue(id, out var u) ? u : null;

    /// <summary><c>NEGOTIATE</c> типа, выращенный до уровня: база <c>Gunits</c> плюс шаги <c>GDynUpgr.NEGOTIATE</c>
    /// (<see cref="UnitGrowth.StepsFor"/>) — сопротивление отряда действию вора (<see cref="LastEdict.Rules.Map.Thieves.StackResistance"/>);
    /// нет типа — 0.</summary>
    public int NegotiateOf(string typeId, int level)
    {
        if (!TryGlobal(typeId, out var id, out _) || !Extras.Units.TryGetValue(id, out var ext)
            || !Rules.Units.TryGetValue(id, out var type)) return 0;
        if (level <= type.Level) return ext.Negotiate;
        var steps = UnitGrowth.StepsFor(type.Level, ext.DynUpgLevel, level);
        int Step(string dyn) => dyn.Length > 0 && Extras.DynUpgrades.TryGetValue(dyn, out var u) ? u.Negotiate : 0;
        return ext.Negotiate + steps.First * Step(ext.DynUpg1Id) + steps.Second * Step(ext.DynUpg2Id);
    }

    /// <summary><c>Gunits.WATER_ONLY</c> типа — ходит только по воде (у лидера это слот <c>0x70</c> реализации); нет типа —
    /// ложь.</summary>
    public bool WaterOnlyOf(string typeId) =>
        TryGlobal(typeId, out var id, out _) && Extras.Units.TryGetValue(id, out var e) && e.WaterOnly;

    /// <summary><c>Gunits.SUBRACE</c> типа (<c>LSubRace</c>); 0 — типа нет.</summary>
    public int SubraceOf(string typeId) =>
        TryGlobal(typeId, out var id, out _) && Extras.Units.TryGetValue(id, out var e) ? e.Subrace : 0;

    /// <summary><c>Gunits.LIFE_TIME</c> типа — срок жизни призванного в днях (слот <c>IUsSummon</c>, <c>0x5d3dab</c>); 0 — срока нет.</summary>
    public int LifeTimeOf(string typeId) =>
        TryGlobal(typeId, out var id, out _) && Extras.Units.TryGetValue(id, out var e) ? e.LifeTime : 0;

    /// <summary><c>Gunits.UNIT_CAT</c>; null — типа нет.</summary>
    public int? CategoryOf(string typeId) =>
        TryGlobal(typeId, out var id, out _) && Extras.Units.TryGetValue(id, out var e) ? e.Category : null;

    private DynUpgrade? Upgrade(string id) => id.Length > 0 && Extras.DynUpgrades.TryGetValue(id, out var u) ? u : null;
}
