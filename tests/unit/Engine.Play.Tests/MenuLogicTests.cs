using LastEdict.Data.Content;
using LastEdict.Data.Menu;
using LastEdict.Data.Ui;
using Xunit;

namespace LastEdict.Data.Tests;

/// <summary>Логика экранов цепочки «новая игра»: выбор сценария → раса → лорд → брифинг. Настоящие данные
/// (<c>data/ui</c>, <c>data/game</c>, строки <c>ru</c>); поведение, которого нет в данных, — со слов пользователя
/// (19.09.2026): первая строка списка выбрана сразу, «Следующая раса» ведёт Империя → Нежить → Эльфы → Кланы →
/// Легионы, первой показывается Империя, щелчок по портрету меняет лицо, в поле имени — системное имя, вверху
/// «Выберите лорда», в брифинге сначала история, потом цели.</summary>
public class MenuLogicTests
{
    private static GameContent Content => SharedContent.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> LoadedStrings = new(() => UiStrings.Load(ImportedData.StringsDir(), "ru"));
    private static IReadOnlyDictionary<string, string> Strings => LoadedStrings.Value;

    private static (UiScreenState Ui, IScreenLogic Logic) Open(string screenId, NewGameSetup setup)
    {
        var screen = Content.Screens.Single(s => s.Id == screenId);
        var ui = new UiScreenState(screen);
        ui.Apply(Content.Defaults.For(screenId));
        foreach (var list in screen.Elements.Where(e => e.Kind == UiKind.TextList)) ui.SetList(list.Id, 0, rowHeight: 20);
        var logic = MenuLogic.For(screenId, new MenuContext(Content.Catalog, Strings, setup, "Игрок"))!;
        logic.Open(ui);
        return (ui, logic);
    }

    private static void Fire(IScreenLogic logic, UiScreenState ui, IEnumerable<UiEvent> events)
    {
        foreach (var e in events) logic.Handle(e, ui);
    }

    [SkippableFact]
    public void Skirmish_ListsScenarios_FirstSelected_ShowsDescriptionSizeAndRaces()
    {
        var setup = new NewGameSetup();
        var (ui, logic) = Open("DLG_CHOOSE_SKIRMISH", setup);

        Assert.Equal(33, ui.Items("TLBOX_GAME_SLOT").Count);
        Assert.Equal(0, ui.ListSelected("TLBOX_GAME_SLOT"));
        var first = Content.Catalog.Scenarios[0];
        Assert.Same(first, setup.Scenario);
        Assert.Equal(first.Description, ui.TextOf("TXT_DESCRIPTION"));
        Assert.Contains("48 х 48", ui.TextOf("TXT_MAP_SIZE"));
        Assert.Equal(["GODHU", "GODHE", null, null],
                     new[] { "IMG_RACE_1", "IMG_RACE_2", "IMG_RACE_3", "IMG_RACE_4" }.Select(ui.Frame));
        // Значок расы (0x4f1095) — ровно место IMG_RACE_n 45×73: клетка заполнена целиком (замечание пользователя 11.10).
        foreach (string code in new[] { "HU", "HE", "UN", "DW", "EL" })
        {
            var icon = Content.AssetsByName[$"GOD{code}"];
            var place = ui.Screen.Elements.Single(e => e.Id == "IMG_RACE_1").Rect;
            Assert.Equal((place.Width, place.Height), (icon.W, icon.H));
        }
        Assert.True(logic.Allows("BTN_LOAD", ui));

        ui.Select("TLBOX_GAME_SLOT", 1);
        logic.Handle(new UiListSelected("TLBOX_GAME_SLOT", 1), ui);
        Assert.Same(Content.Catalog.Scenarios[1], setup.Scenario);
        Assert.Contains("96 х 96", ui.TextOf("TXT_MAP_SIZE"));
    }

    /// <summary>Сценарий с Империей, Легионами и эльфами: круг «следующей» расы идёт только по ним —
    /// Империя → эльфы → Легионы → Империя (Нежити и Кланов в сценарии нет). Смена расы играет ролик
    /// <c>GOD_X2Y</c>, если он есть для этой пары (соседи по полному кругу).</summary>
    [SkippableFact]
    public void Race_StartsWithEmpire_CyclesScenarioRacesInRingOrder()
    {
        var setup = new NewGameSetup { Scenario = Content.Catalog.Scenarios.Single(s => s.Id == "Arrival of the Pilgrims") };
        var (ui, logic) = Open("DLG_CHOOSE_RACE", setup);
        Assert.Equal(0, setup.Race);
        Assert.Equal("DLG_CHOOSE_RACE_GODHU_BG", ui.Underlay);
        Assert.Equal("DLG_CHOOSE_RACE_BG_CHOOSE_RACE", ui.Background);   // рамка остаётся поверх
        // 0x4ee08f: шаблон X005TA0484 «\fMedium;%RACE%» и полное имя расы X005TA0480, а не имя расы таблицы (X000TG0000).
        Assert.Equal(Strings["X005TA0484"].Replace("%RACE%", Strings["X005TA0480"]), ui.TextOf("TXT_RACE_DESC"));

        // «Следующая раса» — кнопка с подписью X100TA0129 (в разметке это BTN_PREV).
        Fire(logic, ui, ui.Key(39));
        Assert.Equal(5, setup.Race);
        Assert.Equal("DLG_CHOOSE_RACE_GODEL_BG", ui.Underlay);
        Assert.Null(ui.VideoOf(UiScreenState.BackgroundId)); // Империя и эльфы не соседи — ролика нет
        Fire(logic, ui, ui.Key(39));
        Assert.Equal(2, setup.Race);
        Fire(logic, ui, ui.Key(39));
        Assert.Equal(0, setup.Race);
        Assert.Equal("GOD_HE2HU", ui.VideoOf(UiScreenState.BackgroundId)!.Name);

        Fire(logic, ui, ui.Key(37)); // «Предыдущая раса»
        Assert.Equal(2, setup.Race);
        Assert.Equal("GOD_HU2HE", ui.VideoOf(UiScreenState.BackgroundId)!.Name);
    }

    [SkippableFact]
    public void Race_WithoutScenario_UsesAllPlayableRaces_AndFullRing()
    {
        var setup = new NewGameSetup();
        var (ui, logic) = Open("DLG_CHOOSE_RACE", setup);
        int[] seen = [.. Enumerable.Range(0, 5).Select(_ => { Fire(logic, ui, ui.Key(39)); return setup.Race; })];
        Assert.Equal([1, 5, 3, 2, 0], seen);
    }

    /// <summary>Вода и светлячки выбора расы — свои у расы (<c>0x4ede4e</c>, <c>0x4edea7</c>): у Империи обе анимации
    /// <c>_HUMAN</c>, у Нежити и Легионов — только вода, у Кланов и эльфов — ни одной (движок снимает картинку).</summary>
    [SkippableFact]
    public void Race_WaterAndFirefliesFollowTheRace()
    {
        var setup = new NewGameSetup();
        var (ui, logic) = Open("DLG_CHOOSE_RACE", setup);
        var seen = new Dictionary<int, (string?, string?)> { [setup.Race] = (ui.Frame("IMG_WATER"), ui.Frame("IMG_FIREFLY")) };
        for (int i = 0; i < 4; i++)
        {
            Fire(logic, ui, ui.Key(39));
            seen[setup.Race] = (ui.Frame("IMG_WATER"), ui.Frame("IMG_FIREFLY"));
        }
        Assert.Equal(("_WATER_HUMAN", "_FIREFLY_HUMAN"), seen[0]);
        Assert.Equal(("_WATER_UNDEAD", null), seen[1]);
        Assert.Equal(("_WATER_HERETIC", null), seen[2]);
        Assert.Equal((null, null), seen[3]);
        Assert.Equal((null, null), seen[5]);
    }

    /// <summary>Вода закрывает «Продолжить» (в разметке она позже кнопки): логика рисует картинки под кнопками, как на кадрах
    /// роликов <c>GOD_X2Y</c> (замечание пользователя 11.10 — у Империи кнопки не было).</summary>
    [SkippableFact]
    public void Race_ButtonsDrawnOverWater()
    {
        var logic = MenuLogic.For("DLG_CHOOSE_RACE", new MenuContext(Content.Catalog, Strings, new NewGameSetup(), "Игрок"))!;
        var screen = Content.Screens.Single(s => s.Id == "DLG_CHOOSE_RACE");
        var order = logic.Shape(screen).Elements.Select(e => e.Id).ToList();
        Assert.True(order.IndexOf("IMG_WATER") < order.IndexOf("BTN_CONTINUE"));
        Assert.True(order.IndexOf("IMG_FIREFLY") < order.IndexOf("BTN_BACK"));
        Assert.Equal(screen.Elements.Count, order.Count);
        // Разметка: вода позже кнопки и накрывает её — порядок меняет только логика.
        Assert.True(screen.Elements.ToList().FindIndex(e => e.Id == "IMG_WATER") > screen.Elements.ToList().FindIndex(e => e.Id == "BTN_CONTINUE"));
    }

    [SkippableFact]
    public void Lord_TitleNameFaceTypeAndDifficulty()
    {
        var setup = new NewGameSetup { Race = 1 };
        var (ui, logic) = Open("DLG_CHOOSE_LORD", setup);

        Assert.Equal("Выберите лорда", ui.TextOf("TXT_TITLE"));
        Assert.Equal("Игрок", ui.TextOf("EDIT_LORD_NAME"));
        Assert.Equal("DLG_CHOOSE_LORD_UNDEAD_BG", ui.Underlay);
        Assert.Equal("DLG_CHOOSE_LORD_BG1TRANS", ui.Background);
        Assert.Null(ui.VideoOf("BTN_FACE"));   // в покое лицо неподвижное — кадр интерфейса (снимок 28.09)
        Assert.Equal("FACE_UN_0", ui.Frame("BTN_FACE"));
        Assert.Equal((1, 1), (setup.LordCategory, setup.Difficulty)); // воин, средняя — из defaults.json
        Assert.Equal(Strings["X000TG3101"], ui.TextOf("TXT_LORD_TYPE"));
        Assert.Equal(Strings["X010TA0003"], ui.TextOf("TXT_DIFF"));

        // Смена лица (0x4e6945): звук, разовый ролик старого лица поверх неподвижного нового; пока ролик идёт — нажатия не принимаются.
        var window = (LastEdict.Data.Ui.Windows.IWindowLogic)logic;
        Fire(logic, ui, [new UiClicked("BTN_FACE")]);
        Assert.Equal(new LastEdict.Data.Ui.Windows.UiSound("CHNGFACE"), window.TakeRequest());
        Assert.Equal(new UiVideoRef("FACE_UN_0", false, 1), ui.VideoOf("BTN_FACE"));
        Assert.Equal("FACE_UN_1", ui.Frame("BTN_FACE"));
        Fire(logic, ui, [new UiClicked("BTN_FACE")]);
        Assert.Null(window.TakeRequest());
        Assert.Equal((1, "FACE_UN_0"), (setup.Face, ui.VideoOf("BTN_FACE")!.Name));
        Fire(logic, ui, [new UiVideoEnded("BTN_FACE")]);
        Assert.Null(ui.VideoOf("BTN_FACE"));
        for (int i = 0; i < 3; i++) Fire(logic, ui, [new UiClicked("BTN_FACE"), new UiVideoEnded("BTN_FACE")]);
        Assert.Equal("FACE_UN_0", ui.Frame("BTN_FACE"));

        ui.SetGroupIndex("RAD_LORD", 1);
        Fire(logic, ui, [new UiGroupChanged("RAD_LORD", 1, "TOG_LORD_MAGE")]);
        Assert.Equal(0, setup.LordCategory);
        Assert.Equal(Strings["X000TG3103"], ui.TextOf("TXT_LORD_TYPE"));

        ui.SetGroupIndex("RAD_DIFF", 3);
        Fire(logic, ui, [new UiGroupChanged("RAD_DIFF", 3, "TOG_DIFF_VERYHARD")]);
        Assert.Equal(3, setup.Difficulty);

        Fire(logic, ui, [new UiEdited("EDIT_LORD_NAME", "Мортис")]);
        Assert.Equal("Мортис", setup.LordName);
    }

    [SkippableFact]
    public void Briefing_HistoryThenGoals_AndRaceVideo()
    {
        var scenario = Content.Catalog.Scenarios.Single(s => s.Id == "Mercantile");
        var setup = new NewGameSetup { Scenario = scenario, Race = 5 };
        var (ui, logic) = Open("DLG_SCENARIO_BRIEFING", setup);

        // Текст — как у «Задания» из партии (общий CBriefingBase, 0x480659): X005TA0832 — имя, история, цель сценария.
        string text = ui.TextOf("TXT_TEXT_SCROLL")!;
        Assert.Contains(scenario.Name, text);
        Assert.Contains("The Elves turned", text);
        Assert.Contains(scenario.Briefing.Trim(), text);
        Assert.Equal("BRIEF3EL", ui.VideoOf("IMG_BRIEFING")!.Name);
        Assert.False(ui.IsHidden("BTN_REPLAY"));
        Assert.True(logic.Allows("BTN_CONTINUE", ui));   // «Продолжить» — переход графа меню

        Fire(logic, ui, [new UiClicked("BTN_REPLAY")]);
        Assert.Equal(2, ui.VideoOf("IMG_BRIEFING")!.Run);
    }

    [SkippableFact]
    public void OtherScreens_HaveNoLogic()
    {
        var context = new MenuContext(Content.Catalog, Strings, new NewGameSetup(), "Игрок");
        Assert.Null(MenuLogic.For("DLG_MAIN_MENU", context));
    }
}
