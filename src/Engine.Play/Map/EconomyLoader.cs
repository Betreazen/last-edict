using D2.Data.Content;
using D2.Data.Content.Schema;
using D2.Rules.Map;

namespace D2.Data.Map;

/// <summary>
/// Содержимое (расы, уровни городов, постройки, юниты, глобальные величины, настройки ИИ, вор, ориентиры) → правила экономики,
/// земли, дипломатии, вора и ИИ. Прямая проекция, без вычислений: всё, что складывается в доход, живёт в <see cref="Economy"/>. Ключи
/// регистронезависимы; «ссылки нет» — пустая строка.
/// </summary>
public static class EconomyLoader
{
    public static EconomyRules Load(string globalsDir) => Load(ContentSource.Open(globalsDir));

    public static EconomyRules Load(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var races = new Dictionary<string, RaceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in content.Races)
            races[r.Key] = new RaceInfo(r.Key, Prices.ToBank(r.Income), r.Scout ?? 0, r.CapitalRegeneration ?? 0, content.KeyOf(r.Protection),
                                        (int)r.Kind, r.Playable)
            {
                Soldiers = [.. r.Soldiers.Select(content.KeyOf)],
                Leaders = LeadersOf(content, r),
                Terrain = r.Land is { } land ? (int)land : LegacyRaces.Terrain((int)r.Kind),
                TurnOrder = r.TurnOrder ?? LegacyRaces.TurnOrder((int)r.Kind),
                EventFlag = r.EventFlag ?? LegacyRaces.EventFlag((int)r.Kind),
                Neutral = r.Neutral ?? r.Kind == RaceKind.Neutral,
            };

        var cities = content.Cities
            .Select(c => new CityInfo(content.KeyOf(c.Race), c.Size, c.Scout, c.Regeneration, content.KeyOf(c.Protection),
                                      Prices.ToBank(c.Growth), c.ResourceMoney ?? 0))
            .ToList();

        var buildings = new Dictionary<string, BuildingType>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in content.Buildings)
            buildings[b.Key] = new BuildingType(b.Key, content.KeyOf(b.Requires), Prices.ToBank(b.Cost), b.Level, (int)b.Category,
                                                (int?)b.Branch ?? 0);

        var hire = new Dictionary<string, UnitHire>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in content.Units)
            hire[u.Key] = new UnitHire(u.Key, content.KeyOf(u.Race), Prices.ToBank(u.HireCost), content.KeyOf(u.HireBuilding), u.Level);

        var g = content.Globals;
        return new EconomyRules(races, cities, buildings, hire,
            // По сложности: лёгкая, средняя, трудная, очень трудная (Ldiff.dbf).
            IncomeBonusPercent: g.Difficulty.IncomePercent,
            CrystalProfit: g.Economy.CrystalProfit,
            SellRatio: g.Economy.SellRatio,
            ConstructionUrgency: g.Ai.ConstructionUrgency);
    }

    /// <summary>Лидеры для найма расы и вор — в этом порядке (<c>getLeadersHireList</c> <c>0x5d475e</c>).</summary>
    internal static IReadOnlyList<string> LeadersOf(ContentSet content, RaceDef race) =>
        [.. race.Leaders.Append(race.Noble).Select(content.KeyOf).Where(id => id.Length > 0)];

    /// <summary>Цены юнитов для лечения, воскрешения и обучения, цена жезла и категории лордов.</summary>
    public static TreasuryRules LoadTreasury(string globalsDir) => LoadTreasury(ContentSource.Open(globalsDir));

    public static TreasuryRules LoadTreasury(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var units = new Dictionary<string, UnitCosts>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in content.Units)
            units[u.Key] = new UnitCosts(u.Key, Prices.ToBank(u.ReviveCost), Prices.ToBank(u.HealCost), Prices.ToBank(u.TrainingCost),
                                         u.XpForNextLevel);
        var lords = content.Lords.ToDictionary(l => l.Key, l => (int)l.Category, StringComparer.OrdinalIgnoreCase);
        return new TreasuryRules(units, Prices.ToBank(content.Globals.Land.RodCost), lords);
    }

    /// <summary>Квоты роста земли: столица, деревни по уровням, радиус жезла, доля при захвате (задача 7 Ф7).</summary>
    public static LandQuotas LoadLand(string globalsDir) => LoadLand(ContentSource.Open(globalsDir));

    public static LandQuotas LoadLand(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var land = content.Globals.Land;
        return new LandQuotas(land.Capital, land.Cities, land.RodRange, land.CapturePercent);
    }

    /// <summary>Рост лидеров и прочие величины карты: прибавка лечения воителя, мастер оружия, заряды талисмана, предел способностей,
    /// заклинания взятой столицы, лечение в руинах (задачи 4, 6, 10 и 11 Ф7).</summary>
    public static MapVariables LoadVars(string globalsDir) => LoadVars(ContentSource.Open(globalsDir));

    public static MapVariables LoadVars(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var g = content.Globals;
        return new MapVariables(g.Heroes.WarlordRegeneration, g.Heroes.WeaponMaster, g.Heroes.TalismanCharges, g.Heroes.MaxAbilities,
                                g.Spells.GainOnCapture, g.Economy.RuinRegeneration);
    }

    /// <summary>Величины дипломатии (задача 13 Ф7).</summary>
    public static DiplomacyRules LoadDiplomacy(string globalsDir) => LoadDiplomacy(ContentSource.Open(globalsDir));

    public static DiplomacyRules LoadDiplomacy(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var d = content.Globals.Diplomacy;
        return new DiplomacyRules(d.Peace, d.War, d.Neutral, d.GoldPerPoint, d.MakeAlly, d.BreakAlly, d.RefuseAlly, d.AttackAtPeace,
                                  d.AttackStack, d.AttackFort, d.RodDestroyed, d.NobleAction);
    }

    /// <summary>Читы ИИ по сложности. Отношения ИИ различаются, но если в читах они разошлись (мод), порт этого не различает —
    /// исключение, а не «первое отношение».</summary>
    public static AiCheatRules LoadAiCheats(string globalsDir) => LoadAiCheats(ContentSource.Open(globalsDir));

    public static AiCheatRules LoadAiCheats(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var rows = content.AiAttitudes.Select(a => new AiCheatRules(a.Cheats.ResourcePercent, a.Cheats.XpPercent)).ToList();
        if (rows.Count == 0 || rows.Any(r => !r.ResourcePercent.SequenceEqual(rows[0].ResourcePercent)
                                             || !r.UnitXpPercent.SequenceEqual(rows[0].UnitXpPercent)))
            throw new InvalidDataException("настройки ИИ: читы (cheats) пусты или различаются по отношению ИИ");
        return rows[0];
    }

    /// <summary>Веса тактики ИИ по отношению (у 4–6 нули жезла и городов) — словарь, а не одна строка.</summary>
    public static IReadOnlyDictionary<int, AiWeights> LoadAiWeights(string globalsDir) => LoadAiWeights(ContentSource.Open(globalsDir));

    public static IReadOnlyDictionary<int, AiWeights> LoadAiWeights(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.AiAttitudes.ToDictionary(a => (int)a.Attitude, a =>
        {
            var w = a.Weights;
            return new AiWeights((int)a.Attitude, w.CitiesNeutral, w.CitiesRival, w.CitiesPlayer, w.StacksNeutral, w.StacksRival,
                                 w.StacksPlayer, w.Bags, w.Ruins, w.Towers, w.Merchants, w.HireUnits, w.HireMercenaries, w.Heal, w.PlantRod,
                                 w.AttackRod, w.NeutralCities, w.NeutralStacks, w.Trainers);
        });
    }

    /// <summary>Доли и пределы лидеров ИИ по отношению.</summary>
    public static IReadOnlyDictionary<int, AiStrategicRules.AiLeaderShares> LoadAiLeaderShares(string globalsDir) =>
        LoadAiLeaderShares(ContentSource.Open(globalsDir));

    public static IReadOnlyDictionary<int, AiStrategicRules.AiLeaderShares> LoadAiLeaderShares(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.AiAttitudes.ToDictionary(a => (int)a.Attitude, a =>
        {
            var l = a.Leaders;
            return new AiStrategicRules.AiLeaderShares((int)a.Attitude, l.FighterPercent, l.RodPercent, l.ThiefPercent, l.FighterMin,
                                                       l.FighterMax, l.ThiefMin, l.ThiefMax, l.RodMin, l.RodMax);
        });
    }

    /// <summary>Шанс действия вора по его виду и мораль; прочие величины вора (задача 12 Ф7). Дважды объявленный вид действия — дрейф
    /// данных, исключение, а не «последний побеждает».</summary>
    public static ThiefRules LoadThieves(string globalsDir) => LoadThieves(ContentSource.Open(globalsDir));

    public static ThiefRules LoadThieves(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var chance = new Dictionary<int, int>();
        foreach (var a in content.ThiefActions)
            if (!chance.TryAdd((int)a.Kind, a.Chance))
                throw new InvalidDataException($"действие вора {a.Kind} объявлено дважды ({a.Id})");
        var t = content.Globals.Thieves;
        return new ThiefRules(chance, [.. t.Morale.Take(Thieves.CapitalMoraleIndex)])
        {
            PoisonStack = t.PoisonStack, PoisonCity = t.PoisonCity, Bribe = t.Bribe, StealRace = t.StealRace, StealNeut = t.StealNeutral,
            RiotMin = t.RiotMin, RiotMax = t.RiotMax, RiotDamage = t.RiotDamage,
        };
    }

    /// <summary>Ориентиры-горы: их клетки рост земли отвергает (<see cref="LandModel.BlockedCells"/>, ai-q25 ранг 1). Пусто — не гора.</summary>
    public static IReadOnlySet<string> LoadMountainLandmarks(string globalsDir) => LoadMountainLandmarks(ContentSource.Open(globalsDir));

    public static IReadOnlySet<string> LoadMountainLandmarks(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Landmarks.Where(l => l.Mountain == true).Select(l => l.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
