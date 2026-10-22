using LastEdict.Data.Input;
using LastEdict.Data.Ui;

namespace LastEdict.Data.Tests;

/// <summary>Шаг крестовины по экрану разметки (<see cref="UiNavigation"/>, Э5, Р12): ближайший элемент по ходу с штрафом за уход вбок, только
/// кнопки и переключатели, которые можно нажать; без начального — «подтвердить» экрана, иначе первая сверху-слева.</summary>
public class UiNavigationTests
{
    private static UiElement Button(string id, int x, int y, params int[] hotkeys) =>
        new() { Id = id, Kind = UiKind.Button, Rect = new UiRect(x, y, 100, 30), Hotkeys = hotkeys };

    //  TOP_L (100,100)   TOP_R (400,100)
    //  MID   (110,200)   FAR_R (700,230)
    //  OK    (400,400)
    private static readonly UiScreen Screen = new()
    {
        Id = "DLG_TEST",
        Elements =
        [
            Button("TOP_L", 100, 100), Button("TOP_R", 400, 100), Button("MID", 110, 200), Button("FAR_R", 700, 230),
            Button("OK", 400, 400, 13),
            new UiElement { Id = "TXT", Kind = UiKind.Text, Rect = new UiRect(400, 200, 100, 30) },
        ],
    };

    private static bool All(string id) => true;

    [Theory]
    [InlineData("TOP_L", NavDirection.Down, "MID")]
    [InlineData("TOP_L", NavDirection.Right, "TOP_R")]   // MID почти прямо под — вне конуса
    [InlineData("MID", NavDirection.Up, "TOP_L")]
    [InlineData("MID", NavDirection.Right, "TOP_R")]      // подпись TXT — не цель; TOP_R ближе по ходу, чем FAR_R
    [InlineData("FAR_R", NavDirection.Left, "MID")]
    [InlineData("TOP_R", NavDirection.Down, "OK")]        // прямо вниз ближе, чем MID наискосок
    [InlineData("OK", NavDirection.Up, "TOP_R")]
    public void Next_NearestAlongDirection(string from, NavDirection direction, string want) =>
        Assert.Equal(want, UiNavigation.Next(Screen, All, from, direction));

    [Fact]
    public void Next_NothingThatWay_Null()
    {
        Assert.Null(UiNavigation.Next(Screen, All, "TOP_L", NavDirection.Up));
        Assert.Null(UiNavigation.Next(Screen, All, "FAR_R", NavDirection.Right));
    }

    [Fact]
    public void Next_SkipsUnusable_StartsAtAccept()
    {
        Assert.Equal("TOP_R", UiNavigation.Next(Screen, id => id != "OK", "FAR_R", NavDirection.Up));
        Assert.Equal("OK", UiNavigation.Next(Screen, All, null, NavDirection.Down));
        Assert.Equal("TOP_L", UiNavigation.Next(Screen, id => id != "OK", null, NavDirection.Down));
        Assert.Null(UiNavigation.Next(Screen, _ => false, null, NavDirection.Down));
    }
}
