using System.Globalization;
using D2.Data.Menu;
using D2.Data.Ui;

namespace D2.Data.Play;

/// <summary>Строка сохранения в списке и его описание — общие для экранов записи и загрузки.</summary>
internal static class SaveText
{
    public static string Row(SaveSlot s) => s.Title;

    public static string Describe(SaveSlot s) =>
        $"{s.Title}\n{s.Scenario}\nДень {s.Day.ToString(CultureInfo.InvariantCulture)}\n" +
        s.SavedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>Запись партии (<c>DLG_SAVE_GAME</c>, из настроек на карте): список сохранений каталога, поле имени,
/// «Сохранить» пишет партию под этим именем (то же имя — перезапись). Выбор строки подставляет её имя в поле.
/// Удаление слота (<c>BTN_DELETE_SLOT</c>, в игре — с подтверждением) пока не сделано.</summary>
public sealed class SaveGameLogic(string dir, Func<GameState> current, string defaultTitle) : IScreenLogic
{
    private const string List = "TLBOX_SAVE_LIST", Edit = "EDIT_SAVE_EDIT", Description = "TXT_DESCRIPTION";
    private IReadOnlyList<SaveSlot> _slots = [];

    /// <summary>Последняя запись — для проверок.</summary>
    public string? Saved { get; private set; }

    public void Open(UiScreenState ui)
    {
        Refresh(ui);
        ui.SetText(Edit, defaultTitle);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        switch (e)
        {
            case UiListSelected { Element: List } selected when selected.Index < _slots.Count:
                ui.SetText(Edit, _slots[selected.Index].Title);
                ui.SetText(Description, SaveText.Describe(_slots[selected.Index]));
                break;
            case UiClicked { Element: "BTN_SAVE" }:
                string title = (ui.TextOf(Edit) ?? "").Trim();
                if (title.Length == 0) title = defaultTitle;
                string path = SaveSlots.PathFor(dir, title);
                GameSave.Write(path, current(), title);
                Saved = path;
                Refresh(ui);
                break;
        }
    }

    public bool Allows(string element, UiScreenState ui) => element != "BTN_DELETE_SLOT";

    private void Refresh(UiScreenState ui)
    {
        _slots = SaveSlots.List(dir);
        ui.SetItems(List, [.. _slots.Select(SaveText.Row)]);
        ui.SetText(Description, _slots.Count > 0 ? SaveText.Describe(_slots[0]) : "");
    }
}

/// <summary>Загрузка партии: на карте — <c>DLG_LOAD_GAME</c> (из настроек), в меню — <c>DLG_LOAD</c> (одиночная
/// игра). Список сохранений, у выбранного — описание; «Загрузить» или двойной щелчок отдают файл хозяину
/// (<paramref name="load"/>): он закрывает текущую партию и открывает сохранённую. Переход графа от кнопки
/// (в игре — окно подтверждения) не нужен: загрузку делает хозяин.
/// <para>Меню (<paramref name="hotseat"/> — открыто из «горячего стула», <c>CMenuLoadSkirmishHotseat</c>; иначе одиночная игра)
/// проверяет людей партии (<c>menuload:ButtonCallback 0x4df7b0</c>): «горячему стулу» нужно не меньше двух —
/// <c>X006TA0033</c>; одиночной игре партия с несколькими людьми — <c>X006TA0037</c> (<c>0x4dfa4c</c>). В игре
/// (<paramref name="hotseat"/> null) проверок нет.</para></summary>
/// <para>Меню загрузки саги (<c>DLG_LOAD_TOURNEMENT</c>) и своей кампании (<c>DLG_LOAD_CUSTOM_TOURNEMENT</c>) показывают только свои
/// сохранения, одиночной игры — только квесты (<paramref name="kind"/>; null — все, в игре).</para>
public sealed class LoadGameLogic(string dir, Action<string> load, bool? hotseat = null, SaveKind? kind = null) : IScreenLogic
{
    private IReadOnlyList<SaveSlot> _slots = [];
    private string _list = "TLBOX_GAME_LIST", _description = "TXT_DESCRIPTION";

    public void Open(UiScreenState ui)
    {
        if (ui.Screen.Elements.Any(e => e.Id == "TLBOX_GAME_SLOT")) (_list, _description) = ("TLBOX_GAME_SLOT", "TXT_DESC");
        _slots = [.. SaveSlots.List(dir).Where(s => kind is null || s.Kind == kind)];
        ui.SetItems(_list, [.. _slots.Select(SaveText.Row)]);
        if (_slots.Count == 0) return;
        ui.Select(_list, 0);
        ui.SetText(_description, SaveText.Describe(_slots[0]));
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        switch (e)
        {
            case UiListSelected selected when selected.Element == _list && selected.Index < _slots.Count:
                ui.SetText(_description, SaveText.Describe(_slots[selected.Index]));
                break;
            case UiListConfirmed confirmed when confirmed.Element == _list && confirmed.Index < _slots.Count
                                                 && RefusalOf(_slots[confirmed.Index]) is null:
                load(_slots[confirmed.Index].Path);
                break;
            case UiClicked { Element: "BTN_LOAD" } when Selected(ui) is { } slot && RefusalOf(slot) is null:
                load(slot.Path);
                break;
        }
    }

    public bool Allows(string element, UiScreenState ui) => element != "BTN_LOAD" && Refusal(element, ui) is null;

    /// <summary>Отказ загрузки выбранной партии — окно сообщения поверх списка.</summary>
    public string? Refusal(string element, UiScreenState ui) =>
        element is "BTN_LOAD" || element == _list ? Selected(ui) is { } slot ? RefusalOf(slot) : null : null;

    private string? RefusalOf(SaveSlot slot) => hotseat switch
    {
        true when slot.Humans < 2 => "X006TA0033",   // «В сохраненной игре должно быть как минимум два игрока.»
        false when slot.Humans > 1 => "X006TA0037",  // «В этой миссии участвовало несколько игроков. …»
        _ => null,
    };

    private SaveSlot? Selected(UiScreenState ui)
    {
        int i = ui.ListSelected(_list);
        return i >= 0 && i < _slots.Count ? _slots[i] : null;
    }
}
