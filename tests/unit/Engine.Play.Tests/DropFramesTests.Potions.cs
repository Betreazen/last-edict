using LastEdict.Data.Play;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;
using LastEdict.Rules.Magic;

namespace LastEdict.Data.Tests;

/// <summary>Зелья из сумки и со склада на воинов в окнах армии и обмена (<see cref="PotionDrops"/>, рамки <see cref="DropFrames.Potion"/>):
/// разбор <c>0x500d03</c>/<c>0x4ff396</c>/<c>0x5de495</c>/<c>0x5d70f9</c>. Харнесс по журналам человека <c>potion_use</c> остался в эталоне.</summary>
public partial class DropFramesTests
{
    /// <summary>
    /// Окно армии: зелье лечения из сумки гостя — на раненого воина гарнизона (держатель — сумка, воин — чужой группы окна: <c>0x5de495</c>
    /// группу не спрашивает); со склада — на раненого гостя. Рамки при нажатии — у обеих групп; здоровый — красная, бросок на него — сообщение
    /// <c>X005TA0594</c> и зелье на месте. Зелье воскрешения на живого — рамка жёлтая (<c>0x500d03</c>: код 5 у воскрешения — 0), бросок —
    /// сообщение <c>X005TA0595</c>, зелье не тратится (ответ пользователя 15.10: «навестись можно, но не применяется»).
    /// </summary>
    [SkippableFact]
    public void City_PotionAcrossGroups_FramesAndDrop()
    {
        var session = CitySession();
        var w = City(session);
        var visitor = GroupCells(w, CityStackLogic.Visitor)!;
        var garrison = GroupCells(w, CityStackLogic.Garrison)!;
        var fort = session.Map.Sites.First(s => s.Id == w.Logic.FortId);
        string guestId = fort.StackId;
        int gPos = Enumerable.Range(0, 6).First(p => garrison.Cells[p].Length > 0 && garrison.PositionOf(garrison.Cells[p]) == p);
        int vPos = Enumerable.Range(0, 6).First(p => visitor.Cells[p].Length > 0 && visitor.PositionOf(visitor.Cells[p]) == p);
        int vFull = Enumerable.Range(0, 6).Last(p => visitor.Cells[p].Length > 0 && visitor.PositionOf(visitor.Cells[p]) == p);
        Assert.NotEqual(vPos, vFull);
        var book = session.Book!;
        var heal = book.Items.Values.First(i => i.Category == ItemCategory.PotionHeal);
        var revive = book.Items.Values.First(i => i.Category == ItemCategory.PotionRevive);
        var units = session.Map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (string id in visitor.Units.Concat(garrison.Units)) units[id] = units[id] with { Hp = session.HpMax(units[id]) };
        units[garrison.Cells[gPos]] = units[garrison.Cells[gPos]] with { Hp = 1 };
        units[visitor.Cells[vPos]] = units[visitor.Cells[vPos]] with { Hp = 1 };
        var stacks = session.Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[guestId] = stacks[guestId] with { Items = [.. stacks[guestId].Items, "S073IMfff1", "S073IMfff2"] };
        var sites = session.Map.Sites.Select(s => s.Id == fort.Id ? s with { Items = [.. s.Items, "S073IMfff3"] } : s).ToList();
        var types = session.Map.ItemTypes.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        (types["S073IMfff1"], types["S073IMfff2"], types["S073IMfff3"]) = (heal.Id, revive.Id, heal.Id);
        session = Rebuilt(session, GameSession.With(session.Map, units: units, stacks: stacks, sites: sites, itemTypes: types));

        // Сумка гостя → раненый гарнизона.
        w = City(session);
        string Press(bool store, string item)
        {
            var cells = store ? w.Logic.StoreCells : w.Logic.BagCells;
            int index = cells.ToList().FindIndex(c => c.Ids.Contains(item, StringComparer.OrdinalIgnoreCase));
            var (x, y) = ListCell(w.Ui, store ? "MLBOX_RINVENTORY" : "MLBOX_LINVENTORY", index);
            w.Logic.Press(x, y, w.Ui);
            string line = DropFrames.Line(w.Ui, CityStackLogic.Visitor) + "|" + DropFrames.Line(w.Ui, CityStackLogic.Garrison);
            output.WriteLine($"{item} ({(store ? "склад" : "сумка")}): {line}");
            return line.ToLowerInvariant();
        }
        void Drop(string group, int pos)
        {
            var (x, y) = CityCell(w.Ui, group, pos);
            w.Logic.Release(x, y, w.Ui);
        }
        string bagLine = Press(store: false, "S073IMfff1");
        Assert.Equal('v', bagLine[7 + gPos]);
        Assert.Equal('v', bagLine[vPos]);
        Assert.Equal('i', bagLine[vFull]);
        Drop(CityStackLogic.Garrison, gPos);
        Assert.Null(w.Logic.LastRefusal);
        Assert.True(session.Map.Units[garrison.Cells[gPos]].Hp > 1);
        Assert.DoesNotContain("S073IMfff1", session.Map.Stacks[guestId].Items);
        Assert.Equal("......|......", DropFrames.Line(w.Ui, CityStackLogic.Visitor) + "|" + DropFrames.Line(w.Ui, CityStackLogic.Garrison));

        // Склад → раненый гость; здоровый — красная, отказ с сообщением.
        Press(store: true, "S073IMfff3");
        Drop(CityStackLogic.Visitor, vFull);
        Assert.Equal(nameof(PotionResult.AlreadyAtFullHp), w.Logic.LastRefusal);
        Assert.Contains(Tells(w.Logic), t => t == Strings.Value["X005TA0594"]);
        Assert.Contains("S073IMfff3", session.Map.Sites.First(s => s.Id == fort.Id).Items);
        Press(store: true, "S073IMfff3");
        Drop(CityStackLogic.Visitor, vPos);
        Assert.Null(w.Logic.LastRefusal);
        Assert.True(session.Map.Units[visitor.Cells[vPos]].Hp > 1);
        Assert.DoesNotContain("S073IMfff3", session.Map.Sites.First(s => s.Id == fort.Id).Items);

        // Воскрешение живому: рамка жёлтая, бросок — X005TA0595, зелье остаётся.
        string reviveLine = Press(store: false, "S073IMfff2");
        Assert.Equal('v', reviveLine[vFull]);
        Drop(CityStackLogic.Visitor, vFull);
        Assert.Equal(nameof(PotionResult.AlreadyAlive), w.Logic.LastRefusal);
        Assert.Contains(Tells(w.Logic), t => t == Strings.Value["X005TA0595"]);
        Assert.Contains("S073IMfff2", session.Map.Stacks[guestId].Items);
    }

    /// <summary>Окно обмена: зелье лечения из сумки левого отряда — на раненого воина правого (обе группы — <c>CDDStackGroup</c>): рамка
    /// жёлтая у обеих групп, бросок лечит, зелье уходит из левой сумки.</summary>
    [SkippableFact]
    public void Exchange_PotionFromOtherBag_Heals()
    {
        const string instance = "S073IMfff4";
        var (session, main, second) = TwoStacks();
        var stacks = session.Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[main] = stacks[main] with { Items = [.. stacks[main].Items, instance] };
        var right = session.GroupCells(second)!;
        int pos = Enumerable.Range(0, 6).First(p => right.Cells[p].Length > 0 && right.PositionOf(right.Cells[p]) == p);
        var units = session.Map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[right.Cells[pos]] = units[right.Cells[pos]] with { Hp = 1 };
        var types = session.Map.ItemTypes.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        types[instance] = session.Book!.Items.Values.First(i => i.Category == ItemCategory.PotionHeal).Id;
        session = Rebuilt(session, GameSession.With(session.Map, stacks: stacks, units: units, itemTypes: types));
        var logic = new ExchangeLogic(Context(session, stacks[main].Owner), main, second, Screens());
        var ui = new UiScreenState(logic.Shape(Screens().First(s => s.Id == ExchangeLogic.Screen)));
        logic.Open(ui);
        int index = logic.BagCells(ExchangeLogic.Left).ToList().FindIndex(c => c.Ids.Contains(instance, StringComparer.OrdinalIgnoreCase));
        var (ix, iy) = ListCell(ui, "MLBOX_LINVENTORY", index);
        logic.Press(ix, iy, ui);
        string r = DropFrames.Line(ui, ExchangeLogic.Right);
        output.WriteLine($"зелье слева: L={DropFrames.Line(ui, ExchangeLogic.Left)} R={r}");
        Assert.Equal('v', r[pos]);
        var (tx, ty) = ExchangeCell(ui, ExchangeLogic.Right, pos);
        Assert.True(logic.DropAccepts(tx, ty, ui));
        logic.Release(tx, ty, ui);
        Assert.Null(logic.LastRefusal);
        Assert.True(session.Map.Units[right.Cells[pos]].Hp > 1);
        Assert.DoesNotContain(instance, session.Map.Stacks[main].Items);
        Assert.Equal("......", DropFrames.Line(ui, ExchangeLogic.Right));
    }

    // --- общее ---

    /// <summary>Точка ячейки списка картинок: список прокручен к ячейке, точка — в её левом верхнем углу.</summary>
    private static (int X, int Y) ListCell(UiScreenState ui, string list, int index)
    {
        Assert.True(index >= 0, $"нет ячейки в {list}");
        ui.Select(list, index);
        var e = ui.Screen.Elements.First(x => x.Id == list);
        var spec = e.List ?? new UiList();
        int col = index - ui.ListTop(list);
        return (e.Rect.X + (col * (spec.CellWidth + spec.GapX)) + 2, e.Rect.Y + 2);
    }

    private static List<string> Tells(IWindowLogic logic)
    {
        var tells = new List<string>();
        while (logic.TakeRequest() is { } r)
            if (r is UiTell t) tells.Add(t.Text);
        return tells;
    }
}
