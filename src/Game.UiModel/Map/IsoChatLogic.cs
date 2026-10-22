using D2.Data.Menu;
using D2.Data.Ui;

namespace D2.Data.Map;

/// <summary>Строки чата над картой (<c>DLG_ISO</c>, у движка — <c>CIsoChatDisplay</c>, <c>0x486c3c</c>; <c>docs/map-screen.md</c> §3.3): туда
/// же движок пишет и сообщения о записи партии (<see cref="D2.Data.Play.QuickSave"/>). Строка оформляется <c>X005TA0703</c> (белым с чёрной
/// обводкой, <c>%TEXT%</c>; <c>0x4872fb</c>), строк не больше <see cref="Capacity"/> (<c>0x48727b</c>: шестая снимает старейшую), каждая
/// живёт <see cref="LifetimeMs"/> мс (<c>0x4875ff</c>: <c>+0x2710</c> с первого показа).
/// <para>Ранг 2: место — <c>TXT_CHAT_AREA</c> разметки <c>DLG_ISO</c> (45,490, одна строка высотой 17), и порт показывает в нём последнюю
/// живую строку. Движок рисует строки сам столбиком; где именно — не разобрано (имён <c>TXT_CHAT_AREA</c>/<c>IMG_SENDER</c> в
/// <c>Discipl2.exe</c> нет).</para></summary>
public sealed class IsoChatLogic(Func<string, string> text) : IScreenLogic
{
    public const string Screen = "DLG_ISO", Area = "TXT_CHAT_AREA", Format = "X005TA0703";
    public const int Capacity = 5;
    public const long LifetimeMs = 10_000;

    private readonly List<(string Line, long Until)> _lines = [];

    /// <summary>Живые строки, старейшая первой (оформленные <see cref="Format"/>).</summary>
    public IReadOnlyList<string> Lines => [.. _lines.Select(l => l.Line)];

    public void Open(UiScreenState ui) => Show(ui);

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    /// <summary>Новая строка в момент <paramref name="nowMs"/>.</summary>
    public void Say(string message, long nowMs)
    {
        if (_lines.Count == Capacity) _lines.RemoveAt(0);
        _lines.Add((text(Format).Replace("%TEXT%", message, StringComparison.Ordinal), nowMs + LifetimeMs));
    }

    /// <summary>Снять строки, чьё время вышло к <paramref name="nowMs"/>; true — что-то снято.</summary>
    public bool Expire(long nowMs) => _lines.RemoveAll(l => l.Until <= nowMs) > 0;

    /// <summary>Показать последнюю живую строку (нет строк — пусто).</summary>
    public void Show(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ui.Screen.Elements.Any(e => e.Id == Area)) ui.SetText(Area, _lines.Count == 0 ? "" : _lines[^1].Line);
    }
}
