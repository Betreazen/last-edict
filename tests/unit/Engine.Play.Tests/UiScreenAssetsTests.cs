using D2.Data.Ui;
using Xunit;
using Xunit.Abstractions;

namespace D2.Data.Tests;

/// <summary>Стык двух наборов данных порта: экраны (<c>data/ui/screens.json</c>) ссылаются на кадры, а кадры
/// перечислены в описи графики (<c>data/ui/assets.json</c>). Без этой сверки любая опечатка в имени кадра
/// всплыла бы только на экране в Godot.</summary>
public class UiScreenAssetsTests(ITestOutputHelper output)
{
    private static string Data(string file) => ImportedData.PathOf("ui/" + file);

    private static IEnumerable<(string Screen, string Element, string Field, string Frame)> Referenced(
        IEnumerable<UiScreen> screens) =>
        screens.SelectMany(s => new[] { (s.Id, s.Id, "Background", s.Background) }
            .Concat(s.Elements.SelectMany(e => new[]
            {
                (s.Id, e.Id, "Image", e.Image ?? ""),
                (s.Id, e.Id, "Normal", e.Sprites?.Normal ?? ""),
                (s.Id, e.Id, "Hover", e.Sprites?.Hover ?? ""),
                (s.Id, e.Id, "Pressed", e.Sprites?.Pressed ?? ""),
                (s.Id, e.Id, "Disabled", e.Sprites?.Disabled ?? ""),
                (s.Id, e.Id, "Checked.Normal", e.Checked?.Normal ?? ""),
                (s.Id, e.Id, "Checked.Hover", e.Checked?.Hover ?? ""),
                (s.Id, e.Id, "Checked.Pressed", e.Checked?.Pressed ?? ""),
            })))
        .Where(r => r.Item4.Length > 0);

    /// <summary>Ссылки, которых в игре нет и не было. Графика сетевого лобби идёт с надстройкой мультиплеера
    /// (архива нет ни в эталонной копии, ни в рабочей), а <c>DLG_LOGIN_ACCOUNT_EDIT_BORDER</c> — битая ссылка самой
    /// разметки: этого имени нет ни в одном <c>.ff</c> игры (проверено поиском по всем архивам 18.09.2026).
    /// Свои экраны порта таких ссылок иметь не должны — поэтому список закрытый, а не шаблон.</summary>
    private static bool Known((string Screen, string Element, string Field, string Frame) r) =>
        r.Screen.StartsWith("DLG_CUSTOM_LOBBY", StringComparison.Ordinal)
        || r.Frame.StartsWith("DLG_LOBBY_FACE", StringComparison.Ordinal)
        || r.Frame == "DLG_LOGIN_ACCOUNT_EDIT_BORDER";

    /// <summary>Каждый кадр, названный экраном, есть в описи — кроме перечисленных в <see cref="Known"/>.
    /// Курсоры (<c>_CU…</c>) сюда не входят: они лежат в других архивах игры и в опись интерфейса не собирались.</summary>
    [SkippableFact]
    public void EveryFrameUsedByScreens_IsInTheAssetManifest()
    {
        var list = UiAssets.Load(Data("assets.json"));
        var assets = UiAssets.ByName(list);
        var anims = UiAssets.Animations(list);
        var screens = UiScreens.Load(Data("screens.json"));

        var missing = Referenced(screens)
            .Where(r => !assets.ContainsKey(r.Frame) && !anims.ContainsKey(r.Frame)
                        && !Known(r))
            .GroupBy(r => r.Frame, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key} ← {g.First().Screen}.{g.First().Element} ({g.First().Field}), всего {g.Count()}")
            .ToList();

        output.WriteLine($"ссылок на кадры {Referenced(screens).Count()}, кадров в описи {assets.Count}, " +
                         $"не найдено разных имён {missing.Count}");
        foreach (var line in missing.Take(15)) output.WriteLine("  " + line);
        Assert.Empty(missing);
    }
}
