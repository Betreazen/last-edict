using D2.Data.Content;
using D2.Data.Ui;
using Xunit;

namespace D2.Data.Tests;

/// <summary>Исполнение графа экранов: стек, фаза, выбор исхода и ручные дополнения графа. Цепочка меню проверяется
/// на настоящих данных (<c>data/ui</c>), правила стека — на выдуманном графе.</summary>
public class UiNavigatorTests
{
    private static GameContent Content => SharedContent.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    private static UiFlowEdge Edge(string? element, UiFlowAction action, string? target = null, int seen = 1) =>
        new() { Element = element, Action = action, Target = target, Seen = seen };

    private static UiFlow Flow(params (string Screen, UiFlowEdge[] Edges)[] screens) => new()
    {
        Screens = [.. screens.Select(s => new UiFlowScreen { Id = s.Screen, Edges = s.Edges })],
    };

    /// <summary>Первая проходимая цепочка: главное меню → одиночная игра → выбор сценария → раса → лорд →
    /// брифинг. Переходы идут через анимацию смены меню, брифинг — через «подождите» и переводит игру в партию.</summary>
    [SkippableFact]
    public void MenuChain_ReachesBriefingInGamePhase()
    {
        var nav = new UiNavigator(Content.Flow, "DLG_MAIN_MENU");
        string[] clicks = ["BTN_SINGLE", "BTN_NEW_SKIRMISH", "BTN_LOAD", "BTN_CONTINUE", "BTN_CONTINUE"];
        var path = clicks.Select(c => nav.Activate(c)!).ToList();

        Assert.Equal(["DLG_SINGLE_PLAYER", "DLG_CHOOSE_SKIRMISH", "DLG_CHOOSE_RACE", "DLG_CHOOSE_LORD",
                      "DLG_SCENARIO_BRIEFING"], path.Select(t => t.Target));
        Assert.All(path.Take(4), t => Assert.Equal("DLG_MENU_TRANS", t.Via));
        Assert.Equal("DLG_WAIT", path[^1].Via);
        Assert.Equal(["DLG_SCENARIO_BRIEFING"], nav.Stack);
        Assert.Equal(UiPhase.Game, nav.Phase);
    }

    /// <summary>«Назад», которых зонд не видел, дописаны в <c>flow.manual.json</c> со слов пользователя
    /// (19.09.2026): с выбора лорда и из одиночной игры — в главное меню.</summary>
    [SkippableTheory]
    [InlineData("DLG_CHOOSE_LORD")]
    [InlineData("DLG_SINGLE_PLAYER")]
    public void ManualBack_ReturnsToMainMenu(string screen)
    {
        var nav = new UiNavigator(Content.Flow, screen);
        var t = nav.Activate("BTN_BACK");
        Assert.NotNull(t);
        Assert.Equal((UiFlowAction.Goto, "DLG_MAIN_MENU", "DLG_MENU_TRANS"), (t.Action, t.Target, t.Via));
        Assert.Equal(["DLG_MAIN_MENU"], nav.Stack);
    }

    /// <summary>Окно загрузки в партии (<c>DLG_LOAD_GAME</c>; пометка 4 прогона 14.10: «не работает Esc и кнопка «Назад»»): у движка
    /// <c>BTN_CLOSE</c> — и щелчком, и клавишей 27 — закрывает окно, верхними снова настройки (корпус <c>20260919-f7b</c>, seq 58799:
    /// <c>ui_click BTN_CLOSE key=27</c> → <c>ui_hide</c>, верх <c>DLG_OPTIONS</c>). Журналы графа этого прохода не содержат — переход дописан
    /// в <c>flow.manual.json</c>. Из меню окно загрузки другое (<c>DLG_LOAD</c>): «Назад» и Esc — в главное меню.</summary>
    [SkippableFact]
    public void LoadGame_EscAndBack_ReturnToPreviousWindow()
    {
        var content = Content;
        foreach (var (screen, button) in new[] { ("DLG_LOAD_GAME", "BTN_CLOSE"), ("DLG_LOAD", "BTN_BACK") })
        {
            var ui = new UiScreenState(content.Screens.First(s => s.Id == screen));
            Assert.Equal([new UiClicked(button)], ui.Key(27));   // Esc жмёт ту же кнопку
        }

        var game = new UiNavigator(content.Flow, "DLG_ISO_PAL", UiPhase.Game);
        Assert.Equal("DLG_OPTIONS", game.Activate("BTN_OPTIONS")!.Target);
        Assert.Equal("DLG_LOAD_GAME", game.Activate("BTN_LOAD")!.Target);
        Assert.Equal(UiFlowAction.Close, game.Activate("BTN_CLOSE")?.Action);
        Assert.Equal(["DLG_ISO_PAL", "DLG_OPTIONS"], game.Stack);

        var menu = new UiNavigator(content.Flow, "DLG_MAIN_MENU");
        menu.Activate("BTN_SINGLE");
        Assert.Equal("DLG_LOAD", menu.Activate("BTN_LOAD_SKIRMISH")!.Target);
        Assert.Equal((UiFlowAction.Goto, "DLG_MAIN_MENU"), (menu.Activate("BTN_BACK")!.Action, menu.Top));
    }

    /// <summary>Титры (решение пользователя 28.09): пустая страница — все элементы спрятаны, остаётся фон; переход без элемента (любой
    /// щелчок или клавиша) — в главное меню через <c>DLG_MENU_TRANS</c>.</summary>
    [SkippableFact]
    public void Credits_EmptyPage_AnyInputToMainMenu()
    {
        var content = Content;
        var nav = new UiNavigator(content.Flow, "DLG_MAIN_MENU");
        Assert.Equal("DLG_CREDITS", nav.Activate("BTN_CREDITS")!.Target);
        var t = nav.Activate(null);
        Assert.NotNull(t);
        Assert.Equal((UiFlowAction.Goto, "DLG_MAIN_MENU", "DLG_MENU_TRANS"), (t.Action, t.Target, t.Via));

        var screen = content.Screens.First(s => s.Id == "DLG_CREDITS");
        var ui = new UiScreenState(screen);
        var logic = D2.Data.Menu.MenuLogic.For("DLG_CREDITS",
            new D2.Data.Menu.MenuContext(content.Catalog, new Dictionary<string, string>(), new D2.Data.Menu.NewGameSetup(), "Игрок"));
        Assert.NotNull(logic);
        logic.Open(ui);
        Assert.All(screen.Elements, e => Assert.True(ui.IsHidden(e.Id), e.Id));
        Assert.Equal("DLG_CREDITS_BG", ui.Background);
        Assert.True(logic.AnyInputLeaves);
    }

    /// <summary>Окно поверх и его закрытие: настройки открываются над главным меню, «закрыть» возвращает к нему;
    /// «выход» в главном меню снимает последний экран — это конец игры.</summary>
    [SkippableFact]
    public void OpenClose_AndQuitOnEmptyStack()
    {
        var nav = new UiNavigator(Content.Flow, "DLG_MAIN_MENU");
        Assert.Equal(UiFlowAction.Open, nav.Activate("BTN_OPTIONS")!.Action);
        Assert.Equal(["DLG_MAIN_MENU", "DLG_OPTIONS"], nav.Stack);
        Assert.Equal(UiFlowAction.Close, nav.Activate("BTN_CLOSE")!.Action);
        Assert.Equal(["DLG_MAIN_MENU"], nav.Stack);
        Assert.False(nav.Quit);
        nav.Activate("BTN_QUIT");
        Assert.True(nav.Quit);
        Assert.Null(nav.Top);
    }

    /// <summary>Несколько исходов у элемента: по умолчанию берётся переход со сменой экрана (а не «ничего»), из них —
    /// встреченный чаще. Логика экрана может выбрать сама.</summary>
    [Fact]
    public void SeveralOutcomes_DefaultPrefersScreenChange_AndChooserOverrides()
    {
        var flow = Flow(("A", [Edge("BTN", UiFlowAction.None, seen: 50), Edge("BTN", UiFlowAction.Goto, "B", 2),
                               Edge("BTN", UiFlowAction.Open, "C", 5)]));
        Assert.Equal("C", new UiNavigator(flow, "A").Activate("BTN")!.Target);

        var nav = new UiNavigator(flow, "A");
        var t = nav.Activate("BTN", edges => edges.First(e => e.Action == UiFlowAction.None));
        Assert.Equal(UiFlowAction.None, t!.Action);
        Assert.Equal(["A"], nav.Stack);
    }

    /// <summary>Элемент без переходов в графе — не ошибка: большинство щелчков экран не меняют.</summary>
    [Fact]
    public void ElementWithoutEdges_ReturnsNull()
    {
        var nav = new UiNavigator(Flow(("A", [Edge("BTN", UiFlowAction.Goto, "B")])), "A");
        Assert.Null(nav.Activate("OTHER"));
        Assert.Equal(["A"], nav.Stack);
    }

    /// <summary>Ручные дополнения ссылаются только на существующие экраны и элементы — как и импортный граф.</summary>
    [SkippableFact]
    public void ManualFlow_ReferencesKnownScreensAndElements()
    {
        var screens = UiScreens.Load(ImportedData.PathOf("ui/screens.json"))
                               .ToDictionary(s => s.Id);
        var manual = UiFlows.Load(Path.Combine(AppContext.BaseDirectory, "data", "ui", "flow.manual.json"));
        Assert.NotEmpty(manual.Screens);
        foreach (var s in manual.Screens)
        foreach (var e in s.Edges)
        {
            Assert.True(screens.TryGetValue(s.Id, out var scr), $"экран {s.Id}");
            Assert.Contains(scr.Elements, x => x.Id == e.Element);
            if (e.Target is { } t) Assert.True(screens.ContainsKey(t), $"{s.Id}.{e.Element} -> {t}");
            if (e.Via is { } v) Assert.True(screens.ContainsKey(v), $"{s.Id}.{e.Element} via {v}");
        }
    }

    // --- ролики смены экранов ---

    /// <summary>Смена «вперёд» по меню играет свой ролик, переход к лорду — ролик своей расы, «Назад» — без ролика.</summary>
    [SkippableFact]
    public void Transitions_ForwardHasVideo_RaceSubstituted_BackHasNone()
    {
        var t = Content.Transitions;
        Assert.Equal("TRANS_MAIN2SINGLE", t.VideoFor("DLG_MAIN_MENU", "DLG_SINGLE_PLAYER"));
        Assert.Equal("TRANS_GOD2LORD_UN", t.VideoFor("DLG_CHOOSE_RACE", "DLG_CHOOSE_LORD", "UN"));
        Assert.Null(t.VideoFor("DLG_CHOOSE_RACE", "DLG_MAIN_MENU"));

        var mod = UiTransitions.FromJson("""{ "Transitions": [ { "From": "DLG_MAIN_MENU", "To": "DLG_SINGLE_PLAYER", "Video": "MY" } ] }""");
        Assert.Equal("MY", UiTransitions.Merge(t, mod).VideoFor("DLG_MAIN_MENU", "DLG_SINGLE_PLAYER"));
    }

    /// <summary>Пары ролика смены — существующие экраны, и каждая пара есть в графе как переход (иначе ролик
    /// никогда бы не сыграл).</summary>
    [SkippableFact]
    public void Transitions_PairsAreGraphEdges()
    {
        var content = Content;
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "data", "ui", "transitions.json")));
        var pairs = json.RootElement.GetProperty("Transitions").EnumerateArray()
            .Select(e => (From: e.GetProperty("From").GetString()!, To: e.GetProperty("To").GetString()!)).ToList();
        Assert.Equal(pairs.Count, content.Transitions.Count);
        foreach (var (from, to) in pairs)
        {
            var screen = content.Flow.Screens.SingleOrDefault(s => s.Id == from);
            Assert.True(screen?.Edges.Any(e => e.Target == to) == true, $"{from} -> {to} нет в графе");
        }
    }

    // --- начальные значения экранов ---

    /// <summary>Выбор лорда открывается с воином и средней сложностью (со слов пользователя, 19.09.2026): воин —
    /// первый участник своей группы, а средняя сложность — вторая, её задают данные.</summary>
    [SkippableFact]
    public void Defaults_ChooseLordStartsWithWarriorAndAverage()
    {
        var content = Content;
        var screen = content.Screens.Single(s => s.Id == "DLG_CHOOSE_LORD");
        var state = new UiScreenState(screen);
        state.Apply(content.Defaults.For(screen.Id));

        Assert.True(state.IsChecked("TOG_LORD_WARRIOR"));
        Assert.True(state.IsChecked("TOG_DIFF_AVERAGE"));
        Assert.False(state.IsChecked("TOG_DIFF_EASY"));
    }

    [Fact]
    public void Defaults_UnknownGroupOrMember_Throws()
    {
        var screen = new UiScreen
        {
            Id = "S",
            Elements = [new UiElement { Id = "TOG", Kind = UiKind.Toggle },
                        new UiElement { Id = "RAD", Kind = UiKind.RadioGroup, Members = ["TOG"] }],
        };
        var state = new UiScreenState(screen);
        Assert.Throws<KeyNotFoundException>(() => state.Apply(new Dictionary<string, string> { ["RAD"] = "NOPE" }));
        Assert.Throws<KeyNotFoundException>(() => state.Apply(new Dictionary<string, string> { ["NOPE"] = "TOG" }));
    }

    [Fact]
    public void Defaults_ModOverridesPerGroup()
    {
        var baseSet = UiDefaults.FromJson("""{ "S": { "RAD_A": "X", "RAD_B": "Y" } }""");
        var mod = UiDefaults.FromJson("""{ "S": { "RAD_B": "Z" }, "T": { "RAD": "W" } }""");
        var merged = UiDefaults.Merge(baseSet, mod);
        Assert.Equal("X", merged.For("S")["RAD_A"]);
        Assert.Equal("Z", merged.For("S")["RAD_B"]);
        Assert.Equal("W", merged.For("T")["RAD"]);
        Assert.Empty(merged.For("NONE"));
    }
}
