using LastEdict.Data.Menu;
using LastEdict.Data.Play;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Журнал (<c>DLG_QUEST_LOG</c>, <c>CQuestLogInterf</c> <c>0x4c2867</c>; <c>BTN_QUESTLOG</c>, F3): записи квест-лога текущего игрока по порядку
/// (<see cref="EventProgress.Log"/>) — строка <c>X005TA0912</c> «%TYPE% за ход %TURN%» (<c>%TYPE%</c> — <c>X005TA0913</c>), пусто —
/// <c>X005TA0916</c>; при открытии выбрана последняя. Выбор (<c>0x4c2be4</c>): текст попапа события (эффект по номеру) в
/// <c>TXT_TEXT</c>, картинка <c>IMAGE</c> — слева при <c>LEFTSIDE</c>, иначе справа и отражённой, по центру места (как у попапа,
/// <c>0x4bd56b</c>, флаги 9 <c>setImage</c>); звук и музыка не играют. Своей клавиатуры нет (<c>0x6d96a4</c>, общий <c>0x5c9bbb</c>): «Закрыть» — Enter/Esc,
/// стрелки и страницы — горячие клавиши кнопок разметки.
/// </summary>
public sealed class QuestLogLogic(WindowContext context) : IWindowLogic
{
    public const string Screen = "DLG_QUEST_LOG";
    private const string List = "LBOX_LOGS";
    private readonly UiRequests _requests = new();

    public UiRequest? TakeRequest() => _requests.Take();

    /// <summary>Записи игрока по порядку.</summary>
    public IReadOnlyList<QuestLogEntry> Entries =>
        [.. context.Session.State.EventProgress.Log.Where(l => l.PlayerId.Equals(context.Viewer, StringComparison.OrdinalIgnoreCase))];

    public int Selected { get; private set; } = -1;

    public string Summary => $"журнал={Entries.Count} выбрано={Selected}";

    public bool Allows(string element, UiScreenState ui) => element != "BTN_CLOSE";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var entries = Entries;
        ui.SetItems(List, entries.Count == 0 ? [context.Text("X005TA0916")]
            : [.. entries.Select(e => UiTemplate.Fill(context.Text("X005TA0912"), ("TYPE", context.Text("X005TA0913")), ("TURN", e.Turn)))]);
        if (entries.Count == 0) return;
        ui.Select(List, entries.Count - 1);
        Show(ui, entries.Count - 1);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        switch (e)
        {
            case UiClicked { Element: "BTN_CLOSE" }:
                _requests.Add(new UiClose());
                break;
            case UiListSelected { Element: List } s:
                Show(ui, s.Index);
                break;
        }
    }

    private void Show(UiScreenState ui, int index)
    {
        var entries = Entries;
        if (index < 0 || index >= entries.Count) return;
        Selected = index;
        var entry = entries[index];
        ui.SetImage("IMG_LEFTSIDE", null);
        ui.SetImage("IMG_RIGHTSIDE", null);
        var popup = context.Session.State.Events.Events.FirstOrDefault(ev => ev.Id.Equals(entry.EventId, StringComparison.OrdinalIgnoreCase))
            is { } ev && entry.EffectNum >= 0 && entry.EffectNum < ev.Effects.Count ? ev.Effects[entry.EffectNum] as PopupEffect : null;
        ui.SetText("TXT_TEXT", popup?.Text ?? "");
        if (popup is not { Image.Length: > 0 }) return;
        // Справа — отражённой: загрузка картинки 0x5ae260 получает «не слева» (0x4bd55b; снимки «Задания» 28.09).
        string frame = EventPopupLogic.GfxFrame("Events", popup.Image);
        if (popup.LeftSide) ui.SetImage("IMG_LEFTSIDE", frame, UiImageAlign.Center);
        else ui.SetImage("IMG_RIGHTSIDE", UiFrameRef.Mirrored(frame), UiImageAlign.Center);
    }
}
