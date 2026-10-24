using LastEdict.Data.Map;
using LastEdict.Data.Ui;

namespace LastEdict.Data.Settings;

/// <summary>Вкладки окна настроек (Э5, roadmap §3.3).</summary>
public enum SettingsTab { Display, Audio, Game, Battle, Controls, Language }

/// <summary>Вид пункта: флажок, ползунок (число в границах с шагом) или выбор из списка.</summary>
public enum SettingKind { Toggle, Slider, Choice }

/// <summary>Вариант пункта-выбора: значение поля и ключ строки подписи.</summary>
public sealed record SettingChoice(int Value, string Label);

/// <summary>Пункт окна настроек: поле <see cref="MapSettings"/> числом (флажок — 0/1, выбор — значение варианта), подпись и подсказка — ключи
/// строк (Р14: тексты только ключами; у пунктов окна оригинала — его строки <c>X005TA…</c>, у новых — свой слой <c>data/ui/own</c>).
/// <see cref="Display"/> — пункт режима экрана: применяется кнопкой «Применить» с подтверждением и откатом через
/// <see cref="DisplayConfirm.Seconds"/> с.</summary>
public sealed record SettingItem(string Id, SettingsTab Tab, SettingKind Kind, string Label, string? Help,
                                 Func<MapSettings, int> Get, Func<MapSettings, int, MapSettings> Set)
{
    public int Min { get; init; }
    public int Max { get; init; } = 1;
    public int Step { get; init; } = 1;
    public IReadOnlyList<SettingChoice> Choices { get; init; } = [];
    public bool Display { get; init; }

    /// <summary>Значение в границах пункта: ползунок — к шагу, выбор — к ближайшему варианту, флажок — 0/1.</summary>
    public int Clamp(int value) => Kind switch
    {
        SettingKind.Toggle => value != 0 ? 1 : 0,
        SettingKind.Choice => Choices.Count == 0 ? value : Choices.OrderBy(c => Math.Abs(c.Value - value)).First().Value,
        _ => Math.Clamp(Min + ((int)Math.Round((value - Min) / (double)Step) * Step), Min, Max),
    };
}

/// <summary>Пункты окна настроек по вкладкам — данными: окно строит из них свои элементы, проверки без движка сверяют их с файлом настроек.
/// Каждое поле <see cref="MapSettings"/>, которое пользователь выбирает сам, — ровно в одном пункте (тест сверяет с
/// <see cref="SettingsFile.Layout"/>); поля без пункта — <see cref="Unlisted"/>.</summary>
public static class SettingsPages
{
    private static int B(bool v) => v ? 1 : 0;

    private static SettingItem Toggle(string id, SettingsTab tab, string label, string? help, Func<MapSettings, bool> get,
                                      Func<MapSettings, bool, MapSettings> set) =>
        new(id, tab, SettingKind.Toggle, label, help, s => B(get(s)), (s, v) => set(s, v != 0));

    /// <summary>Строки окна оригинала: подписи шагов громкости и скоростей (<c>MapOptionsLogic</c>).</summary>
    private static SettingChoice[] Steps(int[] values, string[] labels) => [.. values.Zip(labels, (v, l) => new SettingChoice(v, l))];

    private static readonly string[] SpeedLabels = ["X005TA0114", "X005TA0115", "X005TA0116"];

    /// <summary>Варианты размера: по умолчанию (<paramref name="byDefault"/>) и размеры списка <see cref="DisplaySizes.Common"/>.</summary>
    private static SettingChoice[] Sizes(string byDefault) =>
        [new(0, byDefault), .. DisplaySizes.Common.Select(s => new SettingChoice(DisplaySizes.Encode(s.W, s.H), DisplaySizes.Label(s)))];

    /// <summary>Языки окна по номеру варианта: «» — по умолчанию, затем русский и английский (Р14; новые — модом-переводом).</summary>
    public static readonly string[] Languages = ["", "ru", "en"];

    public static readonly IReadOnlyList<SettingItem> All =
    [
        // «Экран»: окно, кадры и вписывание — с подтверждением; графика карты — сразу.
        new("windowMode", SettingsTab.Display, SettingKind.Choice, "LE_SET_WINDOW_MODE", "LE_SET_WINDOW_MODE_HELP",
            s => (int)s.Display, (s, v) => s with { WindowMode = DisplayModes.Name((DisplayMode)v) })
        {
            Choices = [new((int)DisplayMode.Windowed, "LE_SET_WINDOWED"), new((int)DisplayMode.Borderless, "LE_SET_BORDERLESS"),
                       new((int)DisplayMode.Fullscreen, "LE_SET_FULLSCREEN")],
            Display = true,
        },
        // Размер окна и разрешение полного экрана (ответ пользователя 22.10): подписи — сами размеры.
        new("windowSize", SettingsTab.Display, SettingKind.Choice, "LE_SET_WINDOW_SIZE", "LE_SET_WINDOW_SIZE_HELP", s => s.WindowSize,
            (s, v) => s with { WindowSize = v })
        {
            Choices = Sizes("LE_SET_SIZE_DEFAULT"),
            Display = true,
        },
        new("resolution", SettingsTab.Display, SettingKind.Choice, "LE_SET_RESOLUTION", "LE_SET_RESOLUTION_HELP", s => s.Resolution,
            (s, v) => s with { Resolution = v })
        {
            Choices = Sizes("LE_SET_RES_NATIVE"),
            Display = true,
        },
        // Масштаб карты (ответ пользователя 24.10: «как во врапере, карты больше видно») — сразу, без подтверждения.
        new("mapZoom", SettingsTab.Display, SettingKind.Choice, "LE_SET_MAP_ZOOM", "LE_SET_MAP_ZOOM_HELP", s => s.MapZoom,
            (s, v) => s with { MapZoom = v })
        {
            Choices = [.. MapSettings.MapZooms.Select(z => new SettingChoice(z, z == MapSettings.MapZoomFit ? "LE_SET_MAP_ZOOM_FIT" : $"{z} %"))],
        },
        new("vsync", SettingsTab.Display, SettingKind.Toggle, "LE_SET_VSYNC", "LE_SET_VSYNC_HELP", s => B(s.VSync), (s, v) => s with { VSync = v != 0 })
        {
            Display = true,
        },
        new("maxFps", SettingsTab.Display, SettingKind.Choice, "LE_SET_MAX_FPS", "LE_SET_MAX_FPS_HELP", s => s.MaxFps, (s, v) => s with { MaxFps = v })
        {
            Choices = [new(0, "LE_SET_FPS_UNLIMITED"), new(30, "LE_SET_FPS_30"), new(60, "LE_SET_FPS_60"), new(120, "LE_SET_FPS_120"),
                       new(144, "LE_SET_FPS_144"), new(240, "LE_SET_FPS_240")],
            Display = true,
        },
        new("scaling", SettingsTab.Display, SettingKind.Choice, "LE_SET_SCALING", "LE_SET_SCALING_HELP", s => (int)s.ScreenScaling,
            (s, v) => s with { Scaling = ((ScreenScaling)v).ToString().ToLowerInvariant() })
        {
            Choices = [new((int)ScreenScaling.Sharp, "LE_SET_SCALING_SHARP"), new((int)ScreenScaling.Integer, "LE_SET_SCALING_INTEGER"),
                       new((int)ScreenScaling.Nearest, "LE_SET_SCALING_NEAREST")],
            Display = true,
        },
        // Сглаживание кадра (ответ пользователя 24.10): постобработка всего кадра — смягчает рваные края спрайтов оригинала.
        new("antialiasing", SettingsTab.Display, SettingKind.Choice, "LE_SET_AA", "LE_SET_AA_HELP", s => s.Antialiasing,
            (s, v) => s with { Antialiasing = v })
        {
            Choices = [new(MapSettings.AntialiasingOff, "LE_SET_AA_OFF"), new(MapSettings.AntialiasingFxaa, "FXAA"),
                       new(MapSettings.AntialiasingSmaa, "SMAA")],
            Display = true,
        },
        new("largeUi", SettingsTab.Display, SettingKind.Choice, "LE_SET_LARGE_UI", "LE_SET_LARGE_UI_HELP", s => s.LargeUi,
            (s, v) => s with { LargeUi = v })
        {
            Choices = [new(MapSettings.LargeUiAuto, "LE_SET_LARGE_AUTO"), new(MapSettings.LargeUiOn, "LE_SET_LARGE_ON"),
                       new(MapSettings.LargeUiOff, "LE_SET_LARGE_OFF")],
        },
        Toggle("animatedMap", SettingsTab.Display, "X005TA0706", "X005TA0851", s => s.AnimatedIso, (s, v) => s with { AnimatedIso = v }),
        Toggle("cellHighlight", SettingsTab.Display, "X005TA0762", "X005TA0855", s => s.IsoHighlight, (s, v) => s with { IsoHighlight = v }),
        Toggle("clouds", SettingsTab.Display, "X005TA0817", "X005TA0857", s => s.IsoClouds, (s, v) => s with { IsoClouds = v }),
        Toggle("banners", SettingsTab.Display, "LE_SET_BANNERS", "LE_SET_BANNERS_HELP", s => s.Banners, (s, v) => s with { Banners = v }),
        Toggle("highAlpha", SettingsTab.Display, "X005TA0865", "X005TA0866", s => s.HighAlpha, (s, v) => s with { HighAlpha = v }),
        Toggle("lowEndVideoCard", SettingsTab.Display, "X005TA0741", "X005TA0852", s => s.LowEndVideoCard, (s, v) => s with { LowEndVideoCard = v }),

        // «Звук»: громкости — ползунками (у оригинала — четыре ступени), сразу слышно.
        new("master", SettingsTab.Audio, SettingKind.Slider, "LE_SET_MASTER", "LE_SET_MASTER_HELP", s => s.MasterVolume,
            (s, v) => s with { MasterVolume = v }) { Max = MapSettings.MaxBusVolume, Step = 5 },
        new("music", SettingsTab.Audio, SettingKind.Slider, "X005TA0411", "X005TA0841", s => s.MusicVolume,
            (s, v) => s with { MusicVolume = v }) { Max = MapSettings.MaxVolume },
        new("fx", SettingsTab.Audio, SettingKind.Slider, "X005TA0410", "X005TA0840", s => s.FxVolume,
            (s, v) => s with { FxVolume = v }) { Max = MapSettings.MaxVolume },
        new("voice", SettingsTab.Audio, SettingKind.Slider, "X005TA0760", "X005TA0846", s => s.Acknowledgements,
            (s, v) => s with { Acknowledgements = v }) { Max = MapSettings.MaxAcknowledgements, Step = 5 },
        new("ambient", SettingsTab.Audio, SettingKind.Slider, "LE_SET_AMBIENT", "LE_SET_AMBIENT_HELP", s => s.AmbientVolume,
            (s, v) => s with { AmbientVolume = v }) { Max = MapSettings.MaxBusVolume, Step = 5 },
        new("ambientSources", SettingsTab.Audio, SettingKind.Choice, "X005TA0759", "X005TA0847", s => s.AudioRegions,
            (s, v) => s with { AudioRegions = v })
        {
            Choices = Steps(MapSettings.RegionSteps, ["X005TA0812", "X005TA0813", "X005TA0814", "X005TA0815"]),
        },

        // «Игра».
        new("playerSpeed", SettingsTab.Game, SettingKind.Choice, "X005TA0412", "X005TA0842", s => s.PlayerSpeed,
            (s, v) => s with { PlayerSpeed = v }) { Choices = Steps(MapSettings.SpeedSteps, SpeedLabels) },
        new("opponentSpeed", SettingsTab.Game, SettingKind.Choice, "X005TA0413", "X005TA0843", s => s.OpponentSpeed,
            (s, v) => s with { OpponentSpeed = v }) { Choices = Steps(MapSettings.SpeedSteps, SpeedLabels) },
        Toggle("showAiMoves", SettingsTab.Game, "X005TA0933", "X005TA0934", s => s.PlayAiMoves, (s, v) => s with { PlayAiMoves = v }),
        Toggle("turnSummary", SettingsTab.Game, "X160TA0024", "X160TA0025", s => s.ShowTurnSummary, (s, v) => s with { ShowTurnSummary = v }),
        Toggle("autoSave", SettingsTab.Game, "X005TA0416", "X005TA0849", s => s.AutoSave, (s, v) => s with { AutoSave = v }),
        Toggle("confirmEndOfTurn", SettingsTab.Game, "X005TA0415", "X005TA0848", s => s.ConfirmEndOfTurn, (s, v) => s with { ConfirmEndOfTurn = v }),
        new("scrollSpeed", SettingsTab.Game, SettingKind.Slider, "X005TA0414", "X005TA0844", s => MapSettings.MaxScrollSpeed - s.ScrollSpeed,
            (s, v) => s with { ScrollSpeed = MapSettings.MaxScrollSpeed - v }) { Max = MapSettings.MaxScrollSpeed, Step = 10 },
        Toggle("smoothScroll", SettingsTab.Game, "X005TA0771", "X005TA0853", s => s.SmoothScroll, (s, v) => s with { SmoothScroll = v }),
        Toggle("stackSmoothScroll", SettingsTab.Game, "X005TA0826", "X005TA0854", s => s.StackSmoothScroll, (s, v) => s with { StackSmoothScroll = v }),
        Toggle("pathBattle", SettingsTab.Game, "X005TA0765", "X005TA0856", s => s.PathBattle, (s, v) => s with { PathBattle = v }),
        Toggle("invertedMinimap", SettingsTab.Game, "X005TA0889", "X005TA0890", s => s.InversedPalMap, (s, v) => s with { InversedPalMap = v }),
        Toggle("intro", SettingsTab.Game, "LE_SET_INTRO", "LE_SET_INTRO_HELP", s => s.Intro, (s, v) => s with { Intro = v }),

        // «Бой».
        new("battleSpeed", SettingsTab.Battle, SettingKind.Choice, "X005TA0700", "X005TA0845", s => s.BattleSpeed, (s, v) => s with { BattleSpeed = v })
        {
            Choices = Steps(MapSettings.BattleSteps, ["X005TA0117", "X005TA0118", "X005TA0119", "X005TA0767"]),
        },
        Toggle("battleAnimation", SettingsTab.Battle, "X005TA0701", "X005TA0850", s => s.BattleAnim, (s, v) => s with { BattleAnim = v }),
        Toggle("flipped", SettingsTab.Battle, "X160TA0015", "X160TA0016", s => s.FlippedBattle, (s, v) => s with { FlippedBattle = v }),
        Toggle("queueStrip", SettingsTab.Battle, "LE_SET_QUEUE_STRIP", "LE_SET_QUEUE_STRIP_HELP", s => s.QueueStrip, (s, v) => s with { QueueStrip = v }),

        // «Язык»: значение — номер в списке языков (Languages); 0 — по умолчанию (русский, если импортирован).
        new("language", SettingsTab.Language, SettingKind.Choice, "LE_SET_LANGUAGE", "LE_SET_LANGUAGE_HELP",
            s => Array.IndexOf(Languages, s.Language) is var i and >= 0 ? i : 0, (s, v) => s with { Language = Languages[Math.Clamp(v, 0, Languages.Length - 1)] })
        {
            Choices = [new(0, "LE_SET_LANGUAGE_AUTO"), new(1, "LE_SET_LANGUAGE_RU"), new(2, "LE_SET_LANGUAGE_EN")],
        },
    ];

    /// <summary>Поля файла настроек без пункта окна: окна для них нет — только файл (переназначение клавиш — своя вкладка «Управление»).</summary>
    public static readonly IReadOnlyList<string> Unlisted = [nameof(MapSettings.Bindings)];

    public static IEnumerable<SettingItem> Of(SettingsTab tab) => All.Where(i => i.Tab == tab);

    public static SettingItem Get(string id) => All.FirstOrDefault(i => i.Id == id) ?? throw new KeyNotFoundException($"нет пункта настроек {id}");

    /// <summary>Ключ строки названия вкладки.</summary>
    public static string TabLabel(SettingsTab tab) => tab switch
    {
        SettingsTab.Display => "LE_SET_TAB_DISPLAY",
        SettingsTab.Audio => "LE_SET_TAB_AUDIO",
        SettingsTab.Game => "LE_SET_TAB_GAME",
        SettingsTab.Battle => "LE_SET_TAB_BATTLE",
        SettingsTab.Controls => "LE_SET_TAB_CONTROLS",
        _ => "LE_SET_TAB_LANGUAGE",
    };
}

/// <summary>Черновик окна настроек: что сохранено (<see cref="Saved"/>) и что выбрано сейчас (<see cref="Current"/>). Пункты экрана
/// (<see cref="SettingItem.Display"/>) ждут «Применить» и подтверждения; прочие действуют сразу (громкость слышно, пока двигаешь), а в файл
/// всё идёт по «Применить»; «Отмена» — назад к сохранённому.</summary>
public sealed class SettingsDraft(MapSettings saved)
{
    public MapSettings Saved { get; private set; } = saved.Clamped();
    public MapSettings Current { get; private set; } = saved.Clamped();

    /// <summary>Значение пункта сейчас.</summary>
    public int Value(SettingItem item) => item.Get(Current);

    /// <summary>Поставить значение пункта (в его границах).</summary>
    public MapSettings Set(SettingItem item, int value)
    {
        ArgumentNullException.ThrowIfNull(item);
        Current = item.Set(Current, item.Clamp(value)).Clamped();
        return Current;
    }

    /// <summary>Есть ли что применять.</summary>
    public bool Changed => Current != Saved;

    /// <summary>Изменены ли пункты экрана — «Применить» спросит подтверждение.</summary>
    public bool DisplayChanged => SettingsPages.All.Where(i => i.Display).Any(i => i.Get(Current) != i.Get(Saved));

    /// <summary>Все пункты — по умолчанию (<see cref="MapSettings"/> без аргументов); язык и экран — тоже.</summary>
    public MapSettings Defaults()
    {
        Current = new MapSettings();
        return Current;
    }

    /// <summary>Привязки клавиш и кнопок (вкладка «Управление», <see cref="MapSettings.Bindings"/>); null — все по умолчанию.</summary>
    public MapSettings Bind(LastEdict.Data.Input.KeyBindings? bindings)
    {
        Current = (Current with { Bindings = bindings }).Clamped();
        return Current;
    }

    /// <summary>Быстрые настройки оригинала «Низкое/Высокое качество» (<see cref="MapSettings.Preset"/>, кнопки <c>BTN_LOWEND</c>/<c>BTN_HIGHEND</c>).</summary>
    public MapSettings Preset(bool lowEnd)
    {
        Current = Current.Preset(lowEnd).Clamped();
        return Current;
    }

    /// <summary>Применено и подтверждено — выбранное становится сохранённым.</summary>
    public MapSettings Commit()
    {
        Saved = Current;
        return Saved;
    }

    /// <summary>Режим экрана не подтверждён — пункты экрана назад к сохранённым, прочее выбранное остаётся.</summary>
    public MapSettings RevertDisplay()
    {
        foreach (var item in SettingsPages.All.Where(i => i.Display)) Current = item.Set(Current, item.Get(Saved));
        return Current;
    }

    /// <summary>Отмена — всё назад к сохранённому.</summary>
    public MapSettings Revert()
    {
        Current = Saved;
        return Current;
    }
}

/// <summary>Подтверждение нового режима экрана: «Оставить?» с отсчётом <see cref="Seconds"/> с; не ответили — откат (как у ОС и игр:
/// новый режим мог оставить игрока без картинки). Время — снаружи (окно отдаёт прошедшие секунды кадра), поэтому проверяется без движка.</summary>
public sealed class DisplayConfirm
{
    public const int Seconds = 15;

    private double _left = Seconds;

    /// <summary>Сколько целых секунд осталось (для подписи «Откат через N с»).</summary>
    public int Left => (int)Math.Ceiling(Math.Max(0, _left));

    public bool Expired => _left <= 0;

    /// <summary>Прошло <paramref name="seconds"/>; true — время вышло (окно откатывает режим).</summary>
    public bool Tick(double seconds)
    {
        _left -= seconds;
        return Expired;
    }
}
