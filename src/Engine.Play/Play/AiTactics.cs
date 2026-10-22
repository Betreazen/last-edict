using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Play;

/// <summary>Действие тактики отряда ИИ: тип (слот 1), отряд, цель (слот 3), точка (слот 4); у действий к своей
/// крепости — шаг (<see cref="AiFortStep"/>), у <c>AssistMove</c> — режим «прямо к цели»; у атак — «напрямую»
/// (<c>+0x5c</c>) и усечение подхода <see cref="Reach"/> (N, <c>+0x20</c>) от фабрики (<c>docs/ai-attack.md</c> §8); у
/// производных кандидатов атаки — подготовка (<see cref="AiPrep"/>): пока атака не готова, их отряд, цель и точка — атаки.
/// У сценарных приказов — цель оценщика (<see cref="Eval"/>), по которой судит фильтр приказа.</summary>
public sealed record AiTacticOption(AiActionType Type, string Stack, string Target, int X, int Y,
                                    AiFortStep? Fort = null, bool Direct = false, AiPrep? Prep = null, int Reach = 0,
                                    string Guest = "", AiMoveAct Act = AiMoveAct.Move, AiEvalTarget? Eval = null)
{
    /// <summary>Отряд ключа памяти (слот отряда действия): у выселения — выселяемый гость (зонд пишет его же в
    /// <c>action.stack</c>), у подмоги — помощник (<c>0x6d0dc4</c>: зонд пишет в <c>action.stack</c> его, память исполнения — по нему
    /// же, так что «сегодня 1/3» снимает именно этого помощника; прежде ключ был атакующего, и помощник, вставший рядом, выбирался
    /// снова до потолка оценок — human, день 37: <c>KC0077</c> 16 раз), у прочих — сам отряд.
    /// <para>Слот 2 производных (<c>0x458c01</c>, общий у <c>0x6d0dc4</c> и <c>0x6d0c94</c>) смотрит флаг готовности атаки <c>+0x50</c>: не
    /// готова — отряд атаки (атакующий), готова — свой (помощник). Выбор, у которого готовность уже проверена (<see cref="Ready"/> =
    /// false), показывает поэтому атакующего — карта 23, день 41: <c>S143KC0007</c> AttackCity FT0012 подмогой, атака не дойдёт сегодня.</para></summary>
    public string KeyStack => Guest.Length > 0 ? Guest : Prep is { Kind: AiPrepKind.Assist } prep && Ready != false ? prep.Id : Stack;

    /// <summary>Готовность атаки производного (<see cref="AiTactics.AttackReady"/>) в миг выбора — для трассы выбора (слот 2 у движка);
    /// null — не проверялась (кандидаты, ключ памяти вставки).</summary>
    public bool? Ready { get; init; }
}

/// <summary>Цель оценщика построителя (слот 3 <c>[vtbl+0xc]</c> — точка, цель — с чем его создал построитель): фильтр приказа
/// (<c>0x465c90</c>, у <c>L_DEFEND_LOCATION</c> — <c>0x44f8c8</c>) судит оценщик до счёта, а не готовое действие. У
/// <c>DefendLocation</c> (<c>0x454c78</c> → <c>0x4550d1(отряд, id, точка)</c>) и <c>MoveToLocation</c> оценщик держит цель приказа —
/// врага (или помеху к нему) или само место, а действие (<c>0x460d27</c> при счёте) может оказаться помехой на пути: мешком или
/// чужим отрядом. Журнал 20261014-replay, NM KC009b <c>L_MOVE_TO_LOCATION</c> → LO0006: построитель <c>0x45b2c1</c> вставил мешок
/// BG001a (тип 0, seq 246620 / r2b 2051608) и отряд KC0074 (тип 0x14, seq 257782 / 2059251), оба со счётом 150. Ранг 1.</summary>
public readonly record struct AiEvalTarget(string Target, int X, int Y);

/// <summary>Подготовка к атаке — элемент плана <c>0x471ec7</c>: заклинание (<see cref="Id"/> — заклинание,
/// <see cref="CastTarget"/> и точка — куда бросать, <see cref="SpellBank"/> — со вторым кошельком), подмога
/// (<see cref="Id"/> — помощник, точка — точка атаки) или итоговая атака (<see cref="AiPrepKind.Attack"/>).</summary>
public sealed record AiPrep(AiPrepKind Kind, string Id = "", string CastTarget = "", int X = -1, int Y = -1,
                            bool SpellBank = false);

/// <summary>Ход тактики одного игрока ИИ: веса <c>GAI</c> по отношению игрока, сетка влияния (строится раз на ход,
/// <c>0x461c85</c>), память статусов и захваченные цели (<c>+0x513c</c>: цель → отряд, статус 5 без
/// перезаписи, <c>0x469a37</c>).</summary>
public sealed class AiTacticTurn(string player, AiWeights weights, InfluenceMap grid, int day, AiTacticMemo memo)
{
    public string Player { get; } = player;
    public AiWeights Weights { get; } = weights;
    public InfluenceMap Grid { get; } = grid;
    public int Day { get; } = day;
    public AiTacticMemo Memo { get; set; } = memo;
    public Dictionary<string, string> Captured { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Исключённые цели кэша ближайших целей (<c>data2 +0x6c</c>): цели без пути. Живут, пока оценивается
    /// тот же отряд на той же клетке (<c>0x45e4c7</c>).</summary>
    public HashSet<string> Excluded { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Отряд и клетка, для которых собраны <see cref="Excluded"/>.</summary>
    public (string Stack, int X, int Y) ExcludedFor { get; set; } = ("", -1, -1);

    /// <summary>Радиус-фильтр кэша целей уже снят (<c>+0x51d8</c>): у «главного» отряда кончились действия, и он
    /// оценивается без флага. Живёт до нового выбора отряда (<see cref="Select"/>) или до следующей оценки без действий
    /// (<c>0x46654e</c>).</summary>
    public bool RadiusSpent { get; set; }

    /// <summary>Слабые атаки разрешены — байт <c>+0x18</c> обёртки отряда <c>IAiStack</c> (слот <c>0x24</c> читает,
    /// <c>0x28</c> ставит). Ставит его только <c>0x4665cc</c>: оценка кончилась без действия, а набор исключений кэша
    /// вырос. Без него фабрика атаки отряда (<c>0x45129b</c>) и крепости (<c>0x450444</c>) при не взятом пороге силы
    /// действия не создаёт — ни атаки, ни производных. Обёртка новая при каждом новом выборе отряда (<c>0x466a9f</c>).</summary>
    public bool WeakAttacks { get; set; }

    /// <summary>Новый выбор отряда (<c>0x466a7d</c>…): свежая обёртка и сброс <c>+0x51d8</c>/<c>+0x51d9</c>
    /// (<c>0x466aac</c>). Продолжение того же отряда после исполнения (<c>0x466aba</c>) их не трогает.</summary>
    public void Select()
    {
        RadiusSpent = false;
        RadiusFlag = false;
        WeakAttacks = false;
        FilterFor = ("", -1, -1);
    }

    /// <summary>Точка фильтра приказа (<c>0x465c90</c>, <see cref="AiTactic.Admits"/>) и чей это отряд: позиция отряда для ИИ
    /// (<c>0x5ed9b2</c>) в миг нового выбора — фильтр строит обёртка отряда (<c>0x465af9</c>), а она новая только при новом выборе
    /// (<c>0x466a9f</c>); продолжение того же отряда (<c>0x466aba</c>) ходит со старой точкой. Ранг 1 по листингу и журналу
    /// <c>20261016e-av</c> (greenskins, <c>L_DEFEND_CITY</c>): <c>S092KC0028</c> выбран на (63, 12), и <c>AttackStack</c> на (67, 17)
    /// движок пропускает и после шагов к (61, 7) и в крепость (57, 3) — от точки выбора 5 клеток, от текущей 10–14.</summary>
    public (string Stack, int X, int Y) FilterFor { get; set; } = ("", -1, -1);

    /// <summary>Множество целей тактики (<c>CAiTactic +0x51dc</c>, <see cref="ScenarioObjectives.AiTargets"/>): заполняет вход в
    /// тактику (<c>0x4662d7</c>) с дня <see cref="AiTacticScore.WinTargetsDay"/> по сложности партии, до него пусто. Цели из него
    /// идут в векторы кэша сверх тройки (<see cref="AiTacticScore.PickThree{T}"/>), радиус у чужих столиц их не выбрасывает
    /// (<c>0x45f5f1</c>), а действие с такой целью получает +100 (<see cref="AiTacticScore.TargetSetBonus"/>, <c>0x466cd8</c>).</summary>
    public IReadOnlySet<string> WinTargets { get; init; } = new HashSet<string>();

    /// <summary>Главный враг (<c>CAiTactic +0x51c0</c>, <see cref="AiTactics.MainEnemy"/>): игрок, чьи отряды счёт атаки
    /// (<c>0x453b2c</c>, слот <c>0x2c</c> <c>IAiTacticInternal</c>) берёт ×1,5. Пусто — нет.</summary>
    public string MainEnemy { get; init; } = "";

    /// <summary>Радиус текущей оценки (<c>0x45e70d</c>) и чужие столицы (их вход, <c>0x45f522</c>).</summary>
    public int Radius { get; set; }

    /// <summary>Флаг радиуса текущей оценки (<c>+0x51d9</c>, <c>0x466ac7</c>): отряд первый среди своих сильнейших начала хода и
    /// радиус не снят. От дня не зависит — после порога по дням радиус 0, а флаг стоит, и оценка без действий сперва снимает его
    /// (<c>0x46653d</c>), не разрешая слабых атак.</summary>
    public bool RadiusFlag { get; set; }

    public IReadOnlyList<(int X, int Y)> ForeignCapitals { get; set; } = [];

    /// <summary>Списки сил (<c>0x4760b0</c>), которые получает <c>CAiTactic</c> (конструктор <c>0x46556e</c> из
    /// <c>0x4718ba</c>): свои сильнейшие — <c>+0x51fc</c> (<c>CAi +0x24</c>), чужие — <c>+0x51f8</c> (<c>CAi +0x34</c>). Собраны в
    /// начале хода и заново — после каждого уничтоженного отряда (<see cref="AiTactics.StackDestroyed"/>).</summary>
    public AiForces Forces { get; set; } = new([], []);

    /// <summary>R250 партии со счётчиком: броски тактики — выбор заклинания перед атакой (<c>0x472142</c>). Харнесс на
    /// время окна ставит сюда броски журнала (<c>JournalRolls</c> (LastEdict.Verification), схема 7.5).</summary>
    public CountingRng Rng { get; set; } = new(new EngineRng());

    /// <summary>Кошелёк тактики (<c>CAiTactic +0x515c</c>, слот <c>0x14</c> <c>IAiTacticInternal</c>): в начале хода —
    /// доля <c>B_ISO</c> казны (<c>0x4659e2</c>), дальше из него вычитаются воскрешение в крепости при подготовке
    /// (<c>0x586578</c>; лечение — ноль, см. <see cref="AiStackUnits.CareInFort"/>) и обучение у тренера (<c>0x46d322</c>).
    /// Читает его построитель тренера (<c>0x46ccef</c>).</summary>
    public Bank Purse { get; set; } = Bank.Zero;

    /// <summary>Трата из кошелька тактики (вычитание по валютам с зажимом в ноль).</summary>
    public void Spend(Bank cost) => Purse = Treasury.Pay(Purse, cost);

    /// <summary>Цель уже взял другой отряд (<c>0x469a75</c>) — счёт ×0,1.</summary>
    public bool CapturedByOther(string stack, string target) =>
        target.Length > 0 && Captured.TryGetValue(target, out var by) && !by.Equals(stack, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Тактика ИИ из карты — срез 5 <c>docs/ai-turn-plan.md</c>. Кандидаты строятся по списку типов отряда
/// (<see cref="AiTacticLists.Build"/>) построителями движка (<c>docs/ai-tactic-candidates.md</c>), проходят фильтр
/// приказа (<see cref="AiTactic.Admits"/>) и вставку (<see cref="AiTactic.Insert{T}"/>) в том же порядке, выбор —
/// <see cref="AiTactic.Pick{T}"/> с проверкой пути. Перенесены типы, чьи построители разобраны: <c>AssistMove</c>,
/// <c>GiveItems</c>, <c>HealRevive</c>, <c>SecureCity</c>, <c>DefendCity</c>, жезлы и мешки; остальные пока кандидатов
/// не дают.
/// </summary>
public static partial class AiTactics
{
    /// <summary>Типы, для которых есть построитель из карты.</summary>
    public static readonly IReadOnlySet<AiActionType> Ported = new HashSet<AiActionType>
    {
        AiActionType.AssistMove, AiActionType.GiveItems, AiActionType.HealRevive, AiActionType.SecureCity,
        AiActionType.DefendCity, AiActionType.PlantRod, AiActionType.AttackRod, AiActionType.TakeBag,
        AiActionType.TakeBagRoam, AiActionType.AttackStack, AiActionType.AttackParty, AiActionType.AttackStackRoam,
        AiActionType.AttackCity, AiActionType.AttackCityRoam, AiActionType.VisitRuin, AiActionType.Guard,
        AiActionType.VisitTrainer, AiActionType.VisitMerchant, AiActionType.VisitMage, AiActionType.NobleStack,
        AiActionType.NobleCity, AiActionType.NobleMerchant, AiActionType.AttackStackBezerk, AiActionType.AttackCityBezerk,
        AiActionType.HireMercs, AiActionType.VisitTemple, AiActionType.Retreat, AiActionType.ChangeOrder, AiActionType.StayInside,
        AiActionType.DefendParty, AiActionType.DefendLocation, AiActionType.MoveToLocation, AiActionType.HireUnit,
        AiActionType.EnterCityRoam, AiActionType.SummonAttack, AiActionType.MoveToStack,
    };

    /// <summary>Урон отряда, с которого строится <c>HealRevive</c> (<c>0x4576dd</c>, константа <c>0x6d0ac0</c>).</summary>
    public const double HealDamage = 0.1;

    /// <summary>Начало хода тактики игрока: веса по его отношению (<c>GAI.CATEGORY</c>) и сетка своих крепостей;
    /// списки сил — начала хода (<paramref name="forces"/>, иначе <see cref="GameSession.ForcesOf"/>), иначе по карте сейчас. Нет
    /// правил или строки весов — тактики нет.</summary>
    public static AiTacticTurn? Begin(GameSession session, string playerId, AiTacticMemo memo, AiForces? forces = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var map = session.Map;
        if (session.Book is not { } book || !map.Players.TryGetValue(playerId, out var player)
            || !book.AiWeights.TryGetValue(player.Attitude, out var weights)) return null;
        var forts = Forts(map).Where(f => Same(f.Owner, playerId))
            .Select(f =>
            {
                var (x, y) = Entrance(f);
                return (x, y, AiTacticScore.FortRadius(f.Kind == SiteKind.Capital, f.Size ?? 0));
            });
        return new AiTacticTurn(playerId, weights, AiTacticScore.InfluenceGrid(map.Size, forts), map.Turn, memo)
        {
            Forces = forces ?? session.ForcesOf(playerId) ?? AiSpells.Forces(session, playerId),
            Rng = new CountingRng(session.AiRng),
            Purse = TacticPurse(session, player),
            MainEnemy = MainEnemy(session),
            WinTargets = WinTargets(session, playerId),
        };
    }

    /// <summary>
    /// Множество целей тактики на входе (<c>0x4662d7</c>, ранг 1): день партии (<see cref="MapState.Turn"/>) не меньше порога
    /// сложности партии (<see cref="GameInfo.Difficulty"/> = <c>DIFFGAME</c>, <see cref="AiTacticScore.WinTargetsDay"/>) — цели
    /// событий победы глазами игрока ИИ (<see cref="ScenarioObjectives.AiTargets"/>, флаг 0); иначе пусто. Строится заново на каждом
    /// входе, без событий сценария — пусто.
    /// </summary>
    public static IReadOnlySet<string> WinTargets(GameSession session, string playerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        if (session.Book is not { } book || state.Events.Events.Count == 0
            || session.Map.Turn < AiTacticScore.WinTargetsDay((Difficulty)state.Info.Difficulty)) return new HashSet<string>();
        return ScenarioObjectives.AiTargets(session.Map, state.Events, state.EventProgress,
                                            new EventRules(book.Economy.Races, book.Diplomacy), playerId);
    }

    /// <summary>
    /// Отряд уничтожен (<c>CCmdStackDestroyedMsg</c>: обработчик <c>0x475a74</c> → <c>0x474e64</c> → <c>0x471d4b</c>, ранг 1 по
    /// листингу): ИИ собирает списки сил <c>CAi +0x24</c>/<c>+0x34</c> заново (<c>0x4760b0</c>) — по карте сейчас, с нанятыми за ход
    /// и выросшими в бою юнитами; тактика хода смотрит в те же списки, и «главным» (радиус у чужих столиц, <see cref="SetRadius"/>)
    /// становится новый первый. Журнал probe72 (день 11, <c>S143PL0003</c>): после боя, где пал отряд, юниты <c>S143KC00a2</c> выросли
    /// до <c>UU0087</c> (сила 197,9 против 128,5 у <c>S143KC0002</c>, строки <c>ai_strength_units</c> с адресом <c>0x476393</c>), и
    /// радиус кэша целей ставится ему.
    /// </summary>
    public static void StackDestroyed(GameSession session, AiTacticTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        turn.Forces = AiSpells.Forces(session, turn.Player);
    }

    /// <summary>Доля, с которой игрок становится главным врагом (<c>[0x6d1e80]</c>).</summary>
    private const double MainEnemyShare = 0.4;

    /// <summary>
    /// Главный враг хода тактики (<c>0x465fc9</c>, зовёт вход в тактику <c>0x4659b1</c>; ранг 1 по листингу): по всем игрокам карты
    /// (<c>0x41cffd</c> — объекты типа <c>0x1e</c> по id; свой и нейтрал тоже) доля <c>0x4660d4</c> = (крепости игрока
    /// <c>CFortification +0x84</c> + кристаллы <c>CMidCrystal</c> на земле его расы — <c>0x5e5bc2</c>, <c>GetTerrainByRace</c>
    /// <c>0x5e515e</c>) / (все крепости + все кристаллы); наибольшая строго (равные — первый), и если она не меньше 0,4 — главный
    /// враг, иначе пусто. От того, чей ход, не зависит.
    /// </summary>
    public static string MainEnemy(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var map = session.Map;
        var races = session.Book!.Economy.Races;
        var forts = Forts(map).ToList();
        var crystals = map.Objects.Values.Where(o => o.Kind.Equals("CR", StringComparison.OrdinalIgnoreCase) && map.Contains(o.X, o.Y))
                          .Select(o => map.At(o.X, o.Y).Terrain).ToList();
        int total = forts.Count + crystals.Count;
        if (total == 0) return "";
        string best = "";
        double top = 0;
        foreach (var player in map.Players.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            int land = races.TryGetValue(player.RaceId, out var race) ? race.Terrain : LegacyRaces.Terrain(-1);
            double share = (double)(forts.Count(f => Same(f.Owner, player.Id)) + crystals.Count(t => t == land)) / total;
            if (share > top) (best, top) = (player.Id, share);
        }
        return top >= MainEnemyShare ? best : "";
    }

    /// <summary>Кошелёк тактики в начале хода (<c>0x4659e2</c>): у нейтрала (<c>0x5e5358</c>) пуст, иначе казна
    /// (<c>CMidPlayer +0x70</c>) × <c>B_ISO</c> / 100 по каждой валюте (<c>0x5838c0</c>); строка <c>GAI</c> — по отношению
    /// игрока (<c>0x58fac7</c>). Ранг 1 по листингу; смещение <c>+0x34</c> = <c>B_ISO</c> — по порядку полей (соседние
    /// геттеры <c>+0x38</c>…, как <c>I_H_MERCS +0x88</c>).</summary>
    public static Bank TacticPurse(GameSession session, MapPlayer player)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(player);
        if (NeutralOwner(session, player.Id)) return Bank.Zero;
        return session.Book!.AiTacticPurseShare.TryGetValue(player.Attitude, out int share)
            ? Treasury.Share(player.Bank, share) : Bank.Zero;
    }

    /// <summary>Крепости карты — столицы и деревни.</summary>
    public static IEnumerable<MapSite> Forts(MapState map) =>
        map.Sites.Where(f => f.Kind is SiteKind.Capital or SiteKind.Village);

    /// <summary>Вход крепости — правый нижний угол пятна (<c>0x5e94eb</c>, <c>pos + size − 1</c>).</summary>
    public static (int X, int Y) Entrance(MapSite fort) =>
        AiTacticScore.AnchorCell(fort.X, fort.Y, fort.Kind == SiteKind.Capital ? 5 : 4);

    /// <summary>Позиция объекта для ИИ (<c>0x5ed9b2</c>): отряд в крепости — вход крепости, иначе его клетка.</summary>
    public static (int X, int Y) Position(MapState map, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(stack);
        if (stack.Inside.Length > 0 && Forts(map).FirstOrDefault(f => Same(f.Id, stack.Inside)) is { } fort) return Entrance(fort);
        return (stack.X, stack.Y);
    }

    /// <summary>
    /// «Раненые в крепости» (слот 7 обёртки <c>0x4676ed</c>, ранг 1) для отряда в крепости: в группе есть павший (<c>0x46773b</c>:
    /// <c>0x5e98ad</c> — здоровье &gt; 0) или у кого-то здоровье в процентах (<c>hp·100 / max</c>, целочисленно) ниже <c>GAI.HP_STAYMIN</c>
    /// строки отношения хозяина (<c>0x46779d</c>, порог — <c>+0xc8</c> строки через объект приказа обёртки, слот <c>0x40</c>). До 25.09 порт
    /// ставил признак при любом уроне: human, день 45 — <c>KC0003</c> в столице с уроном 0,041 (163/170, 95 %) у порта держался одним лечением.
    /// </summary>
    public static bool InjuredInside(GameSession session, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        var map = session.Map;
        int stayMin = map.Players.TryGetValue(stack.Owner, out var owner)
                      && session.Book!.AiStayMinHp.TryGetValue(owner.Attitude, out int v) ? v : DefaultStayMinHp;
        foreach (string id in stack.UnitIds)
        {
            if (!map.Units.TryGetValue(id, out var unit)) continue;
            if (unit.Hp <= 0) return true;
            int max = session.HpMax(unit);
            if (max > 0 && unit.Hp * 100 / max < stayMin) return true;
        }
        return false;
    }

    /// <summary><c>GAI.HP_STAYMIN</c> во всех строках ванили.</summary>
    private const int DefaultStayMinHp = 66;

    /// <summary>
    /// Здоровье отряда для счёта лечения (<c>0x457bdb</c>, ранг 1): у каждого юнита группы (павшие тоже) <c>hp·100 / max</c> не ниже
    /// <c>GAI.HP_HEALMIN</c> (<c>0x457d39</c>) и у группы в целом — <c>Σhp·100 / Σmax</c> (<c>0x457e58</c>); иначе счёт <c>HealRevive</c> ×2.
    /// Порог — <c>+0xcc</c> строки <c>GAI</c> отношения хозяина (<c>0x58f7c5</c>), по соседству с <c>HP_STAYMIN</c> (<see cref="InjuredInside"/>).
    /// </summary>
    public static bool HealthyForHeal(GameSession session, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        var map = session.Map;
        int healMin = map.Players.TryGetValue(stack.Owner, out var owner)
                      && session.Book!.AiHealMinHp.TryGetValue(owner.Attitude, out int v) ? v : DefaultHealMinHp;
        int sumHp = 0, sumMax = 0;
        foreach (string id in stack.UnitIds)
        {
            if (!map.Units.TryGetValue(id, out var unit)) continue;
            int hp = Math.Max(unit.Hp, 0), max = session.HpMax(unit);
            if (max <= 0) continue;
            if (hp * 100 / max < healMin) return false;
            sumHp += hp;
            sumMax += max;
        }
        return sumMax <= 0 || sumHp * 100 / sumMax >= healMin;
    }

    /// <summary><c>GAI.HP_HEALMIN</c> во всех строках ванили.</summary>
    private const int DefaultHealMinHp = 50;

    /// <summary>
    /// Урон отряда (<c>0x468969</c>, ранг 1): наибольшее по юнитам <c>1 − ОЗ / ОЗmax</c> (ОЗmax — с модификаторами,
    /// слот <c>0x24</c> реализации); юнит, который один стоит в переднем ряду (клетки 0, 2, 4 — таблица
    /// <c>0x6e8928</c>, проверка <c>0x468a4c</c>), при уроне больше 0,5 считается с уроном 0,99.
    /// </summary>
    public static double Damage(GameSession session, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        double worst = 0;
        for (int i = 0; i < stack.UnitIds.Count; i++)
        {
            if (!session.Map.Units.TryGetValue(stack.UnitIds[i], out var unit)) continue;
            int max = session.HpMax(unit);
            double d = max <= 0 ? 0 : 1.0 - (double)unit.Hp / max;
            if (d > 0.5 && AloneInFront(stack, i)) d = 0.99;
            if (d > worst) worst = d;
        }
        return worst;
    }


    private static bool AloneInFront(MapStack stack, int unitIndex)
    {
        int own = -1;
        for (int c = 0; c < stack.Slots.Count; c++)
            if (stack.Slots[c] == unitIndex) { own = c; break; }
        if (!Formation.IsFront(own)) return false;
        return Formation.Front.Where(c => c != own).All(c => c >= stack.Slots.Count || stack.Slots[c] < 0);
    }

    /// <summary>Класс отряда (<see cref="AiTacticLists.Classify"/>): хозяин нейтрален — неиграбельная раса;
    /// категория лидера <c>UNIT_CAT</c>; жезл — <c>LEADER_CAT</c> 4 (ранг 2, как <see cref="AiStrategicRules.RoleOf"/>).</summary>
    public static AiStackKind KindOf(GameSession session, MapStack stack)
    {
        var book = session.Book!;
        var map = session.Map;
        bool neutral = !(map.Players.TryGetValue(stack.Owner, out var owner)
                         && book.Economy.Races.TryGetValue(owner.RaceId, out var race) && race.Playable);
        string type = map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId : "";
        var kind = book.CategoryOf(type) switch
        {
            UnitCategory.Noble => AiLeaderKind.Noble,
            UnitCategory.Summon => AiLeaderKind.Summon,
            UnitCategory.Illusion => AiLeaderKind.Illusion,
            _ => AiLeaderKind.Other,
        };
        return AiTacticLists.Classify(neutral, kind, book.LeaderCategoryOf(type) == RodLeaderCategory);
    }

    private const int RodLeaderCategory = 4;

    /// <summary>Действующий приказ и его цель (<c>+0xa8</c>/<c>+0xac</c>, при <c>L_NORMAL</c> — <c>+0xb8</c>/<c>+0xbc</c>).</summary>
    public static (StackOrder Order, string Target) OrderOf(MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return stack.Order == (int)StackOrder.Normal
            ? ((StackOrder)stack.AiOrder, stack.AiOrderTarget)
            : ((StackOrder)stack.Order, stack.OrderTarget);
    }

    /// <summary>Список типов действий отряда (<see cref="AiTacticLists.Build"/>) по признакам обёртки из карты: «только по
    /// воде» — <see cref="GameSession.WaterOnly"/>; «раненые в крепости» — <see cref="InjuredInside"/>.</summary>
    public static IReadOnlyList<AiActionType> Types(GameSession session, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        var map = session.Map;
        var fortInside = stack.Inside.Length > 0 ? Forts(map).FirstOrDefault(f => Same(f.Id, stack.Inside)) : null;
        bool captured = map.Players.TryGetValue(stack.Owner, out var owner) && owner.CapturedBy.Length > 0;
        var flags = new AiStackFlags(
            RiotCity: fortInside is { Kind: SiteKind.Village, RiotTurn: > 0 },
            OwnerCaptured: captured,
            InjuredInside: fortInside is not null && InjuredInside(session, stack),
            WaterOnly: session.WaterOnly(stack),
            LeaderAlive: stack.LeaderAlive,
            Inside: stack.Inside.Length > 0,
            Steal: OrderOf(stack).Order == StackOrder.Steal);
        return AiTacticLists.Build(KindOf(session, stack), flags);
    }

    /// <summary>Кандидаты отряда в порядке вставки движка: построители по списку <see cref="Types"/>, фильтр приказа,
    /// счёт, вставка со статусами памяти.</summary>
    public static IReadOnlyList<AiScored<AiTacticOption>> Candidates(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(stack);
        IReadOnlyList<AiScored<AiTacticOption>> list = [];
        var (order, orderTarget) = OrderOf(stack);
        var self = Position(session.Map, stack);
        if (turn.ExcludedFor != (stack.Id, self.X, self.Y))
        {
            turn.Excluded.Clear();
            turn.ExcludedFor = (stack.Id, self.X, self.Y);
        }
        SetRadius(session, turn, stack);
        // Точка фильтра (0x465c90): у L_DEFEND_LOCATION — центр места приказа (CMidLocation, 0x465e9a), радиус LO_RANGE; иначе — отряд
        // в миг нового выбора (AiTacticTurn.FilterFor).
        if (!Same(turn.FilterFor.Stack, stack.Id)) turn.FilterFor = (stack.Id, self.X, self.Y);
        var filterPoint = order == StackOrder.DefendLocation && session.State.Events.Zones.GetValueOrDefault(orderTarget) is { } zone
            ? (zone.X, zone.Y) : (turn.FilterFor.X, turn.FilterFor.Y);
        foreach (var type in Types(session, stack))
            foreach (var (action, score) in Build(session, turn, stack, type))
            {
                // Фильтр судит оценщик построителя (тип списка и цель оценщика), а не готовое действие: помеха на пути к месту
                // приказа (мешок, чужой отряд) проходит (20261014-replay, seq 246620, 257782; см. AiEvalTarget).
                var eval = action.Eval ?? new AiEvalTarget(action.Target, action.X, action.Y);
                bool isOrderTarget = orderTarget.Length > 0 && Same(eval.Target, orderTarget);
                int reach = Cheb(filterPoint, (eval.X, eval.Y));
                if (!AiTactic.Admits(order, type, isOrderTarget, reach, session.Book!.LocationRange)) continue;
                int today = turn.Memo.Status(action.KeyStack, action.Target, action.X, action.Y, turn.Day);
                // Фаза 2 (0x462135): сегодня уже «не пошёл» (1) или «провалено» (3) — действие до вставки не доходит.
                if (today is AiTactic.StatusStuck or AiTactic.StatusFailed) continue;
                list = AiTactic.Insert(list, action, score, today,
                                       turn.Memo.Status(action.KeyStack, action.Target, action.X, action.Y, turn.Day - 1));
            }
        return list;
    }

    /// <summary>Выбор (<see cref="AiTactic.Pick{T}"/>): у лучших четырёх проверка пути (<see cref="AiTacticEval.PathRescore"/>)
    /// — путь до точки ищется <see cref="PathFinder"/>; стоять в точке уже — путь есть. Цель без пути уходит в
    /// исключения кэша (<c>0x46903e</c> → <c>0x45f902</c>); если действия нет, а набор вырос, отряд оценивается заново
    /// (<c>0x46655e</c>) — кэш сдвигается на следующие цели. Оценка без действия (<c>0x46652e</c>): у «главного» отряда —
    /// повтор без радиуса; иначе радиус снова в силе, и если набор исключений вырос — повтор с разрешёнными слабыми
    /// атаками (<see cref="AiTacticTurn.WeakAttacks"/>, <c>0x4665cc</c>).
    /// <para>Повтор у движка — снова шаг выбора того же отряда (<c>0x46671f</c> → фаза 0 → 1, продолжение <c>0x466aba</c>), а он
    /// зовёт подготовку отряда (<c>0x466b68</c>, слот <c>0x44</c>): юниты гарнизона, зелья, снаряжение. <paramref name="reselect"/>
    /// делает её перед каждым повтором и отдаёт отряд после неё. Журнал 18.10, карта 14: у «главного» отряда каждого ИИ оценка с
    /// радиусом пустая (цели далеко), и в повторе без радиуса подготовка снова меняет местами два одинаковых юнита отряда и гарнизона
    /// (S143PL0002: UN0015 ↔ UN0017 — у движка в гарнизоне остаётся UN0015).</para></summary>
    public static AiScored<AiTacticOption>? Choose(GameSession session, AiTacticTurn turn, MapStack stack,
                                                   Func<MapStack>? reselect = null)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(stack);
        var routes = new RouteCache();   // карта за проходы не меняется — путь к той же точке не ищется заново
        for (int pass = 0; ; pass++)
        {
            if (pass > 0 && reselect is not null) stack = reselect();
            var candidates = Adjusted(session.Map, turn, stack, Candidates(session, turn, stack));
            // Набор исключений на входе фазы 3 (0x4665e5 → 0x45f9cc в +0x5184) — уже после фазы 2, где кэш сбрасывает исключения при
            // новом отряде или новой клетке (0x45e4c7, Candidates); фаза 4 без действия сравнивает с ним (0x46655e). Счёт до Candidates
            // брал прежний набор: отряд, вошедший в крепость (новая клетка), не оценивался заново — карта 16, S143KC0005 день 6.
            int excluded = turn.Excluded.Count;
            var pick = AiTactic.Pick(candidates, (a, s) => Rescore(session, turn, stack, a, s, routes));
            bool grown = turn.Excluded.Count != excluded;
            if (pick is null)
            {
                if (turn.RadiusFlag)
                {
                    // 0x46653d: у «главного» отряда действий нет — снова без флага радиуса (флаг, а не радиус > 0).
                    turn.RadiusSpent = true;
                    continue;
                }
                turn.RadiusSpent = false;              // 0x46654e
                if (grown) turn.WeakAttacks = true;    // 0x4665b5…0x4665cc
            }
            // Действие есть — фаза 4 его исполняет (0x466522 → 0x46671f), выросшие исключения ждут следующей оценки отряда; повтор по
            // исключениям — только в ветке без действия (0x46655e). Human, день 38: KC0088 берёт мешок BG0003, а дальние мешки без пути
            // уходят в исключения — следующая оценка находит новые дальние мешки, снова без пути, и включает слабые атаки.
            // Трасса ИИ зонда (ai_pick): у подмоги — слот 2 движка по готовности атаки; у заклинания — слоты 2–4 (0x45840a, 0x45842c,
            // 0x45844e): пока атака не готова, цель и точка — атаки, готова — литья (GameSession.TracePick).
            if (pick is { } chosen)
                session.TracePick(turn.Player, session.AiTrace is not null && chosen.Action.Prep is { Kind: AiPrepKind.Assist or AiPrepKind.Spell }
                    ? chosen with { Action = chosen.Action with { Ready = AttackReady(session, stack, chosen.Action, routes) } } : chosen,
                    candidates.Count);
            if (pick is not null || !grown || pass >= MaxReevaluations) return pick;
        }
    }

    // ------------------------------------------------------------------ «главный» отряд и радиус

    /// <summary>
    /// Лидеры для помощников в порядке движка (<c>0x45603b</c>, сортировка <c>0x4766b9</c>/<c>0x476604</c>): свои
    /// отряды с действующим <c>L_NORMAL</c>, лидер не призыв, не знатный и без жезла; по силе по убыванию, при
    /// равенстве по id.
    /// </summary>
    public static IReadOnlyList<MapStack> AssistLeaders(GameSession session, string playerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        return [.. session.Map.Stacks.Values
            .Where(s => Same(s.Owner, playerId) && OrderOf(s).Order == StackOrder.Normal && KindOf(session, s) == AiStackKind.Fighter)
            .OrderByDescending(s => Strength(session, s)).ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Сила отряда для списков «свои сильнейшие» и лидеров помощников (<c>0x476252</c>, <c>0x4763fc</c> →
    /// <c>0x5dcec7(карта, id, безГорода = 0, юниты, врагов = 1, 0, безКрепости = 1, полноеЗдоровье = 1)</c>, ранг 1): те же юниты,
    /// что у силы атаки — выросшие уровнем, с цепочкой модификаторов (<see cref="GroupStrength"/>), здоровье полное, брони
    /// укрепления нет. До 25.09 порт брал базовую реализацию без модификаторов — в legions на день 52 «главным» у <c>PL0009</c>
    /// выходил <c>KC0086</c> (198,9 против 187,5), а у движка это <c>KC0000</c> (246,7 против 205,6).</summary>
    public static double Strength(GameSession session, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        var constants = session.Book!.Rules.Constants;
        return Fighters(session, stack.UnitIds, fullHp: true).Sum(u => AiStrength.Unit(u, ignoreCity: false, enemies: 1, constants));
    }

    /// <summary>
    /// Радиус кэша целей (<c>0x45e70d</c>): у «главного» отряда, пока не снят (<see cref="AiTacticTurn.RadiusSpent"/>),
    /// и пока ход меньше <c>((размер − 48) / 24)·10 + 11</c> — половина карты; цели ближе радиуса (Чебышёв) к чужой
    /// столице в кэш не идут. «Главный» — первый в списке <c>CAiTactic+0x51fc</c> (<c>0x466ac7</c>), а это свои
    /// сильнейшие начала хода (<see cref="AiTacticTurn.Forces"/>: конструктор <c>0x46556e</c> получает <c>CAi +0x24</c>).
    /// </summary>
    private static void SetRadius(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        bool flag = !turn.RadiusSpent && turn.Forces.Own.Count > 0 && Same(turn.Forces.Own[0], stack.Id);
        turn.RadiusFlag = flag;
        turn.Radius = flag && map.Turn < ((map.Size - 48) / 24 * 10) + 11 ? map.Size / 2 : 0;
        var races = session.Book!.Economy.Races;
        turn.ForeignCapitals = turn.Radius == 0 ? [] :
            [.. Forts(map).Where(f => f.Kind == SiteKind.Capital && !Same(f.Owner, stack.Owner)
                                      && map.Players.TryGetValue(f.Owner, out var p) && races.TryGetValue(p.RaceId, out var r) && r.Playable)
                          .Select(Entrance)];
    }

    /// <summary>
    /// Проверка пути кандидата (слот 6). Атака отряда и крепости (<c>0x4506fb</c>) «напрямую» (<see cref="AiTacticOption.Direct"/>)
    /// пути не ищет — счёт остаётся, атака готова (<c>0x45aaf7</c>); иначе водному лидеру точка не на воде — NoScore без поиска
    /// (<c>0x468f81</c>), потом путь до точки с N фабрики (<c>0x45ab0b</c> → <c>0x46903e</c>, <see cref="AiTacticEval.PathRescore"/>);
    /// без пути в исключения кэша уходят объекты клетки точки (<see cref="PathExclusions"/>), а готовой атаку
    /// делает флаг <c>+0x50</c> поиска — сегодня встанет вплотную (<see cref="AttackReady"/>). Производные (<c>0x458917</c>,
    /// <c>0x4586cc</c>) отдают проверку своей атаке; подмога при готовой атаке ещё ищет путь помощника к точке атаки и
    /// остаётся, только если он сегодня дойдёт вплотную.
    /// </summary>
    /// <summary>Поправки фазы 2 после вставки всех кандидатов (цикл <c>0x466894</c>…<c>0x4668ae</c>, до сортировки фазы 3), каждому
    /// кандидату по очереди: сперва приоритет редактора цели (<c>0x466d5c</c>, <see cref="AiTacticScore.EditorPriority"/>: объект цели —
    /// слот <c>+0xc</c> → <c>findScenarioObjectById</c> → <c>IAiPriority</c>, см. <see cref="TargetPriority"/>; цель приказа — у
    /// <paramref name="stack"/>, выбранного отряда окна), затем +100 цели из множества целей тактики (<c>0x466cd8</c>). Строки
    /// <c>ai_tactic_candidate</c> пишут счёт вставки — до поправок.</summary>
    public static IReadOnlyList<AiScored<AiTacticOption>> Adjusted(MapState map, AiTacticTurn turn, MapStack stack,
                                                                   IReadOnlyList<AiScored<AiTacticOption>> list)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(list);
        return [.. list.Select(c =>
        {
            double s = AiTacticScore.EditorPriority(c.Score, TargetPriority(map, c.Action.Target), Same(c.Action.Target, stack.OrderTarget));
            return c with { Score = AiTacticScore.TargetSetBonus(s, turn.WinTargets.Contains(c.Action.Target)) };
        })];
    }

    /// <summary>Приоритет редактора объекта цели (<c>IAiPriority +4</c>, <c>0x466db1</c>): отряд (<see cref="MapStack.AiPriority"/>) или
    /// крепость, руина, мешок, кристалл, площадка (<see cref="MapSite.AiPriority"/>); null — объекта на карте нет (уничтожен, подобран)
    /// или он без <c>IAiPriority</c> (жезл, локация: <c>__RTDynamicCast</c> к <c>0x798b08</c> даёт null).</summary>
    public static int? TargetPriority(MapState map, string target)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (string.IsNullOrEmpty(target)) return null;
        if (map.Stacks.TryGetValue(target, out var s)) return s.AiPriority;
        foreach (var site in map.Sites)
            if (Same(site.Id, target)) return site.Gone || site.Kind == SiteKind.Rod ? null : site.AiPriority;
        return null;
    }

    /// <summary>Первый выбор оценки (<see cref="AiTactic.Pick{T}"/> с проверкой пути) без следа в ходе: исключения кэша, добавленные
    /// проверкой пути, откатываются. Для сверки выбора при состоянии движка (харнесс кандидатов).</summary>
    public static AiScored<AiTacticOption>? PickPreview(GameSession session, AiTacticTurn turn, MapStack stack,
                                                        IReadOnlyList<AiScored<AiTacticOption>> candidates)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var excluded = turn.Excluded.ToList();
        var routes = new RouteCache();
        var pick = AiTactic.Pick(Adjusted(session.Map, turn, stack, candidates),(a, s) => Rescore(session, turn, stack, a, s, routes));
        turn.Excluded.Clear();
        turn.Excluded.UnionWith(excluded);
        return pick;
    }

    private static double Rescore(GameSession session, AiTacticTurn turn, MapStack stack, AiTacticOption a, double s, RouteCache routes)
    {
        // Выселение (слот 6 0x45a7aa): гость не выйдет в точку (0x5e78a9) — NoScore; иначе путь гостя из крепости в соседнюю
        // с входом клетку (0x45ab0b) есть всегда.
        // 20.10 (replay-legions, день 1): после выхода — обычная проверка пути (0x45a80c → 0x45ab0b → 0x46903e) гостя (слот 8 действия —
        // гость, 0x45a6c5 → 0x467a29), режим 1, N 0 (0x45a6bd, 0x45a6b2). Нейтральный гость S073KC0004 деревни S073FT0004 к (91,17) пути не
        // имеет: у движка выселение отброшено (top [] seq 235065), порт выбирал его каждый день и стоял (статус 1).
        if (a.Fort == AiFortStep.KickOut)
        {
            if (!session.Map.Stacks.TryGetValue(a.Guest, out var guest) || !CanExit(session, guest, a.X, a.Y)) return AiTactic.NoScore;
            if (HasRoute(session, guest, a with { Stack = guest.Id }, routes)) return s;
            turn.Excluded.UnionWith(PathExclusions(session.Map, a.X, a.Y));
            return AiTactic.NoScore;
        }
        // Смена приказа (CAiChangeOrderAction 0x6cfbec): слот 6 — общий 0x467ab3, счёт без изменений, поиска пути нет (точки нет,
        // −1, −1). Прежде поиск к (−1, −1) давал NoScore, и отряд без цели приказа ничего не делал (human дни 41 и 43, KC009e и KC00a0).
        if (a.Type == AiActionType.ChangeOrder) return s;
        bool attack = a.Type is AiActionType.AttackStack or AiActionType.AttackParty or AiActionType.AttackCity
                          or AiActionType.AttackStackBezerk or AiActionType.AttackCityBezerk;
        if (!attack || !a.Direct)
        {
            // 0x468f81 до поиска: водному лидеру точка не на воде — NoScore без поиска и без исключений.
            if (session.WaterOnly(stack) && !session.IsWater(a.X, a.Y)) return AiTactic.NoScore;
            bool found = HasRoute(session, stack, a, routes);
            if (!found) turn.Excluded.UnionWith(PathExclusions(session.Map, a.X, a.Y));
            if (!found) s = AiTactic.NoScore;
            if (!attack || s == AiTactic.NoScore) return s;
        }
        if (a.Prep is not { Kind: AiPrepKind.Assist } prep || !AttackReady(session, stack, a, routes)) return s;
        return session.Map.Stacks.TryGetValue(prep.Id, out var helper)
               && ArrivesToday(session, helper, a with { Stack = helper.Id, Direct = false, Reach = 0 }, routes)
            ? s : AiTactic.NoScore;
    }

    /// <summary>
    /// Атака готова (флаг <c>+0x50</c>): «напрямую» — сразу (<c>0x45aaf7</c>), иначе — если поиск пути (<c>0x46903e</c>)
    /// сегодня приводит отряд вплотную к цели. Готовая атака отдаёт производным их собственное исполнение (заклинание,
    /// подмога); журнал human, день 44: <c>KC0003</c> при взятом пороге бросил удар с 7 клеток, не сходя с места.
    /// </summary>
    public static bool AttackReady(GameSession session, MapStack stack, AiTacticOption a)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(a);
        return AttackReady(session, stack, a, routes: null);
    }

    private static bool AttackReady(GameSession session, MapStack stack, AiTacticOption a, RouteCache? routes) =>
        a.Direct || ArrivesToday(session, stack, a, routes);

    /// <summary>
    /// Что уходит в исключения кэша, когда путь до точки не найден (<c>0x46903e</c> → <c>0x45f902</c>, ранг 1): объекты
    /// плана карты в клетке точки классов крепость, отряд, мешок, площадка, руина, кристалл (типы <c>0x1d</c>, <c>0x23</c>,
    /// <c>0x27</c>–<c>0x29</c>, <c>0x2c</c>) — а не цель действия. Отряд в крепости в клетке входа не стоит: неудача пути к
    /// нему исключает крепость, а сам он остаётся в кэше (human, <c>KC0000</c> дни 35–36: <c>KC000D</c> в деревне у 4,35).
    /// </summary>
    public static IEnumerable<string> PathExclusions(MapState map, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.Plan.TryGetValue((x, y), out var ids)
            ? ids.Where(id => Kind(id) is "FT" or "KC" or "BG" or "SI" or "RU" or "CR")
            : [];
    }

    /// <summary>Потолок повторных оценок из-за исключений: у движка — пока набор растёт, а целей конечно.</summary>
    private const int MaxReevaluations = 16;

    /// <summary>Путь есть и последняя достижимая сегодня клетка (или старт) — вплотную к точке (флаг <c>+0x50</c>). Движок сравнивает
    /// последнюю достижимую <c>+0x28</c> с целью искателя <c>+0x10</c> (<c>0x46903e</c>: <c>0x469266</c>–<c>0x46929e</c>, Чебышёв ≤ 1), а
    /// «идти» = 0 кладёт в <c>+0x10</c> клетку обрыва (<c>0x5cd984</c>, <see cref="StackPath.CutAt"/>) — путь, оборванный мешком или врагом
    /// старта, готов. Путь, усечённый на N (<see cref="Route"/>: снято N − 1 последних точек), кладёт в <c>+0x10</c> свой конец
    /// (<see cref="SearchGoal"/>) — готов, если сегодня его достигает.</summary>
    private static bool ArrivesToday(GameSession session, MapStack stack, AiTacticOption a, RouteCache? routes)
    {
        var start = PathFinder.Start(session.Map, stack);
        if (Cheb(start, (a.X, a.Y)) <= 1) return true;
        var path = routes?.Get(session, stack, a) ?? Route(session, stack, a);
        if (path.Points.Count == 0) return false;
        var today = path.Today;
        var last = today.Count > 0 ? (today[^1].X, today[^1].Y) : start;
        return Cheb(last, path.CutAt ?? SearchGoal(session, stack, a, path)) <= 1;
    }

    /// <summary>Цель искателя <c>+0x10</c> для готовности: точка действия, а у пути, усечённого на N &gt; 1 (точка — сама цель поиска), —
    /// конец пути после снятия N − 1 точек, как у команды (<see cref="CommandGoal"/>). Карта 24 (<c>20261020-replay-q09</c>), день 29:
    /// слабая атака <c>S143KC0001</c> с (77,15) на <c>S143KC0082</c> в (64,23), N = 10 — путь до (74,16), сегодня достижим: атака готова, и
    /// подмога <c>S143KC0094</c>, <c>S143KC00b4</c>, не доходящие сегодня, снимается (у движка в списке после фазы 3 — только
    /// <c>S143KC0098</c>, seq 7742145); прежде порт сравнивал с (64,23), считал атаку неготовой и выбирал подмогу <c>S143KC0094</c>.</summary>
    private static (int X, int Y) SearchGoal(GameSession session, MapStack stack, AiTacticOption a, StackPath path)
    {
        var (_, n) = MoveMode(session, a);
        if (n <= 1 || path.Points.Count == 0) return (a.X, a.Y);
        var opt = session.MoveOptionsOf(stack) with { ForbidWaterOnLand = true };
        return PathFinder.Target(session.Map, stack, a.X, a.Y, opt, MovementRules.Vanilla) == (a.X, a.Y)
            ? (path.Points[^1].X, path.Points[^1].Y) : (a.X, a.Y);
    }

    private static bool HasRoute(GameSession session, MapStack stack, AiTacticOption a, RouteCache routes)
    {
        var start = PathFinder.Start(session.Map, stack);
        if (start == (a.X, a.Y) || a.Fort == AiFortStep.Stay) return true;
        return routes.Get(session, stack, a).Points.Count > 0;
    }

    /// <summary>Пути одной оценки отряда (<see cref="Choose"/>, все её проходы): карта за оценку не меняется, а <see cref="Route"/>
    /// зависит только от отряда, точки и режима с N (<see cref="MoveMode"/>) — повторный проход, готовность атаки и подмога берут
    /// найденный путь, не ища его заново. Решений не меняет: тот же путь, что дал бы новый поиск.</summary>
    private sealed class RouteCache
    {
        private readonly Dictionary<(string Stack, int X, int Y, int Mode, int Stop), StackPath> _paths = [];

        public StackPath Get(GameSession session, MapStack stack, AiTacticOption a)
        {
            var (mode, n) = MoveMode(session, a);
            var key = (stack.Id, a.X, a.Y, mode, n);
            if (!_paths.TryGetValue(key, out var path)) _paths[key] = path = Route(session, stack, a);
            return path;
        }
    }

    /// <summary>
    /// Режим поиска пути и число N действия-перемещения <c>CAiMoveStackAction</c> (vtable <c>0x6d1184</c>, конструктор
    /// <c>0x45a915</c>: <c>+0x1c</c> режим, <c>+0x20</c> N; все 38 мест вызова разобраны 23.09.2026, ранг 1): мешок и
    /// защита города — режим 2, остальное — 1; жезл N = 2, помощь вплотную к лидеру (точка — клетка лидера) N = 2,
    /// атака — N фабрики (<see cref="AiTacticOption.Reach"/>), остальное — без усечения.
    /// </summary>
    public static (int Mode, int Stop) MoveMode(GameSession session, AiTacticOption a)
    {
        ArgumentNullException.ThrowIfNull(session);
        // Выселение (0x45a699 → 0x45a915): режим 1 (0x45a6bd push 1), N 0 (0x45a6b2 push 0) — при любом оценщике (лечение, защита и
        // охрана города, найм).
        if (a.Fort == AiFortStep.KickOut) return (1, 0);
        return a.Type switch
        {
            AiActionType.TakeBag or AiActionType.TakeBagRoam or AiActionType.SecureCity or AiActionType.Retreat => (2, 0),
            AiActionType.PlantRod => (1, 2),
            // Лидер в крепости — у её входа (0x5ed9b2): точка действия сравнивается с позицией, а не с углом крепости (human, день 34, KC007a).
            AiActionType.AssistMove when session.Map.Stacks.TryGetValue(a.Target, out var lead) && Position(session.Map, lead) == (a.X, a.Y) => (1, 2),
            // 0x460d27: мешок — режим 0 (0x469787), место — режим 2; DefendParty — путь режима 1 с N (0x45a9eb).
            AiActionType.DefendLocation or AiActionType.MoveToLocation when a.Act == AiMoveAct.Bag => (0, 0),
            AiActionType.DefendLocation or AiActionType.MoveToLocation when session.State.Events.Zones.ContainsKey(a.Target) => (2, 0),
            _ => (1, a.Reach),
        };
    }

    /// <summary>
    /// Путь действия ИИ (<c>0x46903e</c> → <c>0x5cc6da</c>, исполнение <c>0x45abd8</c>): поиск в режиме
    /// <see cref="MoveMode"/> с масками зоны контроля (<see cref="PathFinder.AiMasks"/>), <c>forbidWaterOnlyOnLand</c> = 1;
    /// если точка действия и есть цель поиска, последние N − 1 точек снимаются до команды движения. «Видеть невидимых» <c>a5</c> = 0
    /// (<c>0x469050</c>: <c>push ebx</c> = 0): чужой невидимый отряд — «можно» в масках (<c>0x5ccc68</c>), последняя достижимая его не видит
    /// (<see cref="ReachHostile"/>) — путь идёт сквозь него, как у интерфейса человека; встаёт перед ним команда хода
    /// (<see cref="GameSession.CommandPath"/>), и невидимый нападает сам.
    /// </summary>
    public static StackPath Route(GameSession session, MapStack stack, AiTacticOption a)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        var (mode, n) = MoveMode(session, a);
        var map = session.Map;
        var opt = session.MoveOptionsOf(stack) with { ForbidWaterOnLand = true };
        var masks = PathFinder.AiMasks(map, stack, a.X, a.Y, mode, opt, MovementRules.Vanilla, (x, y) => Allied(session, x, y),
                                       hostile: PathHostile(session, stack), invisible: MaskInvisible(session));
        bool pointIsTarget = PathFinder.Target(map, stack, a.X, a.Y, opt, MovementRules.Vanilla) == (a.X, a.Y);
        return PathFinder.Find(map, stack, a.X, a.Y, opt, MovementRules.Vanilla, session.DayAllowance(stack),
                               hostile: ReachHostile(session, stack), masks: masks, stopShort: pointIsTarget && n > 1 ? n - 1 : 0);
    }

    /// <summary>Клетка, которую исполнение <c>0x45abd8</c> отдаёт команде хода (<c>0x45ac52</c>–<c>0x45aceb</c>, затем <c>0x41e46b</c>; команда
    /// разрешает её, как поиск, <c>0x5cc120</c> → <c>0x5cc23d</c>): точка действия, а если она и есть цель поиска и N ≥ 1 — конец пути
    /// (N = 1 — <c>+0x20</c>, N &gt; 1 — последняя точка после снятия N − 1). Это цель <c>+0x10</c> искателя команды — по ней
    /// <see cref="PathFinder.ActionObject"/> ищет объект действия.</summary>
    public static (int X, int Y) CommandGoal(GameSession session, MapStack stack, AiTacticOption a, StackPath path)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(path);
        var (_, n) = MoveMode(session, a);
        var opt = session.MoveOptionsOf(stack) with { ForbidWaterOnLand = true };
        var resolved = PathFinder.Target(session.Map, stack, a.X, a.Y, opt, MovementRules.Vanilla);
        if (resolved == (a.X, a.Y) && n >= 1 && path.Points.Count > 0) return (path.Points[^1].X, path.Points[^1].Y);
        return resolved;
    }

    // ------------------------------------------------------------------ построители

    private static IEnumerable<(AiTacticOption Action, double Score)> Build(GameSession session, AiTacticTurn turn,
                                                                            MapStack stack, AiActionType type) =>
        // Бродячие построители (0x4517b4, 0x4508fc, 0x469889) зовут общий построитель, только если отряд не в крепости (CMidStack +0x94 —
        // крепость стоянки, ранг 1 по листингу; прежде порт читал это как «хозяин не захвачен»). 20261014-replay: RA KC008c в деревне FT0012
        // дни 2–4 (r2a) и NR KC009c в ней же дни 3–11 (r2b) — у движка оценки без единого кандидата.
        type is AiActionType.AttackStackRoam or AiActionType.AttackCityRoam or AiActionType.TakeBagRoam && stack.Inside.Length > 0 ? []
        : type switch
        {
            AiActionType.AssistMove => AssistMove(session, stack),
            AiActionType.GiveItems => GiveItems(session, stack),
            AiActionType.HealRevive => HealRevive(session, turn, stack),
            AiActionType.SecureCity => SecureCity(session, stack),
            AiActionType.DefendCity => DefendCity(session, stack),
            AiActionType.PlantRod => RodPlan(session, turn, stack),
            AiActionType.AttackRod => AttackAnyRod(session, turn, stack),
            AiActionType.TakeBag => TakeBag(session, turn, stack, roam: false),
            AiActionType.TakeBagRoam => TakeBag(session, turn, stack, roam: true),
            // Берсерк (0x19, 0x1a) — те же построители 0x4533fb/0x452636 и кэш: «цели по всей карте» у движка — это исключения
            // кэша, растущие, пока путь к ближним не находится (docs/ai-attack.md §9).
            AiActionType.AttackStack or AiActionType.AttackParty or AiActionType.AttackStackRoam or AiActionType.AttackStackBezerk
                => AttackStacks(session, turn, stack, type),
            AiActionType.AttackCity or AiActionType.AttackCityRoam or AiActionType.AttackCityBezerk => AttackCities(session, turn, stack, type),
            AiActionType.VisitRuin => VisitRuins(session, turn, stack),
            AiActionType.Guard => Guard(session, stack),
            AiActionType.VisitTrainer => VisitTrainers(session, turn, stack),
            AiActionType.VisitMerchant => VisitMerchants(session, turn, stack),
            AiActionType.VisitMage => VisitMages(session, turn, stack),
            AiActionType.NobleStack => NobleStacks(session, turn, stack),
            AiActionType.NobleCity => NobleCities(session, turn, stack),
            AiActionType.NobleMerchant => NobleMerchants(session, turn, stack),
            AiActionType.HireMercs => HireMercs(session, turn, stack),
            AiActionType.HireUnit => HireUnit(session, turn, stack),
            AiActionType.Retreat => Retreat(session, turn, stack),
            AiActionType.VisitTemple => VisitTemples(session, turn, stack),
            AiActionType.StayInside => StayInside(session, turn, stack),
            AiActionType.DefendParty => DefendParty(session, turn, stack),
            AiActionType.DefendLocation => DefendLocation(session, turn, stack),
            AiActionType.MoveToLocation => MoveToLocation(session, turn, stack),
            AiActionType.EnterCityRoam => EnterCityRoam(session, stack),
            AiActionType.SummonAttack => SummonAttack(session, turn, stack),
            // MoveToStack (0x45b610) у движка мёртв: цель — голова плана операции (0x473049), а план пополняет только проверка пути
            // 0x46903e по типу 0x23 объекта поиска, который заполняется лишь при найденном пути, в ветке неудачи поиска (разбор 28.09,
            // docs/ai-tactic-candidates.md §22); журналы — 0 кандидатов.
            AiActionType.MoveToStack => [],
            _ => [],
        };

    /// <summary>
    /// <c>StayInside</c> (<c>CAiStayInside</c> <c>0x464af0</c>, оценка <c>0x464b8f</c>, ранг 1 по листингу 26.09): отряд в крепости
    /// остаётся (действие <c>0x6d1c94</c>, статус 1 — журналы: 15 из 15). Счёт <see cref="AiTacticEval.StayInside"/>: свободные клетки
    /// (<c>0x5f4485</c>); недостача здоровья (<c>0x46886e</c>) — сумма наибольшего здоровья юнитов, павших тоже (<c>0x4688a0</c>: слот
    /// <c>0x24</c> реализации юнита), минус текущее (<c>0x46890b</c>); мёртвые — юнитов минус живых (<c>0x5f50fd</c>). Сверено 27.09 по
    /// всем 12 окнам журналов, где движок его строил: счёт совпал (прежде — рост типа по уровню, у павшего 170 → 248 не выходило).
    /// Точка — позиция отряда (вход крепости), цель — крепость.
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> StayInside(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        if (stack.Inside.Length == 0) yield break;
        var map = session.Map;
        var book = session.Book!;
        int max = 0, hp = 0, dead = 0;
        foreach (string id in stack.UnitIds)
        {
            if (!map.Units.TryGetValue(id, out var u)) continue;
            if (u.Hp <= 0) dead++;
            max += session.HpMax(u);
            hp += Math.Max(0, u.Hp);
        }
        if (AiTacticEval.StayInside(turn.Weights, session.FreeCells(stack), max - hp, dead, stack.LeaderAlive)
            is not { } score) yield break;
        var at = Position(map, stack);
        yield return (new AiTacticOption(AiActionType.StayInside, stack.Id, stack.Inside, at.X, at.Y), score);
    }

    /// <summary><c>AssistMove</c> (<c>0x45011b</c>): цель действующего приказа — отряд; точка — ближайшая
    /// проходимая клетка кольца 2 вокруг неё (<see cref="AiTacticEval.AssistMoveTarget"/>); счёт 150.</summary>
    private static IEnumerable<(AiTacticOption, double)> AssistMove(GameSession session, MapStack stack)
    {
        var map = session.Map;
        var (_, target) = OrderOf(stack);
        if (target.Length == 0 || !map.Stacks.TryGetValue(target, out var other)) yield break;
        var (tx, ty) = Position(map, other);
        var (sx, sy) = Position(map, stack);
        var opt = session.MoveOptionsOf(stack) with { ForbidWaterOnLand = true };
        if (AiTacticEval.AssistMoveTarget(tx, ty, sx, sy,
                (x, y) => map.Contains(x, y) && PathFinder.CellCost(map, stack, x, y, opt, MovementRules.Vanilla) != Movement.Forbidden)
            is not { } point) yield break;
        yield return (new AiTacticOption(AiActionType.AssistMove, stack.Id, other.Id, point.X, point.Y, Direct: point.Direct),
                      AiTacticScore.Constant(AiActionType.AssistMove)!.Value);
    }

    /// <summary><c>GiveItems</c> (<c>0x456fea</c>): <c>L_ASSIST</c>, есть предмет, цель задана; точка — позиция цели;
    /// счёт 500.</summary>
    private static IEnumerable<(AiTacticOption, double)> GiveItems(GameSession session, MapStack stack)
    {
        var map = session.Map;
        var (order, target) = OrderOf(stack);
        if (!AiTacticEval.GiveItemsBuilt(order, stack.Items.Count, target.Length > 0)) yield break;
        if (!map.Stacks.TryGetValue(target, out var other)) yield break;
        var (x, y) = Position(map, other);
        yield return (new AiTacticOption(AiActionType.GiveItems, stack.Id, other.Id, x, y),
                      AiTacticScore.Constant(AiActionType.GiveItems)!.Value);
    }

    /// <summary><c>HealRevive</c> (<c>0x4576dd</c>): хозяин не захвачен, урон не меньше 0,1; все крепости подрасы
    /// отряда по id, кроме деревни с бунтом и столицы захваченного хозяина; точка — вход, счёт
    /// <see cref="AiTacticEval.HealRevive"/>; здоровье против <c>GAI.HP_HEALMIN</c> — <see cref="HealthyForHeal"/>.</summary>
    private static IEnumerable<(AiTacticOption, double)> HealRevive(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        if (map.Players.TryGetValue(stack.Owner, out var owner) && owner.CapturedBy.Length > 0) yield break;
        if (Damage(session, stack) < HealDamage) yield break;
        var self = Position(map, stack);
        bool dead = stack.UnitIds.Any(u => map.Units.TryGetValue(u, out var unit) && unit.Hp <= 0);
        bool healthy = HealthyForHeal(session, stack);
        foreach (var fort in OwnForts(map, stack))
        {
            var p = Entrance(fort);
            var step = FortStepOf(map, stack, fort, self, p);
            int today = turn.Memo.Status(stack.Id, fort.Id, p.X, p.Y, turn.Day);
            var facts = new AiHealFacts(true, today, Occupied(fort, stack), healthy, Cheb(self, p), dead, stack.LeaderAlive,
                                        turn.CapturedByOther(stack.Id, fort.Id));
            var score = AiTacticEval.HealRevive(turn.Weights, facts);
            if (step == AiFortStep.KickOut)
            {
                if (KickOut(session, AiActionType.HealRevive, stack, fort) is { } kick) yield return (kick, score);
                continue;
            }
            yield return (new AiTacticOption(AiActionType.HealRevive, stack.Id, fort.Id, p.X, p.Y, step), score);
        }
    }

    /// <summary><c>SecureCity</c> (<c>0x462edf</c>): цель действующего приказа — крепость, не столица захваченного
    /// хозяина и не деревня с бунтом. Шаг — <see cref="AiTacticEval.SecureCityStep"/>; точка: идти — клетка крепости
    /// (её <c>pos</c>), внутри — позиция отряда (вход) — корпус human, <c>S073KC0008</c>: 88,13, затем 91,16 (ранг 2).
    /// Действия шагов (<c>0x46304d</c>, ранг 1 по листингу, 16.10): остаться — <c>0x4632cc</c> (vft <c>0x6d18d4</c>), путь —
    /// <c>0x460d27</c> (<c>0x6d1184</c>), вход в свою пустую — <c>0x455bea</c> (<c>CAiEnterCityAction</c>, <c>0x6d08cc</c>), выселение —
    /// <c>0x45a699</c> (<c>0x6d10ec</c>, <see cref="KickOut"/>), чужая — атака крепости <c>0x4506c5</c> (<c>CAiAttackCityAction</c>,
    /// <c>0x6cfd44</c> — тот же класс, что у тактики <c>AttackCity</c>; общий конструктор перемещения <c>0x45a915</c>, что и у входа, —
    /// точка вход). Кроме лидера-призыва (<c>0x431d8c</c>) у крепости без визитёра (<c>+0x8c</c>) и без гарнизона (<c>0x5f509a</c>):
    /// тогда ничего. Журналы 20261016*: путь, вход, выселение и остаться есть, атаки — нет (ранг 2 для её точки).</summary>
    private static IEnumerable<(AiTacticOption, double)> SecureCity(GameSession session, MapStack stack)
    {
        var map = session.Map;
        var (_, target) = OrderOf(stack);
        if (Forts(map).FirstOrDefault(f => Same(f.Id, target)) is not { } fort || !FortUsable(map, fort)) yield break;
        var self = Position(map, stack);
        var entrance = Entrance(fort);
        var step = AiTacticEval.SecureCityStep(Same(fort.StackId, stack.Id) || Same(stack.Inside, fort.Id),
                                               Cheb(self, entrance) <= 1, Same(fort.Subrace, stack.Subrace),
                                               Occupied(fort, stack), SummonLeader(session, stack), fort.Guards.Count,
                                               map.Stacks.TryGetValue(fort.StackId, out var guest) ? Damage(session, guest) : 0);
        if (step == AiSecureStep.KickOut)
        {
            if (KickOut(session, AiActionType.SecureCity, stack, fort) is { } kick)
                yield return (kick, AiTacticScore.Constant(AiActionType.SecureCity)!.Value);
            yield break;
        }
        if (step == AiSecureStep.AttackCity)
        {
            yield return (new AiTacticOption(AiActionType.SecureCity, stack.Id, fort.Id, entrance.X, entrance.Y, Act: AiMoveAct.Attack),
                          AiTacticScore.Constant(AiActionType.SecureCity)!.Value);
            yield break;
        }
        (int X, int Y)? point = step switch
        {
            AiSecureStep.Path => (fort.X, fort.Y),
            AiSecureStep.Stay or AiSecureStep.Enter => entrance,
            _ => null,
        };
        if (point is not { } p) yield break;
        yield return (new AiTacticOption(AiActionType.SecureCity, stack.Id, fort.Id, p.X, p.Y,
                                         step == AiSecureStep.Stay ? AiFortStep.Stay : step == AiSecureStep.Enter ? AiFortStep.Enter : AiFortStep.Move),
                      AiTacticScore.Constant(AiActionType.SecureCity)!.Value);
    }

    /// <summary>Лидер отряда — призыв (<c>IUsSummon</c>, <c>0x431d8c</c>; иллюзия тоже, <see cref="UnitCategory.IsSummonImpl"/>).</summary>
    private static bool SummonLeader(GameSession session, MapStack stack) =>
        session.Map.Units.TryGetValue(stack.LeaderId, out var leader) && UnitCategory.IsSummonImpl(session.Book!.CategoryOf(leader.TypeId));

    /// <summary>
    /// <c>Retreat</c> (оценщик <c>0x462cda</c>, счёт 2000 <c>0x6d1820</c>, ранг 1 по листингу): отряд не в крепости (<c>+0x94</c>); по
    /// списку чужих сильнейших (<c>CAiTactic +0x51f8</c>, <see cref="AiTacticTurn.Forces"/>) — отряд, чей хозяин человек (<c>CMidPlayer
    /// +0x88</c>), ближе 7 клеток к нам (слот <c>0x34</c> обёртки — Чебышёв), при том что от нас до точки бегства не меньше 7, и сила
    /// его атаки на нас (<c>0x5dda98</c>, <see cref="AttackOn"/>; без поддержки — <c>0x462e0d push 0</c>, флаг <c>[ebp+0x24]</c>) не ниже
    /// 0,8 — одно действие-перемещение (<c>0x45a915</c>, режим 2) к
    /// точке бегства — входу своей столицы (<c>0x5e9450</c> + <c>0x5e9563</c>). Журналы: human 6 кандидатов и 6 выборов, probe65 4 и 4.
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> Retreat(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        if (stack.Inside.Length > 0) yield break;
        if (Forts(map).FirstOrDefault(f => f.Kind == SiteKind.Capital && Same(f.Owner, stack.Owner)) is not { } capital) yield break;
        var home = Entrance(capital);
        var self = Position(map, stack);
        var threats = turn.Forces.Others
            .Select(id => map.Stacks.GetValueOrDefault(id)).OfType<MapStack>()
            .Select(x => (Human: map.Players.TryGetValue(x.Owner, out var p) && p.IsHuman, Distance: Cheb(self, Position(map, x)), Stack: x))
            .Select(t => (t.Human, t.Distance,
                          Strength: t.Human && t.Distance < AiTacticEval.Near ? AttackOn(session, t.Stack, stack, support: false).P : 0.0));
        if (!AiTacticEval.RetreatBuilt(false, Cheb(self, home), threats)) yield break;
        yield return (new AiTacticOption(AiActionType.Retreat, stack.Id, capital.Id, home.X, home.Y),
                      AiTacticScore.Constant(AiActionType.Retreat)!.Value);
    }

    /// <summary>Сила атаки отряда <paramref name="attacker"/> на отряд <paramref name="target"/> в поле (<c>0x5dda98</c>, как у руины —
    /// <see cref="RuinStrength"/>): B — нападающий «без города» против числа юнитов цели, A — цель против числа юнитов нападающего,
    /// C — поддержка у цели (<see cref="Support"/>: чужой нападающему подрасы, не <c>L_STAND</c>, ±10 клеток), если
    /// <paramref name="support"/> (восьмой аргумент <c>0x5dda98</c>, байт <c>[ebp+0x24]</c>: 0 — <c>C</c> = 0 без обхода квадрата).</summary>
    public static AiAttackStrength AttackOn(GameSession session, MapStack attacker, MapStack target, bool support = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);
        double b = GroupStrength(session, attacker, ignoreCity: true, target.UnitIds.Count);
        double a = GroupStrength(session, target, ignoreCity: false, attacker.UnitIds.Count);
        double c = support ? Support(session, attacker, Position(session.Map, target), target.Id) : 0;
        var attackerFighters = Fighters(session, attacker.UnitIds).ToList();
        var targetFighters = Fighters(session, target.UnitIds).ToList();
        return new AiAttackStrength(a, b, c, AiStrength.CanDamage(attackerFighters, targetFighters));
    }

    /// <summary><c>DefendCity</c> (<c>0x4546e4</c>): крепости подрасы отряда (фильтр <c>L_DEFEND_CITY</c> оставит
    /// цель приказа); счёт 225, если отряд уже в ней, иначе 150.</summary>
    private static IEnumerable<(AiTacticOption, double)> DefendCity(GameSession session, MapStack stack)
    {
        var map = session.Map;
        var self = Position(map, stack);
        foreach (var fort in OwnForts(map, stack))
        {
            var p = Entrance(fort);
            var step = FortStepOf(map, stack, fort, self, p);
            var score = AiTacticEval.DefendCityScore(step == AiFortStep.Stay);
            if (step == AiFortStep.KickOut)
            {
                if (KickOut(session, AiActionType.DefendCity, stack, fort) is { } kick) yield return (kick, score);
                continue;
            }
            yield return (new AiTacticOption(AiActionType.DefendCity, stack.Id, fort.Id, p.X, p.Y, step), score);
        }
    }

    // ------------------------------------------------------------------ кэш ближайших целей, жезлы, мешки

    /// <summary>Кэш ближайших целей (<c>0x45e4c7</c>, <c>docs/ai-tactic-candidates.md</c> §6): до трёх не исключённых
    /// целей по евклиду от позиции отряда, равные — по id. ponytail: флаг радиуса (<c>+0x51d9</c>, список
    /// <c>+0x51fc</c> не разобран) не ставится — радиус-фильтр у чужих столиц не работает.</summary>
    private static IReadOnlyList<T> Nearest<T>(IEnumerable<T> items, Func<T, (string Id, int X, int Y)> key,
                                               (int X, int Y) from, AiTacticTurn turn) =>
        [.. items.Select(i => (Item: i, Key: key(i)))
                 .Where(p => !turn.Excluded.Contains(p.Key.Id))
                 .Where(p => !RadiusDrops(turn, p.Key.Id, (p.Key.X, p.Key.Y)))
                 .OrderBy(p => AiTacticScore.SetDistance(p.Key.X - from.X, p.Key.Y - from.Y))
                 .ThenBy(p => p.Key.Id, StringComparer.OrdinalIgnoreCase)
                 .PickThree(p => turn.WinTargets.Contains(p.Key.Id)).Select(p => p.Item)];

    /// <summary>Радиус у чужих столиц (<c>0x45f5f1</c>, ранг 1): цель выбрасывается, если радиус оценки не 0, её нет в множестве
    /// целей тактики (<c>0x45f69d</c> → <c>je</c>) и до какой-нибудь чужой столицы (вход) по Чебышёву не больше радиуса
    /// (<c>0x45f706</c>).</summary>
    private static bool RadiusDrops(AiTacticTurn turn, string id, (int X, int Y) pos) =>
        turn.Radius > 0 && !turn.WinTargets.Contains(id) && turn.ForeignCapitals.Any(c => Cheb(c, pos) <= turn.Radius);

    /// <summary>Кристаллы кэша (<c>0x45f327</c>): только у лидера с жезлом; не на своей и не на союзной земле.</summary>
    private static IReadOnlyList<MapObject> Crystals(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        var book = session.Book!;
        if (!RodLeader(session, stack) || !map.Players.TryGetValue(stack.Owner, out var owner)
            || !book.Economy.Races.TryGetValue(owner.RaceId, out var race)) return [];
        int own = race.Terrain;
        var ally = Diplomacy.AllyTerrain(map.Relations, Diplomacy.PlayerParties(map, book.Economy), owner.Party ?? race.RaceType, book.Economy, map);
        var crystals = map.Objects.Values
            .Where(o => o.Kind.Equals("CR", StringComparison.OrdinalIgnoreCase) && map.Contains(o.X, o.Y))
            .Where(o => map.At(o.X, o.Y).Terrain != own && !ally(map.At(o.X, o.Y).Terrain));
        return Nearest(crystals, o => (o.Id, o.X, o.Y), Position(map, stack), turn);
    }

    private static bool RodLeader(GameSession session, MapStack stack) =>
        session.Map.Units.TryGetValue(stack.LeaderId, out var leader)
        && session.Book!.LeaderCategoryOf(leader.TypeId) == RodLeaderCategory;

    /// <summary>Жезлы у кристаллов (<c>0x461347</c>, <see cref="AiTacticEval.RodPlan"/>): чужие жезлы в восьми соседях —
    /// при живом лидере <c>AttackRod</c> на каждый; нет — <c>PlantRod</c> в клетке жезла, и если до неё не больше
    /// клетки — перебор кончается.</summary>
    private static IEnumerable<(AiTacticOption, double)> RodPlan(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        if (map.Players.TryGetValue(stack.Owner, out var owner) && owner.CapturedBy.Length > 0) yield break;
        var self = Position(map, stack);
        var allyLand = AllyLand(session, stack);
        foreach (var cr in Crystals(session, turn, stack))
        {
            var rods = AiTacticScore.RodNeighbours
                .SelectMany(d => map.Rods.Values.Where(r => r.X == cr.X + d.Dx && r.Y == cr.Y + d.Dy))
                .Where(r => !Same(r.Owner, stack.Owner)).ToList();
            if (rods.Count > 0)
            {
                if (!stack.LeaderAlive) continue;
                foreach (var rod in rods) yield return AttackRod(session, turn, stack, rod, self);
                continue;
            }
            if (AiTacticScore.RodCell(cr.X, cr.Y, self.X, self.Y, (x, y) => RodNear(map, x, y), allyLand,
                                      (x, y) => (x, y) == (stack.X, stack.Y) || RodRefusal(map, x, y, stack)) is not { } cell)
                continue;
            int today = turn.Memo.Status(stack.Id, cr.Id, cell.X, cell.Y, turn.Day);
            yield return (new AiTacticOption(AiActionType.PlantRod, stack.Id, cr.Id, cell.X, cell.Y),
                          AiTacticScore.PlantRod(turn.Weights, AiTacticScore.Influence(turn.Grid, cr.X, cr.Y),
                                                 Cheb(self, (cr.X, cr.Y)), cr.Resource == 0,
                                                 turn.CapturedByOther(stack.Id, cr.Id), today));
            if (Cheb(self, cell) <= 1) yield break;
        }
    }

    /// <summary>Общий <c>AttackRod</c> (<c>0x450cbe</c>): при живом лидере и пустом кэше кристаллов — на каждый
    /// чужой жезл карты; чужих жезлов нет — смена приказа (<see cref="ChangeOrderOption"/>, <c>0x450dfb</c>).</summary>
    private static IEnumerable<(AiTacticOption, double)> AttackAnyRod(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        if (!stack.LeaderAlive || Crystals(session, turn, stack).Count > 0) yield break;
        var self = Position(session.Map, stack);
        bool any = false;
        foreach (var rod in session.Map.Rods.Values.Where(r => !Same(r.Owner, stack.Owner))
                                   .OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase))
        {
            any = true;
            yield return AttackRod(session, turn, stack, rod, self);
        }
        // Журналы карты 4 — 8 из 8 (0x44ffe0, тип 0x0c: S143KC0045/0049 20261016d, KC0047/004b 20261016c, KC0047/004b/004d 20261016b,
        // KC004a 20261016); в крепости — её угол: карта 22 (20261019b), день 7, S143KC0018 в S143FT0008 — (100,40), не вход (103,43).
        if (!any && SelfChangeOrder(session, stack) is { } change) yield return change;
    }

    /// <summary>Смена приказа на себя (<c>0x450ddc</c>, <c>0x45d381</c>, <c>0x45db4c</c> → <c>0x44fc58</c>): цель — id отряда (слот 1
    /// обёртки), точка — клетка из самого отряда (слот 2 + 4: <c>CMidStack</c> <c>+4</c>), а не позиция ИИ <c>0x5ed9b2</c> — у отряда в
    /// крепости это угол крепости.</summary>
    internal static (AiTacticOption, double)? SelfChangeOrder(GameSession session, MapStack stack) =>
        ChangeOrderOption(session, stack, stack.Id, (stack.X, stack.Y));

    /// <summary>
    /// Смена приказа (<c>CAiChangeOrderEval</c>, конструктор <c>0x44fc58</c>, слот 4 <c>0x44fcb5</c>): приказ потерял смысл — у
    /// общего <c>AttackRod</c> нет чужих жезлов (<c>0x450dfb</c>), у <c>AttackParty</c> нет отряда-цели приказа (<c>0x450b64</c>), у
    /// знатного некого разведывать (<c>0x45db6b</c>) или нет торговцев (<c>0x45d3a0</c>). Новый приказ —
    /// <see cref="AiTacticOrders.ChangeOrder"/>; нет смены — нет и кандидата. Счёт — <see cref="AiTacticEval.ChangeOrderScore"/>,
    /// цель и точка — переданные: у <c>AttackParty</c> — исчезнувшая цель приказа (фильтр приказа её пропускает) без точки (−1, −1),
    /// у общего <c>AttackRod</c> — сам отряд и его позиция.
    /// </summary>
    internal static (AiTacticOption, double)? ChangeOrderOption(GameSession session, MapStack stack, string target,
                                                                (int X, int Y)? point = null)
    {
        var kind = KindOf(session, stack);
        return AiTacticOrders.ChangeOrder((StackOrder)stack.Order, (StackOrder)stack.AiOrder,
                                          nobleOrRod: kind is AiStackKind.Thief or AiStackKind.Rod, NeutralOwner(session, stack.Owner)) is null
            ? null
            : (new AiTacticOption(AiActionType.ChangeOrder, stack.Id, target, point?.X ?? -1, point?.Y ?? -1), AiTacticEval.ChangeOrderScore);
    }

    private static (AiTacticOption, double) AttackRod(GameSession session, AiTacticTurn turn, MapStack stack, MapRod rod,
                                                      (int X, int Y) self)
    {
        int today = turn.Memo.Status(stack.Id, rod.Id, rod.X, rod.Y, turn.Day);
        return (new AiTacticOption(AiActionType.AttackRod, stack.Id, rod.Id, rod.X, rod.Y),
                AiTacticEval.AttackRod(turn.Weights, false, Allied(session, stack.Owner, rod.Owner),
                                       AiTacticScore.Influence(turn.Grid, rod.X, rod.Y), Cheb(self, (rod.X, rod.Y)),
                                       turn.CapturedByOther(stack.Id, rod.Id), today));
    }

    /// <summary><c>TakeBag</c>/<c>TakeBagRoam</c> (<c>0x469418</c>): хозяин захвачен — нет; мешки кэша
    /// (<c>0x45f1e3</c>), точка — клетка мешка; бродяга — не в крепости и мешок не дальше 3.</summary>
    private static IEnumerable<(AiTacticOption, double)> TakeBag(GameSession session, AiTacticTurn turn, MapStack stack,
                                                                 bool roam)
    {
        var map = session.Map;
        if (map.Players.TryGetValue(stack.Owner, out var owner) && owner.CapturedBy.Length > 0) yield break;
        if (roam && stack.Inside.Length > 0) yield break;
        var self = Position(map, stack);
        // 0x45f1e3: водному лидеру — только мешки на воде.
        bool waterOnly = session.WaterOnly(stack);
        var bags = Nearest(map.Sites.Where(b => b.Kind == SiteKind.Bag && !b.Gone && (!waterOnly || session.IsWater(b.X, b.Y))),
                           b => (b.Id, b.X, b.Y), self, turn);
        bool rodLeader = RodLeader(session, stack);
        foreach (var bag in bags)
        {
            int d = Cheb(self, (bag.X, bag.Y));
            if (roam && !AiTacticEval.TakeBagRoamAdmits(d)) continue;
            yield return (new AiTacticOption(roam ? AiActionType.TakeBagRoam : AiActionType.TakeBag, stack.Id, bag.Id, bag.X, bag.Y),
                          AiTacticEval.TakeBag(turn.Weights, AiTacticScore.Influence(turn.Grid, bag.X, bag.Y), d, rodLeader,
                                               turn.CapturedByOther(stack.Id, bag.Id)));
        }
    }

    private static bool RodNear(MapState map, int x, int y) =>
        map.Rods.Values.Any(r => Math.Abs(r.X - x) <= Rods.RodKeepOut && Math.Abs(r.Y - y) <= Rods.RodKeepOut);

    private static Func<int, int, bool> AllyLand(GameSession session, MapStack stack)
    {
        var map = session.Map;
        var book = session.Book!;
        if (!map.Players.TryGetValue(stack.Owner, out var p) || !book.Economy.Races.TryGetValue(p.RaceId, out var race))
            return (_, _) => false;
        var ally = Diplomacy.AllyTerrain(map.Relations, Diplomacy.PlayerParties(map, book.Economy), p.Party ?? race.RaceType, book.Economy, map);
        return (x, y) => map.Contains(x, y) && ally(map.At(x, y).Terrain);
    }

    /// <summary>Отказ клетки жезла по <c>0x5cd11f</c> и калитке жезла: не равнина, в клетке объект, в 3×3 крепость или
    /// отряд другого игрока (ранг 2 — <c>AiTacticHarnessTests</c>, 36/36).</summary>
    private static bool RodRefusal(MapState map, int x, int y, MapStack own)
    {
        if (!map.Contains(x, y) || map.At(x, y).Ground != Ground.Plain) return true;
        if (map.Plan.TryGetValue((x, y), out var ids) && ids.Any(id => Kind(id) != "LO" && !Same(id, own.Id))) return true;
        for (int dx = -Rods.FortKeepOut; dx <= Rods.FortKeepOut; dx++)
            for (int dy = -Rods.FortKeepOut; dy <= Rods.FortKeepOut; dy++)
            {
                if (!map.Plan.TryGetValue((x + dx, y + dy), out var near)) continue;
                if (near.Any(id => Kind(id) == "FT")) return true;
                if (near.Any(id => map.Stacks.TryGetValue(id, out var s) && !Same(s.Owner, own.Owner))) return true;
            }
        return false;
    }

    /// <summary>Хозяева в союзе (<c>0x5f7ebd</c>): разные стороны дипломатии (у движка — расы), союз в дипломатии.</summary>
    public static bool Allied(GameSession session, string a, string b)
    {
        var map = session.Map;
        var races = session.Book!.Economy.Races;
        return map.Players.TryGetValue(a, out var pa) && map.Players.TryGetValue(b, out var pb)
            && races.TryGetValue(pa.RaceId, out var ra) && races.TryGetValue(pb.RaceId, out var rb)
            && (pa.Party ?? ra.RaceType) is var partyA && (pb.Party ?? rb.RaceType) is var partyB && partyA != partyB && Diplomacy.AreAllies(map.Relations, partyA, partyB);
    }

    private static string Kind(string id) => id.Length >= 6 ? id.Substring(4, 2).ToUpperInvariant() : "";

    private static IEnumerable<MapSite> OwnForts(MapState map, MapStack stack) =>
        Forts(map).Where(f => Same(f.Subrace, stack.Subrace) && FortUsable(map, f)).OrderBy(f => f.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Не деревня с бунтом (<c>0x5e9582</c>, <c>+0x98</c> ≠ 0) и не столица захваченного хозяина.</summary>
    private static bool FortUsable(MapState map, MapSite fort) =>
        !(fort.Kind == SiteKind.Village && fort.RiotTurn is > 0)
        && !(fort.Kind == SiteKind.Capital && map.Players.TryGetValue(fort.Owner, out var p) && p.CapturedBy.Length > 0);

    /// <summary>Сдвиги точки выселения от входа крепости — таблица <c>0x6d1050</c> (пары dx, dy).</summary>
    public static readonly IReadOnlyList<(int Dx, int Dy)> KickOutOffsets = [(-1, 1), (0, 1), (1, 1), (1, 0), (1, -1)];

    /// <summary>
    /// Выселение гостя (<c>CAiKickOutStackAction</c>, конструктор <c>0x45a699</c>): отряд рядом с занятой своей крепостью —
    /// фабрики лечения (<c>0x4578d8</c>, вызов <c>0x457a83</c>), защиты (<c>0x4549d4</c>), охраны города (<c>0x46328e</c>) и найма
    /// (<c>CAiHireUnitTacticActionEval</c>, vtable <c>0x6d0ef4</c>, слот 4 <c>0x45a11e</c>, вызов <c>0x45a294</c> — <see cref="HireUnit"/>)
    /// вместо входа строят выселение со счётом своего оценщика; других вызовов конструктора нет (ранг 1, 28.09). Точка (<c>0x45a6e5</c>): вход плюс <see cref="KickOutOffsets"/> по порядку, первая
    /// клетка, куда гость может выйти (<see cref="CanExit"/>); ни одной — сам вход. Ключ памяти — гость
    /// (<see cref="AiTacticOption.Guest"/>). Ранг 1 — код; журналы: human 8, legions 5, probe65 13 выборов, у всех гость
    /// выходит в точку действия, статус 2.
    /// </summary>
    private static AiTacticOption? KickOut(GameSession session, AiActionType type, MapStack stack, MapSite fort)
    {
        if (!session.Map.Stacks.TryGetValue(fort.StackId, out var guest)) return null;
        var entrance = Entrance(fort);
        var point = entrance;
        foreach (var (dx, dy) in KickOutOffsets)
            if (CanExit(session, guest, entrance.X + dx, entrance.Y + dy))
            {
                point = (entrance.X + dx, entrance.Y + dy);
                break;
            }
        return new AiTacticOption(type, stack.Id, fort.Id, point.X, point.Y, AiFortStep.KickOut, Guest: guest.Id);
    }

    /// <summary>Гость может выйти из крепости в клетку (<c>CVisitorExitFortification</c>, проверка <c>0x60de91</c> →
    /// <c>0x60cee7</c>): <c>computeMovementCost</c> (<c>0x6027f3</c>) клетки с нулевыми масками и всеми способностями хода, «только по
    /// воде» — по лидеру гостя, <c>forbidWaterOnlyOnLand</c> = 1 — не ноль (<see cref="PathFinder.CellCost"/>). Ранг 1 по листингу
    /// (28.09): мешок в клетке (<c>0x602879</c>) и любой отряд, кроме самого гостя (<c>0x6028be</c>; <c>0x57f529</c> — сравнение id),
    /// при нулевой маске <c>a7</c> дают ноль — свой отряд тоже.</summary>
    internal static bool CanExit(GameSession session, MapStack guest, int x, int y)
    {
        var map = session.Map;
        var opt = new MoveOptions(LeaderAlive: true, PlainsBonus: true, ForestBonus: true, WaterBonus: true,
                                  WaterOnly: session.WaterOnly(guest), ForbidWaterOnLand: true);
        return map.Contains(x, y) && PathFinder.CellCost(map, guest, x, y, opt, MovementRules.Vanilla) != Movement.Forbidden;
    }

    private static bool Occupied(MapSite fort, MapStack stack) =>
        fort.StackId.Length > 0 && !Same(fort.StackId, stack.Id);

    private static AiFortStep FortStepOf(MapState map, MapStack stack, MapSite fort, (int X, int Y) self, (int X, int Y) p) =>
        AiTacticEval.FortStep(Same(fort.StackId, stack.Id) || Same(stack.Inside, fort.Id), Cheb(self, p) <= 1,
                              Occupied(fort, stack));

    private static int Cheb((int X, int Y) a, (int X, int Y) b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
