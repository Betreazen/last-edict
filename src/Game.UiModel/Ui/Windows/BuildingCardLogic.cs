using LastEdict.Data.Menu;

namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Энциклопедия постройки (<c>DLG_R_C_BUILDING</c>, <c>CEncLayoutBuilding</c> <c>0x57d86a</c>; правая кнопка по значку окна построек
/// <c>0x4ac584</c> и по постройке на картинке столицы <c>0x4afa89</c>): имя <c>X005TA0657</c>, описание как есть, значок <c>IconBld</c>;
/// справа — воин, которого постройка даёт (<c>UPGRADE_B</c>; особый отряд расы с <c>ENROLL_B</c> перебивает), слева — из кого он растёт
/// (<c>PREV_ID</c>, стрелка между). Предыдущего нет — стрелки и левой картинки нет, воин встаёт на место левой (<c>0x57dda3</c>).
/// Закрывается отпусканием кнопки, как карточка воина.
/// </summary>
public sealed class BuildingCardLogic(WindowContext context, string buildingId, IReadOnlyDictionary<string, BuildingText> texts) : IWindowLogic
{
    public const string Screen = "DLG_R_C_BUILDING";
    private readonly UiRequests _requests = new();

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"постройка={buildingId} даёт={To ?? "-"} из={From ?? "-"}";

    /// <summary>Воин постройки; нет — null.</summary>
    public string? To
    {
        get
        {
            var book = context.Session.RequireBook();
            string race = context.Player.RaceId;
            string? special = context.Rules?.EnrollBuildings
                .FirstOrDefault(p => p.Value.Equals(buildingId, StringComparison.OrdinalIgnoreCase)
                                     && context.Rules.UnitRaces.GetValueOrDefault(p.Key, "").Equals(race, StringComparison.OrdinalIgnoreCase)).Key;
            return special ?? book.UpgradeBuilding.FirstOrDefault(p => p.Value.Equals(buildingId, StringComparison.OrdinalIgnoreCase)).Key;
        }
    }

    /// <summary>Из кого растёт воин постройки; нет — null.</summary>
    public string? From => To is { } to && context.Session.RequireBook().Rules.Units.TryGetValue(to, out var type) && type.PrevId.Length > 0
        ? type.PrevId : null;

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        texts.TryGetValue(buildingId, out var text);
        ui.SetText("TXT_BUILDING_NAME", UiTemplate.Fill(context.Text("X005TA0657"), ("NAME", text is null ? buildingId : context.Text(text.NameTxt))));
        ui.SetText("TXT_BUILDING_INFO", text is null ? "" : context.Text(text.DescTxt));
        ui.SetImage("IMG_BUILDING", $"IconBld:{buildingId.ToUpperInvariant()}");
        string? to = To, from = From;
        bool pair = from is not null;
        ui.SetHidden("IMG_ARROW", !pair);
        ui.SetHidden("IMG_TO", !pair);
        ui.SetImage("IMG_FROM", pair ? context.Faces.Small(from!) : to is null ? null : context.Faces.Small(to));
        ui.SetImage("IMG_TO", pair && to is not null ? context.Faces.Small(to) : null);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public void RightRelease(int x, int y, UiScreenState ui) => Close();

    public void Release(int x, int y, UiScreenState ui) => Close();

    private void Close()
    {
        if (_requests.Count == 0) _requests.Add(new UiClose());
    }
}
