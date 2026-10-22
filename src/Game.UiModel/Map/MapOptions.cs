using System.Text.Json;
using LastEdict.Data.Menu;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;

namespace LastEdict.Data.Map;

/// <summary>
/// Настройки показа карты (свой JSON порта, не <c>Disciple.ini</c>): скорость движения своих отрядов и отрядов противника
/// (1…3, как <c>PlayerSpeed</c>/<c>OpponentSpeed</c> ini: кадр движения через 14 / 30 / 52 точки, <c>docs/map-render-stacks.md</c>
/// §6) и показ ходов ИИ на карте (<c>TOG_PLAYAIISOANIM</c>; выключен — отряды ИИ встают в конечную клетку без хода по клеткам,
/// ответ пользователя 26.09). <paramref name="ShowTurnSummary"/> — «Всегда показывать результаты хода» (<c>TOG_SHOWTURNSUMMARY</c>,
/// ключ ini <c>ShowTurnSummary</c>, по умолчанию выключен: чтение <c>0x619509</c> с умолчанием 0): итоги хода показываются в партии
/// нескольких людей или при включённом флажке (<see cref="TurnSummaryShown"/>). <paramref name="FlippedBattle"/> — «Поменяться сторонами»
/// (<c>TOG_FLIPBATTLE</c>, ключ ini <c>FlippedBattle</c>, по умолчанию 0: чтение <c>0x6194e5</c>): раскладка экрана боя
/// (<c>LastEdict.Data.Gfx.BattleLayout</c>). <paramref name="BattleSpeed"/> — «Скорость битвы» (<c>SPIN_BATTLE_SPEED</c>, ini <c>BattleSpeed</c> 1…4 с зажимом,
/// <c>0x619e2b</c>; в поставляемом ini — 2): 1–3 — только длительность надписей на портретах (<see cref="NoteTicks"/>), 4 «Сразу» — бой без
/// показа действий (<c>0x62e26a</c>). <paramref name="BattleAnim"/> — «Дополнительная анимация в бою» (<c>TOG_BATTLEANIM</c>, ini
/// <c>BattleAnim</c>, по умолчанию 1, <c>0x619d50</c>): покой <c>IDLE</c> или <c>STIL</c>, анимированные курсоры, рамки и метки.
/// <paramref name="Banners"/> — знамёна над отрядами (<c>TOG_BANNERS</c> палитры карты, <see cref="MapPalette.Banners"/>): у движка байт
/// настроек живёт до конца процесса, у порта сохраняется между запусками (ответ пользователя 11.10); по умолчанию выключены.
/// <paramref name="Scaling"/> — как экран 800×600 вписывается в окно (<see cref="LastEdict.Data.Ui.ScreenScaling"/>: <c>sharp</c> — чёткий
/// билинейный, по умолчанию; <c>integer</c> — целый масштаб с рамкой; <c>nearest</c> — дробный ближайшим соседом, как до 11.10). Настройка
/// порта, у движка её нет; окна для неё нет — только файл и ключ <c>--scale</c> (прогон 3 пользователя 11.10, пункт 1).
/// <para>Остальные флажки окна (пометка 5 прогона 14.10: «не сохраняются настройки» — окно их не читало и не писало), умолчания — как в
/// поставляемом <c>Disciple.ini</c>, ключа нет — как в коде чтения ini (<c>0x619160</c>…, <c>0x619d50</c>): <paramref name="AutoSave"/> —
/// «Автосохранение» (<c>AutoSave</c>, 1; запись по концу хода человека — <see cref="LastEdict.Data.Play.AutoSave"/>); <paramref name="ConfirmEndOfTurn"/> —
/// <c>ConfirmEOT</c> (1); <paramref name="SmoothScroll"/> — «Прокрутка между действиями» (<c>SmoothScroll</c>, 1);
/// <paramref name="StackSmoothScroll"/> — шаг кадра идущего отряда пополам (<c>StackSmoothScroll</c>, 1; <see cref="StackWalk"/>);
/// <paramref name="LowEndVideoCard"/> (<c>LowEndVideoCard</c>, 0); <paramref name="HighAlpha"/> (<c>HighAlpha</c>, 1);
/// <paramref name="PathBattle"/> — путь через бой (<c>DefaultPathBattle</c>, 1); <paramref name="InversedPalMap"/> — протаскивание по
/// мини-карте наоборот (в ini ключа нет, в коде 0); <paramref name="ScrollSpeed"/> — задержка шага прокрутки краем окна в мс
/// (<c>ScrollSpeed</c> 0…100, потолок <c>0x61a2ec</c>; в ini 0 — «быстро»); <paramref name="Intro"/> — ролик <c>INTRO</c> после заставки <c>SF</c> при
/// запуске (<c>Intro</c>, 1; <c>settings+0x157</c>, <c>0x404580</c>; окна для него нет — только файл).</para>
/// <para><paramref name="QueueStrip"/> — полоса очереди ходов над полем боя (своя настройка Last Edict, у движка её нет;
/// <c>docs/own-version-roadmap.md</c> §3.3), по умолчанию включена; окна для неё пока нет — только файл. Под проверкой без окна
/// (<c>--check</c>) экран боя её не показывает без ключа <c>--queue-strip</c>.</para>
/// <para>Настройки порта сверх окна движка (своя версия, техника будущего окна настроек; окна для них пока нет — только файл):
/// <paramref name="MasterVolume"/> и <paramref name="AmbientVolume"/> — общая громкость и громкость фонов мест, 0…100 % (по умолчанию 100 —
/// звучание как прежде; шины звука — <c>GameAudio</c>); <paramref name="WindowMode"/> — режим окна (<see cref="LastEdict.Data.Ui.DisplayMode"/>:
/// <c>windowed</c>, <c>borderless</c>, <c>fullscreen</c>), <paramref name="VSync"/> — вертикальная синхронизация, <paramref name="MaxFps"/> —
/// предел кадров в секунду (0 — без предела, иначе <see cref="MinFps"/>…<see cref="MaxFpsLimit"/>); применяются при запуске игры (сцена
/// <c>Boot</c>), не в проверках. В памяти настройки — одна плоская запись; в файле — разделы с версией схемы (<see cref="SettingsFile"/>).</para>
/// <para><paramref name="Language"/> — язык строк (Р14, Э5: окно настроек, вкладка «Язык»): <c>ru</c>, <c>en</c> или пусто — по умолчанию (русский,
/// если импортирован, иначе английский); ключ <c>--lang</c> главнее.</para>
/// <para><paramref name="Bindings"/> — переназначенные клавиши и кнопки геймпада (Р12, Э5: вкладка «Управление»,
/// <see cref="LastEdict.Data.Input.InputActions"/>); null — все по умолчанию. <paramref name="LargeUi"/> — крупный интерфейс (Р11, Steam Deck):
/// <see cref="LargeUiAuto"/> — на Steam Deck, <see cref="LargeUiOn"/>, <see cref="LargeUiOff"/>; крупнее курсор и подсказки кнопок.</para>
/// </summary>
public sealed record MapSettings(int PlayerSpeed = 1, int OpponentSpeed = 1, bool PlayAiMoves = true,
                                 int FxVolume = 83, int MusicVolume = 80, int Acknowledgements = 66, int AudioRegions = 3,
                                 bool ShowTurnSummary = false, bool FlippedBattle = false,
                                 bool AnimatedIso = true,
                                 bool IsoHighlight = true,
                                 bool IsoClouds = true, int BattleSpeed = 2, bool BattleAnim = true,
                                 bool Banners = false, string Scaling = "sharp", bool AutoSave = true,
                                 bool ConfirmEndOfTurn = true, bool SmoothScroll = true, bool StackSmoothScroll = true,
                                 bool LowEndVideoCard = false, bool HighAlpha = true, bool PathBattle = true,
                                 bool InversedPalMap = false, int ScrollSpeed = 0, bool Intro = true,
                                 bool QueueStrip = true,
                                 int MasterVolume = 100, int AmbientVolume = 100, string WindowMode = "windowed", bool VSync = true,
                                 int MaxFps = 0, string Language = "", LastEdict.Data.Input.KeyBindings? Bindings = null,
                                 int LargeUi = MapSettings.LargeUiAuto)
{
    /// <summary>Режим вписывания экрана в окно; неизвестное имя в файле — чёткий (<see cref="LastEdict.Data.Ui.ScreenScaling.Sharp"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public LastEdict.Data.Ui.ScreenScaling ScreenScaling => LastEdict.Data.Ui.ScreenFit.Parse(Scaling) ?? LastEdict.Data.Ui.ScreenScaling.Sharp;

    /// <summary>Режим окна; неизвестное имя в файле — окно (<see cref="LastEdict.Data.Ui.DisplayMode.Windowed"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public LastEdict.Data.Ui.DisplayMode Display => LastEdict.Data.Ui.DisplayModes.Parse(WindowMode) ?? LastEdict.Data.Ui.DisplayMode.Windowed;

    /// <summary>Потолок общей громкости и громкости фонов мест (проценты); предел кадров: 0 — без предела, иначе от 30 до 360.</summary>
    public const int MaxBusVolume = 100, MinFps = 30, MaxFpsLimit = 360;

    /// <summary>Крупный интерфейс: сам (на Steam Deck), включён, выключен.</summary>
    public const int LargeUiAuto = 0, LargeUiOn = 1, LargeUiOff = 2;

    // IsoClouds — облака над картой (TOG_ISOCLOUDS, ключ ini IsoBirds, settings+0x190, по умолчанию 1: 0x619427, кнопка 0x4c1f54 → 0x61a8f0).
    // IsoHighlight — подсветка клетки под курсором TILE_HIGHLIGHT (TOG_ISOHIGHLIGHT, ini IsoHighlight, поставляемый — 1; settings+0x189,
    // 0x61a8ab): над любой клеткой карты (решение пользователя 28.09).
    public const int MinBattleSpeed = 1, MaxBattleSpeed = 4, InstantBattleSpeed = 4;

    /// <summary>Сколько тиков панели (66 мс) держится надпись на портрете при скорости боя: N + 1, N = 15/10/5/5 (<c>0x64a4cd</c>,
    /// <c>0x64d120</c>).</summary>
    public static int NoteTicks(int battleSpeed) =>
        Math.Clamp(battleSpeed, MinBattleSpeed, MaxBattleSpeed) switch { 1 => 16, 2 => 11, _ => 6 };

    // AnimatedIso — анимация карты (TOG_ANIMISO, ini AnimatedIso, поставляемый — 1): выключена — объекты неподвижными картинками
    // IsoStill и неподвижная вода (0x5ac8bc, 0x5ab292); отряды, флаги и прочее из IsoCmon/IsoUnit анимируются всё равно.
    /// <summary>Показывать ли итоги хода (<c>0x48a363</c>): игра нескольких людей (<c>X160TA0025</c> «…а не только в режиме нескольких
    /// игроков») или включённый флажок; иначе окна итогов нет.</summary>
    public bool TurnSummaryShown(int humans) => humans > 1 || ShowTurnSummary;

    /// <summary>Громкость голосов отрядов 0…100 (<c>Acknoledgements</c> — так ключ написан в ini движка) и число фонов мест, звучащих
    /// сразу, 0…4 (<c>AudioRegions</c>); по умолчанию — из поставляемого <c>Disciple.ini</c> (66 и 3).</summary>
    public const int MaxAcknowledgements = 100, MaxAudioRegions = 4, MaxScrollSpeed = 100;

    /// <summary>Значения ступеней счётчиков окна настроек — списки объекта окна (<c>0x4bf000</c>…<c>0x4bf413</c>), ступень
    /// <c>низ + trunc((верх − низ)·доля)</c> (<c>0x4c05d9</c>): звук 0…127 — доли 0/.33/.66/1, музыка — 0/.2/.44/.63, голоса 0…100 —
    /// 0/.33/.66/1, скорости 1…3 — 0/.5/1, прокрутка 100…0 — 0/.5/1 (100 мс «медленно» … 0 «быстро»), фоны мест — 0, 2, 3, 4 числами.
    /// Значение файла показывается ближайшей ступенью (<c>0x4c14eb</c>).</summary>
    public static readonly int[] FxSteps = [0, 41, 83, 127], MusicSteps = [0, 25, 55, 80], AckSteps = [0, 33, 66, 100],
        SpeedSteps = [1, 2, 3], ScrollSteps = [100, 50, 0], BattleSteps = [1, 2, 3, 4], RegionSteps = [0, 2, 3, 4];

    public const int MaxVolume = 127;

    /// <summary>Номер ступени, ближайшей к значению.</summary>
    public static int Step(int[] steps, int value) =>
        Array.IndexOf(steps, steps.OrderBy(s => Math.Abs(s - value)).First());

    public const int MinSpeed = 1, MaxSpeed = 3;

    private static readonly int[] PixelsPerFrame = [14, 30, 52];

    /// <summary>Множитель скорости шага к скорости 1 (кадр через 14 точек).</summary>
    public static float SpeedFactor(int speed) =>
        PixelsPerFrame[Math.Clamp(speed, MinSpeed, MaxSpeed) - 1] / (float)PixelsPerFrame[0];

    /// <summary>Те же настройки со скоростями в границах.</summary>
    public MapSettings Clamped() => this with
    {
        PlayerSpeed = Math.Clamp(PlayerSpeed, MinSpeed, MaxSpeed),
        OpponentSpeed = Math.Clamp(OpponentSpeed, MinSpeed, MaxSpeed),
        FxVolume = Math.Clamp(FxVolume, 0, MaxVolume),
        MusicVolume = Math.Clamp(MusicVolume, 0, MaxVolume),
        Acknowledgements = Math.Clamp(Acknowledgements, 0, MaxAcknowledgements),
        AudioRegions = Math.Clamp(AudioRegions, 0, MaxAudioRegions),
        BattleSpeed = Math.Clamp(BattleSpeed, MinBattleSpeed, MaxBattleSpeed),
        ScrollSpeed = Math.Clamp(ScrollSpeed, 0, MaxScrollSpeed),
        Scaling = ScreenScaling.ToString().ToLowerInvariant(),
        MasterVolume = Math.Clamp(MasterVolume, 0, MaxBusVolume),
        AmbientVolume = Math.Clamp(AmbientVolume, 0, MaxBusVolume),
        WindowMode = LastEdict.Data.Ui.DisplayModes.Name(Display),
        MaxFps = MaxFps <= 0 ? 0 : Math.Clamp(MaxFps, MinFps, MaxFpsLimit),
        Language = (Language ?? "").Trim().ToLowerInvariant(),
        Bindings = Bindings is { Count: > 0 } ? Bindings : null,
        LargeUi = Math.Clamp(LargeUi, LargeUiAuto, LargeUiOff),
    };

    /// <summary>Быстрые настройки «для слабых» и «для мощных систем» (<c>BTN_LOWEND</c>/<c>BTN_HIGHEND</c>, ответ «Да» на вопрос
    /// <c>X005TA0863</c>/<c>0864</c>: <c>0x4c0c8f</c>/<c>0x4c0e6b</c>): фоны мест — первая или последняя ступень, «Слабая видеокарта» —
    /// включена или выключена, анимация боя и карты, обе плавные прокрутки, подсветка клетки, облака и полупрозрачность — наоборот.</summary>
    public MapSettings Preset(bool lowEnd) => this with
    {
        AudioRegions = lowEnd ? RegionSteps[0] : RegionSteps[^1],
        BattleAnim = !lowEnd, AnimatedIso = !lowEnd, LowEndVideoCard = lowEnd, SmoothScroll = !lowEnd, StackSmoothScroll = !lowEnd,
        IsoHighlight = !lowEnd, IsoClouds = !lowEnd, HighAlpha = !lowEnd,
    };

    /// <summary>Прочитать файл (<see cref="SettingsFile.Parse"/>: разделы схемы 2 или плоский файл схемы 1); нет файла, он испорчен или не
    /// читается — настройки по умолчанию.</summary>
    public static MapSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? SettingsFile.Parse(File.ReadAllText(path)) : new MapSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new MapSettings();
        }
    }

    /// <summary>Записать файл новой схемой (<see cref="SettingsFile.Format"/>): сначала во временный, затем подменой (как
    /// <c>GameState.Write</c>) — оборванная запись не портит прежние настройки.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
        string temp = path + ".tmp";
        File.WriteAllText(temp, SettingsFile.Format(this));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Изменить в файле только то, что меняет <paramref name="change"/>: прочитать, изменить, записать. Так пишут все хозяева
    /// файла (окно настроек карты и меню, переключатель знамён, <c>Alt+Enter</c>): у каждого в памяти своя копия, и запись копии целиком
    /// затёрла бы то, что тем временем изменил другой. Возвращает записанные настройки. Файл не прочитался (занят, нет прав) — исключение
    /// <see cref="IOException"/> или <see cref="UnauthorizedAccessException"/> и файл не трогается: запись умолчаний поверх стёрла бы его;
    /// испорченный файл (не JSON, не те типы) — как при чтении, умолчания плюс изменение.</summary>
    public static MapSettings Update(string path, Func<MapSettings, MapSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        string? text = File.Exists(path) ? File.ReadAllText(path) : null;
        MapSettings current;
        try
        {
            current = text is null ? new MapSettings() : SettingsFile.Parse(text);
        }
        catch (JsonException)
        {
            current = new MapSettings();
        }
        var next = change(current).Clamped();
        next.Save(path);
        return next;
    }
}

/// <summary>
/// Окно настроек <c>DLG_OPTIONS</c> (на карте и из главного меню): все счётчики и флажки окна читаются из <see cref="MapSettings"/> при
/// открытии и после вопроса и сразу уходят хозяину (<paramref name="changed"/>), который пишет файл. Счётчики — ступени списков окна
/// (<see cref="MapSettings.FxSteps"/> и др.) с подписями движка (<c>0x4bfb87</c>…<c>0x4c0190</c>, по какому списку какая подпись —
/// <c>0x4c021a</c>…<c>0x4c0470</c>): звук, музыка и голоса — «Выкл./Тихо/Средне/Громко» <c>X005TA0110</c>…<c>0113</c> (у звука и музыки
/// при выключенных <c>SoundFx</c>/<c>Music</c> первая — <c>X005TA0121</c>; порт их не выключает), своё и чужое движение — один список
/// <c>X005TA0114</c>…<c>0116</c>, прокрутка — <c>X005TA0117</c>…<c>0119</c>, бой — они же и <c>X005TA0767</c>, фоны мест —
/// <c>X005TA0812</c>…<c>0815</c>. «Для слабых/мощных систем» — вопрос и <see cref="MapSettings.Preset"/>.
/// Подписи строк и подсказка в <c>TXT_HELP</c> — по таблице окна движка (<see cref="Rows"/>). Остальные элементы окна — переходы графа.
/// Вне партии (окно из главного меню, <paramref name="inGame"/> = false) «Сохранить», «Загрузить», «Экспорт» и «Выход» спрятаны
/// (<c>0x4bf5a4</c>…: флаг приложения <c>+0x32</c> ложен — вызов <c>+0xa0</c> с 0; снимок 28.09 из меню — на месте кнопок пустые гнёзда:
/// спрятанные дают 0,06 % разницы, выключенные — 0,75 %; ранг 2).
/// </summary>
public sealed class MapOptionsLogic(MapSettings settings, Action<MapSettings> changed, Func<string, string> text, bool inGame = true)
    : IWindowLogic, IUiTicking
{
    /// <summary>Кнопки, которые вне партии спрятаны (порядок таблицы <c>0x79e178</c>).</summary>
    public static readonly string[] GameButtons = ["BTN_SAVE", "BTN_LOAD", "BTN_EXPORT", "BTN_QUIT"];

    public const string Screen = "DLG_OPTIONS";
    private const string AiToggle = "TOG_PLAYAIISOANIM", SummaryToggle = "TOG_SHOWTURNSUMMARY", Help = "TXT_HELP",
        FlipToggle = "TOG_FLIPBATTLE", LowEnd = "BTN_LOWEND", HighEnd = "BTN_HIGHEND";

    /// <summary>Строка окна: подпись, её строка, строка подсказки и элементы, над которыми подсказка показывается.</summary>
    public sealed record Row(string Label, string Text, string? HelpText, params string[] Controls);

    /// <summary>Таблица окна движка (<c>.rdata 0x6d8a44</c>, записи по 7 слов: подпись, строка, подсказка, элемент, стрелки): подписи
    /// ставит код окна, в разметке их нет. Подсказка — над элементом строки; мышь не над строкой — поле пусто (ранг 3).</summary>
    public static readonly Row[] Rows =
    [
        new("TXT_FX_VOLUME", "X005TA0410", "X005TA0840", "BTN_FX_LEFT", "BTN_FX_RIGHT"),
        new("TXT_MUSIC_VOLUME", "X005TA0411", "X005TA0841", "BTN_MUSIC_LEFT", "BTN_MUSIC_RIGHT"),
        new("TXT_UNIT_MOVEMENT", "X005TA0412", "X005TA0842", "BTN_PLAYER_LEFT", "BTN_PLAYER_RIGHT"),
        new("TXT_OPPONENT_MOVEMENT", "X005TA0413", "X005TA0843", "BTN_OPPONENT_LEFT", "BTN_OPPONENT_RIGHT"),
        new("TXT_SCROLL_SPEED", "X005TA0414", "X005TA0844", "BTN_SCROLL_LEFT", "BTN_SCROLL_RIGHT"),
        new("TXT_BATTLE_SPEED", "X005TA0700", "X005TA0845", "BTN_BATSPEED_LEFT", "BTN_BATSPEED_RIGHT"),
        new("TXT_ACKNO", "X005TA0760", "X005TA0846", "BTN_ACKNOLEFT", "BTN_ACKNORIGHT"),
        new("TXT_AUDIORGN", "X005TA0759", "X005TA0847", "BTN_AUDIRGN_LEFT", "BTN_AUDIRGN_RIGHT"),
        new("TXT_CONFIRM_EOT", "X005TA0415", "X005TA0848", "TOG_CONFIRM_EOT"),
        new("TXT_AUTOSAVE", "X005TA0416", "X005TA0849", "TOG_AUTOSAVE"),
        new("TXT_BATTLEANIM", "X005TA0701", "X005TA0850", "TOG_BATTLEANIM"),
        new("TXT_ANIMISO", "X005TA0706", "X005TA0851", "TOG_ANIMISO"),
        new("TXT_LOWENDVIDCARD", "X005TA0741", "X005TA0852", "TOG_LOWENDVIDCARD"),
        new("TXT_SMOOTHSCROLL", "X005TA0771", "X005TA0853", "TOG_SMOOTHSCROLL"),
        new("TXT_STACKSMOOTHSCROLL", "X005TA0826", "X005TA0854", "TOG_STACKSMOOTHSCROLL"),
        new("TXT_ISOHIGHLIGHT", "X005TA0762", "X005TA0855", "TOG_ISOHIGHLIGHT"),
        new("TXT_PATHBATTLE", "X005TA0765", "X005TA0856", "TOG_PATHBATTLE"),
        new("TXT_ISOCLOUDS", "X005TA0817", "X005TA0857", "TOG_ISOCLOUDS"),
        new("TXT_TITLE", "X005TA0419", null),
        new("TXT_GAMESETTINGS", "X005TA0834", null),
        new("TXT_GRAPHICS", "X005TA0835", null),
        new("TXT_SOUNDMUSIC", "X005TA0836", null),
        new("TXT_PERFORMANCE", "X005TA0837", null),
        new("", "", "X005TA0861", LowEnd),
        new("", "", "X005TA0862", HighEnd),
        new("TXT_HIGHALPHA", "X005TA0865", "X005TA0866", "TOG_HIGHALPHA"),
        new("TXT_INVERSEDPALMAP", "X005TA0889", "X005TA0890", "TOG_INVERSEDPALMAP"),
        new("TXT_PLAYAIISOANIM", "X005TA0933", "X005TA0934", AiToggle),
        new("TXT_FLIPBATTLE", "X160TA0015", "X160TA0016", FlipToggle),
        new("TXT_SHOWTURNSUMMARY", "X160TA0024", "X160TA0025", SummaryToggle),
    ];

    /// <summary>Флажок окна и его поле настроек.</summary>
    private sealed record Toggle(string Element, Func<MapSettings, bool> Get, Func<MapSettings, bool, MapSettings> Set);

    /// <summary>Счётчик окна: ступени значений, подписи ступеней и поле настроек.</summary>
    private sealed record Spin(string Element, int[] Steps, string[] Labels, Func<MapSettings, int> Get,
                               Func<MapSettings, int, MapSettings> Set);

    private static readonly Toggle[] Toggles =
    [
        new(AiToggle, s => s.PlayAiMoves, (s, v) => s with { PlayAiMoves = v }),
        new(SummaryToggle, s => s.ShowTurnSummary, (s, v) => s with { ShowTurnSummary = v }),
        new(FlipToggle, s => s.FlippedBattle, (s, v) => s with { FlippedBattle = v }),
        new("TOG_ANIMISO", s => s.AnimatedIso, (s, v) => s with { AnimatedIso = v }),
        new("TOG_ISOHIGHLIGHT", s => s.IsoHighlight, (s, v) => s with { IsoHighlight = v }),
        new("TOG_ISOCLOUDS", s => s.IsoClouds, (s, v) => s with { IsoClouds = v }),
        new("TOG_BATTLEANIM", s => s.BattleAnim, (s, v) => s with { BattleAnim = v }),
        new("TOG_AUTOSAVE", s => s.AutoSave, (s, v) => s with { AutoSave = v }),
        new("TOG_CONFIRM_EOT", s => s.ConfirmEndOfTurn, (s, v) => s with { ConfirmEndOfTurn = v }),
        new("TOG_SMOOTHSCROLL", s => s.SmoothScroll, (s, v) => s with { SmoothScroll = v }),
        new("TOG_STACKSMOOTHSCROLL", s => s.StackSmoothScroll, (s, v) => s with { StackSmoothScroll = v }),
        new("TOG_LOWENDVIDCARD", s => s.LowEndVideoCard, (s, v) => s with { LowEndVideoCard = v }),
        new("TOG_HIGHALPHA", s => s.HighAlpha, (s, v) => s with { HighAlpha = v }),
        new("TOG_PATHBATTLE", s => s.PathBattle, (s, v) => s with { PathBattle = v }),
        new("TOG_INVERSEDPALMAP", s => s.InversedPalMap, (s, v) => s with { InversedPalMap = v }),
    ];

    private static readonly string[] VolumeLabels = ["X005TA0110", "X005TA0111", "X005TA0112", "X005TA0113"];
    private static readonly string[] SpeedLabels = ["X005TA0114", "X005TA0115", "X005TA0116"];
    private static readonly string[] SlowFastLabels = ["X005TA0117", "X005TA0118", "X005TA0119"];

    private static readonly Spin[] Spins =
    [
        new("SPIN_FX_VOLUME", MapSettings.FxSteps, VolumeLabels, s => s.FxVolume, (s, v) => s with { FxVolume = v }),
        new("SPIN_MUSIC_VOLUME", MapSettings.MusicSteps, VolumeLabels, s => s.MusicVolume, (s, v) => s with { MusicVolume = v }),
        new("SPIN_MOVEMENT_SPEED", MapSettings.SpeedSteps, SpeedLabels, s => s.PlayerSpeed, (s, v) => s with { PlayerSpeed = v }),
        new("SPIN_OPPONENT_SPEED", MapSettings.SpeedSteps, SpeedLabels, s => s.OpponentSpeed, (s, v) => s with { OpponentSpeed = v }),
        new("SPIN_SCROLL_SPEED", MapSettings.ScrollSteps, SlowFastLabels, s => s.ScrollSpeed, (s, v) => s with { ScrollSpeed = v }),
        new("SPIN_BATTLE_SPEED", MapSettings.BattleSteps, [.. SlowFastLabels, "X005TA0767"], s => s.BattleSpeed,
            (s, v) => s with { BattleSpeed = v }),
        new("SPIN_ACKNO", MapSettings.AckSteps, VolumeLabels, s => s.Acknowledgements, (s, v) => s with { Acknowledgements = v }),
        new("SPIN_AUDIORGN", MapSettings.RegionSteps, ["X005TA0812", "X005TA0813", "X005TA0814", "X005TA0815"], s => s.AudioRegions,
            (s, v) => s with { AudioRegions = v }),
    ];

    /// <summary>Поля окна (все флажки и счётчики) из <paramref name="window"/> поверх <paramref name="onto"/>: так окно пишет файл
    /// (<see cref="MapSettings.Update"/>), не трогая полей, которые меняют другие хозяева (знамёна, режим окна, громкости шин).</summary>
    public static MapSettings Adopt(MapSettings window, MapSettings onto)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(onto);
        foreach (var toggle in Toggles) onto = toggle.Set(onto, toggle.Get(window));
        foreach (var spin in Spins) onto = spin.Set(onto, spin.Get(window));
        return onto;
    }

    private readonly UiRequests _requests = new();
    private string? _hovered;

    public MapSettings Settings { get; private set; } = settings.Clamped();

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        foreach (var row in Rows)
            if (row.Label.Length > 0) ui.SetText(row.Label, text(row.Text));
        foreach (var button in GameButtons) ui.SetHidden(button, !inGame);
        Show(ui);
    }

    /// <summary>Окно снова наверху (закрылся вопрос быстрых настроек) — флажки и счётчики из настроек.</summary>
    public void Resume(UiScreenState ui) => Show(ui);

    public UiRequest? TakeRequest() => _requests.Take();

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        switch (e)
        {
            case UiClicked { Element: LowEnd or HighEnd } click:
                bool low = click.Element == LowEnd;
                _requests.Add(new UiAsk(text(low ? "X005TA0863" : "X005TA0864"), () => Change(Settings.Preset(low))));
                return;
            case UiToggled t when Toggles.FirstOrDefault(x => x.Element == t.Element) is { } toggle:
                Change(toggle.Set(Settings, t.Checked));
                break;
            case UiSpinChanged s when Spins.FirstOrDefault(x => x.Element == s.Element) is { } spin && s.Option < spin.Steps.Length:
                Change(spin.Set(Settings, spin.Steps[s.Option]));
                break;
            default:
                return;
        }
        Labels(ui);
    }

    /// <summary>Подсказка строки под мышью — в <c>TXT_HELP</c>; меняется только при смене элемента под мышью.</summary>
    public void Tick(int elapsedMs, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ui.Hovered == _hovered) return;
        _hovered = ui.Hovered;
        var row = _hovered is null ? null : Rows.FirstOrDefault(r => r.HelpText is not null && r.Controls.Contains(_hovered));
        ui.SetText(Help, row?.HelpText is { } help ? text(help) : "");
    }

    private void Change(MapSettings next)
    {
        next = next.Clamped();
        if (next == Settings) return;
        Settings = next;
        changed(next);
    }

    private void Show(UiScreenState ui)
    {
        foreach (var toggle in Toggles) ui.SetChecked(toggle.Element, toggle.Get(Settings));
        foreach (var spin in Spins) ui.SetSpin(spin.Element, spin.Steps.Length, MapSettings.Step(spin.Steps, spin.Get(Settings)));
        Labels(ui);
    }

    private void Labels(UiScreenState ui)
    {
        foreach (var spin in Spins) ui.SetText(spin.Element, text(spin.Labels[MapSettings.Step(spin.Steps, spin.Get(Settings))]));
    }
}
