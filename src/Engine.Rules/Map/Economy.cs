using LastEdict.Rules.Units;

namespace LastEdict.Rules.Map;

/// <summary>Сложность партии (<c>Ldiff.dbf</c>: 0 <c>L_EASY</c>, 1 <c>L_AVERAGE</c>,
/// 2 <c>L_HARD</c>, 3 <c>L_VERY_HARD</c>; в <c>.sg</c> — <c>ScenarioInfo.DIFFGAME</c>). Ранг 1.</summary>
public enum Difficulty
{
    Easy = 0,
    Average = 1,
    Hard = 2,
    VeryHard = 3,
}

/// <summary>
/// Читы сложности ИИ — столбцы <c>GAI.dbf</c> по индексу <see cref="Difficulty"/>: <c>D_EASY_M…D_VHARD_M</c>
/// (ресурсы, 70/100/130/145 в ванили) и <c>D_EASY_XP…D_VHARD_XP</c> (дневной опыт, % от <c>XP_NEXT</c>, 0/2/4/6).
/// Ранг 1 по листингу: выбор столбца — <c>0x58f7e5</c> (смещения <c>+0xa8…+0xb4</c> записи по сложности
/// <c>0x837990/0x8379c0/0x8379b0/0x8379a0</c>) и <c>0x58f859</c>; соответствие смещений столбцам — ранг 2 по порядку
/// полей, сверено корпусом (<c>p = 70</c> на лёгкой, <see cref="Treasury.AiResourceCheat"/>). Строка <c>GAI</c> — по
/// отношению ИИ игрока (<c>0x58fac7</c>); в ванили все семь строк в этих столбцах равны, поэтому хранится одна.
/// </summary>
public sealed record AiCheatRules(IReadOnlyList<int> ResourcePercent, IReadOnlyList<int> UnitXpPercent)
{
    public int Resource(Difficulty difficulty) => ResourcePercent[(int)difficulty];

    /// <summary><c>D_*_XP</c> по сложности — процент дневного опыта (<see cref="Units.Experience.AiDailyXp"/>).</summary>
    public int UnitXp(Difficulty difficulty) => UnitXpPercent[(int)difficulty];
}

/// <summary>
/// Ответ движковой <c>getBuildingStatus</c> (Gog <c>0x5dc9e8</c>). Значения — ранг 1, прочитаны
/// в дизассемблере: функция возвращает первый сработавший код в порядке
/// 5 → 8 → 3 → 6 → 9 → 10, иначе 0.
/// </summary>
public enum BuildStatus
{
    CanBeBuilt = 0,
    InsufficientBank = 3,
    ExceedsMaxLevel = 5,
    AlreadyBuiltThisDay = 6,
    AlreadyBuilt = 8,
    NoRequiredBuilding = 9,
    SiblingUnitBuilding = 10,
}

/// <summary>Строка <c>Globals/Grace.dbf</c>. <see cref="RaceType"/> — колонка <c>RACE_TYPE</c>
/// (0 Империя, 1 Орды, 2 Легионы, 3 Кланы, 4 нейтралы, 5 Эльфы), ранг 1.</summary>
public sealed record RaceInfo(
    string Id, Bank Income, int Scout, int RegenHeal, string ProtectionId, int RaceType, bool Playable)
{
    /// <summary><c>SOLDIER_1…SOLDIER_5</c> по порядку, пустые выброшены: список найма <c>0x5d4911</c> (пятый — ветка
    /// L_SIDESHOW, нанимается при построенном <c>ENROLL_B</c>).</summary>
    public IReadOnlyList<string> Soldiers { get; init; } = [];

    /// <summary><c>LEADER_1…LEADER_4</c>, затем <c>NOBLE</c>, пустые выброшены: список лидеров <c>0x5d475e</c>.</summary>
    public IReadOnlyList<string> Leaders { get; init; } = [];

    /// <summary>Своя земля расы (<c>Lterrain.ID</c>): её красят захваты и жезлы, на ней лечится отряд. Из содержимого; без него — по
    /// роду (<see cref="LegacyRaces.Terrain"/>).</summary>
    public int Terrain { get; init; } = LegacyRaces.Terrain(RaceType);

    /// <summary>Место в очереди хода (меньше — раньше). Из содержимого; без него — по роду (<see cref="LegacyRaces.TurnOrder"/>).</summary>
    public int TurnOrder { get; init; } = LegacyRaces.TurnOrder(RaceType);

    /// <summary>Номер флага расы в событиях сценария (<see cref="ScenarioEvents.RaceFlag(IReadOnlyList{bool}, RaceInfo)"/>); −1 — флага нет.
    /// Из содержимого; без него — по роду (<see cref="LegacyRaces.EventFlag"/>).</summary>
    public int EventFlag { get; init; } = LegacyRaces.EventFlag(RaceType);

    /// <summary>Нейтралы: хозяин ничьих отрядов и объектов карты. Из содержимого; без него — род 4. Признаки выше берутся по роду только
    /// при создании записи: <c>with { RaceType = … }</c> их не пересчитывает.</summary>
    public bool Neutral { get; init; } = RaceType == LegacyRaces.Neutral;
}

/// <summary>Строка <c>Globals/GcityInf.dbf</c> — по пять на расу, <see cref="Size"/> 1..5.
/// В дневной доход не входит ни одно её поле (см. <see cref="Economy.DailyIncome"/>);
/// запись нужна задачам про рост гарнизона и защиту города.</summary>
public sealed record CityInfo(
    string RaceId, int Size, int Scout, int RegenFort, string ProtectionId, Bank Growth, int ResourceMoney);

/// <summary>Строка <c>Globals/Gbuild.DBF</c>. <see cref="Category"/> — <c>Lbuild.dbf</c>
/// (0 гильдия, 1 лечебница, 2 башня магии, 3 <b>ветка юнитов</b>), ранг 1.
/// <see cref="RequiredId"/> пуст, если постройка корневая (в файле там <c>g000000000</c>).</summary>
public sealed record BuildingType(
    string Id, string RequiredId, Bank Cost, int Level, int Category, int Branch);

/// <summary>Строка <c>Globals/Gunits.dbf</c> в части найма: цена (<c>ENROLL_C</c>) и постройка,
/// без которой юнит не нанимается (<c>ENROLL_B</c>, пусто — нанимается всегда).</summary>
public sealed record UnitHire(string UnitId, string RaceId, Bank Cost, string BuildingId, int Level);

/// <summary>
/// Справочники экономики, уже прочитанные из <c>.dbf</c> (<c>LastEdict.Rules</c> файлов не читает).
/// Собирает <c>LastEdict.Data.Map.EconomyLoader</c>.
/// </summary>
/// <param name="IncomeBonusPercent">
/// <c>GVars: INCOME_E/A/H/V</c> по индексу <see cref="Difficulty"/> (30/0/0/0 в ванили).
/// Ранг 1: это не «бонус столицы», как значилось в ТЗ задачи, а надбавка к <b>итогу</b> дневного
/// дохода по сложности партии — каждая из шести валют домножается на <c>1 + pct/100</c>
/// (<c>0x5da776..0x5da79c</c>, множитель берётся полем <c>0x12c/0x130/0x134/0x138</c>
/// структуры <c>GlobalVariablesData</c>, то есть ровно <c>INCOME_E/A/H/V</c>).
/// </param>
/// <param name="CrystalProfit">
/// <c>GVars: CRYSTAL_P</c> = 50. Ранг 1: это <b>дневная выработка одной жилы</b>, а не цена
/// продажи — <c>CMidCrystal::getIncome</c> (<c>0x606497</c>) кладёт это число в валюту жилы.
/// </param>
public sealed record EconomyRules(
    IReadOnlyDictionary<string, RaceInfo> Races,
    IReadOnlyList<CityInfo> Cities,
    IReadOnlyDictionary<string, BuildingType> Buildings,
    IReadOnlyDictionary<string, UnitHire> Hire,
    IReadOnlyList<int> IncomeBonusPercent,
    int CrystalProfit,
    int SellRatio,
    int ConstructionUrgency)
{
    /// <summary>Расы нейтралов (<see cref="RaceInfo.Neutral"/>) — хозяев ничьих отрядов и объектов. Считается заново: рас единицы, а кэш в
    /// записи вошёл бы в её равенство и пережил бы <c>with</c> с другими расами.</summary>
    public IReadOnlySet<string> NeutralRaces =>
        Races.Where(p => p.Value.Neutral).Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Игрок <paramref name="playerId"/> — нейтралы; нет такого игрока — тоже (владелец «никто»).</summary>
    public bool IsNeutral(MapState map, string playerId) =>
        !map.Players.TryGetValue(playerId, out var p) || NeutralRaces.Contains(p.RaceId);

    /// <summary>Своя земля рода <paramref name="raceType"/>: признак первой расы этого рода (<see cref="RaceInfo.Terrain"/>); такой расы нет —
    /// по роду (<see cref="LegacyRaces.Terrain"/>). Дипломатия движка (отношения, союзы, доля равнины) ведётся по паре родов
    /// (<c>Relation(Race1, Race2)</c>), а земля — признак расы из содержимого.</summary>
    public int TerrainOf(int raceType) =>
        Races.Values.FirstOrDefault(r => r.RaceType == raceType)?.Terrain ?? LegacyRaces.Terrain(raceType);

    public int BonusPercent(Difficulty difficulty) =>
        (int)difficulty >= 0 && (int)difficulty < IncomeBonusPercent.Count
            ? IncomeBonusPercent[(int)difficulty]
            : 0;
}

/// <summary>
/// Экономика хода: дневной доход, цена найма, можно ли строить.
/// <para>
/// <b>Дневной доход — ранг 1</b>, прочитан в дизассемблере <c>computePlayerDailyIncome</c>
/// (Gog <c>Discipl2.exe</c> 4 474 880 байт, <c>0x5da55f</c>; разбор — capstone, см. отчёт
/// задачи 7 и <c>docs/re-damage-max.md</c> в <c>disciples-modding</c> как образец метода):
/// </para>
/// <code>
/// player = objectMap[playerId];              // нет игрока -> пустая казна
/// if (isRaceUnplayable(player.race)) return 0;      // 0x5e53a6, нейтралы не получают ничего
/// total = 0
/// foreach (CMidCrystal c in objectMap)              // ЖИЛЫ, и только они
///     if (ground(c.pos) is plain or forest          // 0x5e5c78 + глобалы 0x837318/0x837338
///         &amp;&amp; terrain(c.pos) == terrainOf(player.race))  // 0x5e5bc2 vs 0x5e515e
///         total += bank[c.resource] = GVars.CRYSTAL_P;       // 0x606497
/// capital = capitalOfRace(player.race);             // 0x5e9450 -> 0x5e9352
/// total += Grace[owner(capital).race].INCOME;       // 0x606d36 -> 0x5a5bc6
/// pct = GVars.INCOME_{E|A|H|V}[difficulty];         // 0x587985
/// foreach currency: v = v + v*pct/100;              // 0x5838ac
/// </code>
/// <para>
/// Из этого следует три вещи, которых ТЗ задачи не предполагало и которые проверяет харнесс:
/// <b>города дохода не дают вовсе</b> (в цикле только <c>CMidCrystal</c>, а <c>GcityInf.RES_M</c>
/// в доход не входит), доход столицы — это <c>Grace.INCOME</c> целиком, а жила даёт мана/золото
/// только когда стоит на <b>своей</b> земле: землю переводит жезл (<c>CMidRod</c>), а её текущая
/// принадлежность уже лежит в клетке (<see cref="Tile.Terrain"/>).
/// </para>
/// </summary>
public static class Economy
{
    /// <summary><c>Lbuild.dbf</c>: 3 = <c>L_UNIT</c>, ветка найма. Ранг 1.</summary>
    public const int UnitBuildingCategory = 3;

    /// <summary><c>Lbuild.dbf</c>: 0 = <c>L_GUILD</c>, гильдия (<c>0x8379e0</c>). Ранг 1.</summary>
    public const int GuildCategory = 0;

    /// <summary>Есть ли у игрока гильдия (<see cref="GuildCategory"/>): без неё вора нет в списке лидеров найма
    /// (<c>0x5d475e</c> → <c>0x5d4857</c>: вор, <c>0x41b399</c>, кладётся, только если <c>0x5f94a7</c> нашёл постройку
    /// <c>0x8379e0</c>). Ранг 1.</summary>
    public static bool HasGuild(IEnumerable<string> buildings, IReadOnlyDictionary<string, BuildingType> catalog) =>
        buildings.Any(b => catalog.TryGetValue(b, out var t) && t.Category == GuildCategory);

    /// <summary>Потолок и пол любой валюты казны: <c>Bank</c> движка — шесть <c>int16</c>, и
    /// каждое сложение зажимает величину в [0, 9999] (<c>0x586642</c>, <c>0x586a6f</c>). Ранг 1.</summary>
    public const int CurrencyMax = 9999;

    /// <summary>
    /// Дневной доход игрока. Формула — в описании класса (ранг 1, дизассемблер).
    /// <paramref name="difficulty"/> берётся из <c>ScenarioInfo.DIFFGAME</c> сохранения; в
    /// <see cref="MapState"/> этого поля нет, поэтому оно приходит параметром.
    /// </summary>
    /// <param name="ownLand">
    /// Своя земля игрока НА ЭТОТ ДЕНЬ. Земля растёт каждый день (<see cref="LandModel"/>), а
    /// <see cref="MapState"/> — это старт партии, поэтому текущее пятно приходит параметром;
    /// <c>null</c> — считать по стартовой карте (<see cref="Tile.Terrain"/>), как раньше.
    /// </param>
    /// <exception cref="ArgumentException">Нет такого игрока или его расы в справочнике.</exception>
    public static Bank DailyIncome(
        MapState map, string playerId, EconomyRules rules, Difficulty difficulty = Difficulty.Average,
        IReadOnlySet<(int X, int Y)>? ownLand = null)
    {
        var player = PlayerOf(map, playerId);
        if (!rules.Races.TryGetValue(player.RaceId, out var race))
            throw new ArgumentException($"расы {player.RaceId} нет в Grace.dbf", nameof(playerId));
        if (!race.Playable)
            return Bank.Zero;

        // Захваченный игрок дохода не получает вовсе: CEffectPlayerTurn::apply (0x43AB41) на
        // 0x43AB89 проверяет isCaptured и ОБНУЛЯЕТ казну дохода (0x586B5A) вместо расчёта
        // (0x5DA55F). Ранг 1 — код и корпус: у Империи S073PL0009 (захвачена на 21-м ходу) строк
        // income после 20-го нет, у Нежити S073PL000c — после 25-го, у S143PL0002 в m22-capture3
        // (захвачена на 1-м ходу) нет ни одной; харнесс income_after_capture задачи 14.
        if (player.CapturedBy.Length > 0)
            return Bank.Zero;

        // Движок ищет столицу ПО РАСЕ игрока и берёт доход расы её ТЕКУЩЕГО владельца
        // (0x5e9450 -> 0x5e9313 -> 0x5a5bc6); здесь взят доход расы самого игрока. Задачей 14 это
        // перестало быть упрощением: владелец CCapital не меняется НИКОГДА (план Ф7 §1.9, в
        // CEffectCaptureCapital::apply смены владельца нет), поэтому раса владельца столицы — это
        // всегда раса игрока, а единственный случай, где величины могли бы разойтись (столица
        // взята, игрок ещё ходит), выше отсечён нулём.
        var total = Clamp(race.Income);

        int ownTerrain = race.Terrain;
        foreach (var node in map.Objects.Values)
        {
            if (!node.Kind.Equals(CrystalKind, StringComparison.OrdinalIgnoreCase)) continue;
            if (!map.Contains(node.X, node.Y)) continue;
            var tile = map.At(node.X, node.Y);
            if (tile.Ground is not (Ground.Plain or Ground.Forest)) continue;
            bool mine = ownLand is null ? tile.Terrain == ownTerrain : ownLand.Contains((node.X, node.Y));
            if (!mine) continue;
            total = Clamp(total + Currency(node.Resource, rules.CrystalProfit));
        }

        return Scale(total, rules.BonusPercent(difficulty));
    }

    /// <summary>Класс id жилы (<c>S001<b>CR</b>0003</c>) — жилы маны и золотые прииски.</summary>
    private const string CrystalKind = "CR";

    /// <summary>Казна, в которой одна валюта. Индекс — <c>Lres.dbf</c>
    /// (0 <c>L_GOLD</c>, 1 <c>L_RED</c>, 2 <c>L_YELLOW</c>, 3 <c>L_ORANGE</c>, 4 <c>L_WHITE</c>,
    /// 5 <c>L_BLUE</c>), тот же порядок, что у полей <see cref="Bank"/>. Ранг 1: этот же порядок
    /// перечисляет <c>CMidCrystal::getIncome</c> (<c>0x606497</c>). Неизвестный индекс — пусто,
    /// как и в движке (там ни одна ветка не совпала).</summary>
    public static Bank Currency(int resource, long amount) => resource switch
    {
        0 => new Bank(amount, 0, 0, 0, 0, 0),
        1 => new Bank(0, amount, 0, 0, 0, 0),
        2 => new Bank(0, 0, amount, 0, 0, 0),
        3 => new Bank(0, 0, 0, amount, 0, 0),
        4 => new Bank(0, 0, 0, 0, amount, 0),
        5 => new Bank(0, 0, 0, 0, 0, amount),
        _ => Bank.Zero,
    };

    /// <summary>Цена найма юнита БАЗОВОГО уровня (<c>Gunits.ENROLL_C</c>). Ростер наёмника
    /// предлагает и прокачанных — их цену считает
    /// <see cref="HireCost(UnitHire, int, int, DynUpgrade?, DynUpgrade?)"/>.</summary>
    /// <exception cref="ArgumentException">Такого типа юнита нет в <c>Gunits.dbf</c>.</exception>
    public static Bank HireCost(string unitTypeId, EconomyRules rules) =>
        rules.Hire.TryGetValue(unitTypeId, out var hire)
            ? hire.Cost
            : throw new ArgumentException($"юнита {unitTypeId} нет в Gunits.dbf", nameof(unitTypeId));

    /// <summary>
    /// Цена найма юнита уровня <paramref name="level"/>: <c>ENROLL_C</c> базового типа плюс шаги
    /// <c>GDynUpgr.ENROLL_C</c> — тот же рост, которым считаются статы
    /// (<see cref="UnitGrowth.StepsFor"/>).
    ///
    /// <para><b>Ранг 1 — прочитано в дизассемблере</b> (<c>CUnitGenerator</c>, GOG): в теле
    /// порождения реализации у банка <c>enrollCost</c> стоит ровно та же пара «умножить на число
    /// шагов, прибавить»: <c>0x598D61</c> <c>BankApi::multiply</c> (<c>0x58670C</c>) на <c>first</c> →
    /// <c>0x598D69</c> <c>BankApi::add</c> (<c>0x586642</c>), затем <c>0x598D74</c>/<c>0x598D7C</c>
    /// то же на <c>second</c>. Обе операции — <b>int16</b> и обе обрезают каждую валюту в
    /// <c>[0, 9999]</c> (<c>0x586676…</c> у сложения, <c>0x586750…</c> у умножения), поэтому здесь
    /// <see cref="Clamp"/> стоит после каждого шага, а не один раз в конце: у
    /// <c>g000uu8001</c> (<c>ENROLL_C</c> 9950, прибавка 1500) второй уровень уже упирается в
    /// потолок.</para>
    ///
    /// <para><b>Почему это именно <c>ENROLL_C</c>, а не другая цена.</b> Четыре одинаковых блока
    /// пишут в объект по адресам <c>+0x4C</c> (<c>0x598D5E</c>), <c>+0x58</c>, <c>+0x64</c>
    /// (<c>0x598E54</c>) и <c>+0x70</c> (<c>0x598ECF</c>) — подряд, с шагом 12 байт, ровно размер
    /// <c>Bank</c> (шесть <c>int16</c>). В <c>TUsSoldierImplData</c> четыре банка объявлены подряд
    /// в порядке <c>enrollCost, reviveCost, healCost, trainingCost</c>
    /// (<c>tools/d2probe/mss32/include/ussoldierimpl.h:52-55</c>), значит первый блок — цена найма.
    /// Три остальные в ванили нулевые у всех 502 строк <c>GDynUpgr</c> (см. <c>UnitCosts</c>).</para>
    ///
    /// <para><b>Вики (разбор §4 Н24) описывает не правило, а способ, которым набивали таблицу,</b>
    /// и описывает его неверно: «+10 % от изначальной за каждый уровень, округление до 5, минимум
    /// 5, максимум 700» совпадает с <c>GDynUpgr.ENROLL_C</c> лишь у 280 строк <c>Gunits</c> из 356,
    /// ещё 24 совпадают только с неокруглёнными 10 %, а потолок 700 опровергнут прямо в таблице
    /// (<c>g000uu8004</c> — 1600). Числа перепроверяет
    /// <c>WikiMapRulesTests.HireCost_WikiFormula_IsNotTheRule</c>.</para>
    /// </summary>
    /// <param name="hire">Строка найма базового типа (<c>UNIT_ID</c>, <c>ENROLL_C</c>,
    /// <c>LEVEL</c>).</param>
    /// <param name="level">Уровень предложения ростера (<c>UNIT_LEVEL</c>); ниже базового
    /// поднимается до базового — такого юнита движок не порождает.</param>
    /// <param name="dynUpgLevel"><c>Gunits.DYN_UPG_LV</c> базового типа.</param>
    /// <param name="first">Строка <c>GDynUpgr</c> по <c>DYN_UPG1</c>; нет — прибавки нет.</param>
    /// <param name="second">Строка <c>GDynUpgr</c> по <c>DYN_UPG2</c>; нет — прибавки нет.</param>
    public static Bank HireCost(UnitHire hire, int level, int dynUpgLevel,
                                DynUpgrade? first, DynUpgrade? second)
    {
        ArgumentNullException.ThrowIfNull(hire);
        var steps = UnitGrowth.StepsFor(hire.Level, dynUpgLevel, Math.Max(level, hire.Level));
        var cost = Clamp(hire.Cost + Times(first?.EnrollCost, steps.First));
        return Clamp(cost + Times(second?.EnrollCost, steps.Second));
    }

    /// <summary>Банк, умноженный на целое, с той же обрезкой, что у <c>BankApi::multiply</c>.</summary>
    private static Bank Times(Bank? bank, int steps) =>
        bank is null || steps <= 0 ? Bank.Zero
            : Clamp(bank.Map(v => v * steps));

    /// <summary>
    /// Кого игрок может нанять — <c>addPlayerUnitsToHireList</c> (Gog <c>0x5d4911</c>): солдаты расы
    /// (<see cref="RaceInfo.Soldiers"/>) в порядке <c>Grace</c>, у которых нет требуемой постройки
    /// (<c>ENROLL_B</c> пуст) либо она построена. Порядок несущий — по нему ИИ берёт <c>list[rand() % n]</c>
    /// (<see cref="AiStrategicRules.PickUnits"/>); ранг 1 по корпусу 20260918-probe65 (все выборки
    /// <c>ai_unit_pick</c> четырёх рас), листинг <c>0x5d4add</c>/<c>0x5d49df</c> не читался.
    /// </summary>
    public static IReadOnlyList<string> HireList(MapState map, string playerId, EconomyRules rules)
    {
        var player = PlayerOf(map, playerId);
        var built = new HashSet<string>(player.Buildings, StringComparer.OrdinalIgnoreCase);
        var soldiers = rules.Races.TryGetValue(player.RaceId, out var race) ? race.Soldiers : [];
        return soldiers
            .Where(id => rules.Hire.TryGetValue(id, out var u)
                         && (u.BuildingId.Length == 0 || built.Contains(u.BuildingId)))
            .ToList();
    }

    /// <summary>
    /// Тот же ответ, что у движковой <c>getBuildingStatus</c> (Gog <c>0x5dc9e8</c>), и в том же
    /// порядке проверок — порядок несущий: при нехватке казны И отсутствии требуемой постройки
    /// движок вернёт «не хватает казны», а не «нет требуемой».
    /// </summary>
    /// <param name="ignoreCost">Аргумент движка: пропускает проверки казны и «уже строил сегодня».</param>
    /// <param name="maxUnitLevel">Потолок уровня юнитов сценария (<c>ScenarioInfo.MAX_UNIT</c>);
    /// в <see cref="MapState"/> его нет, поэтому параметром. Ранг 1: движок сравнивает с ним
    /// уровень ТОЛЬКО у построек ветки юнитов (<c>0x5dc815</c>).</param>
    /// <param name="builtThisDay">Игрок уже строил в этот день (движок сравнивает поле игрока
    /// <c>+0x50</c> с текущим ходом, <c>0x5dca8e</c>); в <see cref="MapState"/> этого поля нет.</param>
    public static BuildStatus CanBuild(
        MapState map, string playerId, string buildingId, EconomyRules rules,
        bool ignoreCost = false, int maxUnitLevel = int.MaxValue, bool builtThisDay = false)
    {
        var player = PlayerOf(map, playerId);
        if (!rules.Buildings.TryGetValue(buildingId, out var building))
            throw new ArgumentException($"постройки {buildingId} нет в Gbuild.DBF", nameof(buildingId));
        var built = new HashSet<string>(player.Buildings, StringComparer.OrdinalIgnoreCase);

        if (building.Category == UnitBuildingCategory && building.Level > maxUnitLevel)
            return BuildStatus.ExceedsMaxLevel;
        if (built.Contains(building.Id))
            return BuildStatus.AlreadyBuilt;
        if (!ignoreCost && NotEnough(player.Bank, building.Cost))
            return BuildStatus.InsufficientBank;
        if (!ignoreCost && builtThisDay)
            return BuildStatus.AlreadyBuiltThisDay;
        if (building.RequiredId.Length > 0 && !built.Contains(building.RequiredId))
            return BuildStatus.NoRequiredBuilding;
        // ponytail: ранг 2. Движковая проверка (0x5dc9d4 -> 0x5f9e37) сравнивает у постройки-
        // кандидата две величины со списком построенного; по данным Gbuild этими величинами могут
        // быть только BRANCH и LEVEL — в каждой расе на пару (ветка, уровень) приходится ровно
        // одна доступная постройка (Империя, ветка 0, уровень 4: Паладин | Ангел | Верховный
        // инквизитор). Харнесс по build_status подтвердит или опровергнет на живом журнале.
        if (building.Category == UnitBuildingCategory && rules.Buildings.Values.Any(
                b => b.Category == UnitBuildingCategory && b.Branch == building.Branch
                     && b.Level == building.Level && built.Contains(b.Id)))
            return BuildStatus.SiblingUnitBuilding;
        return BuildStatus.CanBeBuilt;
    }

    /// <summary>Не хватает хотя бы одной валюты (<c>0x5869e6</c> в проверке статуса 3).</summary>
    public static bool NotEnough(Bank purse, Bank cost)
    {
        ArgumentNullException.ThrowIfNull(purse);
        return !purse.Covers(cost);
    }

    private static MapPlayer PlayerOf(MapState map, string playerId) =>
        map.Players.TryGetValue(playerId, out var p)
            ? p
            : throw new ArgumentException($"игрока {playerId} нет на карте", nameof(playerId));

    /// <summary>Зажимает каждую валюту в [0, <see cref="CurrencyMax"/>] — так делает каждое
    /// сложение казны в движке.</summary>
    public static Bank Clamp(Bank b)
    {
        ArgumentNullException.ThrowIfNull(b);
        return b.Map(v => Math.Clamp(v, 0, CurrencyMax));
    }

    /// <summary>Надбавка по сложности: <c>v + v*pct/100</c> с усечением к нулю — ровно
    /// <c>imul/idiv</c> движка (<c>0x5838ac</c>), затем запись в казну зажимает величину.</summary>
    private static Bank Scale(Bank b, int percent) => Clamp(b.Map(v => v + v * percent / 100));
}
