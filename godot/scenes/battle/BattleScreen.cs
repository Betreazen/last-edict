using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastEdict.Data.Battle;
using LastEdict.Data.Dbf;
using LastEdict.Data.Gfx;
using LastEdict.Data.Menu;
using LastEdict.Data.Ui;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;
using Godot;

namespace LastEdict.Godot;

/// <summary>Экран боя: поле (<see cref="BattleView"/>) и поверх него разметка <c>DLG_BATTLE_A</c> с кнопками.
/// Бой ведёт <see cref="BattleSession"/> поверх <c>BattleSim</c>; экран только показывает и передаёт команды,
/// как в игре (<c>docs/battle-screen.md</c>).
///
/// <para>Щелчок по юниту бьёт его (удар уходит при отпускании над тем же юнитом, <c>0x6317a0</c>), кнопки —
/// защита, ожидание, отступление, быстрый бой, переключатель автобоя.</para>
///
/// <para>Бой берётся из журнала корпуса (<c>--journal</c>) или от хозяина (<see cref="Prepared"/>: партия на карте).
/// <c>--check</c> — без окна: доиграть и напечатать числа, <c>--shot файл.png</c> — снимок (только с окном).</para></summary>
public partial class BattleScreen : Node
{
    private const string Screen = "DLG_BATTLE_A";

    /// <summary>Бой закрыт игроком (кнопка «Закрыть»); хозяин вернёт игрока туда, откуда пришёл бой.</summary>
    public event Action? Closed;

    /// <summary>Сцена запущена сама (тогда «Закрыть» выходит из процесса).</summary>
    public bool Standalone { get; set; } = true;

    /// <summary>Бой от хозяина (партия на карте); null — бой из журнала корпуса по аргументам.</summary>
    public BattleState? Prepared { get; set; }

    /// <summary>Правила и генератор хозяина: у партии один поток случайных чисел на все бои, как у движка.</summary>
    public GameRules? Rules { get; set; }

    public IRng? Rng { get; set; }

    /// <summary>Место боя для фона (<c>HUMAN</c>, <c>WATER</c>, <c>CAP_UNDEAD</c>…); null — из <c>--place</c>.</summary>
    public string? PlaceName { get; set; }

    /// <summary>Игрок за нападающих (иначе за защитников).</summary>
    public bool PlayerIsAttacker { get; set; } = true;


    /// <summary>Нападающие — отряд того, кто за ПК (<c>CTaskBattle</c> <c>+0x2c</c>, <see cref="LastEdict.Data.Play.MapBattle.OwnsAttackerStack"/>):
    /// только тогда в конце боя есть «Меню героя» (<see cref="LastEdict.Data.Play.MapBattle.PaperdollOffered"/>). Без хозяина — да.</summary>
    public bool OwnsAttackerStack { get; set; } = true;

    /// <summary>Бой закрыт «Меню героя» (<c>BTN_PAPERDOLL</c> → <c>closeBattleWindow(true)</c> <c>0x633e20</c>): хозяин после итога боя
    /// откроет окно нападавшего отряда (<see cref="LastEdict.Data.Play.GameSession.RequestPaperdoll"/>).</summary>
    public bool Paperdoll { get; private set; }

    /// <summary>«Поменяться сторонами» (<c>TOG_FLIPBATTLE</c>, ini <c>FlippedBattle</c>): раскладка <see cref="BattleLayout"/>; без хозяина —
    /// <c>--flip</c>.</summary>
    public bool Flipped { get; set; }

    /// <summary>«Скорость битвы» 1…4 (<see cref="LastEdict.Data.Map.MapSettings.BattleSpeed"/>): длительность надписей на портретах; 4 «Сразу» — бой
    /// без показа действий. Без хозяина — <c>--speed N</c>.</summary>
    public int Speed { get; set; } = new LastEdict.Data.Map.MapSettings().BattleSpeed;

    /// <summary>«Дополнительная анимация в бою» (<see cref="LastEdict.Data.Map.MapSettings.BattleAnim"/>): покой <c>IDLE</c> (иначе <c>STIL</c>),
    /// анимированные курсоры, рамки и метки (<see cref="BattleScene.Animated"/>). Без хозяина — выключается <c>--still</c>.</summary>
    public bool BattleAnim { get; set; } = true;

    /// <summary>Скорость «Сразу» (<c>data2+9</c>, <c>0x62e26a</c>): замахов, ударов и эффектов нет, покой неподвижен (<c>0x651c34</c>).</summary>
    private bool InstantSpeed => Speed >= LastEdict.Data.Map.MapSettings.InstantBattleSpeed;

    /// <summary>Сколько держится надпись на портрете, мс: (N + 1) тиков по 66 мс (<see cref="LastEdict.Data.Map.MapSettings.NoteTicks"/>).</summary>
    private double NoteMs => LastEdict.Data.Map.MapSettings.NoteTicks(Speed) * AnimFrameMs;

    /// <summary>Вариант фона места: хозяин тянет <c>rand() % n</c> из потока CRT партии (<see cref="BattleScene.BackgroundVariants"/>).</summary>
    public int BackgroundVariant { get; set; }

    /// <summary>Собственное имя юнита по id и текущему типу (<see cref="LastEdict.Data.Play.GameSession.OwnName"/>: у героя во главе отряда — имя
    /// из сохранения, даже пустое); null — имени нет, подпись берёт имя типа (<see cref="NameOf"/>). Без хозяина (бой из журнала) — у всех
    /// имя типа.</summary>
    public Func<string, string, string?> OwnName { get; set; } = (_, _) => null;

    /// <summary>Бой по шагам — хозяину для итога (журнал ходов, победитель).</summary>
    public BattleSession Session => _session;

    private string _place = "NEUTRAL";

    private BattleSession _session = null!;
    private BattleView _field = null!;
    private BattleScene _scene = null!;
    private UiScreenView _panel = null!;
    private Vector2 _host = new(800, 600);
    private SubViewportContainer _box = null!;

    /// <summary>Холст поля в точках поля (разметка с полями прироста); вьюпорт — он же в точках экрана.</summary>
    private Vector2 _canvas;
    private Node2D _overlay = null!;

    /// <summary>Слой окон поверх боя (сообщение, карточки): портреты и рамки панелей лежат на слоях 3000+ (<see cref="BattleScene.PanelLayer"/>),
    /// и окно в общем холсте оказывалось под ними (снимок 28 пользователя — портреты поверх свитка «Быстрого поединка»). У движка окно
    /// сообщения — отдельный диалог над экраном боя (<c>CMidgardMsgBox</c>).</summary>
    private CanvasLayer _windows = null!;

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        try
        {
            _strings = GameData.Strings(GameData.Language);
            var screen = GameData.Content.Screens.First(s => s.Id == Screen);
            // Широкий бой (16:9 и шире, как у врапера): разметка — на холст формы окна (WideBattle); уже — как в оригинале.
            var layout = new BattleLayout(PlayerIsAttacker, Flipped || (Standalone && args.Contains("--flip")),
                                          Wanted(Wide, args, "--wide-battle") ? WideBattle.Width(CanvasWidth()) : BattleLayout.ScreenWidth);
            screen = WideBattle.Place(screen, layout.Width);
            _host = new Vector2(screen.Design.Width, screen.Design.Height);
            var rules = Rules ?? RulesLoader.Load(GameData.Rules);
            var state = Prepared ?? Load(args, rules);
            _place = PlaceName ?? Place(args);
            if (Standalone && int.TryParse(MapSource.Arg(args, "--speed"), out int speed)) Speed = speed;
            if (Standalone && args.Contains("--still")) BattleAnim = false;
            _checks = MapSource.Arg(args, "--synth") is not null;
            // Начало хода следующего юнита — после показа прежнего действия (NextStep): действие → его анимации и надписи → тики → ход.
            _session = new BattleSession(state, new BattleSim(state, Rng ?? new EngineRng(), rules.Constants), rules.Constants,
                                         PlayerIsAttacker) { HoldTurnStart = true };
            _scene = new BattleScene(screen, GameData.Content.Gfx, BaseUnit(), DeathRace())
            {
                Layout = layout,
                MainShowsAttackers = !PlayerIsAttacker,   // до хода человека — группа врага
                Animated = BattleAnim,
                StillIdle = !BattleAnim || InstantSpeed,
                IsElf = GameData.Book.IsElf,   // вторая атака «лечение» у эльфов — без картинки на цели (GameBook.IsElf)
            };

            var viewport = new SubViewport
            {
                Size = new Vector2I((int)_host.X, (int)_host.Y), TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            _box = new SubViewportContainer { Name = "Field", Stretch = true, MouseFilter = Control.MouseFilterEnum.Ignore };
            _box.AddChild(viewport);
            AddChild(_box);
            _field = new BattleView
            {
                Name = "BattleView", Text = Text, Marks = BattleMarkSets.Of(GameData.Book), Variant = BackgroundVariant, NoteSeconds = NoteMs / 1000,
                LevelMarks = LevelMarks,
            };
            viewport.AddChild(_field);
            foreach (var unit in state.Units) _xpBefore[unit.Id] = unit.Xp;
            TurnPanel();
            _field.Active = _session.Current;
            _field.ActiveHuman = HumanTurn;
            _field.ActiveName = _session.Current is { } first ? NameOf(first) : "";
            TargetMarks();
            _field.Build(_scene, state, _place);

            _panel = new UiScreenView { Hosted = Screen, Name = Screen, Logic = new BattleButtons(() => _session, () => _scene, () => CanClose, () => PaperdollShown) };
            AddChild(_panel);
            _panel.Fired += Commands;
            _cursor = new AnimatedCursor { Name = "Cursor" };
            AddChild(_cursor);
            _cursor.ShowFrom(BattleCursors.Archive, BattleCursors.Default, BattleAnim);
            PanelBackgrounds();
            _overlay = new Node2D { Name = "Panels" };
            AddChild(_overlay);   // после панели: портреты и полосы здоровья идут поверх разметки
            _field.BuildPanels(_overlay, _scene, state);
            if (Wanted(QueueStrip, args, "--queue-strip")) AddQueueStrip();   // полоса очереди ходов — над панелями, под окнами
            if (_checks)   // метки при открытии боя: первым ходит призыватель человека — круги на клетках и рамки в панели уже есть
                GD.Print($"battle synth open ходит={_session.Current?.Id ?? "-"} клеток={_session.SummonSlots().Count} меток_поля={_field.EmptyMarks} " +
                         $"рамок_панели={_field.PanelFrames.Count(f => f.StartsWith("EMPTYSEL", StringComparison.OrdinalIgnoreCase))}");
            _windows = new CanvasLayer { Name = "Windows", Layer = 1 };
            AddChild(_windows);
            Place();
            GetViewport().SizeChanged += Place;
            _placed = true;
            // Бой от хозяина: первыми могут ходить юниты ИИ (защитник с большей инициативой) — доиграть их до хода человека.
            if (!Standalone) GameAudio.Of(this).BeginBattle();   // музыка боя — временная поверх музыки карты (docs/ai-screen.md §12)
            if (!Standalone && !_session.WaitsPlayer) Continue();
            else WarmNext();   // первый ход человека: замах ходящего и удар по целям — в фоне, пока он выбирает
            if (!Standalone) GD.Print($"battle open ходов_ИИ_до_игрока={_session.Log.Count} ждёт_игрока={(_session.WaitsPlayer ? 1 : 0)} " +
                                      $"предметы_нап={Items(state, attacker: true)} предметы_защ={Items(state, attacker: false)} значков={_field.ItemIcons} " +
                                      $"у_ходящего={BattleScene.ShownItems(_session.Current, HumanTurn).Count()} " +
                                      $"подпись={(_field.BigTextLines.FirstOrDefault() ?? "-").Replace(' ', '_')} имена={Names(state)}");
            if (Standalone && args.Contains("--check")) Failures.Run(this, () => RunCheck(MapSource.Arg(args, "--shot")), "BattleScreen.RunCheck");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException or InvalidOperationException)
        {
            GD.PrintErr($"BattleScreen: {e.GetType().Name}: {e.Message}");
            GetTree().Quit(1);
        }
    }

    public override void _ExitTree()
    {
        _exiting = true;   // продолжение показа конца боя при снятии — без сборки поля и «Закрыть»
        StopPlaying();
        DropQueueStrip();
        EndDone();   // экран сняли посреди показа конца боя — хозяин всё равно получает продолжение (ход ИИ не ждёт вечно)
        if (!Standalone) GameAudio.Of(this).EndTemporary(battle: true);
        _itemDrag = null;
        if (!_placed) return;   // до подписки (ранний отказ при загрузке) отписываться не от чего
        _placed = false;
        GetViewport().SizeChanged -= Place;
    }

    private bool _placed;

    /// <summary>Карточка юнита или предмета держится, пока нажата правая кнопка (прогон 3 пользователя 11.10, пункт 5; на карте так же —
    /// <c>MapScreen.Cards</c>): отпускание где угодно — над окном карточки, панелью или вне поля — закрывает её.</summary>
    public override void _Input(global::Godot.InputEvent @event)
    {
        if (_card is not null && @event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false })
        {
            Card(null);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(global::Godot.InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } right)
        {
            // Правая кнопка открывает карточку юнита (DLG_R_C_UNIT, 0x6399cf); отпускание закрывает (_Input). Тащение предмета
            // при этом отменяется.
            CancelItemDrag();
            if (_card is null && ItemAt(ToField(right.Position)) is { } itemSlot && _session.Current is { } bearer)
            {
                ItemCard(bearer.BattleItems[itemSlot]);   // слот предмета — карточка предмета (0x6327cb)
                return;
            }
            Card(UnitAt(ToField(right.Position)));
            return;
        }
        if (@event.IsActionPressed("ui_cancel")) { CancelItemDrag(); Card(null); return; }
        if (@event is InputEventMouseMotion motion && _itemDrag is not null)
        {
            // Тащение предмета — та же метка под живым юнитом под курсором, что без него (markAttackTargets 0x632b35 зовётся на каждое
            // движение мыши и при тащении — берёт цели предмета, 0x632bb9…0x632cf5); под костями павшего метки нет и у движка (обход юнитов
            // пропускает статусы «мёртв», «ушёл», «скрыт» — 0x632e26…0x632eb4). Партия 15.10, пометки 6 и 7: при тащении круга не было вовсе.
            DragOver(ToField(motion.Position));
            return;
        }
        // Метка под любым живым юнитом под курсором (markAttackTargets 0x632b35), на каждое движение мыши; над полосой SPOT — другая группа.
        if (@event is InputEventMouseMotion hover)
        {
            HoverAt(ToField(hover.Position));
            Spot(ToField(hover.Position));
            TargetCursor(ToField(hover.Position));
        }
        if (_playing || _message is not null) return;   // пока показывается действие или открыто сообщение, ход игрока не начинается
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press && _card is null
            && _session.WaitsPlayer && ItemAt(ToField(press.Position)) is { } slot)
        {
            StartItemDrag(slot);
            return;
        }
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } click) return;
        if (_card is not null) { Card(null); return; }
        if (!_session.WaitsPlayer) return;
        var at = ToField(click.Position);
        if (_itemDrag is not null)
        {
            // Позиция призыва — и на месте павшего (кости на ней не мешают, BattleSession.ItemSummonSlots); и местом в панели (0x64c393).
            if (_itemDrag is { } summoning && SummonCellAt(at, _session.ItemSummonSlots(summoning)) is { } empty) DropItemAt(empty);
            else if (UnitAt(at, withFallen: true) is { } dropOn && _itemDrag is { } dragged && !_session.ItemTargets(dragged).Contains(dropOn)
                     && _session.RefusalText(dropOn, dragged) is { } refusal)
            {
                CancelItemDrag();
                Message(refusal);
            }
            else DropItem(UnitAt(at, withFallen: true));
            return;
        }
        // Призыв человека — щелчок по позиции (BattleSession.SummonSlots): пустой или с костями павшего, на поле или местом в панели.
        if (_session.SummonSlots() is { Count: > 0 } cells)
        {
            if (SummonCellAt(at, cells) is { } cell && _session.Current is { } summoner) Attack(summoner, cell);
            return;
        }
        if (UnitAt(at, withFallen: true) is { } target) TryTarget(target);
    }

    private UiScreenView? _message;

    /// <summary>Снять окно поверх боя: <c>QueueFree</c> удаляет узел только в конце кадра, а до того окно ещё принимало бы ввод этого кадра —
    /// второй щелчок уходил бы в закрытое окно. Ввод выключается сразу.</summary>
    private static void CloseView(UiScreenView view)
    {
        view.SetProcessUnhandledInput(false);
        view.Visible = false;
        view.QueueFree();
    }

    /// <summary>Левый верхний угол окна меньше экрана боя — по середине 800×600 (как окна карты, <c>MapScreen.Centered</c>); во весь экран — null.</summary>
    private Vector2? Centered(string screen)
    {
        var design = GameData.Content.Screens.FirstOrDefault(s => s.Id == screen)?.Design;
        if (design is not { } d || (d.Width >= _host.X && d.Height >= _host.Y)) return null;
        return new Vector2(MathF.Floor((_host.X - d.Width) / 2f), MathF.Floor((_host.Y - d.Height) / 2f));
    }

    /// <summary>Щелчок по юниту: годная цель — удар; негодная — ничего, кроме двух сообщений (<see cref="BattleSession.RefusalText"/>,
    /// <c>0x63cce6</c>).</summary>
    private void TryTarget(BattleUnit target)
    {
        if (_session.CanTarget(target)) Attack(target);
        else if (_session.RefusalText(target) is { } refused) Message(refused);
    }

    /// <summary>Сообщение поверх боя (<c>DLG_MESSAGE_BOX</c>): одна «ОК», с <paramref name="yes"/> — вопрос «Да/Нет»; кнопка закрывает.</summary>
    private void Message(string textId, Action? yes = null)
    {
        if (_message is not null) CloseView(_message);
        var logic = new LastEdict.Data.Ui.Windows.MessageBoxLogic(Text(textId), yes);
        var view = new UiScreenView { Hosted = "DLG_MESSAGE_BOX", Name = "Message", Logic = logic, SlotHost = _host };
        if (Centered("DLG_MESSAGE_BOX") is { } at) view.SlotAt = at;   // окно 337×367 — по середине экрана боя 800×600, а не во всё окно
        view.Fired += _ =>
        {
            if (logic.TakeRequest() is not LastEdict.Data.Ui.Windows.UiClose) return;
            CloseView(view);
            if (ReferenceEquals(_message, view)) _message = null;
        };
        _message = view;
        _windows.AddChild(view);
        GD.Print($"battle message {textId}");
    }

    /// <summary>Карточка предмета из слота (<c>DLG_R_C_ITEM</c>, <see cref="ItemCardLogic"/>): в бою без цены; закрывается как карточка
    /// юнита.</summary>
    public void ItemCard(LastEdict.Rules.Magic.Item item)
    {
        Card(null);
        var texts = _itemTexts ??= LastEdict.Data.Ui.Windows.StackTexts.Load(GameData.Rules);
        _card = new UiScreenView
        {
            Hosted = LastEdict.Data.Ui.Windows.ItemCardLogic.Screen, Name = "Card",
            Logic = new LastEdict.Data.Ui.Windows.ItemCardLogic(Text, item, texts.Items.GetValueOrDefault(item.Id, ""), texts.ItemDescs?.GetValueOrDefault(item.Id, "") ?? ""),
            SlotHost = _host,
        };
        if (Centered(LastEdict.Data.Ui.Windows.ItemCardLogic.Screen) is { } at) _card.SlotAt = at;
        _windows.AddChild(_card);
        GD.Print($"battle item card {item.Id}");
    }

    private LastEdict.Data.Ui.Windows.StackTexts? _itemTexts;

    /// <summary>Карточка юнита поверх боя; <c>null</c> — закрыть.</summary>
    public void Card(BattleUnit? unit)
    {
        if (_card is not null)
        {
            CloseView(_card);
            _card = null;
        }
        if (_cardFace is not null)
        {
            _cardFace.QueueFree();   // текстура — общего кэша графики мира (CardPortrait): её отпускает GfxTextures.Release, не карточка
            _cardFace = null;
        }
        if (unit is null) return;
        // Бой с карты: карточка та же, что на карте (строитель CEncLayoutUnit общий, 0x5744f1) — опыт «N / до уровня», иммунитеты, стойкости,
        // атака по X005TA0787/0788, картина из Events рисует сам экран. Без хозяина (бой из журнала) — простая карточка ниже.
        if (CardLogic?.Invoke(unit) is { } shared)
        {
            _card = new UiScreenView { Hosted = LastEdict.Data.Ui.Windows.UnitCardLogic.Screen, Name = "Card", Logic = shared };
            _windows.AddChild(_card);
            return;
        }
        _card = new UiScreenView
        {
            Hosted = "DLG_R_C_UNIT", Name = "Card",
            // Названия источника атаки и зоны поражения — строки игры: X005TA0145.. по LattS (0 оружие … 7 земля),
            // X005TA0200 «Все поле боя» и X005TA0201 «Ближайшие цели» по LAttR. Строки источников движок берёт по имени
            // категории (0x61cd1b): у земли (6) — X005TA0152, у воздуха (7) — X005TA0151; «Ближайшие цели» — только у
            // L_ADJACENT (3), у L_ANY (2) тоже «Все поле боя» (0x575b80).
            Logic = new UnitCard(unit, UnitName(unit), Text,
                                 id => id is 6 or 7 ? Text(id == 6 ? "X005TA0152" : "X005TA0151")
                                       : id >= 0 && id <= 5 ? Text($"X005TA{145 + id:0000}")
                                       : Enum.IsDefined((LastEdict.Data.Content.Schema.AttackSource)id) ? ((LastEdict.Data.Content.Schema.AttackSource)id).ToString() : "",
                                 id => id == 3 ? Text("X005TA0201") : Text("X005TA0200")),
        };
        _windows.AddChild(_card);
        int before = GetChildCount();
        CardPortrait(unit);
        // Портрет карточки — тоже на слой окон (иначе он под карточкой): CardPortrait кладёт его ребёнком экрана, переносим всё, что добавилось.
        foreach (var added in GetChildren().Skip(before).OfType<CanvasItem>().ToList())
        {
            RemoveChild(added);
            _windows.AddChild(added);
        }
    }

    /// <summary>Картина юнита в карточке: кадр базового типа из <c>Events.ff</c> (350×450, <see cref="LastEdict.Data.Ui.Windows.UnitFaces.Picture"/>)
    /// своим размером по центру места <c>IMG_UNIT_ICON</c> (флаги 9 <c>setImage</c>, строитель <c>CEncLayoutUnit</c> общий с картой:
    /// <c>0x574feb</c> → <c>0x576d51</c>); картинка лежит в графике мира, а не в наборе кадров интерфейса.</summary>
    private void CardPortrait(BattleUnit unit)
    {
        var card = GameData.Content.Screens.FirstOrDefault(s => s.Id == "DLG_R_C_UNIT");
        if (card?.Elements.FirstOrDefault(e => e.Id == "IMG_UNIT_ICON")?.Rect is not { } slot) return;
        string name = BaseUnit()(unit.Type.Id).ToUpperInvariant();
        if (GameData.Content.Gfx.Image("Events", name) is not { } info) return;
        if (GfxAssets.PathOf(info, GameData.GfxRoot) is not { } path || !File.Exists(path)) return;
        if (GfxTextures.Of(path) is not { } texture) return;   // общий кэш графики мира: HD логического размера, не холст в логических точках
        // Без масштаба, по центру места: 350×450 в месте 330×430 — на 10 точек левее и выше угла, как у движка.
        const float k = 1f;
        var size = GetViewport().GetVisibleRect().Size;
        float view = Mathf.Min(size.X / _host.X, size.Y / _host.Y);
        _cardFace = new Sprite2D
        {
            Texture = texture, Centered = false, TextureFilter = ImageScales.Filter(texture, CanvasItem.TextureFilterEnum.Nearest),
            Position = ((size - (_host * view)) / 2f)
                       + (new Vector2(slot.X + ((slot.Width - (info.W * k)) / 2),
                                      slot.Y + ((slot.Height - (info.H * k)) / 2)) * view),
            Scale = new Vector2(k * view, k * view),
        };
        AddChild(_cardFace);
    }

    private Sprite2D? _cardFace;

    /// <summary>Открыта ли карточка (для проверок).</summary>
    public bool CardShown => _card is not null;

    /// <summary>Строка проверки открытой карточки юнита (шаг «battle card» экрана карты): общая с картой или простая, место табуляции и
    /// числа без разметки — табуляция «→», перевод строки «|», пробел «_».</summary>
    public string CardLine()
    {
        if (_card is not { } card) return "карточка=-";
        var stats = LastEdict.Data.Ui.UiTextStyle.Parse(card.State.TextOf("TXT_STATS"));
        string kind = card.Logic is LastEdict.Data.Ui.Windows.UnitCardLogic ? "общая" : "простая";
        string text = stats.Text.Replace('\t', '→').Replace('\n', '|').Replace(' ', '_');
        return $"карточка={kind} таб={stats.Tab} нет_кадров={(card.MissingFrames.Count > 0 ? string.Join(",", card.MissingFrames) : "-")} статы={text}";
    }

    /// <summary>Логика карточки юнита от хозяина (бой с карты — <see cref="LastEdict.Data.Ui.Windows.UnitCardLogic.InBattle"/>); null или ответ null —
    /// простая карточка <see cref="UnitCard"/>.</summary>
    public Func<BattleUnit, LastEdict.Data.Menu.IScreenLogic?>? CardLogic { get; set; }

    /// <summary>Значки повышения и высокого уровня у портретов панели (бой с карты — <see cref="LastEdict.Data.Ui.Windows.UnitCellsView.BattleLevelMarks"/>);
    /// null — без них.</summary>
    public Func<BattleUnit, (bool Upgrade, int HighLevel)>? LevelMarks { get; set; }

    private UiScreenView? _card;

    /// <summary>Имя юнита в подписи крупного портрета — <c>CMidUnit::getName</c> (<c>0x5e97dc</c>, вызов <c>0x64f864</c>): собственное у
    /// героя (<see cref="OwnName"/>), у прочих — имя типа. Раньше прочим шло пустое имя — у рядовых в подписи было одно «ОЗ» (прогон 14.10,
    /// пометка 2).</summary>
    private string NameOf(BattleUnit unit) => OwnName(unit.Id, unit.Type.Id) ?? UnitName(unit);

    /// <summary>Имена всех юнитов боя для проверок и журнала зонда — какой была бы подпись крупного портрета у каждого: «id:имя» через
    /// запятую, пробелы — «_», пустое имя — «-».</summary>
    private string Names(BattleState state) =>
        string.Join(",", state.Units.Select(u => $"{u.Id}:{(NameOf(u) is { Length: > 0 } name ? name.Replace(' ', '_') : "-")}"));

    /// <summary>Имя типа юнита из таблиц порта (<c>Gunits.NAME_TXT</c> → строка локализации).</summary>
    private string UnitName(BattleUnit unit) =>
        GameData.Content.MapArt.Unit(unit.Type.Id) is { NameTxt.Length: > 0 } look
        && _strings.TryGetValue(look.NameTxt, out string? name) ? name : unit.Type.Id;

    private IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>();

    /// <summary>Разметка оформления (\hC;, \fLarge; и прочее) в надписях поля не разбирается — убираем.</summary>
    private static string Strip(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\\[a-zA-Z]+[^;]*;", "")
            .Replace(@"\\n", "\n");

    /// <summary>Строка локализации по id; нет такой — пустая.</summary>
    private string Text(string id) => _strings.TryGetValue(id, out string? value) ? value : "";

    /// <summary>Строка проверки: боевые предметы лидеров стороны на поле (не больше двух слотов), через запятую; нет — «-».</summary>
    private static string Items(BattleState state, bool attacker) =>
        string.Join(",", state.Units.Where(u => u.Attacker == attacker && u.OnField)
                                    .SelectMany(u => u.BattleItems.Take(2).Select(i => i.Id))) is { Length: > 0 } ids ? ids : "-";

    /// <summary>Ходит юнит человека (набор целей от сервера приходит только хозяину ходящего, <c>data->targetData</c> <c>+0xfa0</c>,
    /// <c>0x638425</c>): только тогда показываются боевые предметы ходящего лидера (<see cref="BattleScene.ShowsItems"/>).</summary>
    private bool HumanTurn => _session.Current is { } unit && _session.IsPlayers(unit);

    /// <summary>Слот боевого предмета ходящего под точкой; null — не по слоту.</summary>
    public int? ItemAt(Vector2 at)
    {
        if (_session.Current is not { } unit) return null;
        // предметы — только у ходящего лидера-человека (0x649775); применённый — уже не в слоте (0x622fee)
        foreach (int i in BattleScene.ShownItems(unit, HumanTurn))
        {
            var slot = _scene.ItemSlot(unit.Attacker, i);
            if (at.X >= slot.X && at.Y >= slot.Y && at.X < slot.X + slot.Width && at.Y < slot.Y + slot.Height) return i;
        }
        return null;
    }

    /// <summary>Предмет, который тащат из слота; не тащат — null.</summary>
    private int? _itemDrag;

    /// <summary>
    /// Нажатие над слотом предмета начинает тащение сразу, без порога (<c>CBatItemDropSource</c> <c>0x658139</c>): предмет — под курсором,
    /// метки — на целях предмета (<see cref="BattleSession.ItemTargets"/>, <c>0x63460d</c>). Щелчком предмет не применяется.
    /// </summary>
    public void StartItemDrag(int index)
    {
        if (_session.Current is not { } unit || index >= unit.BattleItems.Count) return;
        _itemDrag = index;
        string item = ArtNames.Of(unit.BattleItems[index].Id);
        _dragImage = GameData.Content.Gfx.Image("IconItem", item) is { } info && GfxAssets.PathOf(info, GameData.GfxRoot) is { } path
                     && File.Exists(path) ? GfxTextures.Canvas(path) : null;
        _dragItem = item;
        DragCursor(null);
        Refresh();
        GD.Print($"battle item drag {index} {item} целей={_session.ItemTargets(index).Count} рамка={_field.MarkKind} нет_кадров={_scene.Missing.Count} " +
                 $"курсор={_cursor.Current ?? "-"}:{_cursor.Frames}");
    }

    private Image? _dragImage;
    private string _dragItem = "";
    private AnimatedCursor _cursor = null!;

    /// <summary>Курсор тащимого предмета (<c>0x6580e3</c>): значок предмета на подложке — над годной целью <c>CUATTACK</c>, иначе
    /// <c>CUATTACKN</c>, анимированные «…A» — по «дополнительной анимации боя», как прочие курсоры боя (общий <see cref="AnimatedCursor"/>:
    /// анимация, обрезка под предел Godot).</summary>
    private void DragCursor(BattleUnit? over)
    {
        if (_itemDrag is not { } index || _dragImage is null) return;
        bool allowed = over is not null && _session.ItemTargets(index).Any(u => ReferenceEquals(u, over));
        _cursor.ShowWith("Battle", allowed ? "CUATTACK" : "CUATTACKN", _dragImage, _dragItem, BattleAnim);
    }

    /// <summary>Мышь над точкой поля при тащении предмета: метка под живым юнитом под курсором (<see cref="BattleView.Hover"/>) и курсор
    /// предмета над годной целью — и над павшим (зелье жизни).</summary>
    public void DragOver(Vector2 at)
    {
        _pointer = at;
        _field.Hover(UnitAt(at));
        DragCursor(UnitAt(at, withFallen: true));
    }

    /// <summary>Своя пустая позиция под точкой (цель призыва; зона мыши места — <see cref="BattleScene.HitSlot"/>, <c>0x637405</c>); нет — null.
    /// Занятой считается и вторая клетка большого (<see cref="BattleUnit.Covers"/>). Скрытого (дуэль вора) клетка здесь «пуста», а для призыва
    /// занята (<see cref="BattleState.SummonCells"/>) — годность решает список целей у вызывающего (<see cref="SummonCellAt"/>).</summary>
    private int? EmptySlotAt(Vector2 at)
    {
        if (_session.Current is not { } unit) return null;
        for (int s = 0; s < LastEdict.Rules.Units.Formation.Cells; s++)
        {
            var r = _scene.HitSlot(unit.Attacker, s);
            if (at.X >= r.X && at.Y >= r.Y && at.X < r.X + r.Width && at.Y < r.Y + r.Height
                && !_session.State.Units.Any(u => u.Attacker == unit.Attacker && u.OnField && u.Covers(s))) return s;
        }
        return null;
    }

    /// <summary>Позиция призыва под точкой из <paramref name="cells"/> (цели призыва ходящего или сферы): место в показанной панели своей стороны
    /// (панель лежит поверх поля; <c>getSelectedUnitId</c> панели <c>0x64c393</c> сперва ловит позиции-цели — щелчок по месту в панели
    /// выбирает позицию, <see cref="BattleScene.PanelPositionAt"/>) или клетка поля (<see cref="EmptySlotAt"/>); не цель — null. Партия 17.10:
    /// «ЛКМ по слоту в меню слева игнорируется и туда не призываются элементали — нужно именно на пол наводиться».</summary>
    private int? SummonCellAt(Vector2 at, IReadOnlyList<int> cells)
    {
        if (_session.Current is not { } unit || cells.Count == 0) return null;
        if (_scene.PanelPositionAt(unit.Attacker, (int)MathF.Floor(at.X), (int)MathF.Floor(at.Y)) is { } placed)
            return cells.Contains(placed) ? placed : null;
        return EmptySlotAt(at) is { } cell && cells.Contains(cell) ? cell : null;
    }

    /// <summary>Кто стоит на позиции <paramref name="slot"/> стороны ходящего после призыва (для журнала): id живого или «-».</summary>
    private string SummonedAt(BattleUnit? summoner, int slot) =>
        summoner is null ? "-"
        : _session.State.Units.FirstOrDefault(u => u.Attacker == summoner.Attacker && u.OnField && u.Summoned && u.Covers(slot))?.Id ?? "-";

    /// <summary>Предмет-призыв брошен на пустую позицию: призванный встаёт на неё; не цель — предмет возвращается.</summary>
    public void DropItemAt(int slot)
    {
        if (_itemDrag is not { } index) return;
        _itemDrag = null;
        TargetCursor(_pointer);
        int uses = _session.ItemUses.Count;
        BeforeAction();
        bool used = _session.UseItemAt(index, slot);
        GD.Print($"battle item drop {index} на=позиция{slot} применён={(used ? 1 : 0)}");
        if (used && _session.ItemUses.Count > uses) ShowItem(_session.ItemUses[^1]);
        else Refresh();
    }

    /// <summary>Рамка целей в панели у обычной атаки ходящего: поддерживающая (<see cref="BattleScene.SupportFrame"/>) — <c>HEA</c>, иначе
    /// <c>SEL</c> (<c>0x630401</c>/<c>0x630417</c>).</summary>
    private string AttackMarkKind() => _session.Current is { } unit && BattleScene.SupportFrame(unit.Attack.Class) ? "HEA" : "SEL";

    /// <summary>Атака ходящего бьёт всю группу — рамка <c>*ALL</c> на сторону целей (<c>0x630104</c>).</summary>
    private bool TargetsAll() => _session.Current is { } unit && unit.Attack.Reach == AttackType.ReachAll;

    /// <summary>Рамка целей тащимого предмета (<c>0x63460d</c>): зелья — всегда <c>ISEL</c> (у них нет атаки); сфера и талисман —
    /// <c>HEA</c>, если класс атаки поддерживающий (<c>0x63086e</c>), иначе <c>ISEL</c>.</summary>
    private string ItemMarkKind(int index)
    {
        if (_session.Current is not { } unit || index >= unit.BattleItems.Count) return "ISEL";
        var item = unit.BattleItems[index];
        return !item.IsPotion && _session.State.Rules?.Attacks.GetValueOrDefault(item.AttackId) is { } attack
               && BattleScene.SupportFrame(attack.Class) ? "HEA" : "ISEL";
    }

    /// <summary>Тащение предмета отменено (правая кнопка, Esc, кнопка панели, выход): предмет — в слот, курсор — обычный.</summary>
    private void CancelItemDrag()
    {
        if (_itemDrag is null) return;
        _itemDrag = null;
        TargetCursor(_pointer);
        if (_placed) Refresh();
    }

    /// <summary>Отпускание тащимого предмета (<c>0x56c2a9</c>): над годной целью — действие 6 (<c>notifyUseItem</c>), мимо — предмет
    /// возвращается в слот, серверу ничего (<c>0x56c606</c>).</summary>
    public void DropItem(BattleUnit? target)
    {
        if (_itemDrag is not { } index) return;
        _itemDrag = null;
        TargetCursor(_pointer);
        int uses = _session.ItemUses.Count;
        BeforeAction();
        bool used = target is not null && _session.UseItemOn(index, target);
        GD.Print($"battle item drop {index} на={(target is null ? "-" : target.Id)} применён={(used ? 1 : 0)}");
        if (used && _session.ItemUses.Count > uses) ShowItem(_session.ItemUses[^1]);   // показ, звуки и надписи — BattleScreen.Items.cs
        else Refresh();
    }

    /// <summary>Юнит под точкой: портрет в показанной боковой панели (<see cref="BattleScene.PanelUnitAt"/> — панель лежит поверх поля,
    /// поиск цели движка идёт по группам панелей, <c>0x631318</c>) или зона мыши его места на поле (<see cref="BattleScene.FieldUnitAt"/>:
    /// место и 32 px под ним, <c>0x6326b9</c>); <paramref name="withFallen"/> — и павшие (цель зелья воскрешения).</summary>
    public BattleUnit? UnitAt(Vector2 at, bool withFallen = false) =>
        // Портрет павшего в панели — тоже цель (зелье жизни, воскрешение): движок ставит цель броска предмета на каждую позицию из набора
        // целей предмета — и на поле, и в панели (0x63885e, 0x63911f → 0x658890; набор зелья жизни — павшие). Партия 15.10, пометка 7:
        // эликсир жизни через портрет павшего не применялся (панель искала только живых). Живой на клетке павшего — раньше (BattleScene.UnitAt).
        _scene.UnitAt(_session.State.Units, (int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), withFallen);

    /// <summary>Удар по цели: удар показывается (<see cref="Animate"/>), дальше ход идёт сам, пока не дойдёт до игрока.</summary>
    public void Attack(BattleUnit target, int? summonSlot = null)
    {
        double started = Now;
        var attacker = _session.Current;
        var before = StrikeNotes.Before(_session.State, attacker);
        int allies = _session.Sim.AllyLog.Count, drains = _session.State.DrainHeals.Count;
        var had = UnitIds();
        BeforeAction();
        var records = summonSlot is { } slot ? _session.SummonAt(slot) : _session.Attack(target);
        if (summonSlot is { } placed) GD.Print($"battle summon {attacker?.Id ?? "-"} на=позиция{placed} призван={SummonedAt(attacker, placed)}");
        IReadOnlyList<AllyNote> gained = [.. _session.Sim.AllyLog.Skip(allies)];
        IReadOnlyList<DrainHeal> healed = [.. _session.State.DrainHeals.Skip(drains)];
        // Удар без записей хода (призыв, превращение себя) — всё равно замах ходящего и цель; у призыва цель показа — призванный
        // (ShowOrder.SummonTargets, 0x63c902).
        IReadOnlyList<TurnRecord> shown = records.Count > 0 || attacker is null
            ? records
            : ShowOrder.SummonTargets([new TurnRecord(_session.State.Round, attacker.Id, target.Id, false, 0, target.Hp)], AddedSince(had));
        _playing = true;
        // Звуки — по показанным записям: у призыва записей нет, а замах (ELEMENTALIST) и удар (SUMMON → G000SS0008) у движка звучат
        // (журнал 20261016g-port-synth, карта 8: 8 призывов — 8 замахов и 8 G000SS0008; у порта до 17.10 — тишина).
        double length = Animate(shown, part => Numbers(records, before, gained, healed, shown, part));
        Timing(attacker?.Id ?? "-", 0, Now - started, length, 0);
        Later(length, () =>
        {
            _playing = false;
            Continue();
        });
    }

    /// <summary>Надписи удара на портретах целей (<c>0x63b391</c>) — <see cref="StrikeNotes"/>: защита, промах, строка класса, урон и крит
    /// по потерянному здоровью, строки атак по своим (<see cref="BattleSim.AllyLog"/>). На каждом ударе, и ИИ тоже, основная панель
    /// встаёт на группу первой цели (<c>0x630e6a</c> → <c>0x630921</c>, <c>TOG_RIGHTUNITS</c>; <c>0x638298</c>): надписи — только в ней.
    /// Первая цель — и во второй крупный портрет (<c>0x630f15</c> → <c>0x64f149</c>, <see cref="_focus"/>).
    /// <paramref name="part"/> — номер атаки шага просмотра: у движка вторая атака — свой шаг после первой (<c>bat_show</c> с <c>attack</c> 2
    /// после <c>bat_show_end</c> первой, <c>0x634dd2</c>), и её надписи и вспышки — в нём (партия 20.10 <c>corpus/20261020-faces</c>: «−38» и
    /// красный, через 650 мс «Яд» и оливковый; у порта обе надписи и обе вспышки ложились разом — вторая гасила первую). 1 — первая атака и всё
    /// прочее шага (звуки, атаки по своим первой атаки, вампиризм, ходы без удара), 2 — только вторая атака; null — всё сразу (бой без показа).</summary>
    private void Numbers(IReadOnlyList<TurnRecord> records, StrikeNotes before, IReadOnlyList<AllyNote> allies, IReadOnlyList<DrainHeal> drains,
                         IReadOnlyList<TurnRecord>? sounded = null, int? part = null)
    {
        bool Mine(int attack) => part is not { } p || (p == 2) == (attack == 2);
        if (part != 2) Sounds(sounded ?? records);   // звуки действий — BattleScreen.Sound.cs (сессия Г)
        var hits = records.Where(r => Mine(r.Attack)).ToList();
        var mine = allies.Where(a => Mine(a.Attack)).ToList();
        var heals = part == 2 ? [] : drains;
        // Цель показа без записей хода (призыв, превращение себя — ShowOrder.SummonTargets/Unrecorded) — тоже первая цель: у призыва основная
        // панель встаёт на группу призванного (у движка bat_flash 28 призванного — on = true; у порта до 20.10 панель оставалась на врагах).
        var shownOnly = (sounded ?? []).Where(r => Mine(r.Attack) && !records.Any(x => SameRecord(x, r))).ToList();
        string? first = hits.Count > 0 ? hits[0].Target : mine.Count > 0 ? mine[0].Target : shownOnly.Count > 0 ? shownOnly[0].Target : null;
        // Вампиризм — «+N» на группе бьющего после надписей целей (0x63c27d); видна, если эта группа в основной панели (BattleView.Note).
        // Вспышки портретов (0x64cee8) — у каждой цели, и где надписи нет (урон 0): по записям модели, атак по своим и вампиризма; у шага без
        // записей (призыв) — по показанной цели: вспышка призыва (0x63bf8e) ложится на призванного. Атаки по своим (усиление, дар атаки,
        // превращение себя) — только по журналу атак по своим: прежде их вспышка шла ещё и по показанной записи (дважды; у превращения себя —
        // красная, по атаке уже превращённого).
        var summons = shownOnly.Where(r => _session.State.Unit(r.Target) is { Summoned: true } s && r.Unit.Equals(s.SummonerId, StringComparison.OrdinalIgnoreCase)
                                           && !mine.Any(a => a.Target.Equals(r.Target, StringComparison.OrdinalIgnoreCase)))
                               .Select(r => (r.Target, new BattleFlash(BattleFace.Summoned, true)));
        ShowNotes(first, before.After(hits, _session.State, Text).Concat(StrikeNotes.Allies(mine, Text)).Concat(StrikeNotes.Drains(heals, Text)),
                  before.Faces(hits, _session.State).Concat(StrikeFaces.AllyFaces(mine)).Concat(StrikeFaces.DrainFaces(heals)).Concat(summons),
                  clear: part != 2);   // списки проверок — за весь шаг: вторая атака их не стирает
        if (part != 2) ShowTurns();   // «Защита» ИИ — шаг без картинок: надпись вместе со звуком (Animate ждёт её снятия)
    }

    private static bool SameRecord(TurnRecord a, TurnRecord b) =>
        a.Attack == b.Attack && a.Unit.Equals(b.Unit, StringComparison.OrdinalIgnoreCase) && a.Target.Equals(b.Target, StringComparison.OrdinalIgnoreCase);

    /// <summary>Надписи на портретах (<see cref="Numbers"/>): основная панель и крупный портрет — на группу первой цели
    /// <paramref name="first"/>, затем каждая надпись — в журнал зонда и на портрет, затем вспышки <paramref name="flashes"/>
    /// (<see cref="BattleView.Flash"/>).</summary>
    private void ShowNotes(string? first, IEnumerable<StrikeNote> notes, IEnumerable<(string Target, BattleFlash Flash)>? flashes = null,
                           bool clear = true)
    {
        if (clear)
        {
            _field.NoteLines.Clear();
            _field.NoteWraps.Clear();
        }
        if (first is not null && _session.State.Unit(first) is { } shown)
        {
            bool moved = !ReferenceEquals(shown, _focus) || _scene.MainShowsAttackers != shown.Attacker;
            (_scene.MainShowsAttackers, _focus) = (shown.Attacker, shown);
            if (moved && !Instant) RefreshPanels();   // портрет и панель — в миг удара; без показа их соберёт Refresh в конце
        }
        foreach (var note in notes)
            if (_session.State.Unit(note.Target) is { } target)   // и павший призванный — он уже снят с поля (BattleState.Gone)
            {
                _field.Note(_scene, target, note.Markup);   // и журнал зонда: надпись удара, как bat_text движка (0x64d09c)
            }
        foreach (var (id, flash) in flashes ?? [])
            if (_session.State.Unit(id) is { } target) _field.Flash(_scene, target, flash);
    }

    /// <summary>«Защищаться» или «Ждать» человека: надпись и вспышка на портрете ходящего (<see cref="ShowTurns"/>, шаги 9 и 10), затем ход
    /// идёт дальше — после снятия надписи, как после шага просмотра движка.</summary>
    private void ContinueAfterTurnShow()
    {
        double length = ShowTurns();
        if (Instant || length <= 0)
        {
            Continue();
            return;
        }
        _playing = true;
        Later(length, () =>
        {
            _playing = false;
            Continue();
        });
    }

    /// <summary>Довести бой до хода игрока (или до конца), если сейчас ходит ИИ либо включён автобой: по одному действию, каждое
    /// показывается (<see cref="Animate"/>); без окна — разом.</summary>
    public void Continue(int limit = 200)
    {
        if (_playing) return;   // показ уже идёт — он сам доведёт бой до игрока
        if (!Instant)
        {
            _playing = true;
            NextStep(limit);
            return;
        }
        while (limit-- > 0)
        {
            _session.StartTurn();   // начало хода следующего (отложено сессией) — со своими надписями и звуками
            Pending(0);
            if (_session.Finished || _session.WaitsPlayer) break;
            AiStep().Numbers(null);
        }
        Refresh();
    }

    /// <summary>Ход ИИ одним действием: записи действия и его надписи со звуками — их ставит показ в свой миг.</summary>
    private (IReadOnlyList<TurnRecord> Records, Action<int?> Numbers) AiStep()
    {
        int from = _session.Log.Count, allies = _session.Sim.AllyLog.Count, drains = _session.State.DrainHeals.Count;
        int acts = _session.State.Actions.Count;
        var before = StrikeNotes.Before(_session.State, _session.Current);
        var had = UnitIds();
        BeforeAction();
        _session.AiStep();
        IReadOnlyList<TurnRecord> records = [.. _session.Log.Skip(from)];
        IReadOnlyList<AllyNote> gained = [.. _session.Sim.AllyLog.Skip(allies)];
        IReadOnlyList<DrainHeal> healed = [.. _session.State.DrainHeals.Skip(drains)];
        // Шаг без записей хода (призыв, усиление) — замах и звуки, как у такого же хода человека (Attack; ShowOrder.Unrecorded):
        // надписи — по записям модели, показ и звук — по показанным; у призыва цель показа — призванный (ShowOrder.SummonTargets).
        IReadOnlyList<TurnRecord> shown = records.Count > 0 ? records
            : ShowOrder.SummonTargets(ShowOrder.Unrecorded(_session.State.Actions.Skip(acts), id => _session.State.Unit(id)?.Hp ?? 0),
                                      AddedSince(had));
        return (shown, part => Numbers(records, before, gained, healed, shown, part));
    }

    /// <summary>Id юнитов боя сейчас — снимок до действия для <see cref="AddedSince"/>.</summary>
    private HashSet<string> UnitIds() => new(_session.State.Units.Select(u => u.Id), StringComparer.OrdinalIgnoreCase);

    /// <summary>Юниты, которых не было в снимке <paramref name="had"/> (призванные действием).</summary>
    private List<BattleUnit> AddedSince(HashSet<string> had) => [.. _session.State.Units.Where(u => !had.Contains(u.Id))];

    private void Commands(IReadOnlyList<UiEvent> events)
    {
        CancelItemDrag();   // кнопка панели во время тащения — предмет возвращается в слот
        if (_message is not null)
        {
            PanelBackgrounds();   // окно сообщения модально (CMidgardMsgBox): панель под ним не слушает, отметки — прежние
            return;
        }
        foreach (var e in events)
        {
            // Пока показывается действие, команды хода ждут (кнопки и так погашены — SyncButtons; это запасная защита); «Быстрый бой»
            // обрывает показ и доигрывает бой.
            if (_playing && e is UiClicked { Element: "BTN_DEFEND" or "BTN_WAIT" or "BTN_RETREAT" }) continue;
            switch (e)
            {
                // Запись действия для проверки порядка — только принятого: начало следующего хода отложено (HoldTurnStart), тиков
                // в самом действии нет, и счёт тиков после него тот же, что до.
                case UiClicked { Element: "BTN_DEFEND" }: if (_session.Defend()) BeforeAction(); ContinueAfterTurnShow(); break;
                case UiClicked { Element: "BTN_WAIT" }: if (_session.Wait()) BeforeAction(); ContinueAfterTurnShow(); break;
                case UiClicked { Element: "BTN_RETREAT" }: if (_session.Retreat()) BeforeAction(); Continue(); break;
                case UiClicked { Element: "BTN_RESOLVE" }: AskResolve(); break;
                case UiClicked { Element: "BTN_CLOSE" } or UiClicked { Element: "BTN_PAPERDOLL" }:
                    // В игре после боя остаются «Закрыть» и «Меню героя» (docs/battle-screen.md §4): closeBattleWindow(false/true)
                    // 0x633e0b/0x633e20; «Меню героя» — хозяин после итога боя откроет окно нападавшего отряда (0x4d856d).
                    if (!CanClose) break;   // конец боя ещё показывается
                    if (e is UiClicked { Element: "BTN_PAPERDOLL" })
                    {
                        if (!PaperdollShown) break;
                        Paperdoll = true;
                    }
                    Closed?.Invoke();
                    if (Standalone) GetTree().Quit(0);
                    break;
                case UiToggled { Element: "TOG_AUTOBATTLE" } toggle:
                    _session.SetAuto(AutoSide, toggle.Checked);
                    Continue();
                    break;
                case UiToggled { Element: "TOG_RIGHTUNITS" }:
                    // Основная панель: другая группа (снимок 28.09 — после переключателя своя группа в той же правой панели). Пока видны
                    // обе панели, стороны групп не меняются (BattleScene.PanelLeft) — переключатель скажется, когда панель одна.
                    _scene.MainShowsAttackers = !_scene.MainShowsAttackers;
                    RefreshPanels();
                    break;
            }
        }
    }

    /// <summary>«Быстрый поединок» (<c>BTN_RESOLVE</c>, <c>0x634244</c>, <c>docs/battle-screen.md</c> §5.2): вопрос <c>X005TA0922</c>, «да» —
    /// обрывает показ действия и досчитывает бой (бой двух людей с сообщением <c>X005TA0926</c> вырезан с «горячим стулом», Р33)
    /// (<see cref="BattleSession.Resolve"/>; у движка ответ на чужом ходу ждёт ближайшего <c>update</c>, <c>0x6344aa</c>, — итог тот же).</summary>
    private void AskResolve()
    {
        if (_session.Finished) return;
        Message(ResolveAsk, () =>
        {
            StopPlaying();
            _session.Resolve();
            GD.Print($"battle resolve раундов={_session.Round} победитель={_session.Winner ?? "-"}");
            Refresh();
        });
    }

    public const string ResolveAsk = "X005TA0922";

    /// <summary>Чей автобой показывает и переключает <c>TOG_AUTOBATTLE</c> (<c>data2+0x3a</c>): сторона игрока.</summary>
    private bool AutoSide => PlayerIsAttacker;

    /// <summary>Доиграть бой автобоем и закрыть его — для проверок хозяина без окна; <paramref name="paperdoll"/> — закрыть «Меню героя»,
    /// если оно есть (<see cref="PaperdollShown"/>), иначе «Закрыть».</summary>
    public void AutoFinish(bool paperdoll = false)
    {
        StopPlaying();
        _session.Auto = true;
        _session.PlayOut();   // автобой, не «Быстрый бой»: предела раундов BAT_ROUND у боя с человеком нет
        Refresh();
        _autoPaperdoll = paperdoll;
        // Хозяин показывает конец боя (EndReached → PlayEnd → ShowEnd) — закрыть по его концу, как «Закрыть» человека.
        if (CanClose) AutoClose();
        else _closeWhenShown = true;
    }

    /// <summary>Закрыть бой проверки: «Меню героя», если просили и оно есть, иначе «Закрыть».</summary>
    private void AutoClose()
    {
        Paperdoll = _autoPaperdoll && PaperdollShown;
        Closed?.Invoke();
    }

    private bool _autoPaperdoll;

    /// <summary>Закрыть бой, как только его конец показан (<see cref="AutoFinish"/>).</summary>
    private bool _closeWhenShown;

    /// <summary>Бой кончился: над выжившими
    /// показывается полученный опыт — в игре это всплывающий текст <c>X008TA0005</c>
    /// (<c>docs/battle-screen.md</c> §4; окна итогов у боя нет).</summary>
    private void Ended()
    {
        if (_ended || !_session.Finished) return;
        _ended = true;
        // Конец боя модели (предметы, превращения, призванные, опыт и рост) у боя с карты делает хозяин — GameSession.EndBattle, после
        // счёта убийств (иначе призванные снимаются раньше и опыт за них теряется); сам экран — только без хозяина. Хозяин подписан на
        // EndReached — конец боя показывается здесь же, до «Закрыть» (шаги 13–16 просмотра 0x630a7b, затем battleEnd 0x630a3c создаёт
        // BTN_CLOSE; замечание 33 прогона 11.10): он зовёт PlayEnd, а по его концу — ShowEnd (опыт и кнопки).
        if (_synthHp is not null)
        {
            Callable.From(SynthEnd).CallDeferred();   // проверка F: конец боя, как у хозяина (BattleScreen.Synth.cs)
            return;
        }
        if (Standalone) _session.Sim.AfterBattle();
        if (!Standalone && EndReached is not null)
        {
            Callable.From(() => EndReached?.Invoke()).CallDeferred();   // не из середины сборки поля и шага показа
            return;
        }
        XpNotes();
    }

    /// <summary>Бой кончился (после ходов лекарей): хозяин считает конец боя модели и показывает его на этом экране (<see cref="PlayEnd"/>,
    /// затем <see cref="ShowEnd"/>). Пока он не показан, «Закрыть» и «Меню героя» скрыты.</summary>
    public event Action? EndReached;

    /// <summary>Конец боя показан: «Опыт: +N» над получившими опыт и кнопки «Закрыть»/«Меню героя».</summary>
    public void ShowEnd()
    {
        if (_endShown || _exiting || !IsInsideTree()) return;
        _endShown = true;
        var gains = XpNotes();
        _playing = false;   // PlayEnd оборвал хвост последнего шага показа (его Later) — поле и панели собрать здесь
        Refresh();
        GD.Print($"battle end shown опыт={(gains.Count == 0 ? "-" : string.Join(",", gains))} портреты={ExperienceFaces()} стойких={_field.SteadyNotes}");
        if (_closeWhenShown) AutoClose();
    }

    /// <summary>Портреты после опыта конца боя: «юнит=состояние@точка_середины» у выживших в панелях — для проверок (<c>ui-render-check</c>).</summary>
    private string ExperienceFaces() => string.Join(",", _session.State.Units.Where(u => u.OnField)
        .Select(u => $"{u.Id}={_field.FaceOf(u)}@{_field.FaceCenters.GetValueOrDefault(u.Id, "-")}"));

    private bool _endShown, _exiting;

    /// <summary>Можно закрыть бой: он кончился и его конец показан (без хозяина-подписчика — сразу).</summary>
    public bool CanClose => _session.Finished && (Standalone || _endShown || EndReached is null);

    /// <summary>«Меню героя» видно: бой можно закрыть, и кнопку создал бы движок (<see cref="LastEdict.Data.Play.MapBattle.PaperdollOffered"/>).</summary>
    public bool PaperdollShown => CanClose && LastEdict.Data.Play.MapBattle.PaperdollOffered(_session.State, OwnsAttackerStack);

    /// <summary>Опыт конца боя (шаг 15 просмотра, <c>0x6369ee</c> → <c>0x64d4a4</c> на все четыре группы): у выросшего (сообщение
    /// <see cref="BattleNoteKind.LevelUp"/>, список роста <c>0x62c2dd</c>) надписи нет — вспышка светло-жёлтым (<see cref="BattleFace.Experience"/>,
    /// <c>0x64d609</c>) и затем портрет без подкраски и ран (<see cref="BattleFace.Plain"/>, <c>0x64d5f0</c>); у прочих, кто получил опыт
    /// (<c>+0x10</c> записи группы &gt; 0; ушедшие — нет, <c>0x64d520</c>), — «Опыт: +N» (<c>X008TA0005</c>, <c>0x64d620</c>) и светло-жёлтым до
    /// закрытия боя (<c>0x64d637</c>: поставленное состояние, пересборка панелей его держит). Опыт юнитов боя пишет конец боя модели хозяина.</summary>
    private List<string> XpNotes()
    {
        var gains = new List<string>();
        var grown = _session.State.Notes.Where(n => n.Kind == BattleNoteKind.LevelUp).Select(n => n.UnitId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in _session.State.Units)
        {
            if (!unit.OnField) continue;
            if (grown.Contains(unit.Id))
            {
                _field.FixFace(unit, BattleFace.Plain);
                _field.Flash(_scene, unit, new BattleFlash(BattleFace.Experience, false), anyPanel: true);
                gains.Add(FormattableString.Invariant($"{unit.Id}:рост"));
                continue;
            }
            int gained = unit.Xp - _xpBefore.GetValueOrDefault(unit.Id);
            if (gained <= 0) continue;
            // X008TA0005 — строка «Опыт: +N» из данных игры.
            _field.Note(_scene, unit, Text("X008TA0005").Replace("%GAIN%", gained.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        anyPanel: true, src: "xp", persist: true);
            _field.FixFace(unit, BattleFace.Experience);
            gains.Add(FormattableString.Invariant($"{unit.Id}+{gained}"));
        }
        return gains;
    }

    private bool _ended;
    private readonly Dictionary<string, int> _xpBefore = new(StringComparer.OrdinalIgnoreCase);

    private BattleUnit? _turnOf;

    /// <summary>Юнит второго крупного портрета (<see cref="BattleView.Focus"/>): под мышью (<c>onMouseMove</c> <c>0x6312a4</c> →
    /// <c>markAttackTargets(…, setBigFace = 1)</c>: живой юнит на поле или портрет в панели) или первая цель показанного действия
    /// (<c>showAttackEffect</c> <c>0x630f15</c>). Не сбрасывается: держит последнего.</summary>
    private BattleUnit? _focus;

    /// <summary>Второй крупный портрет — <see cref="_focus"/>.</summary>
    private void FocusFace()
    {
        _field.Focus = _focus;
        _field.FocusName = _focus is { } focus ? NameOf(focus) : "";
    }

    /// <summary>Мышь над юнитом — он во втором крупном портрете. Пока показывается действие, ввод заперт (<c>0x6311c4</c>: разбор мыши
    /// только при опущенном <c>data+0x14f9</c>).</summary>
    private void FocusAt(Vector2 at)
    {
        if (_playing && !_session.WaitsPlayer || HoveredAt(at) is not { } over || ReferenceEquals(over, _focus)) return;
        _focus = over;
        RefreshPanels();
    }

    /// <summary>Строка проверки раскладки: зеркало, сдвиг, сторона основной панели и её группа, SPOT, рамки панели, крупные портреты.</summary>
    public string LayoutLine()
    {
        var l = _scene.Layout;
        string Side(bool attacker) => _scene.PanelLeft(attacker) switch { true => "L", false => "R", _ => "-" };
        return $"зеркало={(l.Mirrored ? 1 : 0)} сдвиг={l.Shift} фон={l.BackgroundX} панель_нап={Side(true)} панель_защ={Side(false)} " +
               $"spot={(_scene.SpotShown ? 1 : 0)} рамки={string.Join(",", _field.PanelFrames)} лица={_field.BigFaces.Replace(' ', ',')}";
    }

    /// <summary>Ввод в панель боя шагом проверки («click TOG_RIGHTUNITS»).</summary>
    public bool Step(string input) => _panel.Step(input, out _);

    /// <summary>Мышь над точкой поля 800×600 (шаг проверки): метка под юнитом и полоса SPOT.</summary>
    public void Pointer(Vector2 at)
    {
        HoverAt(at);
        FocusAt(at);
        Spot(at);
        TargetCursor(at);
    }

    /// <summary>Последняя точка мыши на поле 800×600 — курсор пересчитывается по ней и после смены хода.</summary>
    private Vector2 _pointer = new(-1, -1);

    /// <summary>Курсор над точкой (<c>updateCursor</c> <c>0x633a1f</c>, <see cref="BattleCursors"/>): пока показывается действие — прежний;
    /// удар мышью не разрешён — <c>CUDEFAULT</c>; над юнитом (и павшим) или пустой позицией — пара класса атаки ходящего, «нельзя» —
    /// кроме годной цели. Тащимый предмет — свой курсор (<see cref="DragCursor"/>).</summary>
    private void TargetCursor(Vector2 at)
    {
        _pointer = at;
        // Показ ходов ИИ кончается сборкой поля с ходом игрока (Refresh в NextStep) — тогда курсор уже пересчитывается.
        if (_itemDrag is not null || _playing && !_session.WaitsPlayer || _cursor is null) return;
        string name = BattleCursors.Default;
        if (_session.WaitsPlayer && _message is null && _session.Allowed.Contains(BattleCommand.Attack) && _session.Current is { } unit)
        {
            if (_session.SummonSlots() is { Count: > 0 } cells)
                name = BattleCursors.Of(unit.Attack.Class, SummonCellAt(at, cells) is not null);
            else if (UnitAt(at, withFallen: true) is { } over) name = BattleCursors.Of(unit.Attack.Class, _session.CanTarget(over));
            else if (AnySlotAt(at)) name = BattleCursors.Of(unit.Attack.Class, false);
        }
        _cursor.ShowFrom(BattleCursors.Archive, name, BattleAnim);
    }

    /// <summary>Точка над зоной мыши места любой группы (позиция поля, <see cref="BattleScene.HitSlot"/>).</summary>
    private bool AnySlotAt(Vector2 at)
    {
        foreach (bool attacker in (bool[])[true, false])
            for (int s = 0; s < LastEdict.Rules.Units.Formation.Cells; s++)
            {
                var r = _scene.HitSlot(attacker, s);
                if (at.X >= r.X && at.Y >= r.Y && at.X < r.X + r.Width && at.Y < r.Y + r.Height) return true;
            }
        return false;
    }

    /// <summary>Мышь над полосой <c>IMG_PUNITGROUP_SPOT</c> (отражённой при зеркале): другая группа в панели своей стороны.</summary>
    private void Spot(Vector2 at)
    {
        var r = _panel.State.Screen.Elements.First(e => e.Id == "IMG_PUNITGROUP_SPOT").Rect;   // уже отражена BattleButtons.Shape
        bool over = at.X >= r.X && at.Y >= r.Y && at.X < r.X + r.Width && at.Y < r.Y + r.Height;
        if (over == _scene.SpotShown) return;
        _scene.SpotShown = over;
        RefreshPanels();
    }

    /// <summary>Фоны панелей — только у показанных сторон (<c>IMG_LUNITGROUPBG</c>/<c>IMG_RUNITGROUPBG</c>).</summary>
    private void PanelBackgrounds()
    {
        bool left = _scene.PanelShown(true) && _scene.PanelLeft(true) == true || _scene.PanelShown(false) && _scene.PanelLeft(false) == true;
        bool right = _scene.PanelShown(true) && _scene.PanelLeft(true) == false || _scene.PanelShown(false) && _scene.PanelLeft(false) == false;
        _panel.State.SetHidden("IMG_LUNITGROUPBG", !left);
        _panel.State.SetHidden("IMG_RUNITGROUPBG", !right);
        // Отметка переключателя — по показанной группе: своя группа в основной панели — отмечен.
        _panel.State.SetChecked("TOG_RIGHTUNITS", _scene.MainShowsAttackers == PlayerIsAttacker);
        _panel.State.SetChecked("TOG_AUTOBATTLE", _session.AutoOf(AutoSide));
        // «Закрыть» и «Меню героя» лежат на местах «Ждать» и «Отступить» и появляются, когда бой кончился (0x637581).
        _panel.State.SetHidden("BTN_CLOSE", !CanClose);
        _panel.State.SetHidden("BTN_PAPERDOLL", !PaperdollShown);
        // Конец боя снимает «Защищаться», «Ждать» и «Отступить» (0x637581); у лекаря в ходу после победы их тоже нет — только лечение
        // (ответ пользователя 11.10; BattleSession.Allowed).
        foreach (string id in (string[])["BTN_DEFEND", "BTN_WAIT", "BTN_RETREAT"]) _panel.State.SetHidden(id, _session.Finished || _session.AfterBattle);
        SyncButtons();   // гаснут на показ и ход ИИ, зажигаются к ходу человека (BattleScreen.Buttons.cs, Зс55)
        _panel.Sync();
    }

    /// <summary>Ход человека начался — основная панель на группе целей (<c>TOG_RIGHTUNITS</c> ставится заново, разбор <c>0x62df0e</c>).</summary>
    private void TurnPanel()
    {
        if (!_session.WaitsPlayer || ReferenceEquals(_session.Current, _turnOf) || _itemDrag is not null) return;
        _turnOf = _session.Current;
        var targets = _session.Targets();
        _scene.MainShowsAttackers = targets.Count > 0 ? targets[0].Attacker : !(_session.Current?.Attacker ?? PlayerIsAttacker);
    }

    private void Refresh()
    {
        TurnPanel();
        PanelBackgrounds();
        _field.Active = _session.Current;
        _field.ActiveHuman = HumanTurn;
        _field.ActiveName = _session.Current is { } current ? NameOf(current) : "";
        FocusFace();
        TargetMarks();
        _field.Build(_scene, _session.State, _place);
        _field.BuildPanels(_overlay, _scene, _session.State);
        _strip?.Hover(null);   // сборка поля сняла метку наведения (до следующего движения мыши) — и рамку портрета в полосе
        if (_pointer.X >= 0) TargetCursor(_pointer);
        Ended();
    }

    /// <summary>Метки целей хода для сборки поля и панелей: цели атаки или тащимого предмета, вид рамки, «вся группа» и позиции призыва
    /// (<c>MRKEMPTYSMALL</c>/<c>EMPTYSELSMALL</c>). Одно место и для открытия боя, и для каждой сборки: прежде открытие ставило всё, кроме
    /// позиций призыва, — если первым в бою ходил призыватель человека, ни кругов на клетках, ни рамок в панели не было (партия 17.10, карта 8,
    /// бой U1: Повелитель стихий в первом ряду ходил первым; в бою U2 первым ходил лидер, и сборка после его хода метки ставила).</summary>
    private void TargetMarks()
    {
        _field.Marked = _itemDrag is { } drag ? _session.ItemTargets(drag) : _session.WaitsPlayer ? _session.Targets() : [];
        _field.MarkKind = _itemDrag is { } dragged ? ItemMarkKind(dragged) : AttackMarkKind();
        _field.TargetsAll = _itemDrag is null && TargetsAll();
        _field.EmptyMarked = _itemDrag is { } summoning && _session.Current is { } bearer
            ? [.. _session.ItemSummonSlots(summoning).Select(s => (bearer.Attacker, s))]
            : _itemDrag is null && _session.Current is { } summoner ? [.. _session.SummonSlots().Select(s => (summoner.Attacker, s))] : [];
    }

    /// <summary>Пересобрать только панели (SPOT, переключатель группы панели): поле при этом не меняется, а его пересборка посреди показа
    /// действия обрывала бы замах, удар и эффекты и запускала их заново (замечание 13).</summary>
    private void RefreshPanels()
    {
        FocusFace();
        PanelBackgrounds();
        _field.BuildPanels(_overlay, _scene, _session.State);
    }

    /// <summary>Ширина холста боя по форме окна (высота 600).</summary>
    private int CanvasWidth()
    {
        var size = GetViewport().GetVisibleRect().Size;
        return size.Y <= 0 ? BattleLayout.ScreenWidth : Mathf.RoundToInt(600f * size.X / size.Y);
    }

    /// <summary>Точка окна в координатах поля 800×600.</summary>
    private Vector2 ToField(Vector2 at)
    {
        var size = GetViewport().GetVisibleRect().Size;
        float k = Mathf.Min(size.X / _host.X, size.Y / _host.Y);
        return (at - ((size - (_host * k)) / 2f)) / k;
    }

    private void Place()
    {
        var size = GetViewport().GetVisibleRect().Size;
        float k = Mathf.Min(size.X / _host.X, size.Y / _host.Y);
        // Поле — на весь холст формы окна (Э5, Р13): юниты и разметка 800×600 — по центру, края — фоном места (BattleView.Edges.cs).
        var margin = new Vector2I(Mathf.CeilToInt((((size.X / k) - _host.X) / 2f) - 0.01f), Mathf.CeilToInt((((size.Y / k) - _host.Y) / 2f) - 0.01f));
        _box.Position = ((size - (_host * k)) / 2f) - (new Vector2(margin.X, margin.Y) * k);
        // Вьюпорт поля — в точках экрана, поле в нём — масштабом холста (как вьюпорт карты, MapScreen.Place): прежде вьюпорт был размером с
        // холст боя в логических точках и растягивался на экран целиком — HD юнитов и фона терялся (прогон 24.10: «поле и юниты пиксельные»).
        _canvas = _host + (new Vector2(margin.X, margin.Y) * 2);
        _box.Scale = Vector2.One;
        _box.Size = (_canvas * k).Ceil();
        ((SubViewport)_box.GetChild(0)).CanvasTransform = new Transform2D(0f, new Vector2(k, k), 0f, Vector2.Zero);
        _field.Margin = margin;
        if (_overlay is null) return;
        _overlay.Position = (size - (_host * k)) / 2f;
        _overlay.Scale = new Vector2(k, k);
        if (_strip is null) return;
        _strip.Position = _overlay.Position;
        _strip.Scale = _overlay.Scale;
    }

    /// <summary>Бой из журнала корпуса: первый законченный.</summary>
    private static BattleState Load(string[] args, GameRules rules)
    {
        if (FromScenario(args) is { } fromMap) return fromMap;   // бой с карты сценария (--scenario, --stacks)
        string journal = MapSource.Arg(args, "--journal")
            ?? GameData.FromRoot(Path.Combine("corpus", "sample", "one_battle.jsonl"));
        if (!File.Exists(journal)) throw new FileNotFoundException($"журнал боя не найден: {journal}");
        return JournalBattle?.Invoke(journal, rules)
               ?? throw new NotSupportedException("бой из журнала зонда оригинала есть только в эталоне (BattleScreen.Journal.cs, LastEdict.Verification)");
    }

    /// <summary>Бой из журнала зонда оригинала (<c>--journal</c>): первый законченный. Разбор журналов — сверка с оригиналом
    /// (<c>LastEdict.Verification</c>); его ставит <c>BattleScreen.Journal.cs</c> эталона, в сборке без сверок крючка нет.</summary>
    private static Func<string, GameRules, BattleState>? JournalBattle { get; set; }

    /// <summary>Место боя для фона (<c>--place</c>); по умолчанию нейтральная земля.</summary>
    private static string Place(string[] args) => MapSource.Arg(args, "--place") ?? "NEUTRAL";

    /// <summary>Базовый тип картинок юнита (<see cref="LastEdict.Data.Content.Schema.UnitDef.BaseUnit"/>), нет — сам тип.</summary>
    private static Func<string, string> BaseUnit()
    {
        var content = GameData.Rules;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in content.Units)
            if (unit.BaseUnit is not null) map[unit.Key] = content.KeyOf(unit.BaseUnit);
        return type => ArtNames.Of(map.TryGetValue(type, out string? based) ? based : type);
    }

    /// <summary>Тип юнита → раса смерти (<see cref="LastEdict.Data.Content.Schema.DeathAnimation"/> заглавными: <c>HUMAN</c>, <c>GHOST</c>…).</summary>
    private static Func<string, string> DeathRace()
    {
        var byUnit = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in GameData.Rules.Units)
            if (unit.DeathAnimation is { } death) byUnit[unit.Key] = death.ToString().ToUpperInvariant();
        return type => byUnit.GetValueOrDefault(type, "");
    }

    /// <summary>Проверка «--item»: на первом ходу игрока после первого раунда ходящему дают зелье лечения как лидеру, предмет
    /// тащат мимо цели (не применён), затем на первую годную цель (применён).</summary>
    private async System.Threading.Tasks.Task CheckItemDrag()
    {
        while (!_session.Finished && !(_session.WaitsPlayer && _session.Round >= 1))
        {
            if (_session.WaitsPlayer) _session.Defend();
            else _session.AiStep();
        }
        if (_session.Current is not { } unit || GameData.Content.MapArt is null) return;
        var potion = LastEdict.Data.Play.GameBook.Load(GameData.Rules).Items.Values.First(i => i.Category == LastEdict.Rules.Magic.ItemCategory.PotionHeal);
        unit.Type = unit.Type with { IsLeader = true };
        unit.BattleItems.Add(potion);
        ItemCard(potion);   // правая кнопка по слоту — карточка предмета
        Card(null);
        StartItemDrag(0);
        DropItem(_session.State.Units.FirstOrDefault(u => u.Attacker != unit.Attacker && u.OnField));
        StartItemDrag(0);
        var target = _session.ItemTargets(0).FirstOrDefault() ?? unit;
        DragCursor(target);
        GD.Print($"battle item hover {target.Id} курсор={_cursor.Current ?? "-"}:{_cursor.Frames}");
        DropItem(target);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>Проверка «--resolve»: «Быстрый поединок» — вопрос и «Да» (бой досчитан).</summary>
    private async System.Threading.Tasks.Task CheckResolve()
    {
        while (!_session.Finished && !_session.WaitsPlayer) _session.AiStep();
        _panel.Step("click BTN_RESOLVE", out _);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        bool question = _message?.Logic is LastEdict.Data.Ui.Windows.MessageBoxLogic { Question: true };
        GD.Print($"battle resolve окно={(_message is null ? "-" : question ? "вопрос" : "ОК")}");
        _message?.Step(question ? "click BTN_YES" : "click BTN_OK", out _);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"battle resolve closed окно={(_message is null ? "-" : "есть")} конец={(_session.Finished ? 1 : 0)}");
    }

    /// <summary>Строка проверки трупов: сколько павших с картинкой трупа и у скольких кости (середина картинки) — у ног их места
    /// (середина нижнего края, <c>0x653887</c>; замечание 12).</summary>
    private string CorpseLine()
    {
        int corpses = 0, atFeet = 0;
        foreach (var unit in _session.State.Units.Where(u => !u.Alive && !u.Retreated))
        {
            if (_scene.Corpse(unit) is not { } corpse || GameData.Content.Gfx.Image(corpse.Archive, corpse.Name) is not { } image) continue;
            corpses++;
            var slot = unit.Type.SizeSmall ? _scene.Slot(unit.Attacker, unit.Slot) : _scene.BigSlot(unit.Attacker, unit.Slot);
            if (corpse.X + (image.W / 2) == slot.X + (slot.Width / 2) && corpse.Y + (image.H / 2) == slot.Y + slot.Height) atFeet++;
        }
        return $"трупов={corpses} у_ног={atFeet}";
    }

    private async System.Threading.Tasks.Task RunCheck(string? shot)
    {
        var start = DateTime.UtcNow;
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        int units = _session.State.Units.Count(u => u.OnField);
        int sprites = _field.Shown;
        GD.Print($"battle panel полосы={string.Join(",", _field.MeterLines)} значков={_field.PanelIcons} рамки={string.Join(",", _field.PanelFrames)}");
        // Метка под курсором: над живым юнитом противника — MRK под ним.
        _field.Hover(_session.State.Units.FirstOrDefault(u => u.OnField && u.Alive && u.Attacker != (_session.Current?.Attacker ?? true)));
        GD.Print($"battle hover меток={_field.HoverMarks}");
        _field.Hover(null);
        bool afterwards = OS.GetCmdlineUserArgs().Contains("--after");
        if (OS.GetCmdlineUserArgs().Contains("--card"))
        {
            Card(_session.State.Units.FirstOrDefault(u => u.OnField));
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GD.Print(VisibleLine());
        if (OS.GetCmdlineUserArgs().Contains("--rmb")) await CheckHeldCard();
        if (OS.GetCmdlineUserArgs().Contains("--item")) await CheckItemDrag();
        if (OS.GetCmdlineUserArgs().Contains("--refusal"))
            while (!_session.Finished && !_session.WaitsPlayer) _session.AiStep();
        if (OS.GetCmdlineUserArgs().Contains("--refusal") && _session.WaitsPlayer && _session.Current is { } me
            && _session.State.Rules?.Attacks.Values.FirstOrDefault(a => a.Class == AttackClass.Revive) is { } revive)
        {
            // Проверка сообщения 0x63cce6: ходящему — атака воскрешения, щелчок по павшему своей стороны, уже воскрешённому (окно), и по
            // павшему врагу (окна нет: сторона цели — 0x63e370 = +0xfa1). Своих в бою может не быть — павший свой ставится на свободную клетку.
            var attack = me.Attack;
            me.Attack = revive;
            var enemy = _session.State.Units.First(u => u.Attacker != me.Attacker && u.OnField);
            int enemyHp = enemy.Hp;
            (enemy.Hp, enemy.Revived) = (0, true);
            TryTarget(enemy);
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"battle refusal враг окно={(_message is null ? "-" : "DLG_MESSAGE_BOX")}");
            (enemy.Hp, enemy.Revived) = (enemyHp, false);
            var ally = new BattleUnit
            {
                Id = "CHECK-ALLY", Type = me.Type, Attack = me.Attack, Attack2 = me.Attack2, Attacker = me.Attacker,
                Slot = Enumerable.Range(0, 6).First(s => !_session.State.Units.Any(u => u.Attacker == me.Attacker && u.Covers(s))),
                HpMax = me.HpMax, Hp = 0, Revived = true,
            };
            _session.State.Units.Add(ally);
            TryTarget(ally);
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"battle refusal окно={(_message is null ? "-" : "DLG_MESSAGE_BOX")}");
            _message?.Step("click BTN_OK", out _);
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"battle refusal closed окно={(_message is null ? "-" : "DLG_MESSAGE_BOX")}");
            _session.State.Units.Remove(ally);
            me.Attack = attack;
        }
        if (OS.GetCmdlineUserArgs().Contains("--marks"))
        {
            // Проверка значков панели: первому юниту показанной группы — усиление 2, паралич, яд и защита; значки и подкраска — сборкой панелей.
            var marked = _session.State.Units.First(u => u.OnField && _scene.PanelShown(u.Attacker));   // группа в панели (раскладка §12)
            marked.BoostDamageLevel = 2;
            marked.Paralyzed = true;
            marked.Defending = true;
            marked.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 1, AppliedRound = _session.Round, Long = false });
            Refresh();
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"battle panel marks значков={_field.PanelIcons} подкраска={BattlePanel.Tint(marked).Whole}");
            // Цели в обеих группах (0x6372e7): на поле MRK под всеми целями.
            _field.Marked = [.. _session.State.Units.Where(u => u.OnField && u.Alive)];
            _field.Build(_scene, _session.State, _place);
            GD.Print($"battle both groups меток={_field.FieldMarks} целей={_field.Marked.Count}");
            marked.Paralyzed = false;
            marked.Dots.Clear();
        }
        if (OS.GetCmdlineUserArgs().Contains("--cursor"))
        {
            // Курсор цели (0x633a1f): над годной целью — пара класса, над своим (не целью) — «нельзя», вне мест — CUDEFAULT.
            while (!_session.Finished && !_session.WaitsPlayer) _session.AiStep();
            Refresh();
            if (_session.Current is { } mover && _session.Targets().FirstOrDefault() is { } aim)
            {
                Vector2 Center(BattleUnit u) => (u.Type.SizeSmall ? _scene.Slot(u.Attacker, u.Slot) : _scene.BigSlot(u.Attacker, u.Slot)) is var r
                    ? new Vector2(r.X + (r.Width / 2f), r.Y + (r.Height / 2f)) : default;
                Pointer(Center(aim));
                string target = _cursor.Current ?? "-";
                Pointer(Center(mover));
                string self = _cursor.Current ?? "-";
                Pointer(new Vector2(400, 20));
                GD.Print($"battle cursor цель={target} свой={self} вне={_cursor.Current ?? "-"} кадров={_cursor.Frames}");
            }
        }
        if (OS.GetCmdlineUserArgs().Contains("--d1")) await CheckInterface();
        if (OS.GetCmdlineUserArgs().Contains("--sides")) await CheckSides();
        if (OS.GetCmdlineUserArgs().Contains("--hover")) CheckHover();
        if (OS.GetCmdlineUserArgs().Contains("--flash")) await CheckFlashes();
        if (OS.GetCmdlineUserArgs().Contains("--resolve")) await CheckResolve();
        if (MapSource.Arg(OS.GetCmdlineUserArgs(), "--synth") is { } synthZone) await CheckSynth(synthZone);
        if (OS.GetCmdlineUserArgs().Contains("--queue-check")) await CheckQueueStrip();
        if (shot is not null && !afterwards) GetViewport().GetTexture().GetImage().SavePng(shot);
        _session.Auto = true;
        Continue();
        while (_playing) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);   // показ действий (D2_BATTLE_PLAYBACK=1)
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (shot is not null && afterwards) GetViewport().GetTexture().GetImage().SavePng(shot);
        GD.Print($"battle panel end полосы={string.Join(",", _field.MeterLines)} значков={_field.PanelIcons} надписи={string.Join(",", _field.NoteLines).Replace(' ', '_')}");
        GD.Print(FormattableString.Invariant($"battle playback шагов={StepsShown} действий={_field.ActionsStarted} повторов={_field.ActionRepeats} ")
                 + FormattableString.Invariant($"синхронно_макс={SyncMsMax:0} портреты={string.Join(",", _field.PanelUnits)} {CorpseLine()} ")
                 + FormattableString.Invariant($"костей_на_поле={_field.CorpsesShown} черепов={_field.DeadMasks} видеопамять_мб={GfxTextures.Bytes / 1048576.0:0.0}"));
        if (_strip is not null) GD.Print($"battle queue конец портретов={_strip.Cells.Count} модель={_session.Upcoming.Count}");
        // Вписывание в окно (Э5, Р13): поле прироста холста, размер поля и растяжка краёв фона.
        GD.Print($"battle fit поле={_field.Margin.X},{_field.Margin.Y} холст={_canvas.X}x{_canvas.Y} края={_field.EdgesShown}");
        GD.Print($"battle units={units} sprites={sprites} missing={_scene.Missing.Count} round={_session.Round} " +
                 $"turns={_session.Log.Count} card={(CardShown ? 1 : 0)} winner={_session.Winner ?? "-"} фильтр={(int)_field.TextureFilter} ms={(DateTime.UtcNow - start).TotalMilliseconds:F0}" +
                 (shot is null ? "" : $" shot={shot}"));
        GetTree().Quit(sprites > 0 && _scene.Missing.Count == 0 && _session.Finished ? 0 : 1);
    }
}

/// <summary>Карточка юнита (<c>DLG_R_C_UNIT</c>) по правой кнопке: портрет, имя и числа. Шаблоны строк игры для
/// этого окна не разобраны, поэтому числа выводятся простыми строками — это видно и подлежит уточнению.</summary>
public sealed class UnitCard(BattleUnit unit, string name, Func<string, string> text,
                            Func<int, string> source, Func<int, string> reach) : IScreenLogic
{
    /// <summary>Шаблоны игры: <c>X005TA0423</c> — уровень, опыт, здоровье, защита; <c>X005TA0788</c> — урон, тип
    /// атаки, инициатива, зона поражения, число целей (<c>data/ui/strings</c>).</summary>
    private const string Stats = "X005TA0423", Attack = "X005TA0788";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_UNIT_NAME", name);
        ui.SetText("TXT_STATS", text(Stats)
            .Replace("%LEVEL%", Num(unit.Type.Level)).Replace("%XP%", Num(unit.Xp))
            .Replace("%HP1%", Num(unit.Hp)).Replace("%HP2%", Num(BattlePanel.ShownHpMax(unit)))
            .Replace("%ARMOR%", Num(unit.Type.Armor)).Replace("%IMMU%", "").Replace("%WARD%", ""));
        ui.SetText("TXT_ATTACK_INFO", text(Attack)
            .Replace("%EFFECT%", "Урон").Replace("%DAMAGE%", Num(unit.Attack.QtyDamage))
            .Replace("%SOURCE%", source(unit.Attack.Source)).Replace("%SOURCE2%", "")
            .Replace("%INIT%", Num(unit.Attack.Initiative)).Replace("%REACH%", reach(unit.Attack.Reach))
            .Replace("%TARGETS%", Num(unit.Attack.Reach == AttackType.ReachAll ? 6 : 1)));
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    private static string Num(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Кнопки экрана боя: доступны те, что разрешает <see cref="BattleSession"/> (в игре это решает
/// сервер, <c>docs/battle-screen.md</c> §1). При зеркале (<see cref="BattleLayout.Mirrored"/>) кнопка <c>TOG_RIGHTUNITS</c> и полоса
/// <c>IMG_PUNITGROUP_SPOT</c> отражены.</summary>
public sealed class BattleButtons(Func<BattleSession> session, Func<BattleScene>? scene = null, Func<bool>? closable = null,
                                  Func<bool>? paperdoll = null)
    : LastEdict.Data.Ui.Windows.IWindowLogic
{
    public LastEdict.Data.Ui.Windows.UiRequest? TakeRequest() => null;

    /// <summary>Статичные картинки разметки (фоны панелей) у движка — на слое поля 3000 (<c>0x646b67</c>), под кнопками: рисуются первыми,
    /// иначе фон нижней панели закрывает кнопки (снимок 28.09).</summary>
    private static readonly string[] Statics = ["IMG_BIGFACESBG", "IMG_LITEMSBG", "IMG_RITEMSBG", "IMG_LUNITGROUPBG", "IMG_RUNITGROUPBG"];

    public UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var layout = scene?.Invoke().Layout ?? BattleLayout.Default;
        screen = WideBattle.Place(screen, layout.Width);   // широкий бой — та же разметка, что у поля
        var elements = screen.Elements
            .Select(e => layout.Mirrored && e.Id is "TOG_RIGHTUNITS" or "IMG_PUNITGROUP_SPOT" ? e with { Rect = layout.Mirror(e.Rect) } : e)
            // Картинки разметки движок снимает с диалога (0x62e017 → 0x62dc66 → 0x50b795; IMG_BIGFACESBG — 0x62dba6) и рисует сам на слое поля:
            // у них нет подсказок. В .dlg у подложек вместо строки подсказки — имя картинки («DLG_BATTLE_A_LITEMSBG»), оно и всплывало (пункт 14).
            .Select(e => e.Kind == UiKind.Image ? e with { TextId = null } : e)
            .OrderBy(e => Statics.Contains(e.Id) ? 0 : 1).ToList();
        return screen with { Elements = elements };
    }

    public void Open(UiScreenState ui)
    {
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public bool Allows(string element, UiScreenState ui)
    {
        var allowed = session().Allowed;
        return element switch
        {
            "BTN_DEFEND" => allowed.Contains(BattleCommand.Defend),
            "BTN_WAIT" => allowed.Contains(BattleCommand.Wait),
            "BTN_RETREAT" => allowed.Contains(BattleCommand.Retreat),
            "BTN_RESOLVE" => allowed.Contains(BattleCommand.Resolve),
            "TOG_AUTOBATTLE" => allowed.Contains(BattleCommand.Auto),
            // «Закрыть» и «Меню героя» появляются, когда бой кончился (в игре их создают заново, 0x637581).
            "BTN_CLOSE" => closable?.Invoke() ?? session().Finished,
            "BTN_PAPERDOLL" => paperdoll?.Invoke() ?? closable?.Invoke() ?? session().Finished,
            _ => true,
        };
    }
}
