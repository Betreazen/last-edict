using LastEdict.Data.Map;
using LastEdict.Data.Play;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Итоги хода (<c>CTurnSummaryInterf</c> <c>0x4c4b88</c>, <c>DLG_TURNSUMMARY</c>): записи о том, что случилось с игроком, пока ходили другие.
/// Движок копит их на сервере (<c>0x5f8693</c>) и шлёт в начале хода; порт собирает их наблюдателем хода (<see cref="TurnSummaryWatch"/>).
/// Виды записей (<c>0x4c4e11</c>): 0 — заклинание (на всю карту <c>X005TA0921</c>, по области <c>X160TA0028</c>, по цели <c>X160TA0027</c>),
/// 1 — нападение <c>X160TA0026</c>, 2 — отряд уничтожен <c>X005TA0923</c>, 3 — город захвачен <c>X005TA0924</c>, 4 — жезл уничтожен
/// <c>X160TA0029</c>, 5 — отряд создан <c>X160TA0030</c>, 6 — отряд присоединился <c>X160TA0031</c> / покинул армию <c>X160TA0032</c>
/// (флаг +0x50). Часть карты — <see cref="MapPart"/>. Окно показывается, когда записи есть и итоги включены
/// (<see cref="MapSettings.TurnSummaryShown"/>).
/// </summary>
public sealed class TurnSummaryLogic(WindowContext context, IReadOnlyList<TurnSummaryEntry> entries, IReadOnlyDictionary<string, SpellText>? spellTexts = null)
    : IWindowLogic
{
    public const string Screen = "DLG_TURNSUMMARY";
    private const string List = "LBOX_EVENTS";
    private readonly UiRequests _requests = new();

    public IReadOnlyList<TurnSummaryEntry> Entries => entries;

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"итоги записей={entries.Count}";

    public bool Allows(string element, UiScreenState ui) => element != "BTN_OK";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_TITLE", context.Text("X005TA0917"));
        var spec = ItemCells.Spec(ui, List);
        ui.SetCards(List, [.. entries.Select(e => new UiCard(null, new UiPoint(0, 0), e.Text, new UiRect(0, 0, spec.CellWidth, spec.CellHeight)))]);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is UiClicked { Element: "BTN_OK" }) _requests.Add(new UiClose());
    }

    /// <summary>Правая кнопка по строке (<c>0x4c53f3</c>): заклинание — его карточка. ponytail: карточки отряда и города — карты (сессия Г).</summary>
    public void RightPress(int x, int y, UiScreenState ui)
    {
        if (SiteLogicLists.At(ui, List, entries.Count, x, y) is { } i && entries[i].Kind == 0 && spellTexts is not null)
            _requests.Add(new UiOpen(SpellCardLogic.Screen, new SpellCardLogic(context, entries[i].ObjectId, spellTexts)));
    }

    /// <summary>Часть карты (<c>0x4c52fb</c>): q = размер/4, h = размер/2; |x−h| и |y−h| меньше q — центральная <c>X160TA0037</c>; x &lt; h,
    /// y &lt; h — верхняя <c>0033</c>; x ≥ h, y ≥ h — нижняя <c>0034</c>; x &lt; h, y ≥ h — левая <c>0035</c>; иначе — правая <c>0036</c>.</summary>
    public static string MapPart(int size, int x, int y)
    {
        int q = size / 4, h = size / 2;
        if (Math.Abs(x - h) < q && Math.Abs(y - h) < q) return "X160TA0037";
        return (x < h, y < h) switch
        {
            (true, true) => "X160TA0033",
            (false, false) => "X160TA0034",
            (true, false) => "X160TA0035",
            _ => "X160TA0036",
        };
    }
}

/// <summary>
/// Сбор итогов хода игрока <paramref name="viewer"/> подписчиком хода (<see cref="GameSession.Watch"/>): от конца его хода (снимок
/// <paramref name="before"/>) до начала следующего, по порядку событий. Нападение — бой, где обороняется его отряд или укрепление
/// (<see cref="ITurnObserver.Battle"/> хода ИИ и <see cref="Attacked"/> боя человека в «горячем стуле»); заклинание другого игрока — на всю
/// карту и по области всегда, по цели — если в клетке после действия его отряд или никого (<see cref="Spell"/>, ранг 1); гибель, захват,
/// создание, смена хозяина — отчёты <see cref="TurnSight"/>. Жезлы — разницей снимков в <see cref="Finish"/> (отчёта у сноса нет).
/// Методы наблюдателя зовутся с рабочего потока хода ИИ — записи под замком. <paramref name="resumed"/> — итоги из загруженного сохранения
/// (v9, <see cref="GameState.Summaries"/>): сбор продолжается с их записями и днём, снимок <paramref name="before"/> — загруженная карта
/// (жезлы — разницей от неё).
/// </summary>
public sealed class TurnSummaryWatch(WindowContext c, string viewer, MapLooks before,
                                     IReadOnlyDictionary<string, SpellText>? spellTexts, IReadOnlyDictionary<string, string> raceNames,
                                     PendingSummary? resumed = null)
    : ITurnObserver
{
    private const int WholeMap = 999, Target = 1;
    private readonly List<TurnSummaryEntry> _entries = [.. resumed?.Entries ?? []];
    private readonly Dictionary<string, string> _stackOwners =
        before.Map.Stacks.Values.ToDictionary(s => s.Id, s => s.Owner, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _siteOwners =
        before.Map.Sites.ToDictionary(s => s.Id, s => s.Owner, StringComparer.OrdinalIgnoreCase);

    public string Viewer => viewer;

    /// <summary>День снимка конца хода игрока: итоги отдаются, когда день сменился.</summary>
    public int Day => resumed?.Day ?? before.Map.Turn;

    public void PlayerTurn(string playerId) => Settle();

    public void StackMoving(StackMove move) => Settle();

    /// <summary>Бой собран: журнал играет основной наблюдатель, подписчик только записывает нападение.</summary>
    public IReadOnlyList<TurnRecord>? Battle(BattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Attacked(state.AttackerGroup, state.DefenderGroup);
        return null;
    }

    /// <summary>Нападение (вид 1, <c>X160TA0026</c>): обороняется отряд или укрепление игрока, нападает чужой отряд.</summary>
    public void Attacked(string attackerId, string defenderId)
    {
        Settle();
        var map = c.Session.Map;
        if (!map.Stacks.TryGetValue(attackerId, out var attacker) || Mine(attacker.Owner)) return;
        string defend;
        int x, y;
        if (map.Stacks.TryGetValue(defenderId, out var stack))
        {
            if (!Mine(stack.Owner)) return;
            (defend, x, y) = (StackName(c.Session.Looks, stack), stack.X, stack.Y);
        }
        else if (map.Sites.FirstOrDefault(s => s.Id.Equals(defenderId, StringComparison.OrdinalIgnoreCase)) is { } site && Mine(site.Owner))
            (defend, x, y) = (site.Name.Length > 0 ? site.Name : site.Id, site.X, site.Y);
        else return;
        Add(1, UiTemplate.Fill(c.Text("X160TA0026"), ("DEFEND", defend), ("ASSAIL", StackName(c.Session.Looks, attacker)),
                               ("ASSAILRACE", Race(attacker.Owner)), ("MAPPART", Part(x, y))), attacker.Id);
    }

    public void Happened(TurnSight sight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        // Гибель отряда из клетки заклинания по цели — его действие: запись заклинания — после неё (как у движка).
        bool aimedDied;
        lock (_entries) aimedDied = sight.Kind == TurnSightKind.Death && sight.Id.Equals(_aimed?.StackId, StringComparison.OrdinalIgnoreCase);
        if (!aimedDied) Settle();
        var looks = c.Session.Looks;
        var map = looks.Map;
        switch (sight.Kind)
        {
            case TurnSightKind.Spell:
                Spell(sight);
                break;
            case TurnSightKind.Death when Mine(sight.PlayerId) && map.Stacks.TryGetValue(sight.Id, out var dead):
                Add(2, UiTemplate.Fill(c.Text("X005TA0923"), ("STACK", StackName(looks, dead))), dead.Id);
                break;
            case TurnSightKind.Capture:
                string was = Swap(_siteOwners, sight.Id, sight.PlayerId);
                if (Mine(was) && !Mine(sight.PlayerId) && map.Sites.FirstOrDefault(s => s.Id.Equals(sight.Id, StringComparison.OrdinalIgnoreCase)) is { } fort)
                    Add(3, UiTemplate.Fill(c.Text("X005TA0924"), ("CITY", fort.Name.Length > 0 ? fort.Name : fort.Id)), fort.Id);
                break;
            case TurnSightKind.Created when map.Stacks.TryGetValue(sight.Id, out var born):
                Swap(_stackOwners, born.Id, born.Owner);
                if (Mine(born.Owner))
                    Add(5, UiTemplate.Fill(c.Text("X160TA0030"), ("STACK", StackName(looks, born)), ("MAPPART", Part(born.X, born.Y))), born.Id);
                break;
            case TurnSightKind.OwnerChanged when map.Stacks.TryGetValue(sight.Id, out var moved):
                string previous = Swap(_stackOwners, moved.Id, sight.PlayerId);
                bool joined = Mine(sight.PlayerId) && !Mine(previous), left = Mine(previous) && !Mine(sight.PlayerId);
                if (joined || left)
                    Add(6, UiTemplate.Fill(c.Text(joined ? "X160TA0031" : "X160TA0032"), ("STACK", StackName(looks, moved)),
                                           ("MAPPART", Part(moved.X, moved.Y))), moved.Id);
                break;
        }
    }

    /// <summary>Заклинание другого игрока (вид 0; пишет <c>CEffectSpell::apply</c> <c>0x43e4a7</c> после действия заклинания, ранг 1): на всю
    /// карту (<c>X005TA0921</c>) и по области (<c>X160TA0028</c>) — каждому человеку, без условий (<c>0x43e67a</c>); по цели
    /// (<c>X160TA0027</c>) — хозяину отряда, который в клетке после действия, а если отряда там нет (погиб от заклинания) — каждому
    /// человеку с пустой целью (<c>0x4c4ff2</c>). Отчёт о заклинании приходит до действия, поэтому запись по цели ждёт следующего
    /// события хода (<see cref="Settle"/>): гибель отряда в клетке — между ними. Свои заклинания игрок не видит: движок стирает записи
    /// ходящего в конце его хода (<c>0x420a09</c>).</summary>
    private void Spell(TurnSight sight)
    {
        if (Mine(sight.PlayerId) || !c.Session.RequireBook().Spells.TryGetValue(sight.Id, out var spell)) return;
        var map = c.Session.Looks.Map;
        string name = spellTexts is not null && spellTexts.TryGetValue(spell.Id, out var t) ? c.Text(t.NameTxt) : spell.Id;
        string race = Race(sight.PlayerId);
        if (spell.Area == WholeMap)
            Add(0, UiTemplate.Fill(c.Text("X005TA0921"), ("SPELL", name), ("CASTERRACE", race)), spell.Id);
        else if (spell.Area == Target)
        {
            var aimed = new Aimed(spell.Id, name, race, sight.X, sight.Y,
                                  map.Plan.TryGetValue((sight.X, sight.Y), out var ids) ? ids.FirstOrDefault(map.Stacks.ContainsKey) : null);
            lock (_entries) _aimed = aimed;
        }
        else
            Add(0, UiTemplate.Fill(c.Text("X160TA0028"), ("SPELL", name), ("CASTERRACE", race), ("MAPPART", Part(sight.X, sight.Y))), spell.Id);
    }

    /// <summary>Заклинание по цели, запись которого ждёт исхода: отряд в клетке до действия (нет — null).</summary>
    private sealed record Aimed(string SpellId, string Name, string Race, int X, int Y, string? StackId);

    private Aimed? _aimed;

    /// <summary>Запись заклинания по цели — по отряду, который остался в клетке: свой — с его именем, чужой — никому из нас, никого —
    /// с пустой целью. <see cref="Finish"/> зовётся с главного потока, события — с потока хода ИИ: ожидание — под замком записей.</summary>
    private void Settle()
    {
        Aimed aimed;
        lock (_entries)
        {
            if (_aimed is null) return;
            (aimed, _aimed) = (_aimed, null);
        }
        var looks = c.Session.Looks;
        var target = aimed.StackId is { } id && looks.Map.Stacks.TryGetValue(id, out var s) && s.X == aimed.X && s.Y == aimed.Y ? s : null;
        if (target is not null && !Mine(target.Owner)) return;
        Add(0, UiTemplate.Fill(c.Text("X160TA0027"), ("SPELL", aimed.Name), ("CASTERRACE", aimed.Race),
                               ("TARGET", target is null ? "" : StackName(looks, target)), ("MAPPART", Part(aimed.X, aimed.Y))), aimed.SpellId);
    }

    /// <summary>Записи к показу: события по порядку и снесённые жезлы игрока (разница снимка <paramref name="after"/> с концом его хода).</summary>
    public IReadOnlyList<TurnSummaryEntry> Finish(MapLooks after)
    {
        ArgumentNullException.ThrowIfNull(after);
        Settle();
        lock (_entries)
        {
            var all = _entries.ToList();
            foreach (var rod in before.Map.Rods.Values.Where(r => Mine(r.Owner) && !after.Map.Rods.ContainsKey(r.Id)))
                all.Add(new(4, UiTemplate.Fill(c.Text("X160TA0029"), ("MAPPART", Part(rod.X, rod.Y))), rod.Id));
            return all;
        }
    }

    private void Add(int kind, string text, string id)
    {
        lock (_entries) _entries.Add(new(kind, text, id));
    }

    private static string Swap(Dictionary<string, string> owners, string id, string owner)
    {
        lock (owners)
        {
            string was = owners.GetValueOrDefault(id, "");
            owners[id] = owner;
            return was;
        }
    }

    private bool Mine(string owner) => owner.Equals(viewer, StringComparison.OrdinalIgnoreCase);

    private string Part(int x, int y) => c.Text(TurnSummaryLogic.MapPart(c.Session.Map.Size, x, y));

    private string Race(string playerId) =>
        c.Session.Map.Players.TryGetValue(playerId, out var p) && raceNames.TryGetValue(p.RaceId, out var rn) ? c.Text(rn) : playerId;

    private string StackName(MapLooks looks, MapStack stack)
    {
        var look = looks.Stacks.FirstOrDefault(l => l.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase));
        return look is { LeaderName.Length: > 0 } ? look.LeaderName
            : looks.Map.Units.TryGetValue(stack.LeaderId, out var u) ? c.UnitName(u.TypeId) : stack.Id;
    }
}
