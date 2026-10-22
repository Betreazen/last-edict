using LastEdict.Data.Menu;

namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Окно имени нового героя (<c>DLG_GETINFO_BOX</c>) после выбора в окне найма (ответ пользователя 25.09, в графе —
/// <c>DLG_HIRE_LEADER_2.BTN_HIRE_LEADER</c> → <c>DLG_GETINFO_BOX</c>): пояснение <c>X005TA0086</c> «Имя нового героя»,
/// портрет, поле с именем по умолчанию. Кнопка (Enter и Esc — её клавиши) нанимает героя под введённым именем; пустое
/// имя — <c>X005TA0345</c> «Лидер должен иметь имя.». Переход графа от кнопки (там он ведёт и в другие окна) не нужен:
/// окно закрывает себя само.
/// </summary>
public sealed class LeaderNameLogic(WindowContext context, string leaderType, string defaultName, Func<string, string?> hire)
    : IWindowLogic
{
    public const string Screen = "DLG_GETINFO_BOX";
    private const string Edit = "EDIT_GET_INFO";

    private readonly UiRequests _requests = new();

    /// <summary>Отказ найма (словами партии) — для проверок.</summary>
    public string? LastRefusal { get; private set; }

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"имя={defaultName.Replace(' ', '_')} отказ={(LastRefusal ?? "-").Replace(' ', '_')}";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_EXPLANATION", context.Text("X005TA0086"));
        // Портрет в малой рамке карточки (0x4aff40: 0x5b1e6a — картинка панелей, FACES/FACE по базе, — затем 0x5b680b BORDERUNITSMALL).
        ui.SetImage("IMG_FACE", UiFrameRef.Layered(LastEdict.Data.Map.MapCards.SmallBorder, context.Faces.Panel(leaderType)), UiImageAlign.Center);
        ui.SetText(Edit, defaultName);
        ui.SetFocus(Edit);
    }

    public bool Allows(string element, UiScreenState ui) => element != "BTN_CLOSE";

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (e is not UiClicked { Element: "BTN_CLOSE" }) return;
        string name = (ui.TextOf(Edit) ?? "").Trim();
        if (name.Length == 0)
        {
            _requests.Add(new UiTell(context.Text("X005TA0345")));
            return;
        }
        LastRefusal = hire(name);
        _requests.Add(LastRefusal is null ? new UiClose() : new UiTell(LastRefusal));
    }
}
