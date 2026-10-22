using D2.Data.Ui;

namespace D2.Data.Menu;

/// <summary>Выбор сценария (<c>DLG_CHOOSE_SKIRMISH</c>): список сценариев каталога, при открытии выбрана первая
/// строка (со слов пользователя); у выбранного — описание, размер карты и значки его рас. Значок расы — <c>GOD&lt;код&gt;</c>
/// (<c>GODHU</c>, <c>GODUN</c>, <c>GODHE</c>, <c>GODDW</c>, <c>GODEL</c> из <c>Interf.ff</c>, 45×73 — ровно место <c>IMG_RACE_n</c>):
/// так у движка — цикл <c>0x4e8f44</c> ставит в <c>IMG_RACE_%d</c> (<c>0x7a1c54</c>) картинку <c>0x4f1095</c>, а та по расе выбирает
/// одну из этих пяти строк (<c>0x6ddd68</c>…<c>0x6ddd88</c>); у прочих рас картинки нет. Прежний вариант (картинка «горячего стула»
/// 174×202, вписанная по ширине) не заполнял клетку (пользователь, 11.10). Порядок сценариев — по имени файла: первым в списке игры
/// стоит <c>'Greenskins'</c>, как и здесь в копии <c>game-dev</c>.</summary>
internal sealed class ChooseSkirmishLogic(MenuContext context) : IScreenLogic
{
    private const string List = "TLBOX_GAME_SLOT";
    private const string MapSizeText = "X005TA0478"; // «Размер карты:\n %X% х %Y%»
    private static readonly string[] RaceImages = ["IMG_RACE_1", "IMG_RACE_2", "IMG_RACE_3", "IMG_RACE_4"];

    public void Open(UiScreenState ui)
    {
        ui.SetItems(List, [.. context.Catalog.Scenarios.Select(s => s.Name)]);
        if (context.Catalog.Scenarios.Count == 0) return;
        ui.Select(List, 0);
        Show(ui, 0);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is UiListSelected { Element: List } selected) Show(ui, selected.Index);
    }

    public bool Allows(string element, UiScreenState ui) => element != "BTN_LOAD" || context.Setup.Scenario is not null;

    /// <summary>Расы, у которых есть значок (<c>0x4f1095</c>): Империя, Легионы, Нежить, Кланы, Эльфы.</summary>
    private static readonly HashSet<string> IconRaces = new(StringComparer.Ordinal) { "HU", "HE", "UN", "DW", "EL" };

    /// <summary>Значок расы в клетке сценария: <c>GOD&lt;код&gt;</c>; у расы без значка — пусто.</summary>
    internal static string? Icon(string code) => IconRaces.Contains(code) ? $"GOD{code}" : null;

    private void Show(UiScreenState ui, int index)
    {
        var scenario = context.Catalog.Scenarios[index];
        context.Setup.Scenario = scenario;
        ui.SetText("TXT_DESCRIPTION", scenario.Description);
        string size = scenario.MapSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ui.SetText("TXT_MAP_SIZE", context.Text(MapSizeText).Replace("%X%", size).Replace("%Y%", size));
        for (int i = 0; i < RaceImages.Length; i++)
        {
            string? frame = i < scenario.Races.Count ? Icon(context.Catalog.Race(scenario.Races[i]).Code) : null;
            ui.SetImage(RaceImages[i], frame);
        }
    }
}
