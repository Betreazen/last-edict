using LastEdict.Data.Play;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;
using LastEdict.Rules.Magic;

namespace LastEdict.Data.Tests;

/// <summary>Рамки для предметов при перетаскивании (<see cref="ItemFrames"/>): слоты куклы (<c>CDDBaseEquip</c> <c>0x5713c9</c>,
/// <c>0x571b20</c>, <c>0x571c80</c>) и списки (<c>CDDFoldedInv</c>, <c>0x56cff7</c>, <c>0x5056f7</c>). Памятка пользователя 3 (партия 15.10):
/// в окне отряда знамя и зелья не подсвечивали слоты.</summary>
public partial class DropFramesTests
{
    /// <summary>
    /// Окно отряда героя: знамя из сумки — рамка только у слота знамени (жёлтая, если герой может его надеть, иначе красная), зелье — у двух
    /// боевых слотов, прочие слоты без рамки (не того рода, <c>0x57201e</c>); сумке рамки нет (тащат из неё). Бросок на слот не того рода не
    /// принимается — ни сообщения, ни перемены. Надетое из слота — слот-источник жёлтый, сумка — <c>DRAGDROP_INVENT_VALID</c>. После
    /// отпускания рамок нет.
    /// </summary>
    [SkippableFact]
    public void ManageStack_ItemFrames_SlotsAndBag()
    {
        const string banner = "S073IMfff6", potion = "S073IMfff7";
        var session = CitySession();
        var stack = Guest(session);
        string stackId = stack.Id;
        var book = session.Book!;
        var stacks = session.Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stackId] = stack with { Items = [.. stack.Items, banner, potion] };
        var types = session.Map.ItemTypes.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        types[banner] = book.Items.Values.First(i => i.Category == ItemCategory.Banner).Id;
        types[potion] = book.Items.Values.First(i => i.Category == ItemCategory.PotionHeal).Id;
        session = Rebuilt(session, GameSession.With(session.Map, stacks: stacks, itemTypes: types));
        var logic = new ManageStackLogic(Context(session, stack.Owner), stackId, Screens(), StackTexts.Empty);
        var ui = new UiScreenState(logic.Shape(Screens().First(s => s.Id == ManageStackLogic.Screen)));
        logic.Open(ui);
        string[] slots = ManageStackLogic.SlotImages;
        Assert.Equal(".......", ItemFrames.Line(ui, slots));

        string PressItem(string item)
        {
            Press(logic, ui, item);
            string line = ItemFrames.Line(ui, slots);
            output.WriteLine($"{item} ({types[item]}): слоты {line}, сумка {ui.Frame(ItemFrames.Element("MLBOX_INVENTORY")) ?? "-"}");
            return line;
        }
        (int X, int Y) Slot(int s) => Center(ui.Screen.Elements.First(e => e.Id == slots[s]).Rect);

        char bannerFrame = session.EquipCheck(stackId, banner, Equipment.SlotBanner) == EquipRefusal.Ok ? 'v' : 'i';
        Assert.Equal(bannerFrame + "......", PressItem(banner));
        Assert.Null(ui.Frame(ItemFrames.Element("MLBOX_INVENTORY")));
        var frame = ui.Screen.Elements.First(e => e.Id == ItemFrames.Element(slots[0]));
        Assert.Equal(ui.Screen.Elements.First(e => e.Id == slots[0]).Rect, frame.Rect);   // по середине слота (0x5717f7)
        int requests = 0;
        var (bx, by) = Slot(Equipment.SlotBoots);
        Assert.False(logic.DropAccepts(bx, by, ui));
        logic.Release(bx, by, ui);   // слот сапог: не того рода — бросок не принимается
        while (logic.TakeRequest() is not null) requests++;
        Assert.Equal(0, requests);
        Assert.Contains(banner, session.Map.Stacks[stackId].Items);
        Assert.Equal(".......", ItemFrames.Line(ui, slots));

        Assert.Equal("..vv...", PressItem(potion));
        var (px, py) = Slot(Equipment.SlotBattle1);
        logic.Release(px, py, ui);
        Assert.Null(logic.LastRefusal);
        Assert.Equal(potion, Equipment.Slots(session.Map.Stacks[stackId])[Equipment.SlotBattle1], ignoreCase: true);

        logic.Press(px, py, ui);   // надетое зелье: слот-источник и второй боевой — жёлтые, сумка принимает
        string worn = ItemFrames.Line(ui, slots);
        output.WriteLine($"надетое: слоты {worn}, сумка {ui.Frame(ItemFrames.Element("MLBOX_INVENTORY"))}");
        Assert.Equal("..vv...", worn);
        Assert.Equal(ItemFrames.ListValid, ui.Frame(ItemFrames.Element("MLBOX_INVENTORY")));
        logic.Release(10, 10, ui);
        Assert.Null(ui.Frame(ItemFrames.Element("MLBOX_INVENTORY")));
        Assert.Equal(".......", ItemFrames.Line(ui, slots));
    }

    /// <summary>Окно армии, кукла гостя: предмет из сумки — рамки слотов куклы и <c>DRAGDROP_INVENT_VALID</c> у склада (сумке — нет); со
    /// склада — у сумки (складу — нет). Без куклы слоты рамок не рисуют. В начале партии сумка героя пуста — в неё положено зелье лечения.</summary>
    [SkippableFact]
    public void City_ItemFrames_DollAndLists()
    {
        const string potion = "S073IMfff9";
        var session = CitySession();
        var guest = Guest(session);
        var stacks = session.Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[guest.Id] = guest with { Items = [.. guest.Items, potion] };
        var types = session.Map.ItemTypes.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        types[potion] = session.Book!.Items.Values.First(i => i.Category == ItemCategory.PotionHeal).Id;
        session = Rebuilt(session, GameSession.With(session.Map, stacks: stacks, itemTypes: types));
        var w = City(session);
        string item = w.Logic.BagCells[0].Top;
        string[] slots = ManageStackLogic.SlotImages;
        var (x, y) = ListCell(w.Ui, "MLBOX_LINVENTORY", 0);
        w.Logic.Press(x, y, w.Ui);
        Assert.Equal(".......", ItemFrames.Line(w.Ui, slots));   // куклы нет — слотов не видно
        Assert.Equal(ItemFrames.ListValid, w.Ui.Frame(ItemFrames.Element("MLBOX_RINVENTORY")));
        Assert.Null(w.Ui.Frame(ItemFrames.Element("MLBOX_LINVENTORY")));
        w.Logic.Release(10, 10, w.Ui);
        Assert.Null(w.Ui.Frame(ItemFrames.Element("MLBOX_RINVENTORY")));

        w.Logic.Handle(new UiToggled("TOG_PAPERDOLL", true), w.Ui);
        (x, y) = ListCell(w.Ui, "MLBOX_LINVENTORY", 0);
        w.Logic.Press(x, y, w.Ui);
        string line = ItemFrames.Line(w.Ui, slots);
        string want = new([.. Enumerable.Range(0, slots.Length).Select(s => ItemFrames.Slot(session, guest.Id, item, s, null) switch
        {
            DropFrame.Valid => 'v',
            DropFrame.Invalid => 'i',
            _ => '.',
        })]);
        output.WriteLine($"{item} ({session.Map.ItemTypes[item]}): слоты {line}");
        Assert.Equal(want, line);
        Assert.Contains(line, ch => ch != '.');
        w.Logic.Release(10, 10, w.Ui);
        Assert.Equal(".......", ItemFrames.Line(w.Ui, slots));
    }

    /// <summary>Окно обмена, куклы: предмет из левой сумки — рамка у правой сумки, у левой нет; слоты обеих кукол — по роду предмета.</summary>
    [SkippableFact]
    public void Exchange_ItemFrames_BothDollsAndBags()
    {
        const string potion = "S073IMfff8";
        var (session, main, second) = TwoStacks();
        var stacks = session.Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[main] = stacks[main] with { Items = [.. stacks[main].Items, potion] };
        var types = session.Map.ItemTypes.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        types[potion] = session.Book!.Items.Values.First(i => i.Category == ItemCategory.PotionHeal).Id;
        session = Rebuilt(session, GameSession.With(session.Map, stacks: stacks, itemTypes: types));
        var logic = new ExchangeLogic(Context(session, stacks[main].Owner), main, second, Screens());
        var ui = new UiScreenState(logic.Shape(Screens().First(s => s.Id == ExchangeLogic.Screen)));
        logic.Open(ui);
        logic.Handle(new UiToggled("TOG_PAPERDOLL", true), ui);
        int index = logic.BagCells(ExchangeLogic.Left).ToList().FindIndex(c => c.Ids.Contains(potion, StringComparer.OrdinalIgnoreCase));
        var (x, y) = ListCell(ui, "MLBOX_LINVENTORY", index);
        logic.Press(x, y, ui);
        string Line(string side) => ItemFrames.Line(ui, [.. ManageStackLogic.SlotImages.Select(s => $"IMG_{side}{s["IMG_".Length..]}")]);
        output.WriteLine($"зелье слева: L {Line(ExchangeLogic.Left)} R {Line(ExchangeLogic.Right)}");
        Assert.Equal("..vv...", Line(ExchangeLogic.Left));
        Assert.Matches("^\\.\\.[vi][vi]\\.\\.\\.$", Line(ExchangeLogic.Right));
        Assert.Equal(ItemFrames.ListValid, ui.Frame(ItemFrames.Element("MLBOX_RINVENTORY")));
        Assert.Null(ui.Frame(ItemFrames.Element("MLBOX_LINVENTORY")));
        logic.Release(10, 10, ui);
        Assert.Equal(".......", Line(ExchangeLogic.Left));
        Assert.Null(ui.Frame(ItemFrames.Element("MLBOX_RINVENTORY")));
    }
}
