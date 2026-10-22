using LastEdict.Data.Play;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Перенос в следующую главу (<c>DLG_CARRY_ITEM_LEADER</c>, <c>CCarryOverInterf</c> <c>0x4812ae</c>; разбор — <c>docs/day-cycle.md</c>
/// §15). Слева — герои игрока (<c>LBOX_LEADER</c>, большое лицо; стрелки листают) и вещи всех его городов и отрядов по типу
/// (<c>LBOX_ITEMS</c>: значок, имя, цена); справа — выбранный герой (<c>IMG_LEADER_KEEP</c>, <c>TXT_LEADER_INFO</c>) и до пяти вещей
/// (<c>LBOX_ITEMS_KEEP</c>). Герой и вещь переносятся только перетаскиванием (окно — <c>CDragAndDropInterf</c>: все четыре элемента —
/// источник и цель, двойного щелчка нет; так же помнит пользователь), обратно — тоже перетаскиванием (герой из слота — в список,
/// вещь — из «оставить» в общий список). «Закрыть» (<c>0x481653</c>) спрашивает: нет героя и вещей меньше пяти — <c>X005TA0299</c>, нет героя —
/// <c>X005TA0296</c>, вещей 0–1 — <c>X005TA0298</c>, меньше пяти — <c>X005TA0297</c>, иначе <c>X005TA0295</c>; «Да» — перенос
/// (<see cref="GameSession.CarryOverOf"/>) уходит в <paramref name="done"/>.
/// </summary>
public sealed class CarryOverLogic(WindowContext context, Action<CarryOver> done) : IWindowLogic
{
    public const string Screen = "DLG_CARRY_ITEM_LEADER";
    private const string Leaders = "LBOX_LEADER", Items = "LBOX_ITEMS", Keep = "LBOX_ITEMS_KEEP", KeepFace = "IMG_LEADER_KEEP";
    private const int IconSize = 85, ItemText = 95;

    private readonly UiRequests _requests = new();
    private IReadOnlyList<MapStack> _heroes = [];
    private List<string> _items = [];
    private readonly List<string> _kept = [];
    private (char List, int Index)? _drag;

    /// <summary>Выбранный герой (отряд); пусто — без героя.</summary>
    public string Hero { get; private set; } = "";

    public IReadOnlyList<string> Kept => _kept;

    public string Summary => $"перенос герой={(Hero.Length > 0 ? Hero : "-")} вещей={_kept.Count}";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _heroes = [.. context.Map.Stacks.Values.Where(s => s.Owner.Equals(context.Viewer, StringComparison.OrdinalIgnoreCase) && s.LeaderAlive
                                                           && context.Map.Units.ContainsKey(s.LeaderId))];
        _items = [.. context.Session.CarryItems(context.Viewer)];
        ui.SetText("TXT_TITLE", context.Text("X005TA0292"));
        ui.SetText("TXT_SELECTED_LEADER", context.Text("X005TA0293"));
        ui.Select(Leaders, 0);
        Refresh(ui);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        switch (e)
        {
            case UiListSelected { Element: Leaders }:
                Refresh(ui);
                break;
            case UiClicked { Element: "BTN_CLOSE" }:
                Ask();
                break;
        }
    }

    public UiRequest? TakeRequest() => _requests.Take();

    /// <summary>«Закрыть» ведёт само окно (вопрос и перенос): переход графа <c>BTN_CLOSE → DLG_MESSAGE_BOX</c> открыл бы второе окно.</summary>
    public bool Allows(string element, UiScreenState ui) => element != "BTN_CLOSE";

    public void Press(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _drag = Cell(ui, Leaders, _heroes.Count, x, y) is { } h ? ('h', h)
              : Cell(ui, Items, Available.Count, x, y) is { } i ? ('i', i)
              : Cell(ui, Keep, _kept.Count, x, y) is { } k ? ('k', k)
              : Inside(ui, KeepFace, x, y) && Hero.Length > 0 ? ('f', 0) : null;
    }

    public void Release(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var drag = _drag;
        _drag = null;
        switch (drag)
        {
            case ('h', var h) when Inside(ui, KeepFace, x, y): ChooseHero(h); break;
            case ('f', _) when !Inside(ui, KeepFace, x, y): Hero = ""; break;
            case ('i', var i) when Inside(ui, Keep, x, y): Take(i); break;
            case ('k', var k) when !Inside(ui, Keep, x, y): Give(k); break;
            default: return;
        }
        Refresh(ui);
    }

    /// <summary>Примет ли место тащимое (как <see cref="Release"/>): герой — слот героя; вещь — список «оставить»; из слота и «оставить» —
    /// любое место вне них. Не тащат — null.</summary>
    public bool? DropAccepts(int x, int y, UiScreenState ui) => _drag switch
    {
        null => null,
        ('h', _) => Inside(ui, KeepFace, x, y),
        ('f', _) => !Inside(ui, KeepFace, x, y),
        ('i', _) => Inside(ui, Keep, x, y) && _kept.Count < CarryOver.MaxItems,
        _ => !Inside(ui, Keep, x, y),
    };

    /// <summary>Кадр тащимого (<see cref="IWindowLogic.DragImage"/>, окно — <c>CDragAndDropInterf</c>): лицо героя или значок вещи.</summary>
    public string? DragImage => _drag switch
    {
        ('h', var h) when h < _heroes.Count => Face(_heroes[h]),
        ('f', _) when _heroes.FirstOrDefault(s => s.Id == Hero) is { } hero => Face(hero),
        ('i', var i) when Available is var free && i < free.Count => Icon(free[i]),
        ('k', var k) when k < _kept.Count => Icon(_kept[k]),
        _ => null,
    };

    private string? Face(MapStack hero) =>
        context.Map.Units.TryGetValue(hero.LeaderId, out var leader) ? context.Faces.Large(leader.TypeId) : null;

    /// <summary>Вещи, ещё не выбранные для переноса.</summary>
    private List<string> Available => [.. _items.Where(i => !_kept.Contains(i))];

    private void ChooseHero(int index)
    {
        if (index >= 0 && index < _heroes.Count) Hero = _heroes[index].Id;
    }

    private void Take(int index)
    {
        var free = Available;
        if (index >= 0 && index < free.Count && _kept.Count < CarryOver.MaxItems) _kept.Add(free[index]);
    }

    private void Give(int index)
    {
        if (index >= 0 && index < _kept.Count) _kept.RemoveAt(index);
    }

    private void Ask()
    {
        int n = _kept.Count;
        string text = Hero.Length == 0
            ? context.Text(n < CarryOver.MaxItems ? "X005TA0299" : "X005TA0296")
            : n <= 1 ? UiTemplate.Fill(context.Text("X005TA0298"), ("NUMBER", n))
            : n < CarryOver.MaxItems ? UiTemplate.Fill(context.Text("X005TA0297"), ("NUMBER", n))
            : context.Text("X005TA0295");
        _requests.Add(new UiAsk(text, () =>
        {
            done(context.Session.CarryOverOf(Hero, _kept));
            _requests.Add(new UiClose());
        }));
    }

    private void Refresh(UiScreenState ui)
    {
        var textRect = new UiRect(0, 0, 0, 0);
        ui.SetCards(Leaders, [.. _heroes.Select(s => new UiCard(context.Faces.Large(context.Map.Units[s.LeaderId].TypeId), new UiPoint(0, 0), null, textRect))]);
        int at = ui.ListSelected(Leaders);
        ui.SetText("TXT_LDR_INFO", at >= 0 && at < _heroes.Count ? Info(_heroes[at]) : "");
        var hero = _heroes.FirstOrDefault(s => s.Id == Hero);
        ui.SetImage(KeepFace, hero is null ? null : context.Faces.Large(context.Map.Units[hero.LeaderId].TypeId));
        ui.SetText("TXT_LEADER_INFO", hero is null ? "" : Info(hero));
        var book = context.Session.RequireBook();
        ui.SetCards(Items, [.. Available.Select(i => new UiCard(Icon(i), new UiPoint(0, 0),
            $"{ItemName(i)}\n{book.Items.GetValueOrDefault(context.Map.ItemTypes[i])?.Value.Gold ?? 0}",
            new UiRect(ItemText, 0, 315 - ItemText, IconSize)))]);
        ui.SetCards(Keep, [.. _kept.Select(i => new UiCard(Icon(i), new UiPoint(0, 0), null, textRect))]);
    }

    private string Info(MapStack s)
    {
        var unit = context.Map.Units[s.LeaderId];
        string name = context.Session.Looks.Stacks.FirstOrDefault(l => l.Id == s.Id)?.LeaderName is { Length: > 0 } n ? n : context.UnitName(unit.TypeId);
        return UiTemplate.Fill(context.Text("X005TA0725"), ("NAME", name), ("LEVEL", unit.Level), ("NBBATTLE", s.BattlesWon));
    }

    private string Icon(string item) => ItemCells.IconFrame(context.Map.ItemTypes[item], context);

    private string ItemName(string item)
    {
        string type = context.Map.ItemTypes[item];
        return context.ItemTexts?.Items.TryGetValue(type, out var id) == true ? context.Text(id) : type;
    }

    private static bool Inside(UiScreenState ui, string id, int x, int y) =>
        ui.Screen.Elements.FirstOrDefault(e => e.Id == id)?.Rect is { } r && x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;

    /// <summary>Строка списка под точкой (с прокруткой); вне строк — null.</summary>
    private static int? Cell(UiScreenState ui, string id, int count, int x, int y)
    {
        var e = ui.Screen.Elements.FirstOrDefault(el => el.Id == id);
        if (e?.List is not { } spec || !Inside(ui, id, x, y)) return null;
        int cols = Math.Max(1, (e.Rect.Width + spec.GapX) / (spec.CellWidth + spec.GapX));
        int col = (x - e.Rect.X) / (spec.CellWidth + spec.GapX), row = (y - e.Rect.Y) / (spec.CellHeight + spec.GapY);
        int index = ui.ListTop(id) + (row * cols) + col;
        return col < cols && index < count ? index : null;
    }
}
