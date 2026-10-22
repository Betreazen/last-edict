using D2.Rules.Map;

namespace D2.Data.Ui.Windows;

/// <summary>
/// «Поставить жезл» человеком (<c>BTN_DROP_ROD</c> панели команд, <c>0x48effd</c>; задача <c>CTaskDropRod</c>, конструктор <c>0x4cd876</c>,
/// наследник <c>CTaskStrat</c>; разбор 28.09, ранг 1, если не сказано иное) — чистая часть, без сцены:
/// <list type="bullet">
/// <item>кнопка (<see cref="StartRefusal"/>, <c>0x5d9ac8</c> с флагом 1) — проверки по порядку с окном отказа; всё годно — режим, панель команд
/// сменяется на <c>DLG_DROP_BAG</c> с <see cref="PanelText"/> (<c>0x48d688</c>, режим 2);</item>
/// <item>курсор (<see cref="Cursor"/>, <c>0x4cdb6b</c>): клетка под мышью годна для жезла — <c>DROPRODVAL</c>, иначе <c>DROPRODINV</c>;</item>
/// <item>щелчок (<c>0x4cdbeb</c>): жезл рядом — окно <see cref="RodNear"/>, земля союзника — <see cref="AllyLand"/> (режим остаётся), прочий
/// отказ — молча; годная клетка — путь до неё (как у обычного хода), второй щелчок по ней же — вопрос <see cref="Ask"/> с ценой, если конец
/// пути достижим сегодня (иначе обычный ход, режим снят). «Да» — отряд дальше клетки идёт, затем ставит (сервер <c>0x440cfc</c>); оба
/// ответа снимают режим.</item>
/// </list>
/// </summary>
public static class DropRodLogic
{
    public const string PanelText = "X005TA0284", Ask = "X005TA0265", NeedLeader = "X005TA0506", DeadLeader = "X005TA0543",
        NoMoves = "X005TA0542", InFort = "X005TA0544", NoGold = "X005TA0507", RodNear = "X005TA0356", AllyLand = "X005TA0647";

    public const string CursorValid = "DROPRODVAL", CursorInvalid = "DROPRODINV";

    /// <summary>Отказ кнопки — текст окна (одна «ОК»), или null — режим начинается. Порядок движка: нет лидера или у него нет <c>L_ROD</c> —
    /// <c>X005TA0506</c> (<c>%LEADER%</c> — жезлоносцы расы игрока, <c>0x48f2bf</c>; разделитель не разобран — «, »); лидер пал; очков хода
    /// нет; отряд в крепости; казны не хватает — <c>X005TA0507</c> с <c>%COST%</c> (<c>ROD_COST</c>).</summary>
    public static string? StartRefusal(WindowContext c, string stackId)
    {
        ArgumentNullException.ThrowIfNull(c);
        var session = c.Session;
        var map = session.Map;
        var book = session.RequireBook();
        if (!map.Stacks.TryGetValue(stackId, out var stack)) return null;
        var player = map.Players[stack.Owner];
        if (!map.Units.TryGetValue(stack.LeaderId, out var leader) || !session.LeaderAbilitiesOf(stack).Contains(Rods.LeaderAbilityRod))
            return UiTemplate.Fill(c.Text(NeedLeader), ("LEADER", string.Join(", ", RodLeaders(c, player.RaceId))));
        if (leader.Hp <= 0) return c.Text(DeadLeader);
        if (stack.Move <= 0) return c.Text(NoMoves);
        if (stack.Inside.Length > 0) return c.Text(InFort);
        var price = Treasury.RodPrice(book.Treasury);
        if (Treasury.Affordable(player.Bank, price) < 1) return UiTemplate.Fill(c.Text(NoGold), ("COST", price.Gold));
        return null;
    }

    /// <summary>Имена жезлоносцев расы (герои найма расы с врождённым <c>L_ROD</c>).</summary>
    private static IEnumerable<string> RodLeaders(WindowContext c, string raceId)
    {
        var book = c.Session.RequireBook();
        return (book.RaceLeaders.GetValueOrDefault(raceId) ?? [])
            .Where(t => book.InnateAbilities.GetValueOrDefault(t)?.Contains(Rods.LeaderAbilityRod) == true)
            .Select(c.UnitName);
    }

    /// <summary>Почему в клетку нельзя поставить жезл этим отрядом (<see cref="Rods.PlaceRefusal"/>, как у <c>GameSession.PlaceRod</c>); null — можно.</summary>
    public static string? Refusal(WindowContext c, string stackId, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(c);
        var session = c.Session;
        var map = session.Map;
        var book = session.RequireBook();
        if (!map.Stacks.TryGetValue(stackId, out var stack)) return "нет отряда";
        var player = map.Players[stack.Owner];
        return Rods.PlaceRefusal(map, x, y, player.Bank, book.Treasury, [.. session.LeaderAbilitiesOf(stack)], AllyTerrain(c, player));
    }

    /// <summary>Курсор над клеткой: <c>DROPRODVAL</c> — годна, <c>DROPRODINV</c> — нет (достижимость курсор не смотрит).</summary>
    public static string Cursor(WindowContext c, string stackId, int x, int y) => Refusal(c, stackId, x, y) is null ? CursorValid : CursorInvalid;

    /// <summary>Окно отказа щелчка: жезл ближе двух клеток (код 9) — <see cref="RodNear"/>, земля союзника (код 8) — <see cref="AllyLand"/>; прочие
    /// отказы и годная клетка — null.</summary>
    public static string? ClickMessage(WindowContext c, string stackId, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (Refusal(c, stackId, x, y) is null) return null;
        var map = c.Session.Map;
        if (map.Rods.Values.Any(r => Math.Abs(r.X - x) <= Rods.RodKeepOut && Math.Abs(r.Y - y) <= Rods.RodKeepOut)) return RodNear;
        var player = map.Players[map.Stacks[stackId].Owner];
        return map.Contains(x, y) && AllyTerrain(c, player) is { } ally && ally(map.At(x, y).Terrain) ? AllyLand : null;
    }

    /// <summary>Вопрос перед постановкой: <c>X005TA0265</c> с ценой <c>ROD_COST</c>.</summary>
    public static string Question(WindowContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return UiTemplate.Fill(c.Text(Ask), ("COST", Treasury.RodPrice(c.Session.RequireBook().Treasury).Gold));
    }

    /// <summary>Нужно ли идти (<c>0x440e97</c>): клетка дальше одной по Чебышёву от отряда — сначала ход по пути, постановка — если дошёл.</summary>
    public static bool NeedsWalk(D2.Rules.Map.MapStack stack, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return Math.Max(Math.Abs(stack.X - x), Math.Abs(stack.Y - y)) > 1;
    }

    /// <summary>Второй щелчок по клетке — вопрос с ценой (а не обычный ход)? Рядом — да; дальше — если показанный путь ведёт в эту клетку и
    /// сегодня доходит хотя бы до соседней с ней (предпоследняя точка, <see cref="StackPath.Reachable"/> — номер последней достижимой):
    /// жезл ставится с соседней клетки (<c>0x440cfc</c>), на саму клетку отряду вставать не нужно (ответ пользователя 11.10).</summary>
    public static bool AsksToday(StackPath? path, int x, int y, bool near) =>
        near || path is { Points.Count: > 2 } p && p.Points[^1].X == x && p.Points[^1].Y == y && p.Reachable >= p.Points.Count - 2;

    private static Func<int, bool>? AllyTerrain(WindowContext c, MapPlayer player)
    {
        var book = c.Session.RequireBook();
        var map = c.Session.Map;
        return Diplomacy.PartyOf(player, book.Economy.Races) is { } party
            ? Diplomacy.AllyTerrain(map.Relations, Diplomacy.PlayerParties(map, book.Economy), party, book.Economy, map) : null;
    }
}
