using LastEdict.Data.Content;
using LastEdict.Data.Map;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Play;

/// <summary>Ход партии: держит текущий снимок (<see cref="GameState"/>) и меняет его командами игрока и правилами
/// дня. Снимки неизменяемы — команда строит новый; сцена карты только показывает <see cref="Looks"/> и зовёт команды.
/// Всё здесь проверяется без Godot.</summary>
public sealed partial class GameSession(GameState state, MapArtData art, GameBook? book = null)
{
    public GameState State
    {
        get => _state;
        private set
        {
            if (_executing == 0) ChangedOutsideCommands?.Invoke();
            _state = value;
        }
    }

    private GameState _state = state;

    /// <summary>Правила боя и опыта; без них партия ходит, но воевать не может.</summary>
    public GameBook? Book { get; } = book;

    /// <summary>Генератор случайных чисел боёв: один поток на всю партию, как у движка (R250 над таблицей,
    /// <see cref="EngineRng"/>). Продолжается с сохранённого места (<see cref="GameState.Random"/>).</summary>
    public EngineRng Rng { get; } = state.Random is { } r ? EngineRng.FromSeed(r.Table, r.Index) : new();

    /// <summary>CRT <c>rand()</c> потока игры (msvcrt, старт <c>srand(1)</c>): им ИИ выбирает юнитов найма
    /// (<c>0x44c137</c>) и имена лидеров. Продолжается с сохранённого места.</summary>
    public LastEdict.Rules.Rng.CrtRand Crt { get; } = new(state.Random?.Crt ?? 1);

    /// <summary>Порождённые реализации, созданные в этом запуске (<see cref="GeneratedImpls"/>): с входа в партию только растут.</summary>
    private readonly HashSet<string> _generated = new(state.GeneratedUnits, StringComparer.OrdinalIgnoreCase);

    /// <summary>Снимок партии для записи: текущее состояние вместе с потоками случайных чисел и итогами хода людей, накопленными к
    /// этому мигу (<see cref="SummariesForSave"/>; нет поставщика — прежние, из загрузки).</summary>
    public GameState Snapshot() => State with
    {
        Random = new RandomStreams(Rng.Index, Rng.TableSnapshot(), Crt.State),
        Summaries = SummariesForSave?.Invoke() ?? State.Summaries,
    };

    /// <summary>Поставщик итогов хода людей для записи (<see cref="GameState.Summaries"/>): их собирает сцена (сборщики
    /// <c>TurnSummaryWatch</c>, сессия Б) — модель итогов не ведёт. Ключ — человек, запись — день снимка конца его хода и записи на
    /// этот миг.</summary>
    public Func<IReadOnlyDictionary<string, PendingSummary>>? SummariesForSave { get; set; }

    /// <summary>Итоги хода из загруженной партии — сцене, чтобы продолжить сбор с ними (<see cref="GameState.Summaries"/>); в
    /// состоянии они после этого пусты. Итоги — данные окон, правила их не читают: снятие не команда (<see cref="Execute"/>) и не правка
    /// партии мимо команд — аудит молчит, отпечаток для журнала переносится на новое состояние.</summary>
    public IReadOnlyDictionary<string, PendingSummary> TakeSummaries()
    {
        var taken = State.Summaries;
        if (taken.Count == 0) return taken;
        var before = _state;
        _state = State with { Summaries = new Dictionary<string, PendingSummary>() };
        if (_settled is { } settled && ReferenceEquals(settled.State, before)) _settled = settled with { State = _state };
        return taken;
    }

    public MapLooks Looks => State.Looks;

    public MapState Map => State.Looks.Map;

    public MapArtData Art { get; } = art;

    private ITurnObserver? _observer;
    private IReadOnlyList<ITurnObserver> _watchers = [];

    /// <summary>Наблюдатель хода (показ хода ИИ на экране); null — поведение без показа. Сеттер ставит основного; подписчики
    /// <see cref="Watch"/> (итоги хода окна, сессия Б) слышат вместе с ним всё, кроме боя (<see cref="TurnWatchers"/>).</summary>
    public ITurnObserver? Observer
    {
        get => _watchers.Count == 0 ? _observer : new TurnWatchers(_observer, _watchers);
        set => _observer = value;
    }

    /// <summary>Подписаться на события хода (<see cref="ITurnObserver"/>, кроме <see cref="ITurnObserver.Battle"/>): основной наблюдатель
    /// от этого не меняется, ход и бой идут как без подписчиков. Список заменяется целиком — ход ИИ на рабочем потоке читает старый.</summary>
    public void Watch(ITurnObserver watcher) => _watchers = [.. _watchers, watcher];

    public void Unwatch(ITurnObserver watcher) => _watchers = [.. _watchers.Where(w => !ReferenceEquals(w, watcher))];

    /// <summary>Дневной запас очков хода отряда — <c>MOVE</c> типа лидера с цепочкой его модификаторов
    /// (<see cref="Movement.StartMovement"/>: навык «Следопыт», сапоги); без правил — из <c>data/map/units.json</c>.</summary>
    public int DayAllowance(MapStack stack) => Allowance(Map, stack);

    /// <summary>Флаги хода отряда: жив ли лидер, бонусы хода его лидера (<c>data/map/move-bonus.json</c>) и «только по воде»
    /// (<see cref="WaterOnly"/>). Суша водному отряду разрешена (дорого), как у интерфейса игрока; ИИ её запрещает сам
    /// (<c>forbidWaterOnlyOnLand</c> = 1, <see cref="AiTactics.Route"/>).</summary>
    public MoveOptions MoveOptionsOf(MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        string type = Map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId : "";
        var grounds = Art.MoveBonuses.FirstOrDefault(b => b.Unit.Equals(type, StringComparison.OrdinalIgnoreCase))?.Grounds ?? [];
        return new MoveOptions(stack.LeaderAlive, grounds.Contains(0), grounds.Contains(1), grounds.Contains(3),
                               WaterOnly: WaterOnly(stack), ForbidWaterOnLand: false);
    }

    /// <summary>Отряд ходит только по воде: у типа лидера <c>WATER_ONLY</c> (слот <c>0x70</c> реализации лидера, слот 5
    /// обёртки ИИ). Ранг 2: модификаторы лидера этот признак не меняют — в ванили таких нет. Без правил — ложь.</summary>
    public bool WaterOnly(MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return Book is { } book && Map.Units.TryGetValue(stack.LeaderId, out var leader) && book.WaterOnlyOf(leader.TypeId);
    }

    /// <summary>Клетка — вода (<c>0x5e5c78</c> → категория грунта <c>0x837328</c>).</summary>
    public bool IsWater(int x, int y) => Map.Contains(x, y) && Map.At(x, y).Ground == Ground.Water;

    /// <summary>
    /// Путь отряда к клетке, как его строит и показывает интерфейс (<c>0x4cca54</c> → <c>0x5cc6da</c>: <c>forbidWaterOnlyOnLand</c> = 0,
    /// <c>a5</c> = 0, режим 0, у невидимого идущего — 1, <c>0x5cc783</c>) с его флагами (<paramref name="opt"/>, по умолчанию
    /// <see cref="MoveOptionsOf"/>) и дневным запасом. Маски <c>0x5ccac4</c> (<see cref="PathFinder.AiMasks"/>): чужие невидимые отряды —
    /// «можно» (<c>0x5ccc68</c>), путь идёт сквозь них, а последняя достижимая (<c>0x5cd5b2</c> с «видеть невидимых» = <c>a5</c> = 0) их не
    /// видит; у невидимого идущего видимые враги закрывают зону контроля. Журнал <c>20261016g-port-synth</c>, карта 8, R10 (seq 876164): путь
    /// (48,21) → (49,21) → (50,21) прямо в клетку невидимого нейтрала, <c>last</c> = цель. Порт до 17.10 обходил невидимку (жалоба пользователя).
    /// </summary>
    public StackPath PathTo(MapStack stack, int x, int y, MoveOptions? opt = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var o = opt ?? MoveOptionsOf(stack);
        if (Book is null) return PathFinder.Find(Map, stack, x, y, o, MovementRules.Vanilla, DayAllowance(stack));   // без правил — без масок
        var masks = PathFinder.AiMasks(Map, stack, x, y, Invisible(stack.Id) ? 1 : 0, o, MovementRules.Vanilla,
                                       (a, b) => AiTactics.Allied(this, a, b), bags: false, hostile: AiTactics.PathHostile(this, stack),
                                       invisible: s => Invisible(s.Id));
        return PathFinder.Find(Map, stack, x, y, o, MovementRules.Vanilla, DayAllowance(stack),
                               hostile: HumanReachHostile(stack, seeInvisible: false), masks: masks);
    }

    /// <summary>Клетка, в которую целит щелчок человека выбранным отрядом <paramref name="stack"/> (<see cref="MapCursors.ClickCell"/>,
    /// <c>0x4d04c3</c>): у пустой клетки над чужим видимым отрядом (он в (x+1, y+1)) — клетка этого отряда; туман — хозяина отряда.</summary>
    public (int X, int Y) ClickAim(MapStack stack, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(stack);
        bool[,]? fog = Map.Players.TryGetValue(stack.Owner, out var owner) && Map.Fog.TryGetValue(owner.FogId, out var f) ? f : null;
        return MapCursors.ClickCell(Map, stack.Owner, x, y, (cx, cy) => fog is not null && fog[cx, cy], Invisible);
    }

    /// <summary>
    /// Щелчок выбранным отрядом <paramref name="stack"/> с лидером-призывом или иллюзией (<see cref="UnitCategory.IsSummonImpl"/>) по своему
    /// укреплению без гостя (Г14, ранг 1 по листингу): у движка ни пути, ни входа. Показ пути по щелчку <c>0x4d1590</c> у укрепления своей
    /// подрасы (<c>+0x88</c> против <c>+0x90</c> отряда) сперва смотрит бунт (<c>0x4d1242</c>), затем <c>0x4d1a6c</c>: гостя нет
    /// (<c>0x4d3ff6</c>) — путь (<c>0x4d194c</c>) только обычному лидеру у своего укрепления, обычному живому у чужого или призыву
    /// (<c>0x431d8c</c>)/иллюзии (<c>0x438ff3</c>) у чужого (<c>0x4d1b30…0x4d1b8e</c>); призыв у своего — отказ, щелчок обработан без
    /// пути (<c>0x4d190d</c> → <c>0x4d17a8</c>), показанный прежде путь не меняется. Курсор над сердцем такого укрепления — не
    /// <c>ENTERCITY</c> (<c>0x4d0bb5</c>/<c>0x4d0bbe</c> → <c>CTaskStrat</c>, <see cref="MapCursors"/>). Вор — обычный лидер: путь есть.
    /// Карта 17, G14 — журнал оригинала даст партия.
    /// </summary>
    public bool OwnFortRefuses(MapStack stack, (int X, int Y) aim)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (Book is not { } book || !Map.Plan.TryGetValue(aim, out var ids)
            || !Map.Units.TryGetValue(stack.LeaderId, out var leader) || !UnitCategory.IsSummonImpl(book.CategoryOf(leader.TypeId))) return false;
        var fort = Map.Sites.FirstOrDefault(s => s.Kind is SiteKind.Capital or SiteKind.Village && ids.Contains(s.Id, StringComparer.OrdinalIgnoreCase));
        return fort is not null && fort.Subrace.Length > 0 && fort.Subrace.Equals(stack.Subrace, StringComparison.OrdinalIgnoreCase) && !Map.Stacks.ContainsKey(fort.StackId)
               && !(fort.Kind == SiteKind.Village && Fortifications.Rioting(fort.RiotTurn));
    }

    /// <summary>
    /// Щелчок по клетке <paramref name="x"/>,<paramref name="y"/> подтверждает показанный путь <paramref name="shown"/> к клетке
    /// <paramref name="aim"/> (<c>0x4cc8c2</c>; показанное хранит цель искателя <c>+0x10</c> и конец списка <c>+0x20</c>, <c>0x4ccaff</c>):
    /// щёлкнули по концу показанного пути — да; иначе путь к самой щёлкнутой клетке, без прицела <see cref="ClickAim"/>, должен дать ту же
    /// цель (<see cref="PathFinder.Target"/>, <c>0x5cc23d</c>) и тот же конец. Нет — щелчок только показывает путь заново (<c>0x4d1590</c>).
    /// Так щелчок по пустой клетке над врагом показывает путь к врагу и никогда его не подтверждает (журнал 20261017-port-synth, карта 10,
    /// VA1–VA3: повторные щелчки — путь заново, хода нет), а щелчок по последней клетке такого пути идёт и нападает (VA3, seq 3215905).
    /// </summary>
    public bool ClickConfirms(MapStack stack, StackPath shown, (int X, int Y) aim, int x, int y, MoveOptions? opt = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(shown);
        if (shown.Points.Count == 0) return false;
        var end = shown.Points[^1];
        if ((end.X, end.Y) == (x, y) || aim == (x, y)) return true;
        var o = opt ?? MoveOptionsOf(stack);
        if (PathFinder.Target(Map, stack, x, y, o, MovementRules.Vanilla) != PathFinder.Target(Map, stack, aim.X, aim.Y, o, MovementRules.Vanilla))
            return false;
        var again = PathTo(stack, x, y, o);
        return again.Points.Count > 0 && (again.Points[^1].X, again.Points[^1].Y) == (end.X, end.Y);
    }

    /// <summary>
    /// Помнит ли интерфейс путь команды после хода (<c>0x4cfead</c>, его зовёт выбор отряда <c>0x4cf9cc</c> — и после хода, и после боя): отряд
    /// не в крепости и не дошёл до клетки цели <paramref name="target"/>, а на точках показанного пути <paramref name="shown"/> после
    /// пройденных <paramref name="walked"/> нет отряда, жезла или мешка (типы 0x23, 0x2b, 0x27 по <c>0x5f5797</c>). Помнит — путь к той же цели
    /// показывается с новой клетки, и один щелчок по ней идёт дальше (<see cref="ClickConfirms"/>). Журнал 20261018-port-synth, карта 13: после
    /// обрыва у Гоблина, мешка и деревни (CUT-S3/B3/V3) и после засады HM1 путь показан снова (<c>path</c> seq 706470, 761996, 773735, 565703), и
    /// следующий щелчок по цели сразу пошёл (718642, 762411, 774689); после обрыва CH1/CH2 — невидимка на пути, пути нет, щелчков понадобилось
    /// два; дошедший до цели (HM2, SU, C5t) пути не показывает. Порт до 18.10 путь после хода сбрасывал всегда.
    /// </summary>
    public bool ShownPathKept(MapStack stack, StackPath shown, int walked, (int X, int Y) target)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(shown);
        if (stack.Inside.Length > 0 || (stack.X, stack.Y) == target) return false;
        for (int i = Math.Max(walked, 0) + 1; i < shown.Points.Count; i++)
        {
            if (!Map.Plan.TryGetValue((shown.Points[i].X, shown.Points[i].Y), out var ids)) continue;
            foreach (string id in ids)
                if ((Map.Stacks.ContainsKey(id) && !Same(id, stack.Id)) || Map.Rods.ContainsKey(id)
                    || Map.Sites.Any(s => s.Kind == SiteKind.Bag && !s.Gone && Same(s.Id, id)))
                    return false;
        }
        return true;
    }

    /// <summary>Путь, который интерфейс показывает после хода (<see cref="ShownPathKept"/>): остаток показанного <paramref name="shown"/> с клетки,
    /// где отряд встал (пройдено <paramref name="walked"/>), — точки те же, не пересчитанные заново (у движка после боя у Гоблина CUT-S3 — <c>path
    /// len 1</c> с той же клетки, seq 720947, хотя Гоблин уже снят), цены от новой клетки, последняя достижимая — по сегодняшнему запасу.</summary>
    public StackPath KeptPath(MapStack stack, StackPath shown, int walked)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(shown);
        int from = Math.Clamp(walked, 0, Math.Max(shown.Points.Count - 1, 0));
        if (shown.Points.Count == 0) return shown;
        int baseCost = shown.Points[from].Cost;
        var rest = new StackPath([.. shown.Points.Skip(from).Select(p => p with { Cost = p.Cost - baseCost })], 0);
        return PathFinder.Recount(Map, stack, rest, MoveOptionsOf(stack), DayAllowance(stack), HumanReachHostile(stack, seeInvisible: false));
    }

    /// <summary>
    /// Команда хода (<c>0x43fe12</c> → <c>0x5cc120(…, 1, 1)</c>; у ИИ ещё и исполнение <c>0x45abd8</c> → <c>0x45ac69</c>): те же точки, что дал
    /// путь интерфейса или ИИ, но последняя достижимая — с «видеть невидимых» = 1 (<c>0x5cd38a</c> → <c>0x5cd5b2</c>): отряд встаёт перед
    /// невидимым врагом. <paramref name="reachHostile"/> — враждебность последней достижимой того же пути, но с невидимыми. Объект действия
    /// (<see cref="PathFinder.ActionObject"/>, <c>0x5cd38a</c>) — враждебный отряд в клетке цели <paramref name="goal"/>, если команда до неё дошла,
    /// иначе первый враг вокруг последней достижимой (<c>0x5cd11f</c>) — <b>любой</b>, видимый или нет; на него команда нападает (генератор
    /// <c>0x43fc3c</c>: <c>0x43ff9d</c>, если путь не оборван событием; очки хода были — <c>0x43fe51</c>). Невидимый нападает сам
    /// (<see cref="StartBattle"/>, засада <c>0x433a57</c>). Журналы: R10 (<c>20261016g-port-synth</c>, seq 876164–876198) — невидимый нейтрал;
    /// 20260919-human seq 2376476 — <c>S073KC0070</c> шёл к (57,23), встал на (58,25) вплотную к <c>S073KC0053</c> (57,26) и напал на него.
    /// </summary>
    public (StackPath Path, string? Attack) CommandPath(MapStack stack, StackPath path, (int X, int Y) goal, Func<MapStack, bool> reachHostile,
                                                       MoveOptions? opt = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(reachHostile);
        if (path.Points.Count == 0 || Book is null) return (path, null);
        var seen = path.Points.Count >= 2 && Map.Stacks.Values.Any(s => Invisible(s.Id) && !Same(s.Owner, stack.Owner))
            ? PathFinder.Recount(Map, stack, path, opt ?? MoveOptionsOf(stack), DayAllowance(stack), reachHostile)
            : path;
        // Очки хода были (0x43fe51: [ebp+0x27] — CMidStack +0x98) — иначе нападения нет.
        if (stack.Move <= 0) return (seen, null);
        return (seen, PathFinder.ActionObject(Map, stack, seen, goal, AiTactics.ObjectHostile(this, stack)));
    }

    /// <summary>Враждебность последней достижимой у пути человека (<see cref="PathTo"/>: чужой, не союзник; <paramref name="seeInvisible"/> —
    /// и невидимый: команда хода, <see cref="CommandPath"/>).</summary>
    public Func<MapStack, bool> HumanReachHostile(MapStack stack, bool seeInvisible)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return other => !Same(other.Owner, stack.Owner) && !AiTactics.Allied(this, stack.Owner, other.Owner) && (seeInvisible || !Invisible(other.Id));
    }

    /// <summary>Отряд <paramref name="stackId"/> невидим (флаг <c>INVISIBLE</c>, <c>+0x9b</c>); нет такого отряда — ложь.</summary>
    public bool IsInvisible(string stackId) => Map.Stacks.ContainsKey(stackId) && Invisible(stackId);

    /// <summary>Обзор лидера отряда — <c>SCOUT</c> его типа с цепочкой (<see cref="Fog.Radius"/>), в границах движка.</summary>
    public int ScoutRange(MapStack stack) => ScoutRange(Map, stack);

    private int Allowance(MapState map, MapStack stack)
    {
        if (Book is not null)
        {
            // База — MOVE типа, выращенный по уровню лидера (GDynUpgr.MOVE: +1 за уровень у героев), журнал: max 20 → 21.
            if (map.Units.TryGetValue(stack.LeaderId, out var lead) && Book.Grown(lead.TypeId, lead.Level) is { } grown)
                return Movement.StartMovement(lead, grown.MoveAllowance, Book.Rules.Modifiers);
            return Movement.StartMovement(stack, map, Book.MoveByType, fallback: Math.Max(1, stack.Move),
                                          modifiers: Book.Rules.Modifiers);
        }
        string type = map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId : "";
        return Art.Unit(type) is { Move: > 0 } unit ? unit.Move : Math.Max(1, stack.Move);
    }

    private int ScoutRange(MapState map, MapStack stack)
    {
        if (Book is not null) return Fog.Radius(stack, map, Book.ScoutByType, fallback: 1, modifiers: Book.Rules.Modifiers);
        string type = map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId : "";
        int radius = Art.Unit(type) is { Scout: > 0 } unit ? unit.Scout : 1;
        return Math.Clamp(radius, Fog.MinScoutRange, Fog.MaxScoutRange);
    }

    /// <summary>Команда хода без шагов: отряд действует с места на соседнюю клетку (нападение, вход, вор, снос и постановка жезла, бросок
    /// мешка). У движка это та же команда хода (<c>CCmdMoveStackMsg</c>, путь из одной точки: журнал 20261016g seq 564403 <c>path len 1</c>),
    /// эффекты — за концом пути (<c>0x4408cb</c>); правила не меняются — только уведомление <see cref="ITurnObserver.StackMoving"/> с клеткой
    /// отряда (зонд порта: строка <c>move</c> с <c>cells</c> = 1; прежде у действий с соседней клетки её не было — вопросы R1–R7 карты 8
    /// оставались открытыми). <paramref name="spend"/> — списание такой команды (<see cref="StackPath.Charge"/>): команда, вставшая у старта
    /// перед невидимым, списывает цену конца присланного списка (<c>0x5cd0e9</c>; карта 13, CH2 и CA2 — по 6).</summary>
    public void ActInPlace(string stackId, bool attack, int spend = 0)
    {
        if (!Map.Stacks.TryGetValue(stackId, out var stack)) return;
        Observer?.StackMoving(new StackMove(stack.Id, stack.Owner, [PathFinder.Start(Map, stack)], attack));
        if (spend <= 0) return;
        var stacks = Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stack.Id] = stack with { Move = Movement.Spend(stack.Move, spend) };
        State = State with { Looks = Retold(Looks, With(Map, stacks: stacks), Looks.Stacks) };
    }

    /// <summary>Отряд пришёл в клетку <paramref name="x"/>,<paramref name="y"/> лицом <paramref name="facing"/>,
    /// потратив <paramref name="spend"/> очков: он переставлен в плане карты, вышел из объекта, а его владелец
    /// открыл обзор вокруг. <paramref name="pathEvents"/> — события, найденные упреждающей проверкой пути
    /// (<see cref="PathEvents"/>): эффекты безвредных у человека уходят в ход и применяются до проверки после хода; оборвавшие путь
    /// (<see cref="EventFiring.CutOnly"/>) только показывают невидимого — их событие проверяется после хода заново. <paramref name="today"/> —
    /// пройденные клетки пути без старта (для <see cref="Observer"/>; на правила не влияет). <paramref name="attack"/> — ход кончается
    /// нападением (только для показа, <see cref="StackMove.Attack"/>).</summary>
    public void MoveStack(string stackId, int x, int y, int facing, int spend, IReadOnlyList<EventFiring>? pathEvents = null,
                          IReadOnlyList<PathPoint>? today = null, bool attack = false)
    {
        var stack = Map.Stacks[stackId];
        // Путь оборван событием — невидимый показывается (0x43fe65 → CEffectShow с 1, 0x43fe8c; эффект — до CEffectMove).
        if (pathEvents is not null && StopsPath(stack.Owner, pathEvents)) Unhide(stackId);
        if (Observer is { } observer)
            observer.StackMoving(new StackMove(stack.Id, stack.Owner,
                [PathFinder.Start(Map, stack), .. today is null ? [(x, y)] : today.Select(p => (p.X, p.Y))], attack));
        var moved = Copy(stack, x, y, Movement.Spend(stack.Move, spend), inside: "");
        var stacks = Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[moved.Id] = moved;
        var plan = PlanWithout(Map.Plan, moved.Id, dropEmptied: false);
        var cell = plan.TryGetValue((x, y), out var ids) ? ids.ToList() : [];
        cell.Add(moved.Id);
        plan[(x, y)] = cell;
        var map = With(Map, stacks: stacks, plan: plan);
        // Обзор — по каждой точке пути (CEffectMove::apply: цикл 0x43a0cf → 0x5d52fe по списку точек; docs/map-path.md §5 п. 3), а не только в
        // конце: харнесс С4 «на каждом шаге» — 2304/2304 клеток тумана (C4HarnessTests).
        if (today is not null)
            foreach (var p in today) RevealAround(map, moved, p.X, p.Y);
        Reveal(map, moved.Owner);
        var looks = Retold(Looks, map, [.. Looks.Stacks.Select(s => s.Id.Equals(moved.Id, StringComparison.OrdinalIgnoreCase)
                        ? s with { X = x, Y = y, Facing = facing, Inside = "" } : s)]);
        State = State with { Looks = looks };
        // Ход выводит отряд из объекта: он больше не гость укрепления (у человека это делала сцена карты, у ИИ не делал никто —
        // отряд, ушедший из города, оставался в MapSite.StackId; заметка сессии Б, docs/ui-windows.md §3).
        LeftFort(stackId);
        if (pathEvents is { Count: > 0 } && Book is not null)
        {
            var rules = new EventRules(Book.Economy.Races, Book.Diplomacy);
            foreach (var f in pathEvents.Where(f => !f.CutOnly)) Fire(f, rules, null);
        }
        // События после хода отряда (StackMove 0x41e46b, триггерер — отряд); упреждающую проверку клеток пути (0x440284) делает
        // тот, у кого путь, — PathEvents (ход ИИ; ход человека из сцены карты — пока без неё).
        RunMoveEvents(stackId);
    }

    /// <summary>Конец хода человека. Дальше, как у движка (<c>docs/ai-turn-plan.md</c> §1, ранг 1 по журналам — харнесс
    /// <c>AiTurnStartHarnessTests.TurnOrder…</c>): в тот же день ходят игроки ИИ в очереди <see cref="TurnOrder"/> (по ключу
    /// расы, нейтралы последними) — у каждого
    /// начало хода (<see cref="BeginTurn"/>) и его ход (<see cref="AiTurn"/>); затем день растёт, и начинается ход
    /// человека. В конце открывается обзор всех.
    ///
    /// <para>Без правил (<see cref="Book"/>) — только очки хода, обзор и день.</para></summary>
    public DayReport EndTurn()
    {
        _endTurn = true;
        try { return EndTurnOfHuman(); }
        finally { _endTurn = false; }
    }

    /// <summary>Идёт <see cref="EndTurn"/> (ходы ИИ, смена дня, начало хода людей): бой эффекта здесь не ждёт экрана — его
    /// играет наблюдатель или автобой.</summary>
    private bool _endTurn;

    private DayReport EndTurnOfHuman()
    {
        var report = new List<string>();
        var humans = State.Info.Humans.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Book is not null)
        {
            // Очередь дня (NextPlayer 0x420929 по NetPlayerInfo): хозяин, затем остальные по ключу расы (DayOrder). Человек в очереди —
            // его начало хода (доход, земля, события) в этот момент, а не на смене дня (журнал 20260920-ui, S135: Империя → Эльфы →
            // нейтралы); «горячий стул» с ИИ между людьми — ИИ ходит между ними (листинг 0x42b8e8, 28.09).
            var order = DayOrder(Map, State.Info.Humans, Book);
            int at = order.ToList().FindIndex(id => id.Equals(ActiveHuman, StringComparison.OrdinalIgnoreCase));
            for (int k = at + 1; k < order.Count; k++)
            {
                if (Over) break;
                string player = order[k];
                if (humans.Contains(player)) return BeginHumanTurn(player, report, grow: Map.Turn > 1);
                report.Add($"ход {player}");
                Observer?.PlayerTurn(player);
                long started = System.Diagnostics.Stopwatch.GetTimestamp();   // трасса могла встать посреди хода (зонд ставит её с главного потока)
                (_aiSteps, _aiPicks) = (0, 0);
                BeginAiTurn(player, report);
                TacticTurn(player, report);
                OperationalAfter(player, report);
                AiTrace?.TurnEnded(player, Map.Turn, (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                                   _aiSteps, _aiPicks);
            }
        }
        // Партия кончилась в ходе ИИ (взята столица человека или последняя чужая) — нового дня нет.
        return Over ? new DayReport(Map.Turn, report) : NextDay(report);
    }

    /// <summary>Очередь игроков дня (<c>0x42b8e8</c>): первым — хозяин (первый человек, <see cref="GameInfo.Humans"/>[0]), затем все
    /// остальные — и люди «горячего стула», и ИИ — по ключу расы <see cref="TurnOrder"/> (нейтралы последними). Без людей — только ИИ.</summary>
    public static IReadOnlyList<string> DayOrder(MapState map, IReadOnlyList<string> humans, GameBook book)
    {
        ArgumentNullException.ThrowIfNull(humans);
        if (humans.Count == 0) return TurnOrder(map, new HashSet<string>(), book);
        var host = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { humans[0] };
        return [humans[0], .. TurnOrder(map, host, book)];
    }

    /// <summary>Партия кончилась: есть победитель (<see cref="GameInfo.Winner"/>).</summary>
    public bool Over => State.Info.Winner.Length > 0;

    /// <summary>Итог партии для игрока: победа, поражение или партия идёт (<c>null</c>).</summary>
    public bool? Won(string playerId) => Over ? State.Info.Winner.Equals(playerId, StringComparison.OrdinalIgnoreCase) : null;

    /// <summary>Человек, чей сейчас ход (<see cref="GameState.ActiveHuman"/>; пусто — первый из людей).</summary>
    public string ActiveHuman => State.ActiveHuman.Length > 0 ? State.ActiveHuman : State.Info.Humans.FirstOrDefault() ?? "";

    /// <summary>Людей больше одного — «горячий стул».</summary>
    public bool Hotseat => State.Info.Humans.Count > 1;

    /// <summary>Смена дня после ходов ИИ (<c>0x420a6b</c>): день растёт, начинается ход первого человека
    /// (<see cref="BeginHumanTurn"/>). Отдельно от <see cref="EndTurn"/> — чтобы сверять начало хода человека с журналом без
    /// ходов ИИ.</summary>
    internal DayReport NextDay(List<string>? report = null)
    {
        report ??= [];
        var map = With(Map, turn: Map.Turn + 1);
        if (Book is not null) map = RegenerateRuins(map);
        if (Book is null || State.Info.Humans.Count == 0)
        {
            if (Book is null)
                map = With(map, stacks: map.Stacks.ToDictionary(p => p.Key, p => Copy(p.Value, p.Value.X, p.Value.Y, Allowance(map, p.Value)),
                                                               StringComparer.OrdinalIgnoreCase));
            foreach (var player in map.Players.Keys) Reveal(map, player);
            State = State with { Looks = Retold(Looks, map, Looks.Stacks) };
            return new DayReport(Map.Turn, report);
        }
        State = State with { Looks = Retold(Looks, map, Looks.Stacks) };
        return BeginHumanTurn(State.Info.Humans[0], report, grow: true);
    }

    /// <summary>Начало хода человека (<c>BeginTurn 0x41e290</c>): земля (<paramref name="grow"/>; в день 1 — нет), доход, запас
    /// хода, регенерация (<see cref="BeginTurn"/>), обзор всех, затем события начала хода (<c>0x41f4ca</c>). Ход — его.</summary>
    private DayReport BeginHumanTurn(string human, List<string> report, bool grow)
    {
        var map = BeginTurn(Map, human, grow, ai: false, report);
        foreach (var player in map.Players.Keys) Reveal(map, player);
        State = State with { Looks = Retold(Looks, map, [.. Looks.Stacks.Where(s => map.Stacks.ContainsKey(s.Id))]), ActiveHuman = human };
        RunEvents(human, "", report);
        Observer?.HumanTurn(human);   // зонд порта: снимок дня и сохранение (PortJournalWatcher)
        return new DayReport(Map.Turn, report);
    }

    /// <summary>Кто ходит после людей — очередь <c>NetPlayerInfo</c> движка (ранг 1 по листингу, харнесс
    /// <c>AiTurnStartHarnessTests.TurnOrder…</c>: 179 дней из 179). Сервер при старте (<c>0x42b8ab</c> → <c>0x42b8e8</c>) берёт
    /// расы сценария, сортирует их ключом <c>0x42e1fe</c> — Империя 0, Легионы 1, Кланы 2, Орды 3, Эльфы 4, нейтралы 5, прочие
    /// в конце — и ставит расу хозяина (человека) первой; остальные сдвигаются по кругу на <c>+0x34</c> данных сервера (в новой
    /// партии 0 — все партии корпуса), затем игроки переставляются по этому списку (<c>0x432165</c>, по одному на расу);
    /// <c>NextPlayer 0x420929</c> идёт по нему. Порядок id и порядок игроков в файле роли не играют: во второй карте
    /// probe65 (человек — Империя) ходят Легионы 000b → Кланы 000a → Орды 000c.</summary>
    public static IReadOnlyList<string> TurnOrder(MapState map, IReadOnlySet<string> humans, GameBook book) =>
        [.. map.Players.Values.Where(p => !humans.Contains(p.Id))
               .OrderBy(p => book.Economy.Races.TryGetValue(p.RaceId, out var r) ? r.TurnOrder : int.MaxValue)
               .ThenBy(p => p.Id, StringComparer.Ordinal).Select(p => p.Id)];

    /// <summary>Начало хода игрока ИИ и его стратегия — всё, что движок делает до тактики: земля, доход, читы,
    /// регенерация (<see cref="BeginTurn"/>), затем <see cref="AiTurn"/>.</summary>
    /// <param name="hireLeaders">Список 0 (найм лидеров) — выключается харнессами, которые берут наймы из журнала.</param>
    /// <param name="growLand">Рост своей земли — выключается харнессами, которые берут землю из журнала (<c>terrain_day</c>).</param>
    /// <param name="hireUnits">Список 2 (найм юнитов в гарнизоны) — выключается харнессами, которые берут наймы из журнала.</param>
    /// <param name="spies">Раскрытие шпионов (<see cref="Spies"/>, броски R250) — выключается харнессами, которые берут
    /// разведанных игроков из сохранений.</param>
    /// <param name="journalHires">Харнессы с наймом лидеров из журнала (<paramref name="hireLeaders"/> = false): найм этого хода —
    /// после стратегии, до оперативного слоя. У движка найм идёт в стратегии (список 0), и новый отряд-гость закрывает
    /// крепость для защиты <c>0x46e37c</c> (визитёр <c>+0x8c</c> не пуст), а в списки сил (они собраны раньше) не входит.</param>
    /// <param name="journalLeaders">Наймы лидеров из журнала как шаги стратегии (<paramref name="hireLeaders"/> = false): список 0
    /// даёт их по одному в порядке журнала, исполнение — <see cref="JournalLeaderHire.Apply"/>; память дня и бюджет — как у найма
    /// стратегией. Без этого стратегия харнесса тратила казну найма на постройки и рост (legions, день 54: у движка два найма лидера
    /// и ничего больше, у порта — постройка, рост деревни и найм юнита в выросший гарнизон).</param>
    /// <param name="purseIncome">Доход и чит ресурсов в казну — выключается харнессами, у которых казна дня уже взята из журнала
    /// (строки <c>bank</c> движок пишет до <c>ai_turn begin</c>): иначе стратегия нанимает и строит на двойной доход. Второй кошелёк
    /// (половина маны) пополняется и так.</param>
    /// <param name="strategyCrt">Харнессы живого дня: поток CRT движка перед стратегией (перед первым выбором юнита найма) — ставится после
    /// начала хода, чтобы броски варианта грунта роста земли порта (<see cref="Recolor"/>) его не сдвигали.</param>
    public void BeginAiTurn(string playerId, List<string>? report = null, bool hireLeaders = true, bool growLand = true,
                            bool hireUnits = true, bool spies = true, Func<GameState, GameState>? journalHires = null,
                            bool purseIncome = true, IReadOnlyList<JournalLeaderHire>? journalLeaders = null, uint? strategyCrt = null)
    {
        report ??= [];
        LastOperationalDraws = 0;
        var map = BeginTurn(Map, playerId, grow: growLand && Map.Turn > 1, ai: true, report, spies, purseIncome);
        State = State with { Looks = Retold(Looks, map, [.. Looks.Stacks.Where(s => map.Stacks.ContainsKey(s.Id))]) };
        RunEvents(playerId, "", report);   // события начала хода (0x41f4ca)
        if (strategyCrt is { } crt) Crt.Reset(crt);
        if (Over) return;
        // Списки сил — в начале хода, до стратегии, у всех игроков ИИ (0x4719f8 → 0x4760b0): их берут оперативный слой и
        // тактика (CAiTactic +0x51f8/+0x51fc).
        (_forces, _forcesOf) = (AiSpells.Forces(this, playerId), playerId);
        (_hireLeaders, _hireUnits, _journalHires) = (hireLeaders, hireUnits, journalHires);
        _journalLeaders = journalLeaders is null ? null : [.. journalLeaders];
        try { AiTurn(Map, playerId, report); }
        finally { (_hireLeaders, _hireUnits, _journalHires, _journalLeaders) = (true, true, null, null); }
    }

    /// <summary>Трасса ИИ (<see cref="IAiTrace"/>): выборы тактики, шаги стратегии, длительность хода. null — нет трассы и затрат
    /// (по умолчанию; харнессы и тесты её не ставят), ставит зонд порта.</summary>
    public IAiTrace? AiTrace { get; set; }

    /// <summary>Шагов стратегии и выборов тактики в текущем ходе ИИ — для <see cref="IAiTrace.TurnEnded"/>.</summary>
    private int _aiSteps, _aiPicks;

    /// <summary>Выбор тактики отряда (<see cref="AiTactics.Choose"/>) — в трассу.</summary>
    internal void TracePick(string playerId, AiScored<AiTacticOption> pick, int candidates)
    {
        if (AiTrace is not { } trace) return;
        _aiPicks++;
        // Атака с заклинанием — своё действие (vtable 0x6d0d2c): цель и точка (слоты 3–4, 0x45842c/0x45844e) — литья, когда атака готова
        // (готовность AiTactics.AttackReady в миг выбора), иначе — атаки. Карта 24 (20261020-replay-q09): день 19 S143KC0002 → S143KC005e с
        // призывом рядом — (30,84), клетка призванного; день 24 S143KC00a4 → S143KC0062 издалека — (35,66), сама цель (статус 5, литья нет).
        var action = pick.Action is { Prep: { Kind: AiPrepKind.Spell } p, Ready: true }
            ? pick.Action with { Target = p.CastTarget, X = p.X, Y = p.Y } : pick.Action;
        trace.Picked(playerId, Map.Turn, action, pick.Score, candidates);
    }

    private bool _hireLeaders = true, _hireUnits = true;
    private Func<GameState, GameState>? _journalHires;

    /// <summary>Наймы лидеров журнала, ещё не исполненные стратегией (<see cref="BeginAiTurn"/>, <c>journalLeaders</c>).</summary>
    private List<JournalLeaderHire>? _journalLeaders;

    /// <summary>Наймы лидеров журнала для списка 0 (оценка — первый из очереди, id оценки — крепость, как у <c>0x44ac50</c>: память дня
    /// пускает один найм на крепость в день, как у движка); бюджет не проверяется — движок этот найм уже сделал. null — наймов из журнала
    /// нет (или стратегия нанимает сама).</summary>
    internal IReadOnlyList<JournalLeaderHire>? JournalLeaders => _hireLeaders ? null : _journalLeaders;

    /// <summary>Исполнение найма лидера журнала (<see cref="JournalLeaders"/>): снимается из очереди при любом исходе (иначе неудача
    /// повторялась бы до предела проходов); успех — новое состояние от <see cref="JournalLeaderHire.Apply"/>.</summary>
    internal bool ExecuteJournalLeader(AiStepAction action)
    {
        if (_journalLeaders is not { Count: > 0 } queue || !queue[0].Fort.Equals(action.Id, StringComparison.OrdinalIgnoreCase)) return false;
        var hire = queue[0];
        queue.RemoveAt(0);
        if (hire.Apply(State) is not { } next) return false;
        State = next;
        return true;
    }

    /// <summary>Строить ли список 2 в стратегии (<see cref="BeginAiTurn"/>).</summary>
    internal bool HireUnitsEnabled => _hireUnits;

    /// <summary>Строить ли список 0 в стратегии (<see cref="BeginAiTurn"/>).</summary>
    internal bool HireLeadersEnabled => _hireLeaders;

    /// <summary>Ход игрока ИИ (<c>docs/ai-turn-plan.md</c>): бюджет дня (<see cref="AiBudget.StartOf"/>), стратегия
    /// <see cref="AiStrategic.RunTurn"/> на списках из карты (<see cref="AiEconomy"/>; перенесена постройка в столице),
    /// память трат и дня — в <see cref="GameState.Ai"/>. Тактику (<see cref="TacticTurn"/>) зовёт <see cref="EndTurn"/>
    /// после этого. Нейтралы стратегию не ведут
    /// (<c>0x5e4e73</c>).</summary>
    private MapState AiTurn(MapState map, string playerId, List<string> report)
    {
        var book = Book!;
        var player = map.Players[playerId];
        if (!(book.Economy.Races.TryGetValue(player.RaceId, out var race) && race.Playable) || player.CapturedBy.Length > 0)
            return map;
        State = State with { Looks = Retold(Looks, map, Looks.Stacks) };
        // Слот 2 CAiStrategic (0x44e26e): в день 1 — раздача стратегических приказов, затем бюджет дня (ниже).
        StrategicOrders(playerId, report);
        // Начало стратегии (слот 4 CAiStrategic 0x44e738 → 0x44edee): гарнизоны своих укреплений без отряда-гостя
        // расставляются с центровкой (StackReorder 0x41ef06 → 0x5d57de).
        foreach (var fort in Map.Sites.Where(s => s.Kind is SiteKind.Capital or SiteKind.Village && s.StackId.Length == 0
                                                  && s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)).ToList())
            AiStackUnits.Arrange(this, fort.Id, center: true);
        map = Map;
        var memory = State.Ai.GetValueOrDefault(playerId) ?? AiMemory.Empty;
        int day = map.Turn;
        var last = AiStrategic.ListOrder.ToDictionary(t => t, memory.LastSpendOf);
        var rules = AiEconomy.BudgetRules(this, playerId);
        // Резерв исследования (0x44cd3e): известных меньше половины предела — бюджет типа 6 от казны плюс казны магии.
        bool reserve = AiStrategicRules.ResearchReserve(player.KnownSpells.Count, (LordCategory)book.LordCategory(player.LordId));
        var budget = new AiBudget(
            AiStrategic.ListOrder.ToDictionary(t => t, t => AiBudget.StartOf(t, day, last[t], player.Bank, player.SpellBank,
                                                                                rules, reserve: reserve && t == AiStepType.SpellResearch)),
            player.Bank)
        { SpellStart = player.SpellBank, Reserved = reserve ? new HashSet<AiStepType> { AiStepType.SpellResearch } : new HashSet<AiStepType>() };
        IReadOnlyDictionary<(string Id, AiStepType Type), int> memo = memory.MemoKeys();
        try
        {
            var failed = new HashSet<AiStepAction>();
            var result = AiStrategic.RunTurn(day, (type, b) => AiEconomy.Lists(this, playerId, type, b),
                                             a =>
                                             {
                                                 bool ok = AiEconomy.Execute(this, playerId, a, report);
                                                 if (!ok && failed.Add(a)) report.Add($"{playerId} не исполнено {a.Type} {a.Id} {a.Target}");
                                                 return ok;
                                             },
                                             () => Map.Players[playerId].Bank, budget, memo, maxPasses: 100);
            foreach (var step in result.Steps.Where(s => s.Ok))
            {
                if (AiTrace is { } trace) trace.Stepped(playerId, day, step, ++_aiSteps);
                last[step.Action.Type] = day;
                // У дипломатии Target — вид действия, цель — Id; у найма лидера Target — тип|приказ, у найма юнита — тип, Id — укрепление.
                report.Add(step.Action.Type is AiStepType.Diplomacy or AiStepType.HireLeader or AiStepType.HireUnit
                    ? $"{playerId} {step.Action.Type} {step.Action.Target} {step.Action.Id}"
                    : $"{playerId} {step.Action.Type} {step.Action.Target}");
            }
            memo = result.Memo;
        }
        catch (InvalidOperationException e)
        {
            // Неудачное исполнение повторяется — у движка вечный цикл; здесь ход ИИ кончается с записью.
            report.Add($"{playerId} стратегия прервана: {e.Message}");
        }
        var all = State.Ai.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        all[playerId] = AiMemory.From(last, memo);
        State = State with { Ai = all };
        if (_journalHires is { } hires) State = hires(State);
        OperationalBefore(playerId, _forces!, report);
        return Map;
    }

    /// <summary>
    /// Шпионы в начале хода игрока (<c>0x44430f</c>, после снятия литья в той же серверной обработке <c>0x443ca2</c>): по
    /// игрокам карты, кроме самого: уже разведанный (<see cref="MapPlayer.Spied"/>) и не союзник (<c>0x5f78fc</c>), чья столица
    /// не взята, — бросок R250(100), и при значении меньше <c>SPY_DISCOV</c> (10) запись снимается (<c>CEffectSpy</c> с
    /// флагом 0); не разведанный союзник — разведанными становятся оба (флаг 1, в обе стороны). Ранг 1 по листингу; журналы:
    /// в окне начала хода игрока ровно по броску на запись (legions, дни 17–21 — у <c>PL000a</c> одна).
    /// </summary>
    private MapState Spies(MapState map, string playerId)
    {
        var book = Book!;
        var me = map.Players[playerId];
        int? Party(MapPlayer p) => Diplomacy.PartyOf(p, book.Economy.Races);
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (var other in map.Players.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (Same(other.Id, playerId)) continue;
            bool allied = Party(me) is { } a && Party(other) is { } b && a != b && Diplomacy.AreAllies(map.Relations, a, b);
            bool spied = players[playerId].Spied.Contains(other.Id, StringComparer.OrdinalIgnoreCase);
            if (spied)
            {
                if (allied || other.CapturedBy.Length > 0) continue;
                if (Rng.Next(Percent) >= book.SpyDiscovery) continue;
                players[playerId] = players[playerId] with { Spied = [.. players[playerId].Spied.Where(x => !Same(x, other.Id))] };
                changed = true;
                // CCmdDiscoverSpyMsg всем (0x43e8c0): разведанному — «обнаружен шпион», хозяину — «голова шпиона» (клиент 0x489f16).
                Observer?.Happened(new TurnSight(TurnSightKind.SpyCaught, other.Id, -1, -1, playerId));
                Observer?.Happened(new TurnSight(TurnSightKind.SpyLost, playerId, -1, -1, other.Id));
            }
            else if (allied)
            {
                players[playerId] = players[playerId] with { Spied = [.. players[playerId].Spied, other.Id] };
                if (!players[other.Id].Spied.Contains(playerId, StringComparer.OrdinalIgnoreCase))
                    players[other.Id] = players[other.Id] with { Spied = [.. players[other.Id].Spied, playerId] };
                changed = true;
            }
        }
        return changed ? With(map, players: players) : map;
    }

    private const int Percent = 100;

    /// <summary>Показ растекания (<c>CMidAnim2Terrain</c>, <c>OCCUPY</c> — сессия Г): клетки, сменившие землю, и они же по источникам в порядке
    /// обхода; точка показа — место единственного источника (крепость — её позиция, жезл — его клетка), при нескольких — столица игрока
    /// (<c>0x43ed42</c>, <c>0x5e9450</c>; ранг 1 по листингу, 28.09).</summary>
    private static TurnSight LandSpread(MapState before, MapState after, string playerId, IReadOnlySet<(int X, int Y)> land, LandModel model)
    {
        bool Changed((int X, int Y) c) => before.Tiles[c.X, c.Y].Terrain != after.Tiles[c.X, c.Y].Terrain;
        var changed = land.Where(Changed).OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
        var walks = model.Walks(2).Select(w => (w.Source, Cells: (IReadOnlyList<(int X, int Y)>)[.. w.Cells.Where(Changed)]))
                                  .Where(w => w.Cells.Count > 0).ToList();
        (int X, int Y) at = walks.Count == 1
            ? walks[0].Source.IsRod ? walks[0].Source.Centre
                : (walks[0].Source.Centre.X - LandModel.FortCentreOffset, walks[0].Source.Centre.Y - LandModel.FortCentreOffset)
            : before.Sites.FirstOrDefault(s => s.Kind == SiteKind.Capital && s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase))
                is { } capital ? (capital.X, capital.Y) : changed[0];
        return new TurnSight(TurnSightKind.LandSpread, playerId, at.X, at.Y, "") { Cells = changed, Groups = [.. walks.Select(w => w.Cells)] };
    }

    /// <summary>Начало хода игрока: отрядам возвращается запас хода, юниты регенерируют (<see cref="RegenTerms"/>), своя земля растёт
    /// (<paramref name="grow"/>; в день 1 — нет), приходит доход по земле дня. У ИИ доход делится (<see cref="Treasury.AiIncome"/>:
    /// половина маны во второй кошелёк), затем читы — ресурсов (<see cref="Treasury.AiResourceCheat"/>) и опыта
    /// (<see cref="AiXpCheat"/>). Порядок строк журнала: <c>regen</c> (укрепления) → <c>ap_restore</c> → <c>regen</c> (юниты) → <c>land_grow</c> → <c>income</c> ×2 → <c>xp</c>.
    /// <paramref name="purseIncome"/> = false — казна ИИ не меняется (харнесс уже поставил её со строки <c>bank</c>), второй кошелёк — как обычно.</summary>
    private MapState BeginTurn(MapState map, string playerId, bool grow, bool ai, List<string> report, bool spies = true,
                               bool purseIncome = true)
    {
        var book = Book!;
        var player = map.Players[playerId];
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)
                                                 ? Copy(p.Value, p.Value.X, p.Value.Y, Allowance(map, p.Value)) : p.Value,
                                             StringComparer.OrdinalIgnoreCase);
        // Записи литья игрока снимаются в начале его хода (CEffectRemoveSpell 0x443d01: игрок и ход ≥ TURN_STOP) — в обоих списках
        // CMidSpellCast: 0x607099 зовёт фильтр 0x6070c4 по спискам +0x8 и +0x18 (заклинания и предметы). Ранг 1 по листингу; в сохранениях корпуса
        // устаревших записей нет ни в одном списке (map_effect_turn_stop).
        bool Kept(CastRecord c) => !c.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase) || LastEdict.Rules.Magic.SpellCast.Live(c.TurnStop, map.Turn);
        var casts = map.SpellCasts.Where(Kept).ToList();
        var itemCasts = map.ItemCasts.Where(Kept).ToList();
        map = With(map, stacks: stacks, spellCasts: casts.Count == map.SpellCasts.Count ? null : casts,
                   itemCasts: itemCasts.Count == map.ItemCasts.Count ? null : itemCasts);
        map = ExpireEffects(map, playerId);
        if (spies) map = Spies(map, playerId);
        // Конец мятежей: бунт снимается до лечения (деревня в этот день уже лечит), удар — после роста земли (BeginRiotEnds).
        var riots = BeginRiotEnds(map, playerId);
        map = riots.Map;
        // Лечение — до роста земли, дохода и чита опыта (probe72: regen fort → ap_restore → regen unit → land_grow → income → xp, seq
        // 823640…824015): «своя земля» отряда — по земле до дневного роста.
        map = Regenerate(map, playerId);
        map = ExpireSummons(map, playerId);   // 0x444631 — после эффекта регенерации, до роста земли
        bool playable = book.Economy.Races.TryGetValue(player.RaceId, out var race) && race.Playable;
        IReadOnlySet<(int X, int Y)>? land = null;
        if (grow && playable && player.CapturedBy.Length == 0)
        {
            try
            {
                // День 1 модели — нынешнее пятно, день 2 — после одного дневного роста (LandModel.Advance).
                var model = LandModel.Build(map, playerId, book.Economy, book.Land, book.MountainLandmarks);
                land = model.Certain(2);
                var before = map;
                map = Recolor(map, land, race!.Terrain);
                if (!ReferenceEquals(before, map) && Observer is not null) Observer.Happened(LandSpread(before, map, playerId, land, model));
            }
            catch (ArgumentException)
            {
                land = null;   // своей земли на карте нет — расти неоткуда, доход по клеткам карты
            }
        }
        // Удар конца мятежа — после лечения и роста земли, перед доходом (ai-q11 13.10: hp_change 1764913/1764915 после regen и land_grow,
        // перед income, 7/7; 20261016b R1: 20 % от HP до лечения).
        map = FinishRiotEnds(map, riots.Ending);
        var difficulty = (Difficulty)State.Info.Difficulty;
        var income = Economy.DailyIncome(map, playerId, book.Economy, difficulty, land);
        if (!ai) NoteIncome(playerId, income);   // окно начала хода человека — с этим доходом (CEffectPlayerTurn 0x43ab41 → CCmdTurnInfoMsg)
        if (income != Bank.Zero)
        {
            player = map.Players[playerId];
            var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            if (ai)
            {
                var (purse, spells) = Treasury.AiIncome(player.Bank, player.SpellBank, income);
                // Чит ресурсов: второй расчёт дохода (0x4734ef), казна − доход + ⌊доход·D_*_M/100⌋. Казна уже со строки bank журнала — не трогается.
                purse = purseIncome ? Treasury.AiResourceCheat(purse, Economy.DailyIncome(map, playerId, book.Economy, difficulty, land),
                                                               book.AiCheats.Resource(difficulty)) : player.Bank;
                players[playerId] = player with { Bank = purse, SpellBank = spells };
            }
            else players[playerId] = player with { Bank = Treasury.Apply(player.Bank, income) };
            map = With(map, players: players);
            report.Add($"{playerId} доход {income.Gold}з");
        }
        return ai ? AiXpCheat(map, playerId) : map;
    }

    /// <summary>Чит опыта ИИ (<see cref="Experience.AiDailyXp"/>, <c>0x473439</c>): юниты отрядов игрока получают
    /// <c>⌊XP_NEXT·p/100⌋</c>, гарнизоны его укреплений — с <c>2p</c>; <c>p</c> — <c>GAI.D_*_XP</c> по сложности.
    /// Группа берётся, только если все клетки 3×3 вокруг неё скрыты у каждого человека (<c>0x47387a</c>, клетки вне карты
    /// пропускаются); клетка группы (ранг 1, 28.09) — у отряда его позиция для ИИ (<c>0x5ed9b2</c>: в крепости — её вход), у
    /// укрепления — вход (<c>0x4737b1</c> ← <c>0x5e94eb</c>: <c>pos + size − 1</c>). Нейтралы пропускаются при <c>SUGG_LVL ≥ 10</c>,
    /// выбывший игрок — всегда.
    /// Повышение — как дневное (<see cref="UnitUpgrade.ApplyDaily"/>), с полным здоровьем.</summary>
    private MapState AiXpCheat(MapState map, string playerId)
    {
        var book = Book!;
        var player = map.Players[playerId];
        int p = book.AiCheats.UnitXp((Difficulty)State.Info.Difficulty);
        bool neutral = !(book.Economy.Races.TryGetValue(player.RaceId, out var race) && race.Playable);
        if (p <= 0 || player.CapturedBy.Length > 0 || (neutral && State.Info.SuggestedLevel >= Experience.AiXpBonusMinSuggestedLevel))
            return map;
        var fogs = State.Info.Humans.Select(h => map.Players.TryGetValue(h, out var hp) ? map.Fog.GetValueOrDefault(hp.FogId) : null)
                                    .OfType<bool[,]>().ToList();
        bool Hidden(int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (map.Contains(x + dx, y + dy) && fogs.Any(f => !f[x + dx, y + dy])) return false;
            return true;
        }
        var units = map.Units.ToDictionary(u => u.Key, u => u.Value, StringComparer.OrdinalIgnoreCase);
        var built = player.Buildings.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, MapStack>? stacks = null;
        GeneratedImpls.Observe(_generated, map, book);
        int maxLeader = map.Limits?.MaxLeader ?? 0;
        void Give(IEnumerable<string> ids, bool garrison, MapStack? group)
        {
            foreach (string id in ids)
            {
                if (!units.TryGetValue(id, out var unit)) continue;
                int category = book.CategoryOf(unit.TypeId) ?? 0;
                int xpNext = book.XpNextOf(unit.TypeId, unit.Level);
                int amount = Experience.AiDailyXp(xpNext, p, garrison);
                if (amount <= 0) continue;
                // Павшему прибавки нет (0x5d7412), но проверка повышения 0x4318ad идёт и у него (UnitUpgrade.ApplyDaily, alive): повышенный
                // встаёт с полным здоровьем (Healed) — карта 23, S143UN0082 G000UU0064 после повышения hp 105.
                bool alive = unit.Hp > 0;
                MapUnit after;
                // Сбор чита (0x473931) категорию не проверяет, прибавка (0x5d7412) — тоже: страж столицы копит опыт (human: S073UN0000
                // G000UU3001 +360 в день). Повышения у него нет: не лидер — цель повышения 0x5d7fe7, а у его категории 0x5d8f84 сразу
                // «нет», поэтому у порога прибавка на 1 меньше (0x5d749e), затем addXp зажимает опыт в [0, XP_NEXT] (0x5e9a7f) — та же
                // LeaderXpAfter без цели. Ранг 1 (листинг и probe65: S143UN0002 8640 + 360 → 8999, 8999 + 360 → 9000; S143UN0006
                // 9975 + 399 → 9995 при XP_NEXT 9995).
                if (!Experience.GainsXp(category))
                {
                    if (!alive) continue;
                    after = unit with { Xp = Experience.LeaderXpAfter(unit.Xp, amount, xpNext, nextTierCreated: false) };
                }
                else if (category == Experience.LeaderCategory)
                {
                    // Тип следующего уровня уже создан в этом запуске (0x5d89c6(…, 0), GeneratedImpls) — прибавка без «−1» (И230).
                    bool created = GeneratedImpls.NextTierCreated(_generated, unit.TypeId, unit.Level, maxLeader, book);
                    var leader = UnitUpgrade.ApplyDailyLeader(new UnitProgress(unit.TypeId, unit.Level, unit.Xp, unit.DynLevel), amount,
                                                              book.XpNextOf, maxLeader, alive, created);
                    after = unit with { Level = leader.Level, Xp = leader.Xp };
                    // Каждый уровень лидера — отложенный выбор навыка (UPGCOUNT + 1, как после боя): карта 21, S143UN0008 G000UU0044 225
                    // (XP_NEXT, тренер) → уровень 2 утром дня 9, и стратегия тем же днём берёт навык (skill_pick upg_before 1, проход 2).
                    if (leader.Level > unit.Level && group is not null)
                    {
                        stacks ??= map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                        var now = stacks[group.Id];
                        stacks[group.Id] = now with { UpgradeCount = now.UpgradeCount + leader.Level - unit.Level };
                    }
                }
                else
                {
                    // Цель повышения ищется, только если юнит «у своей расы» (0x5d7fe7, UpgradeRace): хозяин группы — игрок его подрасы
                    // или раса без столицы на карте (нейтралы). Тогда без наследников — рост на месте.
                    bool raceAllows = UpgradeRace.Allows(map, State.Events.SubraceOwners, playerId, book.SubraceOf(unit.TypeId));
                    var up = UnitUpgrade.ApplyDaily(new UnitProgress(unit.TypeId, unit.Level, unit.Xp, unit.DynLevel), amount,
                                                    book.XpNextOf, book.Rules.Units, book.UpgradeBuilding, built, raceAllows, alive).Unit;
                    after = unit with { TypeId = up.TypeId, Level = up.Level, Xp = up.Xp };
                }
                units[id] = BattleAftermath.Healed(after, unit, book);
                GeneratedImpls.AddUpTo(_generated, unit, after, book);   // повышение создало реализации (0x5d8b17 → 0x5d8514)
            }
        }
        foreach (var stack in map.Stacks.Values.Where(s => s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)))
            if (AiTactics.Position(map, stack) is var at && Hidden(at.X, at.Y)) Give(stack.UnitIds, garrison: false, stack);
        foreach (var fort in map.Sites.Where(s => s.Kind is SiteKind.Capital or SiteKind.Village
                                                  && s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)))
            if (AiTactics.Entrance(fort) is var at && Hidden(at.X, at.Y)) Give(fort.Guards, garrison: true, null);
        return With(map, units: units, stacks: stacks);
    }

    /// <summary>Своя земля дня: клетки пятна перекрашиваются в террейн расы (биты 0–2 клетки).
    /// <para>У перекрашенной клетки леса движок заново бросает вариант картинки грунта на CRT <c>rand()</c> сервера: визитор грунта
    /// <c>0x5e5dbf</c> (из <c>0x5e612a</c>/<c>0x5e62eb</c>) у клетки с грунтом <c>L_FOREST</c> (<c>0x837338</c>) пересчитывает биты 19–25 слова
    /// клетки (<c>0x60ae84</c>) и, раз клетка вставлена с флагом (<c>[узел+0x18]</c> = 1), зовёт <c>0x5e5f84</c> → <c>0x548576</c>
    /// (<c>rand() % N</c>, N — число картинок <c>0x59ab8d</c>; <c>ai_rng</c> <c>ground_variant</c>, <c>0x5e6002</c>); у равнины, воды и гор
    /// биты 6+ обнуляются без броска. Порт картинку не хранит, но бросок тратит: от потока зависит тип найма в гарнизон
    /// (<c>ai_unit_pick</c>) и имя лидера. Журнал <c>20261020-replay-human</c>: у 816 из 818 обходов сервера бросков ровно по клеткам леса
    /// обхода (у двух — ещё клетка леса квадрата жилы), реестр И210. Тот же визитор у земли захвата и жезла: день 33 — захват <c>S073FT000b</c>
    /// (<c>land_grow</c> seq 2429564, 11 бросков <c>ground_variant</c> внутри <c>fort_capture</c>), жезлы — бросок за клетку жезла перед её обходом
    /// (seq 361819).</para></summary>
    private MapState Recolor(MapState map, IReadOnlySet<(int X, int Y)> land, int terrain)
    {
        Tile[,]? tiles = null;
        foreach (var (x, y) in land)
        {
            var tile = map.Tiles[x, y];
            if (tile.Terrain == terrain) continue;
            tiles ??= (Tile[,])map.Tiles.Clone();
            tiles[x, y] = new Tile((byte)((tile.Raw & ~0b111) | terrain));
            if (tile.Ground == Ground.Forest) Crt.Next();
        }
        return tiles is null ? map : WithTiles(map, tiles);
    }

    private BattleStart? _battle;

    /// <summary>Бой в ходу: состояние модели, здоровье и опыт юнитов до боя (опыт — для итога боя наблюдателю), цель, id призванных.</summary>
    private sealed record BattleStart(BattleState State, IReadOnlyDictionary<string, int> HpBefore, string Target, BattleIds Ids,
                                      IReadOnlyDictionary<string, int> XpBefore);

    /// <summary>Id призванных боя — по счётчику карты (<see cref="NewId"/>, <see cref="BattleState.NewUnitId"/>): счётчик на начало боя,
    /// выданные за бой; в карту он возвращается в <see cref="FinishBattle"/>, хотя призванные с боем уходят.</summary>
    private sealed class BattleIds(IReadOnlyDictionary<string, int> next, IReadOnlyList<string> units, string sample)
    {
        private IReadOnlyDictionary<string, int> _next = next;
        private readonly List<string> _issued = [];

        public IReadOnlyDictionary<string, int> Next => _next;
        public int Issued => _issued.Count;

        public string NewUnit()
        {
            string id = NewId(ref _next, units.Concat(_issued), sample, "UN");
            _issued.Add(id);
            return id;
        }
    }

    /// <summary>Отряд <paramref name="attackerId"/> нападает на цель — отряд, укрепление или руину
    /// (<see cref="MapBattle.DefenderOf"/>: в укреплении первым защищается визитёр, потом гарнизон). Атака списывает
    /// половину дневного запаса очков хода до боя (<see cref="Movement.AfterAttack"/>, <c>CEffectChangeAP</c> перед
    /// <c>CEffectBattle</c>), и собирается бой (<see cref="MapBattle.Build"/>). Итог — <see cref="FinishBattle"/>.
    /// <paramref name="duel"/> — дуэль вора: очки хода уже списало действие вора, в бою только два лидера
    /// (<see cref="MapBattle.Build"/>).</summary>
    /// <exception cref="InvalidOperationException">Партия без правил, бой уже идёт или у цели нет защитников (тогда
    /// это вход — <see cref="Enter"/>).</exception>
    public BattleState StartBattle(string attackerId, string targetId, bool duel = false)
    {
        if (Book is null) throw new InvalidOperationException("партия без правил боя");
        if (_battle is not null) throw new InvalidOperationException("бой уже идёт");
        // Цель — отряд, невидимый на момент команды (0x440121 → 0x440667, флаг [ebp+0x18]; вора он ведёт в бой, 0x4407c2): CEffectBattle с
        // этим флагом меняет стороны (0x433a57) — нападает невидимый, защищается пришедший; очки хода за нападение списываются пришедшему
        // (CEffectChangeAP 0x44084b), отношения — по нападающему боя (0x433b25). Журнал 20261016g-port-synth, карта 8, R10: battle_start
        // seq 876198 — attacker_group S143KC0018 (невидимый нейтрал), defender_group S143KC0019; у R10 списано 10 (ap_effect 876184).
        bool ambush = !duel && Map.Stacks.TryGetValue(targetId, out var hidden) && hidden.Inside.Length == 0 && Invisible(targetId);
        string defender = ambush ? attackerId : MapBattle.DefenderOf(Map, targetId)
                          ?? throw new InvalidOperationException($"у {targetId} нет защитников — это вход, а не бой");
        var attacker = Map.Stacks[attackerId];
        string battleAttacker = ambush ? targetId : attackerId;
        AttackRelations(Map.Stacks[battleAttacker].Owner, ambush ? attackerId : targetId);
        // Невидимые показываются (CEffectShow с 1): нападающий — у нападения на отряд, крепость (0x4406bf) и руину (0x440a53), цель-отряд —
        // в генераторе хода (0x43ff69), до CEffectBattle.
        Unhide(attackerId);
        if (Map.Stacks.ContainsKey(targetId)) Unhide(targetId);
        int left = duel ? attacker.Move : Movement.AfterAttack(DayAllowance(attacker), attacker.Move);
        var stacks = Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[attacker.Id] = Copy(attacker, attacker.X, attacker.Y, left);
        State = State with { Looks = Retold(Looks, With(Map, stacks: stacks), Looks.Stacks) };
        var battle = MapBattle.Build(Map, battleAttacker, defender, Book, State.Info.Difficulty,
                                     ambush ? Map.Stacks[battleAttacker].Move : left, duel);
        var ids = new BattleIds(Map.IdNext, [.. Map.Units.Keys], battleAttacker);
        battle.NewUnitId = ids.NewUnit;
        battle.ProcessRand = Crt;   // кого призвать/в кого превратить — rand() процесса (LifeAttacks.Pick)
        _battle = new BattleStart(battle, battle.Units.ToDictionary(u => u.Id, u => u.Hp, StringComparer.OrdinalIgnoreCase),
                                  targetId, ids, battle.Units.ToDictionary(u => u.Id, u => u.Xp, StringComparer.OrdinalIgnoreCase));
        if (Observer is { } observer)   // показ начала боя (сессия Г): ABATTLE над целью, SBATTLE
        {
            var at = Map.Stacks.TryGetValue(targetId, out var target) && target.Inside.Length == 0 ? (target.X, target.Y)
                : PathFinder.EntranceOf(Map, targetId) ?? (attacker.X, attacker.Y);
            observer.Happened(new TurnSight(TurnSightKind.BattleStarted, attacker.Owner, at.Item1, at.Item2, attackerId));
        }
        return battle;
    }

    /// <summary>Бой кончился: потери, здоровье, опыт и повышения — в карту; группы без живых сняты. Если целью было
    /// укрепление или руина и защитников у неё не осталось, победитель входит (<see cref="Enter"/>): захват или
    /// добыча. Если в укреплении остался гарнизон — нужна следующая атака (в корпусе тем же отрядом).
    /// <paramref name="log"/> — журнал ходов модели боя (по нему делится опыт); классы атак для итога зонду — из применений
    /// (<see cref="BattleState.Actions"/>, <see cref="Summary"/>).</summary>
    public VisitReport? FinishBattle(IReadOnlyList<TurnRecord> log)
    {
        EndBattle(log);
        return CommitBattle();
    }

    /// <summary>Конец боя на ещё открытом экране боя (<c>CBatLogic::updateGroupsIfBattleIsOver</c> <c>0x62953c</c>): убийства, предметы,
    /// конец боя модели (уход призванных, откат превращений), опыт и рост — заметки <see cref="BattleNoteKind.LevelUp"/> в состоянии боя и
    /// полученный опыт в его юнитах (<see cref="BattleUnit.Xp"/>) для показа, до кнопки «Закрыть» (замечание 33 прогона 11.10). Карта
    /// партии не меняется до <see cref="CommitBattle"/>.</summary>
    public void EndBattle(IReadOnlyList<TurnRecord> log)
    {
        if (_battle is not { } battle || Book is null) throw new InvalidOperationException("боя нет");
        var kills = BattleAftermath.Kills(battle.State, battle.HpBefore, log, Book);
        // Конец боя модели (превращения, призванные) — после счёта убийств: опыт за них движок начислил ещё в бою.
        // Предметы — по списку применённых (экран боя мог уже сыграть конец боя), до снятия погибших.
        var spent = MapBattle.SpendItems(Map, battle.State, Book);
        var summary = Summary(battle);   // до конца боя модели: он снимает применённые предметы и уводит призванных
        BattleSim.AfterBattle(battle.State);
        var map = BattleAftermath.Award(MapBattle.Apply(spent, battle.State), battle.State, kills, Book,
                                        State.Info.Difficulty, State.Info.SuggestedLevel, State.Events.SubraceOwners, _generated);
        if (battle.Ids.Issued > 0) map = With(map, idNext: battle.Ids.Next);   // номера призванных не возвращаются (0x5ee8bd)
        _ending = (battle, spent, map, summary);
        // Только после расчёта: сбой в нём оставляет бой, и FinishBattle/EndBattle можно позвать снова. Реализации, порождённые повышениями
        // боя, Award уже внёс в _generated: повтор после сбоя считает их созданными (у движка повтора нет).
        _battle = null;
    }

    /// <summary>Отряд с павшим лидером снимает все слоты снаряжения (<c>0x437c4e</c>); не отряд или лидер жив — ничего.</summary>
    private void UnequipFallen(string group)
    {
        if (!Map.Stacks.TryGetValue(group, out var fallen) || fallen.LeaderAlive) return;
        for (int slot = 0; slot < LastEdict.Rules.Magic.Equipment.Slots(fallen).Count; slot++) UnequipItem(group, slot);
    }

    /// <summary>Идёт бой: собран (<see cref="StartBattle"/>) и ещё не записан в карту (<see cref="CommitBattle"/>). Зонд в это время
    /// снимок дня не пишет — карта в полубое.</summary>
    public bool InBattle => _battle is not null || _ending is not null;

    /// <summary>Снимок для записи без итогов хода людей (<see cref="SummariesForSave"/> не зовётся): текущее состояние и генераторы.
    /// Для зонда порта — его сохранение дня идёт с потока хода ИИ, а итоги собирает сцена на главном.</summary>
    public GameState SnapshotWithoutSummaries() => State with { Random = new RandomStreams(Rng.Index, Rng.TableSnapshot(), Crt.State) };

    /// <summary>Бой, конец которого показан (<see cref="EndBattle"/>), но в карту ещё не записан.</summary>
    private (BattleStart Battle, MapState Spent, MapState Map, BattleSummary Summary)? _ending;

    /// <summary>Что было в бою — для зонда (<see cref="BattleOutcome"/>): классы атак ходивших юнитов по сторонам (у призванного — его
    /// атака в бою), типы применённых предметов, бежавшие с поля. Считается до конца боя модели (<see cref="BattleSim.AfterBattle"/>).
    /// Атаки по своим записи хода не дают (<see cref="BattleSim.Act"/>: усиление, дар атаки, обереги, превращение себя, снятие яда —
    /// в <see cref="BattleSim.AllyLog"/>), призыв не даёт ни записи, ни заметки: их классы берутся из заметок, из призванных юнитов (у
    /// стороны есть юнит с классом Summon; сфера — предмет, не атака) и из воскрешённых (<see cref="BattleUnit.Revived"/>, у стороны есть юнит
    /// с классом Revive). Партия 15.10 (<c>docs/synthetic-maps-port/analysis/20261015.md</c>): прежде итог терял усиление, дар атаки,
    /// призыв, обереги и превращение себя, сыгранные на экране.</summary>
    private sealed record BattleSummary(IReadOnlyList<string> AttackerClasses, IReadOnlyList<string> DefenderClasses,
                                        IReadOnlyList<string> Items, int AttackersRetreated, int DefendersRetreated,
                                        IReadOnlyList<string> RetreatedUnits);

    private static BattleSummary Summary(BattleStart battle)
    {
        var state = battle.State;
        // Классы — по всем применениям атак (BattleState.Actions), а не по записям хода: у атак по своим (усиление, дар атаки, снятие яда,
        // обереги, превращение себя), призыва и второй атаки по своим (воскрешение) записи хода нет — прежде зонд их не видел вовсе
        // (партия 15.10: ни одного редкого класса A1–A5 в battle_end).
        IReadOnlyList<string> Side(bool attacker) =>
            [.. state.Actions.Where(a => state.Unit(a.Unit) is { } u && u.Attacker == attacker).Select(a => a.Class.ToString())
                   .Distinct().Order(StringComparer.Ordinal)];
        var units = state.Units.Concat(state.Gone).ToDictionary(u => u.Id, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> items = [.. state.UsedItems
            .Select(i => units.TryGetValue(i.UnitId, out var u) && i.Index < u.BattleItems.Count ? u.BattleItems[i.Index].Id : null)
            .OfType<string>()];
        return new BattleSummary(Side(true), Side(false), items, state.Units.Count(u => u.Attacker && u.Retreated),
                                 state.Units.Count(u => !u.Attacker && u.Retreated),
                                 [.. state.Units.Where(u => u.Retreated).Select(u => u.Id).Order(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>Записать конец боя в карту (после «Закрыть»): выигранные сражения, уничтоженные отряды, могилы, добыча, вход в объект,
    /// события конца боя.</summary>
    public VisitReport? CommitBattle()
    {
        if (_ending is not { } ending) throw new InvalidOperationException("конец боя не посчитан");
        _ending = null;
        var (battle, spent, map, summary) = ending;
        var before = Map;   // карта до боя: хозяева сторон для итога (ITurnObserver.BattleEnded)
        var stacks = Looks.Stacks.Where(s => map.Stacks.ContainsKey(s.Id))
                                 .Select(s => map.Stacks[s.Id] is var m && m.LeaderAlive != s.LeaderAlive
                                             ? s with { LeaderAlive = m.LeaderAlive } : s)
                                 .ToList();
        string attacker = battle.State.AttackerGroup, defender = battle.State.DefenderGroup;
        // Победитель — сторона, что держит поле: отступивший нападающий жив, но проиграл (журнал: winner — защитник).
        bool attackerHolds = map.Stacks.ContainsKey(attacker) && !battle.State.Units.Any(u => !u.Attacker && u.OnField);
        string winner = attackerHolds ? attacker : defender;
        // Отряд-победитель: +1 выигранное сражение (NBBATTLE; корпус 20260919-human и 20260920-legions — прирост между первым и
        // последним сохранением равен числу battle_end с этим победителем у 95 отрядов из 99, у остальных журнал идёт дальше).
        if (map.Stacks.TryGetValue(winner, out var won))
        {
            var withWin = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            withWin[winner] = won with { BattlesWon = won.BattlesWon + 1 };
            map = With(map, stacks: withWin);
        }
        // Бой у руины (CBattleResultRuin 0x41b290): хозяин нападавшего отряда, не нейтрал (0x5e5358), — в посетившие руину
        // (CEffectVisitRuin 0x43f5fa → CVisitorRuinAddVisiter 0x613164), чем бы бой ни кончился; проигравший потом видит её охрану.
        if (Map.Stacks.TryGetValue(attacker, out var raider) && !IsNeutral(raider.Owner)) map = WithRuinVisitor(map, battle.Target, raider.Owner);
        // Погибшие отряды — в уничтоженные (CEffectKillStack 0x438c19): убийца — хозяин победившей группы.
        string killer = Map.Stacks.TryGetValue(winner, out var w) ? w.Owner
                        : map.Sites.FirstOrDefault(s => s.Id.Equals(winner, StringComparison.OrdinalIgnoreCase))?.Owner ?? "";
        var dead = Map.Stacks.Values.Where(s => !map.Stacks.ContainsKey(s.Id)).Select(s => new DestroyedStack(s.Id, killer, s.TemplateId)).ToList();
        if (dead.Count > 0) map = WithDestroyed(map, [.. map.Destroyed, .. dead]);
        map = Buried(map, Map.Stacks.Values.Where(s => !map.Stacks.ContainsKey(s.Id)), winner, killer);   // могилы (GameSession.Tombs.cs)
        // Вещи погибших — победившей группе боя (CBattleResultStack 0x41b6d6: отряд, гарнизон; руина — мешок), GameSession.Loot.cs.
        var loots = new List<LootResult>();
        foreach (var gone in Map.Stacks.Values.Where(s => !map.Stacks.ContainsKey(s.Id)).ToList())
        {
            var loot = Loot(map, spent.Stacks.GetValueOrDefault(gone.Id) ?? gone, winner);
            map = loot.Map;
            loots.Add(loot);
        }
        if (Observer is { } observer)   // гибель — до снятия с карты: показ анимирует отряд на месте и ставит могилу на середине
            foreach (var gone in Map.Stacks.Values.Where(s => !map.Stacks.ContainsKey(s.Id)).ToList())
            {
                if (IllusionKilled(gone, winner) is { } illusion) observer.Happened(illusion);
                observer.Happened(new TurnSight(TurnSightKind.Death, gone.Owner, gone.X, gone.Y, gone.Id,
                                                Tomb: map.Tombs.Any(t => t.X == gone.X && t.Y == gone.Y)));
            }
        State = State with
        {
            Looks = Retold(Looks, map, stacks) with
            {
                Forts = FortLooks(Looks.Forts, map), Tombs = [.. map.Tombs.Select(t => new LastEdict.Data.Map.TombLook(t.Id, t.X, t.Y))],
                Bags = [.. Looks.Bags, .. loots.Select(l => l.Bag).OfType<LastEdict.Data.Map.BagLook>()],
            },
        };
        foreach (var loot in loots) AnnounceLoot(loot);   // сообщение 44 — за гибелью отряда (0x438cc2)
        // Павший лидер — снаряжение в сумку: 0x4379c6 → 0x437c4e для нападающей и защищающейся группы — у отряда с «лидер жив» = 0
        // (+0x2c) каждый занятый слот снимается (unEquipItem 0x5e746d), предмет остаётся в отряде. Журнал 20261016g-port-synth, карта 8,
        // BI: Баньши со Сферой грома пала, сфера не применялась — item_unequip слота 2 сразу за battle_end (seq 558981), экземпляр
        // S143IM000c в сохранении t01-002 — в сумке. Прежде порт оставлял вещи надетыми на павшем.
        UnequipFallen(attacker);
        UnequipFallen(defender);
        RetreatFled(battle.State, attacker, defender);
        // Вход — только в укрепление или руину; побеждённый отряд просто снят с карты. События конца боя (EndBattle 0x41e3e2,
        // триггерер — победитель, если это отряд) — после входа: захват и добыча входят в эффекты боя.
        // Руину грабит победитель-отряд не нейтрала и с павшим лидером (CBattleResultRuin 0x41b290: 0x5e5358 → 0x41b34e → 0x44133a →
        // CEffectLootRuin); прежде вход отказывал «лидер пал», и руина оставалась целой (карта 2 порта, S143KC0005 у S143RU0000).
        VisitReport? visit = null;
        bool ruin = map.Sites.Any(s => s.Kind == SiteKind.Ruin && s.Id.Equals(battle.Target, StringComparison.OrdinalIgnoreCase));
        // Целью был отряд-визитёр укрепления, и он погиб: вход — в его укрепление (CBattleResultStack 0x41b564 → 0x4413bc → 0x4413d9:
        // после CEffectKillStack у отряда-защитника берётся INSIDE (+0x94), и при пустом гарнизоне 0x5f50fd — генератор захвата 0x440ee0).
        // Журнал 20261020-replay-human, день 33: S073KC0002 бьёт AttackStack визитёра S073KC006c нейтральной S073FT000b и берёт её
        // (fort_capture seq 2429563, отряд встаёт на (1,57)); прежде порт оставлял его на (5,61). Реестр И211.
        string entry = battle.Target;
        if (!map.Sites.Any(s => Same(s.Id, entry)) && !map.Stacks.ContainsKey(defender)
            && before.Stacks.TryGetValue(defender, out var guest) && guest.Inside.Length > 0)
            entry = guest.Inside;
        if (map.Stacks.TryGetValue(attacker, out var survivor) && map.Sites.Any(s => Same(s.Id, entry))
            && MapBattle.DefenderOf(map, entry) is null && !(ruin && IsNeutral(survivor.Owner)))
            visit = Enter(attacker, entry, events: false, battleWon: ruin);
        Observer?.BattleEnded(Outcome(battle, winner, before) with
        {
            AttackerClasses = summary.AttackerClasses, DefenderClasses = summary.DefenderClasses, ItemsUsed = summary.Items,
            AttackersRetreated = summary.AttackersRetreated, DefendersRetreated = summary.DefendersRetreated,
            RetreatedUnits = summary.RetreatedUnits,
        });
        RunEvents("", Map.Stacks.ContainsKey(winner) ? winner : "");
        return visit;
    }

    /// <summary>Восемь соседей клетки в порядке таблицы <c>0x791d50</c> (отход бежавшего защитника, <c>0x41b6ed</c>).</summary>
    private static readonly (int X, int Y)[] RetreatRing = [(-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0)];

    /// <summary>Защитник, бежавший с поля (живые юниты отряда ушли, на поле никого), после боя отходит на клетку за собой — прочь от
    /// нападающего: <c>2·защитник − нападающий</c>, очки хода прежние (<c>stack_move</c> сразу за <c>battle_end</c>). Ранг 2 — журналы:
    /// 23 бегства из 23 (human 3, legions 8, карта 5 — 2, карты 19–22 — 4, покрытие 3, карта 23 — 2: например, карта 22 день 8
    /// <c>S143KC0013</c> (81,117) от <c>S143KC0003</c> с (82,117) → (80,117)). Так же — отряд, проигравший дуэль вора: лидер пал, прочие
    /// скрыты (<see cref="BattleUnit.Hidden"/>) и живы — карта 23, день 55: <c>S143KC005c</c> с (128,125) побеждает <c>S143KC0002</c>, и тот
    /// встаёт с (129,125) на (130,125) с прежним запасом 11 (seq 9134881; реестр И153). Очки хода не обнуляются: у бегств карты 23 запас и до
    /// боя был 0 (<c>S143KC0033</c> seq 3866607, <c>S143KC0021</c> — жезл, seq 4022716), прежде порт ставил 0 всем. Клетка вне карты, непроходима или занята отрядом или
    /// объектом (кроме мешка) — следующая по кругу (ниже). Туман хозяина открывается от новой клетки; показ отхода (анимация) не сделан.
    /// Реестр И93.
    /// <para>Порядок клеток — листинг (<c>CBattleResultStack</c> <c>0x41b564</c> → <c>0x41b6ed</c>): направление — знак (защитник −
    /// нападающий) по осям, его номер k в таблице восьми соседей <c>0x791d50</c> (нет — 0); затем i = 0…7 — номер k + i/2, у нечётного i
    /// ещё − i (k, k−1, k+1, k−2, k+2, k−3, k+3, k−4), остаток от деления на 8 со знаком (<c>idiv</c>): у отрицательного номера движок читает
    /// слова перед таблицей (строки, клетка далеко за картой) — такая клетка не годится; первая клетка, куда отряд встаёт
    /// (<c>0x5e709b</c>), — его, ни одной — остаётся. Журнал <c>20261020-replay-human</c>, день 34: <c>S073KC008c</c> на (5,45) бежит от
    /// <c>S073KC0011</c> с (4,45) — (6,45) не годится, движок ставит на (6,44) (k − 1; seq 2735297), прежде порт оставлял его на месте (И212).</para></summary>
    private static bool IsRoad(string id) => id.Length >= 6 && id.AsSpan(4, 2).Equals("RA", StringComparison.OrdinalIgnoreCase);

    private void RetreatFled(BattleState state, string attackerId, string defenderId)
    {
        var map = Map;
        if (!map.Stacks.TryGetValue(defenderId, out var fled) || fled.Inside.Length > 0 || !map.Stacks.TryGetValue(attackerId, out var by)) return;
        // Поле за нападающим, а отряд защитника жив: бежали (Retreated) или — в дуэли вора — пал лидер, а прочие скрыты (Hidden).
        if (state.Units.Any(u => !u.Attacker && u.OnField)
            || !state.Units.Any(u => !u.Attacker && u.Alive && (u.Retreated || u.Hidden))) return;
        var options = MoveOptionsOf(fled);
        bool Fits((int X, int Y) c) =>
            map.Contains(c.X, c.Y) && Movement.Passable(map, c.X, c.Y, options)
            && !(map.Plan.TryGetValue(c, out var ids) && ids.Any(id => !IsRoad(id) && !map.Sites.Any(x => x.Kind == SiteKind.Bag && Same(x.Id, id))));
        // Дорога (RA) клетку не занимает: replay-legions, день 9 — S073KC0073 с (17,37) от S073KC0014 бежит на дорогу (17,38) (stack_move
        // seq 330620); прежде порт оставлял его на месте (И190).
        var dir = (Math.Sign(fled.X - by.X), Math.Sign(fled.Y - by.Y));
        int k = Math.Max(0, Array.IndexOf(RetreatRing, dir));
        (int X, int Y)? found = null;
        for (int i = 0; i < RetreatRing.Length && found is null; i++)
        {
            int j = (k + i / 2 - (i % 2 == 1 ? i : 0)) % RetreatRing.Length;
            if (j < 0) continue;
            var c = (fled.X + RetreatRing[j].X, fled.Y + RetreatRing[j].Y);
            if (Fits(c)) found = c;
        }
        if (found is not { } at) return;
        Relocate(fled.Id, at, PathFinder.Facing(new PathPoint(fled.X, fled.Y, 0, 0), new PathPoint(at.X, at.Y, 0, 0)));
        // Туман хозяина — от новой клетки, как у хода (MoveByEvent).
        var moved = Map;
        if (moved.Players.TryGetValue(fled.Owner, out var owner) && !IsNeutral(fled.Owner) && moved.Fog.TryGetValue(owner.FogId, out var fog))
            LastEdict.Rules.Map.Fog.Reveal(fog, at.X, at.Y, ScoutRange(moved, moved.Stacks[fled.Id]), LastEdict.Rules.Map.Fog.DefaultShape);
    }

    /// <summary>Сообщение о гибели иллюзии (<c>CEffectKillStack</c> <c>0x43895c</c> → <c>CCmdStackIllusionMsg</c> вместо
    /// <c>CCmdStackDestroyedMsg</c>): лидер погибшего отряда — иллюзия (<c>IUsIllusion</c>, <c>0x438ff3</c>), убийца — отряд (порт: победитель
    /// боя — отряд; гарнизон и охрана — ранг 2) не нейтрала (<c>0x5e5358</c>). Имена — по карте до итога (<c>0x4ce918</c> → <c>0x5e97dc</c>).
    /// Иначе null.</summary>
    private TurnSight? IllusionKilled(MapStack gone, string winner)
    {
        if (!IsIllusion(gone.LeaderId) || !Map.Stacks.TryGetValue(winner, out var killer) || IsNeutral(killer.Owner)) return null;
        string[] Leader(MapStack s) => Map.Units.TryGetValue(s.LeaderId, out var u) ? [OwnName(u.Id, u.TypeId) ?? "", u.TypeId] : ["", ""];
        return new TurnSight(TurnSightKind.IllusionKilled, gone.Owner, gone.X, gone.Y, gone.Id) { Ids = [.. Leader(killer), .. Leader(gone)] };
    }

    /// <summary>Итог боя наблюдателю (<see cref="ITurnObserver.BattleEnded"/>): хозяева сторон — по карте до боя
    /// (<paramref name="before"/>), павшие — не призванные юниты без здоровья, живые к началу боя, опыт — прирост к опыту начала боя (<see cref="BattleAftermath.Award"/>
    /// кладёт полученный опыт в юниты боя).</summary>
    private static BattleOutcome Outcome(BattleStart battle, string winner, MapState before)
    {
        var state = battle.State;
        string OwnerOf(string group) => before.Stacks.TryGetValue(group, out var s) ? s.Owner
            : before.Sites.FirstOrDefault(x => x.Id.Equals(group, StringComparison.OrdinalIgnoreCase))?.Owner ?? "";
        // Павший до боя (лежит в отряде костями, MapBattle.Build) павшим в этом бою не считается.
        int Fallen(bool attacker) => state.Units.Count(u => u.Attacker == attacker && !u.Summoned && u.Hp <= 0
                                                            && battle.HpBefore.GetValueOrDefault(u.Id) > 0);
        int Xp(bool attacker) => state.Units.Where(u => u.Attacker == attacker)
                                            .Sum(u => Math.Max(0, u.Xp - battle.XpBefore.GetValueOrDefault(u.Id, u.Xp)));
        return new BattleOutcome(state.AttackerGroup, OwnerOf(state.AttackerGroup), state.DefenderGroup, OwnerOf(state.DefenderGroup),
                                 winner, state.Round, Fallen(true), Fallen(false), Xp(true), Xp(false), before.Turn);
    }

    /// <summary>Отказ входа в свою бунтующую деревню (<see cref="Enter"/>; окно — <c>X005TA0689</c>).</summary>
    public const string RiotRefusal = "город бунтует";

    /// <summary>Отказ захвата деревни отрядом с лидером-призванным или вором (<see cref="Enter"/>; окно — <c>X005TA0458</c>).</summary>
    public const string CaptureRefusal = "призванные и воры не захватывают города";

    /// <summary>Лидер отряда захватывает деревню (<see cref="Fortifications.CanCapture"/>): не призванный (<c>IUsSummon</c>) и не вор
    /// (<c>IUsNoble</c>; у движка — реализация юнита, у порта — <c>UNIT_CAT</c> и <c>LEADER_CAT</c> типа).</summary>
    private bool LeaderCaptures(MapState map, MapStack stack)
    {
        if (Book is not { } book || !map.Units.TryGetValue(stack.LeaderId, out var leader)) return true;
        int category = book.CategoryOf(leader.TypeId) ?? -1;
        return category != UnitCategory.Noble && Fortifications.CanCapture(category, book.LeaderCategoryOf(leader.TypeId));
    }

    /// <summary>Отряд входит в объект без боя: свой город (если в нём нет визитёра), чужой пустой город — захват
    /// (<see cref="Sites.Visit"/>: деревня меняет владельца, <see cref="Fortifications.Capture"/>; столица — гибель её
    /// игрока, <see cref="CapitalCapture.Apply"/>), неохраняемая руина — добыча (казна владельцу, предмет в отряд).
    /// Отряд в город встаёт на его клетку и становится визитёром; у руины остаётся, где стоял.</summary>
    /// <param name="events">Проверить события после входа (вход отрядом с карты); ложь — вход завершает бой, и события
    /// проверит конец боя.</param>
    /// <param name="battleWon">Вход победителя боя у руины (<see cref="CommitBattle"/>): грабит и с павшим лидером
    /// (<see cref="Sites.Visit"/>).</param>
    public VisitReport Enter(string stackId, string siteId, bool events = true, bool battleWon = false)
    {
        var map = Map;
        var stack = map.Stacks[stackId];
        var site = map.Sites.First(s => s.Id.Equals(siteId, StringComparison.OrdinalIgnoreCase));
        if (Book is null) return new VisitReport(siteId, "партия без правил");
        // Невидимый показывается до самого входа (CEffectShow с 1): своя крепость 0x44099e, пустая чужая 0x4406bf, руина 0x440a53,
        // место 0x440b59, мешок 0x440c0b — и когда вход потом откажет (визитёр, бунт).
        Unhide(stackId);
        bool fort = site.Kind is SiteKind.Capital or SiteKind.Village;
        if (fort && site.Owner.Equals(stack.Owner, StringComparison.OrdinalIgnoreCase))
        {
            if (site.StackId.Length > 0 && !site.StackId.Equals(stack.Id, StringComparison.OrdinalIgnoreCase))
                return new VisitReport(siteId, $"в {siteId} уже стоит отряд {site.StackId}");
            // Свою бунтующую деревню движок не открывает и отряд внутрь не пускает (прогон 13.10, ai-q22: «Пока город бунтует…»
            // X005TA0689, 7 раз).
            if (site.Kind == SiteKind.Village && Fortifications.Rioting(site.RiotTurn))
                return new VisitReport(siteId, RiotRefusal);
            var check = MoveCheckOf(stack);
            State = State with { Looks = Inside(Looks, map, stack, site) };
            // Вход в свой город — точка проверки событий хода (StackMove 0x41e46b): L_ENTER_CITY, L_LEADER_TO_CITY срабатывают сразу.
            if (events) RunMoveEvents(stack.Id, check);
            return new VisitReport(siteId, null, Entered: true);
        }
        var result = Sites.Visit(map, stack, site, Book.Economy, battleWon);
        if (result.Blocked is not null) return new VisitReport(siteId, result.Blocked);
        if (site.Kind == SiteKind.Village && result.Captured && !LeaderCaptures(map, stack))
        {
            // CEffectCapture 0x433e2e: флаг +0x14 = 0 (ход, бой за деревню, убийство последнего стража; 1 — только подкуп, 0x41dd00) и лидер
            // IUsSummon (0x431d8c) или IUsNoble (0x41b399) — CCmdOccupyCityMsg с флагом 0 (0x433f2c), деревня остаётся прежнему хозяину;
            // CEffectEnterCity (0x437d1f) при чужом хозяине ничего не делает — отряд стоит, где стоял. Клиент хозяина отряда
            // (0x48b415 → 0x4ced8e) показывает X005TA0458 «Призванные существа и воры не могут захватывать города.». Столица — другой эффект
            // (CEffectCaptureCapital 0x43496a, ветка генератора 0x440f2b): лидера он не смотрит, проверки нет и у порта.
            Observer?.Happened(new TurnSight(TurnSightKind.CaptureRefused, stack.Owner, site.X, site.Y, site.Id));
            return new VisitReport(siteId, CaptureRefusal);
        }
        var continued = MoveCheckOf(stack);   // вход сразу после шага — та же проверка хода, что и шаг
        var after = Sites.AfterVisit(site, stack, result);
        var sites = map.Sites.Select(s => ReferenceEquals(s, site) ? after : s).ToList();
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var looks = Looks;
        bool victory = false;
        Bank? captured = null;   // казна погибшего до захвата — окно взятых ресурсов (0x434d61)
        if (site.Kind == SiteKind.Capital && result.Captured)
        {
            // «Горячий стул» — многопользовательская партия (CMidgardData+0x30): взятие столицы человека партию не кончает, если у
            // захватчика есть другие живые соперники (0x440ee0; docs/day-cycle.md §13).
            var outcome = CapitalCapture.Apply(map, site, stack.Owner, Book.Economy, State.Events, multiplayer: Hotseat);
            if (outcome.Refused is { } why) return new VisitReport(siteId, why);
            victory = outcome.Victory;
            if (!victory)
            {
                var loser = players[site.Owner];
                captured = loser.Bank;
                players[stack.Owner] = players[stack.Owner] with { Bank = outcome.Captor };
                players[loser.Id] = loser with { Bank = outcome.Loser, CapturedBy = outcome.CapturedBy };
                foreach (string id in outcome.Berserk) stacks[id] = stacks[id] with { Order = CapitalCapture.BerserkOrder };
                after = outcome.Capital;
                sites = map.Sites.Select(s => ReferenceEquals(s, site) ? after : s).ToList();
                var razed = outcome.RodsRazed.ToHashSet(StringComparer.OrdinalIgnoreCase);
                map = WithRods(map, map.Rods.Where(r => !razed.Contains(r.Key)).ToDictionary(r => r.Key, r => r.Value,
                                                                                               StringComparer.OrdinalIgnoreCase));
                looks = looks with { Rods = [.. looks.Rods.Where(r => !razed.Contains(r.Id))] };
            }
        }
        if (result.Gained != Bank.Zero)
            players[stack.Owner] = players[stack.Owner] with { Bank = Treasury.Apply(players[stack.Owner].Bank, result.Gained) };
        var itemTypes = map.ItemTypes;
        var ids = map.IdNext;
        var gained = new List<string>();
        var created = new List<string>();
        if (result.ItemsGained.Count > 0)
        {
            // Руина хранит тип предмета (G000IG…), отряд — экземпляры (IM): добыча становится новым экземпляром.
            var types = itemTypes.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            // Мешок отдаёт предметы с конца (0x5d6f48: от count − 1 до 0, каждый — 0x5d752e в конец списка отряда; окно — тем же
            // порядком, 0x43eba9): карта 21, S143BG0001 [IM0011, IM0012] → у S143KC0004 [IM0012, IM0011].
            IEnumerable<string> source = site.Kind == SiteKind.Bag ? result.ItemsGained.Reverse() : result.ItemsGained;
            foreach (string item in source)
            {
                if (types.ContainsKey(item)) { gained.Add(item); continue; }
                string instance = NewId(ref ids, types.Keys, map.Units.Keys.FirstOrDefault() ?? "S000", "IM");
                types[instance] = item;
                gained.Add(instance);
                created.Add(instance);
            }
            itemTypes = types;
            stacks[stack.Id] = stacks[stack.Id] with { Items = [.. stacks[stack.Id].Items, .. gained] };
        }
        // Подобранный мешок движок убирает со сценария — и с клетки плана.
        map = With(map, stacks: stacks, players: players, sites: sites, itemTypes: itemTypes,
                   plan: after.Gone ? PlanWithout(map, site.Id) : null, idNext: ids);
        // Деревня (CEffectCapture 0x433e2e): прежние хозяева, сброс чужих «охранять город», OCCUPY — GameSession.Capture.cs.
        bool occupy = true;
        if (site.Kind == SiteKind.Village && result.Captured)
            (map, occupy) = VillageCaptured(map, site, map.Sites.First(s => Same(s.Id, site.Id)));
        // Склад и заклинания взятой столицы — захватчику (CEffectCaptureCapital, GameSession.Loot.cs).
        IReadOnlyList<string> capitalItems = [], capitalSpells = [];
        if (site.Kind == SiteKind.Capital && result.Captured && !victory)
            (map, capitalItems, capitalSpells) = CaptureCapitalLoot(map, map.Sites.First(s => Same(s.Id, site.Id)), map.Stacks[stack.Id], site.Owner);
        looks = Retold(looks, map, looks.Stacks) with
        {
            Forts = FortLooks(looks.Forts, map),
            Ruins = [.. looks.Ruins.Select(r => r.Id.Equals(siteId, StringComparison.OrdinalIgnoreCase) && site.Kind == SiteKind.Ruin
                                               ? r with { Looted = true } : r)],
            // Подобранный мешок уходит и с картинки карты, как пустой закрытый (CloseBag); прежде облик оставался, и сундук стоял
            // на месте до конца партии (пометка 3 партии 14.10, S001BG000f).
            Bags = after.Gone ? [.. looks.Bags.Where(b => !Same(b.Id, site.Id))] : looks.Bags,
        };
        if (fort && result.Captured) looks = Inside(looks, looks.Map, looks.Map.Stacks[stack.Id], after);
        // Засчитывается первая победа (0x421043): L_WIN того же хода (события пути) уже поставил победителя — захват его не меняет.
        victory = victory && !Over;
        State = State with { Looks = looks, Info = victory ? State.Info with { Winner = stack.Owner } : State.Info };
        ChargeNewTalismans(created);
        if (result.Captured) Observer?.Happened(new TurnSight(TurnSightKind.Capture, stack.Owner, site.X, site.Y, site.Id) { Occupy = occupy });
        if (captured is { } taken)   // CCmdCaptureResourcesMsg захватчику (0x434d61): окно CCaptureResourcesInterf
            Observer?.Happened(new TurnSight(TurnSightKind.ResourcesCaptured, stack.Owner, site.X, site.Y, site.Owner) { Bank = taken });
        if (capitalItems.Count > 0) Given(TurnSightKind.ItemsGiven, stack.Owner, site.X, site.Y, site.Id, capitalItems);   // 0x434f20
        if (capitalSpells.Count > 0) Given(TurnSightKind.SpellsGiven, stack.Owner, site.X, site.Y, site.Id, capitalSpells, library: true);   // 0x434d0b
        if (site.Kind == SiteKind.Ruin && site.LooterId.Length == 0 && after.LooterId.Length > 0)
            Observer?.Happened(new TurnSight(TurnSightKind.Looted, stack.Owner, site.X, site.Y, site.Id) { Bank = result.Gained });   // LOOTRUIN; окно «Вы победили!» (CCmdLootRuinMsg)
        // Окно полученных предметов (сообщение 44): мешок — всегда, и пустой (0x43eb11); руина — только с трофеем (0x439063).
        if (site.Kind == SiteKind.Bag || (site.Kind == SiteKind.Ruin && gained.Count > 0))
            Given(TurnSightKind.ItemsGiven, stack.Owner, site.X, site.Y, site.Id, gained);
        // Продолжение проверки хода (StackMove 0x41e46b): захват, руина, мешок и посещение лавки, мага, наёмников, тренера (L_VISIT_SITE).
        if (events) RunMoveEvents(stack.Id, continued);
        return new VisitReport(siteId, null, Entered: fort, Captured: result.Captured, Gained: result.Gained,
                               Items: result.ItemsGained, Victory: victory);
    }

    /// <summary>Новый id класса <paramref name="kind"/> по счётчику карты (<see cref="MapState.IdNext"/>, слот 9 <c>0x5ee8bd</c>): номер —
    /// счётчик, счётчик в <paramref name="next"/> растёт на 1. Номер не меньше наибольшего из <paramref name="ids"/> + 1 (карта без
    /// счётчиков, <see cref="NextId"/>). Движок номера удалённых объектов не переиспользует: ai-q09 день 3 — отряд <c>KC0095</c> PL0003
    /// пал в бою, и наём PL0004 дал <c>KC0096</c>, <c>KC0097</c> (<c>id_alloc</c> 7.2); прежнее правило «наибольший из живых + 1» давало
    /// <c>KC0095</c>, <c>KC0096</c>.</summary>
    public static string NewId(ref IReadOnlyDictionary<string, int> next, IEnumerable<string> ids, string sample, string kind)
    {
        string id = NextId(ids, sample, kind);
        int n = int.Parse(id.AsSpan(6), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        if (next.TryGetValue(kind, out int counter) && counter > n)
        {
            n = counter;
            id = $"{id[..6]}{n.ToString("x4", System.Globalization.CultureInfo.InvariantCulture)}";
        }
        var bumped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in next) bumped[k] = v;
        bumped[kind] = n + 1;
        next = bumped;
        return id;
    }

    /// <summary>Изученные заклинания игроков — по возрастанию id (<see cref="KnownSpellOrder"/>): так их держит движок после чтения
    /// сценария или сохранения (<c>0x5fe926</c>).</summary>
    public static MapState KnownSpellsSorted(MapState map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value with { KnownSpells = KnownSpellOrder.Sort(p.Value.KnownSpells, LegacyIds.Base.LegacyOf) },
                                               StringComparer.OrdinalIgnoreCase);
        return With(map, players: players);
    }

    /// <summary>Счётчики id карты по её объектам (<c>0x5ee996</c>, как после загрузки <c>.sg</c>).</summary>
    public static IReadOnlyDictionary<string, int> IdNextOf(MapState map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return LastEdict.Data.Map.MapLoader.IdNextOf(map.Stacks.Keys.Concat(map.Units.Keys).Concat(map.Players.Keys).Concat(map.Objects.Keys)
            .Concat(map.Rods.Keys).Concat(map.Sites.Select(s => s.Id)).Concat(map.ItemTypes.Keys).Concat(map.Fog.Keys)
            .Concat(map.Tombs.Select(t => t.Id)).Concat(map.Subraces.Keys).Concat(map.Plan.Values.SelectMany(v => v)));
    }

    /// <summary>Следующий свободный id класса <paramref name="kind"/> (две буквы) с префиксом сценария из
    /// <paramref name="sample"/>: номер на единицу больше наибольшего, шестнадцатерично. Без счётчика карты — только для
    /// <see cref="NewId"/> и карт без счётчиков.</summary>
    public static string NextId(IEnumerable<string> ids, string sample, string kind)
    {
        string prefix = sample.Length >= 4 ? sample[..4] : "S000";
        int max = ids.Where(k => k.Length == 10 && k.Substring(4, 2).Equals(kind, StringComparison.OrdinalIgnoreCase))
                     .Select(k => int.TryParse(k[6..], System.Globalization.NumberStyles.HexNumber,
                                               System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : -1)
                     .DefaultIfEmpty(-1).Max();
        return $"{prefix}{kind}{(max + 1).ToString("x4", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>Отряд встаёт в укрепление визитёром: клетка — позиция укрепления, <c>INSIDE</c> — его id.</summary>
    private static MapLooks Inside(MapLooks looks, MapState map, MapStack stack, MapSite site)
    {
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stack.Id] = stack with { X = site.X, Y = site.Y, Inside = site.Id };
        var plan = PlanWithout(map.Plan, stack.Id, dropEmptied: false);
        plan[(site.X, site.Y)] = [.. plan.GetValueOrDefault((site.X, site.Y), []), stack.Id];
        var sites = map.Sites.Select(s => s.Id.Equals(site.Id, StringComparison.OrdinalIgnoreCase) ? s with { StackId = stack.Id } : s)
                                 .ToList();
        var next = With(map, stacks: stacks, plan: plan, sites: sites);
        return Retold(looks, next, [.. looks.Stacks.Select(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase)
                                        ? s with { X = site.X, Y = site.Y, Inside = site.Id } : s)])
               with { Forts = FortLooks(looks.Forts, next) };
    }

    /// <summary>Картинки укреплений по объектам карты: владелец (знамя) и щит визитёра.</summary>
    private static IReadOnlyList<FortLook> FortLooks(IReadOnlyList<FortLook> forts, MapState map) =>
        [.. forts.Select(f => map.Sites.FirstOrDefault(s => s.Id.Equals(f.Id, StringComparison.OrdinalIgnoreCase)) is { } site
            ? f with { Owner = site.Owner, Garrison = site.StackId } : f)];

    internal static MapState WithRods(MapState map, IReadOnlyDictionary<string, MapRod> rods) => new()
    {
        Size = map.Size, Tiles = map.Tiles, Stacks = map.Stacks, Units = map.Units, Players = map.Players,
        Objects = map.Objects, Plan = map.Plan, Fog = map.Fog, Turn = map.Turn, Rods = rods, Sites = map.Sites,
        Relations = map.Relations, TalismanCharges = map.TalismanCharges, SpellCasts = map.SpellCasts,
        ItemCasts = map.ItemCasts, SpellEffects = map.SpellEffects, ItemTypes = map.ItemTypes, Limits = map.Limits, MapSeed = map.MapSeed, Subraces = map.Subraces, Destroyed = map.Destroyed, Tombs = map.Tombs,
        IdNext = map.IdNext,
    };

    /// <summary>Открыть туман игрока вокруг его отрядов (маска меняется на месте: туман копится и принадлежит
    /// текущему снимку).</summary>
    public void Reveal(string playerId) => Reveal(Map, playerId);

    /// <summary>Обзор отряда вокруг клетки его пути — у клиента на первом кадре каждого шага своего отряда (задача движения <c>0x4f61e2</c> →
    /// <c>0x4f6856</c> → <c>0x5d52fe</c> по клетке, куда шаг ведёт; <c>docs/map-render-stacks.md</c> §6). Маска меняется на месте, как у
    /// <see cref="Reveal(string)"/>; <see cref="MoveStack"/> потом открывает те же клетки ещё раз. Возвращает, открылась ли хоть одна клетка.</summary>
    public bool RevealFrom(string stackId, int x, int y)
    {
        var map = Map;
        return map.Stacks.TryGetValue(stackId, out var stack) && RevealAround(map, stack, x, y);
    }

    private bool RevealAround(MapState map, MapStack stack, int x, int y)
    {
        if (!map.Players.TryGetValue(stack.Owner, out var player) || !map.Fog.TryGetValue(player.FogId, out var fog)) return false;
        int radius = ScoutRange(map, stack);
        int before = HiddenNear(fog, x, y, radius);
        Fog.Reveal(fog, x, y, radius, Fog.DefaultShape);
        return HiddenNear(fog, x, y, radius) != before;
    }

    /// <summary>Скрытых клеток в квадрате радиуса <paramref name="radius"/> вокруг клетки (обзор шире квадрата не бывает).</summary>
    private static int HiddenNear(bool[,] fog, int x, int y, int radius)
    {
        int n = 0;
        for (int i = Math.Max(0, x - radius); i <= Math.Min(fog.GetLength(0) - 1, x + radius); i++)
            for (int j = Math.Max(0, y - radius); j <= Math.Min(fog.GetLength(1) - 1, y + radius); j++)
                if (fog[i, j]) n++;
        return n;
    }

    private void Reveal(MapState map, string playerId)
    {
        if (!map.Players.TryGetValue(playerId, out var player) || !map.Fog.TryGetValue(player.FogId, out var fog)) return;
        foreach (var stack in map.Stacks.Values)
        {
            if (!stack.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)) continue;
            Fog.Reveal(fog, stack.X, stack.Y, ScoutRange(map, stack), Fog.DefaultShape);
        }
        if (Book is null) return;
        // Укрепления игрока: обзор из каждой клетки пятна, радиус — GcityInf.SCOUT по стороне пятна (ранг 2,
        // прогон С4: клетка в клетку с сохранением).
        var scoutBySize = Book.Economy.Cities.Where(c => c.RaceId.Equals(player.RaceId, StringComparison.OrdinalIgnoreCase))
                                             .GroupBy(c => c.Size).ToDictionary(g => g.Key, g => g.First().Scout);
        if (scoutBySize.Count == 0) return;
        foreach (var site in map.Sites.Where(s => s.Kind is SiteKind.Capital or SiteKind.Village
                                                  && s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)))
        {
            // Столица — строка размера 5, деревня — по полю SIZE записи (docs/new-game.md §3: 0 расхождений на 23
            // масках; «по стороне пятна» давало лишние клетки у деревень).
            int size = site.Kind == SiteKind.Capital ? CapitalCitySize : site.Size ?? 1;
            int radius = scoutBySize.TryGetValue(size, out int r) ? r : scoutBySize.Values.Min();
            foreach (var (x, y) in LandModel.PlanCells(map, site.Id)) Fog.Reveal(fog, x, y, radius, Fog.DefaultShape);
        }
    }

    /// <summary>Строка <c>GcityInf</c> столицы — размер 5.</summary>
    private const int CapitalCitySize = 5;

    /// <summary>Туман игрока с начала: скрыто всё, кроме обзора его отрядов и укреплений (старт партии,
    /// <c>docs/new-game.md</c> §3). Маска новая — туман сценария не меняется.</summary>
    public void ResetFog(string playerId)
    {
        var map = Map;
        if (!map.Players.TryGetValue(playerId, out var player)) return;
        var fog = map.Fog.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        fog[player.FogId] = Fog.Empty(map.Size);
        map = With(map, fog: fog);
        Reveal(map, playerId);
        State = State with { Looks = Retold(Looks, map, Looks.Stacks) };
    }

    /// <summary>Начало хода игрока на старте партии: полный запас хода его отрядам (<c>ap_restore</c>), регенерация (<c>regen</c>,
    /// <see cref="Regenerate"/>) и доход дня 1 по нынешней земле (без роста). Регенерация — и в день 1: движок до брифинга проходит начало хода
    /// целиком (<c>20261016e-av</c>, карта 6: <c>regen</c> 6437490…, раненые +20 % = 15 % Воителя и 5 % своей земли, 40 → 70; карта 7 — +5 %;
    /// <c>20261016g-port-synth</c>, карта 8: лидер RL 5 → 35 до боя у руины). Прежде порт в день 1 не лечил.</summary>
    public void BeginFirstDay(string playerId)
    {
        if (Book is null) return;
        var map = Regenerate(Map, playerId);
        var player = map.Players[playerId];
        var income = Economy.DailyIncome(map, playerId, Book.Economy, (Difficulty)State.Info.Difficulty);
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[playerId] = WithBank(player, Treasury.Apply(player.Bank, income));
        NoteIncome(playerId, income);
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase)
                                                 ? Copy(p.Value, p.Value.X, p.Value.Y, Allowance(map, p.Value)) : p.Value,
                                             StringComparer.OrdinalIgnoreCase);
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks, players: players), Looks.Stacks) };
    }

    /// <summary>Доход начала хода человека <paramref name="playerId"/> — для окна начала хода (<c>CBeginTurnInterf</c>, <c>0x4b9b9d</c>):
    /// сервер шлёт его в <c>CCmdTurnInfoMsg</c> (<c>0x478102</c>, «показать» = 1) после зачисления (<c>0x43ad2d</c>). Ход не начинался с
    /// загрузки этой партии — null.</summary>
    public Bank? TurnIncome(string playerId) => State.TurnIncome.TryGetValue(playerId, out var income) ? income : null;

    private void NoteIncome(string playerId, Bank income)
    {
        var all = new Dictionary<string, Bank>(State.TurnIncome, StringComparer.OrdinalIgnoreCase) { [playerId] = income };
        State = State with { TurnIncome = all };
    }

    public static MapStack Copy(MapStack s, int x, int y, int move, string? inside = null) =>
        s with { X = x, Y = y, Move = move, Inside = inside ?? s.Inside };

    /// <summary>Те же объекты карты, но с новой картой и новым списком отрядов; признак «лидер жив» облика — по отряду карты
    /// (<see cref="LeaderLooks"/>).</summary>
    public static MapLooks Retold(MapLooks looks, MapState map, IReadOnlyList<StackLook> stacks) => new()
    {
        Map = map,
        TreeImage = looks.TreeImage, TreeGroup = looks.TreeGroup, Forts = looks.Forts, Ruins = looks.Ruins,
        Sites = looks.Sites, Crystals = looks.Crystals, Bags = looks.Bags, Tombs = looks.Tombs, Rods = looks.Rods,
        Landmarks = looks.Landmarks, Mountains = looks.Mountains, Roads = looks.Roads, Banners = looks.Banners,
        Stacks = LeaderLooks(stacks, map),
    };

    /// <summary>Облики отрядов с признаком «лидер жив» отряда карты: значок павшего лидера <c>ICNDEADL</c> и лодка (MapScene) рисуются
    /// по нему, а воскрешение зельем и в укреплении, гибель от заклинания на карте меняют только отряд карты — прежде значок оставался
    /// над воскрешённым лидером.</summary>
    private static IReadOnlyList<StackLook> LeaderLooks(IReadOnlyList<StackLook> stacks, MapState map)
    {
        MapStack? Stale(StackLook s) => map.Stacks.TryGetValue(s.Id, out var m) && m.LeaderAlive != s.LeaderAlive ? m : null;
        return stacks.Any(s => Stale(s) is not null)
            ? [.. stacks.Select(s => Stale(s) is { } m ? s with { LeaderAlive = m.LeaderAlive } : s)]
            : stacks;
    }

    internal static MapPlayer WithBank(MapPlayer p, Bank bank) => p with { Bank = bank };

    /// <summary>Та же карта с другими клетками.</summary>
    public static MapState WithTiles(MapState map, Tile[,] tiles) => new()
    {
        Size = map.Size, Tiles = tiles, Stacks = map.Stacks, Units = map.Units, Players = map.Players,
        Objects = map.Objects, Plan = map.Plan, Fog = map.Fog, Turn = map.Turn, Rods = map.Rods, Sites = map.Sites,
        Relations = map.Relations, TalismanCharges = map.TalismanCharges, SpellCasts = map.SpellCasts,
        ItemCasts = map.ItemCasts, SpellEffects = map.SpellEffects, ItemTypes = map.ItemTypes, Limits = map.Limits, MapSeed = map.MapSeed, Subraces = map.Subraces, Destroyed = map.Destroyed, Tombs = map.Tombs,
        IdNext = map.IdNext,
    };

    /// <summary>План карты без объекта <paramref name="id"/> (подобранный мешок); клетки, где он был один, уходят из плана.</summary>
    public static IReadOnlyDictionary<(int X, int Y), IReadOnlyList<string>> PlanWithout(MapState map, string id) =>
        PlanWithout(map.Plan, id, dropEmptied: true);

    /// <summary>
    /// Копия плана без <paramref name="id"/>: словарь — новый (план не меняется на месте, <c>PlanIndex</c> держится за ссылку), но списки
    /// клеток без объекта — те же, новые только у клеток, где он был. Прежде каждый ход отряда пересоздавал списки всех клеток карты
    /// (этап Э2, A4: копия только задетого). Порядок клеток — прежний. <paramref name="dropEmptied"/> — клетка, где объект был один,
    /// уходит из плана (снятый объект), иначе остаётся пустой (отряд уходит с клетки).
    /// </summary>
    private static Dictionary<(int X, int Y), IReadOnlyList<string>> PlanWithout(IReadOnlyDictionary<(int X, int Y), IReadOnlyList<string>> plan,
                                                                                string id, bool dropEmptied)
    {
        var copy = new Dictionary<(int X, int Y), IReadOnlyList<string>>(plan.Count);
        foreach (var (cell, ids) in plan)
        {
            if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                copy.Add(cell, ids);
                continue;
            }
            IReadOnlyList<string> left = [.. ids.Where(o => !o.Equals(id, StringComparison.OrdinalIgnoreCase))];
            if (left.Count > 0 || !dropEmptied) copy.Add(cell, left);
        }
        return copy;
    }

    /// <summary>Та же карта с заменой отрядов, юнитов, игроков, плана, тумана, дня или потолков сценария.</summary>

    public static MapState With(MapState map,
                                IReadOnlyDictionary<string, MapStack>? stacks = null,
                                IReadOnlyDictionary<string, MapUnit>? units = null,
                                IReadOnlyDictionary<string, MapPlayer>? players = null,
                                IReadOnlyDictionary<(int X, int Y), IReadOnlyList<string>>? plan = null,
                                IReadOnlyDictionary<string, bool[,]>? fog = null,
                                int? turn = null,
                                ScenarioLimits? limits = null,
                                IReadOnlyList<MapSite>? sites = null,
                                IReadOnlyDictionary<string, string>? itemTypes = null,
                                IReadOnlyList<Relation>? relations = null,
                                IReadOnlyList<CastRecord>? spellCasts = null,
                                IReadOnlyDictionary<string, int>? talismanCharges = null,
                                IReadOnlyList<MapTomb>? tombs = null,
                                IReadOnlyList<CastRecord>? itemCasts = null,
                                IReadOnlyList<SpellEffectRecord>? spellEffects = null,
                                IReadOnlyDictionary<string, MapObject>? objects = null,
                                IReadOnlyDictionary<string, int>? idNext = null) => new()
    {
        Size = map.Size, Tiles = map.Tiles, Stacks = stacks ?? map.Stacks, Units = units ?? map.Units,
        Players = players ?? map.Players, Objects = objects ?? map.Objects, Plan = plan ?? map.Plan, Fog = fog ?? map.Fog,
        Turn = turn ?? map.Turn, Rods = map.Rods, Sites = sites ?? map.Sites, Relations = relations ?? map.Relations,
        TalismanCharges = talismanCharges ?? map.TalismanCharges, SpellCasts = spellCasts ?? map.SpellCasts, ItemCasts = itemCasts ?? map.ItemCasts,
        SpellEffects = spellEffects ?? map.SpellEffects, ItemTypes = itemTypes ?? map.ItemTypes, Limits = limits ?? map.Limits, MapSeed = map.MapSeed, Subraces = map.Subraces, Destroyed = map.Destroyed, Tombs = tombs ?? map.Tombs,
        IdNext = idNext ?? map.IdNext,
    };
}

/// <summary>Итог конца дня: какой день начался и что случилось (доход игроков) — для журнала и проверок.</summary>
public sealed record DayReport(int Day, IReadOnlyList<string> Events);

/// <summary>Итог входа в объект: <see cref="Refused"/> непусто — входа не было, и там сказано почему.</summary>
public sealed record VisitReport(string SiteId, string? Refused, bool Entered = false, bool Captured = false,
                                 Bank? Gained = null, IReadOnlyList<string>? Items = null, bool Victory = false);
