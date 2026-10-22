using LastEdict.Data.Content;
using LastEdict.Data.Menu;
using LastEdict.Data.Play;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;
using LastEdict.Rules.Map;
using LastEdict.Rules.Units;
using Xunit.Abstractions;

namespace LastEdict.Data.Tests;

/// <summary>Рамки допустимости при перетаскивании (<see cref="DropFrames"/>, <c>docs/ui-windows.md</c> §2): разбор
/// <c>0x56c58a</c>/<c>0x56eaef</c>/<c>0x56e02d</c>/<c>0x56e487</c>, правила <c>0x500d03</c>/<c>0x56e75e</c>.
/// <para>Окна — на начале партии импортированного сценария Power to Corrupt за Горные кланы (<see cref="NewGame"/>: разметка экранов, строки и
/// правила импорта; без импорта тест пропускается). В эталоне окна открывались на сохранениях корпуса той же карты (дни 5 и 24): здесь вместо
/// них — день 1, в котором человек сам переставил трёх воинов гарнизона столицы в отряд героя (<see cref="GameSession.SwapUnits"/>), а второй
/// отряд для окна обмена собран рядом из копий героя и воина. Проверки — те же свойства окна, не числа сохранения.</para>
/// <para>Не перенесены харнессы по журналам зонда <c>swap_check</c> (<c>UnitFrames_MatchJournalSwapChecks</c>) и <c>potion_use</c>
/// (<c>CityStack_PotionsMatchJournal</c>): журналы и сохранения корпуса есть только в эталоне, а сверка с оригиналом — его работа
/// (<c>CLAUDE.md</c>).</para></summary>
public partial class DropFramesTests(ITestOutputHelper output)
{
    // --- окна ---

    /// <summary>Рамка — место ячейки (колонки), картинка по середине: у 74×89 сдвиг −2,−2, у 152×85 — −1,0 (<c>0x56e487</c>); элемент
    /// стоит после портрета и подписи и до значков ячейки (<c>0x56e02d</c>: рамка — в первом проходе, значки <c>0x56e2e7</c> — во втором).</summary>
    [Fact]
    public void Layout_FrameElementsCenteredBetweenTextAndIcons()
    {
        var first = new UiRect(190, 68, 70, 85);
        var screen = UnitGroupLayout.Expand(new UiScreen { Id = "T", Elements = [new UiElement { Id = "IMG_LSLOT1", Kind = UiKind.Image, Rect = first }] },
                                            "IMG_LSLOT1", "L", leftSide: true);
        var ids = screen.Elements.Select(e => e.Id).ToList();
        for (int p = 0; p < UnitGroupLayout.Positions; p++)
        {
            var e = screen.Elements.First(x => x.Id == UnitGroupLayout.DropImage("L", p));
            Assert.Equal(UnitGroupLayout.Cell(first, true, p), e.Rect);
            int at = ids.IndexOf(e.Id);
            Assert.True(at > ids.IndexOf(UnitGroupLayout.CellText("L", p)));
            Assert.True(at < ids.IndexOf(UnitGroupLayout.Icon("L", p.ToString(System.Globalization.CultureInfo.InvariantCulture), UnitGroupLayout.UpgradeIcon)));
        }
        for (int row = 0; row < 3; row++)
        {
            var e = screen.Elements.First(x => x.Id == UnitGroupLayout.BigDropImage("L", row));
            Assert.Equal(UnitGroupLayout.Column(first, true, row), e.Rect);
            Assert.True(ids.IndexOf(e.Id) > ids.IndexOf(UnitGroupLayout.BigText("L", row)));
            Assert.True(ids.IndexOf(e.Id) < ids.IndexOf(UnitGroupLayout.Icon("L", $"B{row}", UnitGroupLayout.UpgradeIcon)));
        }
        Assert.Equal((-2, -2), UiImagePlacement.Offset(UiImageAlign.Center, 70, 85, 74, 89));
        Assert.Equal((-1, 0), UiImagePlacement.Offset(UiImageAlign.Center, 150, 85, 152, 85));
        Assert.Equal("DRAGDROP_LARGE_INVALID", DropFrames.Image(DropFrame.Invalid, large: true));
        Assert.Equal("DRAGDROP_SMALL_VALID", DropFrames.Image(DropFrame.Valid, large: false));
        Assert.Null(DropFrames.Image(DropFrame.None, large: true));
    }

    /// <summary>
    /// Окно армии столицы (гость — герой и три воина, гарнизон — большой в колонке и страж): до нажатия рамок нет; нажатие на воина — рамки у
    /// обеих групп сразу; после броска — снова нет. Рамка и бросок не расходятся: жёлтая там и только там, где бросок переставляет клетки (или
    /// это своя клетка/колонка — обмен с собой). У большого — одна рамка на колонку (<c>VV</c>).
    /// </summary>
    [SkippableFact]
    public void City_UnitDrag_FramesAgreeWithDrop()
    {
        var probe = City(CitySession());
        Assert.Equal("......", DropFrames.Line(probe.Ui, CityStackLogic.Visitor));
        Assert.Equal("......", DropFrames.Line(probe.Ui, CityStackLogic.Garrison));
        var sources = new List<(string Group, int Pos)>();
        foreach (string g in (string[])[CityStackLogic.Visitor, CityStackLogic.Garrison])
            for (int p = 0; p < UnitGroupLayout.Positions; p++)
                if (GroupCells(probe, g) is { } cells && cells.Cells[p].Length > 0 && cells.PositionOf(cells.Cells[p]) == p) sources.Add((g, p));
        int valid = 0, none = 0, big = 0;
        foreach (var from in sources)
        {
            var w = City(CitySession());
            var (fx, fy) = CityCell(w.Ui, from.Group, from.Pos);
            w.Logic.Press(fx, fy, w.Ui);
            string l = DropFrames.Line(w.Ui, CityStackLogic.Visitor), r = DropFrames.Line(w.Ui, CityStackLogic.Garrison);
            output.WriteLine($"{from.Group}{from.Pos}: L={l} R={r}");
            if (r.Contains("VV", StringComparison.Ordinal) || l.Contains("VV", StringComparison.Ordinal)) big++;
            w.Logic.Release(fx, fy, w.Ui);
            Assert.Equal("......", DropFrames.Line(w.Ui, CityStackLogic.Visitor));
            foreach (string g in (string[])[CityStackLogic.Visitor, CityStackLogic.Garrison])
                for (int p = 0; p < UnitGroupLayout.Positions; p++)
                {
                    char frame = char.ToLowerInvariant((g == CityStackLogic.Visitor ? l : r)[p]);
                    var drop = City(CitySession());
                    string before = Both(drop);
                    drop.Logic.Press(fx, fy, drop.Ui);
                    var (tx, ty) = CityCell(drop.Ui, g, p);
                    drop.Logic.Release(tx, ty, drop.Ui);
                    bool moved = Both(drop) != before;
                    bool own = g == from.Group && SameColumn(GroupCells(probe, g)!, from.Pos, p);
                    Assert.True(frame == 'v' == (moved || own), $"{from.Group}{from.Pos} → {g}{p}: рамка {frame}, бросок {(moved ? "переставил" : "нет")}");
                    if (frame == 'v') valid++;
                    else none++;
                }
        }
        output.WriteLine($"источников {sources.Count}: жёлтых {valid}, без рамки {none}, с колонкой большого {big}");
        Assert.True(valid > 0 && none > 0 && big > 0);
    }

    /// <summary>Инструменты окна армии (<c>CMidDropAction</c>): рамки, пока инструмент нажат. Лечение — раненый жёлтая, здоровый и павший
    /// красная; воскрешение — павший жёлтая, живой красная; без храма — рамок нет; «Остановить продвижение» — лидер и страж красная;
    /// отпуск — лидер при воинах без рамки, страж гарнизона красная.</summary>
    [SkippableFact]
    public void City_Tools_Frames()
    {
        var session = CitySession();
        var w = City(session);
        var visitor = GroupCells(w, CityStackLogic.Visitor)!;
        var fort = session.Map.Sites.First(s => s.Id == w.Logic.FortId);
        string leader = session.Map.Stacks[fort.StackId].LeaderId;
        int lead = visitor.PositionOf(leader);
        int[] others = [.. Enumerable.Range(0, 6).Where(p => visitor.Cells[p].Length > 0 && p != lead)];
        Assert.True(others.Length >= 2);
        int wounded = others[0], dead = others[1];
        var units = session.Map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (string id in visitor.Units) units[id] = units[id] with { Hp = session.HpMax(units[id]) };
        units[visitor.Cells[wounded]] = units[visitor.Cells[wounded]] with { Hp = 1 };
        units[visitor.Cells[dead]] = units[visitor.Cells[dead]] with { Hp = 0 };
        var human = session.Map.Players[fort.Owner];
        var book = session.RequireBook();
        string temple = book.Economy.Buildings.First(b => b.Value.Category == Healing.TempleCategory).Key;
        var players = session.Map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[human.Id] = human with { Bank = new Bank(99999, 0, 0, 0, 0, 0), Buildings = [.. human.Buildings, temple] };
        w = City(Rebuilt(session, GameSession.With(session.Map, units: units, players: players)));

        string Tool(string toggle)
        {
            w.Logic.Handle(new UiToggled(toggle, true), w.Ui);
            string line = DropFrames.Line(w.Ui, CityStackLogic.Visitor) + "|" + DropFrames.Line(w.Ui, CityStackLogic.Garrison);
            w.Logic.Handle(new UiToggled(toggle, false), w.Ui);
            output.WriteLine($"{toggle}: {line}");
            return line;
        }

        string heal = Tool("TOG_HEAL");
        Assert.Equal('v', heal[wounded]);
        Assert.Equal('i', heal[dead]);
        Assert.Equal('i', heal[lead]);   // лидер здоров
        string revive = Tool("TOG_REVIVE");
        Assert.Equal('v', revive[dead]);
        Assert.Equal('i', revive[wounded]);
        Assert.All(Enumerable.Range(0, 6).Where(p => visitor.Cells[p].Length == 0), p => Assert.Equal('.', revive[p]));
        string grow = Tool("TOG_DYNLEVEL");
        Assert.Equal('i', grow[lead]);
        Assert.Equal('v', grow[wounded]);
        string dismiss = Tool("TOG_DISMISS");
        Assert.Equal('.', dismiss[lead]);   // лидер при воинах (0x5df266 код 5)
        Assert.Equal('v', dismiss[wounded]);
        Assert.Equal("......|......", DropFrames.Line(w.Ui, CityStackLogic.Visitor) + "|" + DropFrames.Line(w.Ui, CityStackLogic.Garrison));
        var garrison = GroupCells(w, CityStackLogic.Garrison)!;
        int guardian = Enumerable.Range(0, 6).FirstOrDefault(p => garrison.Cells[p].Length > 0
            && book.CategoryOf(session.Map.Units[garrison.Cells[p]].TypeId) == LastEdict.Rules.Battle.UnitCategory.Guardian, -1);
        output.WriteLine($"страж гарнизона: {guardian}");
        if (guardian >= 0)
        {
            int at = UnitGroupLayout.Positions + 1 + guardian;
            Assert.Equal('i', char.ToLowerInvariant(dismiss[at]));
            Assert.Equal('i', char.ToLowerInvariant(grow[at]));
        }

        // Без храма лечения и воскрешения рамок нет (0x5dcc28).
        players[human.Id] = human with { Buildings = [.. human.Buildings.Where(b => !(book.Economy.Buildings.TryGetValue(b, out var t) && t.Category == Healing.TempleCategory))] };
        w = City(Rebuilt(session, GameSession.With(session.Map, units: units, players: players)));
        Assert.Equal("......|......", Tool("TOG_HEAL"));
    }

    /// <summary>
    /// Окно отряда героя: воин — жёлтые рамки у всех шести позиций своей группы (пустых тоже); зелье лечения из сумки — раненый жёлтая,
    /// здоровый и павший красная, пустая позиция — нет (<c>0x5de495</c>); отпуск — лидер при воинах без рамки.
    /// </summary>
    [SkippableFact]
    public void ManageStack_UnitPotionAndTool_Frames()
    {
        const string instance = "S073IMfffd";
        var session = CitySession();
        string stackId = Guest(session).Id;
        var stack = session.Map.Stacks[stackId];
        var cells = session.GroupCells(stackId)!;
        int lead = cells.PositionOf(stack.LeaderId);
        int[] others = [.. Enumerable.Range(0, 6).Where(p => cells.Cells[p].Length > 0 && p != lead && cells.PositionOf(cells.Cells[p]) == p)];
        Assert.True(others.Length >= 2);
        var potion = session.Book!.Items.Values.First(i => i.Category == LastEdict.Rules.Magic.ItemCategory.PotionHeal);
        var stacks = session.Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stackId] = stack with { Items = [.. stack.Items, instance] };
        var units = session.Map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (string id in cells.Units) units[id] = units[id] with { Hp = session.HpMax(units[id]) };
        units[cells.Cells[others[0]]] = units[cells.Cells[others[0]]] with { Hp = 1 };
        units[cells.Cells[others[1]]] = units[cells.Cells[others[1]]] with { Hp = 0 };
        var types = session.Map.ItemTypes.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        types[instance] = potion.Id;
        session = Rebuilt(session, GameSession.With(session.Map, stacks: stacks, units: units, itemTypes: types));
        var c = Context(session, stack.Owner);
        var logic = new ManageStackLogic(c, stackId, Screens(), StackTexts.Empty);
        var ui = new UiScreenState(logic.Shape(Screens().First(s => s.Id == ManageStackLogic.Screen)));
        logic.Open(ui);
        Assert.Equal("......", DropFrames.Line(ui, ManageStackLogic.Group));

        var (ux, uy) = SlotCell(ui, others[0]);
        logic.Press(ux, uy, ui);
        string unit = DropFrames.Line(ui, ManageStackLogic.Group);
        output.WriteLine($"воин {others[0]}: {unit} | {UnitCellsView.Line(c, stackId)}");
        Assert.All(unit, ch => Assert.Equal('v', char.ToLowerInvariant(ch)));
        logic.Release(ux, uy, ui);
        Assert.Equal("......", DropFrames.Line(ui, ManageStackLogic.Group));

        Press(logic, ui, instance);
        string drink = DropFrames.Line(ui, ManageStackLogic.Group);
        output.WriteLine($"зелье: {drink}");
        Assert.Equal('v', drink[others[0]]);
        Assert.Equal('i', drink[others[1]]);
        Assert.Equal('i', char.ToLowerInvariant(drink[lead]));
        Assert.All(Enumerable.Range(0, 6).Where(p => cells.Cells[p].Length == 0), p => Assert.Equal('.', drink[p]));
        logic.Release(10, 10, ui);
        Assert.Equal("......", DropFrames.Line(ui, ManageStackLogic.Group));

        logic.Handle(new UiToggled("TOG_DISMISS", true), ui);
        string dismiss = DropFrames.Line(ui, ManageStackLogic.Group);
        output.WriteLine($"отпуск: {dismiss}");
        Assert.Equal('.', char.ToLowerInvariant(dismiss[lead]));
        Assert.Equal('v', dismiss[others[0]]);
    }

    /// <summary>
    /// «Нельзя»: у полной чужой группы пустые позиции без рамки (обмен с занятой — жёлтая), лидер на месте цели — без рамки
    /// (<c>0x56e839</c>); тащат лидера — у чужой группы рамок нет вовсе (код 3), своя — все жёлтые. Окно обмена: слева отряд героя (полон —
    /// лидерство 4, юнитов 4), справа второй отряд.
    /// </summary>
    [SkippableFact]
    public void Exchange_LeadershipAndLeader_NoFrame()
    {
        var (session, main, second) = TwoStacks();
        var stacks = session.Map.Stacks;
        var mainCells = session.GroupCells(main)!;
        bool full = mainCells.Occupied >= session.GroupCapacity(main);
        var c = Context(session, stacks[main].Owner);
        var logic = new ExchangeLogic(c, main, second, Screens());
        var ui = new UiScreenState(logic.Shape(Screens().First(s => s.Id == ExchangeLogic.Screen)));
        logic.Open(ui);
        var secondCells = session.GroupCells(second)!;
        int from = Enumerable.Range(0, 6).First(p => secondCells.Cells[p].Length > 0 && secondCells.Cells[p] != stacks[second].LeaderId
                                                     && secondCells.PositionOf(secondCells.Cells[p]) == p);
        var (fx, fy) = ExchangeCell(ui, ExchangeLogic.Right, from);
        logic.Press(fx, fy, ui);
        string l = DropFrames.Line(ui, ExchangeLogic.Left), r = DropFrames.Line(ui, ExchangeLogic.Right);
        output.WriteLine($"воин R{from}: L={l} R={r} L-клетки={UnitCellsView.Line(c, main)} полон={full}");
        Assert.Equal('.', char.ToLowerInvariant(l[mainCells.PositionOf(stacks[main].LeaderId)]));
        Assert.True(full);
        Assert.All(Enumerable.Range(0, 6).Where(p => mainCells.Cells[p].Length == 0), p => Assert.Equal('.', l[p]));
        Assert.All(r, ch => Assert.Equal('v', char.ToLowerInvariant(ch)));
        logic.Release(fx, fy, ui);

        int leader = secondCells.PositionOf(stacks[second].LeaderId);
        (fx, fy) = ExchangeCell(ui, ExchangeLogic.Right, leader);
        logic.Press(fx, fy, ui);
        output.WriteLine($"лидер R{leader}: L={DropFrames.Line(ui, ExchangeLogic.Left)} R={DropFrames.Line(ui, ExchangeLogic.Right)}");
        Assert.Equal("......", DropFrames.Line(ui, ExchangeLogic.Left));
        Assert.All(DropFrames.Line(ui, ExchangeLogic.Right), ch => Assert.Equal('v', char.ToLowerInvariant(ch)));
    }

    /// <summary>Наёмник из списка (хвост <c>0x500d03</c>): малый — пустые позиции при свободных клетках; большой — пустая колонка и больше
    /// одной свободной клетки; красной нет.</summary>
    [Fact]
    public void Mercenary_FramesByFreeCells()
    {
        var cells = new UnitCells(["lead", "a"], ["", "", "lead", "", "a", ""]);
        Assert.Equal("vv.v.v", Line(p => DropFrames.Mercenary(false, 2, cells, p)));
        Assert.Equal("......", Line(p => DropFrames.Mercenary(false, 0, cells, p)));
        Assert.Equal("vv....", Line(p => DropFrames.Mercenary(true, 2, cells, p)));
        Assert.Equal("......", Line(p => DropFrames.Mercenary(true, 1, cells, p)));
    }

    private static string Line(Func<int, DropFrame> state) =>
        new([.. Enumerable.Range(0, 6).Select(p => state(p) switch { DropFrame.Valid => 'v', DropFrame.Invalid => 'i', _ => '.' })]);

    // --- партия ---

    /// <summary>Сценарий импорта и раса человека: Power to Corrupt за Горные кланы (тип расы 3) — та же карта и раса, что у сохранений
    /// эталона.</summary>
    private const string Scenario = "Power to Corrupt";
    private const int Clans = 3;

    private static readonly Lazy<GameContent> Content = new(() => SharedContent.Load(ImportedData.ContentDir()));
    private static readonly Lazy<GameBook> Book = new(() => GameBook.Load(ImportedData.Content));
    /// <summary>Строки импорта: русские, если копия игры русская, иначе английские (тестам важно лишь, что сообщение — та же строка).</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Strings = new(() =>
    {
        string dir = Path.Combine(ImportedData.Dir, "ui");
        return UiStrings.Load(dir, File.Exists(Path.Combine(dir, "strings.ru.json")) ? "ru" : "en");
    });
    private static readonly Lazy<UnitFaces> Faces = new(() => UnitFaces.Load(ImportedData.PathOf("rules")));

    /// <summary>День 1 партии за кланы, в котором человек переставил три малых воина гарнизона (не стражей) из столицы в отряд героя.</summary>
    private static readonly Lazy<GameState> CityDay = new(() =>
    {
        var data = ScenarioFile.Read(ScenarioFile.PathOf(ImportedData.PathOf("scenarios"), Scenario));
        var catalog = Content.Value.Catalog;
        var setup = new NewGameSetup { Scenario = catalog.Scenario(data.Id), RaceId = BaseRaces.Clans, LordCategory = 1, Difficulty = 1 };
        var session = Session(NewGame.Start(data, setup, catalog, Book.Value, new Random(1)));
        var capital = Capital(session);
        string guest = capital.StackId;
        for (int moved = 0; moved < 3; moved++)
        {
            var garrison = session.GroupCells(capital.Id)!;
            int from = Enumerable.Range(0, UnitCells.CellCount).First(p => garrison.Cells[p] is { Length: > 0 } id
                && garrison.PositionOf(id) == p && garrison.Cells[Formation.Pair(p)] != id
                && Book.Value.CategoryOf(session.Map.Units[id].TypeId) != LastEdict.Rules.Battle.UnitCategory.Guardian);
            var visitor = session.GroupCells(guest)!;
            int to = Enumerable.Range(0, UnitCells.CellCount).First(p => visitor.Cells[p].Length == 0);
            Assert.Null(session.SwapUnits(capital.Id, from, guest, to));
        }
        return session.State;
    });

    private static void RequireImport() => ImportedData.Require([.. ImportedData.ContentFiles, "content/base", "rules", "scenarios"]);

    private static GameSession Session(GameState state) => new(state, Content.Value.MapArt, Book.Value);

    /// <summary>Сессия дня 1 с воинами в отряде героя — заново на каждый бросок (бросок меняет сессию).</summary>
    private static GameSession CitySession()
    {
        RequireImport();
        return Session(CityDay.Value);
    }

    private static MapSite Capital(GameSession session)
    {
        var human = session.Map.Players.Values.First(p => p.IsHuman);
        return session.Map.Sites.First(s => s.Kind == SiteKind.Capital && s.Owner == human.Id);
    }

    /// <summary>Отряд героя — гость столицы человека.</summary>
    private static MapStack Guest(GameSession session) => session.Map.Stacks[Capital(session).StackId];

    /// <summary>Отряд героя (<c>Main</c>, полон) и второй отряд человека рядом со столицей (<c>Second</c>): копия героя лидером и копия воина.</summary>
    private static (GameSession Session, string Main, string Second) TwoStacks()
    {
        var session = CitySession();
        var main = Guest(session);
        var units = session.Map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var leader = units[main.LeaderId] with { Id = "S073UNfff0" };
        var soldier = units[main.UnitIds.First(u => !u.Equals(main.LeaderId, StringComparison.OrdinalIgnoreCase))] with { Id = "S073UNfff1" };
        units[leader.Id] = leader;
        units[soldier.Id] = soldier;
        var second = main with
        {
            Id = "S073KCfff0", LeaderId = leader.Id, UnitIds = [leader.Id, soldier.Id], Slots = [-1, -1, 0, 1, -1, -1],
            Inside = "", X = main.X + 1, Y = main.Y, Items = [],
        };
        var stacks = session.Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[second.Id] = second;
        return (Rebuilt(session, GameSession.With(session.Map, stacks: stacks, units: units)), main.Id, second.Id);
    }

    private static GameSession Rebuilt(GameSession session, MapState map) =>
        new(session.State with { Looks = GameSession.Retold(session.Looks, map, session.Looks.Stacks) }, session.Art, session.Book);

    private static IReadOnlyList<UiScreen> Screens() => Content.Value.Screens;

    private static WindowContext Context(GameSession session, string viewer) => new(session, viewer, Strings.Value, Faces.Value);

    private sealed record CityWindow(CityStackLogic Logic, UiScreenState Ui, GameSession Session);

    private static CityWindow City(GameSession session)
    {
        var capital = Capital(session);
        var logic = new CityStackLogic(Context(session, capital.Owner), capital.Id, Screens());
        var ui = new UiScreenState(logic.Shape(Screens().First(s => s.Id == CityStackLogic.Screen)));
        logic.Open(ui);
        return new CityWindow(logic, ui, session);
    }

    private static UnitCells? GroupCells(CityWindow w, string group)
    {
        string id = group == CityStackLogic.Visitor ? w.Session.Map.Sites.First(s => s.Id == w.Logic.FortId).StackId : w.Logic.FortId;
        return id.Length > 0 ? w.Session.GroupCells(id) : null;
    }

    private static string Both(CityWindow w) =>
        $"{string.Join(",", GroupCells(w, CityStackLogic.Visitor)?.Cells ?? [])}|{string.Join(",", GroupCells(w, CityStackLogic.Garrison)?.Cells ?? [])}";

    private static bool SameColumn(UnitCells cells, int from, int to)
    {
        bool big = cells.Cells[from] == cells.Cells[Formation.Pair(from)]
                   || (cells.Cells[to].Length > 0 && cells.Cells[to] == cells.Cells[Formation.Pair(to)]);
        return big ? Formation.ColumnFront(from) == Formation.ColumnFront(to) : from == to;
    }

    private static (int X, int Y) CityCell(UiScreenState ui, string group, int pos) =>
        GroupCell(ui, group == CityStackLogic.Visitor, pos);

    private static (int X, int Y) ExchangeCell(UiScreenState ui, string side, int pos) => GroupCell(ui, side == ExchangeLogic.Left, pos);

    private static (int X, int Y) GroupCell(UiScreenState ui, bool left, int pos)
    {
        var first = ui.Screen.Elements.First(e => e.Id == (left ? "IMG_LSLOT1" : "IMG_RSLOT1")).Rect;
        return Center(UnitGroupLayout.Cell(first, left, pos));
    }

    private static (int X, int Y) Center(UiRect r) => (r.X + (r.Width / 2), r.Y + (r.Height / 2));
}
