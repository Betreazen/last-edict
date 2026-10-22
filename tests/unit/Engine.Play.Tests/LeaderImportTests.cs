using D2.Data.Content;
using D2.Data.Leaders;
using D2.Data.Menu;
using D2.Data.Play;

namespace D2.Data.Tests;

/// <summary>Импорт героя (<c>docs/day-cycle.md</c> §12): файл игры <c>.ldr</c> → формат порта → герой в столице на старте партии.</summary>
public class LeaderImportTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>Образцы <c>.ldr</c> — каталог Exports копии игры с экспортом (<c>game-dev</c> соседнего репозитория).</summary>
    internal static string Exports()
    {
        if (Directory.Exists(GamePaths.Exports) && Directory.GetFiles(GamePaths.Exports, "*.ldr").Length > 0) return GamePaths.Exports;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")) && !File.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        string guess = Path.Combine(dir?.Parent?.FullName ?? "", "disciples-modding", "game-dev", "Exports");
        Skip.IfNot(Directory.Exists(guess), "нет каталога с героями игры (.ldr)");
        return guess;
    }

    /// <summary><c>Merlot.ldr</c> (экспорт английской версии): Архимаг 1-го уровня, лидерство 4, опыт 17 из 150, 4 победы, здоровье 65,
    /// «Lightning» — урон 30, воздух, инициатива 40; жезлы и свитки, сферы; версия 6 — отложенных навыков 0.</summary>
    [SkippableFact]
    public void Ldr_Sample_Merlot()
    {
        var leader = LdrFile.Read(File.ReadAllBytes(Path.Combine(Exports(), "Merlot.ldr")));
        Assert.Equal(("Merlot", 0, "G000UU0021"), (leader.Name, leader.Race, leader.UnitType));
        Assert.Equal((1, 4, 17, 150, 4), (leader.Level, leader.Leadership, leader.Xp, leader.XpNext, leader.BattlesWon));
        Assert.Equal((65, 0), (leader.HitPoints, leader.Armor));
        Assert.Equal(new LeaderAttack("G000AA0021", 1, "Lightning", 80, 0, 30, 7, 40, 1), leader.Attack);
        Assert.Equal("", leader.Attack2);
        Assert.Equal((20, 5, 10), (leader.Move, leader.Scout, leader.Negotiate));
        Assert.Equal([7, 14], leader.Abilities);
        Assert.Empty(leader.Equipment);
        Assert.Equal(0, leader.UpgradeCount);
    }

    /// <summary>Файл, у которого не сходится сумма тела, или не <c>D2LDR</c>, — не герой.</summary>
    [SkippableFact]
    public void Ldr_BadChecksum_Rejected()
    {
        byte[] file = File.ReadAllBytes(Path.Combine(Exports(), "Merlot.ldr"));
        file[20] ^= 0x01;
        Assert.Throws<InvalidDataException>(() => LdrFile.Read(file));
        Assert.Throws<InvalidDataException>(() => LdrFile.Read(new byte[4110]));
    }

    /// <summary>Файл игры — недоверенные данные: случайное тело с верной суммой либо разбирается, либо даёт
    /// <see cref="InvalidDataException"/> — другое исключение уронило бы меню при переводе каталога.</summary>
    [Fact]
    public void Ldr_RandomBodies_OnlyInvalidData()
    {
        var random = new Random(7);
        for (int n = 0; n < 300; n++)
        {
            var file = new byte[4110];
            "D2LDR\0"u8.CopyTo(file);
            BitConverter.GetBytes(6).CopyTo(file, 6);
            random.NextBytes(file.AsSpan(14));
            // Длины первых строк — малые, чтобы разбор уходил вглубь записи.
            if (n % 2 == 0) BitConverter.GetBytes(~random.Next(0, 64)).CopyTo(file, 14);
            uint sum = 0;
            foreach (byte b in file.AsSpan(14)) sum += b;
            BitConverter.GetBytes(sum).CopyTo(file, 10);
            try
            {
                LdrFile.Read(file);
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    /// <summary>Импорт каталога: каждый <c>.ldr</c> — герой порта под своим именем, повтор ничего не добавляет; формат порта читается
    /// тем же героем.</summary>
    [SkippableFact]
    public void Store_ImportsOnce_AndRoundTrips()
    {
        string exports = Exports();
        string dir = Path.Combine(Path.GetTempPath(), "d2-leaders-" + Guid.NewGuid().ToString("N"));
        try
        {
            int files = Directory.GetFiles(exports, "*.ldr").Length;
            Assert.Equal(files, LeaderStore.Import(exports, dir));
            Assert.Equal(0, LeaderStore.Import(exports, dir));
            var list = LeaderStore.List(dir);
            Assert.Equal(files, list.Count);
            var merlot = list.Single(l => l.FileName == "Merlot");
            Assert.Equal(LdrFile.Read(File.ReadAllBytes(Path.Combine(exports, "Merlot.ldr"))) with { }, merlot.Leader with { },
                         new LeaderComparer());
            LeaderStore.Delete(merlot);
            Assert.DoesNotContain(LeaderStore.List(dir), l => l.FileName == "Merlot");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Равенство по JSON — у записи есть списки.</summary>
    private sealed class LeaderComparer : IEqualityComparer<ExportedLeader>
    {
        public bool Equals(ExportedLeader? x, ExportedLeader? y) =>
            x is not null && y is not null && LeaderFile.ToJson(x) == LeaderFile.ToJson(y);

        public int GetHashCode(ExportedLeader obj) => LeaderFile.ToJson(obj).GetHashCode(StringComparison.Ordinal);
    }

    /// <summary>Одиночная игра (<c>DLG_CHOOSE_LORD</c>): «Импорт героя» включена при героях в каталоге (пустой — видна, но выключена,
    /// <c>0x4e5b6a</c>) и открывает окно выбора просьбой (не графом); выбранный — в выборе игрока и в <c>TXT_IMPORT_NAME</c>; «Нет» снимает.</summary>
    [SkippableFact]
    public void ChooseLord_ImportsLeader()
    {
        string dir = Path.Combine(Path.GetTempPath(), "d2-lord-leaders-" + Guid.NewGuid().ToString("N"));
        try
        {
            var content = SharedContent.Load(Data);
            var lordScreen = content.Screens.Single(s => s.Id == "DLG_CHOOSE_LORD");
            var empty = new D2.Data.Ui.UiScreenState(lordScreen);
            empty.Apply(content.Defaults.For(lordScreen.Id));
            MenuLogic.For("DLG_CHOOSE_LORD", new MenuContext(content.Catalog, new Dictionary<string, string>(),
                                                            new NewGameSetup { Scenario = content.Catalog.Scenarios[0], Race = 0 }, "Игрок")
            { LeadersDir = dir })!.Open(empty);
            Assert.False(empty.IsHidden("BTN_IMPORT_LEADER"));
            Assert.False(empty.IsEnabled("BTN_IMPORT_LEADER"));

            var hero = new ExportedLeader
            {
                Name = "Мерло", Race = 0, UnitType = "G000UU0021", Level = 1,
                Attack = new LeaderAttack("G000AA0021", 1, "Молния", 80, 0, 30, 7, 40, 1),
            };
            LeaderFile.Write(Path.Combine(dir, "Merlot.json"), hero);
            var setup = new NewGameSetup { Scenario = content.Catalog.Scenarios[0], Race = 0 };
            var context = new MenuContext(content.Catalog, new Dictionary<string, string>(), setup, "Игрок") { LeadersDir = dir };
            var logic = (D2.Data.Ui.Windows.IWindowLogic)MenuLogic.For("DLG_CHOOSE_LORD", context)!;
            var screen = content.Screens.Single(s => s.Id == "DLG_CHOOSE_LORD");
            var ui = new D2.Data.Ui.UiScreenState(screen);
            ui.Apply(content.Defaults.For(screen.Id));
            logic.Open(ui);
            Assert.True(ui.IsEnabled("BTN_IMPORT_LEADER"));
            Assert.False(logic.Allows("BTN_IMPORT_LEADER", ui));
            logic.Handle(new D2.Data.Ui.UiClicked("BTN_IMPORT_LEADER"), ui);
            var open = Assert.IsType<D2.Data.Ui.Windows.UiOpen>(logic.TakeRequest());
            var window = (D2.Data.Ui.Windows.IWindowLogic)open.Logic;
            var wui = new D2.Data.Ui.UiScreenState(content.Screens.Single(s => s.Id == open.Screen));
            wui.SetList("LBOX_LEADERS", 0, rowHeight: 93);
            window.Open(wui);
            wui.Select("LBOX_LEADERS", 1);
            window.Handle(new D2.Data.Ui.UiClicked("BTN_OK"), wui);
            Assert.IsType<D2.Data.Ui.Windows.UiAsk>(window.TakeRequest()).Yes();
            Assert.Equal(("Мерло", "Мерло"), (setup.ImportedLeader?.Name, ui.TextOf("TXT_IMPORT_NAME")));

            // Назад в меню, другая раса: выбор прежней расы не этой партии.
            setup.Race = 3;
            var again = MenuLogic.For("DLG_CHOOSE_LORD", context)!;
            var ui2 = new D2.Data.Ui.UiScreenState(screen);
            ui2.Apply(content.Defaults.For(screen.Id));
            again.Open(ui2);
            Assert.Null(setup.ImportedLeader);
            Assert.Equal("", ui2.TextOf("TXT_IMPORT_NAME"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Правая кнопка в окне просмотра героя (<c>0x4cab65</c>): лицо — карточка героя из файла (имя героя, здоровье «N / N»,
    /// «Выиграно сражений» из файла), строка навыка — карточка навыка по порядку файла (<c>X005TA0911</c>), слот — карточка предмета.</summary>
    [SkippableFact]
    public void ViewLeader_RightClickCards()
    {
        var content = SharedContent.Load(Data);
        var strings = D2.Data.Ui.UiStrings.Load(ImportedData.StringsDir(), "ru");
        var book = GameBook.Load(ImportedData.PathOf("rules"));
        var context = new MenuContext(content.Catalog, strings, new NewGameSetup(), "Игрок")
        {
            Book = book, Rules = D2.Data.Ui.Windows.RulesTexts.Load(ImportedData.PathOf("rules")),
            ItemTexts = D2.Data.Ui.Windows.StackTexts.Load(ImportedData.PathOf("rules")),
        };
        var hero = new ExportedLeader
        {
            Name = "Мерло", Race = 0, UnitType = "G000UU0021", Level = 1, Leadership = 4, Xp = 17, XpNext = 150, BattlesWon = 4,
            HitPoints = 65, Attack = new LeaderAttack("G000AA0021", 1, "Lightning", 80, 0, 30, 7, 40, 1), Move = 20, Scout = 5,
            Abilities = [14, 7], Equipment = [new LeaderItem(2, "G000IG0001", 0)],
        };
        var logic = new ViewLeaderLogic(context, hero);
        var screen = content.Screens.Single(s => s.Id == "DLG_VIEW_EXPLEAD");
        var ui = new D2.Data.Ui.UiScreenState(screen);
        ui.SetList("LBOX_ITEM_ICON", 0, rowHeight: 36);
        logic.Open(ui);
        D2.Data.Ui.UiRect At(string id) => screen.Elements.Single(e => e.Id == id).Rect;

        var face = At("IMG_LEADER_FACE");
        logic.RightPress(face.X + 5, face.Y + 5, ui);
        var card = Assert.IsType<D2.Data.Ui.Windows.UiOpen>(logic.TakeRequest());
        var cui = new D2.Data.Ui.UiScreenState(content.Screens.Single(s => s.Id == card.Screen));
        cui.SetList("MLBOX_ITEM_ICON", 0, rowHeight: 36);
        card.Logic.Open(cui);
        Assert.Contains("Мерло", cui.TextOf("TXT_UNIT_NAME"));
        Assert.Contains("65", cui.TextOf("TXT_STATS"));
        Assert.Contains("Выиграно сражений", cui.TextOf("TXT_LEADER_INFO"));
        Assert.Contains("Lightning", cui.TextOf("TXT_ATTACK_INFO"));

        var list = At("LBOX_ITEM_ICON");
        logic.RightPress(list.X + 5, list.Y + 5, ui);   // первая строка — первая способность файла (14, сферы)
        var ability = Assert.IsType<D2.Data.Ui.Windows.UiOpen>(logic.TakeRequest());
        var aui = new D2.Data.Ui.UiScreenState(content.Screens.Single(s => s.Id == ability.Screen));
        ability.Logic.Open(aui);
        Assert.Contains(strings["X005TA0907"], aui.TextOf("TXT_INFO"));

        var slot = At("IMG_BATTLE1");
        logic.RightPress(slot.X + 5, slot.Y + 5, ui);
        Assert.Equal("DLG_R_C_ITEM", Assert.IsType<D2.Data.Ui.Windows.UiOpen>(logic.TakeRequest()).Screen);
    }

    /// <summary>Хвосты импорта (<c>0x42f359</c>): в пустой столице — новый отряд из героя (владелец и подраса столицы, внутри неё,
    /// «обычный» приказ); в столице с большим отрядом — лишние воины (<c>E = N − L − 1</c>) уходят в гарнизон; у отряда героя уровень
    /// создания 1.</summary>
    [SkippableFact]
    public void ImportLeader_EmptyCapital_And_Leadership()
    {
        string path = ScenarioFile.PathOf(ImportedData.PathOf("scenarios"), "Power to Corrupt");
        Skip.IfNot(File.Exists(path), "нет сценария порта");
        var book = GameBook.Load(ImportedData.PathOf("rules"));
        var looks = ScenarioFile.Read(path).Looks;
        var leader = new ExportedLeader
        {
            Name = "Мерло", Race = 3, UnitType = "G000UU0021", Level = 1, Leadership = 1,
            Attack = new LeaderAttack("G000AA0021", 1, "Lightning", 80, 0, 30, 7, 40, 1),
        };
        var map = looks.Map;
        var capital = map.Sites.Single(s => s.Kind == D2.Rules.Map.SiteKind.Capital && s.Owner.Equals("S073PL000a", StringComparison.OrdinalIgnoreCase));

        // Пустая столица.
        var empty = D2.Data.Play.GameSession.Retold(looks, D2.Data.Play.GameSession.With(map,
            stacks: map.Stacks.Where(p => p.Key != capital.StackId).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
            sites: [.. map.Sites.Select(s => s.Id == capital.Id ? s with { StackId = "" } : s)]), looks.Stacks);
        var created = NewGame.ImportLeader(empty, "S073PL000a", leader, book).Map;
        var inside = created.Stacks[created.Sites.Single(s => s.Id == capital.Id).StackId];
        Assert.Equal((capital.Owner, capital.Subrace, capital.Id, (int)D2.Rules.Battle.StackOrder.Normal, 1),
                     (inside.Owner, inside.Subrace, inside.Inside, inside.Order, inside.CreationLevel));
        Assert.Equal("G000UU0021", created.Units[inside.LeaderId].TypeId);

        // Большой герой — обе клетки колонки (2 и 3).
        var bigType = book.Rules.Units.Values.First(u => !u.SizeSmall && book.CategoryOf(u.Id) == D2.Rules.Battle.UnitCategory.Leader
                                                         && u.Id.Substring(4, 2).Equals("UU", StringComparison.OrdinalIgnoreCase));
        var bigMap = NewGame.ImportLeader(empty, "S073PL000a", leader with { UnitType = bigType.Id }, book).Map;
        var bigStack = bigMap.Stacks[bigMap.Sites.Single(s => s.Id == capital.Id).StackId];
        Assert.Equal((0, 0), (bigStack.Slots[2], bigStack.Slots[3]));

        // Отряд из четырёх при лидерстве 1: лишних 4 − 1 − 1 = 2 — в гарнизон.
        var stack = map.Stacks[capital.StackId];
        var extra = map.Stacks.Values.Where(s => s.Id != stack.Id).SelectMany(s => s.UnitIds).Take(3).ToList();
        var big = stack with { UnitIds = [stack.LeaderId, .. extra], Slots = [1, 2, 0, 3, -1, -1] };
        var full = D2.Data.Play.GameSession.Retold(looks, D2.Data.Play.GameSession.With(map,
            stacks: map.Stacks.ToDictionary(p => p.Key, p => p.Key == stack.Id ? big : p.Value, StringComparer.OrdinalIgnoreCase),
            sites: [.. map.Sites.Select(s => s.Id == capital.Id ? s with { Guards = [], GuardSlots = [-1, -1, -1, -1, -1, -1] } : s)]), looks.Stacks);
        int guards = 0;   // гарнизон пуст — места хватает, никто не удаляется
        var fitted = NewGame.ImportLeader(full, "S073PL000a", leader, book).Map;
        var after = fitted.Stacks[stack.Id];
        var garrison = fitted.Sites.Single(s => s.Id == capital.Id);
        Assert.Equal(2, after.UnitIds.Count);
        Assert.Equal(guards + 2, garrison.Guards.Count);
        Assert.Contains(stack.LeaderId, after.UnitIds);
        Assert.Equal(1, after.CreationLevel);
    }

    /// <summary>Герой ячейки лобби на старте (<c>0x42f359</c>): лидер отряда в столице — тип и уровень героя (здоровье — из типа),
    /// имя, опыт, модификаторы; предмет — новый экземпляр в своём слоте; отряду — победы и отложенные навыки.</summary>
    [SkippableFact]
    public void NewGame_ImportedLeader_ReplacesCapitalHero()
    {
        string path = ScenarioFile.PathOf(ImportedData.PathOf("scenarios"), "Power to Corrupt");
        Skip.IfNot(File.Exists(path), "нет сценария порта");
        var content = SharedContent.Load(Data);
        var book = GameBook.Load(ImportedData.PathOf("rules"));
        var leader = new ExportedLeader
        {
            Name = "Мерло", Race = 3, UnitType = "G000UU0021", Level = 3, Xp = 17, BattlesWon = 4, UpgradeCount = 2,
            Attack = new LeaderAttack("G000AA0021", 1, "Lightning", 80, 0, 30, 7, 40, 1),
            Modifiers = ["G000UM9031"], Equipment = [new LeaderItem(2, "G000IG0001", 0)],
        };
        var setup = new NewGameSetup
        {
            Scenario = content.Catalog.Scenarios.Single(s => s.Id == "Power to Corrupt"),
            Hotseat = [new HotseatSeat(3, "Гимли", 0, 1, leader), new HotseatSeat(2, "Б", 0, 1)],
        };
        var state = NewGame.Start(ScenarioFile.Read(path), setup, content.Catalog, book, new Random(1));
        var map = state.Looks.Map;
        var capital = map.Sites.Single(s => s.Kind == D2.Rules.Map.SiteKind.Capital && s.Owner.Equals("S073PL000a", StringComparison.OrdinalIgnoreCase));
        var stack = map.Stacks[capital.StackId];
        var hero = map.Units[stack.LeaderId];
        var grown = book.Grown("G000UU0021", 3)!;
        Assert.Equal(("G000UU0021", grown.Level, 17), (hero.TypeId, hero.Level, hero.Xp));
        Assert.Contains("G000UM9031", hero.Modifiers);
        Assert.Equal(("G000IG0001", 4, 2), (map.ItemTypes[stack.Equipment[2]], stack.BattlesWon, stack.UpgradeCount));
        Assert.Equal("Мерло", state.Looks.Stacks.Single(s => s.Id == stack.Id).LeaderName);

        // Герой чужой расы (Империя в ячейке Кланов) не встаёт: лидер — стартовый.
        setup.Hotseat = [new HotseatSeat(3, "Гимли", 0, 1, leader with { Race = 0 }), new HotseatSeat(2, "Б", 0, 1)];
        var foreign = NewGame.Start(ScenarioFile.Read(path), setup, content.Catalog, book, new Random(1)).Looks.Map;
        var foreignStack = foreign.Stacks[foreign.Sites.Single(s => s.Id == capital.Id).StackId];
        Assert.NotEqual("G000UU0021", foreign.Units[foreignStack.LeaderId].TypeId);
    }
}
