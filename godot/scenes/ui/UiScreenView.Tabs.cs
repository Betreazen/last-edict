using D2.Data.Ui;
using Godot;

namespace D2.Godot;

/// <summary>Режим <c>--tabs</c> — табуляция подписи числом (прогон 2 пользователя 11.10, снимок 33: «Уровень:1» слитно) через
/// <see cref="Apply"/>, как у подписи экрана. Строка карточки воина <c>X005TA0423</c> с местом <c>\s110;</c>:
/// <c>tabs tab=110 width=117.00 want=117.00 без_места=57.00 слитно=56.00</c> — <c>width</c> — ширина «Уровень:», табуляция, «1»;
/// <c>want</c> — место плюс ширина «1»; <c>без_места</c> — та же строка без мест табуляции (как было), <c>слитно</c> — «Уровень:1».
/// Два места в строке (<c>X005TA0425</c> «\s60;\t120 \s90;\tМ»): <c>tabs2 width=… want=…</c> — 90 плюс ширина «М».
/// Выход 1, если ширина не совпала с ожидаемой.</summary>
public partial class UiScreenView
{
    private void TabsCheck()
    {
        _fonts = GameData.Fonts;
        var line = UiTextStyle.Parse("\\s110;\\fmedbold;Уровень:\\t\\fnormal;1");
        float width = Measure(line);
        float none = Measure(line with { Tabs = [] });
        float joined = Measure(line with { Text = "Уровень:1", Tabs = [] });
        float want = line.Tab + Measure(line with { Text = "1", Tabs = [] });
        var two = UiTextStyle.Parse("\\s60;\\t120 \\s90;\\tМ");
        float width2 = Measure(two);
        float want2 = 90 + Measure(two with { Text = "М", Tabs = [] });
        bool ok = line.Tab == 110 && Mathf.Abs(width - want) < 0.01f && Mathf.Abs(width2 - want2) < 0.01f;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        GD.Print(string.Create(inv, $"tabs tab={line.Tab} width={width:0.00} want={want:0.00} без_места={none:0.00} слитно={joined:0.00}"));
        GD.Print(string.Create(inv, $"tabs2 места={string.Join(",", two.TabStops())} width={width2:0.00} want={want2:0.00}"));
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>Ширина подписи в одну строку без переноса с оформлением <paramref name="style"/>.</summary>
    private float Measure(UiTextStyle style)
    {
        var label = new Label { AutowrapMode = TextServer.AutowrapMode.Off };
        AddChild(label);
        Apply(label, style);
        float width = label.GetMinimumSize().X;
        _applied.Remove(label);
        label.QueueFree();
        return width;
    }
}
