using D2.Data.Sound;
using D2.Data.Ui;
using D2.Data.Ui.Windows;

namespace D2.Data.Menu;

/// <summary>Выбор лорда (<c>DLG_CHOOSE_LORD</c>): фон расы, живой портрет (ролик <c>FACE_XX_N</c>, щелчок по нему
/// перебирает четыре лица), описание выбранного типа лорда, название сложности и имя. Вверху — «Выберите лорда»
/// (<c>X005TA0830</c>), в поле имени — системное имя (оба — со слов пользователя). Начальные тип лорда и сложность
/// задаёт <c>data/ui/defaults.json</c>, логика читает их из группы. «Импорт героя» (<c>BTN_IMPORT_LEADER</c>, <c>CMenuLord</c>
/// <c>0x4e5ad5</c>) — включена, если в каталоге героев кто-то есть (у одиночных сценариев кампания <c>C000CC0001</c> с <c>CANIMPORT</c>):
/// окно выбора героя расы лорда (<see cref="ImportLeaderLogic"/>), выбранный — в <see cref="NewGameSetup.ImportedLeader"/> и
/// <c>TXT_IMPORT_NAME</c>.</summary>
internal sealed class ChooseLordLogic(MenuContext context) : IWindowLogic
{
    private readonly UiRequests _requests = new();
    private string _importFile = "";
    private bool _changing;

    private const int Faces = 4;
    private const string Face = "BTN_FACE";
    private const string Title = "X005TA0830";

    /// <summary>Переключатель типа лорда → категория <c>Llord</c>.</summary>
    private static readonly Dictionary<string, int> LordOf = new()
    {
        ["TOG_LORD_WARRIOR"] = 1, ["TOG_LORD_MAGE"] = 0, ["TOG_LORD_DIPLOMAT"] = 2,
    };

    public void Open(UiScreenState ui)
    {
        var setup = context.Setup;
        var race = context.Catalog.Race(setup.Race);
        // Картинка расы — под рамкой BG1TRANS (41 % прозрачных точек; снимок 28.09 — рамка поверх пейзажа Империи).
        ui.SetUnderlay($"DLG_CHOOSE_LORD_{race.Art}_BG");
        ui.SetText("TXT_TITLE", context.Text(Title));
        setup.LordName ??= context.SystemName;
        ui.SetText("EDIT_LORD_NAME", setup.LordName);
        ShowFace(ui);
        ReadGroups(ui);
        bool canImport = context.LeadersDir.Length > 0 && Leaders.LeaderStore.List(context.LeadersDir).Count > 0;
        // Герой, выбранный раньше для другой расы (назад в меню, другая раса), — не этой партии.
        if (setup.ImportedLeader is { } old && (old.Race != setup.Race || !canImport)) setup.ImportedLeader = null;
        // Каталог пуст — кнопка видна, но выключена (0x4e5b6a: setEnabled(false), снимок 28.09 — серая).
        ui.SetEnabled("BTN_IMPORT_LEADER", canImport);
        ui.SetText("TXT_IMPORT_NAME", setup.ImportedLeader?.Name ?? "");
    }

    public UiRequest? TakeRequest() => _requests.Take();

    /// <summary>«Импорт героя» — окно выбора (просьбой), а не переход графа.</summary>
    public bool Allows(string element, UiScreenState ui) => element != "BTN_IMPORT_LEADER";

    public void Handle(UiEvent e, UiScreenState ui)
    {
        switch (e)
        {
            case UiClicked { Element: Face }:
                ChangeFace(ui);
                break;
            case UiVideoEnded { Element: Face }:
                _changing = false;
                ui.SetVideo(Face, null);   // под роликом — неподвижное новое лицо (0x4e71c7 → 0x4e6530)
                break;
            case UiGroupChanged:
                ReadGroups(ui);
                break;
            case UiEdited { Element: "EDIT_LORD_NAME", Text: var name }:
                context.Setup.LordName = name;
                break;
            case UiClicked { Element: "BTN_IMPORT_LEADER" }:
                var setup = context.Setup;
                _requests.Add(new UiOpen("DLG_IMPORT_LEADER", new ImportLeaderLogic(context,
                    new LeaderPick(setup.Race, setup.Scenario?.SuggestedLevel ?? 1, _importFile, chosen =>
                    {
                        (_importFile, setup.ImportedLeader) = (chosen?.FileName ?? "", chosen?.Leader);
                        ui.SetText("TXT_IMPORT_NAME", chosen?.Leader.Name ?? "");
                    }))));
                break;
        }
    }

    /// <summary>Смена лица (<c>0x4e6945</c>, разбор 28.09, ранг 1): пока идёт ролик прошлой смены (поле <c>[+0x44]</c>), нажатие не
    /// принимается — ни звука, ни смены. Иначе — звук <c>CHNGFACE</c>, лицо +1 по кругу из четырёх и разовый ролик <b>старого</b> лица
    /// <c>FACE_{раса}_{n}</c> поверх неподвижного нового; ролик доиграл — остаётся неподвижное лицо.</summary>
    private void ChangeFace(UiScreenState ui)
    {
        if (_changing) return;
        int old = context.Setup.Face;
        context.Setup.Face = (old + 1) % Faces;
        _changing = true;
        if (WindowSounds.OfClick("DLG_CHOOSE_LORD", Face) is { } sound) _requests.Add(new UiSound(sound));
        ShowFace(ui);
        ui.SetVideo(Face, FaceName(old));
    }

    /// <summary>Неподвижное лицо — кадр интерфейса <c>FACE_{раса}_{n}</c> на кнопке во всех состояниях (<c>0x4e6530</c>/<c>0x4e6557</c>; снимок
    /// 28.09 совпал с кадром точка в точку).</summary>
    private void ShowFace(UiScreenState ui)
    {
        string face = FaceName(context.Setup.Face);
        ui.SetSprites(Face, new UiSprites(face, face, face, face));
    }

    private string FaceName(int face) => $"FACE_{context.Catalog.Race(context.Setup.Race).Code}_{face}";

    private void ReadGroups(UiScreenState ui)
    {
        var setup = context.Setup;
        string lordToggle = Checked(ui, "RAD_LORD");
        setup.LordCategory = LordOf[lordToggle];
        var lord = context.Catalog.Race(setup.Race).Lords.Single(l => l.Category == setup.LordCategory);
        ui.SetText("TXT_LORD_TYPE", context.Text(lord.DescText));

        setup.Difficulty = ui.GroupIndex("RAD_DIFF");
        string diffToggle = Checked(ui, "RAD_DIFF");
        ui.SetText("TXT_DIFF", context.Text(ui.Screen.Elements.First(x => x.Id == diffToggle).TextId ?? ""));
    }

    private static string Checked(UiScreenState ui, string group) =>
        ui.Screen.Elements.First(e => e.Id == group).Members[Math.Max(0, ui.GroupIndex(group))];
}
