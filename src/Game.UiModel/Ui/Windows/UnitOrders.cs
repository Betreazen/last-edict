using D2.Data.Menu;
using D2.Data.Play.Commands;

namespace D2.Data.Ui.Windows;

/// <summary>
/// Приказы воину инструментом окна (щелчок по воину, после одного воина инструмент отжимается): отпуск (<c>TOG_DISMISS</c>,
/// <c>CDismissUnit</c> <c>0x4f8662</c>) и «Остановить продвижение» (<c>TOG_DYNLEVEL</c>, <c>CDynLevelUnit</c> <c>0x4f8780</c>). Общее окна
/// армии, окна отряда и наёмников.
/// </summary>
public static class UnitOrders
{
    /// <summary>
    /// Отпуск: герой, нужный заданию, — <c>X005TA0883</c> (<see cref="Play.GameSession.NeededByQuest"/>, до вопроса); страж —
    /// <c>X005TA0450</c>; прочие отказы (<see cref="Play.GameSession.DismissCheck"/>) молча, как у движка; можно — вопрос
    /// <c>X002TA0033</c> «Расформировать …?», «Да» — <see cref="Play.GameSession.DismissUnit"/>.
    /// </summary>
    /// <returns>Отказ до вопроса; null — задан вопрос (итог — в <paramref name="done"/>).</returns>
    public static string? Dismiss(WindowContext c, UiRequests requests, string groupId, string unitId, Action<string?> done)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(requests);
        if (c.Session.NeededByQuest(groupId, unitId))
        {
            requests.Add(new UiTell(c.Text("X005TA0883")));
            return "герой нужен заданию";
        }
        int check = c.Session.DismissCheck(groupId, unitId);
        if (check == 2) requests.Add(new UiTell(c.Text("X005TA0450")));
        if (check != 0) return $"код {check}";
        requests.Add(new UiAsk(UiTemplate.Fill(c.Text("X002TA0033"), ("UNITNAME", Name(c, unitId))), () =>
        {
            int result = c.Session.Run<int>(new DismissUnitCommand(groupId, unitId));
            done(result == 0 ? null : $"код {result}");
        }));
        return null;
    }

    /// <summary>
    /// «Остановить продвижение» (<c>0x5de8ba</c>): уже остановлено — <c>X005TA0939</c>, герой — <c>X005TA0936</c>, страж —
    /// <c>X005TA0937</c>; можно — вопрос <c>X005TA0938</c>, «Да» — <see cref="Play.GameSession.ForceDynLevel"/>.
    /// </summary>
    /// <returns>Отказ до вопроса; null — задан вопрос.</returns>
    public static string? StopGrowth(WindowContext c, UiRequests requests, string unitId, Action<string?> done)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(requests);
        int check = c.Session.DynLevelCheck(unitId);
        string? text = check switch { 0x13 => "X005TA0939", 0x12 => "X005TA0936", 0x11 => "X005TA0937", _ => null };
        if (text is not null) requests.Add(new UiTell(c.Text(text)));
        if (check != 0) return $"код {check}";
        requests.Add(new UiAsk(UiTemplate.Fill(c.Text("X005TA0938"), ("UNITNAME", Name(c, unitId))), () =>
        {
            int result = c.Session.Run<int>(new ForceDynLevelCommand(unitId));
            done(result == 0 ? null : $"код {result}");
        }));
        return null;
    }

    private static string Name(WindowContext c, string unitId) =>
        c.Map.Units.TryGetValue(unitId, out var unit) ? c.UnitName(unit.TypeId) : unitId;
}
