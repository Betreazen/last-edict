using D2.Rules.Units;

namespace D2.Rules.Map;

/// <summary>
/// Флаги отряда, с которыми движок спрашивает стоимость шага. Ранг 1: это шесть последних
/// аргументов <c>computeMovementCost</c> (Gog <c>0x6027f3</c>, <c>ret 0x34</c> = 13 аргументов;
/// прототип — <c>tools/d2probe/mss32/include/game.h:722-734</c>), и зонд пишет их в событие
/// <c>move_cost</c> как есть (<c>leader_alive</c>, <c>plains</c>, <c>forest</c>, <c>water</c>,
/// <c>water_only</c>, <c>forbid_water</c>). Грунта среди аргументов нет: движок читает его сам
/// из <c>CMidgardMap</c>, поэтому в сверке грунт приходит с нашей стороны — из <c>.sg</c>.
///
/// <para><b>Откуда берутся сами флаги</b> (лидерские способности отряда, вода-только у морского
/// отряда) — вопрос не этой задачи: сюда они приходят готовыми из журнала.</para>
/// </summary>
public sealed record MoveOptions(
    bool LeaderAlive = true,
    bool PlainsBonus = false,
    bool ForestBonus = false,
    bool WaterBonus = false,
    bool WaterOnly = false,
    bool ForbidWaterOnLand = true);

/// <summary>
/// Три цены одного грунта: обычная, со «своим» бонусом лидера и с мёртвым лидером.
/// У равнины <see cref="WithBonus"/> совпадает с <see cref="Default"/> — так в движке
/// (см. <see cref="MovementRules.Vanilla"/>), отдельной «дешёвой равнины» у него нет.
/// </summary>
public sealed record GroundCost(int Default, int WithBonus, int DeadLeader);

/// <summary>
/// Числа стоимости шага. В формулу они не зашиты: <see cref="Movement.Cost"/> берёт их отсюда,
/// поэтому цену можно подменить в тесте, не трогая ветвление.
///
/// <para><b>Ранг 1</b> — все значения <see cref="Vanilla"/> прочитаны в дизассемблере
/// <c>Discipl2.exe</c> (Gog, размер 4 474 880), функция <c>computeMovementCost</c>
/// <c>0x6027f3</c>, ветки <c>0x602967-0x6029eb</c>: вода <c>waterBonus ? 2 : 6</c>
/// (<c>and al,0xFC; add eax,6</c>) и <c>12</c> при мёртвом лидере; лес <c>forestBonus ? 2 : 4</c>
/// (<c>and al,0xFE; add eax,4</c>) и <c>8</c>; равнина <c>4</c> при мёртвом лидере, иначе
/// <c>1</c> по дороге и <c>2</c> без неё; вода-только в глубокой воде <c>2</c>; суша под
/// вода-только <c>forbidWaterOnlyOnLand ? 0 : 1000</c> (<c>and ax,0xFC18; add eax,0x3E8</c>).
/// Те же числа независимо стоят в <c>tools/d2probe/mss32/src/settings.cpp:496-505</c>
/// (<c>baseSettings</c> — ванильное поведение тулсета) — ранг 2, совпало полностью.</para>
///
/// <para>«Множитель за мёртвого лидера» как отдельная величина в движке отсутствует: там три
/// литерала <c>12/8/4</c>, каждый ровно вдвое больше обычной цены своего грунта. Поэтому здесь
/// хранится сама цена, а не множитель (отличие от интерфейса задачи 5 плана Ф5).</para>
/// </summary>
public sealed record MovementRules(
    GroundCost Plain, GroundCost Forest, GroundCost Water,
    int Road, int DeepWaterForWaterOnly, int LandForWaterOnly)
{
    public static readonly MovementRules Vanilla = new(
        Plain: new GroundCost(Default: 2, WithBonus: 2, DeadLeader: 4),
        Forest: new GroundCost(Default: 4, WithBonus: 2, DeadLeader: 8),
        Water: new GroundCost(Default: 6, WithBonus: 2, DeadLeader: 12),
        Road: 1,
        DeepWaterForWaterOnly: 2,
        LandForWaterOnly: 1000);
}

/// <summary>
/// Движение отряда по карте: стоимость шага, проходимость клетки, очки хода.
///
/// <para><b>Что здесь ранг 1.</b> Всё ветвление и все числа сняты дизассемблером с
/// <c>computeMovementCost</c> (Gog <c>0x6027f3</c>), <c>stackCanMoveToPosition</c>
/// (<c>0x4cd1dd</c>) и <c>isWaterTileSurroundedByWater</c> (<c>0x5e6453</c>) — см. комментарии у
/// методов. Ранг 3 остался у двух вещей: сколько очков хода отряд получает на новый день
/// (<see cref="StartMovement"/>) и правило остановки <see cref="Walk"/>.</para>
///
/// <para><b>Известный потолок.</b> Движковая функция получает две битовые маски
/// (<c>a6</c>, <c>a7</c>, по 18 байт на строку карты), которыми ищущий путь помечает клетки; их
/// содержимое нам недоступно, и клетка, запрещённая маской, у нас получит обычную цену грунта.
/// По той же причине не воспроизводятся две ветки: мешок на клетке и чужой отряд на клетке
/// снимают запрет, если бит в <c>a7</c> стоит. Харнесс <c>C4MoveHarnessTests</c> считает такие
/// клетки отдельно, а не прячет их в «разошлось».</para>
/// </summary>
public static class Movement
{
    /// <summary>Движок возвращает 0 в значении «сюда нельзя», а не «бесплатно»
    /// (<c>constexpr int movementForbidden = 0</c>, <c>movepathhooks.cpp:306</c>; в дизассемблере —
    /// <c>xor eax,eax</c> по метке <c>0x6029f0</c>).</summary>
    public const int Forbidden = 0;

    /// <summary>
    /// Классы объектов, которые движок считает непроходимыми до всякого грунта: город, ландшафт,
    /// лавка, руина, жезл, кристалл. Ранг 1 — массив <c>interactiveObjectTypes</c> из шести
    /// <c>IdType</c> (<c>movepathhooks.cpp:322-331</c>), в дизассемблере вызов
    /// <c>isPositionContainsObjects(plan, pos, 0x6efe60, 6)</c> по адресу <c>0x60285d</c>;
    /// буквы классов — <c>midgardid.h:70-86</c>.
    /// </summary>
    private static readonly string[] BlockingKinds = ["FT", "MM", "SI", "RU", "RD", "CR"];

    /// <summary>
    /// Восемь соседей по часовой стрелке. Ранг 1: таблица <c>0x6ee760</c> длиной 8 пар
    /// <c>(dx, dy)</c>, по которой ходит <c>isWaterTileSurroundedByWater</c>
    /// (цикл <c>0x5e64ae-0x5e650e</c>).
    /// </summary>
    private static readonly (int Dx, int Dy)[] Neighbours =
        [(-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0)];

    /// <summary>
    /// Стоимость шага на клетку в очках хода; <see cref="Forbidden"/> (0) — шаг запрещён.
    /// Числа берутся из <paramref name="rules"/>, а не из кода.
    ///
    /// <para>Порядок веток — ровно движковый (<c>0x6027f3</c>): сначала границы карты, потом
    /// непроходимый объект на клетке, потом дорога и грунт. Мёртвый лидер отменяет и бонусы, и
    /// скидку за дорогу; бонус равнины не удешевляет равнину, а <b>отменяет скидку за дорогу</b>
    /// (<c>0x6029d3</c>: <c>plainsBonus</c> → сразу 2, дорога проверяется только без него) —
    /// это выглядит как опечатка движка, но так в нём и стоит, и так же это переписал тулсет
    /// (<c>movepathhooks.cpp:398-406</c>).</para>
    ///
    /// <para>Отряд «только по воде» (<c>WaterOnly</c>) ходит лишь по глубокой воде; на любой суше
    /// и на мелкой воде он получает <c>0</c> при <c>ForbidWaterOnLand</c> и 1000 без него
    /// (движок так рисует запрещённый путь, не тратя очки). Флаг живого лидера в этой ветке не
    /// смотрится вовсе.</para>
    /// </summary>
    public static int Cost(MapState map, int x, int y, MoveOptions opt, MovementRules rules)
    {
        if (!map.Contains(x, y)) return Forbidden;
        if (BlockedByObject(map, x, y)) return Forbidden;

        var ground = map.At(x, y).Ground;

        if (opt.WaterOnly)
        {
            if (ground == Ground.Water && IsDeepWater(map, x, y)) return rules.DeepWaterForWaterOnly;
            if (ground is not (Ground.Water or Ground.Forest or Ground.Plain)) return Forbidden;
            return opt.ForbidWaterOnLand ? Forbidden : rules.LandForWaterOnly;
        }

        return ground switch
        {
            Ground.Water => Pick(rules.Water, opt.LeaderAlive, opt.WaterBonus),
            Ground.Forest => Pick(rules.Forest, opt.LeaderAlive, opt.ForestBonus),
            Ground.Plain => !opt.LeaderAlive ? rules.Plain.DeadLeader
                          : opt.PlainsBonus ? rules.Plain.WithBonus
                          : map.IsRoad(x, y) ? rules.Road
                          : rules.Plain.Default,
            _ => Forbidden,   // горы и любой незнакомый грунт
        };
    }

    private static int Pick(GroundCost cost, bool leaderAlive, bool bonus) =>
        !leaderAlive ? cost.DeadLeader : bonus ? cost.WithBonus : cost.Default;

    /// <summary>
    /// Стоит ли на клетке объект, мимо которого не пройти (см. <see cref="BlockingKinds"/>).
    /// Занятость берётся из <see cref="MapState.Plan"/> — того же <c>CMidgardPlan</c>, который
    /// спрашивает движок.
    /// </summary>
    public static bool BlockedByObject(MapState map, int x, int y) =>
        map.Plan.TryGetValue((x, y), out var ids) && ids.Any(IsBlocking);

    private static bool IsBlocking(string id) =>
        id.Length >= 6 && BlockingKinds.Contains(id.Substring(4, 2), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Глубокая вода: сама клетка — вода и все её соседи, <b>лежащие на карте</b>, тоже вода.
    /// Ранг 1: <c>isWaterTileSurroundedByWater</c> (<c>0x5e6453</c>) сначала проверяет грунт самой
    /// клетки, затем обходит восемь соседей и выходит с <c>false</c> на первом не-водном;
    /// сосед за границей карты пропускается (<c>je 0x5e6505</c> — переход на инкремент, а не на
    /// выход), поэтому вода у края карты глубокой быть может.
    /// </summary>
    public static bool IsDeepWater(MapState map, int x, int y)
    {
        if (!map.Contains(x, y) || map.At(x, y).Ground != Ground.Water) return false;
        foreach (var (dx, dy) in Neighbours)
        {
            int nx = x + dx, ny = y + dy;
            if (map.Contains(nx, ny) && map.At(nx, ny).Ground != Ground.Water) return false;
        }
        return true;
    }

    /// <summary>
    /// Пускает ли клетка отряд с такими флагами — то же, что «стоимость не <see cref="Forbidden"/>».
    /// Это <b>проходимость по местности</b>, и она не имеет отношения к движковой
    /// <c>stackCanMoveToPosition</c>: та про занятость клетки другим отрядом и грунта не смотрит
    /// вовсе (см. <see cref="CanMoveTo"/>).
    /// </summary>
    public static bool Passable(MapState map, int x, int y, MoveOptions opt) =>
        Cost(map, x, y, opt, MovementRules.Vanilla) != Forbidden;

    /// <summary>
    /// Движковая <c>stackCanMoveToPosition</c> (Gog <c>0x4cd1dd</c>), ранг 1: клетка, где отряд уже
    /// стоит, — <c>false</c>; иначе <c>false</c>, только если в клетке стоит другой отряд, и
    /// <c>true</c> в остальных случаях. Грунт эта функция не читает — по её мнению гора «доступна»,
    /// запрещает её <see cref="Cost"/>.
    ///
    /// <para><b>Потолок:</b> движок пропускает и занятую клетку, если чужой отряд невидим
    /// (<c>cmp byte ptr [eax+0x9b], 0</c> — поле <c>CMidStack::invisible</c>, <c>midstack.h:77</c>,
    /// смещение 155). В <see cref="MapStack"/> этого поля нет, поэтому здесь невидимый отряд
    /// считается препятствием; сколько раз это могло сказаться, печатает харнесс.</para>
    /// </summary>
    /// <param name="positions">Где сейчас стоит каждый отряд карты. Отдельным аргументом, а не
    /// из <see cref="MapState"/>, потому что в сверке по журналу позиции живые: отряды за прогон
    /// разъезжаются, и <c>.sg</c> помнит только их расстановку на старте.</param>
    public static bool CanMoveTo(IReadOnlyDictionary<string, (int X, int Y)> positions, string stackId,
                                 int x, int y)
    {
        if (positions.TryGetValue(stackId, out var self) && self.X == x && self.Y == y) return false;
        foreach (var (id, p) in positions)
            if (p.X == x && p.Y == y && !id.Equals(stackId, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    /// <summary>То же по расстановке из файла карты.</summary>
    public static bool CanMoveTo(MapState map, MapStack stack, int x, int y) =>
        CanMoveTo(Positions(map), stack.Id, x, y);

    /// <summary>Расстановка отрядов из файла — начальное состояние для сверки по журналу.
    /// Отряд внутри города или руины (<see cref="MapStack.Inside"/>) для движка стоит у ВХОДА, а не
    /// в левом верхнем углу объекта, который записан в его <c>POS</c>: на дне 1 прогона 20260907-c4
    /// <c>stackCanMoveToPosition</c> ответила <c>false</c> на (9,9) отряду со стартовой позицией
    /// (5,5) в столице 5×5 (<c>can_move seq 95073</c>) — «своя клетка». Вход — правый нижний
    /// угол пятна (<c>getFortOrRuinEntrance</c>, ранг 1, Gog <c>0x4cd253</c>); пятно берётся из
    /// <see cref="MapState.Plan"/>. Объекта в плане нет — остаётся <c>POS</c>.</summary>
    public static Dictionary<string, (int X, int Y)> Positions(MapState map) =>
        map.Stacks.Values.ToDictionary(
            s => s.Id,
            s => s.Inside.Length == 0 ? (s.X, s.Y) : Entrance(map, s.Inside) ?? (s.X, s.Y),
            StringComparer.OrdinalIgnoreCase);

    private static (int X, int Y)? Entrance(MapState map, string objectId)
    {
        int? maxX = null, maxY = null;
        foreach (var ((x, y), ids) in map.Plan)
        {
            if (!ids.Contains(objectId, StringComparer.OrdinalIgnoreCase)) continue;
            maxX = Math.Max(maxX ?? x, x);
            maxY = Math.Max(maxY ?? y, y);
        }
        return maxX is null ? null : (maxX.Value, maxY!.Value);
    }

    /// <summary>
    /// Очки хода отряда на новый день — <c>MOVE</c> лидера из <c>Gunits.dbf</c> (у героя
    /// <c>g000uu0019</c> это 20 — ранг 1, значение прочитано в таблице).
    ///
    /// <para><b>Ранг 3 — само правило:</b> что движок берёт именно <c>MOVE</c> лидера, а не минимум
    /// или максимум по отряду, данными пока не подтверждено. Проверяется по журналу: первое
    /// <c>stack_move</c> нового дня даёт <c>move_left</c>, из которого видно исходное число. Тип
    /// лидера неизвестен или его нет в таблице — <paramref name="fallback"/>.</para>
    ///
    /// <para><b>Мёртвый лидер запас НЕ урезает — ранг 1 (задача 26 Ф7).</b> Байт
    /// <c>CMidStack::movement</c> (<c>+0x98</c>, <c>midstack.h:76</c>) во всём образе пишут ровно
    /// две инструкции: <c>0x5EBFBE</c> (<c>CMidStack::initialize</c> — сразу <c>MOVE</c> лидера,
    /// слот <c>vftable+4</c>, <c>0x5EBF9E</c>) и <c>0x5ECFD5</c>
    /// (<c>CVisitorChangeStackMoveAllowance</c>, <see cref="Spend"/>); ни одна не читает
    /// <c>leaderAlive</c> (<c>+0x38</c>). Вики («отряд с павшим лидером ходит вдвое медленнее»,
    /// разбор §4 Н17) описывает <b>удвоенную цену шага</b> <see cref="GroundCost.DeadLeader"/>,
    /// а не половинный запас: на дороге мёртвый лидер платит вчетверо (1 → 4), а под бонусом
    /// лидера бонус отменяется и цена растёт вчетверо у леса (2 → 8) и вшестеро у воды (2 → 12).
    /// Сверяет харнесс <c>dead_leader_ground</c>.</para>
    ///
    /// <para><b>Уровневые навыки лидера — задача 11 Ф7.</b> Pathfinding (+20 %) и Advanced
    /// Pathfinding (+25 %) — это элементы <c>MoveAllowance</c> цепочки <c>MidUnit.MODIF_ID</c>, и
    /// накладывает их <see cref="EffectiveStats.OfStack"/>. <paramref name="modifiers"/> —
    /// справочник <c>Gmodif</c>; не подан (<c>null</c>) — цепочка не читается и число остаётся
    /// базовым, ровно как до задачи 11. Модификатор цепочки, которого в справочнике нет,
    /// пропускается молча: занижение запаса хода видно харнессом (<c>leader_move_max</c>), а
    /// падать посреди дневного тика из-за чужой строки нельзя.</para>
    /// </summary>
    public static int StartMovement(MapStack stack, MapState map, IReadOnlyDictionary<string, int> moveByType,
                                    int fallback = 0,
                                    IReadOnlyDictionary<string, Modifier>? modifiers = null)
    {
        if (!map.Units.TryGetValue(stack.LeaderId, out var leader)) return fallback;
        if (!moveByType.TryGetValue(leader.TypeId, out int move)) return fallback;
        return StartMovement(leader, move, modifiers);
    }

    /// <summary>Дневной запас лидера от готовой базы <paramref name="baseMove"/> — <c>MOVE</c> типа, выращенный по
    /// уровню (<c>GDynUpgr.MOVE</c>, <c>0x598b68</c>), — с цепочкой модификаторов лидера.</summary>
    public static int StartMovement(MapUnit leader, int baseMove, IReadOnlyDictionary<string, Modifier>? modifiers = null)
    {
        ArgumentNullException.ThrowIfNull(leader);
        return EffectiveStats.OfStack(Chain(leader, modifiers), moveAllowance: baseMove).MoveAllowance;
    }

    /// <summary>Цепочка модификаторов юнита как список <see cref="Modifier"/>; справочника нет —
    /// цепочка пуста. Общее место <see cref="StartMovement"/> и <see cref="Fog.Radius"/>.</summary>
    internal static IReadOnlyList<Modifier> Chain(MapUnit unit,
                                                  IReadOnlyDictionary<string, Modifier>? modifiers) =>
        modifiers is null ? []
            : unit.Modifiers.Select(id => modifiers.GetValueOrDefault(id)).OfType<Modifier>().ToList();

    /// <summary>
    /// Остаток хода после действия, которое съедает ход <b>целиком</b>: постановка жезла и его снос
    /// (<see cref="Rods.Place"/>, <see cref="Rods.Break"/>). Не «минус цена шага» и не «минус
    /// половина» — ровно ноль.
    /// <para><b>Ранг 1</b> по прогону 2 <c>m6-rods</c> (run2 §2.2): <c>MOVE</c> в сохранении равен 0
    /// ровно у того жезлоносца, который в этот ход ставил или сносил жезл, и остаётся 20 у тех, кто
    /// не делал ни того, ни другого. Шесть наблюдений: три установки (ход 1) и три сноса (ходы 2 и 3).
    /// Сверяет харнесс задачи 8 (<c>rod_place_move</c>, <c>rod_break_move</c>).</para>
    /// <para>Величина одна на оба действия, а не по одной на команду: «съесть ход целиком» — одно
    /// правило движка, и цена у постановки и сноса измерена одинаковой.</para>
    /// </summary>
    public const int WholeTurnSpent = 0;

    /// <summary>
    /// Половина запаса хода — цена действия, которое движок берёт «половиной»: атака по отряду или
    /// по укреплению, действие вора (его обработчик Дуэлю не списывает, но результат Дуэли платит
    /// сам — см. ниже и §3 п. 56 плана Ф7) и столкновение с иллюзией. Считается от <b>полного</b>
    /// запаса лидера, а не от остатка.
    ///
    /// <para><b>Ранг 1 — одна функция движка на все эти случаи</b> (GOG <c>0x5CE239</c>,
    /// <c>ret 8</c>, аргументы «карта объектов, id отряда»): по id берётся отряд, у него лидер
    /// (<c>[stack+0x1c]</c>, <c>0x5E97D8</c>), лидер приводится к <c>IUsSoldier</c>
    /// (<c>0x40A41F</c>), его <c>MOVE</c> читается слотом <c>vftable+4</c>, дальше
    /// <c>fild; fmul qword [0x6EDE68]</c> (константа <b>0.5</b>) <c>; ceil</c>
    /// (<c>0x5CE299…0x5CE2A6</c>) — то есть <c>ceil(MOVE / 2)</c>.</para>
    ///
    /// <para><b>Атака платит её же — вики §4 Н16, ранг 1.</b> Все <b>семь</b> вызовов
    /// <c>0x5CE239</c> кладут результат в <c>CEffectChangeAP</c> (<c>0x43588C</c>). Четыре из них
    /// воровские: <c>0x442F9F</c>, <c>0x443181</c>, <c>0x443581</c> в обработчике действия
    /// (<see cref="Thieves"/>) и <c>0x41D8B6</c> — слот 3 <c>CNobleActionResultDuel</c>
    /// (vftable <c>0x6CDC3C</c>). Три картовых, в обработчике приказа отряду <c>0x440667</c> и его
    /// соседе <c>0x4408CB</c>:
    /// <list type="bullet">
    /// <item><c>0x44078C</c> — цель-<b>иллюзия</b> (<c>IUsIllusion</c>, приведение
    /// <c>0x438FF3</c>): вместо боя <c>CEffectKillStack</c> (<c>0x4388E4</c>);</item>
    /// <item><c>0x44083E</c> — <b>атака</b> по цели, у которой есть юниты (<c>0x5F50FD &gt; 0</c>;
    /// у укрепления, тип id <c>0x1D</c>, дополнительно <c>[fort+0x20] &gt; 0</c>,
    /// <c>0x5E98AD</c>);</item>
    /// <item><c>0x440AA8</c> — шаг на клетку укрепления.</item>
    /// </list>
    /// В каждом из этих трёх и в Дуэли списание идёт <b>перед</b> <c>CEffectBattle</c>
    /// (<c>0x4338BA</c>): бой стоит половины запаса независимо от исхода.</para>
    ///
    /// <para>Ветку «отступивший отходит на лишнюю клетку бесплатно» и «атакованный не тратит»
    /// (та же Н16) здесь не моделируем: отдельного списания у них нет — вызовов
    /// <c>0x5CE239</c> всего семь и все перечислены.</para>
    /// </summary>
    public static int HalfAllowance(int moveAllowance) =>
        moveAllowance <= 0 ? 0 : (moveAllowance + 1) / 2;

    /// <summary>
    /// Остаток очков хода после списания, с полом нуля —
    /// <c>CVisitorChangeStackMoveAllowance::apply</c> (GOG <c>0x5ECF76</c>, зовётся из
    /// <c>0x60D2E2</c> с <b>минус</b> величиной, <c>neg ecx</c> <c>0x60D30B</c>):
    /// <c>movement += delta</c>, затем обрезка в <c>[0, MOVE лидера]</c>
    /// (<c>0x5ECFC7…0x5ECFD5</c>). Верхняя граница вычитанию безразлична, поэтому здесь только пол.
    /// </summary>
    public static int Spend(int moveLeft, int cost) => Math.Max(moveLeft - cost, 0);

    /// <summary>Остаток хода атакующего после атаки по карте: <see cref="Spend"/> на
    /// <see cref="HalfAllowance"/> полного запаса.</summary>
    /// <param name="moveAllowance">Полный запас хода лидера на день, не остаток.</param>
    /// <param name="moveLeft">Остаток очков хода до атаки.</param>
    public static int AfterAttack(int moveAllowance, int moveLeft) =>
        Spend(moveLeft, HalfAllowance(moveAllowance));

    /// <summary>
    /// Проходит путь клетка за клеткой, пока хватает очков хода (<see cref="MapStack.Move"/>), и
    /// возвращает пройденную часть; <paramref name="spent"/> — потраченные очки.
    ///
    /// <para><b>Ранг 1 — правило остановки</b> (<c>0x5cd5b2</c>, разбор <c>docs/map-path.md</c> §2): клетка
    /// принимается, если очков хода <b>осталось хоть сколько-то</b>, и уже <b>после</b> неё путь обрывается, когда
    /// накопленная цена не меньше остатка. То есть последний шаг дня делается в долг, а остаток обрезается нулём
    /// (<c>0x5ecf76</c>). В журнале <c>20260919-human</c> отряд <c>S073KC0070</c> с тремя очками прошёл две клетки
    /// по 2 и остался с нулём. Запрещённая клетка (<see cref="Forbidden"/>) обрывает путь.</para>
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> Walk(
        MapState map, MapStack stack, IReadOnlyList<(int X, int Y)> path,
        MoveOptions opt, MovementRules rules, out int spent)
    {
        spent = 0;
        var walked = new List<(int X, int Y)>();
        int left = stack.Move;
        foreach (var (x, y) in path)
        {
            if (left <= 0) break;
            int cost = Cost(map, x, y, opt, rules);
            if (cost == Forbidden) break;
            left = Spend(left, cost);
            spent += cost;
            walked.Add((x, y));
        }
        return walked;
    }
}
