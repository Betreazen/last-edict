using System.Globalization;
using D2.Data.Content;
using D2.Data.Menu;
using D2.Rules.Map;
using D2.Data.Play.Commands;

namespace D2.Data.Ui.Windows;

/// <summary>Имя и описание постройки (<c>Gbuild.NAME_TXT</c>/<c>DESC_TXT</c>).</summary>
public sealed record BuildingText(string NameTxt, string DescTxt);

/// <summary>Вид значка постройки (флаги <c>0x4acc39</c>/<c>0x4acf0d</c>): можно строить, построена, не построена предыдущая,
/// выбран другой путь, выше потолка уровня сценария (значка нет).</summary>
public enum BuildIcon { Can, Built, NeedPrevious, OtherPath, Hidden }

/// <summary>
/// Окно построек столицы (<c>DLG_BUILD_STRUCTURE</c>, <c>CBuildStructInterf</c> <c>0x4aac6b</c>): слева — дерево ветки,
/// выбранной переключателями <c>RAD_BUILDING_TYPE</c> (номер переключателя = номер ветки; панели <c>DLG_{раса}_{ветка}</c> —
/// переходы графа <c>Panel</c>), справа — выбранная постройка (<c>0x4ab5cc</c>). Все панели расы встраиваются в экран
/// (<see cref="Shape"/>), видна одна. Картинки дерева — <c>IMG_%x</c> = постройка <c>G000BB0%03x</c> (значок <c>IconBld</c>,
/// подкраска по виду <c>0x5b6c9d</c>), подписи <c>TXT_%x</c> — имя постройки. «Построить» доступна у значка «можно строить»;
/// нажатие проверяет казну (<c>X005TA0120</c>) и «раз в день» (<c>X005TA0239</c>), затем спрашивает <c>X005TA0240</c>.
/// Открывается на последней выбранной за партию ветке (<c>0x835a8c</c>), в начале — «Воины».
/// Справа у постройки отрядов (<c>0x4ab5cc</c>): кто в ней живёт — воин с <c>UPGRADE_B</c> = постройка (<c>X005TA0384</c>) или, если она
/// нанимает, «Особый отряд» (<c>ENROLL_B</c>, <c>X005TA0677</c>); «Тренируется: предыдущий» (<c>X005TA0383</c>), «В вашей армии: сколько
/// их у игрока в отрядах и гарнизонах» (<c>X005TA0282</c>) и, если строить можно или нужна предыдущая, «Ждёт повышения: сколько из них
/// набрали опыт» (<c>X005TA0381</c>). Выбор значка «можно строить» временно, до следующего щелчка, красит «другим путём» соседние
/// постройки той же ветки со статусом «можно» и всё, что от них растёт (<c>0x4acf0d</c> для самой постройки и её корня) — что станет
/// недоступно после постройки. Правая кнопка по значку — энциклопедия постройки (<see cref="BuildingCardLogic"/>, <c>0x4ac584</c>).
/// </summary>
public sealed class BuildLogic : IWindowLogic
{
    public const string Screen = "DLG_BUILD_STRUCTURE";
    private const string Group = "RAD_BUILDING_TYPE", IconArchive = "IconBld";

    private static readonly UiColor BuiltTint = new(0, 0, 196), PreviousTint = new(255, 0, 0), OtherTint = new(0, 0, 0);

    private readonly WindowContext _c;
    private readonly UiFlow _flow;
    private readonly IReadOnlyList<UiScreen> _screens;
    private readonly IReadOnlyDictionary<string, BuildingText> _texts;
    private readonly UiRequests _requests = new();

    /// <summary>Переключатель ветки → фон панели и постройки (id, картинка, подпись).</summary>
    private readonly Dictionary<string, (string Background, List<(string Building, UiElement Image, string? Caption)> Items)> _branches =
        new(StringComparer.Ordinal);

    public BuildLogic(WindowContext context, UiFlow flow, IReadOnlyList<UiScreen> screens, IReadOnlyDictionary<string, BuildingText> texts)
    {
        _c = context ?? throw new ArgumentNullException(nameof(context));
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
    }

    /// <summary>Имена и описания построек содержимого.</summary>
    public static IReadOnlyDictionary<string, BuildingText> LoadTexts(string rulesDir) => LoadTexts(ContentSource.Open(rulesDir));

    public static IReadOnlyDictionary<string, BuildingText> LoadTexts(ContentSet content) =>
        content.Buildings.ToDictionary(
            b => b.Key.ToUpperInvariant(), b => new BuildingText(b.NameText ?? "", b.DescriptionText ?? ""), StringComparer.OrdinalIgnoreCase);

    /// <summary>Открытая ветка — переключатель группы.</summary>
    public string Branch { get; private set; } = "";

    /// <summary>Выбранная постройка; нет — null.</summary>
    public string? Selected
    {
        get => _selected;
        private set
        {
            _selected = value;
            Shade();
        }
    }

    private string? _selected;

    /// <summary>Постройки, временно показанные «другим путём» от текущего выбора.</summary>
    private readonly HashSet<string> _shaded = new(StringComparer.OrdinalIgnoreCase);

    public string? LastRefusal { get; private set; }

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary =>
        $"ветка={Branch} построек={(_branches.TryGetValue(Branch, out var b) ? b.Items.Count : 0)} выбрано={Selected ?? "-"} " +
        $"построено={_c.Player.Buildings.Count} золото={_c.Player.Bank.Gold} значки={Icons()} отказ={(LastRefusal ?? "-").Replace(' ', '_')} " +
        $"портрет={_faceFrame ?? "-"}";

    /// <summary>Значки открытой ветки для проверки: «номер@x,y:вид» — <c>+</c> можно, <c>b</c> построена, <c>r</c> нет
    /// предыдущей, <c>k</c> другой путь, <c>h</c> скрыт.</summary>
    private string Icons() => !_branches.TryGetValue(Branch, out var branch) ? "-" : string.Join(",", branch.Items.Select(i =>
        $"{i.Image.Id[4..]}@{i.Image.Rect.X + (i.Image.Rect.Width / 2)},{i.Image.Rect.Y + (i.Image.Rect.Height / 2)}:" +
        Icon(i.Building) switch
        {
            BuildIcon.Built => "b", BuildIcon.Can => "+", BuildIcon.NeedPrevious => "r", BuildIcon.OtherPath => "k", _ => "h",
        }));

    /// <summary>Буква расы в именах панелей (<c>CreateDialogName</c> <c>0x4aa98c</c>): Империя H, Орды U, Легионы E, Кланы M,
    /// эльфы F — по <c>Grace.RACE_TYPE</c> 0, 1, 2, 3, 5.</summary>
    private char RaceLetter()
    {
        int type = _c.Session.RequireBook().Economy.Races.TryGetValue(_c.Player.RaceId, out var race) ? race.RaceType : 0;
        return type switch { 1 => 'U', 2 => 'E', 3 => 'M', 5 => 'F', _ => 'H' };
    }

    public UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var elements = screen.Elements.ToList();
        int at = elements.FindIndex(e => e.Id == "IMG_FRAME_DLG") + 1;
        var added = new List<UiElement>();
        string prefix = $"DLG_{RaceLetter()}_";
        var edges = _flow.Screens.FirstOrDefault(s => s.Id == Screen)?.Edges ?? [];
        foreach (var toggle in elements.First(e => e.Id == Group).Members)
        {
            string? target = edges.FirstOrDefault(e => e.Element == toggle && e.Action == UiFlowAction.Panel
                                                       && (e.Target ?? "").StartsWith(prefix, StringComparison.Ordinal))?.Target;
            var panel = target is null ? null : _screens.FirstOrDefault(s => s.Id == target);
            if (panel is null) continue;
            string background = $"IMG_PANEL_{toggle}";
            added.Add(new UiElement { Id = background, Kind = UiKind.Image, Rect = panel.Design with { X = 0, Y = 0 }, Image = panel.Background });
            var items = new List<(string, UiElement, string?)>();
            foreach (var e in panel.Elements)
            {
                added.Add(e);
                // Рамка выбора (0x4ac8a9, 0x4adafe): картинка + 2 по сторонам, вплотную снаружи значка.
                if (e.Kind == UiKind.Image && e.Id.StartsWith("IMG_", StringComparison.Ordinal))
                    added.Add(new UiElement { Id = Frame(e.Id), Kind = UiKind.Image, Rect = new UiRect(e.Rect.X - 1, e.Rect.Y - 1, e.Rect.Width + 2, e.Rect.Height + 2) });
                // IMG_%x ↔ G000BB0%03x (0x79c7d4): номер — шестнадцатеричный, как и в id постройки.
                if (e.Kind != UiKind.Image || !e.Id.StartsWith("IMG_", StringComparison.Ordinal)) continue;
                string number = e.Id[4..];
                string caption = $"TXT_{number}";
                items.Add(($"G000BB0{number.PadLeft(3, '0').ToUpperInvariant()}", e, panel.Elements.Any(x => x.Id == caption) ? caption : null));
            }
            _branches[toggle] = (background, items);
        }
        elements.InsertRange(at, added);
        // Большой воин постройки: место портрета — во всю ширину большой рамки от левого края, подпись правее на ту же величину
        // (правый край на месте; 0x4abb08…0x4abbd7). Порт кладёт это вторым набором элементов рядом с первым.
        if (elements.FindIndex(e => e.Id == Face) is var face and >= 0 && elements.FindIndex(e => e.Id == FaceInfo) is var info and >= 0)
        {
            int wider = BigFaceWidth - elements[face].Rect.Width;
            elements.Insert(info + 1, elements[info] with
            {
                Id = BigFaceInfo, Rect = elements[info].Rect with { X = elements[info].Rect.X + wider, Width = elements[info].Rect.Width - wider },
            });
            elements.Insert(face + 1, elements[face] with { Id = BigFace, Rect = elements[face].Rect with { Width = BigFaceWidth } });
        }
        return screen with { Elements = elements };
    }

    /// <summary>Портрет воина постройки и подпись к нему; у большого воина — <see cref="BigFace"/>/<see cref="BigFaceInfo"/>.</summary>
    private const string Face = "IMG_FACE", FaceInfo = "TXT_UNIT_INFO", BigFace = "IMG_FACE_BIG", BigFaceInfo = "TXT_UNIT_INFO_BIG";

    /// <summary>Ширина большой рамки <c>BORDERUNITLARGE</c> (<see cref="D2.Data.Map.MapCards.LargeBorder"/>, 164×99): движок ставит место
    /// портрета по ширине составной картинки (<c>0x4abb69</c>), а у большого её задаёт рамка.</summary>
    private const int BigFaceWidth = 164;

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var members = ui.Screen.Elements.First(e => e.Id == Group).Members;
        int index = Math.Max(0, members.ToList().IndexOf(_c.Memory.BuildBranch));
        ui.SetGroupIndex(Group, index);
        Branch = members[index];
        ui.SetText("TXT_CITY_DEVELOPMENT", _c.Text("X005TA0094"));
        Refresh(ui);
    }

    public void Resume(UiScreenState ui) => Refresh(ui);

    /// <summary>«Построить» спрашивает сама логика (окно вопроса с ценой).</summary>
    public bool Allows(string element, UiScreenState ui) => element != "BTN_BUILD";

    public void Handle(UiEvent e, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        switch (e)
        {
            case UiGroupChanged { Element: Group } changed:
                Branch = _c.Memory.BuildBranch = changed.Member;
                break;
            case UiClicked { Element: "BTN_BUILD" }:
                Build();
                break;
        }
        Refresh(ui);
    }

    public void Release(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (IconAt(x, y) is not { } building) return;
        Selected = building;
        Refresh(ui);
    }

    public void RightPress(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (IconAt(x, y) is { } building) _requests.Add(new UiOpen(BuildingCardLogic.Screen, new BuildingCardLogic(_c, building, _texts)));
        else if (_faceUnit is { } unit && ui.Screen.Elements.FirstOrDefault(e => e.Id == (_faceBig ? BigFace : Face)) is { } face
                 && x >= face.Rect.X && x < face.Rect.Right && y >= face.Rect.Y && y < face.Rect.Bottom)
            _requests.Add(new UiOpen(UnitCardLogic.Screen, UnitCardLogic.OfType(_c, unit)));
    }

    /// <summary>Воин постройки на портрете справа (<c>IMG_FACE</c>, «Для деталей — правый клик» <c>X005TA0382</c>): правая кнопка по
    /// портрету — его карточка (<see cref="UnitCardLogic.OfType"/>), пока кнопка держится; нет воина — null.</summary>
    private string? _faceUnit;

    /// <summary>Воин постройки большой — портрет в большой рамке (<see cref="BigFace"/>).</summary>
    private bool _faceBig;

    /// <summary>Кадр портрета воина постройки (для <see cref="Summary"/>); нет воина — null.</summary>
    private string? _faceFrame;

    /// <summary>Двойной щелчок по значку — «Построить», если кнопка доступна (<c>0x4add6c</c> → <c>0x4ab0f4</c>).</summary>
    public void DoubleClick(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (IconAt(x, y) is not { } building) return;
        Selected = building;
        Build();
        Refresh(ui);
    }

    private string? IconAt(int x, int y)
    {
        if (!_branches.TryGetValue(Branch, out var branch)) return null;
        foreach (var (building, image, _) in branch.Items)
        {
            var r = image.Rect;
            if (x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom && Icon(building) != BuildIcon.Hidden) return building;
        }
        return null;
    }

    /// <summary>Рамка выбранного значка: картинки <c>BUILDING_HIGHLIGHT</c> в игре нет — движок рисует контур 1 точка цвета
    /// (225,225,0) (<c>CImage2Outline</c> <c>0x5b6a0c</c>, цвет <c>[0x7b2044]</c>).</summary>
    private static string Frame(string image) => $"{image}_SEL";

    private void Build()
    {
        if (Selected is not { } id || Icon(id) != BuildIcon.Can) return;
        var book = _c.Session.RequireBook();
        switch (Status(id))
        {
            case BuildStatus.InsufficientBank:
                _requests.Add(new UiTell(_c.Text("X005TA0120")));
                return;
            case BuildStatus.AlreadyBuiltThisDay:
                _requests.Add(new UiTell(_c.Text("X005TA0239")));
                return;
            case BuildStatus.CanBeBuilt:
                break;
            case var other:
                LastRefusal = other.ToString();
                return;
        }
        string text = UiTemplate.Fill(_c.Text("X005TA0240"), ("NAME", Name(id)), ("PRICE", book.Economy.Buildings[id].Cost.Gold));
        string player = _c.Viewer;
        _requests.Add(new UiAsk(text, () =>
        {
            var done = _c.Session.Run<BuildStatus>(new BuildCommand(player, id));
            LastRefusal = done == BuildStatus.CanBeBuilt ? null : done.ToString();
            // Построено — окно построек закрывается само (обработчик вопроса 0x4ab364: постройка, затем 0x5c8f71 в 0x4ab3d1, звук BUILDING);
            // корпус — 23 постройки человека, все 23 закрыли DLG_BUILD_STRUCTURE (20261016e-av Dungeon: BTN_YES 1198448 → ui_close 1198457).
            _requests.Add(LastRefusal is null ? new UiClose() : new UiTell(LastRefusal));
        }));
    }

    /// <summary>Полная проверка постройки (<c>getBuildingStatus</c> <c>0x5dc9e8</c>, как <see cref="Play.GameSession.Build"/>):
    /// казна, «раз в день», потолок уровня сценария.</summary>
    public BuildStatus Status(string buildingId, bool ignoreCost = false)
    {
        var map = _c.Map;
        var player = _c.Player;
        return Economy.CanBuild(map, player.Id, buildingId, _c.Session.RequireBook().Economy, ignoreCost,
                                maxUnitLevel: map.Limits?.MaxUnit is > 0 and var max ? max : int.MaxValue,
                                builtThisDay: player.ConstructionTurn == map.Turn);
    }

    /// <summary>Вид значка: деньги и «уже строили сегодня» на него не влияют; «выше потолка» и «другой путь» наследуются
    /// вниз по дереву (от предыдущих построек, <c>REQUIRED</c>).</summary>
    public BuildIcon Icon(string buildingId) => _shaded.Contains(buildingId) ? BuildIcon.OtherPath : BaseIcon(buildingId);

    private BuildIcon BaseIcon(string buildingId)
    {
        var buildings = _c.Session.RequireBook().Economy.Buildings;
        var own = Status(buildingId, ignoreCost: true);
        if (own == BuildStatus.AlreadyBuilt) return BuildIcon.Built;
        bool other = own == BuildStatus.SiblingUnitBuilding;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (string id = buildingId; visited.Add(id) && buildings.TryGetValue(id, out var type);)
        {
            var status = Status(id, ignoreCost: true);
            if (status == BuildStatus.ExceedsMaxLevel) return BuildIcon.Hidden;
            if (status == BuildStatus.SiblingUnitBuilding) other = true;
            if (type.RequiredId.Length == 0 || type.RequiredId.Equals(id, StringComparison.OrdinalIgnoreCase)) break;
            id = type.RequiredId;
        }
        if (other) return BuildIcon.OtherPath;
        return own == BuildStatus.NoRequiredBuilding ? BuildIcon.NeedPrevious : BuildIcon.Can;
    }

    private string Name(string id) => _texts.TryGetValue(id, out var t) ? _c.Text(t.NameTxt) : id;

    /// <summary>Затемнение соседей выбранной (<c>0x4acf0d</c>): у самой постройки и у её корня — прочие постройки ветки с тем же
    /// предком и статусом «можно» вместе с поддеревьями.</summary>
    private void Shade()
    {
        _shaded.Clear();
        if (_selected is not { } s || BaseIcon(s) != BuildIcon.Can) return;
        var buildings = _c.Session.RequireBook().Economy.Buildings;
        if (!buildings.TryGetValue(s, out var selected)) return;
        string root = s;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { s };
        while (buildings.TryGetValue(root, out var t) && t.RequiredId.Length > 0 && buildings.ContainsKey(t.RequiredId) && seen.Add(t.RequiredId))
            root = t.RequiredId;
        foreach (string node in root.Equals(s, StringComparison.OrdinalIgnoreCase) ? new[] { s } : new[] { s, root })
        {
            if (!buildings.TryGetValue(node, out var n)) continue;
            foreach (var sibling in buildings.Values.Where(b => b.Branch == selected.Branch && b.Category == selected.Category
                                                                && !b.Id.Equals(node, StringComparison.OrdinalIgnoreCase)
                                                                && b.RequiredId.Equals(n.RequiredId, StringComparison.OrdinalIgnoreCase)
                                                                && BaseIcon(b.Id) == BuildIcon.Can))
                Subtree(sibling.Id);
        }

        void Subtree(string id)
        {
            if (!_shaded.Add(id)) return;
            foreach (var child in buildings.Values.Where(b => b.RequiredId.Equals(id, StringComparison.OrdinalIgnoreCase))) Subtree(child.Id);
        }
    }

    /// <summary>Строки воина постройки: живущий в ней, тренирующийся, сколько их в армии и ждут повышения.</summary>
    private void UnitLines(UiScreenState ui, string id)
    {
        var book = _c.Session.RequireBook();
        string? special = _c.Rules?.EnrollBuildings.FirstOrDefault(p => p.Value.Equals(id, StringComparison.OrdinalIgnoreCase)).Key;
        string? unit = special ?? book.UpgradeBuilding.FirstOrDefault(p => p.Value.Equals(id, StringComparison.OrdinalIgnoreCase)).Key;
        if (unit is null) return;
        _faceUnit = unit;
        // Портрет в рамке, как у карточки отряда (0x4abaa1 → 0x5b0bdd лицо, getSizeSmall типа 0x4abaa8: малый — 0x5b680b BORDERUNITSMALL,
        // большой — 0x5b6870 BORDERUNITLARGE).
        _faceBig = book.Rules.Units.TryGetValue(unit, out var sized) && !sized.SizeSmall;
        _faceFrame = UiFrameRef.Layered(_faceBig ? D2.Data.Map.MapCards.LargeBorder : D2.Data.Map.MapCards.SmallBorder, _c.Faces.Small(unit));
        ui.SetImage(_faceBig ? BigFace : Face, _faceFrame, UiImageAlign.Center);
        ui.SetText("TXT_UPGRADED", $"{_c.Text(special is null ? "X005TA0384" : "X005TA0677")} {_c.UnitName(unit)}");
        ui.SetText(_faceBig ? BigFaceInfo : FaceInfo, _c.Text("X005TA0382"));
        if (!book.Rules.Units.TryGetValue(unit, out var type) || type.PrevId.Length == 0) return;
        string prev = type.PrevId;
        ui.SetText("TXT_UPGRADING", UiTemplate.Fill(_c.Text("X005TA0383"), ("NAME", _c.UnitName(prev))));
        var map = _c.Map;
        var ours = map.Stacks.Values.Where(s => s.Owner.Equals(_c.Viewer, StringComparison.OrdinalIgnoreCase)).SelectMany(s => s.UnitIds)
            .Concat(map.Sites.Where(s => s.Owner.Equals(_c.Viewer, StringComparison.OrdinalIgnoreCase)).SelectMany(s => s.Guards))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(u => map.Units.GetValueOrDefault(u)).OfType<MapUnit>()
            .Where(u => u.TypeId.Equals(prev, StringComparison.OrdinalIgnoreCase)).ToList();
        string info = UiTemplate.Fill(_c.Text("X005TA0282"), ("NUMBER", ours.Count));
        if (Icon(id) is BuildIcon.Can or BuildIcon.NeedPrevious)
        {
            int urgent = ours.Count(u => book.XpNextOf(u.TypeId, u.Level) is var next and > 0 && u.Xp >= next - 1);
            info += @"\n" + UiTemplate.Fill(_c.Text("X005TA0381"), ("URGENT", urgent));
        }
        ui.SetText("TXT_UPGRADE_INFO", info);
    }

    private void Refresh(UiScreenState ui)
    {
        var player = _c.Player;
        var book = _c.Session.RequireBook();
        ui.SetText("TXT_GOLD", player.Bank.Gold.ToString(CultureInfo.InvariantCulture));
        foreach (var (toggle, branch) in _branches)
        {
            bool shown = toggle == Branch;
            ui.SetHidden(branch.Background, !shown);
            foreach (var (building, image, caption) in branch.Items)
            {
                var icon = Icon(building);
                bool visible = shown && icon != BuildIcon.Hidden;
                ui.SetHidden(image.Id, !visible);
                string frame = $"{IconArchive}:{building}";
                ui.SetImage(image.Id, icon switch
                {
                    BuildIcon.Built => UiFrameRef.Tinted(frame, BuiltTint),
                    BuildIcon.NeedPrevious => UiFrameRef.Tinted(frame, PreviousTint),
                    BuildIcon.OtherPath => UiFrameRef.Tinted(frame, OtherTint),
                    _ => frame,
                });
                var r = image.Rect;
                ui.SetImage(Frame(image.Id), visible && building.Equals(Selected, StringComparison.OrdinalIgnoreCase)
                    ? $"Outline:{r.Width + 2}x{r.Height + 2}#E1E100" : null);
                if (caption is null) continue;
                ui.SetHidden(caption, !visible);
                ui.SetText(caption, Name(building));
            }
        }
        ui.SetEnabled("BTN_BUILD", Selected is { } s && Icon(s) == BuildIcon.Can);
        foreach (string t in (string[])["TXT_INFO", "TXT_COST", "TXT_STATUS", FaceInfo, BigFaceInfo, "TXT_UPGRADED", "TXT_UPGRADING", "TXT_UPGRADE_INFO"])
            ui.SetText(t, "");
        ui.SetImage(Face, null);
        ui.SetImage(BigFace, null);
        _faceUnit = null;
        _faceBig = false;
        _faceFrame = null;
        if (Selected is not { } id || !book.Economy.Buildings.TryGetValue(id, out var type)) return;
        string cost = UiTemplate.Fill(_c.Text("X005TA0055"), ("QTY", type.Cost.Gold));
        ui.SetText("TXT_COST", UiTemplate.Fill(_c.Text("X005TA0449"), ("NAME", Name(id)), ("COST", cost)));
        ui.SetText("TXT_INFO", _texts.TryGetValue(id, out var text) ? _c.Text(text.DescTxt) : "");
        ui.SetText("TXT_STATUS", _c.Text(Icon(id) switch
        {
            BuildIcon.Built => "X005TA0308", BuildIcon.NeedPrevious => "X005TA0310", BuildIcon.OtherPath => "X005TA0312", _ => "X005TA0307",
        }));
        UnitLines(ui, id);
    }
}
