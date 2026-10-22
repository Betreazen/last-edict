using D2.Data.Leaders;
using D2.Data.Play;

namespace D2.Data.Ui.Windows;

/// <summary>
/// Вывоз героя после победы (<c>DLG_LEADER_PICKER</c>, <c>CLeaderPickerInterf</c> <c>0x4c9963</c>; разбор — <c>docs/day-cycle.md</c> §14).
/// Список <c>LBOX_LEADERS</c> (ячейки 250×95): герои игрока, которых можно вывезти (<see cref="GameSession.ExportCandidates"/>), — лицо
/// и «имя, уровень, выиграно сражений» (<c>X005TA0725</c>). «ОК»: герой не нанят в своём городе — <c>X005TA0726</c>, не играбельной
/// расы — <c>X005TA0729</c>; иначе файл героя в каталоге (<see cref="LeaderStore.Save"/>) и «сохранён» (<c>X005TA0727</c>; не записался —
/// <c>X005TA0728</c>), вывезенный из списка уходит (у движка окно открывается заново без него, <c>0x4d73fd</c>). Список опустел или
/// «Отмена» — окно закрывается и зовёт <paramref name="done"/>. «Перегруппировка отряда» и двойной щелчок по строке — окно отряда
/// выбранного героя (<paramref name="manage"/>, <c>0x4ca54c</c>/<c>0x4ca5e5</c>); без него кнопки нет (вывоз из «Опций», <c>0x4c1549</c>).
/// </summary>
public sealed class LeaderPickerLogic(WindowContext context, string leadersDir, Action done,
                                      Func<string, IWindowLogic>? manage = null) : IWindowLogic
{
    public const string Screen = "DLG_LEADER_PICKER";
    private const string List = "LBOX_LEADERS";
    private const int CellHeight = 95, FaceHeight = 85, TextX = 88, CellWidth = 250;

    private readonly UiRequests _requests = new();
    private readonly HashSet<string> _exported = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MapStackRef> _stacks = [];
    private bool _closed;

    /// <summary>Отряд в списке: id и запись героя, какой она уйдёт в файл.</summary>
    private sealed record MapStackRef(string StackId, ExportedLeader Leader);

    /// <summary>Вывезенные за это открытие файлы — для проверок.</summary>
    public List<string> Saved { get; } = [];

    public string Summary => $"вывоз героев={_stacks.Count} сохранено={Saved.Count}";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        Load(ui);
        ui.Select(List, 0);
        Buttons(ui);
        if (_stacks.Count == 0) Close();
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        switch (e)
        {
            case UiListSelected { Element: List }:
                Buttons(ui);
                break;
            case UiClicked { Element: "BTN_OK" }:
                Export(ui);
                break;
            case UiClicked { Element: "BTN_CANCEL" }:
                Close();
                break;
            case UiClicked { Element: "BTN_MANAGESTACK" } or UiListConfirmed { Element: List } when manage is not null && Selected(ui) is { } s:
                _requests.Add(new UiOpen(ManageStackLogic.Screen, manage(s.StackId)));
                break;
        }
    }

    public UiRequest? TakeRequest() => _requests.Take();

    /// <summary>Кнопки ведёт само окно: переходы графа (<c>BTN_OK → DLG_MESSAGE_BOX</c>, <c>BTN_CANCEL → DLG_END_WIN_LOSE_</c>) открыли бы
    /// второе окно поверх своего.</summary>
    public bool Allows(string element, UiScreenState ui) => element is not ("BTN_OK" or "BTN_CANCEL" or "BTN_MANAGESTACK");

    /// <summary>Снова наверху (закрыто сообщение или окно отряда): список заново — снаряжение могло смениться.</summary>
    public void Resume(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        int at = ui.ListSelected(List);
        Load(ui);
        if (_stacks.Count == 0)
        {
            Close();
            return;
        }
        ui.Select(List, Math.Clamp(at, 0, _stacks.Count - 1));
        Buttons(ui);
    }

    private void Export(UiScreenState ui)
    {
        if (Selected(ui) is not { } s || context.Map.Stacks.GetValueOrDefault(s.StackId) is not { } stack) return;
        if (context.Session.ExportRefusal(stack) is { } refusal)
        {
            _requests.Add(new UiTell(context.Text(refusal)));
            return;
        }
        try
        {
            Saved.Add(LeaderStore.Save(leadersDir, s.Leader));
            _exported.Add(s.StackId);
            _requests.Add(new UiTell(UiTemplate.Fill(context.Text("X005TA0727"), ("NAME", s.Leader.Name))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _requests.Add(new UiTell(context.Text("X005TA0728")));
        }
    }

    private void Load(UiScreenState ui)
    {
        _stacks = [.. context.Session.ExportCandidates(context.Viewer).Where(s => !_exported.Contains(s.Id))
                              .Select(s => new MapStackRef(s.Id, context.Session.ExportOf(s, AttackName)))];
        var text = new UiRect(TextX, 0, CellWidth - TextX, CellHeight);
        ui.SetCards(List, [.. _stacks.Select(s => new UiCard(context.Faces.Small(s.Leader.UnitType), new UiPoint(0, (CellHeight - FaceHeight) / 2),
            UiTemplate.Fill(context.Text("X005TA0725"), ("NAME", s.Leader.Name), ("LEVEL", s.Leader.Level), ("NBBATTLE", s.Leader.BattlesWon)),
            text))]);
    }

    private string AttackName(string attackId) =>
        context.Rules?.AttackNames.TryGetValue(attackId, out var name) == true ? context.Text(name) : "";

    private MapStackRef? Selected(UiScreenState ui)
    {
        int i = ui.ListSelected(List);
        return i >= 0 && i < _stacks.Count ? _stacks[i] : null;
    }

    private void Buttons(UiScreenState ui)
    {
        bool any = Selected(ui) is not null;
        ui.SetEnabled("BTN_OK", any);
        ui.SetEnabled("BTN_MANAGESTACK", any);
        ui.SetHidden("BTN_MANAGESTACK", manage is null);
    }

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        done();
        _requests.Add(new UiClose());
    }
}
