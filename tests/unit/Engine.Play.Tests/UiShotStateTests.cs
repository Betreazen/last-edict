using LastEdict.Data.Ui;

namespace LastEdict.Data.Tests;

/// <summary>Состояние снимка для попиксельной сверки (<see cref="UiShotState"/>, <c>tools/pixel-states</c>): флажки, спрятанные кнопки,
/// счётчики и фон ложатся на экран; опечатка в имени элемента — ошибка, а не молча «в пределах».</summary>
public class UiShotStateTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static UiScreenState Screen(string id) =>
        new(UiScreens.Load(ImportedData.PathOf("ui/screens.json")).First(s => s.Id == id));

    [SkippableFact]
    public void Apply_SetsTogglesSpinsHiddenAndBackground()
    {
        var ui = Screen("DLG_OPTIONS");
        UiShotState.FromJson("""
            { "note": "снимок", "background": "DLG_X_BG", "checked": ["TOG_AUTOSAVE"], "hidden": ["BTN_SAVE"],
              "disabled": ["BTN_LOAD"], "spins": { "SPIN_FX_VOLUME": [4, 0] }, "mask": [{ "rect": [0, 0, 1, 1], "why": "курсор" }] }
            """).ApplyTo(ui);
        Assert.True(ui.IsChecked("TOG_AUTOSAVE"));
        Assert.True(ui.IsHidden("BTN_SAVE"));
        Assert.False(ui.IsEnabled("BTN_LOAD"));
        Assert.Equal(0, ui.SpinOption("SPIN_FX_VOLUME"));
        Assert.False(ui.IsEnabled("BTN_FX_LEFT"));   // стрелка у края счётчика
        Assert.Equal("DLG_X_BG", ui.Background);
    }

    /// <summary>Кадр кнопки на снимке (лицо лорда <c>BTN_FACE</c> — неподвижный кадр <c>FACE_{раса}_{n}</c>, как у логики выбора лорда):
    /// во всех состояниях кнопки.</summary>
    [SkippableFact]
    public void Apply_Sprites_SetButtonFrames()
    {
        var ui = Screen("DLG_CHOOSE_LORD");
        UiShotState.FromJson("""{ "sprites": { "BTN_FACE": "FACE_HU_0" } }""").ApplyTo(ui);
        Assert.Equal("FACE_HU_0", ui.Frame("BTN_FACE"));
    }

    /// <summary>Список на снимке: сколько пунктов и какой выбран — стрелки у края выбора выключены, как у движка (снимки «Задания» 28.09:
    /// выбран первый из двух — «вверх» выключена, последний — «вниз»); подписи пунктов закрыты маской сверки.</summary>
    [SkippableFact]
    public void Apply_ListCountAndSelection_DisablesEdgeArrow()
    {
        var first = Screen("DLG_QUEST_LOG");
        UiShotState.FromJson("""{ "lists": { "LBOX_LOGS": [2, 0] } }""").ApplyTo(first);
        Assert.False(first.IsEnabled("BTN_UP"));
        Assert.True(first.IsEnabled("BTN_DOWN"));
        var last = Screen("DLG_QUEST_LOG");
        UiShotState.FromJson("""{ "lists": { "LBOX_LOGS": [2, 1] } }""").ApplyTo(last);
        Assert.True(last.IsEnabled("BTN_UP"));
        Assert.False(last.IsEnabled("BTN_DOWN"));
        Assert.Throws<FormatException>(() => UiShotState.FromJson("""{ "lists": { "LBOX_LOGS": [2, 2] } }"""));
    }

    /// <summary>Картинка события по центру места (флаги 9 <c>setImage</c> <c>0x530e29</c>): 350×450 в месте 330×430 — на 10 точек левее и
    /// выше угла, без масштаба; без флага — от угла.</summary>
    [SkippableFact]
    public void Apply_CenteredImage_AlignsToPlaceCenter()
    {
        var ui = Screen("DLG_QUEST_LOG");
        UiShotState.FromJson("""
            { "images": { "IMG_LEFTSIDE": "gfx:Events/G000UU3001", "IMG_RIGHTSIDE": "gfx:Events/G000UU0001" }, "centered": ["IMG_LEFTSIDE"] }
            """).ApplyTo(ui);
        Assert.Equal(UiImageAlign.Center, ui.ImageAlign("IMG_LEFTSIDE"));
        Assert.Equal(UiImageAlign.Fit, ui.ImageAlign("IMG_RIGHTSIDE"));
        Assert.Equal((-10, -10), UiImagePlacement.Offset(UiImageAlign.Center, 330, 430, 350, 450));
        Assert.Equal((0, 0), UiImagePlacement.Offset(UiImageAlign.Fit, 330, 430, 350, 450));
        Assert.Equal((-1, 0), UiImagePlacement.Offset(UiImageAlign.Center, 10, 10, 13, 10));   // к нулю, как sar после cdq
        ui.SetImage("IMG_LEFTSIDE", null);
        Assert.Equal(UiImageAlign.Fit, ui.ImageAlign("IMG_LEFTSIDE"));
    }

    [SkippableFact]
    public void Apply_UnknownElementOrBadSpin_Throws()
    {
        var ui = Screen("DLG_OPTIONS");
        Assert.Throws<KeyNotFoundException>(() => UiShotState.FromJson("""{ "checked": ["TOG_NOPE"] }""").ApplyTo(ui));
        Assert.Throws<FormatException>(() => UiShotState.FromJson("""{ "spins": { "SPIN_FX_VOLUME": [4] } }"""));
    }

    /// <summary>Файлы состояний в репозитории читаются и ложатся на свой экран (имя файла — снимок: экран и, после дефиса, номер).</summary>
    [SkippableFact]
    public void RepoStates_ApplyToTheirScreens()
    {
        string dir = Path.Combine(RepoPaths.Root, "tools", "pixel-states");
        Assert.True(Directory.Exists(dir), dir);
        var screens = UiScreens.Load(ImportedData.PathOf("ui/screens.json"));
        foreach (var file in Directory.GetFiles(dir, "DLG_*.json"))
        {
            string id = Path.GetFileNameWithoutExtension(file).Split('-')[0];   // DLG_BATTLE_A-f0-2648341 — снимок экрана DLG_BATTLE_A
            var screen = screens.FirstOrDefault(s => s.Id == id);
            Assert.True(screen is not null, $"{id}: нет экрана");
            UiShotState.Load(file).ApplyTo(new UiScreenState(screen));
        }
    }
}
