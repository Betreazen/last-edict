using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using D2.Data.Gfx;
using D2.Data.Map;
using D2.Data.Play;
using D2.Rules.Map;
using Godot;

namespace D2.Godot;

/// <summary>Мышь на карте: щелчок по своему отряду выбирает его, щелчок по клетке показывает путь, повторный
/// щелчок по той же клетке отправляет отряд (<c>docs/map-path.md</c> §4). Путь считает <see cref="PathFinder"/>,
/// метки рисует <see cref="MapScene.Marks"/>.
///
/// <para>Чего пока нет: правая кнопка (окна <c>DLG_R_C_*</c>), курсоры, числа дней над флажками, режим
/// <c>Ctrl</c>, ход шагами с анимацией — отряд переставляется сразу, и сцена собирается заново.</para></summary>
public partial class MapView
{
    private string? _selected;
    private StackPath? _preview;
    private (int X, int Y)? _target;

    /// <summary>Направление по шагу (<c>FACING</c> 0..7, <c>0x5ed8ad</c>): индекс — шаг из таблицы.</summary>
    /// <summary>Что-то на карте изменилось: выбор, путь, положение отряда, день.</summary>
    public event Action? Changed;

    /// <summary>Отряд дошёл до врага и нападает: (нападающий, защитник). Бой открывает хозяин.</summary>
    public event Action<string, string>? AttackRequested;

    /// <summary>Отряд вошёл в своё укрепление (<see cref="GameSession.Enter"/>): хозяин открывает окно города — столица
    /// меняет панель на <c>DLG_CAPITAL</c>, деревня открывает <c>DLG_CITY_STACK</c> (граф экранов, переходы без элемента).</summary>
    public event Action<string>? Entered;

    /// <summary>Своё укрепление, уже выбранное щелчком вне сердца (<c>CTaskSelectCity</c>), щёлкнуто ещё раз — открыть его (<c>0x4cf79d</c>): столица —
    /// экран столицы (<c>CCapitalInterf</c>, <c>0x405a4b</c>), деревня — окно армии (<c>0x486c8e</c>).</summary>
    public event Action<string>? FortOpened;

    /// <summary>Щелчок по своему укреплению, гость которого выбран, — окно армии этого укрепления (<c>CCityStackInterf</c>): так у движка и
    /// для столицы (<c>0x4d0e2c</c> → <c>0x486c8e</c>, без проверки вида укрепления).</summary>
    public event Action<string>? CityArmyOpened;

    /// <summary>Выбранное своё укрепление (<c>CTaskSelectCity</c>, <c>0x4cf43c</c>) — щелчком вне его сердца; нет — null. Выбор отряда его снимает.</summary>
    public string? SelectedFort { get; private set; }

    /// <summary>Отряд дошёл до площадки (торговец, маг, тренер, наёмники) или щёлкнул по ней, стоя рядом: хозяин открывает её
    /// окно (команда <c>StackVisit</c> → <c>0x48b218</c>; ответ пользователя 25.09). (отряд, площадка).</summary>
    public event Action<string, string>? SiteVisited;

    /// <summary>Щелчок по сносимому жезлу, до которого отряд сегодня дойдёт (<see cref="GameSession.AsksRodBreak"/>): хозяин спрашивает
    /// «Хотите уничтожить этот жезл?» и на «Да» зовёт <see cref="ConfirmRodBreak"/>. (отряд, жезл).</summary>
    public event Action<string, string>? RodBreakAsked;

    /// <summary>Щелчок выбранным отрядом по союзнику (<see cref="GameSession.AllyRefuses"/>): хозяин показывает окно
    /// <see cref="GameSession.AllyRefusalText"/> «Перед тем, как атаковать союзника, расторгните альянс с ним.»; пути нет.</summary>
    public event Action? AllyRefused;

    private static bool Trading(SiteKind kind) => kind is SiteKind.Merchant or SiteKind.Mage or SiteKind.Trainer or SiteKind.Mercenaries;

    /// <summary>Враг, на которого идёт отряд: встанет рядом и нападёт по окончании шага.</summary>
    private string? _attackTarget;

    /// <summary>Отряды для «Следующего лидера» (<see cref="NextLeaderRule"/>).</summary>
    public IReadOnlyList<MapStack> NextLeaders() => NextLeaderRule.Of(Map, Owned);

    /// <summary>Следующий свой отряд (кнопка «Следующий лидер», Tab) — по <see cref="NextLeaders"/>; вид — на него.</summary>
    public void NextLeader()
    {
        var own = NextLeaders();
        if (own.Count == 0) return;
        int at = _selected is null ? -1 : own.ToList().FindIndex(s => s.Id.Equals(_selected, StringComparison.OrdinalIgnoreCase));
        var next = own[(at + 1) % own.Count];
        _selected = next.Id;
        SelectedFort = null;
        _preview = null;
        _target = null;
        ShowRoute();   // путь отряда — снова (0x4cf9cc → 0x4cfead)
        CenterOn((next.X, next.Y));
        Marks();
        Changed?.Invoke();
    }

    /// <summary>Выбрать свой отряд и показать его (вывод героя из города).</summary>
    public void Select(string stackId)
    {
        if (!Map.Stacks.TryGetValue(stackId, out var stack)) return;
        _selected = stack.Id;
        SelectedFort = null;
        _preview = null;
        _target = null;
        ShowRoute();
        CenterOn((stack.X, stack.Y));
        Marks();
        Changed?.Invoke();
    }

    /// <summary>Снять выделение отряда <paramref name="stackId"/>, если выделен он (<c>CCmdStackActionMsg</c> действие 2, <c>0x48d8f3</c>:
    /// отряд ушёл к другому хозяину).</summary>
    public void Deselect(string stackId)
    {
        if (!string.Equals(_selected, stackId, StringComparison.OrdinalIgnoreCase)) return;
        _selected = null;
        _preview = null;
        _target = null;
        Marks();
        Changed?.Invoke();
    }

    /// <summary>Выбранный отряд выбран и его лидер — для панели.</summary>
    public MapStack? SelectedStack => _selected is not null ? Map.Stacks.GetValueOrDefault(_selected) : null;

    /// <summary>Выбранный отряд и показанный путь — для проверок.</summary>
    public string? Selected => _selected;

    public StackPath? Preview => _preview;

    /// <summary>Щелчок по клетке карты.</summary>
    public void Click(int x, int y)
    {
        if (!Map.Contains(x, y)) return;
        if (StackAt(x, y) is { } own && Owned(own) && own.Id != _selected)
        {
            SelectStack(own);
            return;
        }
        if (!Walking && OwnFortAt(x, y) is { } fort && FortClick(fort, x, y)) return;
        if (_selected is null || !Map.Stacks.TryGetValue(_selected, out var stack)) return;
        if (Walking) return;
        Command(stack, x, y);
    }

    /// <summary>Щелчок выбранным отрядом по клетке (<c>CTaskSelectUnit</c> <c>0x4d0e2c</c>; его же зовёт «Продолжить движение»,
    /// <see cref="ContinueMove"/>): подтверждение показанного пути — ход, иначе новый путь к клетке.</summary>
    private void Command(MapStack stack, int x, int y)
    {
        // Клетка самого отряда: у движка — окно отряда или города, путь и запомненный путь не трогаются (0x4d0e2c, ветвь 1).
        if ((x, y) == (stack.X, stack.Y)) return;
        if (Session.BreakableRodAt(stack.Id, x, y) is { } rod)
        {
            RodClick(stack, rod, x, y);
            return;
        }
        // Щелчок, подтверждающий показанный путь (0x4cc8c2: по концу пути или по клетке с той же целью и тем же концом), — идти; путь из
        // одной точки (враг вплотную) — сразу в бой. Пустая клетка над чужим отрядом целит в него (0x4d04c3, GameSession.ClickAim): путь
        // показывается к нему, а повторный щелчок по той же клетке его не подтверждает (журнал карты 10, VA1–VA3).
        if (_target is { } aim && (x, y) != (stack.X, stack.Y) && _preview is { Points.Count: > 0 } preview
            && (preview.Points.Count > 1 || TargetAt(aim.X, aim.Y) is not null) && Session.ClickConfirms(stack, preview, aim, x, y, Options(stack)))
        {
            Move(stack, preview);
            return;
        }
        var aimed = Session.ClickAim(stack, x, y);
        // Союзник под прицелом (отряд, крепость, жезл) — пути нет, окно X005TA0303; показанный путь не трогается (0x4d1590 → 0x4d0ca4).
        if (Session.AllyRefuses(stack, aimed))
        {
            GameLog.Verbose($"allyrefused {stack.Id} щелчок=({x},{y}) цель=({aimed.X},{aimed.Y})");
            AllyRefused?.Invoke();
            return;
        }
        // Призыв или иллюзия у своего укрепления без гостя — ни пути, ни входа; показанный путь не трогается (0x4d1590 → 0x4d1a6c, Г14).
        if (Session.OwnFortRefuses(stack, aimed))
        {
            GameLog.Verbose($"ownfortrefused {stack.Id} щелчок=({x},{y}) цель=({aimed.X},{aimed.Y})");
            return;
        }
        _target = aimed;
        _preview = Session.PathTo(stack, _target.Value.X, _target.Value.Y, Options(stack));
        _routes.Remember(stack.Id, _preview, aimed);   // путь отряда — до следующего показанного щелчком (0x4d1209 → 0x41a799)
        if (_preview.Points.Count == 0) NoPathSound();
        Marks();
        Changed?.Invoke();
    }

    /// <summary>Щелчок по сносимому жезлу (<c>0x4d0e2c</c> → <c>0x4d144d</c>, <see cref="GameSession.AsksRodBreak"/>): первый — путь (рядом с
    /// жезлом путь — одна клетка отряда, и вопрос сразу), второй по той же клетке — вопрос, если сегодня дойдёт, иначе обычный ход.</summary>
    private void RodClick(MapStack stack, MapRod rod, int x, int y)
    {
        if (_target != (x, y) || _preview is null)
        {
            _target = (x, y);
            _preview = Session.PathTo(stack, x, y, Options(stack));
            _routes.Remember(stack.Id, _preview, (x, y));   // путь к жезлу показан щелчком — тоже путь отряда (0x4d1590 → 0x41a799)
            Marks();
            Changed?.Invoke();
            if (_preview.Points.Count != 1) return;
        }
        if (_preview is not { Points.Count: > 0 } path) return;
        if (!GameSession.AsksRodBreak(path))
        {
            Move(stack, path);
            return;
        }
        GameLog.Verbose($"rodbreak ask {stack.Id} {rod.Id} путь={path.Points.Count}");
        RodBreakAsked?.Invoke(stack.Id, rod.Id);
    }

    /// <summary>«Да» на вопрос сноса (<c>0x4d1ec7</c>: щелчок повторяется с флагом): отряд идёт по показанному пути и у жезла сносит его
    /// (шаг у цели <c>0x4408cb</c> → <c>CEffectBreakRod</c>, <see cref="GameSession.BreakRod"/>); рядом — сносит сразу.</summary>
    public void ConfirmRodBreak(string stackId, string rodId)
    {
        if (Walking || !Map.Stacks.TryGetValue(stackId, out var stack) || !Map.Rods.TryGetValue(rodId, out var rod)
            || _preview is not { Points.Count: > 0 } path || _target != (rod.X, rod.Y)) return;
        _selected = stackId;
        Move(stack, path);
    }

    private void SelectStack(MapStack own)
    {
        _selected = own.Id;
        SelectedFort = null;
        SelectVoice(own);   // голос лидера — MapView.Sounds.cs
        _preview = null;
        _target = null;
        ShowRoute();   // путь, который отряд помнит, — снова (0x4cf9cc → 0x4cfead)
        Marks();
        Changed?.Invoke();
    }

    /// <summary>
    /// Щелчок по своему укреплению (нажатие левой у движка: <c>CTaskSelectUnit</c> <c>0x4d1c25</c> и <c>CTaskSelectCity</c> <c>0x4cf79d</c>
    /// сначала зовут общий <c>0x4d3f71</c>). Сердце укрепления — клетка входа и три клетки над ней и левее (<c>0x5e948d</c>: вход минус
    /// клетка — 0 или 1 по обеим осям). Что под мышью, решает <c>0x4d4262</c>: в сердце — гость (нет гостя — ничего), вне сердца — само
    /// укрепление. Значит, щелчок по сердцу выбирает гостя (<c>0x4d45a6</c>), а без гостя не обрабатывается — выбранному отряду это путь
    /// к укреплению, как к любой цели; щелчок по сердцу укрепления с выбранным гостем — окно армии (<c>0x4d0e2c</c>). Щелчок вне сердца
    /// выбирает укрепление (<c>CTaskSelectCity</c>, <c>0x4d45a6</c> → <c>0x4cf43c</c>), а щелчок по уже выбранному (второе нажатие двойного)
    /// открывает его (<c>0x4cf79d</c>: <c>0x4cf6a9</c> даёт укрепление по любой его клетке, кроме самой клетки входа при госте): столица —
    /// экран столицы (<c>0x405a4b</c>), деревня — окно армии (<c>0x486c8e</c>). true — щелчок обработан; иначе — путь.
    /// </summary>
    private bool FortClick(string fortId, int x, int y)
    {
        var site = Map.Sites.FirstOrDefault(s => s.Id.Equals(fortId, StringComparison.OrdinalIgnoreCase));
        if (site is null) return false;
        string guest = site.StackId;
        bool hasGuest = guest.Length > 0 && Map.Stacks.ContainsKey(guest);
        bool atGate = PathFinder.EntranceOf(Map, fortId) is { } e && e.X == x && e.Y == y;
        if (fortId.Equals(SelectedFort, StringComparison.OrdinalIgnoreCase) && !(hasGuest && atGate))
        {
            FortOpened?.Invoke(fortId);
            return true;
        }
        if (InFortCore(fortId, x, y))
        {
            if (!hasGuest) return false;
            if (guest.Equals(_selected, StringComparison.OrdinalIgnoreCase)) CityArmyOpened?.Invoke(fortId);
            else SelectStack(Map.Stacks[guest]);
            return true;
        }
        SelectedFort = site.Id;
        _selected = null;
        _preview = null;
        _target = null;
        Marks();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Клетка — сердце укрепления (<c>0x5e948d</c>): вход (<see cref="PathFinder.EntranceOf"/> — правый нижний угол пятна, <c>0x5e9563</c>)
    /// минус клетка даёт 0 или 1 по обеим осям.</summary>
    private bool InFortCore(string fortId, int x, int y) =>
        PathFinder.EntranceOf(Map, fortId) is { } e && e.X - x is 0 or 1 && e.Y - y is 0 or 1;

    /// <summary>Отряд идёт по сегодняшней части пути ключевыми кадрами задачи движения (<see cref="StackWalk"/>, <c>docs/map-render-stacks.md</c> §6):
    /// картинки встают в точку кадра, вид центрируется на ней, на первом кадре шага открывается обзор вокруг клетки, куда шаг ведёт; на каждом
    /// шаге меняется направление. По окончании отряд переставляется (<see cref="GameSession.MoveStack"/>), и сцена собирается заново.</summary>
    private void Move(MapStack stack, StackPath path)
    {
        // Упреждающая проверка пути событиями (docs/map-path.md §7): отряд встаёт там, где сработало бы событие.
        // Команда видит невидимых (GameSession.CommandPath): путь сквозь невидимого врага кончается перед ним, и он нападает сам; враг
        // (любой) у последней достижимой — объект действия команды (0x5cd11f): по приходу бой с ним.
        var move = PlayerMove.Plan(Session, stack.Id, path, _target, Options(stack));
        var today = move.Today.ToList();
        if (move.Fired.Count > 0 || move.Cut)
            GameLog.Verbose($"pathevents {stack.Id} клеток={path.Today.Count} пройдёт={today.Count} событий={move.Fired.Count}");
        // Щелчок по врагу, городу, руине или мешку: путь кончается вплотную к цели (её клетку искатель пути снимает), и,
        // если отряд дойдёт сегодня (и событие не оборвёт путь), по приходу он нападает, входит или берёт мешок.
        _attackTarget = null;
        if (_target is { } aim && !move.Cut && today.Count == path.Points.Count - 1 && TargetAt(aim.X, aim.Y) is { } target
            && Near(path.Points[^1], target.Cell) && (!IsBag(target.Id) || stack.Move > 0))
            _attackTarget = target.Id;
        // Мешок оборвал путь (0x5cda67, StackPath.CutAt): отряд дошёл до последней достижимой — эффекты хода берут мешок с соседней клетки
        // (0x43fbac → 0x4408cb по клетке обрыва; флаг 0x43fc3c: путь не оборван событием). docs/map-path.md §8, поправка 28.09.
        if (_attackTarget is null && !move.Cut && path.CutAt is { } cut && today.Count == path.Today.Count && stack.Move > 0
            && BagIdAt(cut.X, cut.Y) is { } bagAhead)
            _attackTarget = bagAhead;
        if (move.Attack is { } foe) _attackTarget = foe;
        if (today.Count == 0 && _attackTarget is null) return;
        var shownTarget = _target;
        _preview = null;
        _target = null;
        _selectionHeld = today.Count > 0;   // команда хода снимает эллипс до прихода (0x4d2260 → 0x4d0252), MapView.Selection.cs
        Marks();
        if (today.Count == 0)
        {
            // Команда хода с места (зонд: строка move); вставшая у старта перед невидимым — со списанием конца списка (PlayerMove.Charge).
            Session.Run(new D2.Data.Play.Commands.ActInPlaceCommand(stack.Id, _attackTarget is { } aimed && Attacks(aimed), move.Charge ?? 0));
            _routes.Walked(stack.Id, path, 0, shownTarget);
            Attack(stack.Id);
            if (!Session.InBattle) RestoreShownPath();
            return;
        }
        _walk = new Walk(stack, path, [.. today], 0, move.Fired, -1, move.Charge, shownTarget);
        _walkAnim = (Time.GetTicksMsec(), WalkFrames.Frames);
        // Перед ходом — вид к отряду, если он дальше 2 клеток (0x4f7f9b: задача прокрутки перед CMidAnim2Unit), затем шаги.
        ScrollThen((stack.X, stack.Y), onlyIfFar: true, () => BeginWalk(stack, path, today));
    }

    /// <summary>Первый шаг хода: звук, картинки идущего отряда и первый ключевой кадр — сразу (вид на отряде, обзор вокруг первой клетки шага).</summary>
    private void BeginWalk(MapStack stack, StackPath path, List<PathPoint> today)
    {
        (_walkLastKeyMs, _walkClockMs) = (-1, 0);
        _walkTicker.Reset();
        StepSound(stack.Id, today[0].X, today[0].Y, first: true, AttackStep(0, today.Count));
        var look = Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase));
        if (look is null) { Finish(); return; }
        Reload(hiddenStack: stack.Id);
        _objects.ShowMoving(Scene.StackSprites(look, Facing(path.Points[0], today[0]), moving: true),
                            GameData.Content.Gfx, GameData.GfxRoot);
        StepWalk(0);
    }

    /// <summary>Ход к клетке с остановкой рядом с ней (жезл дальше соседней клетки: сервер ведёт отряд к клетке и ставит жезл с соседней —
    /// <c>0x440cfc</c>; в журналах 123 постановки из 123 — жезл в соседней клетке от отряда, ни одной под ним). Возвращает, пошёл ли отряд.</summary>
    public bool WalkBeside(string stackId, int x, int y)
    {
        if (Walking || !Map.Stacks.TryGetValue(stackId, out var stack)) return false;
        var path = Session.PathTo(stack, x, y, Options(stack));
        if (path.Points.Count < 3 || path.Points[^1].X != x || path.Points[^1].Y != y) return false;
        var beside = new StackPath([.. path.Points.Take(path.Points.Count - 1)], Math.Min(path.Reachable, path.Points.Count - 2))
                     { CutAt = path.CutAt };
        _selected = stack.Id;
        _target = null;
        Move(stack, beside);
        return Walking;
    }

    /// <summary>Шаг хода-нападения (<see cref="StepSound"/>): null — ход не нападение, true — последний шаг (звук <c>ATTK</c>). Флаг
    /// движка (<c>0x43fc3c</c>) — только у несоюзного отряда или чужой крепости за концом пути; руина, лавка, мешок, своя крепость — нет.</summary>
    private bool? AttackStep(int step, int count) => _attackTarget is { } target && Attacks(target) ? step == count - 1 : null;

    private bool Attacks(string target) =>
        Map.Stacks.ContainsKey(target)
        || Map.Sites.Any(s => s.Id.Equals(target, StringComparison.OrdinalIgnoreCase) && s.Kind is SiteKind.Capital or SiteKind.Village
                              && !Owned(s.Owner));

    /// <summary>Идущий отряд: путь, номер текущего шага и последний показанный ключевой кадр шага (−1 — ещё ни одного). <c>Charge</c> —
    /// списание команды, вставшей перед невидимым (<see cref="PlayerMove.Charge"/>); null — цена клетки, где встал.</summary>
    private sealed record Walk(MapStack Stack, StackPath Path, IReadOnlyList<PathPoint> Today, int Step,
                               IReadOnlyList<EventFiring> Fired, int Shown, int? Charge = null, (int X, int Y)? Target = null);

    private Walk? _walk;

    /// <summary>Начало хода своего отряда и счёт ключевых кадров до него — строка <c>map_anim CMidAnim2Unit</c> зонда по концу хода.</summary>
    private (ulong StartMs, int Frames) _walkAnim;

    /// <summary>Часы хода отряда — группа <see cref="AnimClock.StackWalk"/>: 33 мс при <see cref="StackSmooth"/>, иначе 66; ключевой кадр за тик.</summary>
    private readonly AnimTicker _walkTicker = new(AnimClock.StackWalk(StackWalk.SmoothScroll));

    /// <summary>Скорость шага своих отрядов — множитель настройки «скорость движения» (<see cref="MapSettings.SpeedFactor"/>): точек между
    /// ключевыми кадрами (<see cref="StackWalk.PixelsPerKeyframe"/>).</summary>
    public float WalkSpeed { get; set; } = 1f;

    /// <summary>Шаг кадра идущего отряда пополам — флажок «Плавная прокрутка при движении отряда» (<see cref="MapSettings.StackSmoothScroll"/>).</summary>
    public bool StackSmooth
    {
        get => _stackSmooth;
        set
        {
            _stackSmooth = value;
            _walkTicker.Group = _aiWalkTicker.Group = AnimClock.StackWalk(value);   // флажок меняет и группу часов хода (0x4f5e7c)
        }
    }

    private bool _stackSmooth = StackWalk.SmoothScroll;

    /// <summary>Идёт ли сейчас отряд.</summary>
    public bool Walking => _walk is not null || _pendingAttack is not null;

    /// <summary>Шагов своего отряда, на которых открылся туман, и всего шагов с начала (проверка: туман по шагу, а не по концу пути).</summary>
    public (int Revealed, int Steps) WalkFog { get; private set; }

    /// <summary>Ключевых кадров своих ходов и сколько раз за ними шёл вид (проверка: вид на отряде на каждом кадре).</summary>
    public (int Frames, int Followed) WalkFrames { get; private set; }

    /// <summary>Ключевые кадры шага из клетки <paramref name="from"/> в соседнюю <paramref name="to"/> при множителе скорости.</summary>
    private IReadOnlyList<(int X, int Y)> StepFrames((int X, int Y) from, (int X, int Y) to, float speed)
    {
        var (fx, fy) = GroundGeometry.CellOrigin(from.X, from.Y);
        var (tx, ty) = GroundGeometry.CellOrigin(to.X, to.Y);
        return StackWalk.Keyframes(tx - fx, ty - fy, StackWalk.PixelsPerKeyframe(speed, StackSmooth));
    }

    /// <summary>Вид — на точку идущего отряда: середина клетки плюс сдвиг кадра (<c>0x540c38</c>, как <see cref="CenterOn"/>).</summary>
    private void FollowStack((int X, int Y) cell, Vector2 offset)
    {
        var (x, y) = GroundGeometry.CellOrigin(cell.X, cell.Y);
        _camera.Position = Clamp(new Vector2(x + 32, y + 16) + offset);
    }

    /// <summary>Движение отряда между клетками по ключевым кадрам: ключевой кадр за тик часов хода (<see cref="_walkTicker"/>, без догона); первый
    /// кадр шага — сразу (в начале хода и в тике, где кончился прежний шаг). Зовётся каждый кадр (и при начале хода с нулевым временем).</summary>
    private void StepWalk(double delta)
    {
        // Вид ещё едет к отряду (BeginWalk — по концу прокрутки) или туман меняется кольцами: у движка задачи анимаций идут по очереди.
        if (_walk is not { } walk || Scrolling || FogChanging) return;
        var from = walk.Step == 0 ? (walk.Path.Points[0].X, walk.Path.Points[0].Y)
                                  : (walk.Today[walk.Step - 1].X, walk.Today[walk.Step - 1].Y);
        var to = (walk.Today[walk.Step].X, walk.Today[walk.Step].Y);
        var frames = StepFrames(from, to, WalkSpeed);
        int k = walk.Shown + 1;
        _walkClockMs += delta * 1000;
        if (walk.Shown >= 0 && !_walkTicker.Advance(delta * 1000)) return;
        if (k >= frames.Count)
        {
            if (walk.Step + 1 >= walk.Today.Count) { Finish(); return; }
            var next = walk.Today[walk.Step + 1];
            _walk = walk with { Step = walk.Step + 1, Shown = -1 };
            StepSound(walk.Stack.Id, next.X, next.Y, first: false, AttackStep(walk.Step + 1, walk.Today.Count));
            if (Looks.Stacks.FirstOrDefault(s => s.Id.Equals(walk.Stack.Id, StringComparison.OrdinalIgnoreCase)) is { } look)
                _objects.ShowMoving(Scene.StackSprites(look with { X = to.Item1, Y = to.Item2 },
                                                       Facing(walk.Today[walk.Step], next), moving: true),
                                    GameData.Content.Gfx, GameData.GfxRoot);
            _objects.MoveOffset(Vector2.Zero);
            StepWalk(0);   // первый кадр нового шага — в этом же тике
            return;
        }
        _walk = walk with { Shown = k };
        if (k == 0)
        {
            _walkTicker.Reset();
            RevealStep(walk.Stack.Id, to);
        }
        var offset = new Vector2(frames[k].X, frames[k].Y);
        _objects.MoveOffset(offset);
        FollowStack(from, offset);
        var (fx, fy) = GroundGeometry.CellOrigin(from.Item1, from.Item2);
        bool followed = _camera.Position == Clamp(new Vector2(fx + 32, fy + 16) + _objects.MovingOffset);   // вид — на картинке отряда
        WalkFrames = (WalkFrames.Frames + 1, WalkFrames.Followed + (followed ? 1 : 0));
        if (_walkLastKeyMs >= 0)
            WalkTicks = (WalkTicks.Count + 1, WalkTicks.Ms + (_walkClockMs - _walkLastKeyMs),
                         Math.Min(WalkTicks.Count == 0 ? double.MaxValue : WalkTicks.MinMs, _walkClockMs - _walkLastKeyMs));
        _walkLastKeyMs = _walkClockMs;
    }

    /// <summary>Промежутков между ключевыми кадрами своих ходов, их сумма и наименьший в мс (проверка: кадр не короче периода группы хода).</summary>
    public (int Count, double Ms, double MinMs) WalkTicks { get; private set; }

    /// <summary>Часы хода по кадрам Godot и время последнего ключевого кадра (−1 — ход только начался).</summary>
    private double _walkClockMs, _walkLastKeyMs = -1;

    /// <summary>Первый кадр шага своего отряда: обзор вокруг клетки, куда шаг ведёт (<c>0x4f6856</c> → <c>0x5d52fe</c>); открылось — земля и
    /// туман сцены обновляются сразу, а не по концу пути.</summary>
    private void RevealStep(string stackId, (int X, int Y) to)
    {
        bool opened = Viewer is not null && Session.Run<bool>(new D2.Data.Play.Commands.RevealFromCommand(stackId, to.X, to.Y));
        WalkFog = (WalkFog.Revealed + (opened ? 1 : 0), WalkFog.Steps + 1);
        if (!opened) return;
        RefreshGround();
        Reload(hiddenStack: stackId);
    }

    /// <summary>Отряд дошёл: партия переставляет его (<see cref="GameSession.MoveStack"/>), картинки движения
    /// снимаются, сцена собирается заново — целиком, а не по спрайтам: порядок отрисовки зависит от диагонали. Вид — на последней клетке
    /// (<c>0x4f6671</c>).</summary>
    private void Finish()
    {
        if (_walk is not { } walk) return;
        var today = walk.Today;
        var end = today[^1];
        var before = today.Count > 1 ? today[^2] : walk.Path.Points[0];
        _walk = null;
        StepsDone();
        _objects.ShowMoving([], GameData.Content.Gfx, GameData.GfxRoot);
        _objects.MoveOffset(Vector2.Zero);
        CenterOn((end.X, end.Y));
        PortProbe.Av?.MapAnim("end", "CMidAnim2Unit", end.X, end.Y, walk.Stack.Id, (long)(Time.GetTicksMsec() - _walkAnim.StartMs),
                              WalkFrames.Frames - _walkAnim.Frames, group: _walkTicker.Group == AnimGroup.Fast ? 1 : 0);
        var messagesBefore = Session.State.Messages;
        Session.Run(new D2.Data.Play.Commands.MoveStackCommand(walk.Stack.Id, end.X, end.Y, Facing(before, end), walk.Charge ?? Spent(walk.Path, today.Count),
                                                       [.. walk.Fired.Select(D2.Data.Play.Commands.FiringRef.Of)], today));
        GameLog.Verbose($"walked {walk.Stack.Id} в=({end.X},{end.Y}) событий={walk.Fired.Count} туман_по_шагам={WalkFog.Revealed}/{WalkFog.Steps} " +
                 $"кадров={WalkFrames.Frames} вид_на_отряде={WalkFrames.Followed} " +
                 $"кадр_мс={(WalkTicks.Count == 0 ? 0 : WalkTicks.Ms / WalkTicks.Count).ToString("0.#", CultureInfo.InvariantCulture)} " +
                 $"кадр_мин_мс={(WalkTicks.Count == 0 ? 0 : WalkTicks.MinMs).ToString("0.#", CultureInfo.InvariantCulture)} " +
                 $"группа={_walkTicker.PeriodMs}");
        Session.Run(new D2.Data.Play.Commands.LeftFortCommand(walk.Stack.Id));   // ушёл из укрепления — больше не его гость
        SelectionAfterWalk(messagesBefore);
        // Отряд пришёл и открыл обзор — сразу; туман событий на пути (L_UNFOG) — кольцами (MapView.Fog.cs).
        HintEventFog(walk.Fired);
        PlayFogChanges(derive: false);
        RefreshGround();
        Reload();
        Marks();
        Changed?.Invoke();
        _routes.Walked(walk.Stack.Id, walk.Path, today.Count, walk.Target);   // CCmdMoveStackMsg: пройденное срезается (0x41a84d); без цели — стёрт
        Attack(walk.Stack.Id);
        if (!Session.InBattle) RestoreShownPath();
    }

    /// <summary>Сколько очков стоят первые <paramref name="steps"/> шагов пути (цена точки пути — накопленная): у
    /// сокращённого пути (до врага) — меньше, чем весь сегодняшний путь.</summary>
    private static int Spent(StackPath path, int steps) => path.Points[Math.Clamp(steps, 0, path.Points.Count - 1)].Cost;

    /// <summary>Отряд дошёл до цели: у цели есть защитники — бой (хозяин открывает экран боя), нет — вход
    /// (<see cref="GameSession.Enter"/>: свой город, захват пустого, добыча руины).</summary>
    private void Attack(string stackId)
    {
        if (_attackTarget is not { } target) return;
        _attackTarget = null;
        if (!Map.Stacks.ContainsKey(stackId) || Session.EventBattleWaiting) return;   // событие шага начало бой
        if (Map.Rods.ContainsKey(target))
        {
            string? refused = Session.Run<string?>(new D2.Data.Play.Commands.BreakRodCommand(stackId, target));
            GameLog.Verbose($"rodbreak {stackId} {target} {refused?.Replace(' ', '_') ?? "снесён"}");
            Reload();
            Marks();
            Changed?.Invoke();
            return;
        }
        bool site = !Map.Stacks.ContainsKey(target);
        // Вор, у которого есть действия против цели (пустой список — без анимации, 0x4f2eea).
        bool noble = !IsBag(target) && IsNoble(stackId) && Session.ThiefActions(stackId, AiTactics.ThiefTargetOf(Session, target)).Count > 0;
        if (site && Map.Sites.FirstOrDefault(s => s.Id.Equals(target, StringComparison.OrdinalIgnoreCase)) is { } shop && Trading(shop.Kind))
        {
            // Вор у площадки — ANOBLE между ним и входом до окна (CEffectNobleChoose), прочие — сразу окно площадки.
            if (noble && PathFinder.EntranceOf(Map, shop.Id) is { } door && PlayNoble(stackId, door.X, door.Y, EffectSpeed))
            {
                _pendingAttack = (stackId, shop.Id, true);
                return;
            }
            // Посещение — CEffectVisit (список посетивших, проверка событий L_VISIT_SITE) до окна площадки: у движка попап события идёт
            // раньше окна лавки (прогон 13.10, ai-r3: site_visit 2912814, попап 2912882, DLG_MERCHANT 2912904). Прежде окно открывалось без
            // посещения, и событие у человека не срабатывало.
            var visit = Session.Run<VisitReport>(new D2.Data.Play.Commands.EnterCommand(stackId, shop.Id));
            if (visit.Refused is { } why)
            {
                GameLog.Verbose($"visit {shop.Id} {why}");
                return;
            }
            Changed?.Invoke();
            SiteVisited?.Invoke(stackId, shop.Id);
            return;
        }
        if (site && (Owned(SiteOwner(target)) || MapBattle.DefenderOf(Map, target) is null))
        {
            var report = Session.Run<VisitReport>(new D2.Data.Play.Commands.EnterCommand(stackId, target));
            GameLog.Verbose($"visit {target} {(report.Refused ?? (report.Captured ? "захват" : report.Entered ? "вход" : "добыча"))}");
            if (report.Refused == GameSession.RiotRefusal) FortOpened?.Invoke(target);   // окно «Пока город бунтует…»
            // Мешок звучит своим окном (OPENINTR окна предметов, docs/ai-screen.md §12), а не руиной.
            // Разграбление руины звучит по уведомлению модели (TurnSightKind.Looted, MapView.AiTurn.cs) — и при входе, и после боя у руины.
            if (report.Refused is null && !IsBag(target))
                VisitSound(entered: report.Entered && !report.Captured, looted: false);
            RefreshGround();
            Reload();
            Marks();
            Changed?.Invoke();
            // Окно — только у входа в своё укрепление. Захват (деревня ходом или после боя) окна не открывает: у движка за fort_capture
            // человека нет ни одного ui_open (20260919-f7 t=53094 — ход, следующее окно — конец хода; 20261014-replay t=5054297 — только
            // попап события, затем отряд уходит из деревни), а звучит OCCUPY (CCmdOccupyCityMsg с флагом 1, 0x48b415).
            if (report.Entered && !report.Captured) Entered?.Invoke(target);
            return;
        }
        // Начало боя на виду — ABATTLE над целью и SBATTLE (MapView.Sounds.cs); экран боя — после анимации.
        var targetCell = Map.Stacks.TryGetValue(target, out var targetStack) ? (targetStack.X, targetStack.Y)
            : PathFinder.EntranceOf(Map, target) ?? (Map.Stacks[stackId].X, Map.Stacks[stackId].Y);
        // Вор у отряда или крепости — не бой, а выбор действия (CEffectNobleChoose): ANOBLE между вором и целью, потом окно вора.
        if (noble ? PlayNoble(stackId, targetCell.Item1, targetCell.Item2, EffectSpeed)
                  : PlayBattleStart(targetCell.Item1, targetCell.Item2, EffectSpeed))
        {
            _pendingAttack = (stackId, target, false);
            return;
        }
        AttackRequested?.Invoke(stackId, target);
    }

    /// <summary>Проверка: отряд уже у цели — действие у цели, как по концу пути (бой, вход, окно площадки, вор у цели).</summary>
    public void ArriveAt(string stackId, string target)
    {
        _attackTarget = target;
        Attack(stackId);
    }

    /// <summary>Своё укрепление (столица или деревня) в клетке; нет — null.</summary>
    private string? OwnFortAt(int x, int y) =>
        Map.Plan.TryGetValue((x, y), out var ids)
            ? ids.Select(id => Map.Sites.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                 .FirstOrDefault(s => s is { Kind: SiteKind.Capital or SiteKind.Village } && Owned(s.Owner))?.Id
            : null;

    private string SiteOwner(string siteId) =>
        Map.Sites.FirstOrDefault(s => s.Id.Equals(siteId, StringComparison.OrdinalIgnoreCase))?.Owner ?? "";

    /// <summary>Цель щелчка: чужой отряд в поле, укрепление (своё — вход, чужое — осада; отряд в нём защищает его) или
    /// неразграбленная руина. Клетка цели — клетка отряда или вход объекта (правый нижний угол пятна).</summary>
    private (string Id, (int X, int Y) Cell)? TargetAt(int x, int y)
    {
        if (StackAt(x, y) is { } other && !Owned(other) && other.Inside.Length == 0) return (other.Id, (other.X, other.Y));
        // Сносимый жезл: путь кончается рядом, шаг у цели сносит его (0x4408cb).
        if (_selected is { } selected && Session.BreakableRodAt(selected, x, y) is { } rod) return (rod.Id, (rod.X, rod.Y));
        if (!Map.Plan.TryGetValue((x, y), out var ids)) return null;
        foreach (string id in ids)
        {
            var site = Map.Sites.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (site is null) continue;
            bool target = site.Kind switch
            {
                SiteKind.Capital or SiteKind.Village => !Owned(site.Owner) || site.StackId.Length == 0,
                SiteKind.Ruin => site.LooterId.Length == 0,
                var kind when Trading(kind) => true,
                _ => false,
            };
            // Мешок — цель хода: путь встаёт рядом, мешок берут эффекты хода с соседней клетки (0x43fbac → 0x4408cb; путь из двух точек с
            // мешком в конце — без хода, только подбор, 0x43fcb5; docs/map-path.md §8, поправка 28.09).
            if (site.Kind == SiteKind.Bag && !site.Gone) return (site.Id, (site.X, site.Y));
            if (target && PathFinder.EntranceOf(Map, site.Id) is { } entrance) return (site.Id, entrance);
        }
        return null;
    }

    private bool IsBag(string id) => Map.Sites.Any(s => s.Kind == SiteKind.Bag && s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Мешок в клетке (не подобранный); нет — null.</summary>
    private string? BagIdAt(int x, int y) =>
        Map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Bag && !s.Gone && s.X == x && s.Y == y)?.Id;

    private static bool Near(PathPoint end, (int X, int Y) cell) => Math.Abs(end.X - cell.X) <= 1 && Math.Abs(end.Y - cell.Y) <= 1;

    /// <summary>Направление шага (<c>FACING</c>); шаг «на месте» — 4, как в движке.</summary>
    private static int Facing(PathPoint from, PathPoint to) => PathFinder.Facing(from, to);

    /// <summary>Метки: эллипс под выбранным отрядом и флажки показанного пути.</summary>
    private void Marks()
    {
        if (_objects is null) return;
        (int X, int Y)? selected = !_selectionHeld && _selected is not null && Map.Stacks.TryGetValue(_selected, out var s) ? (s.X, s.Y) : null;
        SelectionShown = selected is not null;
        var path = _preview?.Points.Select(p => (p.X, p.Y)).ToList() ?? [];
        bool action = _preview is { } p2 && p2.Points.Count > 0 && ActionAtEnd(p2);
        var marks = Scene.Marks(selected, path, _preview?.Reachable ?? 0, action);
        _markNames = [.. marks.Select(m => m.Name)];
        _objects.ShowMarks(marks, GameData.Content.Gfx, GameData.GfxRoot, _preview is null ? null : _selected);
        // Число дня стоит на клетке, где день сменился, и на последней клетке пути (docs/map-path.md §2).
        var days = new List<(Vector2 At, int Day)>();
        var points = _preview?.Points ?? [];
        for (int i = 1; i < points.Count; i++)
        {
            bool last = i == points.Count - 1;
            if (points[i].Day == 0 || (!last && points[i].Day == points[i + 1].Day)) continue;
            var (cx, cy) = GroundGeometry.CellOrigin(points[i].X, points[i].Y);
            days.Add((new Vector2(cx - 4, cy - 20), points[i].Day));
        }
        _objects.ShowDays(days);
    }

    /// <summary>Клетка с подсветкой <c>TILE_HIGHLIGHT</c>; null — подсветки нет.</summary>
    public (int X, int Y)? Highlighted { get; private set; }

    private const string HighlightChannel = "highlight";

    /// <summary>Подсветка клетки под курсором (<see cref="MapScene.Highlight"/>): при смене клетки анимация продолжается с того же кадра
    /// (<c>0x4d4bc9</c>); null — убрать.</summary>
    public void Highlight((int X, int Y)? cell)
    {
        if (_objects is null || cell == Highlighted) return;
        Highlighted = cell;
        var sprite = cell is { } c ? Scene.Highlight(c.X, c.Y) : null;
        _objects.ShowLayered(HighlightChannel, sprite is null ? [] : [sprite], GameData.Content.Gfx, GameData.GfxRoot, carryPhase: true);
    }

    /// <summary>Кадр анимации подсветки сейчас (проверка переноса фазы); нет — -1.</summary>
    public int HighlightPhase => _objects?.PhaseOf(HighlightChannel) ?? -1;

    /// <summary>Сколько точек подсветки видно поверх остальных картинок (проверка): (видно, всего).</summary>
    public (int Seen, int All) HighlightVisibility() => _objects?.Visibility(HighlightChannel) ?? (0, 0);

    /// <summary>Картинки канала в общем порядке отрисовки (<see cref="MapObjects.ShowLayered"/>): квадрат прицела под туманом и т. п.</summary>
    public void ShowLayered(string channel, IReadOnlyList<MapSprite> sprites) =>
        _objects.ShowLayered(channel, sprites, GameData.Content.Gfx, GameData.GfxRoot);

    /// <summary>Картинка в середине клетки как спрайт общего порядка: слой движка и якорь — сама клетка.</summary>
    public static MapSprite CellSprite(string archive, string name, int x, int y, int width, int height, int layer)
    {
        var (ox, oy) = GroundGeometry.CellOrigin(x, y);
        return new MapSprite(archive, name, false, 1, ox + (GroundGeometry.CellWidth / 2) - (width / 2),
                             oy + (GroundGeometry.CellHeight / 2) - (height / 2), width, height, layer, x, y, "cell");
    }

    /// <summary>Сколько картинок карты ложится половиной, сложением и вычитанием (проверка).</summary>
    public (int Half, int Add, int Subtract) BlendCounts() => _objects.BlendCounts();

    /// <summary>Места меток в общем порядке отрисовки (проверка): номер каждой среди всех картинок и их число.</summary>
    public (IReadOnlyList<int> At, int Of) LayerOrder(string channel = "marks")
    {
        var at = new List<int>();
        for (int i = 0; ; i++)
        {
            var (place, of) = _objects.OrderOf(channel, i);
            if (place < 0) return (at, of);
            at.Add(place);
        }
    }

    /// <summary>Картинки канала на клетках под туманом (проверка): сколько их и сколько легло под картинку тумана своей клетки —
    /// место в общем порядке решает ключ (<see cref="MapSprite.Key"/>), так что сравниваются ключи. Туман — слои с 1024
    /// (<c>FOG</c> 1052, <c>FOG_BORDER</c>).</summary>
    public (int Under, int Fogged) UnderFog(string channel)
    {
        var fog = Scene.Sprites.Where(s => s.Layer >= FogLayers).GroupBy(s => (s.AnchorX, s.AnchorY))
                               .ToDictionary(g => g.Key, g => g.Min(s => s.Key));
        int under = 0, fogged = 0;
        foreach (var s in _objects.Channel(channel))
        {
            if (!fog.TryGetValue((s.AnchorX, s.AnchorY), out long key)) continue;
            fogged++;
            if (s.Key < key) under++;
        }
        return (under, fogged);
    }

    private const int FogLayers = 1024;

    /// <summary>В конце пути ждёт действие (<c>0x4ccc2c</c>): чужой отряд или чужая крепость в клетке цели либо враждебный видимый отряд
    /// вокруг последней достижимой — объект действия (<see cref="PathFinder.ActionObject"/>, <c>0x5cd11f</c>; интерфейс невидимых не видит,
    /// <c>a5</c> = 0): на него команда нападёт (<see cref="PlayerMove.Attack"/>).</summary>
    private bool ActionAtEnd(StackPath path)
    {
        var end = path.Points[^1];
        if (_selected is not null && Map.Stacks.TryGetValue(_selected, out var mover) && mover.Move > 0 && Session.Book is not null)
        {
            // Тот же объект действия, что у команды (PathFinder.ActionObject), но без невидимых: интерфейс их не видит (a5 = 0).
            var hostile = AiTactics.ObjectHostile(Session, mover);
            var goal = _target is { } t ? PathFinder.Target(Map, mover, t.X, t.Y, Options(mover), MovementRules.Vanilla) : (end.X, end.Y);
            if (PathFinder.ActionObject(Map, mover, path, goal, (o, at) => hostile(o, at) && !Session.IsInvisible(o.Id)) is not null) return true;
        }
        foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
        {
            if (!Map.Plan.TryGetValue((end.X + dx, end.Y + dy), out var ids)) continue;
            foreach (string id in ids)
            {
                string kind = id.Length >= 6 ? id.Substring(4, 2).ToUpperInvariant() : "";
                if (kind == "KC" && Map.Stacks.TryGetValue(id, out var other) && !Owned(other) && !Session.IsInvisible(other.Id)) return true;
                if (kind == "FT" && Map.Sites.FirstOrDefault(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                        is { } fort && fort.Owner.Length > 0 && !Owned(fort.Owner)) return true;
            }
        }
        return false;
    }

    private MapStack? StackAt(int x, int y) =>
        Map.Plan.TryGetValue((x, y), out var ids)
            ? ids.Select(id => Map.Stacks.GetValueOrDefault(id)).OfType<MapStack>().FirstOrDefault()
            : null;

    private bool Owned(MapStack stack) => Owned(stack.Owner);

    private bool Owned(string owner) =>
        Viewer is null || owner.Equals(Viewer, StringComparison.OrdinalIgnoreCase);

    /// <summary>Флаги отряда: жив ли лидер и бонусы хода его лидера (<c>data/map/move-bonus.json</c>). У интерфейса
    /// игрока водному отряду суша разрешена, путь по ней помечается «не дойти».</summary>
    private MoveOptions Options(MapStack stack)
    {
        string type = Map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId : "";
        var bonus = GameData.Content.MapArt.MoveBonuses
            .FirstOrDefault(b => b.Unit.Equals(type, StringComparison.OrdinalIgnoreCase));
        var grounds = bonus?.Grounds ?? [];
        return new MoveOptions(stack.LeaderAlive, grounds.Contains(0), grounds.Contains(1), grounds.Contains(3),
                               WaterOnly: false, ForbidWaterOnLand: false);
    }

    /// <summary>Дневной запас очков хода отряда (<see cref="GameSession.DayAllowance"/>).</summary>
    public int DayAllowance(MapStack stack) => Session.DayAllowance(stack);
}
