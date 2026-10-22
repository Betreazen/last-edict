using D2.Data.Content;
using D2.Data.Play;
using D2.Data.Ui;
using D2.Rules.Map;

namespace D2.Data.Tests;

/// <summary>Конец партии (<see cref="GameEnd"/>): окно сообщения, эпилог, попап события — по разбору движка
/// (<c>docs/scenario-events.md</c> §3–4).</summary>
public class GameEndTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly Lazy<IReadOnlyDictionary<string, string>> LoadedStrings = new(() => UiStrings.Load(ImportedData.StringsDir(), "ru"));
    private static IReadOnlyDictionary<string, string> Strings => LoadedStrings.Value;

    [SkippableFact]
    public void Message_WinNoWinnerOtherWon()
    {
        string path = ScenarioFile.PathOf(ImportedData.PathOf("scenarios"), "Power to Corrupt");
        Skip.IfNot(File.Exists(path), "нет сценария порта");
        var content = SharedContent.Load(Data);
        var map = ScenarioFile.Read(path).Looks.Map;
        string Text(string id) => Strings.GetValueOrDefault(id) ?? id;
        var info = new GameInfo("PtC", "PtC", ["S073PL000A"], 1);
        Assert.Null(GameEnd.Message(info, "S073PL000A", map, content.Catalog, Text));
        Assert.Contains("Победа!", GameEnd.Message(info with { Winner = "S073PL000A" }, "S073PL000A", map, content.Catalog, Text));
        Assert.Contains("проиграли", GameEnd.Message(info with { Winner = GameInfo.NoWinner }, "S073PL000A", map, content.Catalog, Text));
        string other = GameEnd.Message(info with { Winner = "S073PL0009" }, "S073PL000A", map, content.Catalog, Text)!;
        Assert.Contains(map.Players["S073PL0009"].Name.Trim(), other);
        Assert.DoesNotContain("%RACE%", other);
        Assert.DoesNotContain("%PLAYER%", other);
    }

    [Fact]
    public void Epilogue_Template()
    {
        Assert.Equal(@"\hC;\vC;текст", GameEnd.Epilogue(@"\hC;\vC;%TEXT%", "текст"));
        Assert.Equal("  а\n\n\nб", GameEnd.Epilogue("%TEXT%", "%2%а%3%б"));
    }

    [SkippableFact]
    public void EndScreen_TextAndVideo_Popup_ImageSide()
    {
        var content = SharedContent.Load(Data);
        Skip.IfNot(content.Screens.Any(s => s.Id == "DLG_END_WIN_LOSE_"), "нет экранов");
        var scenario = content.Catalog.Scenarios.Single(s => s.Id == "Power to Corrupt");
        var ui = new UiScreenState(content.Screens.Single(s => s.Id == "DLG_END_WIN_LOSE_"));
        new EndScenarioLogic(false, scenario, id => Strings.GetValueOrDefault(id) ?? id).Open(ui);
        Assert.Contains("You have been defeated", ui.TextOf("TXT_TEXT_SCROLL"));
        Assert.Equal("DEFLOSE", ui.VideoOf("IMG_END_LOSE")?.Name);

        // Кампании сценария нет в таблице кампаний (0x669464) — у эпилога нет ни текста, ни ролика (0x4bc78f).
        var bare = new UiScreenState(content.Screens.Single(s => s.Id == "DLG_END_WIN_LOSE_"));
        new EndScenarioLogic(true, scenario with { HasEnding = false, WinVideo = "" }, id => Strings.GetValueOrDefault(id) ?? id).Open(bare);
        Assert.Equal("", bare.TextOf("TXT_TEXT_SCROLL"));
        Assert.Null(bare.VideoOf("IMG_END_LOSE"));

        var popup = new UiScreenState(content.Screens.Single(s => s.Id == "DLG_EVENT_POPUP"));
        new EventPopupLogic(new EventMessage("P", 1, "E", new PopupEffect("привет", Image: "ADVISOR", LeftSide: true))).Open(popup);
        Assert.Equal("привет", popup.TextOf("TXT_RIGHTSIDE"));
        Assert.Equal("gfx:Events/ADVISOR", popup.Frame("IMG_LEFTSIDE"));
        Assert.NotEqual("gfx:Events/ADVISOR", popup.Frame("IMG_RIGHTSIDE"));
    }

    /// <summary>Звук эпилога (<c>0x4bcba0</c>): победа — <c>WAV_WIN</c> записи, пустой и поражение — <c>GENBRIEF</c>, нет записи — без звука.
    /// Числа каталога: у глав саг свой <c>WINH01</c>…, у эльфов <c>WINFGEN</c>/<c>WINF08</c>, у квестов (<c>C000CC0001</c>, запись
    /// <c>S021SC0000</c>) — <c>GENBRIEF</c>.</summary>
    [SkippableFact]
    public void EpilogueSound_WinSound_Genbrief_OrSilence()
    {
        var content = SharedContent.Load(Data);
        var quest = content.Catalog.Scenarios.FirstOrDefault(s => s.Id == "Power to Corrupt");
        Skip.If(quest is null, "нет каталога");
        var empire = content.Catalog.Campaigns.Single(c => c.Id == "C000CC0002");
        var elves = content.Catalog.Campaigns.Single(c => c.Id == "C000CC0006");
        Assert.Equal(["WINH01", "WINH02", "WINH03", "WINH04", "WINH05", "WINH06", "WINH07"], empire.Chapters.Select(c => c.Scenario.WinSound));
        Assert.Equal("WINF08", elves.Chapters[^1].Scenario.WinSound);
        Assert.Equal("WINH03", GameEnd.EpilogueSound(true, empire.Chapters[2].Scenario));
        Assert.Equal(GameEnd.DefaultEpilogueSound, GameEnd.EpilogueSound(false, empire.Chapters[2].Scenario));
        Assert.Equal("GENBRIEF", GameEnd.EpilogueSound(true, quest));
        Assert.Equal(GameEnd.DefaultEpilogueSound, GameEnd.EpilogueSound(true, quest! with { WinSound = "" }));
        Assert.Null(GameEnd.EpilogueSound(true, quest with { HasEnding = false }));
        Assert.Null(GameEnd.EpilogueSound(true, null));
    }

    /// <summary>Экран эпилога просит звук при открытии (затухание музыки 750 мс), «Повтор» выключен до конца звука; после конца — включён,
    /// нажатие — звук снова без затухания и кнопка снова выключена. Без записи эпилога — звука нет, кнопка выключена навсегда.</summary>
    [SkippableFact]
    public void EndScreen_Sound_ReplayEnabledAfterEnd()
    {
        var content = SharedContent.Load(Data);
        Skip.IfNot(content.Screens.Any(s => s.Id == "DLG_END_WIN_LOSE_"), "нет экранов");
        var scenario = content.Catalog.Campaigns.Single(c => c.Id == "C000CC0005").Chapters[0].Scenario;
        var ui = new UiScreenState(content.Screens.Single(s => s.Id == "DLG_END_WIN_LOSE_"));
        var logic = new EndScenarioLogic(true, scenario, id => id);
        logic.Open(ui);
        Assert.Equal(new D2.Data.Ui.Windows.UiTune("WINU01", EndScenarioLogic.MusicFadeMs), logic.TakeRequest());
        Assert.Null(logic.TakeRequest());
        Assert.False(ui.IsEnabled("BTN_REPLAY"));
        logic.Handle(new UiClicked("BTN_REPLAY"), ui);
        Assert.Null(logic.TakeRequest());
        logic.TuneEnded(ui);
        Assert.True(ui.IsEnabled("BTN_REPLAY"));
        logic.Handle(new UiClicked("BTN_REPLAY"), ui);
        Assert.Equal(new D2.Data.Ui.Windows.UiTune("WINU01", 0), logic.TakeRequest());
        Assert.False(ui.IsEnabled("BTN_REPLAY"));

        var bare = new UiScreenState(content.Screens.Single(s => s.Id == "DLG_END_WIN_LOSE_"));
        var silent = new EndScenarioLogic(true, scenario with { HasEnding = false }, id => id);
        silent.Open(bare);
        Assert.Null(silent.TakeRequest());
        silent.TuneEnded(bare);
        Assert.False(bare.IsEnabled("BTN_REPLAY"));
    }
}
