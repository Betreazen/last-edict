using System.Globalization;
using D2.Data.Menu;
using D2.Rules.Map;
using D2.Data.Play.Commands;
using D2.Rules.Units;

namespace D2.Data.Ui.Windows;

/// <summary>Кого нанимает окно: воина в ячейку гарнизона или героя (новый отряд-гость).</summary>
public enum HireKind { Unit, Leader }

/// <summary>
/// Окно найма (<c>DLG_HIRE_LEADER_2</c>, <c>CEnrollUnitInterf</c> <c>0x4b4575</c>) — одно на воинов и героев. Заголовок
/// <c>X005TA0161</c> «Нанять воина» или <c>X005TA0160</c> «Нанять героя»; строка списка 285×85 (<c>0x4b50xx…0x4b5445</c>):
/// малый портрет слева, справа от x = 160 — имя (<c>X005TA0435</c>), цена (<c>X005TA0614</c>), у героя лидерство
/// (<c>X005TA0615</c>: движок пишет лидерство − 1, <c>0x4b5339</c> — без места самого героя).
/// Воин — вопрос <c>X005TA0285</c>, затем найм в ячейку, по которой щёлкнули (<see cref="Play.GameSession.HireUnit"/>);
/// герой-вор без гильдии — <c>X005TA0451</c>, не хватает казны — <c>X005TA0510</c>, иначе окно имени
/// (<see cref="LeaderNameLogic"/>) и найм под этим именем (<see cref="Play.GameSession.HireLeader"/>). Нанят — окно закрывается.
/// У героя последней строкой — его тип (<c>getAbilityName</c>, <c>Gunits.ABIL_TXT</c>: «Герой», «Вор»…).
/// </summary>
public sealed class HireLogic : IWindowLogic
{
    public const string Screen = "DLG_HIRE_LEADER_2";
    private const string List = "LBOX_LEADER";
    private const int TextX = 160, RowW = 285, RowH = 85;
    private const int ThiefCategory = 5, GuildCategory = 0;

    private readonly WindowContext _c;
    private readonly string _fortId;
    private readonly int? _cell;
    private readonly UiRequests _requests = new();
    private IReadOnlyList<string> _types = [];

    public HireLogic(WindowContext context, string fortId, HireKind kind, int? cell = null)
    {
        _c = context ?? throw new ArgumentNullException(nameof(context));
        _fortId = fortId;
        Kind = kind;
        _cell = cell;
    }

    public HireKind Kind { get; }

    /// <summary>Типы в списке по порядку.</summary>
    public IReadOnlyList<string> Types => _types;

    public string? LastRefusal { get; private set; }

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"найм={Kind} пунктов={_types.Count} отказ={(LastRefusal ?? "-").Replace(' ', '_')}";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var book = _c.Session.RequireBook();
        var player = Owner;
        _types = Kind == HireKind.Unit
            ? Economy.HireList(_c.Map, player.Id, book.Economy)
            : book.RaceLeaders.GetValueOrDefault(player.RaceId) ?? [];
        ui.SetText("TXT_LEGENDE", _c.Text(Kind == HireKind.Unit ? "X005TA0161" : "X005TA0160"));
        ui.SetText("TXT_GOLD", player.Bank.Gold.ToString(CultureInfo.InvariantCulture));
        ui.SetCards(List, [.. _types.Select(Card)]);
        if (_types.Count > 0) ui.Select(List, 0);
    }

    /// <summary>Найм — подтверждение списка: <c>BTN_HIRE_LEADER</c> — его кнопка подтверждения (разметка), и двойной
    /// щелчок. Переход графа от кнопки (окно вопроса) делает сама логика.</summary>
    public bool Allows(string element, UiScreenState ui) => element != "BTN_HIRE_LEADER";

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (e is UiListConfirmed { Element: List } confirmed) Hire(confirmed.Index);
        else if (e is UiClicked { Element: "BTN_HIRE_LEADER" }) Hire(ui.ListSelected(List));
    }

    /// <summary>Нанять пункт списка <paramref name="index"/>.</summary>
    public void Hire(int index)
    {
        if (index < 0 || index >= _types.Count) return;
        string type = _types[index];
        if (Kind == HireKind.Unit)
        {
            _requests.Add(new UiAsk(_c.Text("X005TA0285"), () => Done(_c.Session.Run<string?>(new HireUnitCommand(_fortId, type, Cell(type))))));
            return;
        }
        var book = _c.Session.RequireBook();
        if (book.LeaderCategoryOf(type) == ThiefCategory
            && !Owner.Buildings.Any(b => book.Economy.Buildings.TryGetValue(b, out var t) && t.Category == GuildCategory))
        {
            _requests.Add(new UiTell(_c.Text("X005TA0451")));
            return;
        }
        if (Economy.NotEnough(Owner.Bank, Economy.HireCost(type, book.Economy)))
        {
            Done("не хватает казны");
            return;
        }
        string name = DefaultName(type);
        _requests.Add(new UiOpen(LeaderNameLogic.Screen, new LeaderNameLogic(_c, type, name, chosen =>
        {
            string? refusal = _c.Session.Run<string?>(new HireLeaderCommand(_fortId, type, chosen));
            if (refusal is null) Done(null);
            return refusal;
        })));
    }

    /// <summary>Имя по умолчанию (<c>0x4b02f3</c>): из имён языка интерфейса той же расы и пола — среди наименее занятых
    /// лидерами на карте случайное (<see cref="LeaderNameList.Suggest"/>); списка нет — случайное мужское имя расы из правил, как у
    /// найма ИИ (CRT rand, <c>0x44b126</c>).</summary>
    private string DefaultName(string type)
    {
        bool male = _c.Rules?.UnitSexes.GetValueOrDefault(type, "T") is not { } sex || !sex.Equals("F", StringComparison.OrdinalIgnoreCase);
        var taken = _c.Session.Looks.Stacks.Select(s => s.LeaderName).Where(n => n.Length > 0);
        if (_c.LeaderNames?.Suggest(Owner.RaceId, male, taken, () => _c.Session.Crt.Next()) is { } suggested) return suggested;
        var names = _c.Session.RequireBook().LeaderNames.GetValueOrDefault(Owner.RaceId) ?? [];
        return names.Count > 0 ? names[_c.Session.Crt.Next() % names.Count] : _c.UnitName(type);
    }

    private void Done(string? refusal)
    {
        LastRefusal = refusal;
        if (refusal is null) { _requests.Add(new UiClose()); return; }
        var fort = _c.Map.Sites.First(s => s.Id.Equals(_fortId, StringComparison.OrdinalIgnoreCase));
        string text = refusal switch
        {
            "не хватает казны" => _c.Text("X005TA0510"),
            "в гарнизоне нет места" => UiTemplate.Fill(_c.Text("X005TA0288"), ("CITY_LEVEL", fort.Size ?? 1)),
            _ => refusal,
        };
        _requests.Add(new UiTell(text));
    }

    /// <summary>Ячейка найма: по которой щёлкнули; большой встаёт в колонку — её передняя клетка.</summary>
    private int? Cell(string type)
    {
        if (_cell is not { } cell) return null;
        bool big = _c.Session.RequireBook().Rules.Units.TryGetValue(type, out var t) && !t.SizeSmall;
        return big ? Formation.ColumnFront(cell) : cell;
    }

    private MapPlayer Owner =>
        _c.Map.Players[_c.Map.Sites.First(s => s.Id.Equals(_fortId, StringComparison.OrdinalIgnoreCase)).Owner];

    private UiCard Card(string type)
    {
        var book = _c.Session.RequireBook();
        long cost = Economy.HireCost(type, book.Economy).Gold;
        string text = UiTemplate.Fill(_c.Text("X005TA0435"), ("NAME", _c.UnitName(type)))
                      + UiTemplate.Fill(_c.Text("X005TA0614"), ("COST", cost));
        if (Kind == HireKind.Leader)
        {
            int leadership = book.TryGlobal(type, out var id, out _) && book.Extras.Units.TryGetValue(id, out var e) ? e.Leadership : 0;
            text += UiTemplate.Fill(_c.Text("X005TA0615"), ("LEADERSHIP", Math.Max(0, leadership - 1)));
            // Последней строкой — тип героя (IUsStackLeader::getAbilityName, Gunits.ABIL_TXT) как есть, без шаблона (0x4b5445).
            if (_c.Rules?.UnitAbilities.TryGetValue(type, out var ability) == true && ability.Length > 0) text += _c.Text(ability);
        }
        return new UiCard(_c.Faces.Small(type), new UiPoint(0, 0), text, new UiRect(TextX, 0, RowW - TextX, RowH));
    }
}
