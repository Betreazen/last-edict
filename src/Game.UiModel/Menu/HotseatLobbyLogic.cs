using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;

namespace LastEdict.Data.Menu;

/// <summary>
/// Лобби «горячего стула» (<c>DLG_HOTSEAT_LOBBY</c>, <c>CMenuHotseatLobby</c> <c>0x4dc67c</c>; разбор — <c>docs/day-cycle.md</c> §8).
/// Ячейка на каждую играбельную расу сценария (<c>DLG_HOTSEAT_LOBBY_RACE</c>, <c>CMenuHotseatLobbyRace</c> <c>0x4dc9de</c>) —
/// по центру: первое занятое место <c>(4 − n) / 2</c>. Ячейки вкладываются в экран лобби (<see cref="Shape"/>) с суффиксом
/// номера ячейки у элементов: <c>TOG_PLAYERTYPE_1</c>, <c>EDT_NAME_2</c>.
/// <list type="bullet">
/// <item>по умолчанию каждая ячейка — компьютер: лицо, лорд и имя выключены, сверху заслонка (<c>0x4dd1c9</c>, <c>0x4dd184</c>);</item>
/// <item>лицо — 4 по кругу, с первого (<c>0x4dd429</c>); лорд — воин → маг → гильдмастер, с воина (<c>0x4dd5ac</c>, <c>0x4ddc84</c>);</item>
/// <item>имя — до 15 знаков, пустое (<c>0x4dcb0d</c>); у человека обязательно (<c>X005TA0344</c>);</item>
/// <item>золото и мана — 0…2000 шагом 100, по умолчанию 0 (<c>0x4dc88d</c>); сложность — одна на партию, средняя (<c>0x4dc85e</c>);</item>
/// <item>«Далее» — не меньше двух людей (<c>X006TA0032</c>) и имя у каждого (<c>0x4dd798</c>).</item>
/// </list>
/// Импорт героя (<c>BTN_IMPORT_LEADER</c>, файлы <c>.ldr</c>) не перенесён — кнопка скрыта. Окна отказа «Далее» не показываются:
/// переход просто не случается (ранг 3 для порта).
/// </summary>
internal sealed class HotseatLobbyLogic(MenuContext context) : IWindowLogic
{
    public const string Cell = "DLG_HOTSEAT_LOBBY_RACE";
    private const int Holders = 4, Faces = 4, NameMax = 15, CashStep = 100, CashOptions = 21, AverageDifficulty = 1;
    private const string Gold = "SPIN_GOLD", Mana = "SPIN_MANA", Difficulty = "RAD_DIFF", Ok = "BTN_OK";

    /// <summary>Номер лорда ячейки → категория <c>Llord</c> и кадр (<c>0x4ddc84</c>, <c>0x4dce16</c>).</summary>
    private static readonly (int Category, string Art)[] Lords = [(1, "LWARRIOR"), (0, "LMAGE"), (2, "LDIPLOMAT")];

    private sealed record Seat(int Race, bool Human = false, string Name = "", int Face = 0, int Lord = 0, Leaders.StoredLeader? Leader = null);

    private readonly UiRequests _requests = new();

    /// <summary>Героев для импорта в каталоге — кнопка импорта есть (<c>0x4dc985</c>).</summary>
    private readonly bool _canImport = context.LeadersDir.Length > 0 && Leaders.LeaderStore.List(context.LeadersDir).Count > 0;

    private readonly List<Seat> _seats = [.. Races(context).Select(r => new Seat(r))];

    /// <summary>Играбельные расы сценария в порядке заголовка (<c>0x4e93a4</c>), не больше четырёх ячеек.</summary>
    private static IEnumerable<int> Races(MenuContext context) =>
        (context.Setup.Scenario?.Races ?? []).Where(t => context.Catalog.Races.Any(r => r.Type == t && r.Playable)).Take(Holders);

    private static string Of(string element, int cell) => $"{element}_{cell + 1}";

    public UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var cell = context.Screens.First(s => s.Id == Cell);
        int first = (Holders - _seats.Count) >> 1;
        var added = new List<UiElement>();
        for (int i = 0; i < _seats.Count; i++)
        {
            var holder = screen.Elements.First(e => e.Id == $"IMG_RACE{first + i + 1}_HOLDER").Rect;
            UiRect At(UiRect r) => r with { X = holder.X + r.X, Y = holder.Y + r.Y };
            UiElement Part(string id)
            {
                var e = cell.Elements.First(x => x.Id == id);
                return e with { Id = Of(id, i), Rect = At(e.Rect) };
            }
            // Порядок рисования: фон, раса, имя, кнопки (лицо и лорд — картинкой самой кнопки), заслонка компьютера, над ней —
            // переключатель.
            added.Add(new UiElement { Id = Of("IMG_CELL", i), Kind = UiKind.Image, Rect = At(cell.Design with { X = 0, Y = 0 }), Image = cell.Background });
            added.Add(Part("IMG_RACE"));
            added.AddRange(cell.Elements.Where(e => e.Id is not ("IMG_RACE" or "IMG_COVERER" or "TOG_PLAYERTYPE")).Select(e => Part(e.Id)));
            added.Add(Part("IMG_COVERER"));
            added.Add(Part("TOG_PLAYERTYPE"));
        }
        return screen with { Elements = [.. added, .. screen.Elements] };
    }

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetSpin(Gold, CashOptions, 0);
        ui.SetSpin(Mana, CashOptions, 0);
        ui.SetGroupIndex(Difficulty, AverageDifficulty);
        for (int i = 0; i < _seats.Count; i++)
        {
            ui.SetImage(Of("IMG_RACE", i), $"DLG_HOTSEAT_LOBBY_RACE_R{context.Catalog.Race(_seats[i].Race).Art}");
            ui.SetImage(Of("IMG_COVERER", i), "DLG_HOTSEAT_LOBBY_RACE_COVERER_IMAGE");
            ui.SetHidden(Of("BTN_IMPORT_LEADER", i), !_canImport);
            ui.SetText(Of("TXT_IMPORT_NAME", i), "");
            ui.SetText(Of("EDT_NAME", i), "");
            Show(ui, i);
        }
        Commit(ui);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(e);
        for (int i = 0; i < _seats.Count; i++)
        {
            var seat = _seats[i];
            var next = e switch
            {
                // Компьютеру импорт не положен — выбор снимается (0x4dd1c9).
                UiToggled t when t.Element == Of("TOG_PLAYERTYPE", i) => seat with { Human = t.Checked, Leader = t.Checked ? seat.Leader : null },
                UiClicked c when c.Element == Of("BTN_FACE", i) => seat with { Face = (seat.Face + 1) % Faces },
                UiClicked c when c.Element == Of("BTN_LORD", i) => seat with { Lord = (seat.Lord + 1) % Lords.Length },
                UiEdited d when d.Element == Of("EDT_NAME", i) => seat with { Name = d.Text.Length > NameMax ? d.Text[..NameMax] : d.Text },
                _ => seat,
            };
            if (e is UiClicked { Element: var element } && element == Of("BTN_IMPORT_LEADER", i)) PickLeader(ui, i);
            if (next == seat) continue;
            _seats[i] = next;
            if (e is UiEdited typed && typed.Text != next.Name) ui.SetText(Of("EDT_NAME", i), next.Name);   // сверх 15 знаков
            if (next.Human && !seat.Human) ui.SetFocus(Of("EDT_NAME", i));   // 0x538741, ранг 3
            Show(ui, i);
        }
        Commit(ui);
    }

    public UiRequest? TakeRequest() => _requests.Take();

    /// <summary>Импорт героя ячейки (<c>0x4dd28a</c>): окно выбора с расой ячейки, рекомендованным уровнем сценария и текущим выбором;
    /// выбор (<c>0x4dd365</c>) — герой ячейки и его имя в <c>TXT_IMPORT_NAME</c>, лицо, лорд и имя лорда не меняются.</summary>
    private void PickLeader(UiScreenState ui, int i)
    {
        var seat = _seats[i];
        _requests.Add(new UiOpen("DLG_IMPORT_LEADER", new ImportLeaderLogic(context,
            new LeaderPick(seat.Race, context.Setup.Scenario?.SuggestedLevel ?? 1, seat.Leader?.FileName ?? "", chosen =>
            {
                _seats[i] = _seats[i] with { Leader = chosen };
                Show(ui, i);
                Commit(ui);
            }))));
    }

    /// <summary>«Далее» — не меньше двух людей, у каждого имя (<c>0x4dd798</c>).</summary>
    public bool Allows(string element, UiScreenState ui) => element != Ok || Refusal(element, ui) is null;

    /// <summary>Отказ «Далее» (<c>0x4dd798</c>): меньше двух людей — <c>X006TA0032</c> «Вы должны выбрать как минимум две расы.»,
    /// человек без имени — <c>X005TA0344</c> «Лорд должен иметь имя.» (<c>0x4de148</c>).</summary>
    public string? Refusal(string element, UiScreenState ui) =>
        element != Ok ? null
        : _seats.Count(s => s.Human) < 2 ? TooFewHumans
        : _seats.Any(s => s.Human && s.Name.Trim().Length == 0) ? NoName
        : null;

    private const string TooFewHumans = "X006TA0032", NoName = "X005TA0344";

    private void Show(UiScreenState ui, int i)
    {
        var seat = _seats[i];
        var race = context.Catalog.Race(seat.Race);
        ui.SetChecked(Of("TOG_PLAYERTYPE", i), seat.Human);
        foreach (string id in (string[])["BTN_FACE", "BTN_LORD", "EDT_NAME"]) ui.SetEnabled(Of(id, i), seat.Human);
        ui.SetHidden(Of("IMG_COVERER", i), seat.Human);
        ui.SetEnabled(Of("BTN_IMPORT_LEADER", i), seat.Human && _canImport);
        ui.SetText(Of("TXT_IMPORT_NAME", i), seat.Leader?.Leader.Name ?? "");
        ui.SetSprites(Of("BTN_FACE", i), Frames($"DLG_HOTSEAT_LOBBY_RACE_FACE{race.Code}{seat.Face + 1}"));
        ui.SetSprites(Of("BTN_LORD", i), Frames($"DLG_HOTSEAT_LOBBY_RACE_{Lords[seat.Lord].Art}"));
    }

    /// <summary>Картинка кнопки лица или лорда (<c>0x4dd458</c>): <c>…N</c> в обычном, нажатом и выключенном состоянии, <c>…H</c> —
    /// под мышью (ранг 2 — номера состояний).</summary>
    private static UiSprites Frames(string name) => new($"{name}N", $"{name}H", $"{name}N", $"{name}N");

    /// <summary>Выбор лобби — в <see cref="NewGameSetup"/>: люди по ячейкам, казна, сложность; первый человек — ещё и раса меню
    /// (ролики смены экранов).</summary>
    private void Commit(UiScreenState ui)
    {
        var setup = context.Setup;
        setup.Hotseat = [.. _seats.Where(s => s.Human).Select(s => new HotseatSeat(s.Race, s.Name.Trim(), s.Face, Lords[s.Lord].Category, s.Leader?.Leader))];
        if (setup.Hotseat.Count > 0) setup.Race = setup.Hotseat[0].Race;
        setup.StartGold = ui.SpinOption(Gold) * CashStep;
        setup.StartMana = ui.SpinOption(Mana) * CashStep;
        setup.Difficulty = Math.Max(0, ui.GroupIndex(Difficulty));
        ui.SetText(Gold, setup.StartGold.ToString(System.Globalization.CultureInfo.InvariantCulture));
        ui.SetText(Mana, setup.StartMana.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
