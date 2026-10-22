namespace D2.Rules.Map;

/// <summary>
/// Записанный маршрут прогона: путь по клеткам и разрез его на ходы. Читается из
/// <c>route.json</c>, который строит <c>tools/scenario_c4.py route</c> и играет
/// <c>tools/corpus_c4_run.py</c>; чтение файла — <c>D2.Data.Map.RouteLoader</c>
/// (<c>D2.Rules</c> файлов не читает).
///
/// <para><b>Отличие от интерфейса задачи 9 плана:</b> добавлено поле <see cref="Path"/>. Без него
/// модель знала бы только конечную клетку хода и должна была бы заново искать путь Дейкстрой —
/// то есть повторять чужой код и сверяться не с тем маршрутом, который сыграл человек.
/// <c>route.json</c> путь уже содержит.</para>
/// </summary>
/// <param name="Path">Все клетки маршрута подряд, начиная с клетки, где отряд стоит.</param>
/// <param name="Turns">Конечная клетка каждого хода (<c>turn</c> нумеруется с 1).</param>
public sealed record MapRoute(
    string Scenario,
    string StackId,
    IReadOnlyList<(int X, int Y)> Path,
    IReadOnlyList<(int Turn, int X, int Y)> Turns,
    IReadOnlyList<int> Checkpoints);

/// <summary>
/// Пять величин критерия С4 на контрольном ходе плюс остаток очков хода (шестой, справочный —
/// см. <see cref="MapSim"/>). Такой же снимок снимается и с модели, и с сохранения игры.
/// </summary>
/// <param name="UnitType">Тип юнита по id (<c>Gunits.TYPE</c>). Нужен сверке состава: движок
/// пересоздаёт стартового героя под лорда, выбранного на экране лорда, с НОВЫМ id (прогон
/// 20260907-c4: <c>S143UN0001</c> в <c>map-start.sg</c> → <c>S143UN0002</c> в каждом сохранении,
/// тот же тип <c>g000uu0021</c>, уровень, hp и опыт; лорд <c>G000LR0001</c> → <c>G000LR0003</c>),
/// поэтому по id состав не сходится никогда, а по типам — сходится.</param>
public sealed record MapCheckpoint(
    int Turn,
    Bank Bank,
    IReadOnlyDictionary<string, (int X, int Y, int Move)> Stacks,
    IReadOnlyDictionary<string, int> UnitXp,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Squads,
    bool[,] Fog,
    IReadOnlyDictionary<string, string>? UnitType = null);

/// <summary>То, что модель заведомо не считает, — с причиной и ходом, на котором это сказалось.
/// Список пропусков печатается харнессом отдельным блоком: молчаливое «сошлось» на невычисленной
/// величине — худший вид зелёного теста.</summary>
public sealed record SimNote(int Turn, string Quantity, string Reason);

/// <summary>
/// Настройки прогона. Отличие от интерфейса задачи 9 (<c>MapSim(start, economy, rules, movement,
/// rng)</c>): <c>GameRules</c> и <c>IRng</c> не передаются — это справочники и генератор боя (Ф4),
/// а в ходе по карте не участвует ни одна случайная величина, и подставлять их значило бы делать
/// вид, что модель их использует. Всё, чего нет в <see cref="MapState"/> (сложность партии,
/// <c>MOVE</c> и <c>SCOUT</c> из <c>Gunits.dbf</c>, города как источники обзора), приходит сюда
/// параметрами.
/// </summary>
/// <param name="Eyes">Неподвижные источники обзора игрока (города): у <see cref="MapObject"/> нет
/// владельца, поэтому их подаёт вызывающий — так же, как в <see cref="Fog.Recompute"/>.</param>
public sealed record MapSimSettings(
    string PlayerId,
    EconomyRules Economy,
    MovementRules Costs,
    Difficulty Difficulty = Difficulty.Average,
    IReadOnlyDictionary<string, int>? MoveByType = null,
    IReadOnlyDictionary<string, int>? ScoutByType = null,
    IReadOnlyList<(int X, int Y, int Radius)>? Eyes = null,
    FogShape Shape = Fog.DefaultShape,
    MoveOptions? Options = null,
    bool RevealEachStep = true,
    // Справочник Gmodif: им уровневые навыки лидера правят запас хода (Pathfinding) и обзор
    // (Keen-Sight) — задача 11 Ф7. Не подан — цепочка не читается: на маршруте С4 у лидеров
    // уровневых навыков нет вовсе, и подстановка ничего не сдвинула бы (ср. §3 п. 26 плана).
    IReadOnlyDictionary<string, Units.Modifier>? Modifiers = null);

/// <summary>
/// Проигрывает записанный маршрут на модели и снимает контрольные точки — вторая половина С4
/// (первая — сохранения самой игры, снятые тем же маршрутом).
///
/// <para><b>Порядок внутри дня — ранг 2, снят с журнала прогона 20260907-c4:</b> доход дня 1
/// начисляется при старте партии (событие <c>income turn=1</c> — <c>seq 3</c>, раньше любого
/// хода), затем каждый конец хода — это смена дня: доход следующего дня и полные очки хода
/// (<c>income turn=N+1</c> идёт сразу за последним <c>stack_move</c> дня N, а в сохранении после
/// хода N стоит день N+1 и <c>MOVE</c> = полный). Поэтому снимок контрольной точки «после хода N»
/// снимается ПОСЛЕ смены дня — ровно то состояние, которое <c>corpus_c4_run</c> сохраняет в
/// <c>save-NN.sg</c>.</para>
///
/// <para><b>Выход из города — ранг 3:</b> отряд, стоящий внутри города (<see cref="MapStack.Inside"/>),
/// на первом ходе переносится в первую клетку маршрута бесплатно. Основание — журнал того же
/// прогона: на дне 1 все восемь <c>stack_move</c> отряда <c>S143KC0000</c> (<c>seq 97788..97811</c>)
/// идут с <c>move_left 20</c>, то есть выход из столицы в (10,10) очков не тратил.</para>
///
/// <para><b>Что модель не считает</b> (уходит в <see cref="Notes"/>, а не в тихое «сошлось»):
/// траты (найм, постройка, покупка у лавки, обучение у наставника — остаток задач 7 и 8), опыт
/// (его дают бой и наставник), посещения объектов, события сценария и любые ходы чужих игроков.
/// Земля игрока с задачи 12 растёт (<see cref="LandModel"/>) и доход считается по пятну ДНЯ, но
/// смену хозяина у объектов (захват города, гибель столицы) модель по-прежнему не считает.</para>
///
/// <para><b>Остаток очков хода</b> (<c>Move</c> в снимке) — шестая, справочная величина: в критерий
/// С4 она не входит, но именно она различает «разошлась стоимость шага» и «разошлась модель хода»
/// (точка отмены 4 плана Ф5), поэтому снимается и печатается.</para>
/// </summary>
public sealed class MapSim
{
    private readonly MapState _map;
    private readonly MapSimSettings _cfg;
    private readonly MapPlayer _player;
    private readonly Dictionary<string, (int X, int Y)> _pos;
    private readonly Dictionary<string, int> _left = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SimNote> _notes = [];
    private readonly bool[,] _fog;
    private readonly LandModel? _land;
    private Bank _bank;
    private int _day = 1;

    /// <summary>Ходов сыграно (последний проигранный ход маршрута).</summary>
    public int Turn { get; private set; }

    /// <summary>Казна игрока на текущий момент прогона.</summary>
    public Bank Bank => _bank;

    /// <summary>Где сейчас стоят отряды.</summary>
    public IReadOnlyDictionary<string, (int X, int Y)> Positions => _pos;

    public IReadOnlyList<SimNote> Notes => _notes;

    /// <summary>Модель роста земли игрока; <c>null</c>, если у игрока на карте нет своей земли.</summary>
    public LandModel? Land => _land;

    public MapSim(MapState start, MapSimSettings settings)
    {
        _map = start;
        _cfg = settings;
        if (!start.Players.TryGetValue(settings.PlayerId, out var player))
            throw new ArgumentException(
                $"игрока {settings.PlayerId} нет на карте (есть: {string.Join(", ", start.Players.Keys)})",
                nameof(settings));
        _player = player;
        _bank = player.Bank;
        _pos = Movement.Positions(start);
        _fog = Fog.Empty(start.Size);

        // Стартовый туман берётся не из файла: в сценарии все биты нулевые (туман расставляется
        // при старте партии, задача 4), то есть «всё открыто» — сверять с ним нечего. Модель
        // начинает с полностью скрытой карты и открывает то, что видно с начальных позиций.
        foreach (var s in Own()) Reveal(s.X, s.Y, RadiusOf(s));
        foreach (var (x, y, r) in _cfg.Eyes ?? []) Fog.Reveal(_fog, x, y, r, _cfg.Shape);

        // Что модель не считает вовсе — сказано один раз (ход 0), а не по разу на каждый день:
        // это свойство модели, а не событие прогона.
        Note(0, "казна", "траты не моделируются: найм, постройка, покупка у лавки, обучение " +
                         "(остаток задач 7 и 8) — расхождение казны может быть тратой, а не доходом");
        // Земля растёт каждый день, и жила платит только на своей земле (задача 12): доход
        // считается по ПЯТНУ ДНЯ, а не по стартовой карте. Рост — обход движка поштучно (LandModel,
        // §3 п. 7), источники — столица, свои деревни (MapState.Sites, §3 п. 26) и свои жезлы.
        try
        {
            _land = LandModel.Build(start, settings.PlayerId, settings.Economy);
            Note(0, "казна", "квота деревни — по её уровню на старте: рост города (трата) не моделируется");
        }
        catch (ArgumentException e)
        {
            _land = null;
            Note(0, "казна", $"рост земли не считается ({e.Message}) — доход по стартовой карте");
        }
        Note(0, "опыт", "опыт даёт бой (Ф4) и наставник (не моделируется) — модель опыт не меняет");
        Note(0, "состав", "найм и обучение не моделируются — модель состав отрядов не меняет");
        if (_cfg.Eyes is null || _cfg.Eyes.Count == 0)
            Note(0, "туман", "города как источники обзора не поданы (у MapObject нет владельца) — " +
                             "открытым считается только то, что видели отряды");
    }

    /// <summary>
    /// Проигрывает маршрут ход за ходом и возвращает снимок на каждом ходе из
    /// <paramref name="checkpointTurns"/> в порядке возрастания хода.
    /// </summary>
    /// <exception cref="ArgumentException">Отряда маршрута нет на стартовой карте, или маршрут
    /// начинается не там, где отряд стоит.</exception>
    public IReadOnlyList<MapCheckpoint> Run(MapRoute route, IReadOnlyList<int> checkpointTurns)
    {
        if (!_map.Stacks.TryGetValue(route.StackId, out var stack))
            throw new ArgumentException($"отряда {route.StackId} нет на стартовой карте", nameof(route));
        if (route.Path.Count == 0)
            throw new ArgumentException("маршрут пуст", nameof(route));
        if (route.Path[0] != (stack.X, stack.Y))
        {
            if (stack.Inside.Length == 0)
                throw new ArgumentException(
                    $"маршрут начинается в {route.Path[0]}, а отряд {stack.Id} стоит в ({stack.X},{stack.Y})",
                    nameof(route));
            // Выход из города: бесплатный перенос к первой клетке маршрута (см. описание класса).
            _pos[stack.Id] = route.Path[0];
        }

        var want = new HashSet<int>(checkpointTurns);
        var shots = new List<MapCheckpoint>();
        int index = 0;

        StartDay();                                   // день 1: доход начислен при старте партии
        foreach (var (turn, tx, ty) in route.Turns.OrderBy(t => t.Turn))
        {
            Turn = turn;
            index = Walk(stack, route.Path, index, (tx, ty), turn);
            StartDay();                               // конец хода = смена дня
            if (want.Contains(turn)) shots.Add(Snapshot(turn));
        }
        return shots;
    }

    /// <summary>Смена дня: земля подросла, доход в казну, полные очки хода отрядам игрока.
    /// <para>ponytail: дневного тика HP (<see cref="Regeneration.Tick"/>, задача 6 Ф7) здесь нет, и
    /// это не забывчивость. Модель хода по карте HP юнитов не держит вовсе: в <see cref="MapCheckpoint"/>
    /// их нет, контрольная точка С4 сверяет казну, позиции, опыт, состав и туман — потребителя у
    /// величины не существует, а лечить поле, которого никто не читает, значило бы заводить состояние
    /// ради красоты. Потолок известен: как только в снимок войдут HP (бой на карте, лечение по ходу
    /// прогона), тик встраивается сюда одной строкой на отряд — правило уже написано и сверено
    /// (<c>regen_tick</c> 135/0).</para></summary>
    private void StartDay()
    {
        _bank = Treasury.Apply(_bank, Economy.DailyIncome(
            _map, _cfg.PlayerId, _cfg.Economy, _cfg.Difficulty, _land?.Certain(_day)));
        _day++;

        foreach (var s in Own())
            _left[s.Id] = Movement.StartMovement(s, _map, _cfg.MoveByType ?? new Dictionary<string, int>(),
                                                 fallback: s.Move, modifiers: _cfg.Modifiers);
    }

    /// <summary>Шаги по маршруту, пока хватает очков и пока не достигнута клетка конца хода.</summary>
    private int Walk(MapStack stack, IReadOnlyList<(int X, int Y)> path, int index, (int X, int Y) target,
                     int turn)
    {
        var opt = _cfg.Options ?? new MoveOptions(LeaderAlive: stack.LeaderAlive);
        int radius = RadiusOf(stack);
        Reveal(_pos[stack.Id].X, _pos[stack.Id].Y, radius);   // клетка после выхода из города

        while (index + 1 < path.Count && _pos[stack.Id] != target)
        {
            var (x, y) = path[index + 1];
            int cost = Movement.Cost(_map, x, y, opt, _cfg.Costs);
            if (cost == Movement.Forbidden)
            {
                Note(turn, "позиции", $"клетка ({x},{y}) запрещена местностью — маршрут дальше не проигран");
                break;
            }
            if (cost > _left[stack.Id]) break;      // ранг 3: шаг делается только целиком (Movement.Walk)

            _left[stack.Id] -= cost;
            _pos[stack.Id] = (x, y);
            index++;
            if (_cfg.RevealEachStep) Reveal(x, y, radius);
        }
        if (!_cfg.RevealEachStep) Reveal(_pos[stack.Id].X, _pos[stack.Id].Y, radius);   // диагностика геометрии

        if (_pos[stack.Id] != target)
            Note(turn, "позиции", $"ход кончился в {_pos[stack.Id]}, цель хода {target} — очков не хватило " +
                                  "или клетка запрещена");
        return index;
    }

    private MapCheckpoint Snapshot(int turn) => new(
        turn,
        _bank,
        _map.Stacks.Keys.ToDictionary(id => id,
            id => (_pos[id].X, _pos[id].Y, _left.GetValueOrDefault(id)), StringComparer.OrdinalIgnoreCase),
        _map.Units.Values.ToDictionary(u => u.Id, u => u.Xp, StringComparer.OrdinalIgnoreCase),
        _map.Stacks.Values.ToDictionary(s => s.Id, s => s.UnitIds, StringComparer.OrdinalIgnoreCase),
        (bool[,])_fog.Clone(),
        _map.Units.Values.ToDictionary(u => u.Id, u => u.TypeId, StringComparer.OrdinalIgnoreCase));

    private IEnumerable<MapStack> Own() =>
        _map.Stacks.Values.Where(s => s.Owner.Equals(_cfg.PlayerId, StringComparison.OrdinalIgnoreCase));

    private int RadiusOf(MapStack stack) =>
        Fog.Radius(stack, _map, _cfg.ScoutByType ?? new Dictionary<string, int>(), fallback: 1,
                   modifiers: _cfg.Modifiers);

    private void Reveal(int x, int y, int radius) => Fog.Reveal(_fog, x, y, radius, _cfg.Shape);

    /// <summary>Пропуск с причиной; одинаковые причины на одном ходе не дублируются.</summary>
    private void Note(int turn, string quantity, string reason)
    {
        if (!_notes.Any(n => n.Turn == turn && n.Quantity == quantity && n.Reason == reason))
            _notes.Add(new SimNote(turn, quantity, reason));
    }

    /// <summary>Идентификатор тумана игрока — по нему берётся эталонная маска из сохранения.</summary>
    public string FogId => _player.FogId;
}
