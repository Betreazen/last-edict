using LastEdict.Data.Content;
using LastEdict.Data.Menu;
using LastEdict.Data.Play;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;

namespace LastEdict.Data.Tests;

/// <summary>Лобби «горячего стула» (<c>docs/day-cycle.md</c> §8, разбор <c>CMenuHotseatLobby</c> <c>0x4dc67c</c>): ячейки по
/// центру, по умолчанию компьютер, лицо и лорд по кругу, «Далее» — два человека с именами; казна лобби в новой партии.</summary>
public class HotseatLobbyTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static GameContent Content => SharedContent.Load(Data);

    private static (UiScreenState Ui, IWindowLogic Logic) Lobby(NewGameSetup setup)
    {
        var context = new MenuContext(Content.Catalog, new Dictionary<string, string>(), setup, "Игрок") { Screens = Content.Screens };
        var logic = (IWindowLogic)MenuLogic.For("DLG_HOTSEAT_LOBBY", context)!;
        var screen = logic.Shape(Content.Screens.Single(s => s.Id == "DLG_HOTSEAT_LOBBY"));
        var ui = new UiScreenState(screen);
        foreach (var spin in screen.Elements.Where(e => e.Kind == UiKind.Spin)) ui.SetSpin(spin.Id, 0, 0);
        logic.Open(ui);
        return (ui, logic);
    }

    private static void Fire(IScreenLogic logic, UiScreenState ui, IEnumerable<UiEvent> events)
    {
        foreach (var e in events) logic.Handle(e, ui);
    }

    /// <summary>Щелчок по середине элемента: нажатие и отпускание.</summary>
    private static IReadOnlyList<UiEvent> Click(UiScreenState ui, string id)
    {
        var r = Rect(ui, id);
        int x = r.X + (r.Width / 2), y = r.Y + (r.Height / 2);
        return [.. ui.Press(x, y), .. ui.Release(x, y)];
    }

    private static UiRect Rect(UiScreenState ui, string id) => ui.Screen.Elements.Single(e => e.Id == id).Rect;

    /// <summary>«A Return To Simpler Times» — две расы: ячейки во 2-м и 3-м держателях (<c>(4 − 2) / 2 = 1</c>), обе — компьютер
    /// под заслонкой, лицо 1, воин; золото и мана 0, сложность средняя; «Далее» закрыто.</summary>
    [SkippableFact]
    public void Opens_TwoCellsCentered_AllComputer()
    {
        var setup = new NewGameSetup { Scenario = Content.Catalog.Scenarios.Single(s => s.Id == "'A Return To Simpler Times'") };
        var (ui, logic) = Lobby(setup);
        var holder = Rect(ui, "IMG_RACE2_HOLDER");
        Assert.Equal((holder.X + 142, holder.Y), (Rect(ui, "TOG_PLAYERTYPE_1").X, Rect(ui, "TOG_PLAYERTYPE_1").Y));
        Assert.Equal(Rect(ui, "IMG_RACE3_HOLDER").X + 142, Rect(ui, "TOG_PLAYERTYPE_2").X);
        Assert.DoesNotContain(ui.Screen.Elements, e => e.Id == "TOG_PLAYERTYPE_3");
        var empire = Content.Catalog.Race(setup.Scenario!.Races[0]);
        Assert.Equal($"DLG_HOTSEAT_LOBBY_RACE_R{empire.Art}", ui.Frame("IMG_RACE_1"));
        Assert.False(ui.IsChecked("TOG_PLAYERTYPE_1"));
        Assert.Equal("DLG_HOTSEAT_LOBBY_RACE_COVERER_IMAGE", ui.Frame("IMG_COVERER_1"));
        Assert.False(ui.IsEnabled("BTN_FACE_1"));
        Assert.Equal($"DLG_HOTSEAT_LOBBY_RACE_FACE{empire.Code}1N", ui.Frame("BTN_FACE_1"));
        Assert.Equal("DLG_HOTSEAT_LOBBY_RACE_LWARRIORN", ui.Frame("BTN_LORD_1"));
        Assert.Equal(("0", "0", 1), (ui.TextOf("SPIN_GOLD"), ui.TextOf("SPIN_MANA"), setup.Difficulty));
        Assert.False(logic.Allows("BTN_OK", ui));
        Assert.Equal("X006TA0032", logic.Refusal("BTN_OK", ui));   // «Вы должны выбрать как минимум две расы.»
        Assert.Empty(setup.Hotseat);
    }

    /// <summary>Два человека с именами — «Далее» открыто; лицо и лорд по кругу, имя режется до 15 знаков, казна шагом 100.</summary>
    [SkippableFact]
    public void TwoNamedHumans_FillSetup()
    {
        var setup = new NewGameSetup { Scenario = Content.Catalog.Scenarios.Single(s => s.Id == "'A Return To Simpler Times'") };
        var (ui, logic) = Lobby(setup);
        Fire(logic, ui, ui.Screen.Elements.Where(e => e.Id.StartsWith("TOG_PLAYERTYPE_", StringComparison.Ordinal))
                                          .Select(e => new UiToggled(e.Id, true)).ToList());
        Assert.Null(ui.Frame("IMG_COVERER_1"));
        Assert.True(ui.IsEnabled("EDT_NAME_2"));
        Assert.Equal("X005TA0344", logic.Refusal("BTN_OK", ui));   // имён нет: «Лорд должен иметь имя.»
        var face = Rect(ui, "BTN_FACE_1");
        ui.Move(face.X + 5, face.Y + 5);
        Assert.EndsWith("1H", ui.Frame("BTN_FACE_1"));   // под мышью — кадр наведения
        Fire(logic, ui, [new UiEdited("EDT_NAME_1", "Утер"), new UiEdited("EDT_NAME_2", "Очень-очень-длинное-имя"),
                         new UiClicked("BTN_LORD_2"), new UiClicked("BTN_LORD_2"), new UiClicked("BTN_FACE_1"),
                         .. Click(ui, "BTN_GOLD_UP"), .. Click(ui, "BTN_GOLD_UP"), .. Click(ui, "BTN_MANA_UP")]);
        Assert.True(logic.Allows("BTN_OK", ui));
        Assert.Null(logic.Refusal("BTN_OK", ui));
        Assert.Equal("Очень-очень-дли", ui.TextOf("EDT_NAME_2"));
        Assert.Equal("DLG_HOTSEAT_LOBBY_RACE_LDIPLOMATN", ui.Frame("BTN_LORD_2"));
        var races = setup.Scenario!.Races;
        Assert.Equal([new HotseatSeat(races[0], "Утер", 1, 1), new HotseatSeat(races[1], "Очень-очень-дли", 0, 2)], setup.Hotseat);
        Assert.Equal((200, 100), (setup.StartGold, setup.StartMana));
    }

    /// <summary>Импорт героя в лобби (<c>0x4dd28a</c> → <c>DLG_IMPORT_LEADER</c>): кнопка у человека при героях в каталоге; окно —
    /// «Нет» и герои (лицо, имя и уровень), выбран «Нет»; герой своей расы → вопрос <c>X005TA0736</c> → «Да»: окно закрывается, в ячейке —
    /// имя героя, в выборе лобби — герой; переключение ячейки на компьютер снимает выбор.</summary>
    [SkippableFact]
    public void ImportLeader_PickedIntoSeat()
    {
        string dir = Path.Combine(Path.GetTempPath(), "d2-lobby-leaders-" + Guid.NewGuid().ToString("N"));
        try
        {
            var hero = new LastEdict.Data.Leaders.ExportedLeader
            {
                Name = "Мерло", Race = 0, UnitType = "G000UU0021", Level = 2,
                Attack = new LastEdict.Data.Leaders.LeaderAttack("G000AA0021", 1, "Молния", 80, 0, 30, 7, 40, 1),
            };
            LastEdict.Data.Leaders.LeaderFile.Write(Path.Combine(dir, "Merlot.json"), hero);
            LastEdict.Data.Leaders.LeaderFile.Write(Path.Combine(dir, "Zgnom.json"), hero with { Name = "Гном", Race = 3 });
            var setup = new NewGameSetup { Scenario = Content.Catalog.Scenarios.Single(s => s.Id == "'A Return To Simpler Times'") };
            var context = new MenuContext(Content.Catalog, new Dictionary<string, string>(), setup, "Игрок")
            {
                Screens = Content.Screens, LeadersDir = dir,
            };
            var lobby = (IWindowLogic)MenuLogic.For("DLG_HOTSEAT_LOBBY", context)!;
            var ui = new UiScreenState(lobby.Shape(Content.Screens.Single(s => s.Id == "DLG_HOTSEAT_LOBBY")));
            foreach (var spin in ui.Screen.Elements.Where(e => e.Kind == UiKind.Spin)) ui.SetSpin(spin.Id, 0, 0);
            lobby.Open(ui);
            Assert.False(ui.IsHidden("BTN_IMPORT_LEADER_1"));
            Assert.False(ui.IsEnabled("BTN_IMPORT_LEADER_1"));   // компьютеру — нельзя
            Fire(lobby, ui, [new UiToggled("TOG_PLAYERTYPE_1", true), new UiClicked("BTN_IMPORT_LEADER_1")]);
            var open = Assert.IsType<UiOpen>(lobby.TakeRequest());
            Assert.Equal("DLG_IMPORT_LEADER", open.Screen);

            var window = (IWindowLogic)open.Logic;
            var screen = Content.Screens.Single(s => s.Id == "DLG_IMPORT_LEADER");
            var wui = new UiScreenState(screen);
            wui.SetList("LBOX_LEADERS", 0, rowHeight: 93);
            window.Open(wui);
            Assert.Equal(3, wui.Cards("LBOX_LEADERS").Count);
            Assert.Equal((false, true), (wui.Cards("LBOX_LEADERS")[1].Dimmed, wui.Cards("LBOX_LEADERS")[2].Dimmed));   // чужая раса — темнее
            Assert.Equal(0, wui.ListSelected("LBOX_LEADERS"));
            Assert.Equal("Faces:G000UU0021FACE", wui.Cards("LBOX_LEADERS")[1].Image);
            wui.Select("LBOX_LEADERS", 1);
            window.Handle(new UiListSelected("LBOX_LEADERS", 1), wui);
            Assert.True(wui.IsEnabled("BTN_VIEW_LEADER"));
            window.Handle(new UiClicked("BTN_OK"), wui);
            var ask = Assert.IsType<UiAsk>(window.TakeRequest());
            ask.Yes();
            Assert.IsType<UiClose>(window.TakeRequest());
            Assert.Equal("Мерло", ui.TextOf("TXT_IMPORT_NAME_1"));
            Assert.Equal("Мерло", setup.Hotseat.Single().Leader?.Name);

            Fire(lobby, ui, [new UiToggled("TOG_PLAYERTYPE_1", false)]);
            Assert.Equal("", ui.TextOf("TXT_IMPORT_NAME_1"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Загрузка из меню (<c>menuload:ButtonCallback 0x4df7b0</c>): «горячему стулу» нужно не меньше двух людей
    /// (<c>X006TA0033</c>), одиночной игре партия с несколькими людьми запрещена (<c>X006TA0037</c>); годная партия уходит хозяину.</summary>
    [SkippableFact]
    public void LoadMenu_ChecksHumans()
    {
        string dir = Path.Combine(Path.GetTempPath(), "d2-hotseat-load-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = ScenarioFile.PathOf(ImportedData.PathOf("scenarios"), "'A Return To Simpler Times'");
            var looks = ScenarioFile.Read(path).Looks;
            GameSave.Write(Path.Combine(dir, "one" + SaveSlots.Extension),
                           new GameState(new GameInfo("s", "s", ["S001PL0001"], 1), looks), "один");
            GameSave.Write(Path.Combine(dir, "two" + SaveSlots.Extension),
                           new GameState(new GameInfo("s", "s", ["S001PL0001", "S001PL0002"], 1), looks), "двое");
            Assert.Equal([1, 2], SaveSlots.List(dir).Select(s => s.Humans).Order());

            foreach (bool hotseat in new[] { true, false })
            {
                var loaded = new List<string>();
                var logic = new LoadGameLogic(dir, loaded.Add, hotseat);
                var screen = Content.Screens.Single(s => s.Id == "DLG_LOAD");
                var ui = new UiScreenState(screen);
                foreach (var list in screen.Elements.Where(e => e.Kind == UiKind.TextList)) ui.SetList(list.Id, 0, rowHeight: 20);
                logic.Open(ui);
                string listId = screen.Elements.First(e => e.Kind == UiKind.TextList).Id;
                var slots = SaveSlots.List(dir);
                foreach (var (slot, i) in slots.Select((s, i) => (s, i)))
                {
                    ui.Select(listId, i);
                    logic.Handle(new UiClicked("BTN_LOAD"), ui);
                    string? refusal = logic.Refusal("BTN_LOAD", ui);
                    string? expected = hotseat ? (slot.Humans < 2 ? "X006TA0033" : null) : (slot.Humans > 1 ? "X006TA0037" : null);
                    Assert.Equal(expected, refusal);
                    Assert.Equal(expected is null, loaded.Contains(slot.Path));
                }
            }
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Новая партия из лобби: люди с именами, лицами и лордами ячеек; казна лобби у каждого игрока карты (золото и пять
    /// видов маны) — у ИИ до его хода она та же; без казны лобби — из сценария.</summary>
    [SkippableFact]
    public void NewGame_SeatsAndStartCash()
    {
        string path = ScenarioFile.PathOf(ImportedData.PathOf("scenarios"), "Power to Corrupt");
        Skip.IfNot(File.Exists(path), "нет сценария порта");
        var book = GameBook.Load(ImportedData.PathOf("rules"));
        var data = ScenarioFile.Read(path);
        var scenario = Content.Catalog.Scenarios.Single(s => s.Id == "Power to Corrupt");
        var setup = new NewGameSetup
        {
            Scenario = scenario, Hotseat = [new HotseatSeat(3, "Гимли", 2, 0), new HotseatSeat(2, "Бетрезен", 1, 2)],
            StartGold = 300, StartMana = 100,
        };
        var state = NewGame.Start(data, setup, Content.Catalog, book, new Random(1));
        var players = state.Looks.Map.Players;
        Assert.Equal(["S073PL000A", "S073PL000B"], state.Info.Humans.Select(h => h.ToUpperInvariant()));
        Assert.Equal(("Гимли", 2), (players["S073PL000a"].Name, players["S073PL000a"].Face));
        Assert.Equal(0, book.LordCategory(players["S073PL000a"].LordId));
        Assert.Equal(("Бетрезен", 2), (players["S073PL000b"].Name, book.LordCategory(players["S073PL000b"].LordId)));
        // Казна лобби прибавляется к казне сценария (Currency::Add 0x586642), и нейтралу тоже.
        Assert.Equal(data.Looks.Map.Players["S073PL0009"].Bank + new LastEdict.Rules.Map.Bank(300, 100, 100, 100, 100, 100),
                     players["S073PL0009"].Bank);
        Assert.True(data.Looks.Map.Players["S073PL0009"].Bank.Gold > 0);

        setup.StartGold = setup.StartMana = 0;
        var plain = NewGame.Start(data, setup, Content.Catalog, book, new Random(1));
        Assert.Equal(data.Looks.Map.Players["S073PL0009"].Bank, plain.Looks.Map.Players["S073PL0009"].Bank);
    }
}
