using LastEdict.Data.Play;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;
using LastEdict.Rules.Magic;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Tests;

public partial class DropFramesTests
{
    /// <summary>
    /// Свиток и посох перетаскиванием на лидера в окне отряда (<c>CDDStackGroup</c>: рамка <c>0x500d03</c>, бросок <c>0x4ff396</c>, ворота
    /// <c>0x5de6dc</c>; ответ пользователя 17.10). Отряд героя с воинами; навык посохов и свитков (<see cref="GameSession.WandScrollAbility"/>)
    /// герою дан модификатором правил с этой способностью — в эталоне такой герой был в сохранении дня 24. Тащат свиток из сумки — рамка
    /// жёлтая только у лидера, у прочих воинов нет; картинка курсора — значок свитка; бросок на лидера закрывает окно и ставит прицел
    /// (<see cref="UiCast"/> с отрядом и предметом), на простого воина — ничего. Павший лидер — рамка красная, бросок — <c>X005TA0645</c>
    /// (свиток) или <c>X005TA0644</c> (посох), литья нет.
    /// </summary>
    [SkippableFact]
    public void ManageStack_ScrollAndWandOnLeader_StartAim()
    {
        const string scroll = "S073IMfee1", wand = "S073IMfee2";
        var session = CitySession();
        var stack = Guest(session);
        var book = session.Book!;
        var lore = book.Rules.Modifiers!.Values.First(m => m.Elements.Any(e => e.Type == ModifierElementType.LeaderAbility
                                                                               && e.Ability == GameSession.WandScrollAbility));
        bool Local(Item i) => i.SpellId.Length > 0 && book.Spells.TryGetValue(i.SpellId, out var sp) && sp.Area is > 0 and < 999;
        var units = session.Map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[stack.LeaderId] = units[stack.LeaderId] with { Modifiers = [.. units[stack.LeaderId].Modifiers, lore.Id] };
        var stacks = session.Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stack.Id] = stack with { Items = [.. stack.Items, scroll, wand] };
        var types = session.Map.ItemTypes.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        types[scroll] = book.Items.Values.First(i => i.Category == ItemCategory.Scroll && Local(i)).Id;
        types[wand] = book.Items.Values.First(i => i.Category == ItemCategory.Wand && Local(i)).Id;
        var players = session.Map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var owner = players[stack.Owner];
        players[owner.Id] = owner with { Bank = owner.Bank with { Infernal = 100_000, Life = 100_000, Death = 100_000, Runic = 100_000, Grove = 100_000 } };
        session = Rebuilt(session, GameSession.With(session.Map, stacks: stacks, units: units, itemTypes: types, players: players));
        stack = session.Map.Stacks[stack.Id];
        Assert.Contains(GameSession.WandScrollAbility, session.LeaderAbilitiesOf(stack));

        var (logic, ui) = Manage(session, stack.Id);
        var cells = session.GroupCells(stack.Id)!;
        int leader = cells.PositionOf(stack.LeaderId);
        int soldier = cells.PositionOf(stack.UnitIds.First(u => !u.Equals(stack.LeaderId, StringComparison.OrdinalIgnoreCase)));

        Press(logic, ui, scroll);
        string line = DropFrames.Line(ui, ManageStackLogic.Group);
        output.WriteLine($"свиток {types[scroll]}: рамки {line}, тащимое {logic.DragImage}");
        Assert.Equal('v', char.ToLowerInvariant(line[leader]));
        Assert.Equal('.', char.ToLowerInvariant(line[soldier]));
        Assert.Equal(ItemCells.Icon(scroll, Context(session, stack.Owner)), logic.DragImage);
        var at = SlotCell(ui, soldier);
        Assert.False(logic.DropAccepts(at.X, at.Y, ui));
        at = SlotCell(ui, leader);
        Assert.True(logic.DropAccepts(at.X, at.Y, ui));
        logic.Release(at.X, at.Y, ui);
        var requests = Requests(logic);
        Assert.Contains(requests, r => r is UiClose);
        var cast = Assert.Single(requests.OfType<UiCast>());
        Assert.Equal((book.Items[types[scroll]].SpellId, stack.Id, scroll), (cast.SpellId, cast.StackId, cast.ItemId));
        Assert.Contains(scroll, session.Map.Stacks[stack.Id].Items);   // свиток тратит литьё по цели, не бросок

        // Павший лидер: рамка красная, сообщение «Герой мёртв», литья нет.
        units = session.Map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[stack.LeaderId] = units[stack.LeaderId] with { Hp = 0 };
        session = Rebuilt(session, GameSession.With(session.Map, units: units));
        (logic, ui) = Manage(session, stack.Id);
        foreach (var (item, text) in (ReadOnlySpan<(string, string)>)[(scroll, "X005TA0645"), (wand, "X005TA0644")])
        {
            Press(logic, ui, item);
            Assert.Equal('i', char.ToLowerInvariant(DropFrames.Line(ui, ManageStackLogic.Group)[leader]));
            logic.Release(at.X, at.Y, ui);
            requests = Requests(logic);
            Assert.DoesNotContain(requests, r => r is UiCast or UiClose);
            Assert.Contains(requests.OfType<UiTell>(), t => t.Text == Strings.Value[text]);
        }
    }

    private static (ManageStackLogic Logic, UiScreenState Ui) Manage(GameSession session, string stackId)
    {
        var logic = new ManageStackLogic(Context(session, session.Map.Stacks[stackId].Owner), stackId, Screens(), StackTexts.Empty);
        var ui = new UiScreenState(logic.Shape(Screens().First(s => s.Id == ManageStackLogic.Screen)));
        logic.Open(ui);
        return (logic, ui);
    }

    /// <summary>Середина ячейки позиции группы окна отряда (угол — <c>IMG_SLOT1</c>).</summary>
    private static (int X, int Y) SlotCell(UiScreenState ui, int pos) =>
        Center(UnitGroupLayout.Cell(ui.Screen.Elements.First(e => e.Id == "IMG_SLOT1").Rect, true, pos));

    private static void Press(ManageStackLogic logic, UiScreenState ui, string item)
    {
        int index = logic.BagCells.ToList().FindIndex(c => c.Ids.Contains(item, StringComparer.OrdinalIgnoreCase));
        var (x, y) = ListCell(ui, "MLBOX_INVENTORY", index);
        logic.Press(x, y, ui);
    }

    private static List<UiRequest> Requests(IWindowLogic logic)
    {
        var list = new List<UiRequest>();
        while (logic.TakeRequest() is { } r) list.Add(r);
        return list;
    }
}
