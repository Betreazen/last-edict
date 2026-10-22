using D2.Data.Menu;
using D2.Data.Play;

namespace D2.Data.Ui.Windows;

/// <summary>
/// Гонец «горячего стула» (<c>DLG_HOTSEAT_POPUP</c> режима 1, <c>CHotseatPopupInterf</c> <c>0x4bdd72</c>): предложение человека другому
/// человеку этого ПК приходит сразу, ещё в ход предложившего (обработчики 15/17/60 диспетчера <c>0x489448</c> — до проверки адресата;
/// союз <c>0x4d5d87</c>, обмен картами <c>0x4d620e</c>, сделка <c>0x4d66a0</c>): адресат не за ПК — сначала гонец (<see cref="HotseatTurnLogic"/>
/// с <see cref="HotseatCall.Message"/>: <c>X005TA0724</c>, портрет адресата), его «ОК» (Enter, Esc) закрывает гонца и открывает окно ответа
/// (коллбэки <c>0x4d602b</c>, <c>0x4d64b2</c>, <c>0x4d68d5</c>). Зритель, туман и панель не меняются — адресат отвечает поверх карты
/// предложившего (ранг 2), ответ идёт от его имени (<c>0x4068b1</c>).
/// </summary>
public sealed class HotseatCourierLogic(HotseatTurnLogic popup, UiOpen answer) : IWindowLogic
{
    public const string Screen = "DLG_HOTSEAT_POPUP";
    private readonly UiRequests _requests = new();
    private bool _done;

    /// <summary>Окно ответа, которое откроет «ОК».</summary>
    public UiOpen Answer => answer;

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"гонец ответ={answer.Screen}";

    public void Open(UiScreenState ui) => popup.Open(ui);

    // «ОК» закрывает гонца сам: в графе у BTN_OK переход в DLG_BEGIN_TURN (гонец начала хода).
    public bool Allows(string element, UiScreenState ui) => element != "BTN_OK";

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is not UiClicked { Element: "BTN_OK" } || _done) return;
        _done = true;
        _requests.Add(new UiClose());
        _requests.Add(answer);
    }
}
