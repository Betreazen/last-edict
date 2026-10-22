using LastEdict.Data.Map;
using LastEdict.Data.Ui;

namespace LastEdict.Data.Tests;

/// <summary>Настройки показа карты (<see cref="MapSettings"/>) и их часть окна <c>DLG_OPTIONS</c> (<see cref="MapOptionsLogic"/>).</summary>
public class MapOptionsTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    [SkippableFact]
    public void Window_ShowsAndChangesSpeedsAndAiToggle()
    {
        var screen = UiScreens.Load(ImportedData.PathOf("ui/screens.json")).First(s => s.Id == MapOptionsLogic.Screen);
        var ui = new UiScreenState(screen);
        var saved = new List<MapSettings>();
        var logic = new MapOptionsLogic(new MapSettings(PlayerSpeed: 2, OpponentSpeed: 1), saved.Add, id => $"[{id}]");
        logic.Open(ui);
        Assert.Equal(1, ui.SpinOption("SPIN_MOVEMENT_SPEED"));
        Assert.Equal("[X005TA0115]", ui.TextOf("SPIN_MOVEMENT_SPEED"));
        Assert.Equal("[X005TA0114]", ui.TextOf("SPIN_OPPONENT_SPEED"));   // своё и чужое движение — один список подписей (0x4c02c4, 0x4c0319)
        Assert.True(ui.IsChecked("TOG_PLAYAIISOANIM"));
        Assert.True(ui.IsChecked("TOG_ANIMISO"));   // анимация карты — как поставляемый Disciple.ini (AnimatedIso=1)
        Click(screen, ui, logic, "TOG_ANIMISO");
        Assert.False(logic.Settings.AnimatedIso);
        Click(screen, ui, logic, "TOG_ANIMISO");

        Click(screen, ui, logic, "BTN_OPPONENT_RIGHT");
        Click(screen, ui, logic, "BTN_OPPONENT_RIGHT");
        Click(screen, ui, logic, "BTN_OPPONENT_RIGHT");   // у края — без изменений
        Click(screen, ui, logic, "TOG_PLAYAIISOANIM");
        Assert.Equal(new MapSettings(2, 3, false), logic.Settings);
        Assert.Equal("[X005TA0116]", ui.TextOf("SPIN_OPPONENT_SPEED"));
        Assert.Equal(5, saved.Count);
        Assert.Equal(logic.Settings, saved[^1]);
    }

    /// <summary>«Всегда показывать результаты хода»: по умолчанию выключен (ini <c>ShowTurnSummary</c>, <c>0x619509</c>); итоги — в игре
    /// нескольких людей или с флажком. Подписи строк — по таблице движка (<c>0x6d8a44</c>), подсказка строки под мышью — в <c>TXT_HELP</c>.</summary>
    [SkippableFact]
    public void Window_TurnSummaryToggleLabelsAndHelp()
    {
        var screen = UiScreens.Load(ImportedData.PathOf("ui/screens.json")).First(s => s.Id == MapOptionsLogic.Screen);
        var ui = new UiScreenState(screen);
        var logic = new MapOptionsLogic(new MapSettings(), _ => { }, id => $"[{id}]");
        logic.Open(ui);
        Assert.False(new MapSettings().ShowTurnSummary);
        Assert.False(new MapSettings().TurnSummaryShown(humans: 1));
        Assert.True(new MapSettings().TurnSummaryShown(humans: 2));
        Assert.False(ui.IsChecked("TOG_SHOWTURNSUMMARY"));
        Assert.Equal("[X160TA0024]", ui.TextOf("TXT_SHOWTURNSUMMARY"));
        Assert.Equal("[X005TA0419]", ui.TextOf("TXT_TITLE"));
        Assert.Equal("[X005TA0933]", ui.TextOf("TXT_PLAYAIISOANIM"));

        Click(screen, ui, logic, "TOG_SHOWTURNSUMMARY");
        Assert.True(logic.Settings.ShowTurnSummary);
        Assert.True(logic.Settings.TurnSummaryShown(humans: 1));
        // «Поменяться сторонами»: ini FlippedBattle, по умолчанию 0 (0x6194e5) — раскладка экрана боя.
        Assert.False(new MapSettings().FlippedBattle);
        Assert.False(ui.IsChecked("TOG_FLIPBATTLE"));
        Click(screen, ui, logic, "TOG_FLIPBATTLE");
        Assert.True(logic.Settings.FlippedBattle);
        // Скорость боя 1…4 («Сразу» — X005TA0767; поставляемый ini — 2) и «Дополнительная анимация в бою» (по умолчанию 1, 0x619d50).
        Assert.Equal((1, "[X005TA0118]"), (ui.SpinOption("SPIN_BATTLE_SPEED"), ui.TextOf("SPIN_BATTLE_SPEED")));
        Click(screen, ui, logic, "BTN_BATSPEED_RIGHT");
        Click(screen, ui, logic, "BTN_BATSPEED_RIGHT");
        Click(screen, ui, logic, "BTN_BATSPEED_RIGHT");   // у края
        Assert.Equal((4, "[X005TA0767]"), (logic.Settings.BattleSpeed, ui.TextOf("SPIN_BATTLE_SPEED")));
        Assert.True(ui.IsChecked("TOG_BATTLEANIM"));
        Click(screen, ui, logic, "TOG_BATTLEANIM");
        Assert.False(logic.Settings.BattleAnim);
        Assert.Equal([16, 11, 6, 6], new[] { 1, 2, 3, 4 }.Select(MapSettings.NoteTicks));

        var toggle = screen.Elements.First(e => e.Id == "TOG_PLAYAIISOANIM").Rect;
        ui.Move(toggle.X + 2, toggle.Y + 2);
        logic.Tick(16, ui);
        Assert.Equal("[X005TA0934]", ui.TextOf("TXT_HELP"));
        var arrow = screen.Elements.First(e => e.Id == "BTN_FX_LEFT").Rect;
        ui.Move(arrow.X + 2, arrow.Y + 2);
        logic.Tick(16, ui);
        Assert.Equal("[X005TA0840]", ui.TextOf("TXT_HELP"));
        ui.Move(1, 599);
        logic.Tick(16, ui);
        Assert.Equal("", ui.TextOf("TXT_HELP"));
    }

    /// <summary>Окно из главного меню (партии нет, <c>0x4bf5a4</c>): «Сохранить», «Загрузить», «Экспорт», «Выход» спрятаны; в партии —
    /// на месте. Изменение в меню уходит хозяину через <see cref="LastEdict.Data.Menu.MenuContext.SettingsChanged"/>.</summary>
    [SkippableFact]
    public void Window_OutsideGame_HidesGameButtons()
    {
        var screen = UiScreens.Load(ImportedData.PathOf("ui/screens.json")).First(s => s.Id == MapOptionsLogic.Screen);
        var inGame = new UiScreenState(screen);
        new MapOptionsLogic(new MapSettings(), _ => { }, id => id).Open(inGame);
        Assert.All(MapOptionsLogic.GameButtons, b => Assert.False(inGame.IsHidden(b)));

        var got = new List<MapSettings>();
        var menu = new LastEdict.Data.Menu.MenuContext(null!, new Dictionary<string, string>(), new LastEdict.Data.Menu.NewGameSetup(), "игрок")
        {
            SettingsChanged = got.Add,
        };
        var logic = Assert.IsType<MapOptionsLogic>(LastEdict.Data.Menu.MenuLogic.For(MapOptionsLogic.Screen, menu));
        var ui = new UiScreenState(screen);
        logic.Open(ui);
        Assert.All(MapOptionsLogic.GameButtons, b => Assert.True(ui.IsHidden(b)));
        Click(screen, ui, logic, "TOG_SHOWTURNSUMMARY");
        Assert.True(Assert.Single(got).ShowTurnSummary);
        Assert.True(menu.Settings.ShowTurnSummary);
    }

    /// <summary>Громкости: по умолчанию 83 и 80 — ступени «Нормал.» звука и «Громко» музыки; стрелки ходят по ступеням 0/41/83/127 и
    /// 0/25/55/80, у края — без изменений.</summary>
    [SkippableFact]
    public void Window_ChangesVolumesBySteps()
    {
        var screen = UiScreens.Load(ImportedData.PathOf("ui/screens.json")).First(s => s.Id == MapOptionsLogic.Screen);
        var ui = new UiScreenState(screen);
        var logic = new MapOptionsLogic(new MapSettings(), _ => { }, id => $"[{id}]");
        logic.Open(ui);
        Assert.Equal((2, 3), (ui.SpinOption("SPIN_FX_VOLUME"), ui.SpinOption("SPIN_MUSIC_VOLUME")));
        Assert.Equal("[X005TA0112]", ui.TextOf("SPIN_FX_VOLUME"));
        Assert.Equal("[X005TA0113]", ui.TextOf("SPIN_MUSIC_VOLUME"));   // подписи музыки — те же, что у звука (0x4bfc89); 0812… — фоны мест

        Click(screen, ui, logic, "BTN_FX_RIGHT");
        Click(screen, ui, logic, "BTN_FX_RIGHT");   // у края
        Click(screen, ui, logic, "BTN_MUSIC_LEFT");
        Click(screen, ui, logic, "BTN_MUSIC_LEFT");
        Click(screen, ui, logic, "BTN_MUSIC_LEFT");
        Assert.Equal((127, 0), (logic.Settings.FxVolume, logic.Settings.MusicVolume));
        Assert.Equal("[X005TA0110]", ui.TextOf("SPIN_MUSIC_VOLUME"));
        Assert.Equal((127, 0), (new MapSettings(FxVolume: 500, MusicVolume: -3).Clamped() is var c ? (c.FxVolume, c.MusicVolume) : default));
        // Голоса и фоны мест: по умолчанию как поставляемый Disciple.ini (66 и 3), в границах 0…100 и 0…4.
        Assert.Equal((66, 3), (new MapSettings().Acknowledgements, new MapSettings().AudioRegions));
        var wide = new MapSettings(Acknowledgements: 250, AudioRegions: 9).Clamped();
        Assert.Equal((100, 4), (wide.Acknowledgements, wide.AudioRegions));
    }

    /// <summary>Щелчок по середине элемента: нажатие и отпускание, события — логике.</summary>
    private static void Click(UiScreen screen, UiScreenState ui, MapOptionsLogic logic, string id)
    {
        var r = screen.Elements.First(e => e.Id == id).Rect;
        int x = r.X + (r.Width / 2), y = r.Y + (r.Height / 2);
        foreach (var e in ui.Press(x, y).Concat(ui.Release(x, y))) logic.Handle(e, ui);
    }

    [Fact]
    public void Settings_RoundTrip_BrokenFileGivesDefaults_SpeedFactor()
    {
        string dir = Path.Combine(Path.GetTempPath(), "d2-map-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "settings.json");
        try
        {
            Assert.Equal(new MapSettings(), MapSettings.Load(path));
            new MapSettings(3, 2, false).Save(path);
            Assert.Equal(new MapSettings(3, 2, false), MapSettings.Load(path));
            // Знамёна (TOG_BANNERS) — между запусками (ответ пользователя 11.10); по умолчанию выключены; запись — через временный файл.
            Assert.False(new MapSettings().Banners);
            new MapSettings(Banners: true).Save(path);
            Assert.True(MapSettings.Load(path).Banners);
            Assert.False(File.Exists(path + ".tmp"));
            File.WriteAllText(path, "{ не json");
            Assert.Equal(new MapSettings(), MapSettings.Load(path));
            File.WriteAllText(path, """{"PlayerSpeed": 9, "OpponentSpeed": 0, "PlayAiMoves": true}""");
            Assert.Equal(new MapSettings(3, 1, true), MapSettings.Load(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        Assert.Equal(1f, MapSettings.SpeedFactor(1));
        Assert.Equal(52f / 14, MapSettings.SpeedFactor(3));
    }

    private static readonly string[] AllToggles =
    [
        "TOG_PLAYAIISOANIM", "TOG_SHOWTURNSUMMARY", "TOG_FLIPBATTLE", "TOG_ANIMISO", "TOG_ISOHIGHLIGHT", "TOG_ISOCLOUDS", "TOG_BATTLEANIM",
        "TOG_AUTOSAVE", "TOG_CONFIRM_EOT", "TOG_SMOOTHSCROLL", "TOG_STACKSMOOTHSCROLL", "TOG_LOWENDVIDCARD", "TOG_HIGHALPHA", "TOG_PATHBATTLE",
        "TOG_INVERSEDPALMAP",
    ];

    private static readonly (string Spin, string Left, string Right)[] AllSpins =
    [
        ("SPIN_FX_VOLUME", "BTN_FX_LEFT", "BTN_FX_RIGHT"), ("SPIN_MUSIC_VOLUME", "BTN_MUSIC_LEFT", "BTN_MUSIC_RIGHT"),
        ("SPIN_MOVEMENT_SPEED", "BTN_PLAYER_LEFT", "BTN_PLAYER_RIGHT"), ("SPIN_OPPONENT_SPEED", "BTN_OPPONENT_LEFT", "BTN_OPPONENT_RIGHT"),
        ("SPIN_SCROLL_SPEED", "BTN_SCROLL_LEFT", "BTN_SCROLL_RIGHT"), ("SPIN_BATTLE_SPEED", "BTN_BATSPEED_LEFT", "BTN_BATSPEED_RIGHT"),
        ("SPIN_ACKNO", "BTN_ACKNOLEFT", "BTN_ACKNORIGHT"), ("SPIN_AUDIORGN", "BTN_AUDIRGN_LEFT", "BTN_AUDIRGN_RIGHT"),
    ];

    /// <summary>Пометка 5 прогона 14.10 («не сохраняются настройки»): каждый флажок и каждый счётчик окна, которые можно изменить, переживают
    /// «щелчок → файл → новое окно из файла»; прочие поля файла при этом не трогаются.</summary>
    [SkippableFact]
    public void Window_EveryControlSurvivesFileAndReopen()
    {
        var screen = UiScreens.Load(ImportedData.PathOf("ui/screens.json")).First(s => s.Id == MapOptionsLogic.Screen);
        Assert.Equal(AllToggles.Order(), screen.Elements.Where(e => e.Kind == UiKind.Toggle).Select(e => e.Id).Order());
        Assert.Equal(AllSpins.Select(s => s.Spin).Order(), screen.Elements.Where(e => e.Kind == UiKind.Spin).Select(e => e.Id).Order());
        string dir = Path.Combine(Path.GetTempPath(), "d2-map-options-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "settings.json");
        try
        {
            new MapSettings(Banners: true, Scaling: "integer").Save(path);
            foreach (var id in AllToggles)
            {
                var (was, now) = ClickAndReopen(screen, path, id, id);
                Assert.Equal(!was, now.IsChecked(id));
            }
            foreach (var (spin, left, right) in AllSpins)
            {
                // Шаг туда, где есть место: у левого края — вправо, иначе — влево.
                var probe = new UiScreenState(screen);
                new MapOptionsLogic(MapSettings.Load(path), _ => { }, id => id).Open(probe);
                int at = probe.SpinOption(spin);
                var (_, now) = ClickAndReopen(screen, path, spin, at == 0 ? right : left);
                Assert.Equal((spin, at == 0 ? 1 : at - 1), (spin, now.SpinOption(spin)));
            }
            var saved = MapSettings.Load(path);
            Assert.True(saved.Banners);
            Assert.Equal("integer", saved.Scaling);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Окно из файла, щелчок, запись в файл, новое окно из файла: было ли отмечено и состояние нового окна.</summary>
    private static (bool Was, UiScreenState Now) ClickAndReopen(UiScreen screen, string path, string control, string click)
    {
        var ui = new UiScreenState(screen);
        var logic = new MapOptionsLogic(MapSettings.Load(path), s => s.Save(path), id => id);
        logic.Open(ui);
        bool was = ui.IsChecked(control);
        Click(screen, ui, logic, click);
        var reopened = new UiScreenState(screen);
        new MapOptionsLogic(MapSettings.Load(path), _ => { }, id => id).Open(reopened);
        return (was, reopened);
    }

    /// <summary>Умолчания — как поставляемый <c>Disciple.ini</c> (ключа нет — как код чтения <c>0x619160</c>…), старый файл без новых полей
    /// читается так же; скорость прокрутки — ступень «Быстро» (0 мс), фоны мест 3 — «Нормал.», голоса 66 — «Средне».</summary>
    [SkippableFact]
    public void Settings_NewFieldsDefaultLikeShippedIni()
    {
        var shipped = new MapSettings();
        Assert.Equal((true, true, true, true, false, true, true, false, 0),
                     (shipped.AutoSave, shipped.ConfirmEndOfTurn, shipped.SmoothScroll, shipped.StackSmoothScroll, shipped.LowEndVideoCard,
                      shipped.HighAlpha, shipped.PathBattle, shipped.InversedPalMap, shipped.ScrollSpeed));
        string dir = Path.Combine(Path.GetTempPath(), "d2-map-old-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "settings.json");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, """{"PlayerSpeed":2,"OpponentSpeed":3,"Banners":true,"ScrollSpeed":400}""");
            Assert.Equal(shipped with { PlayerSpeed = 2, OpponentSpeed = 3, Banners = true, ScrollSpeed = 100 }, MapSettings.Load(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        var screen = UiScreens.Load(ImportedData.PathOf("ui/screens.json")).First(s => s.Id == MapOptionsLogic.Screen);
        var ui = new UiScreenState(screen);
        new MapOptionsLogic(shipped, _ => { }, id => $"[{id}]").Open(ui);
        Assert.Equal((2, "[X005TA0119]"), (ui.SpinOption("SPIN_SCROLL_SPEED"), ui.TextOf("SPIN_SCROLL_SPEED")));
        Assert.Equal((2, "[X005TA0814]"), (ui.SpinOption("SPIN_AUDIORGN"), ui.TextOf("SPIN_AUDIORGN")));
        Assert.Equal((2, "[X005TA0112]"), (ui.SpinOption("SPIN_ACKNO"), ui.TextOf("SPIN_ACKNO")));
        Assert.Equal([100, 50, 0], MapSettings.ScrollSteps);
        Assert.Equal([0, 33, 66, 100], MapSettings.AckSteps);
        Assert.Equal([0, 2, 3, 4], MapSettings.RegionSteps);
    }

    /// <summary>«Для слабых систем» / «для мощных систем» (<c>0x4c0c8f</c>/<c>0x4c0e6b</c>): вопрос <c>X005TA0863</c>/<c>0864</c>; «Да» — фоны
    /// мест на крайнюю ступень, «Слабая видеокарта», анимации, плавные прокрутки, подсветка, облака и полупрозрачность — разом; окно,
    /// снова ставшее верхним, показывает новое.</summary>
    [SkippableFact]
    public void Window_LowAndHighEndAskThenApply()
    {
        var screen = UiScreens.Load(ImportedData.PathOf("ui/screens.json")).First(s => s.Id == MapOptionsLogic.Screen);
        var ui = new UiScreenState(screen);
        var saved = new List<MapSettings>();
        var logic = new MapOptionsLogic(new MapSettings(), saved.Add, id => $"[{id}]");
        logic.Open(ui);
        Click(screen, ui, logic, "BTN_LOWEND");
        var ask = Assert.IsType<LastEdict.Data.Ui.Windows.UiAsk>(logic.TakeRequest());
        Assert.Equal("[X005TA0863]", ask.Text);
        Assert.Empty(saved);   // до «Да» — ничего
        ask.Yes();
        logic.Resume(ui);
        var low = Assert.Single(saved);
        Assert.Equal((0, false, false, true, false, false, false, false, false),
                     (low.AudioRegions, low.BattleAnim, low.AnimatedIso, low.LowEndVideoCard, low.SmoothScroll, low.StackSmoothScroll,
                      low.IsoHighlight, low.IsoClouds, low.HighAlpha));
        Assert.True(ui.IsChecked("TOG_LOWENDVIDCARD"));
        Assert.False(ui.IsChecked("TOG_HIGHALPHA"));
        Assert.Equal("[X005TA0812]", ui.TextOf("SPIN_AUDIORGN"));
        Assert.Equal((83, 1, true), (low.FxVolume, low.PlayerSpeed, low.AutoSave));   // прочее — как было

        Click(screen, ui, logic, "BTN_HIGHEND");
        var high = Assert.IsType<LastEdict.Data.Ui.Windows.UiAsk>(logic.TakeRequest());
        Assert.Equal("[X005TA0864]", high.Text);
        high.Yes();
        logic.Resume(ui);
        Assert.Equal(new MapSettings() with { AudioRegions = 4 }, logic.Settings);
        Assert.Equal("[X005TA0815]", ui.TextOf("SPIN_AUDIORGN"));
    }

    /// <summary>Автосохранение (пометка 5 прогона 14.10): окно не читало и не писало <c>TOG_AUTOSAVE</c> — флажок, выключенный или включённый
    /// пользователем, при следующем открытии возвращался к виду разметки. Теперь: по умолчанию включено (ini <c>AutoSave=1</c>), файл без
    /// поля (прежние настройки) — тоже включено; переключение → в файл → новое окно из прочитанного файла показывает то же.</summary>
    [SkippableFact]
    public void Window_AutoSaveToggle_SurvivesFileAndReopen()
    {
        var screen = UiScreens.Load(ImportedData.PathOf("ui/screens.json")).First(s => s.Id == MapOptionsLogic.Screen);
        string dir = Path.Combine(Path.GetTempPath(), "d2-map-autosave-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "settings.json");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, """{"PlayerSpeed":2,"OpponentSpeed":3,"PlayAiMoves":true,"Banners":true,"Scaling":"sharp"}""");
            Assert.True(MapSettings.Load(path).AutoSave);

            foreach (bool want in new[] { false, true })
            {
                var ui = new UiScreenState(screen);
                var logic = new MapOptionsLogic(MapSettings.Load(path), s => s.Save(path), id => id);
                logic.Open(ui);
                Assert.Equal(!want, ui.IsChecked("TOG_AUTOSAVE"));
                Click(screen, ui, logic, "TOG_AUTOSAVE");
                Assert.Equal(want, logic.Settings.AutoSave);

                var reopened = new UiScreenState(screen);   // «через несколько ходов» — новое окно из файла
                new MapOptionsLogic(MapSettings.Load(path), _ => { }, id => id).Open(reopened);
                Assert.Equal(want, reopened.IsChecked("TOG_AUTOSAVE"));
                Assert.Equal(2, MapSettings.Load(path).PlayerSpeed);   // прочие поля файла не тронуты
            }
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Вписывание экрана в окно (прогон 3 от 11.10, пункт 1): по умолчанию чёткий билинейный; имя из файла — без учёта регистра,
    /// неизвестное или пустое — чёткий; файл без поля (прежние настройки) — тоже чёткий.</summary>
    [Fact]
    public void Settings_Scaling_DefaultsToSharp()
    {
        Assert.Equal(LastEdict.Data.Ui.ScreenScaling.Sharp, new MapSettings().ScreenScaling);
        Assert.Equal(LastEdict.Data.Ui.ScreenScaling.Integer, new MapSettings(Scaling: "Integer").ScreenScaling);
        Assert.Equal("nearest", new MapSettings(Scaling: "NEAREST").Clamped().Scaling);
        Assert.Equal("sharp", new MapSettings(Scaling: "blurry").Clamped().Scaling);
        Assert.Equal("sharp", new MapSettings(Scaling: null!).Clamped().Scaling);
        string dir = Path.Combine(Path.GetTempPath(), "d2-map-scaling-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "settings.json");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, """{"PlayerSpeed": 2}""");
            Assert.Equal(LastEdict.Data.Ui.ScreenScaling.Sharp, MapSettings.Load(path).ScreenScaling);
            new MapSettings(Scaling: "integer").Save(path);
            Assert.Equal(LastEdict.Data.Ui.ScreenScaling.Integer, MapSettings.Load(path).ScreenScaling);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
