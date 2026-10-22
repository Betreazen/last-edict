using D2.Data.Leaders;
using D2.Data.Ui;
using D2.Data.Ui.Windows;

namespace D2.Data.Menu;

/// <summary>Просьба выбрать героя для импорта: раса ячейки (герой другой расы не берётся), рекомендованный уровень сценария (только
/// показ), текущий выбор (файл) и ответ — выбранный герой или null («Нет»).</summary>
public sealed record LeaderPick(int Race, int SuggestedLevel, string CurrentFile, Action<StoredLeader?> Chosen);

/// <summary>
/// Выбор героя (<c>DLG_IMPORT_LEADER</c>, <c>CImportLeaderInterf</c> <c>0x4c6b69</c>; разбор — <c>docs/day-cycle.md</c> §12). Список
/// <c>LBOX_LEADERS</c> (ячейки 189×93): первая — «Нет» (<c>X005TA0734</c>), дальше герои каталога (<see cref="LeaderStore.List"/>) — лицо
/// типа слева по центру высоты и «имя, уровень» (<c>X005TA0735</c>). При открытии выбран текущий герой, иначе «Нет». «ОК» — на «Нет»
/// сразу, на герое своей расы — вопрос <c>X005TA0736</c>; герой другой расы не берётся (кнопка выключена, <c>0x4c7b8c</c>).
/// «Просмотр» — <c>DLG_VIEW_EXPLEAD</c>, «Удалить» — вопрос <c>X005TA0884</c> и файл из каталога. «Отмена» — выбор не меняется
/// (у движка «Отмена» стирает подпись в лобби, но файл остаётся выбранным — похоже на ошибку, не переносится).
/// Лицо героя другой расы затемнено вдвое (<c>0x4c78cd</c>: режим картинки 0xd — чёрная заливка с альфой 0x80).
/// </summary>
internal sealed class ImportLeaderLogic(MenuContext context, LeaderPick pick) : IWindowLogic
{
    private const string List = "LBOX_LEADERS";
    private const int CellHeight = 93, FaceHeight = 85, TextX = 88, CellWidth = 189;
    private readonly UiRequests _requests = new();
    private IReadOnlyList<StoredLeader> _leaders = [];
    private UiScreenState? _ui;

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _ui = ui;
        ui.SetText("TXT_SUGG_LEVEL", UiTemplate.Fill(context.Text("X005TA0733"), ("LEVEL", pick.SuggestedLevel)));
        Load(ui);
        int current = _leaders.ToList().FindIndex(l => l.FileName.Equals(pick.CurrentFile, StringComparison.OrdinalIgnoreCase));
        ui.Select(List, current + 1);
        Buttons(ui);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        switch (e)
        {
            case UiListSelected { Element: List }:
                Buttons(ui);
                break;
            case UiClicked { Element: "BTN_OK" }:   // «ОК» ещё и подтверждает список — второй раз не спрашивать
                Choose(ui);
                break;
            case UiClicked { Element: "BTN_CANCEL" }:
                _requests.Add(new UiClose());
                break;
            case UiClicked { Element: "BTN_VIEW_LEADER" } when Selected(ui) is { } leader:
                _requests.Add(new UiOpen(ViewLeaderLogic.Screen, new ViewLeaderLogic(context, leader.Leader)));
                break;
            case UiClicked { Element: "BTN_DELETE" } when Selected(ui) is { } leader:
                _requests.Add(new UiAsk(UiTemplate.Fill(context.Text("X005TA0884"), ("LDR", leader.FileName)), () =>
                {
                    LeaderStore.Delete(leader);
                    Load(ui);
                    ui.Select(List, 0);
                    Buttons(ui);
                }));
                break;
        }
    }

    public UiRequest? TakeRequest() => _requests.Take();

    /// <summary>Окно снова наверху (закрыт вопрос или просмотр) — выбор и кнопки прежние.</summary>
    public void Resume(UiScreenState ui) => Buttons(ui);

    private void Choose(UiScreenState ui)
    {
        if (Selected(ui) is not { } leader)
        {
            pick.Chosen(null);
            _requests.Add(new UiClose());
            return;
        }
        if (leader.Leader.Race != pick.Race) return;
        _requests.Add(new UiAsk(UiTemplate.Fill(context.Text("X005TA0736"), ("NAME", leader.Leader.Name)), () =>
        {
            pick.Chosen(leader);
            _requests.Add(new UiClose());
        }));
    }

    private void Load(UiScreenState ui)
    {
        _leaders = LeaderStore.List(context.LeadersDir);
        var text = new UiRect(TextX, 0, CellWidth - TextX, CellHeight);
        ui.SetCards(List,
        [
            new UiCard(null, new UiPoint(0, 0), context.Text("X005TA0734"), text),
            .. _leaders.Select(l => new UiCard($"Faces:{l.Leader.UnitType.ToUpperInvariant()}FACE", new UiPoint(0, (CellHeight - FaceHeight) / 2),
                UiTemplate.Fill(context.Text("X005TA0735"), ("NAME", l.Leader.Name), ("LEVEL", l.Leader.Level)), text,
                Dimmed: l.Leader.Race != pick.Race)),
        ]);
    }

    /// <summary>Выбранный герой; «Нет» — null.</summary>
    private StoredLeader? Selected(UiScreenState ui)
    {
        int i = ui.ListSelected(List) - 1;
        return i >= 0 && i < _leaders.Count ? _leaders[i] : null;
    }

    /// <summary>Кнопки по выбору (<c>0x4c7b8c</c>): на «Нет» — «ОК»; на герое — просмотр и удаление, «ОК» — только своей расе.</summary>
    private void Buttons(UiScreenState ui)
    {
        var leader = Selected(ui);
        ui.SetEnabled("BTN_OK", leader is null || leader.Leader.Race == pick.Race);
        ui.SetEnabled("BTN_VIEW_LEADER", leader is not null);
        ui.SetEnabled("BTN_DELETE", leader is not null);
    }
}

/// <summary>
/// Просмотр героя из файла (<c>DLG_VIEW_EXPLEAD</c>, <c>CViewExportedLeaderInterf</c> <c>0x4ca85d</c>): имя; «Скорость, лидерство,
/// обзор» (<c>X005TA0400</c>: оба хода — ход героя из файла); «Навыки героя:» и строки навыков (<see cref="HeroSkills"/>); лицо — в
/// рамке (<c>IMG_SLOTLEADER</c>, <c>{тип}FACE</c>) и большое (<c>IMG_LEADER_FACE</c>, <c>{тип}FACEB</c>, ранг 2); предметы — в слотах
/// снаряжения. «Закрыть» — назад. Правая кнопка — карточки героя, навыка и предмета (<see cref="RightPress"/>).
/// </summary>
internal sealed class ViewLeaderLogic(MenuContext context, ExportedLeader leader) : IWindowLogic
{
    public const string Screen = "DLG_VIEW_EXPLEAD";

    private static readonly string[] Slots =
        ["IMG_BANNER", "IMG_TOME", "IMG_BATTLE1", "IMG_BATTLE2", "IMG_ARTIFACT1", "IMG_ARTIFACT2", "IMG_BOOTS"];

    private readonly UiRequests _requests = new();

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetText("TXT_NAME", leader.Name);
        ui.SetText("TXT_STACK_STATS", UiTemplate.Fill(context.Text("X005TA0400"), ("MOVE1", leader.Move), ("MOVE2", leader.Move),
            ("LEADERSHIP", leader.Leadership), ("SCOUTRNG", leader.Scout)));
        ui.SetText("TXT_LEADER_ABILITY", context.Text("X005TA0185"));
        ui.SetCards(Abilities, [.. LeaderAbilities.Rows(leader).Select(r => new UiCard(r.Icon.Length > 0 ? $"Icons:{r.Icon}" : null,
            new UiPoint(0, 0), UiTemplate.Fill(context.Text("X005TA0940"), ("ABIL", context.Text(r.Name))), new UiRect(36, 0, 158 - 36, 36)))]);
        string type = leader.UnitType.ToUpperInvariant();
        ui.SetImage("IMG_SLOTLEADER", $"Faces:{type}FACE");
        ui.SetImage("IMG_LEADER_FACE", $"Faces:{type}FACEB");
        foreach (var item in leader.Equipment.Where(i => i.Slot >= 0 && i.Slot < Slots.Length && i.ItemType.Length > 0))
            ui.SetImage(Slots[item.Slot], $"IconItem:{item.ItemType.ToUpperInvariant()}");
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is UiClicked { Element: "BTN_CLOSE" }) _requests.Add(new UiClose());
    }

    private const string Abilities = "LBOX_ITEM_ICON";
    private const int AbilityRow = 36;

    /// <summary>Правая кнопка (<c>0x4cab65</c>), пока держится: лицо — карточка героя из файла (<see cref="LeaderFileCardLogic"/>), строка
    /// навыка — карточка навыка (<see cref="AbilityCardLogic"/>), слот снаряжения — карточка предмета без цены.</summary>
    public void RightPress(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        bool Inside(string id) => ui.Screen.Elements.FirstOrDefault(e => e.Id == id)?.Rect is { } r
                                  && x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;
        if (Inside("IMG_LEADER_FACE") || Inside("IMG_SLOTLEADER"))
        {
            _requests.Add(new UiOpen(D2.Data.Ui.Windows.UnitCardLogic.Screen, new LeaderFileCardLogic(context, leader)));
            return;
        }
        if (Inside(Abilities) && ui.Screen.Elements.First(e => e.Id == Abilities).Rect is var list)
        {
            int row = ui.ListTop(Abilities) + ((y - list.Y) / AbilityRow);
            var rows = LeaderAbilities.Rows(leader);
            if (row >= 0 && row < rows.Count) _requests.Add(new UiOpen(AbilityCardLogic.Screen, new AbilityCardLogic(context.Text, rows[row].Name, rows[row].Desc)));
            return;
        }
        if (context.Book is not { } book) return;
        for (int slot = 0; slot < Slots.Length; slot++)
        {
            var r = ui.Screen.Elements.FirstOrDefault(e => e.Id == Slots[slot])?.Rect;
            if (r is not { } rect || x < rect.X || x >= rect.Right || y < rect.Y || y >= rect.Bottom) continue;
            var worn = leader.Equipment.FirstOrDefault(i => i.Slot == slot);
            if (worn is null || !book.Items.TryGetValue(worn.ItemType.ToUpperInvariant(), out var item)) return;
            var texts = context.ItemTexts;
            // Цены и зарядов нет (CEncParamID с флагами 0: цена только при флаге 2, 0x578848).
            _requests.Add(new UiOpen(ItemCardLogic.Screen, new ItemCardLogic(context.Text, item,
                texts?.Items.GetValueOrDefault(item.Id, "") ?? "", texts?.ItemDescs?.GetValueOrDefault(item.Id, "") ?? "")));
            return;
        }
    }

    public UiRequest? TakeRequest() => _requests.Take();
}
