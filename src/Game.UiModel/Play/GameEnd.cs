using System.Text.RegularExpressions;
using LastEdict.Data.Game;
using LastEdict.Data.Menu;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>Конец партии для игрока (<c>docs/scenario-events.md</c> §4): окно сообщения клиента (<c>0x489b64</c>), затем экран
/// эпилога <c>DLG_END_WIN_LOSE_</c> (<see cref="EndScenarioLogic"/>).</summary>
public static partial class GameEnd
{
    /// <summary>Победа — «Победа! Миссия успешно пройдена.»</summary>
    public const string WinText = "X005TA0611";

    /// <summary>Победителя нет — «Поражение! Вы проиграли эту миссию.»</summary>
    public const string NoWinnerText = "X005TA0761";

    /// <summary>Победил другой — «Поражение! %PLAYER%, Правитель %RACE%, победил!».</summary>
    public const string OtherWonText = "X150TA0509";

    /// <summary>Шаблон текста эпилога: <c>%TEXT%</c> — <c>DEBUNKW</c> или <c>DEBUNKL</c> (<c>0x4bc6f1</c>).</summary>
    public const string EpilogueTemplate = "X005TA0932";

    /// <summary>Текст окна сообщения конца партии для <paramref name="viewer"/>; партия идёт — null. Победитель — я: 0611;
    /// победителя нет: 0761; победил другой: 1509 с именем игрока и названием его расы.</summary>
    public static string? Message(GameInfo info, string viewer, MapState map, GameCatalog catalog, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(text);
        if (info.Winner.Length == 0) return null;
        if (info.Winner.Equals(viewer, StringComparison.OrdinalIgnoreCase)) return text(WinText);
        if (info.Winner == GameInfo.NoWinner || !map.Players.TryGetValue(info.Winner, out var winner)) return text(NoWinnerText);
        string race = catalog.Races.FirstOrDefault(r => r.Id.Equals(winner.RaceId, StringComparison.OrdinalIgnoreCase)) is { } r
            ? text(r.NameText) : "";
        return text(OtherWonText).Replace("%PLAYER%", winner.Name.Trim()).Replace("%RACE%", race);
    }

    /// <summary>Текст эпилога по шаблону: <c>%TEXT%</c> — текст сценария; в нём ведущий <c>%NN%</c> — NN пробелов, остальные
    /// <c>%3%…%7%</c> — столько же переводов строки (<c>0x4bc6f1</c>, ранг 1).</summary>
    public static string Epilogue(string template, string text)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(text);
        var lead = Lead().Match(text);
        if (lead.Success) text = new string(' ', int.Parse(lead.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)) + text[lead.Length..];
        text = Breaks().Replace(text, m => new string('\n', m.Groups[1].Value[0] - '0'));
        return template.Replace("%TEXT%", text);
    }

    /// <summary>Звук эпилога, общий (<c>[0x7ae460]</c>): победа без своего <c>WAV_WIN</c> и поражение.</summary>
    public const string DefaultEpilogueSound = "GENBRIEF";

    /// <summary>Звук экрана эпилога (<c>0x4bcba0</c>, ранг 1): нет записи эпилога (кампании сценария нет в <c>CCamp.dbf</c>,
    /// <c>0x669464</c>, или главы нет в <c>CScen.dbf</c>, <c>0x668d69</c>) — без звука (null); победа — <c>WAV_WIN</c> записи (поле
    /// <c>+0x1F</c>), пусто или поражение — <see cref="DefaultEpilogueSound"/>.</summary>
    public static string? EpilogueSound(bool won, ScenarioInfo? scenario)
    {
        if (scenario is not { HasEnding: true }) return null;
        return won && scenario.WinSound.Length > 0 ? scenario.WinSound : DefaultEpilogueSound;
    }

    [GeneratedRegex(@"^%(\d+)%")]
    private static partial Regex Lead();

    [GeneratedRegex(@"%([3-7])%")]
    private static partial Regex Breaks();
}

/// <summary>Эпилог сценария (<c>DLG_END_WIN_LOSE_</c>, <c>CEndScenarioInterf</c> <c>0x4bc283</c>): картинка <c>IMG_END_LOSE</c>
/// скрыта, в её прямоугольнике — ролик победы или поражения; текст — <see cref="GameEnd.Epilogue"/>. Звук эпилога
/// (<see cref="GameEnd.EpilogueSound"/>): при открытии музыка затухает 750 мс (<c>0x4bc4c6</c>: <c>0x5af952(0x2ee)</c> → <c>0x5afbe4</c>), затем
/// звук (<c>0x4bca73</c> → <c>0x4bcba0</c>, громкостью музыки <c>0x4bcac7</c>); «Повтор» (<c>BTN_REPLAY</c>, <c>0x4bcdcc</c>) играет его снова.
/// Кнопка выключена при открытии и пока звук играет (<c>0x4bc49f</c>, включает конец звука <c>0x4bcd11</c> → <c>0x4bcd95</c>), без записи
/// эпилога — навсегда.</summary>
public sealed class EndScenarioLogic(bool won, ScenarioInfo? scenario, Func<string, string> text) : IWindowLogic, ITuneListener
{
    private const string Text = "TXT_TEXT_SCROLL", Picture = "IMG_END_LOSE", Replay = "BTN_REPLAY";

    /// <summary>Затухание музыки перед звуком эпилога, мс (<c>0x2ee</c>).</summary>
    public const int MusicFadeMs = 750;

    private readonly UiRequests _requests = new();

    /// <summary>Звук эпилога; нет — null.</summary>
    public string? Sound { get; } = GameEnd.EpilogueSound(won, scenario);

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"эпилог звук={Sound ?? "-"}";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        // Нет записи эпилога (кампания сценария не в таблице кампаний) — текста нет, даже если он есть в файле.
        string epilogue = scenario is not { HasEnding: true } ? "" : won ? scenario.WinText : scenario.LoseText;
        ui.SetText(Text, epilogue.Length == 0 ? "" : GameEnd.Epilogue(text(GameEnd.EpilogueTemplate), epilogue));
        string video = won ? scenario?.WinVideo ?? "" : scenario?.LoseVideo ?? "";
        // Картинка элемента в разметке пуста — рисовать нечего, в прямоугольнике идёт ролик.
        if (video.Length > 0) ui.SetVideo(Picture, video);
        ui.SetEnabled(Replay, false);
        if (Sound is not null) _requests.Add(new UiTune(Sound, MusicFadeMs));
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        switch (e)
        {
            case UiClicked { Element: "BTN_SCROLL_UP" }: ui.SetScroll(Text, ui.ScrollOf(Text) - 1); break;
            case UiClicked { Element: "BTN_SCROLL_DOWN" }: ui.SetScroll(Text, ui.ScrollOf(Text) + 1); break;
            case UiClicked { Element: Replay } when Sound is not null && ui.IsEnabled(Replay):
                ui.SetEnabled(Replay, false);
                _requests.Add(new UiTune(Sound, 0));
                break;
        }
    }

    /// <summary>Звук доиграл — «Повтор» снова доступен.</summary>
    public void TuneEnded(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (Sound is not null) ui.SetEnabled(Replay, true);
    }
}

/// <summary>Попап события (<c>DLG_EVENT_POPUP</c>, <c>CEventPopup</c> <c>0x4bd0e8</c>): текст <c>POPUP_TXT</c> как есть в
/// <c>TXT_RIGHTSIDE</c>; картинка <c>IMAGE</c> из архива <c>Events</c> — на одну сторону: <c>LEFT_SIDE</c> 1 — слева, иначе
/// справа; пустая или нет в архиве — место пустое (у <c>IMG_LEFTSIDE</c>/<c>IMG_RIGHTSIDE</c> разметки картинки нет, запасного архива
/// нет: <c>0x4bd5d1</c>). <c>IMAGE2</c> клиент не читает. Ранг 1.
/// <para>При лидере-триггерере (<c>BOOLVALUE</c>, <c>0x4bd3ae…0x4bd50b</c>, ранг 1): перед текстом — имя и перевод строки (у героя —
/// собственное, даже пустое; у прочих — имя типа, <paramref name="unitName"/>), вместо <c>IMAGE</c> — кадр <c>Events</c> его типа на
/// ту же сторону.</para></summary>
public sealed class EventPopupLogic(EventMessage message, Func<string, string>? unitName = null) : IScreenLogic
{
    /// <summary>Имя кадра архива графики для экранов: <c>gfx:Архив/Кадр</c>.</summary>
    public static string GfxFrame(string archive, string name) => $"gfx:{archive}/{name}";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        string side = message.Popup.LeftSide ? "IMG_LEFTSIDE" : "IMG_RIGHTSIDE";
        // По центру места без масштаба (setImage с флагами 9), справа — отражённая (загрузка 0x5ae260 с «не слева», 0x4bd55b;
        // разбор сессии Б 28.09, docs/ui-windows.md §13).
        void Picture(string frame) =>
            ui.SetImage(side, message.Popup.LeftSide ? frame : UiFrameRef.Mirrored(frame), UiImageAlign.Center);
        if (message.LeaderType.Length > 0)
        {
            string name = message.OwnName || message.LeaderName.Length > 0 ? message.LeaderName : unitName?.Invoke(message.LeaderType) ?? "";
            ui.SetText("TXT_RIGHTSIDE", name.Length > 0 || message.OwnName ? $"{name}\n{message.Popup.Text}" : message.Popup.Text);
            Picture(GfxFrame("Events", Ui.ArtNames.Of(message.LeaderType)));
            return;
        }
        ui.SetText("TXT_RIGHTSIDE", message.Popup.Text);
        if (message.Popup.Image.Length > 0) Picture(GfxFrame("Events", Ui.ArtNames.Of(message.Popup.Image)));
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }
}
