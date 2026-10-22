using LastEdict.Data.Menu;

namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Панель столицы <c>DLG_CAPITAL</c> (<c>CCapitalInterf</c>) — на месте панели команд, пока на месте карты картинка
/// столицы (ответ пользователя 25.09). Портрет и имя героя-гостя — как у панели команд (<c>X005TA0399</c>); кнопки
/// ведут по графу экранов: «Окно армии» → <c>DLG_CITY_STACK</c>, «Построить» → <c>DLG_BUILD_STRUCTURE</c>, «Назад» →
/// <c>DLG_STRATEGIC</c>, «Исследовать» → <c>DLG_RESEARCH_SPELL</c>; «Вывести героя» выводит гостя (хозяин закрывает столицу).
/// </summary>
public sealed class CapitalPanelLogic(WindowContext context, string capitalId) : IScreenLogic
{
    public const string Screen = "DLG_CAPITAL";
    private const string NameLine = "X005TA0399";

    public string CapitalId => capitalId;

    public void Open(UiScreenState ui) => Refresh(ui);

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    /// <summary>Исследование открывает хозяин сам (проверка постройки магии, <c>X005TA0405</c>).</summary>
    public bool Allows(string element, UiScreenState ui) => true;

    /// <summary>Гость столицы; нет — пусто. «Вывести героя» доступна, только когда он есть (<c>0x4aecd6</c>).</summary>
    public string VisitorId => context.Map.Sites.First(s => s.Id.Equals(capitalId, StringComparison.OrdinalIgnoreCase)).StackId;

    public void Refresh(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetEnabled("BTN_EXIT_CAPITAL", VisitorId.Length > 0);
        var site = context.Map.Sites.First(s => s.Id.Equals(capitalId, StringComparison.OrdinalIgnoreCase));
        if (site.StackId.Length == 0 || !context.Map.Stacks.TryGetValue(site.StackId, out var stack)
            || !context.Map.Units.TryGetValue(stack.LeaderId, out var leader))
        {
            ui.SetImage("IMG_LEADER", null);
            ui.SetText("TXT_LEADER_NAME", "");
            ui.SetText("TXT_LEADER_STATS", "");
            return;
        }
        var look = context.Session.Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase));
        string name = look is { LeaderName.Length: > 0 } ? look.LeaderName : context.UnitName(leader.TypeId);
        // Портрет гостя в панели столицы отражён (снимки 28.09 CCapitalInterf: 2,4 против 35,6 без отражения); картинка — как у панели
        // команд (0x486a46 → 0x5b1e6a: FACES большого юнита).
        ui.SetImage("IMG_LEADER", UiFrameRef.Mirrored(CapitalGuestFace.Of(context, leader)));
        ui.SetText("TXT_LEADER_NAME", UiTemplate.Fill(context.Text(NameLine), ("NAME", name)));
        // Очки хода — та же строка, что у панели команд (StrategicPanelLogic, файл карты — не трогаем): X005TA0396, красным при нуле;
        // на снимке столицы «Скорость: 20 / 20».
        int max = context.Session.DayAllowance(stack);
        ui.SetText("TXT_LEADER_STATS", context.Text("X005TA0396")
            .Replace("%COLOR%", context.Text(stack.Move == 0 ? "X005TA0398" : "X005TA0397"), StringComparison.Ordinal)
            .Replace("%MOVE1%", stack.Move.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%MOVE2%", max.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }
}

/// <summary>Лицо гостя панели столицы по состоянию лидера — как у панели команд (<see cref="LastEdict.Data.Map.PanelFace"/>): <c>0x486a6f</c> —
/// жив (<c>0x5e98ad</c>) — доля здоровья <c>hp·100/max</c> (<c>0x486abc</c>), раненый — нижняя доля красным; павший — состояние 10
/// (<c>0x486ac7</c>), маска <c>MASKDEADS</c> (у широкого <c>FACE</c> большого без своего <c>FACES</c> — <c>MASKDEADL</c>). Значков у
/// гостя столицы движок не кладёт (картинка ставится сразу, флаги 9, <c>0x486ada</c>).</summary>
public static class CapitalGuestFace
{
    private static readonly UiColor Wounded = new(255, 0, 0);

    public static string Of(WindowContext context, LastEdict.Rules.Map.MapUnit leader)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(leader);
        string name = context.Faces.PanelName(leader.TypeId);
        string face = context.Faces.Panel(leader.TypeId);
        if (leader.Hp <= 0)
        {
            bool wide = !name.EndsWith("FACES", StringComparison.OrdinalIgnoreCase) && context.Session.Book is { } book
                        && book.Rules.Units.TryGetValue(leader.TypeId, out var type) && !type.SizeSmall;
            return UiFrameRef.Masked(face, wide ? "MASKDEADL" : "MASKDEADS");
        }
        int max = context.Session.Book is null ? 0 : context.Session.HpMax(leader);
        return max <= 0 || leader.Hp >= max ? face : UiFrameRef.Tinted(face, Wounded, leader.Hp * 100 / max);
    }
}

/// <summary>Постройка под мышью на картинке столицы (<c>DLG_BUILDING</c> на месте верхней правой панели <c>DLG_ISO_PAL</c>,
/// <c>0x4af468</c>): значок <c>IconBld</c> и имя; «Отчёт», «Настройки», «Задания» выключены; мышь ушла — панель прежняя
/// (<c>0x488f8e</c>).</summary>
public sealed class BuildingPanelLogic(WindowContext context, string buildingId) : IScreenLogic
{
    public const string Screen = "DLG_BUILDING";

    public string BuildingId => buildingId;

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetImage("IMG_BUILDING", $"IconBld:{ArtNames.Of(buildingId)}");
        ui.SetText("TXT_BUILDING", context.Rules?.BuildingNames.TryGetValue(buildingId, out var name) == true ? context.Text(name) : buildingId);
        foreach (string button in (string[])["BTN_REPORT", "BTN_OPTIONS", "BTN_QUESTLOG"]) ui.SetEnabled(button, false);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public bool Allows(string element, UiScreenState ui) => false;
}
