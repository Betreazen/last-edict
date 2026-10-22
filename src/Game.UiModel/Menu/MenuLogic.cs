using LastEdict.Data.Game;
using LastEdict.Data.Ui;

namespace LastEdict.Data.Menu;

/// <summary>Выбор игрока по дороге к новой игре: сценарий, раса, лорд, лицо, сложность, имя. Один на всю цепочку
/// экранов меню — экраны пишут в него, следующие читают.</summary>
public sealed class NewGameSetup
{
    public ScenarioInfo? Scenario { get; set; }

    /// <summary>Раса игрока по id (<see cref="RaceInfo.Id"/>; Э5 — не по роду: раса мода бывает того же рода, что и раса оригинала).</summary>
    public string RaceId { get; set; } = "";

    /// <summary>Тип лорда (<c>Llord</c>: 0 — маг, 1 — воин, 2 — вор).</summary>
    public int LordCategory { get; set; } = 1;

    /// <summary>Номер лица лорда (0…3, ролики <c>FACE_XX_N</c>).</summary>
    public int Face { get; set; }

    /// <summary>Сложность (<c>Ldiff</c>: 0 — легко … 3 — очень сложно).</summary>
    public int Difficulty { get; set; } = 1;

    public string? LordName { get; set; }

    /// <summary>Импортированный герой одиночной игры (<c>DLG_CHOOSE_LORD</c> → <c>DLG_IMPORT_LEADER</c>); null — стартовый.</summary>
    public Leaders.ExportedLeader? ImportedLeader { get; set; }

    /// <summary>Места людей партии нескольких людей (лобби сетевой игры, Э7; «горячий стул» вырезан, Р33): раса, имя, лицо, лорд; пусто —
    /// одиночная игра по <see cref="Race"/>, <see cref="LordCategory"/>, <see cref="Face"/>, <see cref="LordName"/>.</summary>
    public IReadOnlyList<PlayerSeat> Seats { get; set; } = [];

    /// <summary>Стартовое золото и мана лобби (0…2000): оба 0 — казна из сценария, иначе казна каждого игрока карты — золото и столько же
    /// маны каждого вида (<c>0x42b74f</c>).</summary>
    public int StartGold { get; set; }

    public int StartMana { get; set; }

    /// <summary>Сохранение, выбранное для загрузки на экране загрузки меню; хозяин начинает партию из него.</summary>
    public string? LoadPath { get; set; }

    /// <summary>Меню ведёт в сагу («Новая сага», <c>BTN_NEW_TOURNEMENT</c>): выбор расы выбирает кампанию, а не расу сценария.</summary>
    public bool Saga { get; set; }

    /// <summary>Глава кампании, которую начинает меню; null — квест.</summary>
    public Play.CampaignRun? Campaign { get; set; }

    /// <summary>Перенос из прошлой главы (герой и вещи); null — первая глава или без переноса.</summary>
    public Play.CarryOver? Carry { get; set; }

    /// <summary>Какие расы можно выбрать (своя кампания — только раса её заголовка, <c>0x4eae60</c>); null — расы сценария.</summary>
    public IReadOnlyList<string>? RaceLimit { get; set; }
}

/// <summary>Место человека в лобби: раса (id), имя лорда, лицо (0…3) и категория лорда (<c>Llord</c>).</summary>
/// <param name="Leader">Импортированный герой (<c>BTN_IMPORT_LEADER</c>); null — стартовый герой сценария.</param>
/// <param name="PlayerId">Игрок сценария этого места; null — первый ещё не занятый игрок расы <paramref name="RaceId"/> (у сценария бывает
/// несколько игроков одной расы, Э4).</param>
/// <param name="Team">Команда игрока (<see cref="LastEdict.Rules.Map.Teams"/>); null — как в сценарии.</param>
public sealed record PlayerSeat(string RaceId, string Name, int Face, int LordCategory, Leaders.ExportedLeader? Leader = null,
                                 string? PlayerId = null, string? Team = null);

/// <summary>Что нужно логике экранов: каталог, строки языка, выбор игрока и системное имя (им игра заполняет поле
/// имени лорда).</summary>
public sealed record MenuContext(GameCatalog Catalog, IReadOnlyDictionary<string, string> Strings,
                                 NewGameSetup Setup, string SystemName)
{
    /// <summary>Каталог сохранений игрока; пусто — экрана загрузки нет.</summary>
    public string SavesDir { get; init; } = "";

    /// <summary>Правила — для карточек героя из файла (предметы, типы воинов); нет — карточек нет.</summary>
    public Play.GameBook? Book { get; init; }

    /// <summary>Тексты таблиц правил (описания воинов) для карточки героя из файла.</summary>
    public Ui.Windows.RulesTexts? Rules { get; init; }

    /// <summary>Имена и описания предметов (<c>GItem</c>) для карточки предмета.</summary>
    public Ui.Windows.StackTexts? ItemTexts { get; init; }

    /// <summary>Каталог героев игрока (<see cref="Leaders.LeaderStore"/>); пусто — импорта героя нет.</summary>
    public string LeadersDir { get; init; } = "";

    /// <summary>Разметка экранов — для окон, которые вкладывают другие экраны (ячейки рас лобби «горячего стула»).</summary>
    public IReadOnlyList<UiScreen> Screens { get; init; } = [];

    /// <summary>Настройки показа (<see cref="Map.MapSettings"/>) — для окна настроек из меню; изменение уходит в
    /// <see cref="SettingsChanged"/> (хозяин применяет звук и пишет файл).</summary>
    public Map.MapSettings Settings { get; set; } = new();

    public Action<Map.MapSettings>? SettingsChanged { get; init; }

    /// <summary>Окно настроек Last Edict (<see cref="Settings.SettingsLogic"/>): выбор действует сразу (без записи) —
    /// <see cref="SettingsPreview"/>, «Применить» — <see cref="SettingsApplied"/> (хозяин применяет, пишет файл, меняет язык).</summary>
    public Action<Map.MapSettings>? SettingsPreview { get; init; }

    public Action<Map.MapSettings>? SettingsApplied { get; init; }

    /// <summary>Ролики следующего показа <c>DLG_CINEMATIC</c> (запуск игры — <see cref="Cinematic.Startup"/>); нет — ролик меню
    /// (<see cref="Cinematic.Menu"/>, кнопка «Вступление»). Показ забирает их.</summary>
    public IReadOnlyList<CinematicClip>? Cinematic { get; set; }

    /// <summary>Строка по id; нет строки — сам id, чтобы пропуск был виден, а не молча пуст.</summary>
    public string Text(string id) => Strings.TryGetValue(id, out var s) ? s : id;
}

/// <summary>Логика одного экрана: наполняет его при открытии, отвечает на события элементов и решает, разрешён ли
/// переход по графу от элемента («Продолжить» без выбранного сценария игру не начинает). Решения — здесь, в обычном
/// C#; сцена только передаёт события и рисует <see cref="UiScreenState"/>.</summary>
public interface IScreenLogic
{
    /// <summary>Экран, который рисуется вместо разметки (по умолчанию — она сама): свои элементы окна, порядок рисования.</summary>
    UiScreen Shape(UiScreen screen) => screen;

    void Open(UiScreenState ui);

    void Handle(UiEvent e, UiScreenState ui);

    bool Allows(string element, UiScreenState ui) => true;

    /// <summary>Строка окна сообщения (<c>DLG_MESSAGE_BOX</c>), которым экран отвечает на запрещённый <see cref="Allows"/> переход;
    /// <c>null</c> — переход просто не случается.</summary>
    string? Refusal(string element, UiScreenState ui) => null;

    /// <summary>Экран уходит по любому щелчку или клавише мимо элементов — переходом графа без элемента (титры). Прочие переходы без
    /// элемента в графе — решения игры (окна по ходу партии, брифинг после выбора лорда), вводом они не вызываются.</summary>
    bool AnyInputLeaves => false;
}

/// <summary>Логика экранов меню по <c>Id</c> экрана. Экран без логики показывается как есть, по разметке.</summary>
public static class MenuLogic
{
    /// <summary>Ролики показа <c>DLG_CINEMATIC</c>: заданные при запуске (<see cref="MenuContext.Cinematic"/>, забираются), иначе ролик меню.</summary>
    private static IReadOnlyList<CinematicClip> TakeCinematic(MenuContext context)
    {
        var clips = context.Cinematic ?? Cinematic.Menu;
        context.Cinematic = null;
        return clips;
    }

    public static IScreenLogic? For(string screenId, MenuContext context) => screenId switch
    {
        "DLG_CHOOSE_SKIRMISH" => new ChooseSkirmishLogic(context),
        "DLG_CHOOSE_RACE" => new ChooseRaceLogic(context),
        "DLG_CHOOSE_LORD" => new ChooseLordLogic(context),
        "DLG_SCENARIO_BRIEFING" => BriefingLogic.Create(context),
        // «Мультиплеер» погашен: «горячий стул» вырезан (Р33), люди одной партии — только по сети (Э7).
        "DLG_MAIN_MENU" => new MainMenuLogic(),
        "DLG_LOAD" when context.SavesDir.Length > 0 => new Play.LoadGameLogic(context.SavesDir, path => context.Setup.LoadPath = path,
                                                                              singlePlayer: true, Play.SaveKind.Quest),
        "DLG_LOAD_TOURNEMENT" when context.SavesDir.Length > 0 =>
            new Play.LoadGameLogic(context.SavesDir, path => context.Setup.LoadPath = path, singlePlayer: true, Play.SaveKind.Saga),
        "DLG_LOAD_CUSTOM_TOURNEMENT" when context.SavesDir.Length > 0 =>
            new Play.LoadGameLogic(context.SavesDir, path => context.Setup.LoadPath = path, singlePlayer: true, Play.SaveKind.Custom),
        "DLG_SINGLE_PLAYER" => new MenuModeLogic(() =>
        {
            context.Setup.Saga = false;
            context.Setup.Campaign = null;
            context.Setup.Carry = null;
            context.Setup.RaceLimit = null;
            // Места лобби одиночной игре не нужны (иначе NewGame строит людей по ним).
            context.Setup.Seats = [];
            context.Setup.StartGold = 0;
            context.Setup.StartMana = 0;
        }, element => context.Setup.Saga = element == "BTN_NEW_TOURNEMENT"),
        "DLG_NEW_CUSTOM_TOURNEMENT" => new CustomCampaignLogic(context),
        // Настройки из главного меню: партии нет — кнопки партии спрятаны (MapOptionsLogic).
        "DLG_OPTIONS" => new Map.MapOptionsLogic(context.Settings, settings =>
        {
            context.Settings = settings;
            context.SettingsChanged?.Invoke(settings);
        }, context.Text, inGame: false),
        // Окно настроек Last Edict (Э5) — на месте DLG_OPTIONS в графе (data/ui/flow.manual.json).
        Settings.SettingsLogic.Screen => new Settings.SettingsLogic(context.Settings, inGame: false, context.SettingsPreview, settings =>
        {
            context.Settings = settings;
            context.SettingsApplied?.Invoke(settings);
        }),
        "DLG_CREDITS" => new CreditsLogic(),
        CinematicLogic.Screen => new CinematicLogic(TakeCinematic(context)),
        _ => null,
    };
}

/// <summary>Титры <c>DLG_CREDITS</c> — пустая страница: фон экрана без ролика <c>CREDITS</c> в свитке и без надписей (решение пользователя
/// 28.09; текст переводчиков не переносится). Уход — любым щелчком или клавишей по переходу графа без элемента.</summary>
internal sealed class CreditsLogic : IScreenLogic
{
    public void Open(UiScreenState ui)
    {
        foreach (var element in ui.Screen.Elements) ui.SetHidden(element.Id, true);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public bool AnyInputLeaves => true;
}

/// <summary>Экран-развилка меню («горячий стул» или одиночная игра): при открытии отмечает режим в выборе игрока; нажатая кнопка —
/// <paramref name="clicked"/> (одиночная игра: «Новая сага» или квест).</summary>
/// <summary>Главное меню: «Мультиплеер» (<c>BTN_MULTI</c> → выбор протокола → «горячий стул» или сеть) погашен — «горячий стул» вырезан
/// (Р33), сетевая игра — Э7.</summary>
public sealed class MainMenuLogic : IScreenLogic
{
    public const string Multiplayer = "BTN_MULTI";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ui.Screen.Elements.Any(e => e.Id == Multiplayer)) ui.SetEnabled(Multiplayer, false);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public bool Allows(string element, UiScreenState ui) => element != Multiplayer;
}

internal sealed class MenuModeLogic(Action mark, Action<string>? clicked = null) : IScreenLogic
{
    public void Open(UiScreenState ui) => mark();

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is UiClicked c) clicked?.Invoke(c.Element);
    }
}

/// <summary>Окно сообщения (<c>DLG_MESSAGE_BOX</c>), которое открыл переход по графу: текст — строка перехода
/// (<see cref="UiFlowEdge.Message"/>); кнопки решает граф. <paramref name="okOnly"/> — сообщение карты (повтор миссии, конец партии,
/// покорённая раса): у движка <c>yesNo = 0</c> (корпус <c>X005TA0079</c>, <c>X005TA0828</c>, <c>X005TA0761</c>, <c>X005TA0775</c> —
/// <c>yes_no=false</c>), одна «ОК».</summary>
public sealed class MessageLogic(string text, bool okOnly = false) : IScreenLogic
{
    public void Open(UiScreenState ui)
    {
        if (ui.Screen.Elements.Any(e => e.Id == "TXT_INFO")) ui.SetText("TXT_INFO", text);
        if (okOnly && ui.Screen.Elements.Any(e => e.Id == "BTN_OK")) LastEdict.Data.Ui.Windows.MessageBoxLogic.Buttons(ui, question: false);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }
}
