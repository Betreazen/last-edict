using System.Linq;
using D2.Data.Ui;
using Godot;

namespace D2.Godot;

/// <summary>Режим <c>--lines</c> — проверка шага строки числом: у каждого начертания <c>data/ui/fonts.json</c> подпись в одну и в три строки
/// с оформлением экранов (<see cref="UiArt.Settings"/>), шаг — половина разницы их высот. Строка на начертание:
/// <c>lines face=Normal size=14 font=20.00 ascent=15.00 spacing=-3.00 pitch=17.00 want=17</c>; выход 1, если шаг не совпал с <see cref="UiFont.Line"/>.</summary>
public partial class UiScreenView
{
    private const string LineSample = "Жу";

    private void LinesCheck()
    {
        bool ok = true;
        foreach (var (key, face) in GameData.Fonts.Faces.OrderBy(p => p.Key, System.StringComparer.Ordinal))
        {
            var settings = UiArt.Settings(face, Colors.Black);
            var one = new Label { Text = LineSample, LabelSettings = settings };
            var three = new Label { Text = $"{LineSample}\n{LineSample}\n{LineSample}", LabelSettings = settings };
            AddChild(one);
            AddChild(three);
            float pitch = (three.GetMinimumSize().Y - one.GetMinimumSize().Y) / 2f;
            bool match = face.Line <= 0 || Mathf.Abs(pitch - face.Line) < 0.01f;
            ok &= match;
            GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"lines face={key} size={face.Size} font={settings.Font.GetHeight(face.Size):0.00} ascent={settings.Font.GetAscent(face.Size):0.00} spacing={settings.LineSpacing:0.00} " +
                $"pitch={pitch:0.00} want={face.Line}"));
            one.QueueFree();
            three.QueueFree();
        }
        GetTree().Quit(ok ? 0 : 1);
    }
}
